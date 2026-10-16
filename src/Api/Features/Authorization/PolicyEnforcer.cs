using Core.Features.Authorization;

namespace Api.Features.Authorization;

internal sealed record DenyBody(IReadOnlyList<string> Reasons);

internal interface IPolicyEnforcer
{
    /// <summary>Returns null when the request is allowed, otherwise the result to send.</summary>
    Task<IResult?> EnforceAsync(HttpContext http, string action, PolicyResource resource, CancellationToken cancellationToken);
}

internal sealed class PolicyEnforcer(IPolicyClient policyClient) : IPolicyEnforcer
{
    public const string EnvironmentItemKey = "environment";

    public async Task<IResult?> EnforceAsync(HttpContext http, string action, PolicyResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        var input = new PolicyInput(
            CurrentSubject.From(http.User),
            http.Items[EnvironmentItemKey] as string ?? string.Empty,
            action,
            resource);

        try
        {
            var decision = await policyClient.DecideAsync(input, cancellationToken).ConfigureAwait(false);
            return decision.Allow ? null : Results.Json(new DenyBody(decision.Reasons), statusCode: StatusCodes.Status403Forbidden);
        }
        catch (PolicyEngineUnavailableException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "policy engine unavailable");
        }
    }
}
