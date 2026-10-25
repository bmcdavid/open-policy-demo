using System.Text.Json;
using AwesomeAssertions;
using Core.Features.Audit;
using Core.Features.Cosmos;
using Core.Features.Documents;
using Core.Features.Events;
using Core.Features.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Support;
using Worker.Features.Audit;
using Worker.Features.CacheInvalidation;
using Xunit;

namespace Tests.Worker;

/// <summary>The relay, audit and cache-invalidation flow wired together over the in-memory transport: no Docker.</summary>
public sealed class PipelineWithoutDockerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangeFeedBatchReachesTheAuditTrailAndTheCache()
    {
        var bus = new InMemoryTransport();
        var delivery = new EventDelivery(bus, NullLogger<EventDelivery>.Instance, new EventDeliveryOptions(5, _ => TimeSpan.Zero));
        var audit = new List<AuditEntry>();
        var cache = new InMemoryCache();
        var documents = new InMemoryDocumentStore();
        var cached = new DocumentCache(cache, NullLogger<DocumentCache>.Instance);
        var store = cached.Wrap(documents);
        var seeded = DocumentSeed.Documents[0];
        await store.FindAsync(seeded.Id, Ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = delivery.RunAsync(
            [
                new Subscription("audit", "#", AuditHandler.Create((entry, _) => { audit.Add(entry); return Task.CompletedTask; })),
                new Subscription("cache-invalidation", EventTypes.DocumentChanged, CacheInvalidationHandler.Create(cached.InvalidateAsync)),
            ],
            cts.Token);

        await documents.UpdateAsync(seeded.Id, "changed by another writer", Ct);
        var change = OutboxEvent.Create(seeded.Tenant, EventTypes.DocumentChanged,
            new DocumentChanged(seeded.Id, seeded.Tenant, DocumentChangeKind.Updated, "changed by another writer"));
        var feedItem = JsonSerializer.SerializeToElement(new
        {
            id = change.Id,
            tenant = change.Tenant,
            type = "outbox",
            eventType = change.EventType,
            occurredAt = change.OccurredAt,
            traceId = change.TraceId,
            payload = change.Payload,
            ttl = 604800,
        }, EventJson.Options);

        await CosmosOutbox.RelayAsync([feedItem], delivery.Publish, Ct);
        await bus.DrainAsync(TimeSpan.FromSeconds(5));

        audit.Should().ContainSingle().Which.Id.Should().Be(change.Id);
        (await store.FindAsync(seeded.Id, Ct))!.Title.Should().Be("changed by another writer");

        await cts.CancelAsync();
        await run.Invoking(r => r).Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class InMemoryCache : ICacheBackend
    {
        private readonly Dictionary<string, byte[]> _entries = [];

        public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult(_entries.GetValueOrDefault(key));

        public Task SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan timeToLive, CancellationToken cancellationToken)
        {
            _entries[key] = value.ToArray();
            return Task.CompletedTask;
        }

        public Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            foreach (var key in keys)
            {
                _entries.Remove(key);
            }

            return Task.CompletedTask;
        }
    }
}
