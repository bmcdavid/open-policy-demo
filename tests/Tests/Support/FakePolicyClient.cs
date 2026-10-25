using Core.Features.Authorization;

namespace Tests.Support;

internal sealed class FakePolicyClient
{
    public Func<PolicyInput, PolicyDecision> Decide { get; set; } = _ => new PolicyDecision(true, []);

    public Exception? Throws { get; set; }

    public List<PolicyInput> Inputs { get; } = [];

    public Task<PolicyDecision> DecideAsync(PolicyInput input, CancellationToken _)
    {
        Inputs.Add(input);
        return Throws is null ? Task.FromResult(Decide(input)) : Task.FromException<PolicyDecision>(Throws);
    }
}
