using Api.Features.Authorization;
using Core.Features.Authorization;
using Core.Features.Documents;

namespace Api.Features.Documents;

internal sealed record UpdateDocumentRequest(Guid Id, string? Title);

internal sealed class UpdateDocumentHttpRequestHandler(IDocumentStore store, IPolicyEnforcer enforcer)
    : IEndpointHandler<UpdateDocumentRequest, IResult>
{
    public async Task<IResult> HandleAsync(UpdateDocumentRequest input, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var document = store.Find(input.Id);
        if (document is null)
        {
            return Results.NotFound();
        }

        var denied = await enforcer.EnforceAsync(
            httpContext, PolicyActions.Update, new PolicyResource("document", document.Tenant, document.Id.ToString()), cancellationToken)
            .ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            return Results.BadRequest(new { error = "title is required" });
        }

        var updated = store.Update(input.Id, input.Title);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }
}
