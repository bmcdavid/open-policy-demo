using Api.Features.Authorization;
using Core.Features.Authorization;
using Core.Features.Documents;

namespace Api.Features.Documents;

internal sealed record CreateDocumentBody(string? Title, string? Tenant) : ITenantRequest;

internal sealed record UpdateDocumentBody(string? Title);

/// <summary>What each document endpoint does once <see cref="AccessFilter"/> has allowed it.</summary>
internal static class DocumentEndpoints
{
    public static async Task<IResult> List(TenantScope scope, IDocumentStore store, CancellationToken cancellationToken) =>
        Results.Ok(await store.ListAsync(scope.Tenant, cancellationToken));

    public static IResult Get(AuthorizedDocument document) => Results.Ok(document.Value);

    public static async Task<IResult> Create(CreateDocumentBody body, TenantScope scope, IDocumentStore store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Title))
        {
            return Results.BadRequest(new { error = "title is required" });
        }

        if (string.IsNullOrWhiteSpace(scope.Tenant))
        {
            return Results.BadRequest(new { error = "tenant is required" });
        }

        var document = await store.AddAsync(scope.Tenant, body.Title, cancellationToken);
        return Results.Created("/documents/" + document.Id, document);
    }

    public static async Task<IResult> Update(UpdateDocumentBody body, AuthorizedDocument document, IDocumentStore store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Title))
        {
            return Results.BadRequest(new { error = "title is required" });
        }

        var updated = await store.UpdateAsync(document.Value.Id, body.Title, cancellationToken);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    public static async Task<IResult> Delete(AuthorizedDocument document, IDocumentStore store, CancellationToken cancellationToken)
    {
        await store.RemoveAsync(document.Value.Id, cancellationToken);
        return Results.NoContent();
    }
}
