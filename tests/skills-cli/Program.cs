using System.Text.Json;
using Lain.Cli;
using Lain.Client;

var assertions = 0;
void Require(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "lain-skills-tests", Guid.NewGuid().ToString("N"));
WorkerProjectFiles.PrivateDirectory(root);
var project = Path.Combine(root, "project ü with spaces");
Directory.CreateDirectory(project);
var hostCalls = 0;
LainApiClient NoHost() { hostCalls++; throw new Exception("Skill commands must work without Host discovery."); }
async Task<(int Code, string Out, string Error)> Cli(params string[] args)
{
    using var output = new StringWriter(); using var error = new StringWriter();
    var code = await CliApplication.RunAsync(args, output, error, NoHost);
    return (code, output.ToString(), error.ToString());
}
var expected = new[] { "lain-connect", "lain-doctor", "lain-usage", "lain-workers" };
var listed = await Cli("skills", "list", "--json");
Require(listed.Code == 0 && listed.Error == "", "list works without a Host");
using (var document = JsonDocument.Parse(listed.Out))
{
    Require(document.RootElement.GetProperty("schemaVersion").GetInt32() == 1, "versioned JSON list");
    Require(document.RootElement.GetProperty("skills").EnumerateArray().Select(x => x.GetString()).SequenceEqual(expected), "all four packaged skills advertised");
}
Require((await Cli("skills", "help")).Code == 0, "local help works");
foreach (var name in expected)
{
    var installed = await Cli("skills", "install", name, "--project", project, "--json");
    Require(installed.Code == 0 && installed.Error == "", "skill installed from package: " + name);
    var target = Path.Combine(project, ".agents", "skills", name, "SKILL.md");
    var assembly = typeof(BundledSkillInstaller).Assembly;
    var prefix = "Lain.Cli.BundledSkills/" + name + "/";
    foreach (var resource in assembly.GetManifestResourceNames().Where(x => x.StartsWith(prefix, StringComparison.Ordinal)))
    {
        using var embedded = assembly.GetManifestResourceStream(resource)!;
        using var bytes = new MemoryStream(); embedded.CopyTo(bytes);
        Require(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(target)!, resource[prefix.Length..])).SequenceEqual(bytes.ToArray()), "all bundled resource bytes installed: " + resource);
    }
    var original = File.ReadAllText(target);
    Require(original.Contains("name: " + name, StringComparison.Ordinal), "actual skill identity installed: " + name);
    if (!OperatingSystem.IsWindows())
        Require((File.GetUnixFileMode(target) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) == 0, "installed skill file is private");
    File.WriteAllText(target, original + "\nLocal project edit.\n");
    Require((await Cli("skills", "install", name, "--project", project)).Code == 3 && File.ReadAllText(target) == original + "\nLocal project edit.\n", "existing project edits preserved: " + name);
}
var legacyProject = Path.Combine(root, "legacy"); Directory.CreateDirectory(legacyProject);
Require((await Cli("workers", "install-skill", "--project", legacyProject)).Code == 0, "legacy worker install remains compatible");
Require(File.ReadAllText(Path.Combine(legacyProject, ".agents/skills/lain-workers/SKILL.md")).Contains("name: lain-workers"), "legacy installs actual worker resource");
foreach (var name in new[] { "unknown", "../lain-workers", "/tmp/escape", "lain-connect/x" })
    Require((await Cli("skills", "install", name, "--project", project)).Code == 2, "non-package name refused: " + name);
foreach (var arguments in new[] { new[] { "skills", "install", "lain-connect" }, new[] { "skills", "list", "--project", project }, new[] { "skills", "install", "lain-connect", "extra", "--project", project } })
    Require((await Cli(arguments)).Code == 2, "strict command arguments");
if (!OperatingSystem.IsWindows())
{
    var linkedProject = Path.Combine(root, "linked"); Directory.CreateDirectory(linkedProject);
    Directory.CreateSymbolicLink(Path.Combine(linkedProject, ".agents"), Path.Combine(project, ".agents"));
    Require((await Cli("skills", "install", "lain-connect", "--project", linkedProject)).Code == 3, "symlink ancestor refused");
    var leafProject = Path.Combine(root, "leaf"); Directory.CreateDirectory(Path.Combine(leafProject, ".agents/skills"));
    Directory.CreateSymbolicLink(Path.Combine(leafProject, ".agents/skills/lain-connect"), Path.Combine(project, ".agents/skills/lain-connect"));
    Require((await Cli("skills", "install", "lain-connect", "--project", leafProject)).Code == 3, "symlink target refused");
}
Require(hostCalls == 0, "all skill operations avoid Host, credentials and provider discovery");
Require(!Directory.Exists(Path.Combine(project, ".lain")), "skill installation creates no worker policy or route permissions");
Require(!Directory.EnumerateDirectories(Path.Combine(project, ".agents/skills"), ".*").Any(), "staging directories are not retained after installation");
Console.WriteLine($"skills-cli: {assertions} assertions passed; private fixture: {root}");
