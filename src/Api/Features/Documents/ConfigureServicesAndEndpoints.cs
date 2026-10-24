using Core;
using Core.Features.Audit;
using Core.Features.Cosmos;
using Core.Features.Documents;
using Core.Features.Events;
using Core.Features.Valkey;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Core.Hosting;
using StackExchange.Redis;

namespace Api.Features.Documents;

internal static class ConfigureServicesAndEndpoints
{
    public static WebApplicationBuilder ConfigureDocuments(this WebApplicationBuilder builder)
    {
        if (builder.Configuration.GetConnectionString(ConnectionNames.Cosmos) is not null)
        {
            AddCosmosBackedStore(builder);
        }
        else
        {
            // No Cosmos configured (plain `dotnet run` of the Api, unit tests): in-memory store, and the event seams do nothing.
            builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
            builder.Services.AddSingleton<WriteOutboxEvent>((_, _) => Task.CompletedTask);
            builder.Services.AddSingleton<QueryAudit>((_, _) => Task.FromResult(new AuditPage([], null)));
        }

        return builder;
    }

    private static void AddCosmosBackedStore(WebApplicationBuilder builder)
    {
        builder.AddCosmosClient();
        builder.Services.AddHostedService<CosmosInitializerService>();
        builder.Services.AddSingleton<WriteOutboxEvent>(sp => CosmosOutbox.Write(sp.GetRequiredService<Database>()));
        builder.Services.AddSingleton<QueryAudit>(sp => CosmosAudit.Query(sp.GetRequiredService<Database>()));

        if (builder.Configuration.GetConnectionString(ConnectionNames.Valkey) is null)
        {
            builder.Services.AddSingleton<IDocumentStore>(sp => new CosmosDocumentStore(sp.GetRequiredService<Database>()));
            return;
        }

        builder.AddValkeyClient();
        builder.Services.AddSingleton<ICacheBackend>(sp => new ValkeyCacheBackend(sp.GetRequiredService<IConnectionMultiplexer>()));
        builder.Services.AddSingleton<DocumentCache>();
        builder.Services.AddSingleton<IDocumentStore>(sp =>
            sp.GetRequiredService<DocumentCache>().Wrap(new CosmosDocumentStore(sp.GetRequiredService<Database>())));
    }

    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var documents = app.MapGroup(ApiRoutes.Documents)
            .RequireAuthorization();

        documents.MapGet("/", DocumentEndpoints.List);
        documents.MapGet("/{id:guid}", DocumentEndpoints.Get);
        documents.MapPost("/", DocumentEndpoints.Create);
        documents.MapPut("/{id:guid}", DocumentEndpoints.Update);
        documents.MapDelete("/{id:guid}", DocumentEndpoints.Delete);
    }
}
