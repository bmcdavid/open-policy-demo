using Core.Features.Audit;
using Core.Features.Cosmos;
using Core.Features.Events;
using Core.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Core.Features.Documents;

internal static class DocumentServices
{
    /// <summary>
    /// Document storage for a host that can run without infrastructure: Cosmos (cached when Valkey is configured) when a
    /// Cosmos connection string exists, otherwise the in-memory store with outbox and audit seams that do nothing, as in
    /// a plain <c>dotnet run</c> or a unit test.
    /// </summary>
    public static TBuilder AddDocumentStorage<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Configuration.GetConnectionString(ConnectionNames.Cosmos) is null)
        {
            builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
            builder.Services.AddSingleton<WriteOutboxEvent>((_, _) => Task.CompletedTask);
            builder.Services.AddSingleton<QueryAudit>((_, _) => Task.FromResult(new AuditPage([], null)));
            return builder;
        }

        builder.AddCosmosStorage();
        if (builder.Configuration.GetConnectionString(ConnectionNames.Valkey) is not null)
        {
            builder.AddDocumentCache();
        }

        return builder;
    }

    /// <summary>
    /// The Valkey client and <see cref="DocumentCache"/>, with <see cref="InvalidateDocument"/> as its seam for changes made
    /// by other writers. Stores registered by <see cref="CosmosServices.AddCosmosStorage{TBuilder}"/> read through it.
    /// </summary>
    public static TBuilder AddDocumentCache<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddValkeyClient();
        builder.Services.AddSingleton<ICacheBackend>(sp => new Valkey.ValkeyCacheBackend(sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>()));
        builder.Services.AddSingleton<DocumentCache>();
        builder.Services.AddSingleton<InvalidateDocument>(sp => sp.GetRequiredService<DocumentCache>().InvalidateAsync);
        return builder;
    }
}
