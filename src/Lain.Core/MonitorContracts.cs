namespace Lain.Core;

/// <summary>Public setup guidance; never contains authentication material.</summary>
public sealed record MonitorAccessOptionDto(string Kind, string Name, bool SecretRequired,
    string ResourceLabel, string ResourceHelp, string DocumentationUrl, string[] Scopes,
    string? Description = null);

/// <summary>Sent only to the authenticated local management API. Never log this request.</summary>
public sealed record MonitorAccessRequest(string Kind, string? Secret = null,
    string? ResourceId = null, DateTimeOffset? ExpiresAt = null)
{
    public override string ToString() => "MonitorAccessRequest [redacted]";
}

public sealed record MonitorAccessDto(string AccountId, string Provider, string State,
    string? Kind = null, string? ResourceId = null, DateTimeOffset? ExpiresAt = null,
    string? Guidance = null, string? DocumentationUrl = null);
