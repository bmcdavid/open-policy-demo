using Core.Features.Audit;
using Core.Features.Messaging;

namespace Worker.Features.Audit;

internal static class AuditHandler
{
    /// <summary>The audit entry id is the event id, so a redelivered event overwrites itself instead of duplicating.</summary>
    public static HandleEvent Create(UpsertAuditEntry upsert)
    {
        ArgumentNullException.ThrowIfNull(upsert);
        return (outboxEvent, cancellationToken) => upsert(AuditEntry.From(outboxEvent), cancellationToken);
    }
}
