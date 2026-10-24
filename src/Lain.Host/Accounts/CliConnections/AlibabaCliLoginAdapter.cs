using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Accounts.CliConnections;

public sealed class AlibabaCliLoginAdapter : QualifiedCliLoginAdapter
{
    public override string Provider => "alibaba-model-studio";
    public override string QualificationBlocker => "Alibaba CLI 2.5.1 source supports BAILIAN_CONFIG_DIR and --config, but executable integrity, callback identity and Linux isolation are not qualified. Use an existing ordinary API key.";
    protected override string IsolationEnvironment => "BAILIAN_CONFIG_DIR";
    private readonly string site;
    public AlibabaCliLoginAdapter(CliConnectionStateStore store, CliQualificationManifest? qualification = null, string site = "international") : base(store, qualification)
    { if (site is not ("international" or "domestic")) throw new ArgumentException("Unsupported Alibaba site."); this.site = site; }
    protected override IReadOnlyList<string> LoginArguments(string profile, bool reconnect) => ["auth", "login", "--console", "--console-site", site, "--config", profile];
    protected override Task<PreparedLoginCredential?> ReadCredentialAsync(LoginOperationContext context, CliConnectionState state, string directory, CancellationToken ct)
    {
        var profile = new AlibabaOwnedProfileReader(Store).Read(state.ProfileReference);
        if (profile.Site != site || profile.ApiKey is null) return Task.FromResult<PreparedLoginCredential?>(null);
        var purposes = context.Request.Purposes.Where(p => p is "inference" or "models").Distinct().ToArray();
        // Console ownership is separate. A shared static key cannot falsely advertise
        // its private Console session as quota access or acquire cloud credentials.
        if (purposes.Length == 0) return Task.FromResult<PreparedLoginCredential?>(null);
        var json = JsonSerializer.Serialize(new { kind = "api-key", provider = Provider, coreProvider = Provider,
            sourceSection = "openai-compatibility", apiKey = profile.ApiKey, baseUrl = profile.BaseUrl,
            origin = "official-cli", cliRelease = Qualification!.Release, cliSourceCommit = Qualification.SourceCommit,
            cliProfileReference = state.ProfileReference, cliOperationId = context.OperationId,
            settings = new { region = profile.Region, workspaceId = profile.WorkspaceId } });
        return Task.FromResult<PreparedLoginCredential?>(new("static-key", purposes, profile.Binding, json, PreserveForRecovery: true));
    }
    public override Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        if (context.Request.Purposes.Length == 0 || context.Request.Purposes.Any(p => p is not ("inference" or "models")))
            return Task.FromResult(new LoginAdapterResult("failed", Error: "Alibaba Console-only ownership publication is not qualified. Existing inference keys remain usable; do not bootstrap another key for monitoring.", NextAction: "use-api-key"));
        return base.BeginAsync(context, ct);
    }
}
