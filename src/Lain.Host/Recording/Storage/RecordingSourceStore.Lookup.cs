using Lain.Core;
namespace Lain.Host.Recording.Storage;
public sealed partial class RecordingSourceStore : IRecordingSourceLookup, IRecordingMaintenance, IRecordingSourceCheckpoint
{
    public async Task<long> CommittedSequenceAsync(string sourceEpoch, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { return identities.Keys.Where(k => k.Epoch == sourceEpoch).Select(k => k.Sequence).DefaultIfEmpty().Max(); }
        finally { gate.Release(); }
    }
    public async Task<CommittedObservation?> FindSourceAsync(RecordingSourceReference source, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!identities.TryGetValue((source.SourceEpoch, source.Sequence), out var location) ||
                location.Record.Metadata.EventId != source.EventId || IsHidden(location.Record)) return null;
            return Materialize(location);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
        { throw new IOException("recording_source_unavailable"); }
        finally { gate.Release(); }
    }
}
