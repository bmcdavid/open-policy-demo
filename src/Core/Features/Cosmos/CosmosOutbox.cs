using System.IO;
using System.Text.Json;
using Core.Features.Documents;
using Core.Features.Events;
using Microsoft.Azure.Cosmos;

namespace Core.Features.Cosmos;

/// <summary>
/// The outbox as stored in Cosmos: the item layout, the write side (standalone or inside a document batch) and the
/// read side (turning a change feed batch back into events). Nothing outside this module knows the layout.
/// </summary>
public static class CosmosOutbox
{
    internal static OutboxItem ToItem(OutboxEvent outboxEvent) => OutboxItem.From(outboxEvent);

    /// <summary>Adds the event to a transactional batch, so it commits atomically with the writes already in the batch.</summary>
    public static TransactionalBatch AddTo(this TransactionalBatch batch, OutboxEvent outboxEvent)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return batch.CreateItem(ToItem(outboxEvent));
    }

    /// <summary>Writes a standalone outbox item; a failure surfaces as <see cref="StoreUnavailableException"/> (fail closed).</summary>
    public static WriteOutboxEvent Write(Database database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var container = database.GetContainer(CosmosNames.Documents);

        return async (outboxEvent, cancellationToken) =>
        {
            try
            {
                await container.CreateItemAsync(ToItem(outboxEvent), new PartitionKey(outboxEvent.Tenant), cancellationToken: cancellationToken);
            }
            catch (CosmosException ex)
            {
                throw new StoreUnavailableException("Cosmos outbox write failed", ex);
            }
        };
    }

    /// <summary>
    /// Publishes the outbox items in a change feed batch, in order, and ignores documents. A publish failure propagates
    /// so the change feed processor does not checkpoint and delivers the batch again (at least once). A malformed outbox
    /// item throws <see cref="InvalidDataException"/> and blocks the lease until the data is fixed.
    /// </summary>
    public static async Task RelayAsync(IReadOnlyCollection<JsonElement> changes, PublishEvent publish, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(publish);

        foreach (var change in changes)
        {
            if (!change.TryGetProperty("type", out var type) || type.GetString() != CosmosNames.OutboxType)
            {
                continue;
            }

            await publish(Read(change), cancellationToken);
        }
    }

    private static OutboxEvent Read(JsonElement change)
    {
        var id = change.TryGetProperty("id", out var idProperty) ? idProperty.ToString() : "<no id>";

        try
        {
            var item = change.Deserialize<OutboxItem>(EventJson.Options);
            return item?.ToEvent() ?? throw new InvalidDataException($"Outbox item {id} deserialized to nothing");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Malformed outbox item {id}", ex);
        }
    }
}
