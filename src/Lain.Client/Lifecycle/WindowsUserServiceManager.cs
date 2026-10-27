using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Lain.Client.Lifecycle;

/// <summary>Per-user Task Scheduler registration; the Host itself has the GUI subsystem.</summary>
public sealed class WindowsUserServiceManager : IUserServiceManager
{
    private readonly InstallationMetadata installation;
    private readonly IWindowsTaskScheduler scheduler;
    private readonly Func<CancellationToken, Task> shutdown;

    public WindowsUserServiceManager(InstallationMetadata installation, ILifecycleProcessRunner? runner = null)
        : this(installation, new WindowsTaskScheduler(), ShutdownLocalHostAsync) { }

    public WindowsUserServiceManager(InstallationMetadata installation, IWindowsTaskScheduler scheduler,
        Func<CancellationToken, Task> shutdown)
    { this.installation = installation; this.scheduler = scheduler; this.shutdown = shutdown; }

    public Task<UserServiceStatus> InspectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            NativeServiceFiles.Validate(installation);
            if (!File.Exists(installation.RegistrationPath))
                return Task.FromResult(new UserServiceStatus("task-scheduler", "missing", null, "stopped", "The Lain scheduled-task definition is missing. Run setup again."));
            NativeServiceFiles.ValidateOwnedFile(installation, installation.RegistrationPath);
            var expected = WindowsTaskContract.Read(File.ReadAllText(installation.RegistrationPath), installation);
            var task = scheduler.Inspect(installation.ServiceName);
            if (task is null)
                return Task.FromResult(new UserServiceStatus("task-scheduler", "missing", expected.LoginAutostart, "stopped", "The Lain scheduled task is missing. Run setup again."));
            var actual = WindowsTaskContract.Read(task.Xml, installation);
            if (actual != expected || expected.LoginAutostart != NativeServiceFiles.ReadLoginPreference(installation))
                return Task.FromResult(new UserServiceStatus("task-scheduler", "modified", actual.LoginAutostart, "unknown", "The Lain scheduled task has been customized. Resolve it before changing the service."));
            var state = task.State switch { 2 or 4 => "running", 3 => "stopped", _ => "unknown" };
            return Task.FromResult(new UserServiceStatus("task-scheduler", "registered", actual.LoginAutostart, state));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
        { return Task.FromResult(new UserServiceStatus("task-scheduler", "modified", null, "unknown", error.Message)); }
        catch (Exception error) when (error is COMException or PlatformNotSupportedException)
        { return Task.FromResult(new UserServiceStatus("task-scheduler", "unavailable", null, "unknown", "The current user's Task Scheduler is unavailable.")); }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var status = await RequireRegisteredAsync(ct).ConfigureAwait(false);
        if (status.State == "running") return;
        try { scheduler.Run(installation.ServiceName); }
        catch (COMException error) { throw new IOException("Task Scheduler could not start the Lain Host.", error); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var status = await RequireRegisteredAsync(ct).ConfigureAwait(false);
        if (status.State == "stopped") return;
        try { await shutdown(ct).ConfigureAwait(false); }
        catch (Exception error) when (error is HttpRequestException or IOException or LainApiException or TaskCanceledException)
        { throw new IOException("The Lain Host could not acknowledge graceful shutdown. No process was terminated.", error); }
        var deadline = DateTime.UtcNow.AddSeconds(35);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var current = await RequireRegisteredAsync(ct).ConfigureAwait(false);
            if (current.State == "stopped") return;
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        throw new IOException("The Lain Host did not finish graceful shutdown within 35 seconds. No process was terminated.");
    }

    public async Task RestartAsync(CancellationToken ct = default)
    { await StopAsync(ct).ConfigureAwait(false); await StartAsync(ct).ConfigureAwait(false); }

    public async Task SetLoginAutostartAsync(bool enabled, CancellationToken ct = default)
    {
        await RequireRegisteredAsync(ct).ConfigureAwait(false);
        var document = XDocument.Load(installation.RegistrationPath);
        XNamespace ns = WindowsTaskContract.Namespace;
        document.Root!.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!.Element(ns + "Enabled")!.Value = enabled ? "true" : "false";
        var xml = document.ToString();
        var changed = new Dictionary<string, byte[]> { [installation.RegistrationPath] = System.Text.Encoding.UTF8.GetBytes(xml) };
        // Shared lifecycle holds the operation gate. Do not stop a running task when
        // changing only its future logon trigger. Incomplete updates stay journaled.
        NativeServiceFiles.ChangeLoginPreference(installation, enabled, () =>
        {
            scheduler.Register(installation.ServiceName, xml);
            var registered = scheduler.Inspect(installation.ServiceName)
                ?? throw new IOException("The updated Lain task could not be verified.");
            if (WindowsTaskContract.Read(registered.Xml, installation) != WindowsTaskContract.Read(xml, installation))
                throw new IOException("The updated Lain task does not match its owned definition.");
            changed[installation.RegistrationPath] = System.Text.Encoding.UTF8.GetBytes(XDocument.Parse(registered.Xml).ToString());
            NativeServiceFiles.WriteAtomic(installation.RegistrationPath, changed[installation.RegistrationPath]);
        }, changed, []);
    }

    private async Task<UserServiceStatus> RequireRegisteredAsync(CancellationToken ct)
    {
        var status = await InspectAsync(ct).ConfigureAwait(false);
        if (status.RegistrationState != "registered") throw new IOException(status.Error ?? "The Lain scheduled task is unavailable.");
        return status;
    }
    private static async Task ShutdownLocalHostAsync(CancellationToken ct)
    {
        using var client = LainApiClient.CreateLocal();
        await client.ShutdownHostAsync(ct).ConfigureAwait(false);
    }
}

