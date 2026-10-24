using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Host.Accounts.CliConnections;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Cloud;

public sealed record GoogleGcloudAccess(string Account, string AccessToken, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "GoogleGcloudAccess [redacted]";
}
public interface IGoogleGcloudProcess
{
    bool IsAvailable { get; }
    Task<GoogleGcloudAccess> LoginAsync(string profile, string? account, Action<string> browser, CancellationToken ct);
    Task<GoogleGcloudAccess> ReadAccessAsync(string profile, string account, CancellationToken ct);
}

/// <summary>The official SDK owns OAuth, PKCE, callback and refresh. No SDK client identity is copied into lain.</summary>
public sealed class GoogleGcloudProcess : IGoogleGcloudProcess
{
    public const string QualifiedVersion = "584.0.0";
    public const string InstallationRoot = "/opt/google-cloud-cli";
    private readonly TimeProvider clock;
    public GoogleGcloudProcess(TimeProvider? clock = null) => this.clock = clock ?? TimeProvider.System;
    public bool IsAvailable => QualifiedInstallation();
    private static readonly Dictionary<string, string> Sources = new()
    {
        ["lib/googlecloudsdk/core/config.py"] = "fc06f07045d0a8054355f904fa4aac12151a1d1e08c6c366620b405c4ee98f52",
        ["lib/googlecloudsdk/core/credentials/gce_cache.py"] = "1ff225abebe9a546320e140f6a317beda3ae612d6387b2f3ce956a506821decb",
        ["lib/googlecloudsdk/core/metrics.py"] = "045019bf8cd4739e12f106529e87760a2a4d192920b1331ba968db00e8448724",
        ["bin/gcloud"] = "8628b9f971d07c5e52fd2c6d469ee4a0940c75eb51820d628c3dd068981bd260",
        ["lib/gcloud.py"] = "d223bce54ff2e1441268e73b06eec4830032dd7b4b17b8fa9e2638d4f6d8d39d",
        ["lib/surface/auth/login.py"] = "c18aa067c11869f2ecb57427dfe50c4cf7ca27164d4e2d3cd219adc08c0cb999",
        ["lib/surface/config/config_helper.py"] = "68bc5cc5d85d7233f4c76e5c564191f70555c5c7d0e4eea2727ed985888309f9",
        ["lib/googlecloudsdk/command_lib/config/config_helper.py"] = "453aeb0d2007120d7cb7008c355218857717430f94c3c8250d6016a89957bc24",
        ["lib/googlecloudsdk/api_lib/auth/util.py"] = "3aabd710e4e5733ddc935ee23746b20b62f0516aaf610c6cb811570abd4cb069",
        ["lib/googlecloudsdk/core/credentials/flow.py"] = "2969c14764c8f0ec8bc1b4862e086f557815f6efbdfb5a4552954ad5b3152de6",
        ["lib/googlecloudsdk/command_lib/util/check_browser.py"] = "e0fcfe555c74736e1474a33a719eaf52060a8709431acb0ac402c54e4be50b28"
    };
    public static string? RegisteredClientId
    {
        get
        {
            if (!QualifiedInstallation()) return null;
            var line = File.ReadLines(Path.Combine(InstallationRoot, "lib/googlecloudsdk/core/config.py"))
                .Single(value => value.StartsWith("CLOUDSDK_CLIENT_ID = '", StringComparison.Ordinal));
            return line.Split('\'')[1]; // Validation only; lain never constructs an OAuth request with this ID.
        }
    }
    private static bool QualifiedInstallation()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64) return false;
        try
        {
            foreach (var (relative, expected) in Sources)
            {
                var path = Path.Combine(InstallationRoot, relative);
                CliPrivatePaths.RefuseLinks(path);
                if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024 ||
                    (File.GetUnixFileMode(path) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0) return false;
                if (!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return (File.GetUnixFileMode(Path.Combine(InstallationRoot, "bin/gcloud")) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0
                && File.ReadAllText(Path.Combine(InstallationRoot, "VERSION")).Trim() == QualifiedVersion;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
    private void RequireInstallation() { if (!IsAvailable) throw new InvalidDataException("The source-qualified Google Cloud CLI installation is unavailable or changed. Update its qualification before reconnecting."); }
    public async Task<GoogleGcloudAccess> LoginAsync(string profile, string? account, Action<string> browser, CancellationToken ct)
    {
        RequireInstallation();
        var arguments = new List<string> { "auth", "login", "--launch-browser", "--brief", "--force", "--quiet" };
        if (account is not null) { ValidateAccount(account); arguments.Add(account); }
        using var output = await GoogleGcloudProcessTransport.RunAsync(Path.Combine(InstallationRoot, "bin/gcloud"), arguments, profile, browser, TimeSpan.FromMinutes(10), ct);
        if (output.ExitCode != 0) throw new InvalidDataException("Google Cloud CLI login did not complete. Reconnect to try again.");
        return await ReadAsync(profile, account, ct);
    }
    public Task<GoogleGcloudAccess> ReadAccessAsync(string profile, string account, CancellationToken ct)
    { ValidateAccount(account); return ReadAsync(profile, account, ct); }
    private async Task<GoogleGcloudAccess> ReadAsync(string profile, string? account, CancellationToken ct)
    {
        RequireInstallation();
        var arguments = new List<string> { "config", "config-helper", "--format=json", "--force-auth-refresh", "--quiet" };
        if (account is not null) arguments.Add("--account=" + account);
        using var output = await GoogleGcloudProcessTransport.RunAsync(Path.Combine(InstallationRoot, "bin/gcloud"), arguments, profile, null, TimeSpan.FromSeconds(20), ct);
        if (output.ExitCode != 0) throw new InvalidDataException("Google Cloud CLI could not renew this isolated connection. Reconnect if access was revoked.");
        return ParseAccess(output.Stdout.Span, clock.GetUtcNow(), account);
    }
    public static GoogleGcloudAccess ParseAccess(ReadOnlySpan<byte> json, DateTimeOffset now, string? expectedAccount = null)
    {
        using var document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        var credential = root.GetProperty("credential");
        var account = root.GetProperty("configuration").GetProperty("properties").GetProperty("core").GetProperty("account").GetString();
        ValidateAccount(account);
        var token = credential.GetProperty("access_token").GetString();
        var expiry = credential.GetProperty("token_expiry").GetString();
        if (token is not { Length: > 0 and <= 32768 } || token.Any(char.IsWhiteSpace) || token.Any(char.IsControl)
            || !DateTimeOffset.TryParseExact(expiry, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires)
            || expires <= now.AddSeconds(30) || expires > now.AddHours(2) || expectedAccount is not null && !string.Equals(account, expectedAccount, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Google Cloud CLI returned invalid or mismatched access credentials.");
        return new(account!, token, expires.ToUniversalTime());
    }
    internal static void ValidateAccount(string? account)
    {
        if (account is not { Length: > 2 and <= 254 } || account.StartsWith('-') || !account.Contains('@') || account.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            throw new InvalidDataException("Google Cloud CLI account identity is invalid.");
    }
}

/// <summary>Bounded, isolated process transport, including a private typed browser bridge. No shell invocation.</summary>
public static class GoogleGcloudProcessTransport
{
    public static readonly string[] ExpectedScopes = ["openid", "https://www.googleapis.com/auth/userinfo.email", "https://www.googleapis.com/auth/cloud-platform",
        "https://www.googleapis.com/auth/appengine.admin", "https://www.googleapis.com/auth/sqlservice.login", "https://www.googleapis.com/auth/compute", "https://www.googleapis.com/auth/accounts.reauth"];
    private static readonly HashSet<string> QueryNames = ["client_id", "redirect_uri", "response_type", "scope", "state", "code_challenge", "code_challenge_method", "access_type", "prompt", "include_granted_scopes", "login_hint"];
    public static string? ValidateBrowserUrl(string url)
    {
        var safe = CliBrowserAction.Validate(url, new HashSet<string> { "accounts.google.com" }, QueryNames);
        if (safe is null) return null;
        var uri = new Uri(safe);
        if (uri.AbsolutePath is not ("/o/oauth2/auth" or "/o/oauth2/v2/auth")) return null;
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = part.Split('=', 2);
            if (fields.Length != 2 || !query.TryAdd(Uri.UnescapeDataString(fields[0]), Uri.UnescapeDataString(fields[1].Replace('+', ' ')))) return null;
        }
        if (GoogleGcloudProcess.RegisteredClientId is not { Length: > 0 } sdkClientId || query.GetValueOrDefault("client_id") != sdkClientId
            || query.GetValueOrDefault("scope") is not { } scope || !scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).SequenceEqual(ExpectedScopes.Order(StringComparer.Ordinal))
            || query.GetValueOrDefault("response_type") != "code" || query.GetValueOrDefault("code_challenge_method") != "S256"
            || query.GetValueOrDefault("state") is not { Length: >= 16 and <= 512 }
            || query.GetValueOrDefault("code_challenge") is not { Length: 43 }
            || !Uri.TryCreate(query.GetValueOrDefault("redirect_uri"), UriKind.Absolute, out var callback)
            || callback.Scheme != "http" || callback.Host != "localhost" || callback.Port is < 8085 or > 8185 || callback.AbsolutePath != "/"
            || callback.UserInfo.Length != 0 || callback.Query.Length != 0 || callback.Fragment.Length != 0) return null;
        return safe;
    }

    public static async Task<CliPrivateOutput> RunAsync(string executable, IReadOnlyList<string> arguments, string profile,
        Action<string>? browser, TimeSpan timeout, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This Google Cloud CLI transport is qualified on Linux only.");
        if (!Path.IsPathFullyQualified(executable) || arguments.Count > 16 || arguments.Any(a => a.Length > 1024 || a.Any(char.IsControl))
            || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10)) throw new ArgumentException("Invalid Google Cloud CLI invocation.");
        CliPrivatePaths.Directory(profile);
        ValidateProfileTree(profile);
        var lockPath = Path.Combine(profile, "lain-process.lock"); CliPrivatePaths.RefuseLinks(lockPath);
        using var lease = new FileStream(lockPath, new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        // This is an explicit user-only login profile. Seed the SDK's documented local residency
        // cache so even auth/login's separate GCE check cannot inspect ambient VM credentials.
        var gceCache = Path.Combine(profile, "gce"); CliPrivatePaths.RefuseLinks(gceCache);
        OwnStatePaths.WritePrivate(gceCache, "False");
        var bridge = Path.Combine(profile, "lain-browser");
        var marker = "LAIN_GCLOUD_BROWSER_" + Guid.NewGuid().ToString("N") + ":";
        CliPrivatePaths.RefuseLinks(bridge);
        OwnStatePaths.WritePrivate(bridge, "#!/usr/bin/python3\nimport sys\nif len(sys.argv) != 2: sys.exit(1)\nprint(" + JsonSerializer.Serialize(marker) + " + sys.argv[1], flush=True)\n");
        File.SetUnixFileMode(bridge, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, CreateNoWindow = true, WorkingDirectory = profile };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment.Clear();
        info.Environment["PATH"] = "/usr/bin:/bin";
        info.Environment["LANG"] = "C.UTF-8";
        info.Environment["CLOUDSDK_CONFIG"] = profile;
        info.Environment["CLOUDSDK_CORE_DISABLE_USAGE_REPORTING"] = "true";
        info.Environment["CLOUDSDK_METRICS_ENVIRONMENT"] = "lain-isolated";
        info.Environment["CLOUDSDK_CORE_DISABLE_FILE_LOGGING"] = "true";
        info.Environment["CLOUDSDK_CORE_CHECK_GCE_METADATA"] = "false";
        info.Environment["CLOUDSDK_COMPONENT_MANAGER_DISABLE_UPDATE_CHECK"] = "true";
        info.Environment["CLOUDSDK_CORE_DISABLE_PROMPTS"] = "true";
        info.Environment["PYTHONNOUSERSITE"] = "1";
        info.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        // SDK checks DISPLAY for native callback mode. This bridge never connects to X/Wayland:
        // Python webbrowser invokes only our private executable, and the host opens its validated URL.
        info.Environment["DISPLAY"] = "lain-browser-bridge";
        info.Environment["BROWSER"] = "\"" + bridge.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\" %s";
        using var process = new Process { StartInfo = info };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        Task<byte[]>? stdout = null; Task<byte[]>? stderr = null;
        var markerPath = Path.Combine(profile, "lain-process.json"); CliPrivatePaths.RefuseLinks(markerPath);
        try
        {
            if (!process.Start()) throw new IOException("Google Cloud CLI could not start.");
            process.StandardInput.Close();
            OwnStatePaths.WritePrivate(markerPath, JsonSerializer.Serialize(new GcloudProcessIdentity(process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()))));
            stdout = Capture(process.StandardOutput, marker, browser, deadline.Token);
            stderr = Capture(process.StandardError, null, null, deadline.Token);
            _ = stdout.ContinueWith(t => { if (t.IsFaulted) deadline.Cancel(); }, TaskScheduler.Default);
            _ = stderr.ContinueWith(t => { if (t.IsFaulted) deadline.Cancel(); }, TaskScheduler.Default);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token));
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            Stop(process);
            if (stdout is not null && stderr is not null) try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new IOException("Google Cloud CLI stopped or timed out. Reconnect this isolated connection.");
        }
        finally
        {
            Stop(process);
            if (File.Exists(markerPath)) File.Delete(markerPath);
            if (File.Exists(bridge)) File.Delete(bridge);
        }
    }
    internal static void ValidateProfileTree(string profile)
    {
        var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((profile, 0)); var count = 0;
        while (queue.TryDequeue(out var entry))
        {
            if (entry.Depth > 8) throw new IOException("Google Cloud CLI profile exceeds the directory bound.");
            foreach (var path in Directory.EnumerateFileSystemEntries(entry.Path))
            {
                if (++count > 2048) throw new IOException("Google Cloud CLI profile exceeds the file bound.");
                CliPrivatePaths.RefuseLinks(path);
                if (Directory.Exists(path)) queue.Enqueue((path, entry.Depth + 1));
            }
        }
    }
    private static async Task<byte[]> Capture(StreamReader reader, string? marker, Action<string>? browser, CancellationToken ct)
    {
        var bytesBuffer = new byte[2048]; var buffer = new char[4096]; var decoder = Encoding.UTF8.GetDecoder();
        var line = new StringBuilder(); using var output = new MemoryStream();
        var total = 0; int bytesRead;
        while ((bytesRead = await reader.BaseStream.ReadAsync(bytesBuffer.AsMemory(), ct)) > 0)
        {
            total += bytesRead;
            var count = decoder.GetChars(bytesBuffer.AsSpan(0, bytesRead), buffer.AsSpan(), flush: false);
            if (total > 1024 * 1024) throw new IOException("Google Cloud CLI output exceeds the bound.");
            for (var i = 0; i < count; i++)
            {
                var c = buffer[i];
                if (c != '\n') { line.Append(c); if (line.Length > 32768) throw new IOException("Google Cloud CLI output line exceeds the bound."); continue; }
                Consume(line.ToString().TrimEnd('\r')); line.Clear();
            }
        }
        if (line.Length != 0) Consume(line.ToString());
        return output.ToArray();
        void Consume(string text)
        {
            if (marker is not null && text.StartsWith(marker, StringComparison.Ordinal))
            {
                var url = ValidateBrowserUrl(text[marker.Length..]);
                if (url is null || browser is null) throw new IOException("Google Cloud CLI returned an unsupported browser action.");
                browser(url); return;
            }
            var bytes = Encoding.UTF8.GetBytes(text + "\n"); output.Write(bytes);
        }
    }
    private static void Stop(Process process)
    { try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(3000); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } }

    public static void ReconcileOwnedProcess(string profile)
    {
        var marker = Path.Combine(profile, "lain-process.json");
        if (!File.Exists(marker)) return;
        var identity = JsonSerializer.Deserialize<GcloudProcessIdentity>(CliPrivatePaths.Read(marker, 2048)) ?? throw new InvalidDataException("Invalid Google Cloud CLI process marker.");
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            if (new DateTimeOffset(process.StartTime.ToUniversalTime()) == identity.StartedAt)
            {
                var environ = File.ReadAllBytes($"/proc/{identity.ProcessId}/environ");
                if (environ.Length <= 65536 && Encoding.UTF8.GetString(environ).Split('\0').Contains("CLOUDSDK_CONFIG=" + profile, StringComparer.Ordinal)) Stop(process);
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception) { }
        File.Delete(marker);
    }
    private sealed record GcloudProcessIdentity(int ProcessId, DateTimeOffset StartedAt);
}
