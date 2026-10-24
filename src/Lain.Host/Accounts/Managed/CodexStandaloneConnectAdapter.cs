namespace Lain.Host.Accounts.Managed;

/// <summary>Separate candidate grant only. Existing Core-owned credentials and external snapshots remain untouched.</summary>
public sealed class CodexStandaloneConnectAdapter() : UnqualifiedManagedLoginAdapter(ManagedLoginQualifications.Codex) { }
