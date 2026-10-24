using System.Text;
namespace Lain.Host.Accounts.CliConnections;

public sealed class RequestyCliLoginAdapter : UnavailableCliLoginAdapter
{
    public override string Provider => "requesty";
    public override string QualificationBlocker => "Requesty auth token --profile is source-backed, but the pinned executable and complete login/profile/keyring isolation are not qualified. Use an existing Requesty API key.";
    public static IReadOnlyList<string> LoginArguments(string ownedProfile, bool reconnect)
    {
        CliPrivatePaths.Id(ownedProfile);
        return reconnect ? ["login", "--profile", ownedProfile, "--force"] : ["login", "--profile", ownedProfile];
    }
    public static IReadOnlyList<string> ExportArguments(string ownedProfile)
    { CliPrivatePaths.Id(ownedProfile); return ["auth", "token", "--profile", ownedProfile]; }
    public static string ParsePrivateKeyExport(ReadOnlySpan<byte> output)
    {
        if (output.Length is < 2 or > 16385) throw new CliConnectionException("invalid-requesty-key-export");
        var value = new UTF8Encoding(false, true).GetString(output);
        if (!value.EndsWith('\n')) throw new CliConnectionException("invalid-requesty-key-export");
        var key = value[..^1];
        if (key.EndsWith('\r')) key = key[..^1];
        if (AlibabaOwnedProfileReader.Secret(key) is null) throw new CliConnectionException("invalid-requesty-key-export");
        return key;
    }
}
