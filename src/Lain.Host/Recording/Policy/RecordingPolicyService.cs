using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Recording.Policy;

/// <summary>
/// Persisted capture choice with optional first-install content initialization. Snapshot publication and admission stop share one lock. Existing bounded
/// writes that passed their final gate may complete; queued observations from prior generations fail admission.
/// No inference configuration, Apply operation, training eligibility or display state is consulted.
/// </summary>
public sealed class RecordingPolicyService : IRecordingPolicy
{
    private readonly object _gate = new();
    private readonly string? _directory;
    private readonly RecordingSanitizer _sanitizer = new();
    private CapturePolicySnapshot _snapshot = new(IdentityScope: Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string? LastErrorCode { get; private set; }

    /// <param name="directory">Dedicated private recording directory; null provides an ephemeral policy for tests.</param>
    public RecordingPolicyService(string? directory = null, bool initializeContentCapture = false)
    {
        _directory = directory is null ? null : Path.GetFullPath(directory);
        if (_directory is null) return;
        try
        {
            EnsurePrivateDirectory(_directory);
            var path = Path.Combine(_directory, "policy.json");
            RejectLink(path);
            if (!File.Exists(path))
            {
                // Only an absent policy receives the installation default. Explicit stops and invalid
                // existing policies must never be turned back on by startup or a UI refresh.
                if (initializeContentCapture)
                {
                    var initial = _snapshot with { Revision = 1, Enabled = true,
                        Mode = RecordingContract.Content, StopGeneration = 1 };
                    Save(initial);
                    _snapshot = initial;
                }
                return;
            }
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024) { LastErrorCode = "policy_file_invalid"; return; }
            var loaded = JsonSerializer.Deserialize<CapturePolicySnapshot>(File.ReadAllBytes(path), Json);
            if (loaded is null || Validate(loaded.Mode, loaded.Scope, loaded.RouteIds, loaded.EffectiveLimits) is not null ||
                loaded.Revision < 0 || loaded.StopGeneration < 0 ||
                loaded.IdentityScope is not null && !Guid.TryParseExact(loaded.IdentityScope, "N", out _))
            { LastErrorCode = "policy_file_invalid"; return; }
            _snapshot = Copy(loaded with { IdentityScope = loaded.IdentityScope ?? _snapshot.IdentityScope });
            PrivateFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { LastErrorCode = "policy_file_unavailable"; }
    }

    public CapturePolicySnapshot Snapshot { get { lock (_gate) return Copy(_snapshot); } }

    public bool IsCurrent(long revision, long stopGeneration)
    {
        lock (_gate) return _snapshot.Enabled && _snapshot.Revision == revision && _snapshot.StopGeneration == stopGeneration;
    }

    public RecordingPolicyResult Update(RecordingPolicyUpdate update)
    {
        lock (_gate)
        {
            if (update.ExpectedRevision != _snapshot.Revision) return new(false, Copy(_snapshot), "stale_revision");
            var limits = update.Limits ?? _snapshot.EffectiveLimits;
            var error = Validate(update.Mode, update.Scope, update.RouteIds, limits);
            if (error is not null) return new(false, Copy(_snapshot), error);
            if (_snapshot.Revision == long.MaxValue || _snapshot.StopGeneration == long.MaxValue)
                return new(false, Copy(_snapshot), "revision_exhausted");
            var next = new CapturePolicySnapshot(_snapshot.Revision + 1, update.Enabled, update.Mode,
                update.Scope, _snapshot.StopGeneration + 1, update.RouteIds?.ToArray(), limits, _snapshot.IdentityScope);
            // Disabling is immediate even if the subsequent control-file write fails.
            if (!next.Enabled) _snapshot = next;
            try { Save(next); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                LastErrorCode = "policy_save_failed";
                return new(false, Copy(_snapshot), LastErrorCode);
            }
            _snapshot = next;
            LastErrorCode = null;
            return new(true, Copy(_snapshot));
        }
    }

    public SanitizedObservation? Sanitize(SourceObservation observation, CapturePolicySnapshot policy)
    {
        if (!IsCurrent(policy.Revision, policy.StopGeneration)) return null;
        SanitizedObservation? retained;
        try { retained = _sanitizer.Sanitize(observation, policy); }
        catch (Exception e) when (e is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { return null; }
        // A stop/scope revision arriving during redaction invalidates the completed copy.
        return IsCurrent(policy.Revision, policy.StopGeneration) ? retained : null;
    }

    private static string? Validate(string mode, string scope, string[]? routeIds, RecordingLimitsDto limits)
    {
        if (mode is not (RecordingContract.MetadataOnly or RecordingContract.Content)) return "unsupported_mode";
        if (scope != RecordingContract.OwnedGatewayScope) return "unsupported_scope";
        if (routeIds is { Length: > 256 } || routeIds?.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 256 ||
            id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':' or '/'))) == true)
            return "invalid_route_filter";
        if (routeIds is not null && routeIds.Distinct(StringComparer.Ordinal).Count() != routeIds.Length)
            return "duplicate_route_filter";
        if (limits.MaxRecordBytes is < 32768 or > 16 * 1024 * 1024 ||
            limits.QueueBytes < 2L * limits.MaxRecordBytes + 8192 || limits.QueueBytes > 256L * 1024 * 1024 ||
            limits.DiskBytes < limits.MaxRecordBytes || limits.DiskBytes > 1024L * 1024 * 1024 * 1024 ||
            limits.ContentRetentionDays is < 1 or > 3650 ||
            limits.MetadataRetentionDays < limits.ContentRetentionDays || limits.MetadataRetentionDays > 3650 ||
            limits.MaxPageSize is < 1 or > 500 || limits.MaxBodyDepth is < 4 or > 128) return "invalid_limits";
        return null;
    }

    private void Save(CapturePolicySnapshot policy)
    {
        if (_directory is null) return;
        EnsurePrivateDirectory(_directory);
        var path = Path.Combine(_directory, "policy.json");
        RejectLink(path);
        var temporary = Path.Combine(_directory, ".policy-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                Share = FileShare.None, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, policy, Json);
                stream.Flush(flushToDisk: true);
            }
            RejectLink(path);
            File.Move(temporary, path, overwrite: true);
            PrivateFile(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static CapturePolicySnapshot Copy(CapturePolicySnapshot source) => source with { RouteIds = source.RouteIds?.ToArray() };
    private static void EnsurePrivateDirectory(string path)
    {
        // Reject links in existing ancestors before creating files beneath this path.
        var current = new DirectoryInfo(path);
        for (var ancestor = current; ancestor is not null; ancestor = ancestor.Parent) RejectLink(ancestor.FullName);
        Directory.CreateDirectory(path);
        RejectLink(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("recording_path_link");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
    private static void PrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
