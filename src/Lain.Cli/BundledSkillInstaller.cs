namespace Lain.Cli;

/// <summary>Embedded package resources only; no downloads or overwriting project skills.</summary>
public static class BundledSkillInstaller
{
    private const string Prefix = "Lain.Cli.BundledSkills/";
    public static string[] Names => typeof(BundledSkillInstaller).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith("/SKILL.md", StringComparison.Ordinal))
        .Select(name => name[Prefix.Length..^"/SKILL.md".Length])
        .Where(ValidName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static string Install(string name, string project)
    {
        if (!ValidName(name) || !Names.Contains(name, StringComparer.Ordinal))
            throw new ArgumentException("Unknown bundled skill. Run 'lain skills list' for package contents.");
        var directory = WorkerProjectFiles.ProjectRoot(project);
        foreach (var component in new[] { ".agents", "skills" })
        {
            directory = Path.Combine(directory, component);
            WorkerProjectFiles.AssertRegular(directory);
        }
        var target = Path.Combine(directory, name);
        WorkerProjectFiles.AssertRegular(target);
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException("The project skill already exists; preserve and review its local changes before updating it.");
        var assembly = typeof(BundledSkillInstaller).Assembly;
        var resourcePrefix = Prefix + name + "/";
        var resources = assembly.GetManifestResourceNames().Where(resource => resource.StartsWith(resourcePrefix, StringComparison.Ordinal)).ToArray();
        WorkerProjectFiles.PrivateDirectory(directory);
        var staging = Path.Combine(directory, "." + name + "-" + Guid.NewGuid().ToString("N"));
        WorkerProjectFiles.PrivateDirectory(staging);
        try
        {
            foreach (var resource in resources)
            {
                var relative = resource[resourcePrefix.Length..].Replace('\\', '/');
                if (relative.Split('/').Any(segment => segment is "" or "." or "..") || Path.IsPathRooted(relative))
                    throw new IOException("Invalid bundled skill path.");
                var destination = Path.Combine(staging, relative);
                WorkerProjectFiles.PrivateDirectory(Path.GetDirectoryName(destination)!);
                using var input = assembly.GetManifestResourceStream(resource) ?? throw new IOException("Missing bundled skill resource.");
                using var output = WorkerProjectFiles.CreatePrivate(destination);
                input.CopyTo(output);
            }
            Directory.Move(staging, target);
            return target;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private static bool ValidName(string name) => name.Length is > 0 and <= 64 &&
        name[0] is >= 'a' and <= 'z' && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
