using System.Text.Json;
using AwesomeAssertions;
using Core.Features.Documents;
using Core.Features.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.CoreFeatures;

public sealed class DocumentCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeCache : ICacheBackend
    {
        public Dictionary<string, byte[]> Entries { get; } = [];

        public Exception? Throws { get; set; }

        public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
            Throws is null ? Task.FromResult(Entries.GetValueOrDefault(key)) : Task.FromException<byte[]?>(Throws);

        public Task SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan timeToLive, CancellationToken cancellationToken)
        {
            if (Throws is not null)
            {
                return Task.FromException(Throws);
            }

            Entries[key] = value.ToArray();
            return Task.CompletedTask;
        }

        public Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            if (Throws is not null)
            {
                return Task.FromException(Throws);
            }

            foreach (var key in keys)
            {
                Entries.Remove(key);
            }

            return Task.CompletedTask;
        }

        public void CorruptEveryEntry()
        {
            foreach (var key in Entries.Keys.ToList())
            {
                Entries[key] = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
            }
        }
    }

    private static (DocumentCache Cache, IDocumentStore Cached, InMemoryDocumentStore Inner, FakeCache Backend) Create()
    {
        var inner = new InMemoryDocumentStore();
        var backend = new FakeCache();
        var cache = new DocumentCache(backend, NullLogger<DocumentCache>.Instance);
        return (cache, cache.Wrap(inner), inner, backend);
    }

    private static DocumentChanged Changed(Document document) =>
        new(document.Id, document.Tenant, DocumentChangeKind.Updated, document.Title);

    [Fact]
    public async Task FindReadsThroughAndThenServesFromCache()
    {
        var (_, cached, inner, _) = Create();
        var seeded = DocumentSeed.Documents[0];

        (await cached.FindAsync(seeded.Id, Ct))!.Title.Should().Be(seeded.Title);
        await inner.UpdateAsync(seeded.Id, "changed behind the cache", Ct);

        (await cached.FindAsync(seeded.Id, Ct))!.Title.Should().Be(seeded.Title);
    }

    [Fact]
    public async Task FindOfMissingDocumentReturnsNullAndCachesNothing()
    {
        var (_, cached, _, backend) = Create();

        (await cached.FindAsync(Guid.NewGuid(), Ct)).Should().BeNull();

        backend.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ListReadsThroughAndThenServesFromCache()
    {
        var (_, cached, inner, _) = Create();

        (await cached.ListAsync("acme", Ct)).Should().ContainSingle();
        await inner.AddAsync("acme", "added behind the cache", Ct);

        (await cached.ListAsync("acme", Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task AddIsVisibleInTheNextList()
    {
        var (_, cached, _, _) = Create();
        await cached.ListAsync("acme", Ct);

        await cached.AddAsync("acme", "new", Ct);

        (await cached.ListAsync("acme", Ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateIsVisibleInTheNextFindAndList()
    {
        var (_, cached, _, _) = Create();
        var seeded = DocumentSeed.Documents[0];
        await cached.FindAsync(seeded.Id, Ct);
        await cached.ListAsync("acme", Ct);

        await cached.UpdateAsync(seeded.Id, "renamed", Ct);

        (await cached.FindAsync(seeded.Id, Ct))!.Title.Should().Be("renamed");
        (await cached.ListAsync("acme", Ct)).Should().ContainSingle().Which.Title.Should().Be("renamed");
    }

    [Fact]
    public async Task RemoveIsVisibleInTheNextFindAndList()
    {
        var (_, cached, _, _) = Create();
        var seeded = DocumentSeed.Documents[0];
        await cached.FindAsync(seeded.Id, Ct);
        await cached.ListAsync("acme", Ct);

        (await cached.RemoveAsync(seeded.Id, Ct)).Should().NotBeNull();

        (await cached.FindAsync(seeded.Id, Ct)).Should().BeNull();
        (await cached.ListAsync("acme", Ct)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ünïcödé — 文書 📄")]
    [InlineData("a title with \"quotes\" and \n newlines")]
    public async Task TitlesSurviveTheCacheRoundTrip(string title)
    {
        var (_, cached, inner, _) = Create();
        var added = await inner.AddAsync("acme", title, Ct);

        var firstRead = await cached.FindAsync(added.Id, Ct);
        var secondRead = await cached.FindAsync(added.Id, Ct);

        secondRead.Should().Be(firstRead).And.Be(added);
    }

    [Fact]
    public async Task UndecodableEntriesAreTreatedAsMissesAndRefetched()
    {
        var (_, cached, _, backend) = Create();
        var seeded = DocumentSeed.Documents[0];
        await cached.FindAsync(seeded.Id, Ct);
        await cached.ListAsync("acme", Ct);
        backend.CorruptEveryEntry();

        (await cached.FindAsync(seeded.Id, Ct))!.Title.Should().Be(seeded.Title);
        (await cached.ListAsync("acme", Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task EntriesWrittenUnderTheOldJsonKeyFormatAreNeverRead()
    {
        var (_, cached, _, backend) = Create();
        var seeded = DocumentSeed.Documents[0];
        var stale = JsonSerializer.SerializeToUtf8Bytes(seeded with { Title = "stale json entry" }, EventJson.Options);
        backend.Entries[$"document:{seeded.Id}"] = stale;
        backend.Entries[$"documents:{seeded.Tenant}"] = stale;

        (await cached.FindAsync(seeded.Id, Ct))!.Title.Should().Be(seeded.Title);
        (await cached.ListAsync(seeded.Tenant, Ct)).Should().ContainSingle().Which.Title.Should().Be(seeded.Title);
    }

    [Fact]
    public async Task CacheOutageFallsThroughToTheStoreForReadsAndWrites()
    {
        var (_, cached, _, backend) = Create();
        backend.Throws = new CacheUnavailableException("valkey down");
        var seeded = DocumentSeed.Documents[0];

        (await cached.FindAsync(seeded.Id, Ct))!.Title.Should().Be(seeded.Title);
        (await cached.ListAsync("acme", Ct)).Should().ContainSingle();
        (await cached.AddAsync("acme", "still works", Ct)).Title.Should().Be("still works");
        (await cached.UpdateAsync(seeded.Id, "renamed", Ct))!.Title.Should().Be("renamed");
        (await cached.RemoveAsync(seeded.Id, Ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task InvalidateMakesAnotherWritersChangeVisibleOnTheNextRead()
    {
        var (cache, cached, inner, _) = Create();
        var seeded = DocumentSeed.Documents[0];
        await cached.FindAsync(seeded.Id, Ct);
        await cached.ListAsync("acme", Ct);
        await inner.UpdateAsync(seeded.Id, "changed by another writer", Ct);

        await cache.InvalidateAsync(Changed(seeded), Ct);

        (await cached.FindAsync(seeded.Id, Ct))!.Title.Should().Be("changed by another writer");
        (await cached.ListAsync("acme", Ct)).Should().ContainSingle().Which.Title.Should().Be("changed by another writer");
    }

    [Fact]
    public async Task InvalidateReportsAnOutageSoTheMessageIsRetried()
    {
        var (cache, _, _, backend) = Create();
        backend.Throws = new CacheUnavailableException("valkey down");

        var act = () => cache.InvalidateAsync(Changed(DocumentSeed.Documents[0]), Ct);

        await act.Should().ThrowAsync<CacheUnavailableException>();
    }
}
