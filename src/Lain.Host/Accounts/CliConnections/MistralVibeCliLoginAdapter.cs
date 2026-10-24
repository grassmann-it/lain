namespace Lain.Host.Accounts.CliConnections;
public sealed class MistralVibeCliLoginAdapter : UnavailableCliLoginAdapter
{
    public override string Provider => "mistral";
    public override string QualificationBlocker => "Mistral Vibe owned keyring/.env isolation, targeted key handoff and external Studio product/workspace authorization are unqualified. Use an existing Studio API key; Vibe plan identity is not remaining allowance.";
}
