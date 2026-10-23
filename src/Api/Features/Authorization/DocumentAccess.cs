using Core.Features.Authorization;
using Core.Features.Documents;
using Core.Features.Events;

namespace Api.Features.Authorization;

internal sealed record DenyBody(IReadOnlyList<string> Reasons);

/// <summary>
/// "Load, authorize, then act" for every protected endpoint. Owns the ordering rules: a missing or invalid
/// <c>X-Environment</c> header is 400 before anything else; a missing document is 404 without
/// calling policy; the policy decision is recorded as an outbox event before the action runs (so a decision that cannot
/// be recorded fails closed with 503); a denial is 403 with reasons; an unreachable engine is 503. The action only runs
/// when the request is allowed.
/// </summary>
internal sealed class DocumentAccess(IDocumentStore store, DecidePolicy decidePolicy, WriteOutboxEvent writeOutbox)
{
    /// <summary>For operations on one document: get, update and delete.</summary>
    public async Task<IResult> Existing(
        HttpContext http, Guid id, string action, Func<Document, Task<IResult>> act, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(act);

        if (!DemoEnvironments.TryParse(http.Request.Headers[DemoEnvironments.HeaderName].ToString(), out var environment))
        {
            return InvalidEnvironment;
        }

        var document = await store.FindAsync(id, cancellationToken);
        if (document is null)
        {
            return Results.NotFound();
        }

        var denied = await EnforceAsync(
            http, environment, action, new PolicyResource("document", document.Tenant, document.Id.ToString()), cancellationToken);
        return denied ?? await act(document);
    }

    /// <summary>For operations on a tenant's collection: list, create and the audit trail. The tenant defaults to the caller's.</summary>
    public async Task<IResult> InTenant(
        HttpContext http, string resourceType, string action, string? tenant, Func<string, Task<IResult>> act, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(act);

        if (!DemoEnvironments.TryParse(http.Request.Headers[DemoEnvironments.HeaderName].ToString(), out var environment))
        {
            return InvalidEnvironment;
        }

        var effectiveTenant = tenant ?? CurrentSubject.From(http.User).Tenant;
        var denied = await EnforceAsync(http, environment, action, new PolicyResource(resourceType, effectiveTenant, null), cancellationToken);
        return denied ?? await act(effectiveTenant);
    }

    private static IResult InvalidEnvironment =>
        Results.BadRequest(new { error = DemoEnvironments.HeaderName + " must be one of dev, staging, prod" });

    private async Task<IResult?> EnforceAsync(
        HttpContext http, string environment, string action, PolicyResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        var input = new PolicyInput(
            CurrentSubject.From(http.User),
            environment,
            action,
            resource);

        PolicyDecision decision;
        try
        {
            decision = await decidePolicy(input, cancellationToken);
        }
        catch (PolicyEngineUnavailableException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "policy engine unavailable");
        }

        await writeOutbox(
            OutboxEvent.Create(resource.Tenant, EventTypes.PolicyDecisionMade, PolicyDecisionMade.From(input, decision)),
            cancellationToken);

        return decision.Allow ? null : Results.Json(new DenyBody(decision.Reasons), statusCode: StatusCodes.Status403Forbidden);
    }
}
