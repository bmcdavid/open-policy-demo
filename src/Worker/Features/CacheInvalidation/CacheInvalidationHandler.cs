using System.Text.Json;
using Core.Features.Documents;
using Core.Features.Events;
using Core.Features.Messaging;

namespace Worker.Features.CacheInvalidation;

internal static class CacheInvalidationHandler
{
    /// <summary>Only document changes matter; the cache itself decides which entries a change makes stale.</summary>
    public static HandleEvent Create(InvalidateDocument invalidate)
    {
        ArgumentNullException.ThrowIfNull(invalidate);

        return (outboxEvent, cancellationToken) =>
        {
            if (outboxEvent.EventType != EventTypes.DocumentChanged)
            {
                return Task.CompletedTask;
            }

            var change = outboxEvent.Payload.Deserialize<DocumentChanged>(EventJson.Options);
            return change is null ? Task.CompletedTask : invalidate(change, cancellationToken);
        };
    }
}
