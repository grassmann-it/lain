namespace Lain.Desktop.Presentation;

public enum InformationLevel { Barebones, Simple, Advanced }
public enum ViewDensity { Normal, Compact }

/// <summary>Presentation only; never changes provider or proxy configuration.</summary>
public sealed record PresentationOptions(InformationLevel Level = InformationLevel.Simple,
    ViewDensity Density = ViewDensity.Normal);

public sealed record WorkspacePreferences
{
    public int Version { get; init; } = 1;
    public InformationLevel Level { get; init; } = InformationLevel.Simple;
    public Dictionary<string, ViewDensity> Densities { get; init; } = new(StringComparer.Ordinal);
    public string AccountSort { get; init; } = "favorites";
    public bool GroupAccountsByProvider { get; init; }
    public string[] PinnedAccountIds { get; init; } = [];
    public bool MinimizeToTray { get; init; } = true;
    public bool CloseToTray { get; init; }
    public bool NotificationsEnabled { get; init; } = true;
}

/// <summary>Typed targets preserve identity through contextual navigation.</summary>
public sealed record WorkspaceNavigationRequest(string Section, string? AccountId = null,
    string? RouteId = null, string? Model = null);

public interface IPresentationView
{
    void SetPresentation(PresentationOptions options);
}
