using Api.Features.Authorization;
using Core.Features.Authorization;
using Core.Features.Documents;

namespace Api.Features.Documents;

internal sealed record CreateDocumentBody(string? Title, string? Tenant);

internal sealed record UpdateDocumentBody(string? Title);

/// <summary>What each document endpoint does once <see cref="DocumentAccess"/> has allowed it.</summary>
internal static class DocumentEndpoints
{
    public static Task<IResult> List(HttpContext http, string? tenant, DocumentAccess access, IDocumentStore store) =>
        access.InTenant(http, "document", PolicyActions.List, tenant,
            async t => Results.Ok(await store.ListAsync(t, http.RequestAborted)), http.RequestAborted);

    public static Task<IResult> Get(HttpContext http, Guid id, DocumentAccess access) =>
        access.Existing(http, id, PolicyActions.Read, document => Task.FromResult(Results.Ok(document)), http.RequestAborted);

    public static Task<IResult> Create(HttpContext http, CreateDocumentBody body, DocumentAccess access, IDocumentStore store) =>
        access.InTenant(http, "document", PolicyActions.Create, body.Tenant, async tenant =>
        {
            if (string.IsNullOrWhiteSpace(body.Title))
            {
                return Results.BadRequest(new { error = "title is required" });
            }

            if (string.IsNullOrWhiteSpace(tenant))
            {
                return Results.BadRequest(new { error = "tenant is required" });
            }

            var document = await store.AddAsync(tenant, body.Title, http.RequestAborted);
            return Results.Created("/documents/" + document.Id, document);
        }, http.RequestAborted);

    public static Task<IResult> Update(HttpContext http, Guid id, UpdateDocumentBody body, DocumentAccess access, IDocumentStore store) =>
        access.Existing(http, id, PolicyActions.Update, async _ =>
        {
            if (string.IsNullOrWhiteSpace(body.Title))
            {
                return Results.BadRequest(new { error = "title is required" });
            }

            var updated = await store.UpdateAsync(id, body.Title, http.RequestAborted);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }, http.RequestAborted);

    public static Task<IResult> Delete(HttpContext http, Guid id, DocumentAccess access, IDocumentStore store) =>
        access.Existing(http, id, PolicyActions.Delete, async document =>
        {
            await store.RemoveAsync(document.Id, http.RequestAborted);
            return Results.NoContent();
        }, http.RequestAborted);
}
