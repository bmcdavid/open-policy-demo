using Api.Features.Authorization;
using Core.Features.Authorization;
using Core.Features.Documents;

namespace Api.Features.Documents;

internal sealed record ListDocumentsRequest(string? Tenant);

internal sealed class ListDocumentsHttpRequestHandler(IDocumentStore store, IPolicyEnforcer enforcer)
    : IEndpointHandler<ListDocumentsRequest, IResult>
{
    public async Task<IResult> HandleAsync(ListDocumentsRequest input, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(httpContext);

        var tenant = input.Tenant ?? CurrentSubject.From(httpContext.User).Tenant;
        var denied = await enforcer.EnforceAsync(httpContext, PolicyActions.List, new PolicyResource("document", tenant, null), cancellationToken)
            .ConfigureAwait(false);
        return denied ?? Results.Ok(store.List(tenant));
    }
}
