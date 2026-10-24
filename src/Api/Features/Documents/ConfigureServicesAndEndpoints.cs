using Api.Features.Authorization;
using Core;
using Core.Features.Authorization;
using Core.Features.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Api.Features.Documents;

internal static class ConfigureServicesAndEndpoints
{
    public static WebApplicationBuilder ConfigureDocuments(this WebApplicationBuilder builder) => builder.AddDocumentStorage();

    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var documents = app.MapGroup(ApiRoutes.Documents)
            .RequireAuthorization();

        documents.MapGet("/", DocumentEndpoints.List).AuthorizeInTenant(PolicyActions.List, Resource.Document);
        documents.MapGet("/{id:guid}", DocumentEndpoints.Get).AuthorizeExisting(PolicyActions.Read);
        documents.MapPost("/", DocumentEndpoints.Create).AuthorizeInTenant(PolicyActions.Create, Resource.Document);
        documents.MapPut("/{id:guid}", DocumentEndpoints.Update).AuthorizeExisting(PolicyActions.Update);
        documents.MapDelete("/{id:guid}", DocumentEndpoints.Delete).AuthorizeExisting(PolicyActions.Delete);
    }
}
