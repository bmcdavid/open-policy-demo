namespace Core.Features.Authorization;

/// <summary>Asks the policy engine for a decision. An unreachable or unusable engine surfaces as <see cref="PolicyEngineUnavailableException"/>.</summary>
public delegate Task<PolicyDecision> DecidePolicy(PolicyInput input, CancellationToken cancellationToken);
