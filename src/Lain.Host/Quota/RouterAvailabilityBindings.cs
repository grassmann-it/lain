using System.Security.Cryptography;
using System.Text.Json;

namespace Lain.Host.Quota;

/// <summary>A bounded projection of the owned generated runtime descriptors, never proxy auth/config files.</summary>
public sealed record RouterAvailabilityBindings(string Fingerprint, RouterAvailabilityMember[] Members)
{
    public const int MaximumBytes = 4 * 1024 * 1024;

    public static async Task<RouterAvailabilityBindings> ReadAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await ReadBoundedAsync(stream, MaximumBytes, ct);
        return Parse(bytes);
    }

    public static RouterAvailabilityBindings Parse(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Router descriptors exceed the size bound.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var routes = JsonFields.Object(document.RootElement, "routes");
        var routers = JsonFields.Object(document.RootElement, "routers");
        var members = new List<RouterAvailabilityMember>();
        if (routes.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Missing router route descriptors.");
        if (routers.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new(Convert.ToHexString(SHA256.HashData(bytes)), []);
        if (routers.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid router descriptors.");
        var byId = routes.EnumerateObject().ToDictionary(p => Required(p.Value, "id"), p => p.Value, StringComparer.Ordinal);
        foreach (var routerProperty in routers.EnumerateObject())
        {
            var router = routerProperty.Value;
            if (Required(router, "selection") != "ordered_sticky_v1") continue;
            var id = Required(router, "id");
            var profile = Required(router, "profileId");
            var ids = JsonFields.Object(router, "routeIds");
            var fingerprints = JsonFields.Object(router, "memberFingerprints");
            if (ids.ValueKind != JsonValueKind.Array || fingerprints.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid router membership.");
            foreach (var routeIdValue in ids.EnumerateArray())
            {
                var routeId = routeIdValue.GetString() ?? throw new InvalidDataException("Invalid router member identity.");
                if (!byId.TryGetValue(routeId, out var route)) throw new InvalidDataException("Missing router member.");
                var accounts = JsonFields.Object(route, "accounts");
                if (Required(route, "selection") != "strict" || accounts.ValueKind != JsonValueKind.Array || accounts.GetArrayLength() != 1 ||
                    JsonFields.Object(route, "visible").ValueKind == JsonValueKind.False ||
                    Required(route, "upstreamProtocol") is not ("openai" or "responses")) continue;
                var fingerprint = Required(fingerprints, routeId);
                if (fingerprint.Length != 64 || !fingerprint.All(char.IsAsciiHexDigit))
                    throw new InvalidDataException("Invalid router member fingerprint.");
                members.Add(new(id, profile, routeId, fingerprint, accounts[0].GetString()!, Required(route, "provider"), Required(route, "model")));
                if (members.Count > RouterAvailabilityProjector.MaximumEntries)
                    throw new InvalidDataException("Too many router descriptors.");
            }
        }
        return new(Convert.ToHexString(SHA256.HashData(bytes)), members.Distinct().ToArray());
    }

    private static string Required(JsonElement value, string key) => JsonFields.String(value, key) is { Length: > 0 and <= 512 } text &&
        !text.Any(char.IsControl) ? text : throw new InvalidDataException("Invalid router descriptor field.");

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + count > maximumBytes) throw new InvalidDataException("Router availability size bound exceeded.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
