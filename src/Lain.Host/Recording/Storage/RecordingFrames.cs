using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Recording.Storage;

internal sealed record StoredObservation(SanitizedObservation Metadata, long Cursor, string ContentHash,
    string? PayloadFile, int PayloadBytes, string? PayloadHash, long OriginalBytes);
internal sealed record ArchiveControl(int Version, string Generation, long HighWatermark = 0,
    long InvalidatedThrough = 0, string[]? HiddenCaptures = null, bool PurgePending = false, bool ClearPending = false, string[]? HiddenCaptureHashes = null);
internal sealed record StoredLocation(StoredObservation Record, string Journal, long Offset);

internal static class RecordingFrames
{
    public const int HeaderSize = 40;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 96 };
    private const uint Magic = 0x31434552; // REC1

    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        using var result = new MemoryStream();
        using (var compressor = new BrotliStream(result, CompressionLevel.Fastest, true)) compressor.Write(input);
        return result.ToArray();
    }

    public static byte[] Decompress(Stream input, int maximum, CancellationToken ct = default)
    {
        using var decompressor = new BrotliStream(input, CompressionMode.Decompress, true);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while (true) {
            ct.ThrowIfCancellationRequested();
            count = decompressor.Read(buffer);
            if (count == 0) break;
            if (output.Length + count > maximum) throw new InvalidDataException("Recording expansion limit exceeded.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public static byte[] Encode(StoredObservation observation)
    {
        var body = Compress(JsonSerializer.SerializeToUtf8Bytes(observation, Json));
        var frame = new byte[HeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        SHA256.HashData(body).CopyTo(frame.AsSpan(8));
        body.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }

    // A partial header/body is a torn append only on the final active segment.
    // A complete frame with a bad checksum is corruption even at the active tail.
    public static StoredObservation? Read(FileStream input, int maximum, out bool torn, CancellationToken ct = default)
    {
        torn = false;
        ct.ThrowIfCancellationRequested();
        var header = new byte[HeaderSize];
        var count = input.ReadAtLeast(header, HeaderSize, false);
        if (count == 0) return null;
        if (count != HeaderSize) { torn = true; return null; }
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic || length <= 0 || length > maximum + 65536)
            throw new InvalidDataException("Recording frame header is corrupt or unsupported.");
        var bytes = new byte[length];
        if (input.ReadAtLeast(bytes, length, false) != length) { torn = true; return null; }
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), header.AsSpan(8, 32)))
            throw new InvalidDataException("Recording frame checksum mismatch.");
        using var body = new MemoryStream(bytes, false);
        var item = JsonSerializer.Deserialize<StoredObservation>(Decompress(body, maximum + 65536, ct), Json)
            ?? throw new InvalidDataException("Recording frame is invalid.");
        if (item.Metadata.SchemaVersion != RecordingContract.Version || item.Cursor <= 0)
            throw new InvalidDataException("Recording frame schema is unsupported.");
        return item;
    }

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
