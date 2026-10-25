using AwesomeAssertions;
using Core.Features.Audit;
using Core.Features.Cosmos;
using Core.Features.Documents;
using Core.Features.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tests.Core;

public sealed class StorageCompositionTests
{
    private const string _emulatorConnectionString =
        "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private static HostApplicationBuilder Builder(bool cosmos = false, bool valkey = false)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        if (cosmos)
        {
            builder.Configuration["ConnectionStrings:cosmos"] = _emulatorConnectionString;
        }

        if (valkey)
        {
            builder.Configuration["ConnectionStrings:valkey"] = "localhost:6379";
        }

        return builder;
    }

    [Fact]
    public void WithoutCosmosTheStoreIsInMemoryAndTheSeamsDoNothing()
    {
        var builder = Builder();
        builder.AddDocumentStorage();

        using var host = builder.Build();

        host.Services.GetRequiredService<IDocumentStore>().Should().BeOfType<InMemoryDocumentStore>();
        host.Services.GetRequiredService<WriteOutboxEvent>().Should().NotBeNull();
        host.Services.GetService<DocumentCache>().Should().BeNull();
    }

    [Fact]
    public void WithCosmosAloneTheStoreIsNotCached()
    {
        var builder = Builder(cosmos: true);
        builder.AddDocumentStorage();

        using var host = builder.Build();

        host.Services.GetRequiredService<IDocumentStore>().Should().BeOfType<CosmosDocumentStore>();
        host.Services.GetRequiredService<UpsertAuditEntry>().Should().NotBeNull();
    }

    [Fact]
    public void WithCosmosAndValkeyTheStoreReadsThroughTheCache()
    {
        var builder = Builder(cosmos: true, valkey: true);
        builder.AddDocumentStorage();

        using var host = builder.Build();

        host.Services.GetRequiredService<IDocumentStore>().Should().BeOfType<CachedDocumentStore>();
        host.Services.GetRequiredService<InvalidateDocument>().Should().NotBeNull();
    }

    [Fact]
    public void CacheAndStorageComposeInEitherOrder()
    {
        var builder = Builder(cosmos: true, valkey: true);
        builder.AddDocumentCache();
        builder.AddCosmosStorage();

        using var host = builder.Build();

        host.Services.GetRequiredService<IDocumentStore>().Should().BeOfType<CachedDocumentStore>();
    }
}
