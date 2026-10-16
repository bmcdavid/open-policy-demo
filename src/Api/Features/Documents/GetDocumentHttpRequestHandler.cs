using Api.Features.Authorization;
using Core.Features.Authorization;
using Core.Features.Documents;

namespace Api.Features.Documents;

internal sealed class GetDocumentHttpRequestHandler(IDocumentStore store, IPolicyEnforcer enforcer)
    : IEndpointHandler<Guid, IResult>
{
    public async Task<IResult> HandleAsync(Guid input, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var document = store.Find(input);
        if (document is null)
        {
            return Results.NotFound();
        }

        var denied = await enforcer.EnforceAsync(
            httpContext, PolicyActions.Read, new PolicyResource("document", document.Tenant, document.Id.ToString()), cancellationToken)
            .ConfigureAwait(false);
        return denied ?? Results.Ok(document);
    }
}
