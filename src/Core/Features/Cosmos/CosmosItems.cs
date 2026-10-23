using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Features.Documents;
using Core.Features.Events;

namespace Core.Features.Cosmos;

/// <summary>A document and an outbox event share the <c>documents</c> container so they can be written in one batch.</summary>
public sealed record DocumentItem(
    string Id,
    string Tenant,
    string Title,
    string Type = CosmosNames.DocumentType,
    [property: JsonPropertyName("_etag")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ETag = null)
{
    public static DocumentItem From(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentItem(document.Id.ToString(), document.Tenant, document.Title);
    }

    public Document ToDocument() => new(Guid.Parse(Id, System.Globalization.CultureInfo.InvariantCulture), Tenant, Title);
}

internal sealed record OutboxItem(
    string Id,
    string Tenant,
    string EventType,
    DateTimeOffset OccurredAt,
    string? TraceId,
    JsonElement Payload,
    int Ttl,
    string Type = CosmosNames.OutboxType)
{
    public static OutboxItem From(OutboxEvent outboxEvent)
    {
        ArgumentNullException.ThrowIfNull(outboxEvent);
        return new OutboxItem(
            outboxEvent.Id,
            outboxEvent.Tenant,
            outboxEvent.EventType,
            outboxEvent.OccurredAt,
            outboxEvent.TraceId,
            outboxEvent.Payload,
            (int)CosmosNames.OutboxTimeToLive.TotalSeconds);
    }

    public OutboxEvent ToEvent() => new(Id, Tenant, EventType, OccurredAt, TraceId, Payload);
}
