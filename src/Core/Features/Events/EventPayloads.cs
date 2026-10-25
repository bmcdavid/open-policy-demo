using Core.Features.Authorization;

namespace Core.Features.Events;

public enum DocumentChangeKind
{
    Created,
    Updated,
    Deleted,
}

public sealed record DocumentChanged(Guid DocumentId, string Tenant, DocumentChangeKind Change, string? Title);

public sealed record PolicyDecisionMade(
    string SubjectId,
    IReadOnlyList<string> SubjectRoles,
    string SubjectTenant,
    string Environment,
    string Action,
    string ResourceType,
    string ResourceTenant,
    string? ResourceId,
    bool Allow,
    IReadOnlyList<string> Reasons)
{
    public static PolicyDecisionMade From(PolicyInput input, PolicyDecision decision)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(decision);

        return new PolicyDecisionMade(
            input.Subject.Id,
            input.Subject.Roles,
            input.Subject.Tenant,
            input.Environment,
            input.Action,
            input.Resource.Type,
            input.Resource.Tenant,
            input.Resource.Id,
            decision.Allow,
            decision.Reasons);
    }
}
