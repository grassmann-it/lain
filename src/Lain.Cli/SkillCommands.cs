namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task<int> Skills(Options options, TextWriter output)
    {
        if (options.Positionals.Count == 0 || options.Positionals.SequenceEqual(new[] { "help" }))
        {
            options.RequireOnly();
            await output.WriteLineAsync("Usage: lain skills list [--json] | install NAME --project DIR [--json]\nInstalls only bundled skills into .agents/skills; existing targets are preserved. No Host connection or profile changes.");
            return 0;
        }
        if (options.Positionals[0] == "list")
        {
            options.RequirePositionals(1); options.RequireOnly("json");
            if (options.Json) await Write(output, new { schemaVersion = 1, skills = BundledSkillInstaller.Names }, true);
            else foreach (var name in BundledSkillInstaller.Names) await output.WriteLineAsync(name);
            return 0;
        }
        if (options.Positionals[0] != "install") throw new ArgumentException("Use skills list or skills install NAME --project DIR.");
        options.RequirePositionals(2); options.RequireOnly("project", "json");
        var project = options.Get("project") ?? throw new ArgumentException("--project is required for skill installation.");
        var path = BundledSkillInstaller.Install(options.Positionals[1], project);
        await Write(output, new { schemaVersion = 1, state = "installed", skill = options.Positionals[1], path,
            nextAction = "Discover the skill in the client; start a new session if necessary. No profile was applied." }, options.Json);
        return 0;
    }
}
