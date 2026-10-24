using System.Text.Json;
using Lain.Host.Quota;

static void Check(bool value, string name) { if (!value) throw new Exception(name); }
var fixture = File.ReadAllText("tests/nvidia-quota/fixtures/public-limits.json");
using var raw = JsonDocument.Parse(fixture);
var limits = NvidiaBuildPublicReference.ReadPublicLimits(raw.RootElement);
Check(limits is { RequestsPerMinute: "Up to 40 rpm", RequestsPerDay: "10,000 requests per day" }, "native reference wording preserved");
var support = NvidiaBuildPublicReference.Support();
Check(support.Kind == "console" && support.CredentialPurpose is null && support.ActionUrl == NvidiaBuildPublicReference.ConsoleUrl,
    "reference links to console without offering a setup or connection method");
Check(support.Message.Contains(NvidiaBuildPublicReference.ReviewedSourceDate) && support.Message.Contains("unknown")
    && support.Message.Contains("No supported"), "dated reference cannot imply measured account reporting or working login");
foreach (var bad in new[] { "{}", "{\"remainingCredits\":12345}", fixture.Replace("Up to 40 rpm", "<script>private</script>"), fixture.Replace("10,000 requests per day", new string('x', 65)) })
{
    using var document = JsonDocument.Parse(bad);
    Check(NvidiaBuildPublicReference.ReadPublicLimits(document.RootElement) is null, "unqualified values and unrelated credits excluded");
}
Console.WriteLine("NVIDIA published-reference checks passed.");
