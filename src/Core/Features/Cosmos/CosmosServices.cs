using Core.Features.Audit;
using Core.Features.Documents;
using Core.Features.Events;
using Core.Hosting;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Core.Features.Cosmos;

internal static class CosmosServices
{
    /// <summary>
    /// Everything backed by Cosmos, for hosts that require it: the client and database, the initializer that creates them,
    /// the document store, and the outbox and audit seams. The seams are stateless closures over the database, so a host
    /// that never calls one pays nothing for it. Documents are read through <see cref="DocumentCache"/> when
    /// <see cref="DocumentServices.AddDocumentCache{TBuilder}"/> was also called, in either order.
    /// </summary>
    public static TBuilder AddCosmosStorage<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddCosmosClient();
        builder.Services.AddHostedService<CosmosInitializerService>();
        builder.Services.AddSingleton<WriteOutboxEvent>(sp => CosmosOutbox.Write(sp.GetRequiredService<Database>()));
        builder.Services.AddSingleton<QueryAudit>(sp => CosmosAudit.Query(sp.GetRequiredService<Database>()));
        builder.Services.AddSingleton<UpsertAuditEntry>(sp => CosmosAudit.Upsert(sp.GetRequiredService<Database>()));
        builder.Services.AddSingleton<IDocumentStore>(sp =>
        {
            var store = new CosmosDocumentStore(sp.GetRequiredService<Database>());
            return sp.GetService<DocumentCache>()?.Wrap(store) ?? store;
        });
        return builder;
    }
}
