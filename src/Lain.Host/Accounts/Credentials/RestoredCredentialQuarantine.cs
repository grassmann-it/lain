using System.Text.Json;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Credentials;

/// <summary>Quarantine every restored core-owned grant before the core can enumerate its auth directory.</summary>
public static class RestoredCredentialQuarantine
{
    public static void PrepareForRestore(OwnStatePaths paths)
    {
        var epoch = new CredentialGenerationStore(paths).RestoreEpoch;
        if (epoch is null) return;
        var directory = Path.Combine(paths.Credentials, "quarantine", epoch);
        OwnStatePaths.EnsurePrivateDirectory(directory);
        var complete = Path.Combine(directory, "prepared.json");
        CredentialGenerationStore.RefuseLink(complete);
        if (File.Exists(complete))
        {
            OwnStatePaths.EnsurePrivateDirectory(paths.OwnedAuth);
            return;
        }
        var quarantine = Path.Combine(directory, "auth-owned");
        // Rename is atomic. On restart its presence proves the old directory was already moved.
        if (!Directory.Exists(quarantine))
        {
            OwnStatePaths.EnsurePrivateDirectory(paths.OwnedAuth);
            Directory.Move(paths.OwnedAuth, quarantine);
        }
        else
        {
            OwnStatePaths.EnsurePrivateDirectory(quarantine);
            if (Directory.Exists(paths.OwnedAuth) && Directory.EnumerateFileSystemEntries(paths.OwnedAuth).Any())
                throw new InvalidDataException("Restore quarantine is incomplete and the fresh core auth directory is not empty.");
        }
        OwnStatePaths.EnsurePrivateDirectory(paths.OwnedAuth);
        OwnStatePaths.WritePrivate(complete, JsonSerializer.Serialize(new { restoreEpoch = epoch, state = "quarantined" }));
    }
}
