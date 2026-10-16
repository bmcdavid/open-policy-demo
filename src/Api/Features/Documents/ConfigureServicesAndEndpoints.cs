using Core;
using Core.Features.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Features.Documents;

internal sealed record UpdateDocumentBody(string? Title);

internal static class ConfigureServicesAndEndpoints
{
    public static WebApplicationBuilder ConfigureDocuments(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
        builder.Services.AddTransient<ListDocumentsHttpRequestHandler>();
        builder.Services.AddTransient<GetDocumentHttpRequestHandler>();
        builder.Services.AddTransient<CreateDocumentHttpRequestHandler>();
        builder.Services.AddTransient<UpdateDocumentHttpRequestHandler>();
        builder.Services.AddTransient<DeleteDocumentHttpRequestHandler>();
        return builder;
    }

    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var documents = app.MapGroup(ApiRoutes.Documents)
            .RequireAuthorization()
            .AddEndpointFilter<EnvironmentHeaderFilter>();

        documents.MapGet("/", (HttpContext h, string? tenant, ListDocumentsHttpRequestHandler handler) =>
            handler.HandleAsync(new ListDocumentsRequest(tenant), h, h.RequestAborted));
        documents.MapGet("/{id:guid}", (HttpContext h, Guid id, GetDocumentHttpRequestHandler handler) =>
            handler.HandleAsync(id, h, h.RequestAborted));
        documents.MapPost("/", (HttpContext h, CreateDocumentRequest body, CreateDocumentHttpRequestHandler handler) =>
            handler.HandleAsync(body, h, h.RequestAborted));
        documents.MapPut("/{id:guid}", (HttpContext h, Guid id, UpdateDocumentBody body, UpdateDocumentHttpRequestHandler handler) =>
            handler.HandleAsync(new UpdateDocumentRequest(id, body.Title), h, h.RequestAborted));
        documents.MapDelete("/{id:guid}", (HttpContext h, Guid id, DeleteDocumentHttpRequestHandler handler) =>
            handler.HandleAsync(id, h, h.RequestAborted));
    }
}
