namespace Core.Features.Authorization;

public interface IPolicyClient
{
    Task<PolicyDecision> DecideAsync(PolicyInput input, CancellationToken cancellationToken);
}
