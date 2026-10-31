using System.Text;

namespace Lain.Host.Recording.Storage;

/// <summary>The requested deletion was rejected before changing durable or visible state.</summary>
internal sealed class RecordingDeletionCapacityException() : IOException("recording_deletion_capacity") { }

public sealed partial class RecordingSourceStore
{
    // Version 2 stores fixed-size SHA-256 capture tombstones. Old readers must refuse
    // this version rather than ignore hashes and permit replay of deleted captures.
    private const int ArchiveControlVersion = 2;
    private const int MaximumDeletionTombstones = 100_000;
    private const int MaximumControlBytes = 8 * 1024 * 1024; // 100k * 67 JSON bytes + bounded manifest fields.
    private int DeletionCapacity => Math.Min(options.MaximumRecords, MaximumDeletionTombstones);

    private static string CaptureTombstone(string capture) => RecordingFrames.Hash(Encoding.UTF8.GetBytes(capture));
    private bool IsCaptureHidden(string capture) => hiddenCaptures.Contains(CaptureTombstone(capture));

    private void UpgradeDeletionControl()
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in control.HiddenCaptureHashes ?? []) {
            if (value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c)))
                throw new InvalidDataException("Recording deletion control is invalid.");
            hashes.Add(value.ToUpperInvariant());
        }
        // Legacy IDs are converted before any control write. No raw-ID array grows
        // with new deletions; the in-memory membership check also uses fixed hashes.
        foreach (var capture in control.HiddenCaptures ?? []) hashes.Add(CaptureTombstone(capture));
        if (hashes.Count > MaximumDeletionTombstones)
            throw new InvalidDataException("Recording deletion control exceeds its bound.");
        control = control with { Version = ArchiveControlVersion, HiddenCaptures = null, HiddenCaptureHashes = hashes.ToArray() };
    }
}
