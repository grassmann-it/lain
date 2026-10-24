namespace Lain.Host.Accounts.Managed;

/// <summary>Own toolkit admission and delegated CLI isolation are separate, outstanding decisions.</summary>
public sealed class KimiConnectAdapter() : UnqualifiedManagedLoginAdapter(ManagedLoginQualifications.Kimi) { }
