namespace Lain.Host.Accounts.CliConnections;
public sealed class CommandCodeCliLoginAdapter : UnavailableCliLoginAdapter
{
    public override string Provider => "commandcode";
    public override string QualificationBlocker => "Command Code login has no qualified executable version, supported owned cache isolation or targeted private key export. Use an existing Studio API key; no existing CLI cache is imported.";
}
