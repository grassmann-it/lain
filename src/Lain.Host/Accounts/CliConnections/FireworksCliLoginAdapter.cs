namespace Lain.Host.Accounts.CliConnections;
public sealed class FireworksCliLoginAdapter : UnavailableCliLoginAdapter
{
    public override string Provider => "fireworks";
    public override string QualificationBlocker => "firectl signin isolation is unqualified; a later --profile does not prove it. Standard Serverless key output and non-Fire-Pass type are also unqualified. Use an existing matching API key; no signin or key creation is started.";
}
