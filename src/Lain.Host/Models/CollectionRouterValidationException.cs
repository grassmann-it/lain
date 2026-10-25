using Lain.Core;

namespace Lain.Host.Models;

public enum CollectionRouterFailureReason { ProviderAccountsAmbiguous, NoEligibleMembers, AliasConflict }

/// <summary>Allowlisted router failures. Public diagnostics contain only fixed text and bounded configuration identities.</summary>
public sealed class CollectionRouterValidationException : ArgumentException
{
    public CollectionRouterFailureReason Reason { get; }
    public string? RouterId { get; }
    public string? ProfileId { get; }
    public string? GroupId { get; }
    public string Code => Reason switch
    {
        CollectionRouterFailureReason.ProviderAccountsAmbiguous => "router_provider_accounts_ambiguous",
        CollectionRouterFailureReason.NoEligibleMembers => "router_no_eligible_members",
        CollectionRouterFailureReason.AliasConflict => "router_alias_conflict",
        _ => "router_validation"
    };

    public CollectionRouterValidationException(CollectionRouterFailureReason reason, CollectionRouterDto router)
        : base(SafeMessage(reason, router))
    {
        Reason = reason;
        RouterId = SafeIdentity(router.Id);
        ProfileId = SafeIdentity(router.ProfileId);
        GroupId = SafeGroup(router.GroupId);
    }

    public ApiError ToApiError() => new(Code, Message);

    private static string SafeMessage(CollectionRouterFailureReason reason, CollectionRouterDto router)
    {
        var message = reason switch
        {
            CollectionRouterFailureReason.ProviderAccountsAmbiguous => "The provider router spans multiple exported accounts. Select an explicit account:<stableAccountId> group or custom collection.",
            CollectionRouterFailureReason.NoEligibleMembers => "The router has no enabled, visible, strict single-account profile members with matching Chat/Responses transport proof under its cost policy. Free routing also requires qualified free-only enforcement.",
            CollectionRouterFailureReason.AliasConflict => "The router alias conflicts with a model route. Choose a distinct router alias.",
            _ => "The router configuration is invalid."
        };
        return message + (SafeIdentity(router.Id) is { } id ? " Router: " + id + "." : "") +
            (SafeIdentity(router.ProfileId) is { } profile ? " Profile: " + profile + "." : "");
    }

    private static string? SafeIdentity(string? value) => value is { Length: > 0 and <= 100 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ? value : null;

    private static string? SafeGroup(string? value)
    {
        if (value == "policy:free") return value;
        if (value is null) return null;
        foreach (var prefix in new[] { "collection:", "account:", "provider:" })
            if (value.StartsWith(prefix, StringComparison.Ordinal) && SafeIdentity(value[prefix.Length..]) is { } identity)
                return prefix + identity;
        return null;
    }
}
