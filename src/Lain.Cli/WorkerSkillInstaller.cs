namespace Lain.Cli;

/// <summary>Compatibility entry point for the project-local worker skill.</summary>
public static class WorkerSkillInstaller
{
    public static string Install(string project) => BundledSkillInstaller.Install("lain-workers", project);
}
