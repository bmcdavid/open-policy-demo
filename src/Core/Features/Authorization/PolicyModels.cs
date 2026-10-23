namespace Core.Features.Authorization;

public sealed record PolicySubject(string Id, IReadOnlyList<string> Roles, string Tenant);

public sealed record PolicyResource(string Type, string Tenant, string? Id);

public sealed record PolicyInput(PolicySubject Subject, string Environment, string Action, PolicyResource Resource);

public sealed record PolicyDecision(bool Allow, IReadOnlyList<string> Reasons);
