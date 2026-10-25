using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

// Read PE metadata only. Never load/execute inspected application assemblies.
var results = new Dictionary<string, object>();
foreach (var arg in args)
{
    var pair = arg.Split('=', 2);
    if (pair.Length != 2 || pair[0] is not ("cli" or "host" or "desktop")) throw new ArgumentException("Expected cli=/absolute/DLL, host=/absolute/DLL and desktop=/absolute/DLL");
    var path = Path.GetFullPath(pair[1]);
    var bytes = File.ReadAllBytes(path);
    using var stream = new MemoryStream(bytes, writable: false);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    var values = new List<string>();
    foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
    {
        var attribute = metadata.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
        var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (member.Parent.Kind != HandleKind.TypeReference) continue;
        var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        if (metadata.GetString(type.Namespace) != "System.Reflection" ||
            metadata.GetString(type.Name) != "AssemblyInformationalVersionAttribute") continue;
        var blob = metadata.GetBlobReader(attribute.Value);
        if (blob.ReadUInt16() != 1) throw new InvalidDataException("Invalid attribute prolog");
        var value = blob.ReadSerializedString() ?? throw new InvalidDataException("Null informational version");
        if (blob.ReadUInt16() != 0 || blob.RemainingBytes != 0) throw new InvalidDataException("Unexpected named attribute arguments");
        values.Add(value);
    }
    if (values.Count != 1) throw new InvalidDataException("Expected exactly one informational version attribute");
    results.Add(pair[0], new { method = "assembly-metadata-reader",
        attribute = "System.Reflection.AssemblyInformationalVersionAttribute",
        dllSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), informationalVersion = values[0], passed = true });
}
if (results.Count != 3) throw new ArgumentException("All three exact component assemblies required");
Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
