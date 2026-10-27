namespace Lain.Core;

/// <summary>Bounded Host-owned startup diagnostics. Reasons never contain Core output, paths or credentials.</summary>
public record RuntimeLifecycleDto(bool AutomaticStartSuppressed, string Stage, string? Reason = null,
    DateTimeOffset? StartedAt = null, DateTimeOffset? UpdatedAt = null, int? ExitCode = null,
    DateTimeOffset? CompletedAt = null);
