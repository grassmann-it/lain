using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Help = """
        Usage: lain <command> [options]
          status | accounts | models | profiles | quotas [--json]
          usage [--full] [--json]  Current quotas for every configured account
                Compact by default; --full includes IDs and complete source details.
          accounts add  Private interactive account setup (TTY required)
          accounts monitor ID  Private optional monitoring setup (TTY required)
          accounts methods [PROVIDER] [--json]
          accounts login PROVIDER [--method ID] [--purpose quota,inference]
                   [--account ID] [--name NAME] [--revision N] [--json]
          accounts login-status|login-cancel|check ID [--json]
          accounts enable|disable|remove ID [--revision N] [--json]
          accounts add-api --name NAME --provider PROVIDER --base-url URL
                   (--key-env ENVNAME | --key-stdin) [--provider-route ID]
                   [--setting NAME=VALUE ...] [--revision N] [--json]
          mode [monitor|gateway] [--revision N] [--json]
          profiles preview ID [--revision N] [--json]
          profiles apply ID [--revision N] [--json]
          profiles install-codex ID [--revision N] [--json]
          import CONFIG [--auth-dir DIR] [--revision N] [--json]
          history [--today] [--from ISO] [--to ISO] [--timezone IANA]
                [--account ID] [--model ID] [--status HTTP] [--json]
          history analysis|aggregate|performance [history filters]
                [--group-by DIMENSION[,DIMENSION...]] [--group-limit 1..500] [--json]
                Default groups: modelFamily,accountId,reasoningEffort; at most 3 dimensions.
          History filters: --route --model-family --manufacturer --inference-provider
                --gateway --billing-account --reasoning --mode --conversation
                --effective-reasoning-effort --turn --worker --harness --profile
                (each followed by a value; --reasoning is the requested effort)
          usage with history filters or analysis remains a history alias.
          attempts [history options] [--offset N] [--limit N] [--json]
          inspector [snapshot] [--request ID] [--limit 1..256] [--json]
          inspector events [--source ID] [--after N] [--request ID] [--limit 1..500] [--json]
          providers [catalog] [--json]
          providers onboarding ID [--json]
          providers routes [--provider ID] [--json]
          providers check-free ROUTE_ID [--json]
          auth token --audience codex
          service start|stop|restart|status [--json]
          ui
        Exit codes: 0 success; 1 unexpected; 2 usage; 3 unavailable;
                    4 authorization; 5 request rejected.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        Func<LainApiClient>? clientFactory = null, CancellationToken ct = default, TextReader? input = null, IAccountWizardTerminal? wizardTerminal = null)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            { await WriteHelp(output); return 0; }
            var options = Options.Parse(args[1..]);
            var command = args[0];
            if (command == "ui")
            {
                options.RequireOnly(); options.RequirePositionals(0);
                var executable = Path.GetFullPath(Path.Combine(LocalDiscovery.StateDirectory, "app", "desktop", "Lain.Desktop"));
                if (!File.Exists(executable)) throw new IOException("lain desktop is not installed. Run the lain installer first.");
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
                return 0;
            }
            if (command == "service" && options.Positionals.FirstOrDefault() is "start" or "stop" or "restart")
            {
                options.RequireOnly("json"); options.RequirePositionals(1);
                var action = options.Positionals[0];
                using var process = Process.Start(new ProcessStartInfo("systemctl") {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    ArgumentList = { "--user", action, "lain.service" }
                }) ?? throw new IOException("Could not launch the user service manager.");
                var stdout = process.StandardOutput.ReadToEndAsync(ct);
                var stderr = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct); await stdout;
                var diagnostic = await stderr;
                if (process.ExitCode != 0)
                {
                    await error.WriteLineAsync("lain: user service action failed." + (string.IsNullOrWhiteSpace(diagnostic) ? "" : " " + diagnostic.Trim()));
                    return 3;
                }
                await Write(output, new { service = "lain.service", action, state = "completed" }, options.Json);
                return 0;
            }
            // Validate the command before discovery, so misspellings work even when the host is stopped.
            if (command is not ("status" or "accounts" or "import" or "models" or "profiles" or "usage" or "history" or "attempts" or "quotas" or "auth" or "service" or "providers" or "inspector" or "mode"))
                throw new ArgumentException($"Unknown command '{command}'. Run 'lain help'.");
            Validate(command, options);
            if (command == "accounts" && options.Positionals.FirstOrDefault() is "add" or "monitor")
            {
                wizardTerminal ??= new ConsoleAccountWizardTerminal(output);
                if (!wizardTerminal.IsInteractive)
                    throw new ArgumentException("Account setup requires an interactive terminal. Use accounts add-api --key-stdin or --key-env for automation.");
            }
            using var client = (clientFactory ?? LainApiClient.CreateLocal)();
            object result;
            switch (command)
            {
                case "status":
                case "service": result = await client.StatusAsync(ct); break;
                case "mode":
                    if (options.Positionals.Count == 0) result = await client.StatusAsync(ct);
                    else
                    {
                        var modeRevision = options.Get("revision") is { } modeText ? ParseLong(modeText, "revision") : (await client.GetConfigurationAsync(ct)).Revision;
                        result = await client.SetRuntimeModeAsync(new(modeRevision, options.Positionals[0]), ct);
                    }
                    break;
                case "accounts":
                    if (options.Positionals.FirstOrDefault() == "add")
                    {
                        var saved = await AddAccountWizard(client, output, wizardTerminal!, ct);
                        if (!saved) await output.WriteLineAsync("Account setup cancelled. No account was saved.");
                        return 0;
                    }
                    if (options.Positionals.FirstOrDefault() == "monitor")
                    {
                        if (!await MonitorAccountWizard(client, options.Positionals[1], output, wizardTerminal!, ct))
                            await output.WriteLineAsync("Monitoring setup cancelled. Saved account is unchanged.");
                        return 0;
                    }
                    result = await Accounts(client, options, input ?? Console.In, ct);
                    break;
                case "models": result = await client.ModelsAsync(ct); break;
                case "profiles":
                    if (options.Positionals.Count == 0) { result = await client.ProfilesAsync(ct); break; }
                    var profileRevision = options.Get("revision") is { } revisionValue ? ParseLong(revisionValue, "revision") :
                        (await client.GetConfigurationAsync(ct)).Revision;
                    if (options.Positionals[0] == "preview") result = await client.PreviewProfileAsync(options.Positionals[1], profileRevision, ct);
                    else if (options.Positionals[0] == "apply")
                        result = await client.ApplyProfileAsync(profileRevision, options.Positionals[1], ct);
                    else
                    {
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        var executable = Path.GetFullPath(Path.Combine(home, ".local", "bin", "lain"));
                        if (!File.Exists(executable)) throw new IOException("lain launcher is missing at ~/.local/bin/lain. Run the lain installer first.");
                        var codexDirectory = Path.Combine(home, ".codex");
                        var artifacts = await client.ExportCodexProfileAsync(profileRevision, options.Positionals[1], executable,
                            Path.Combine(codexDirectory, "lain.models.json"), ct);
                        result = CodexArtifactInstaller.Install(artifacts, codexDirectory);
                    }
                    break;
                case "quotas": result = await client.QuotasAsync(ct); break;
                case "providers": result = await Providers(client, options, ct); break;
                case "inspector": result = await Inspector(client, options, ct); break;
                case "import":
                    var revision = options.Get("revision") is { } revisionText ? ParseLong(revisionText, "revision") :
                        (await client.GetConfigurationAsync(ct)).Revision;
                    result = await client.ImportAsync(new ImportRequest(revision, Path.GetFullPath(options.Positionals[0]),
                        options.Get("auth-dir") is { } directory ? Path.GetFullPath(directory) : null), ct);
                    break;
                case "usage":
                    if (IsAccountUsage(options)) { result = await AccountUsage(client, ct); break; }
                    goto case "history";
                case "history":
                    result = options.Positionals.Count == 0 ? await client.UsageAsync(ParseQuery(options), ct) :
                        await client.UsageAnalysisAsync(ParseQuery(options), ct);
                    break;
                case "attempts": result = await client.AttemptsAsync(ParseQuery(options), ct); break;
                case "auth":
                    var token = await client.InferenceTokenAsync("codex", ct);
                    if (string.IsNullOrWhiteSpace(token.Token) || token.Token.Any(char.IsWhiteSpace))
                        throw new InvalidDataException("lain host returned an invalid inference credential.");
                    await output.WriteLineAsync(token.Token);
                    return 0;
                default: throw new ArgumentException("Unsupported command.");
            }
            await Write(output, result, options.Json, options.Has("full"));
            if (!options.Json && result is ImportResult imported)
                foreach (var warning in imported.Warnings) await error.WriteLineAsync("lain: warning: " + warning);
            if (!options.Json && result is LoginDto { Error: { } loginError }) await error.WriteLineAsync("lain: " + loginError);
            return 0;
        }
        catch (LainApiException ex)
        {
            await error.WriteLineAsync($"lain: {ex.Code}: {ex.Message}");
            return ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? 4 :
                (int)ex.StatusCode >= 500 ? 3 : 5;
        }
        catch (ArgumentException ex) { await error.WriteLineAsync("lain: " + ex.Message); return 2; }
        catch (TimeZoneNotFoundException) { await error.WriteLineAsync("lain: unknown timezone."); return 2; }
        catch (InvalidTimeZoneException) { await error.WriteLineAsync("lain: invalid timezone."); return 2; }
        catch (HttpRequestException) { await error.WriteLineAsync("lain: host is unavailable at http://127.0.0.1:18418. Start it with 'lain service start'."); return 3; }
        catch (OperationCanceledException) { await error.WriteLineAsync(ct.IsCancellationRequested ? "lain: operation cancelled." : "lain: host request timed out."); return 3; }
        catch (IOException ex) { await error.WriteLineAsync("lain: " + ex.Message); return 3; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { await error.WriteLineAsync("lain: " + ex.Message); return 3; }
        catch (Exception) { await error.WriteLineAsync("lain: unexpected failure."); return 1; }
    }

    private static void Validate(string command, Options options)
    {
        if (command == "mode")
        {
            options.RequireOnly("json", "revision");
            if (options.Positionals.Count > 1 || options.Positionals.FirstOrDefault() is { } mode && mode is not ("monitor" or "gateway")) throw new ArgumentException("Use mode monitor or mode gateway.");
            if (options.Get("revision") is { } revision) _ = ParseLong(revision, "revision");
            return;
        }
        if (command == "providers") { ValidateProviders(options); return; }
        if (command == "inspector") { ValidateInspector(options); return; }
        if (command == "accounts" && options.Positionals.Count > 0)
        {
            var action = options.Positionals[0];
            if (action == "methods")
            {
                options.RequireOnly("json");
                if (options.Positionals.Count is not (1 or 2)) throw new ArgumentException("Use accounts methods [PROVIDER].");
                return;
            }
            if (action == "add")
            {
                options.RequireOnly(); options.RequirePositionals(1); return;
            }
            if (action == "monitor")
            {
                options.RequireOnly(); options.RequirePositionals(2); return;
            }
            if (action == "add-api")
            {
                options.RequireOnly("json", "revision", "name", "provider", "base-url", "key-env", "key-stdin", "provider-route", "setting");
                options.RequirePositionals(1);
                foreach (var required in new[] { "name", "provider", "base-url" })
                    if (string.IsNullOrWhiteSpace(options.Get(required))) throw new ArgumentException($"--{required} is required.");
                if (options.Has("key-env") == options.Has("key-stdin"))
                    throw new ArgumentException("Specify exactly one of --key-env ENVNAME or --key-stdin. API keys cannot be passed as command arguments.");
                if (options.Has("key-env") && string.IsNullOrWhiteSpace(options.Get("key-env")))
                    throw new ArgumentException("--key-env requires a nonempty environment variable name.");
                if (options.Has("provider-route") && string.IsNullOrWhiteSpace(options.Get("provider-route")))
                    throw new ArgumentException("--provider-route requires a nonempty catalog route ID.");
                _ = ParseAccountSettings(options);
            }
            else
            {
                options.RequirePositionals(2);
                if (action == "login")
                {
                    options.RequireOnly("json", "revision", "method", "purpose", "account", "name");
                    if (options.Get("purpose") is { } purposes && purposes.Split(',', StringSplitOptions.TrimEntries).Any(p => p is not ("quota" or "inference" or "cost" or "models"))) throw new ArgumentException("Unknown connection purpose.");
                }
                else if (action is "enable" or "disable" or "remove") options.RequireOnly("json", "revision");
                else if (action is "check" or "login-status" or "login-cancel") options.RequireOnly("json");
                else throw new ArgumentException("Account action must be add, add-api, monitor, login, login-status, login-cancel, enable, disable, remove or check.");
            }
            if (options.Get("revision") is { } revision) _ = ParseLong(revision, "revision");
            return;
        }
        if ((command is "usage" or "history") && options.Has("full") && (command != "usage" || !IsAccountUsage(options)))
            throw new ArgumentException("--full is only valid for the current account usage overview.");
        if (command == "usage" && IsAccountUsage(options))
        { options.RequireOnly("json", "full"); options.RequirePositionals(0); return; }
        if (command is "usage" or "history" or "attempts")
        {
            ValidateUsage(command, options); return;
        }
        if (command == "import")
        { options.RequireOnly("json", "auth-dir", "revision"); options.RequirePositionals(1); return; }
        if (command == "profiles" && options.Positionals.Count > 0)
        {
            options.RequireOnly("json", "revision"); options.RequirePositionals(2);
            if (options.Positionals[0] is not ("apply" or "preview" or "install-codex"))
                throw new ArgumentException("Profile action must be preview, apply or install-codex.");
            if (options.Get("revision") is { } revision) _ = ParseLong(revision, "revision");
            return;
        }
        if (command == "auth")
        {
            options.RequireOnly("audience"); options.RequirePositionals(1);
            if (options.Positionals[0] != "token" || options.Get("audience") != "codex")
                throw new ArgumentException("Use 'lain auth token --audience codex'.");
            return;
        }
        options.RequireOnly("json");
        options.RequirePositionals(command == "service" ? 1 : 0);
        if (command == "service" && options.Positionals[0] != "status")
            throw new ArgumentException("Service action must be start, stop, restart or status.");
    }

    private static async Task<object> Accounts(LainApiClient client, Options options, TextReader input, CancellationToken ct)
    {
        if (options.Positionals.Count == 0) return await client.AccountsAsync(ct);
        var action = options.Positionals[0];
        if (action == "methods") return await client.ConnectionMethodsAsync(options.Positionals.Count > 1 ? options.Positionals[1] : null, ct);
        if (action == "login-status") return await client.GetLoginAsync(options.Positionals[1], ct);
        if (action == "login-cancel") return await client.CancelLoginAsync(options.Positionals[1], ct);
        if (action == "check") return await client.CheckAccountAsync(options.Positionals[1], ct);
        var settings = action == "add-api" ? ParseAccountSettings(options) : null;
        if (settings is not null) await ValidateAccountSettings(client, options.Get("provider")!, settings, ct);
        string? apiKey = null;
        if (action == "add-api")
        {
            apiKey = options.Has("key-stdin") ? await input.ReadLineAsync(ct) :
                Environment.GetEnvironmentVariable(options.Get("key-env")!);
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsWhiteSpace))
                throw new ArgumentException("The API key source is empty or contains whitespace.");
        }
        var revision = options.Get("revision") is { } supplied ? ParseLong(supplied, "revision") :
            (await client.GetConfigurationAsync(ct)).Revision;
        if (action == "login")
        {
            var accountId = options.Get("account");
            var target = accountId is null ? null : (await client.GetConfigurationAsync(ct)).Accounts.FirstOrDefault(a => a.Id == accountId)
                ?? throw new ArgumentException("The reconnect account is no longer configured.");
            return await client.LoginAsync(new LoginRequest(revision, options.Positionals[1], options.Get("method"),
                options.Get("purpose")?.Split(',', StringSplitOptions.TrimEntries), accountId, target?.Binding,
                target?.Generation, options.Get("name"), Guid.NewGuid().ToString("N")), ct);
        }
        return action switch
        {
            "enable" => await client.SetAccountEnabledAsync(revision, options.Positionals[1], true, ct),
            "disable" => await client.SetAccountEnabledAsync(revision, options.Positionals[1], false, ct),
            "remove" => await client.RemoveAccountAsync(revision, options.Positionals[1], ct),
            "add-api" => await client.AddApiAccountAsync(revision, new AddApiAccountRequest(options.Get("name")!, options.Get("provider")!, apiKey!, options.Get("base-url")!,
                options.Get("provider-route"), settings), ct),
            _ => throw new ArgumentException("Unsupported account action.")
        };
    }

    private static UsageQuery ParseQuery(Options options)
    {
        var zone = options.Get("timezone") ?? TimeZoneInfo.Local.Id;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(zone);
        var from = ParseTime(options.Get("from")); var to = ParseTime(options.Get("to"));
        if (options.Has("today"))
        {
            if (from is not null || to is not null) throw new ArgumentException("--today cannot be combined with --from or --to.");
            var date = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).Date;
            from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date, timeZone));
            to = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1), timeZone));
        }
        if (from is not null && to is not null && from >= to) throw new ArgumentException("--from must be earlier than --to.");
        if (from is not null && to is not null && to.Value - from.Value > TimeSpan.FromDays(366)) throw new ArgumentException("Usage intervals must not exceed 366 days.");
        int? status = options.Get("status") is { } statusText ? ParseInt(statusText, "status") : null;
        if (status is < 100 or > 599) throw new ArgumentException("--status must be an HTTP status between 100 and 599.");
        var offset = options.Get("offset") is { } offsetText ? ParseInt(offsetText, "offset") : 0;
        var limit = options.Get("limit") is { } limitText ? ParseInt(limitText, "limit") : 100;
        if (offset < 0 || limit is < 1 or > 500) throw new ArgumentException("--offset must be nonnegative; --limit must be between 1 and 500.");
        var groupLimit = options.Get("group-limit") is { } groupLimitText ? ParseInt(groupLimitText, "group-limit") : 100;
        if (groupLimit is < 1 or > 500) throw new ArgumentException("--group-limit must be between 1 and 500.");
        return new UsageQuery(from, to, zone, options.Get("account"), options.Get("model"), status, offset, limit,
            options.Get("route"), options.Get("model-family"), options.Get("manufacturer"), options.Get("inference-provider"),
            options.Get("gateway"), options.Get("billing-account"), options.Get("reasoning"), options.Get("mode"),
            options.Get("conversation"), options.Get("turn"), options.Get("worker"), options.Get("harness"), options.Get("profile"),
            options.Get("group-by") is { } grouping ? string.Join(',', grouping.Split(',').Select(d => d.Trim())) : null,
            groupLimit, options.Get("effective-reasoning-effort"));
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (value is null) return null;
        // Require an explicit offset to avoid machine-local interpretation of a supplied instant.
        if (!value.Contains('T') || !(value.EndsWith('Z') || value.LastIndexOf('+') > value.IndexOf('T') || value.LastIndexOf('-') > value.IndexOf('T')) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException("Times must be ISO 8601 timestamps with an explicit offset (for example 2026-10-23T00:00:00+02:00).");
        return time;
    }
    private static int ParseInt(string value, string name) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        ? parsed : throw new ArgumentException($"--{name} must be an integer.");
    private static long ParseLong(string value, string name) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        ? parsed : throw new ArgumentException($"--{name} must be a nonnegative integer.");

    private static async Task Write(TextWriter output, object value, bool json, bool full = false)
    {
        if (json) { await output.WriteLineAsync(JsonSerializer.Serialize(value, value.GetType(), Json)); return; }
        switch (value)
        {
            case AccountUsageOverview overview: await WriteAccountUsage(output, overview, full: full); break;
            case StatusDto s: await WriteStatus(output, s); break;
            case AccountDto[] accounts:
                await WriteAccounts(output, accounts); break;
            case LoginDto login:
                await output.WriteLineAsync($"Login {Cell(login.Id)} ({Cell(login.Provider)}): {Cell(login.State)}");
                if (login.Url is not null) await output.WriteLineAsync(Cell(login.Url));
                break;
            case ModelCatalogDto models:
                await WriteModels(output, models); break;
            case ProfileDto[] profiles:
                await WriteProfiles(output, profiles); break;
            case UsageSummaryDto u:
                await WriteUsage(output, u); break;
            case PageDto<AttemptDto> attempts:
                await output.WriteLineAsync($"Attempts {attempts.Items.Length} of {attempts.Total}; offset {attempts.Offset}; limit {attempts.Limit}");
                foreach (var a in attempts.Items)
                {
                    await HumanTable.WriteTextAsync(output, $"Attempt {Cell(a.Id)} · {Cell(a.State)}; completion {Cell(a.CompletionState)}");
                    await HumanTable.WriteTextAsync(output, $"Account {Cell(a.AccountId)}; provider {Cell(a.Provider)}; model {Cell(a.Model)}", indent: "  ");
                    await HumanTable.WriteTextAsync(output, $"HTTP {Number(a.StatusCode)}; tokens {Number(a.Tokens.Total)}; first output {Number(a.Measurements?.FirstOutputLatencyMs)} ms; duration {Number(a.Measurements?.TotalDurationMs)} ms", indent: "  ");
                    if (a.ErrorDetails is { } errorDetails)
                        await HumanTable.WriteTextAsync(output, $"Error category {Cell(errorDetails.Category)}; evidence {Cell(errorDetails.Evidence)}; next action {Cell(errorDetails.Action)}", indent: "  ");
                    if (a.FirstTokenMs is not null || a.DurationMs is not null)
                        await output.WriteLineAsync($"  Historical timing: first token {Number(a.FirstTokenMs)} ms; duration {Number(a.DurationMs)} ms");
                }
                break;
            case QuotaDto[] quotas:
                await WriteQuotas(output, quotas); break;
            case ImportResult i:
                await output.WriteLineAsync($"Imported revision {i.Revision}: {i.Added} added, {i.Updated} updated."); break;
            case CodexInstallResult i:
                await output.WriteLineAsync($"Installed {i.ConfigurationPath} and {i.CatalogPath} (revision {i.Revision}).");
                await output.WriteLineAsync("Codex arguments: " + string.Join(" ", i.LaunchArguments)); break;
            default:
                if (!await WriteExtended(output, value))
                    await output.WriteLineAsync(JsonSerializer.Serialize(value, value.GetType(), Json));
                break;
        }
    }

    // Keep remote identifiers and redacted previews on a single terminal line.
    private static string Cell(string? value) => HumanTable.Clean(value);
    private static string Number<T>(T? value) where T : struct, IFormattable =>
        value?.ToString(null, CultureInfo.InvariantCulture) ?? "unknown";
    private sealed class Options
    {
        private readonly Dictionary<string, string?> values = new(StringComparer.Ordinal);
        public List<string> Positionals { get; } = [];
        public List<string> Settings { get; } = [];
        public bool Json => Has("json");
        public bool Has(string name) => values.ContainsKey(name);
        public bool HasAnyExcept(params string[] names) => values.Keys.Any(key => !names.Contains(key, StringComparer.Ordinal));
        public string? Get(string name) => values.GetValueOrDefault(name);
        public static Options Parse(string[] args)
        {
            var options = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) { options.Positionals.Add(arg); continue; }
                var name = arg[2..];
                if (name.Length == 0 || name.Contains('=')) throw new ArgumentException("Use options as '--name value'.");
                if (options.values.ContainsKey(name) && name != "setting") throw new ArgumentException($"Duplicate option --{name}.");
                string? value = null;
                if (name is not ("json" or "full" or "today" or "key-stdin"))
                {
                    if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"--{name} requires a value.");
                    value = args[i];
                }
                options.values[name] = value;
                if (name == "setting") options.Settings.Add(value!);
            }
            return options;
        }
        public void RequireOnly(params string[] names)
        {
            foreach (var name in values.Keys) if (!names.Contains(name, StringComparer.Ordinal)) throw new ArgumentException($"Unknown option --{name}.");
        }
        public void RequirePositionals(int count)
        { if (Positionals.Count != count) throw new ArgumentException($"Expected {count} positional argument(s); run 'lain help'."); }
    }
}
