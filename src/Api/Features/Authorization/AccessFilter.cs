using Core.Features.Authorization;
using Core.Features.Documents;
using Core.Features.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Features.Authorization;

internal sealed record DenyBody(IReadOnlyList<string> Reasons);

/// <summary>Resource types the policy knows about.</summary>
internal static class Resource
{
    public const string Document = "document";
    public const string Audit = "audit";
}

/// <summary>
/// "Load, authorize, then act" for every protected endpoint, declared on the route instead of called from the body.
/// Owns the ordering rules: an <c>X-Environment</c> header that does not resolve for this deployment (missing without a
/// default, unknown, or not allowed) is 400 before anything else; a missing document is 404 without calling policy; the
/// policy decision is recorded as an outbox event before the endpoint runs (so a decision that cannot be recorded fails
/// closed with 503); a denial is 403 with reasons; an unreachable engine is 503. The endpoint only runs when the request
/// is allowed, and reads what was loaded from <see cref="AuthorizedDocument"/> or <see cref="TenantScope"/>.
/// </summary>
internal sealed class AccessFilter(string action, string resourceType, bool existing) : IEndpointFilter
{
    public static InvalidOperationException NotAuthorized(string accessor) =>
        new($"{accessor} was read on a route that is not declared with AuthorizeExisting or AuthorizeInTenant.");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;

        var resolution = services.GetRequiredService<ResolveEnvironment>()(http.Request.Headers[DemoEnvironments.HeaderName].ToString());
        if (resolution.Environment is not { } environment)
        {
            return Results.BadRequest(new { error = resolution.Error });
        }

        Document? document = null;
        string? tenant = null;
        PolicyResource resource;
        if (existing)
        {
            var id = Guid.Parse(http.GetRouteValue("id")!.ToString()!);
            document = await services.GetRequiredService<IDocumentStore>().FindAsync(id, http.RequestAborted);
            if (document is null)
            {
                return Results.NotFound();
            }

            resource = new PolicyResource(resourceType, document.Tenant, document.Id.ToString());
        }
        else
        {
            tenant = RequestedTenant(context) ?? CurrentSubject.From(http.User).Tenant;
            resource = new PolicyResource(resourceType, tenant, null);
        }

        var denied = await EnforceAsync(http, environment, resource);
        if (denied is not null)
        {
            return denied;
        }

        if (document is not null)
        {
            services.GetRequiredService<AuthorizedDocument>().Set(document);
        }
        else
        {
            services.GetRequiredService<TenantScope>().Set(tenant!);
        }

        return await next(context);
    }

    private static string? RequestedTenant(EndpointFilterInvocationContext context) =>
        context.Arguments.OfType<ITenantRequest>().FirstOrDefault()?.Tenant
        ?? (context.HttpContext.Request.Query.TryGetValue("tenant", out var query) ? query.ToString() : null);

    private async Task<IResult?> EnforceAsync(HttpContext http, string environment, PolicyResource resource)
    {
        var services = http.RequestServices;
        var input = new PolicyInput(CurrentSubject.From(http.User), environment, action, resource);

        PolicyDecision decision;
        try
        {
            decision = await services.GetRequiredService<DecidePolicy>()(input, http.RequestAborted);
        }
        catch (PolicyEngineUnavailableException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "policy engine unavailable");
        }

        await services.GetRequiredService<WriteOutboxEvent>()(
            OutboxEvent.Create(resource.Tenant, EventTypes.PolicyDecisionMade, PolicyDecisionMade.From(input, decision)),
            http.RequestAborted);

        return decision.Allow ? null : Results.Json(new DenyBody(decision.Reasons), statusCode: StatusCodes.Status403Forbidden);
    }
}

internal static class AccessExtensions
{
    /// <summary>Loads the document named by the route's <c>id</c>, asks policy about it, and exposes it as <see cref="AuthorizedDocument"/>.</summary>
    public static TBuilder AuthorizeExisting<TBuilder>(this TBuilder builder, string action)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new AccessFilter(action, Resource.Document, existing: true));

    /// <summary>Asks policy about a tenant's collection (the body's or query's tenant, else the caller's) and exposes it as <see cref="TenantScope"/>.</summary>
    public static TBuilder AuthorizeInTenant<TBuilder>(this TBuilder builder, string action, string resourceType)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new AccessFilter(action, resourceType, existing: false));
}
