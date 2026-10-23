using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static class CliApplication
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Help = """
        Usage: lain <command> [options]
          status | accounts | models | profiles | quotas [--json]
          profiles apply ID [--revision N] [--json]
          profiles install-codex ID [--revision N] [--json]
          import CONFIG [--auth-dir DIR] [--revision N] [--json]
          usage [--today] [--from ISO] [--to ISO] [--timezone IANA]
                [--account ID] [--model ID] [--status HTTP] [--json]
          attempts [usage options] [--offset N] [--limit N] [--json]
          auth token --audience codex
          service start|stop|restart|status [--json]
          ui
        Exit codes: 0 success; 1 unexpected; 2 usage; 3 unavailable;
                    4 authorization; 5 request rejected.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        Func<LainApiClient>? clientFactory = null, CancellationToken ct = default)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            { await output.WriteLineAsync(Help); return 0; }
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
            if (command is not ("status" or "accounts" or "import" or "models" or "profiles" or "usage" or "attempts" or "quotas" or "auth" or "service"))
                throw new ArgumentException($"Unknown command '{command}'. Run 'lain help'.");
            Validate(command, options);
            using var client = (clientFactory ?? LainApiClient.CreateLocal)();
            object result;
            switch (command)
            {
                case "status":
                case "service": result = await client.StatusAsync(ct); break;
                case "accounts": result = await client.AccountsAsync(ct); break;
                case "models": result = await client.ModelsAsync(ct); break;
                case "profiles":
                    if (options.Positionals.Count == 0) { result = await client.ProfilesAsync(ct); break; }
                    var profileRevision = options.Get("revision") is { } revisionValue ? ParseLong(revisionValue, "revision") :
                        (await client.GetConfigurationAsync(ct)).Revision;
                    if (options.Positionals[0] == "apply")
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
                case "import":
                    var revision = options.Get("revision") is { } revisionText ? ParseLong(revisionText, "revision") :
                        (await client.GetConfigurationAsync(ct)).Revision;
                    result = await client.ImportAsync(new ImportRequest(revision, Path.GetFullPath(options.Positionals[0]),
                        options.Get("auth-dir") is { } directory ? Path.GetFullPath(directory) : null), ct);
                    break;
                case "usage": result = await client.UsageAsync(ParseQuery(options), ct); break;
                case "attempts": result = await client.AttemptsAsync(ParseQuery(options), ct); break;
                case "auth":
                    var token = await client.InferenceTokenAsync("codex", ct);
                    if (string.IsNullOrWhiteSpace(token.Token) || token.Token.Any(char.IsWhiteSpace))
                        throw new InvalidDataException("lain host returned an invalid inference credential.");
                    await output.WriteLineAsync(token.Token);
                    return 0;
                default: throw new ArgumentException("Unsupported command.");
            }
            await Write(output, result, options.Json);
            if (!options.Json && result is ImportResult imported)
                foreach (var warning in imported.Warnings) await error.WriteLineAsync("lain: warning: " + warning);
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
        if (command is "usage" or "attempts")
        {
            options.RequireOnly("json", "today", "from", "to", "timezone", "account", "model", "status", "offset", "limit");
            options.RequirePositionals(0); _ = ParseQuery(options); return;
        }
        if (command == "import")
        { options.RequireOnly("json", "auth-dir", "revision"); options.RequirePositionals(1); return; }
        if (command == "profiles" && options.Positionals.Count > 0)
        {
            options.RequireOnly("json", "revision"); options.RequirePositionals(2);
            if (options.Positionals[0] is not ("apply" or "install-codex"))
                throw new ArgumentException("Profile action must be apply or install-codex.");
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
        int? status = options.Get("status") is { } statusText ? ParseInt(statusText, "status") : null;
        if (status is < 100 or > 599) throw new ArgumentException("--status must be an HTTP status between 100 and 599.");
        var offset = options.Get("offset") is { } offsetText ? ParseInt(offsetText, "offset") : 0;
        var limit = options.Get("limit") is { } limitText ? ParseInt(limitText, "limit") : 100;
        if (offset < 0 || limit is < 1 or > 1000) throw new ArgumentException("--offset must be nonnegative; --limit must be between 1 and 1000.");
        return new UsageQuery(from, to, zone, options.Get("account"), options.Get("model"), status, offset, limit);
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

    private static async Task Write(TextWriter output, object value, bool json)
    {
        if (json) { await output.WriteLineAsync(JsonSerializer.Serialize(value, value.GetType(), Json)); return; }
        switch (value)
        {
            case StatusDto s: await output.WriteLineAsync($"lain {s.Version}: {s.State}; proxy {(s.ProxyReady ? "ready" : "unavailable")}; configuration {s.AppliedRevision}/{s.Revision}"); break;
            case AccountDto[] accounts:
                foreach (var a in accounts) await output.WriteLineAsync($"{a.Id}\t{a.Name}\t{a.Provider}\t{(a.Enabled ? "enabled" : "disabled")}\t{a.State}"); break;
            case ModelCatalogDto models:
                foreach (var m in models.Models) await output.WriteLineAsync($"{m.Id}\t{m.Provider}\t{string.Join(",", m.AccountIds)}"); break;
            case ProfileDto[] profiles:
                foreach (var p in profiles) await output.WriteLineAsync($"{p.Id}\t{p.Name}\t{string.Join(",", p.RouteIds)}"); break;
            case UsageSummaryDto u:
                await output.WriteLineAsync($"{u.From:O} to {u.To:O} ({u.TimeZone})");
                await output.WriteLineAsync($"Requests {u.Totals.Requests}; attempts {u.Totals.Attempts}; errors {u.Totals.Errors}; tokens {u.Totals.Tokens.Total?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
                await output.WriteLineAsync($"Observed {u.ObservedAt:O}; coverage {(u.Complete ? "complete" : "incomplete")}; unknown usage attempts {u.Totals.UnknownUsageAttempts}"); break;
            case ImportResult i:
                await output.WriteLineAsync($"Imported revision {i.Revision}: {i.Added} added, {i.Updated} updated."); break;
            case CodexInstallResult i:
                await output.WriteLineAsync($"Installed {i.ConfigurationPath} and {i.CatalogPath} (revision {i.Revision}).");
                await output.WriteLineAsync("Codex arguments: " + string.Join(" ", i.LaunchArguments)); break;
            default: await output.WriteLineAsync(JsonSerializer.Serialize(value, value.GetType(), Json)); break;
        }
    }

    private sealed class Options
    {
        private readonly Dictionary<string, string?> values = new(StringComparer.Ordinal);
        public List<string> Positionals { get; } = [];
        public bool Json => Has("json");
        public bool Has(string name) => values.ContainsKey(name);
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
                if (options.values.ContainsKey(name)) throw new ArgumentException($"Duplicate option --{name}.");
                string? value = null;
                if (name is not ("json" or "today"))
                {
                    if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"--{name} requires a value.");
                    value = args[i];
                }
                options.values[name] = value;
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
