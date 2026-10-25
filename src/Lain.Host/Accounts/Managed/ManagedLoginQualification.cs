using Lain.Core;

namespace Lain.Host.Accounts.Managed;

public sealed record ManagedLoginQualification(string Provider, string Method, string CandidateOwner,
    string State, string[] MissingEvidence, string DocumentationUrl, string Fallback);

/// <summary>These candidate methods are deliberately unavailable until each owner/admission contract is qualified.</summary>
public static class ManagedLoginQualifications
{
    public static readonly ManagedLoginQualification Kimi = new("kimi", "kimi-device", "undecided", "unqualified",
        ["Supported lain client admission for the published device toolkit.", "One region/subject-bound refresh owner and isolated persistence; or a pinned isolated CLI with bounded local bearer handoff."],
        "https://github.com/MoonshotAI/kimi-code/tree/main/packages/oauth", "Use the existing Kimi Coding API key on its qualified .com/.ai host.");
    public static readonly ManagedLoginQualification Zai = new("zai", "zai-managed", "undecided", "unqualified",
        ["No supported independent client contract or bound, non-creating official CLI path has been established from the pinned source.", "A personal international account, explicit organization/project and existing key ID are required; primary session expiry requires reconnect, not an invented OAuth refresh."],
        "https://github.com/zai-org/ZCode/blob/main/packages/shared/src/project-access-token-client.ts", "Use an existing international Coding key; no key is created by monitoring.");
    public static readonly ManagedLoginQualification Codex = new("codex", "codex-standalone", "delegated-cli-candidate", "unqualified",
        ["Pinned app-server isolated HOME/CODEX_HOME and file-only credential backend, restart and renewal qualification.", "Stable verified account/workspace scope: documented account/read only supplies nullable email and plan type; no guessed identity or foreign auth-cache adoption."],
        "https://developers.openai.com/codex/app-server/", "Keep the existing Core-owned browser login or explicitly imported expiring access snapshot.");

    public static ManagedLoginQualification[] All() => [Kimi with { MissingEvidence = Kimi.MissingEvidence.ToArray() },
        Zai with { MissingEvidence = Zai.MissingEvidence.ToArray() }, Codex with { MissingEvidence = Codex.MissingEvidence.ToArray() }];
    public static ProviderConnectionMethodDto[] Methods() => All().Select(q => new ProviderConnectionMethodDto(q.Provider, q.Method,
        q.Provider switch { "kimi" => "Kimi device login", "zai" => "Z.ai managed login", _ => "Codex standalone monitor" },
        q.Provider switch { "kimi" => "device", "zai" => "browser", _ => "cli" }, ["quota"], "unavailable", false, false, Description: "Candidate owner; production login is disabled.",
        DocumentationUrl: q.DocumentationUrl, Guidance: string.Join(" ", q.MissingEvidence) + " " + q.Fallback)).ToArray();
}

/// <summary>No I/O, token store or owner is created by an unqualified candidate. Publication belongs to LoginOperationService.</summary>
public abstract class UnqualifiedManagedLoginAdapter(ManagedLoginQualification qualification) : IAccountLoginAdapter
{
    public string Provider => qualification.Provider;
    public string Method => qualification.Method;
    public bool RequiresCore => false;
    public bool IsAvailable => false;
    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct) => Reject(context, ct);
    public Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct) => Reject(context, ct);
    public Task CancelAsync(LoginOperationContext context, CancellationToken ct) { Validate(context); return Task.CompletedTask; }
    public Task ReleaseAsync(LoginOperationContext context, CancellationToken ct) { Validate(context); return Task.CompletedTask; }
    private Task<LoginAdapterResult> Reject(LoginOperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Validate(context);
        return Task.FromResult(new LoginAdapterResult("failed", Error: "This managed login is not qualified. " + qualification.Fallback,
            NextAction: "reconnect"));
    }
    private void Validate(LoginOperationContext context)
    {
        if (context.Request.Provider != Provider || context.Request.Method != Method)
            throw new ArgumentException("Managed login operation does not match this provider/method.");
    }
}
