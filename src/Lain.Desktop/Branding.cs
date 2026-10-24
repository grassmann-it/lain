using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Lain.Desktop;

/// <summary>The original user-supplied artwork, decoded once and scaled by the UI.</summary>
internal static class Branding
{
    private static readonly Lazy<Bitmap> Artwork = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://Lain.Desktop/Assets/Brand/lain-cat.png"));
        return new Bitmap(stream);
    });

    public static IImage Logo => Artwork.Value;
    public static WindowIcon WindowIcon { get; } = new(Artwork.Value);
}
