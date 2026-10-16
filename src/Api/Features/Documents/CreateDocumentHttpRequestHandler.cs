using Api.Features.Authorization;
using Core.Features.Authorization;
using Core.Features.Documents;

namespace Api.Features.Documents;

internal sealed record CreateDocumentRequest(string? Title, string? Tenant);

internal sealed class CreateDocumentHttpRequestHandler(IDocumentStore store, IPolicyEnforcer enforcer)
    : IEndpointHandler<CreateDocumentRequest, IResult>
{
    public async Task<IResult> HandleAsync(CreateDocumentRequest input, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(httpContext);

        var tenant = input.Tenant ?? CurrentSubject.From(httpContext.User).Tenant;
        var denied = await enforcer.EnforceAsync(httpContext, PolicyActions.Create, new PolicyResource("document", tenant, null), cancellationToken)
            .ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            return Results.BadRequest(new { error = "title is required" });
        }

        var document = store.Add(tenant, input.Title);
        return Results.Created("/documents/" + document.Id, document);
    }
}
