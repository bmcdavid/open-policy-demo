using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using Core.Features.Cosmos;
using Core.Features.Events;
using Tests.Support;
using Xunit;

namespace Tests.CoreFeatures;

public sealed class CosmosOutboxTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OutboxEvent Changed(string tenant = "acme") =>
        OutboxEvent.Create(tenant, EventTypes.DocumentChanged, new DocumentChanged(Guid.NewGuid(), tenant, DocumentChangeKind.Updated, "t"));

    /// <summary>Raw JSON as Cosmos returns it from the change feed. Written by hand so this test pins the stored layout.</summary>
    private static JsonElement OutboxFeedItem(OutboxEvent e) => JsonDocument.Parse($$"""
        {
          "id": "{{e.Id}}",
          "tenant": "{{e.Tenant}}",
          "type": "outbox",
          "eventType": "{{e.EventType}}",
          "occurredAt": "{{e.OccurredAt:O}}",
          "traceId": {{(e.TraceId is null ? "null" : $"\"{e.TraceId}\"")}},
          "traceParent": {{(e.TraceParent is null ? "null" : $"\"{e.TraceParent}\"")}},
          "payload": {{e.Payload.GetRawText()}},
          "ttl": 604800,
          "_etag": "\"0000\"",
          "_ts": 1700000000
        }
        """).RootElement.Clone();

    private static JsonElement DocumentFeedItem() => JsonDocument.Parse($$"""
        { "id": "{{Guid.NewGuid()}}", "tenant": "acme", "title": "a document", "type": "document" }
        """).RootElement.Clone();

    private static PublishEvent Recording(List<OutboxEvent> into) => (e, _) =>
    {
        into.Add(e);
        return Task.CompletedTask;
    };

    [Fact]
    public async Task RelayPublishesOutboxItemsAndSkipsDocuments()
    {
        var published = new List<OutboxEvent>();
        var outbox = Changed();

        await CosmosOutbox.RelayAsync([DocumentFeedItem(), OutboxFeedItem(outbox)], Recording(published), Ct);

        var relayed = published.Should().ContainSingle().Subject;
        relayed.Id.Should().Be(outbox.Id);
        relayed.Tenant.Should().Be("acme");
        relayed.EventType.Should().Be(EventTypes.DocumentChanged);
        relayed.TraceId.Should().Be(outbox.TraceId);
        relayed.OccurredAt.Should().Be(outbox.OccurredAt);
        relayed.Payload.GetRawText().Should().Be(outbox.Payload.GetRawText());
    }

    [Fact]
    public async Task RelayPreservesFeedOrder()
    {
        var events = Enumerable.Range(0, 5).Select(_ => Changed()).ToList();
        var published = new List<OutboxEvent>();

        await CosmosOutbox.RelayAsync(events.Select(OutboxFeedItem).ToList(), Recording(published), Ct);

        published.Select(e => e.Id).Should().Equal(events.Select(e => e.Id));
    }

    [Fact]
    public async Task DocumentsOnlyBatchPublishesNothing()
    {
        var published = new List<OutboxEvent>();

        await CosmosOutbox.RelayAsync([DocumentFeedItem(), DocumentFeedItem()], Recording(published), Ct);

        published.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishFailureStopsTheBatchSoItIsRetried()
    {
        var items = new[] { OutboxFeedItem(Changed()), OutboxFeedItem(Changed()) };
        var attempts = 0;

        var act = () => CosmosOutbox.RelayAsync(items, (_, _) =>
        {
            attempts++;
            throw new InvalidOperationException("broker down");
        }, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task MalformedOutboxItemThrowsInvalidDataSoTheLeaseBlocksUntilFixed()
    {
        var malformed = JsonDocument.Parse("""{ "id": "e1", "type": "outbox", "tenant": "acme", "occurredAt": "not-a-date" }""").RootElement.Clone();
        var published = new List<OutboxEvent>();

        var act = () => CosmosOutbox.RelayAsync([malformed], Recording(published), Ct);

        (await act.Should().ThrowAsync<InvalidDataException>()).Which.Message.Should().Contain("e1");
        published.Should().BeEmpty();
    }

    [Fact]
    public void StoredItemCarriesTheOutboxTypeAndExpires()
    {
        var item = CosmosOutbox.ToItem(Changed());

        item.Type.Should().Be("outbox");
        item.Ttl.Should().Be((int)TimeSpan.FromDays(7).TotalSeconds);
    }

    [Fact]
    public void StoredItemRoundTripsThroughTheChangeFeedLayout()
    {
        var original = Changed();
        var stored = JsonSerializer.SerializeToElement(CosmosOutbox.ToItem(original), EventJson.Options);

        stored.GetProperty("type").GetString().Should().Be("outbox");
        stored.GetProperty("tenant").GetString().Should().Be(original.Tenant);
        stored.GetProperty("eventType").GetString().Should().Be(original.EventType);
        stored.GetProperty("ttl").GetInt32().Should().BeGreaterThan(0);
        stored.GetProperty("traceParent").ValueKind.Should().Be(original.TraceParent is null ? JsonValueKind.Null : JsonValueKind.String);
    }

    [Fact]
    public async Task RelayPublishesInsideASpanThatContinuesTheTraceTheEventWasWrittenIn()
    {
        using var recorder = new ActivityRecorder();
        using var requests = new ActivitySource(ActivityRecorder.ParentSourceName);
        OutboxEvent outbox;
        ActivityContext request;
        using (var incoming = requests.StartActivity("request")!)
        {
            outbox = Changed();
            request = incoming.Context;
        }

        Activity? during = null;
        await CosmosOutbox.RelayAsync([OutboxFeedItem(outbox)], (_, _) =>
        {
            during = Activity.Current;
            return Task.CompletedTask;
        }, Ct);

        during.Should().NotBeNull();
        during!.Source.Name.Should().Be(EventTracing.SourceName);
        during.DisplayName.Should().Be("relay document.changed");
        during.Kind.Should().Be(ActivityKind.Producer);
        during.TraceId.Should().Be(request.TraceId);
        during.ParentSpanId.Should().Be(request.SpanId);
    }
}
