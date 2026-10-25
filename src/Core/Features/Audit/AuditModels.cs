using System.Text.Json;
using Core.Features.Events;

namespace Core.Features.Audit;

public sealed record AuditEntry(string Id, string Tenant, string EventType, DateTimeOffset OccurredAt, string? TraceId, JsonElement Payload)
{
    public static AuditEntry From(OutboxEvent outboxEvent)
    {
        ArgumentNullException.ThrowIfNull(outboxEvent);
        return new AuditEntry(outboxEvent.Id, outboxEvent.Tenant, outboxEvent.EventType, outboxEvent.OccurredAt, outboxEvent.TraceId, outboxEvent.Payload);
    }
}

public sealed record AuditQuery(string Tenant, DateTimeOffset? From, DateTimeOffset? To, string? ContinuationToken, int PageSize);

public sealed record AuditPage(IReadOnlyList<AuditEntry> Entries, string? ContinuationToken);

/// <summary>Idempotent: the entry id is the event id, so a redelivered event overwrites itself.</summary>
public delegate Task UpsertAuditEntry(AuditEntry entry, CancellationToken cancellationToken);

public delegate Task<AuditPage> QueryAudit(AuditQuery query, CancellationToken cancellationToken);
