namespace Lain.Host.Accounts.Managed;

/// <summary>No official client identity is borrowed and no resolver with implicit key creation is invoked.</summary>
public sealed class ZaiConnectAdapter() : UnqualifiedManagedLoginAdapter(ManagedLoginQualifications.Zai) { }
