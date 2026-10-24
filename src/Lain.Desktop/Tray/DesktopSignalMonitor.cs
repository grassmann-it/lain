using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lain.Client;
using Lain.Core;

namespace Lain.Desktop.Tray;

/// <summary>Read-only desktop signals. The caller owns the client and polling lifetime.</summary>
public sealed class DesktopSignalMonitor(LainApiClient client)
{
    private static readonly TimeSpan QuotaFreshness = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AttemptLookback = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(5);
    private const int AttemptPageSize = 500, AttemptPages = 4, MaximumSignals = 12;
    private const int MaximumSeenAttempts = 4096, MaximumRoutes = 256, MaximumWindows = 512;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly Dictionary<AttemptKey, DateTimeOffset> _seenAttempts = [];
    private readonly Dictionary<RouteKey, RouteSignal> _routes = [];
    private readonly Dictionary<WindowKey, WindowSignal> _windows = [];
    private DateTimeOffset? _startedAt;
    private bool _attemptBaseline;

    public async Task<TrayStatusSnapshot> PollAsync(DateTimeOffset now, CancellationToken ct)
    {
        await _pollGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _startedAt ??= now;
            var statusTask = ReadAsync(() => client.StatusAsync(ct), ct);
            var configurationTask = ReadAsync(() => client.GetConfigurationAsync(ct), ct);
            var quotasTask = ReadAsync(() => client.QuotasAsync(ct), ct);
            var attemptsTask = ReadAttemptsAsync(now, ct);
            await Task.WhenAll(statusTask, configurationTask, quotasTask, attemptsTask).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var status = await statusTask;
            var configuration = await configurationTask;
            var quotas = await quotasTask;
            var attempts = await attemptsTask;
            var connected = status is { ContractVersion: Api.Version };
            var alerts = new List<DesktopAlert>();
            var accounts = configuration?.Accounts ?? [];
            var routes = configuration?.Routes ?? [];
            if (quotas is not null) ObserveQuotas(quotas, accounts, now, alerts);
            if (attempts is not null) ObserveAttempts(attempts.Items, routes, now, alerts);
            Trim(now);

            var lines = new List<string>
            {
                connected ? status!.ProxyReady ? "Local proxy ready" : "Local proxy not ready" : "Local host unavailable",
                "Provider quota · reported windows, separate from local usage"
            };
            if (connected && status!.Revision != status.AppliedRevision) lines.Insert(1, "Configuration changes pending");
            if (configuration is null) lines.Add("Account labels unavailable");
            if (quotas is null) lines.Add("Provider quota unavailable · no fresh quota signal");
            else AppendQuotaLines(lines, quotas, accounts, now);
            lines.Add(attempts is null ? "Local HTTP 429 observations unavailable · coverage unknown"
                : attempts.Truncated ? "Local HTTP 429 scan limited · some attempts may be missed"
                : "Recent local HTTP 429 observations · may be incomplete");
            var tooltip = connected ? status!.ProxyReady ? "lain · proxy ready" : "lain · proxy not ready" : "lain · host unavailable";
            return new TrayStatusSnapshot(tooltip, lines.ToArray(), alerts.ToArray(), connected);
        }
        finally { _pollGate.Release(); }
    }

    private async Task<AttemptScan?> ReadAttemptsAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var items = new List<AttemptDto>();
            var from = now - AttemptLookback;
            // A fixed [from,to) prevents new starts from moving the page boundary.
            // Late journal updates can still shift pages; this is never certified coverage.
            for (var pageIndex = 0; pageIndex < AttemptPages; pageIndex++)
            {
                var offset = pageIndex * AttemptPageSize;
                var page = await client.AttemptsAsync(new UsageQuery(From: from, To: now, StatusCode: 429,
                    Offset: offset, Limit: AttemptPageSize), ct).ConfigureAwait(false);
                if (page.Offset != offset || page.Items.Length > AttemptPageSize || page.Total < 0) return null;
                items.AddRange(page.Items);
                if ((long)offset + page.Items.Length >= page.Total) return new(items, false);
                if (page.Items.Length < AttemptPageSize) return new(items, true);
            }
            return new(items, true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return null; }
    }

    private void ObserveAttempts(IReadOnlyList<AttemptDto> attempts, RouteDto[] configuredRoutes, DateTimeOffset now, List<DesktopAlert> alerts)
    {
        foreach (var attempt in attempts)
        {
            if (attempt.StatusCode != 429 || attempt.StartedAt < now - AttemptLookback || attempt.StartedAt >= now ||
                string.IsNullOrEmpty(attempt.Id) || attempt.Id.Length > 256 || attempt.SourceId?.Length > 256) continue;
            var identity = new AttemptKey(attempt.SourceId, attempt.Id);
            if (!_seenAttempts.TryAdd(identity, now)) continue;
            if (!_attemptBaseline || _startedAt is { } started && attempt.StartedAt < started) continue;
            var key = new RouteKey(attempt.RouteId is { Length: > 0 and <= 256 } ? attempt.RouteId : null);
            if (!_routes.TryGetValue(key, out var signal))
            {
                EnsureSpace(_routes, MaximumRoutes, entry => entry.LastObservedAt);
                _routes[key] = signal = new RouteSignal();
            }
            signal.LastObservedAt = now;
            signal.Pending = Math.Min(signal.Pending + 1, int.MaxValue - 1);
            var provider = ProviderLabel(attempt.Provider);
            if (signal.Providers.Count < 4) signal.Providers.Add(provider);
            else if (!signal.Providers.Contains(provider)) signal.MoreProviders = true;
        }
        _attemptBaseline = true;
        foreach (var (key, signal) in _routes.OrderBy(pair => pair.Value.LastObservedAt).ToArray())
        {
            if (now - signal.LastObservedAt > AttemptLookback)
            {
                signal.Pending = 0;
                signal.Providers.Clear();
                signal.MoreProviders = false;
                continue;
            }
            if (signal.Pending == 0 || signal.LastAlertAt is { } last && now - last < AlertCooldown || alerts.Count >= MaximumSignals) continue;
            var configured = key.Id is null ? null : configuredRoutes.FirstOrDefault(route => route.Id == key.Id);
            var route = key.Id is null ? "Unknown route" : configured is null
                ? "Route " + SafeLabel(key.Id, "unknown", 48) : SafeLabel(configured.Alias, "Unnamed route", 64);
            var providers = signal.Providers.Count == 0 ? "Unknown provider" : string.Join(", ", signal.Providers.Order(StringComparer.Ordinal));
            if (signal.MoreProviders) providers += ", others";
            alerts.Add(new DesktopAlert(AlertId("429", key.Id ?? "<no-route>", now), "HTTP 429 observed",
                $"{route} · {providers}: {signal.Pending:N0} observed HTTP 429 attempt{(signal.Pending == 1 ? "" : "s")}, including retries. See Usage for details."));
            signal.LastAlertAt = now;
            signal.Pending = 0;
            signal.Providers.Clear();
            signal.MoreProviders = false;
        }
    }

    private void ObserveQuotas(QuotaDto[] quotas, AccountDto[] accounts, DateTimeOffset now, List<DesktopAlert> alerts)
    {
        foreach (var group in QuotaGroups(quotas, accounts))
        {
            var fresh = group.Where(row => Fresh(row.Quota, now) && !string.IsNullOrWhiteSpace(row.Quota.WindowLabel) &&
                !string.IsNullOrWhiteSpace(row.Quota.Source) && row.Quota.Source != "unavailable" &&
                (row.Account is null || row.Account.Enabled) && (row.Quota.ResetsAt is not { } reset || reset > now))
                .OrderByDescending(row => row.Quota.LastSuccessAt).ThenBy(row => row.Quota.AccountId, StringComparer.Ordinal).FirstOrDefault();
            if (fresh is null || RemainingPercent(fresh.Quota) is not { } percent) continue;
            var quota = fresh.Quota;
            var low = percent <= 20;
            if (!_windows.TryGetValue(group.Key, out var signal))
            {
                EnsureSpace(_windows, MaximumWindows, entry => entry.LastObservedAt);
                _windows[group.Key] = new WindowSignal(low, quota.ResetsAt, now);
                continue; // First known observation of each window is a baseline, even if already low.
            }
            var resetCycle = signal.ResetAt is { } oldReset && oldReset <= now && quota.LastSuccessAt is { } success && success > oldReset &&
                quota.ResetsAt is { } nextReset && nextReset > oldReset;
            if ((!signal.Low || resetCycle) && low) signal.Pending = true;
            if (!low) signal.Pending = false;
            signal.Low = low;
            signal.ResetAt = quota.ResetsAt;
            signal.LastObservedAt = now;
            if (!signal.Pending || alerts.Count >= MaximumSignals || signal.LastAlertAt is { } last && now - last < AlertCooldown) continue;
            var names = group.Select(row => SafeLabel(row.Account?.Name ?? row.Quota.AccountId, "Unnamed account", 32))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(3);
            var shared = group.Key.Pool ? " · shared provider pool" : "";
            alerts.Add(new DesktopAlert(AlertId("quota", group.Key.ToString(), now), "Provider quota low",
                $"{string.Join(" / ", names)} · {ProviderLabel(fresh.Account?.Provider)} · {SafeLabel(quota.WindowLabel, "Quota window", 48)}: {percent:N0}% remaining{shared}. {ResetLabel(quota.ResetsAt)}."));
            signal.LastAlertAt = now;
            signal.Pending = false;
        }
    }

    private static IEnumerable<IGrouping<WindowKey, QuotaRow>> QuotaGroups(QuotaDto[] quotas, AccountDto[] accounts)
    {
        var names = accounts.GroupBy(account => account.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return quotas.Take(MaximumWindows).Select(quota =>
        {
            var account = names.GetValueOrDefault(quota.AccountId);
            var pool = !string.IsNullOrWhiteSpace(quota.BudgetPoolId);
            return new QuotaRow(quota, account, new WindowKey(IdentityKey(account?.Provider ?? ""), pool,
                IdentityKey(pool ? quota.BudgetPoolId! : quota.AccountId), IdentityKey(quota.Source), IdentityKey(quota.Unit)));
        }).GroupBy(row => row.Key);
    }

    private static void AppendQuotaLines(List<string> lines, QuotaDto[] quotas, AccountDto[] accounts, DateTimeOffset now)
    {
        var rows = QuotaGroups(quotas, accounts).SelectMany(group => group).OrderBy(row => row.Account?.Provider, StringComparer.Ordinal)
            .ThenBy(row => row.Account?.Name ?? row.Quota.AccountId, StringComparer.Ordinal).ThenBy(row => row.Quota.WindowLabel, StringComparer.Ordinal).ToArray();
        const int maximumLines = 12;
        foreach (var row in rows.Take(maximumLines))
        {
            var quota = row.Quota;
            var state = Fresh(quota, now) ? "reported" : quota.State == "ok" && quota.LastSuccessAt is { } success && success <= now ? "stale" : quota.State switch
            {
                "unsupported" => "unsupported", "disabled" => "disabled", "refreshing" => "refreshing",
                "stale" => "stale", _ => "unknown"
            };
            if (quota.ResetsAt is { } reset && reset <= now && quota.LastSuccessAt is not null &&
                state is "reported" or "stale" or "refreshing") state = "reset due · last known";
            var quantity = RemainingPercent(quota) is { } percent ? $"{percent:N0}% left" :
                Valid(quota.Remaining) is { } remaining ? $"{ShortNumber(remaining)} {SafeLabel(quota.Unit, "units", 8)} left (% unknown)" : "remaining unknown";
            if (state is "unsupported" or "disabled" or "unknown") quantity = "remaining unknown";
            lines.Add(SafeLabel($"{SafeLabel(row.Account?.Name ?? quota.AccountId, "Account", 18)} · {SafeLabel(ProviderLabel(row.Account?.Provider), "Provider", 20)} · {SafeLabel(quota.WindowLabel, "Unnamed window", 20)}: {quantity} · {state} · {ResetLabel(quota.ResetsAt)}", "Quota unknown", 160));
        }
        var unobserved = accounts.Where(account => !quotas.Any(quota => quota.AccountId == account.Id)).ToArray();
        foreach (var account in unobserved.Take(Math.Max(0, maximumLines - rows.Length)))
            lines.Add($"{SafeLabel(account.Name, "Account", 24)} · {ProviderLabel(account.Provider)}: quota unknown · reset unknown");
        if (rows.Length + unobserved.Length > maximumLines || quotas.Length > MaximumWindows)
            lines.Add("More account/windows available in Accounts · quota list bounded");
        if (accounts.Length == 0 && quotas.Length == 0) lines.Add("No configured accounts · quota unknown");
    }

    private void Trim(DateTimeOffset now)
    {
        foreach (var key in _seenAttempts.Where(pair => now - pair.Value > AttemptLookback + AlertCooldown).Select(pair => pair.Key).ToArray()) _seenAttempts.Remove(key);
        while (_seenAttempts.Count > MaximumSeenAttempts) _seenAttempts.Remove(_seenAttempts.MinBy(pair => pair.Value).Key);
        foreach (var key in _routes.Where(pair => now - pair.Value.LastObservedAt > TimeSpan.FromDays(1)).Select(pair => pair.Key).ToArray()) _routes.Remove(key);
        foreach (var key in _windows.Where(pair => now - pair.Value.LastObservedAt > TimeSpan.FromDays(1)).Select(pair => pair.Key).ToArray()) _windows.Remove(key);
    }

    private static void EnsureSpace<TKey, TValue>(Dictionary<TKey, TValue> values, int maximum, Func<TValue, DateTimeOffset> observed) where TKey : notnull
    {
        if (values.Count >= maximum) values.Remove(values.MinBy(pair => observed(pair.Value)).Key);
    }

    private static bool Fresh(QuotaDto quota, DateTimeOffset now) => quota.State == "ok" && quota.LastSuccessAt is { } success &&
        success <= now + TimeSpan.FromSeconds(30) && now - success < QuotaFreshness;

    private static double? RemainingPercent(QuotaDto quota)
    {
        if (quota.UsedPercent is { } used) return double.IsFinite(used) && used is >= 0 and <= 100 ? 100 - used : null;
        var remaining = Valid(quota.Remaining);
        var limit = Valid(quota.Limit);
        if (remaining is { } amount && limit is { } maximum && maximum > 0 && amount <= maximum) return amount / maximum * 100;
        return null;
    }

    private static double? Valid(double? value) => value is { } number && double.IsFinite(number) && number >= 0 ? number : null;
    private static string ShortNumber(double value) => value >= 1_000_000 ? value.ToString("0.##E+0", CultureInfo.CurrentCulture) : value.ToString("N0", CultureInfo.CurrentCulture);
    private static string ResetLabel(DateTimeOffset? reset) => reset is { } value
        ? "reported reset " + value.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture) + " local" : "reset unknown";

    private static string ProviderLabel(string? provider) => provider?.ToLowerInvariant() switch
    {
        "codex" => "OpenAI Codex", "commandcode" => "Command Code", "zai" => "Z.ai", "kimi" => "Kimi",
        "xiaomi-mimo" => "Xiaomi MiMo", "openai" => "OpenAI", "anthropic" => "Anthropic", "google" => "Google",
        _ => SafeLabel(provider, "Unknown provider", 24)
    };

    private static string SafeLabel(string? value, string fallback, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("://", StringComparison.Ordinal) || value.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)) return fallback;
        var result = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format || char.IsWhiteSpace(character))
            {
                if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
            }
            else result.Append(character);
            if (result.Length >= maximum) break;
        }
        if (result.Length > 0 && char.IsHighSurrogate(result[^1])) result.Length--;
        return result.Length == 0 ? fallback : result.ToString().Trim();
    }

    private static string AlertId(string kind, string key, DateTimeOffset now) => kind + ":" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16] + ":" + now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    private static string IdentityKey(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task<T?> ReadAsync<T>(Func<Task<T>> read, CancellationToken ct) where T : class
    {
        try { return await read().ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return null; }
    }

    private sealed record AttemptScan(IReadOnlyList<AttemptDto> Items, bool Truncated);
    private sealed record AttemptKey(string? Source, string Id);
    private sealed record RouteKey(string? Id);
    private sealed class RouteSignal
    {
        public DateTimeOffset LastObservedAt;
        public DateTimeOffset? LastAlertAt;
        public int Pending;
        public bool MoreProviders;
        public HashSet<string> Providers { get; } = new(StringComparer.Ordinal);
    }
    private sealed record WindowKey(string Provider, bool Pool, string Identity, string Source, string Unit);
    private sealed record QuotaRow(QuotaDto Quota, AccountDto? Account, WindowKey Key);
    private sealed class WindowSignal(bool low, DateTimeOffset? resetAt, DateTimeOffset observedAt)
    {
        public bool Low = low;
        public bool Pending;
        public DateTimeOffset? ResetAt = resetAt;
        public DateTimeOffset LastObservedAt = observedAt;
        public DateTimeOffset? LastAlertAt;
    }
}