public sealed record WindowsScheduledTask(string Xml, int State);
public interface IWindowsTaskScheduler
{
    WindowsScheduledTask? Inspect(string taskName);
    void Run(string taskName);
    void Register(string taskName, string xml);
}

/// <summary>Native COM calls take data arguments, never command or PowerShell text.</summary>
internal sealed class WindowsTaskScheduler : IWindowsTaskScheduler
{
    public WindowsScheduledTask? Inspect(string taskName) => WithFolder<WindowsScheduledTask?>(folder =>
    {
        object? task = null;
        try
        {
            try { task = ((dynamic)folder).GetTask(taskName); }
            catch (COMException error) when ((uint)error.HResult is 0x80070002 or 0x8004130F) { return null; }
            return new WindowsScheduledTask((string)((dynamic)task).Xml, (int)((dynamic)task).State);
        }
        finally { Release(task); }
    });
    public void Run(string taskName) => WithFolder<object?>(folder =>
    {
        object? task = null; object? instance = null;
        try { task = ((dynamic)folder).GetTask(taskName); instance = ((dynamic)task).Run(null); return null; }
        finally { Release(instance); Release(task); }
    });
    public void Register(string taskName, string xml) => WithFolder<object?>(folder =>
    {
        object? task = null;
        try
        {
            // TASK_CREATE_OR_UPDATE=6, TASK_LOGON_INTERACTIVE_TOKEN=3. No password,
            // elevation, background account or LocalSystem registration is requested.
            task = ((dynamic)folder).RegisterTask(taskName, xml, 6, null, null, 3, null);
            return null;
        }
        finally { Release(task); }
    });
    private static T WithFolder<T>(Func<object, T> operation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        object? service = null; object? folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!);
            ((dynamic)service!).Connect(); folder = ((dynamic)service).GetFolder("\\");
            return operation(folder!);
        }
        finally { Release(folder); Release(service); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}

internal sealed record WindowsTaskContract(string UserId, bool LoginAutostart, string CanonicalXml)
{
    internal const string Namespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    internal static WindowsTaskContract Read(string xml, InstallationMetadata installation)
    {
        XNamespace ns = Namespace;
        var root = XDocument.Parse(xml).Root ?? throw new IOException("Invalid Lain scheduled-task XML.");
        var actions = root.Element(ns + "Actions")?.Elements().ToArray() ?? [];
        var principals = root.Element(ns + "Principals")?.Elements().ToArray() ?? [];
        var triggers = root.Element(ns + "Triggers")?.Elements().ToArray() ?? [];
        var settings = root.Element(ns + "Settings");
        var user = principals.SingleOrDefault()?.Element(ns + "UserId")?.Value;
        string? Setting(string name) => settings?.Element(ns + name)?.Value;
        if (root.Name != ns + "Task" || actions.Length != 1 || actions[0].Name != ns + "Exec" ||
            actions[0].Element(ns + "Command")?.Value != installation.HostPath ||
            actions[0].Element(ns + "Arguments")?.Value != Arguments(installation) ||
            actions[0].Element(ns + "WorkingDirectory")?.Value != Path.Combine(installation.InstallationRoot, "data") ||
            principals.Length != 1 || principals[0].Element(ns + "LogonType")?.Value != "InteractiveToken" ||
            principals[0].Element(ns + "RunLevel")?.Value != "LeastPrivilege" ||
            string.IsNullOrWhiteSpace(user) || !user.StartsWith("S-1-", StringComparison.Ordinal) ||
            triggers.Length != 1 || triggers[0].Name != ns + "LogonTrigger" || triggers[0].Element(ns + "UserId")?.Value != user ||
            triggers[0].Element(ns + "Enabled")?.Value is not ("true" or "false") ||
            Setting("Enabled") != "true" || Setting("AllowStartOnDemand") != "true" || Setting("AllowHardTerminate") != "false" ||
            Setting("MultipleInstancesPolicy") != "IgnoreNew" || Setting("ExecutionTimeLimit") != "PT0S" ||
            Setting("DisallowStartIfOnBatteries") != "false" || Setting("StopIfGoingOnBatteries") != "false" ||
            settings?.Element(ns + "RestartOnFailure")?.Element(ns + "Interval")?.Value != "PT1M" ||
            settings?.Element(ns + "RestartOnFailure")?.Element(ns + "Count")?.Value != "3")
            throw new IOException("The Lain scheduled task does not match this installation's user-service contract.");
        root.Element(ns + "RegistrationInfo")?.Elements(ns + "Date").Remove();
        static object Shape(XElement node) => new object[] { node.Name.ToString(),
            node.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString(), StringComparer.Ordinal)
                .Select(a => new[] { a.Name.ToString(), a.Value }).ToArray(),
            node.HasElements ? "" : node.Value.Trim(), node.Elements().Select(Shape).ToArray() };
        return new(user, triggers[0].Element(ns + "Enabled")!.Value == "true", System.Text.Json.JsonSerializer.Serialize(Shape(root)));
    }
    internal static string Arguments(InstallationMetadata value)
    {
        var values = new List<string> { "--lain-home", Path.Combine(value.InstallationRoot, "data") };
        if (value.ProxyPath is not null) { values.Add("--lain-proxy-path"); values.Add(value.ProxyPath); }
        return string.Join(" ", values.Select(Quote));
    }
    // CommandLineToArgvW quoting. There is no cmd.exe expansion of %, !, &, or |.
    private static string Quote(string value)
    {
        var result = new System.Text.StringBuilder("\""); var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
            else { result.Append('\\', slashes); result.Append(c); }
            slashes = 0;
        }
        result.Append('\\', slashes * 2); return result.Append('"').ToString();
    }
}
