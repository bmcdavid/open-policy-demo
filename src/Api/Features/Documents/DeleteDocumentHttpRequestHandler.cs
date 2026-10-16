using Api.Features.Authorization;
using Core.Features.Authorization;
using Core.Features.Documents;

namespace Api.Features.Documents;

internal sealed class DeleteDocumentHttpRequestHandler(IDocumentStore store, IPolicyEnforcer enforcer)
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
            httpContext, PolicyActions.Delete, new PolicyResource("document", document.Tenant, document.Id.ToString()), cancellationToken)
            .ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        store.Remove(input);
        return Results.NoContent();
    }
}
