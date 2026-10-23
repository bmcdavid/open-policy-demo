using System.Diagnostics;
using System.Text.Json;

namespace Core.Features.Events;

/// <summary>
/// Envelope for an event written to the outbox, relayed to RabbitMQ and audited. <see cref="TraceParent"/> is the W3C
/// trace context of the request that wrote it, so the relay and the handlers continue that trace.
/// </summary>
public sealed record OutboxEvent(
    string Id, string Tenant, string EventType, DateTimeOffset OccurredAt, string? TraceId, JsonElement Payload, string? TraceParent = null)
{
    public const string UnassignedTenant = "_unassigned";

    public static OutboxEvent Create<TPayload>(string tenant, string eventType, TPayload payload, TimeProvider? time = null) =>
        new(
            Guid.CreateVersion7().ToString("N"),
            string.IsNullOrEmpty(tenant) ? UnassignedTenant : tenant,
            eventType,
            (time ?? TimeProvider.System).GetUtcNow(),
            Activity.Current?.TraceId.ToString(),
            JsonSerializer.SerializeToElement(payload, EventJson.Options),
            Activity.Current?.Id);
}
