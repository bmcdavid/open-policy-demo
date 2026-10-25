namespace Core.Features.Messaging;

/// <summary>One consumer's interest: a queue name, the event types bound to it ('#' for all) and what to do with each event.</summary>
public sealed record Subscription(string Name, string BindingKey, HandleEvent Handle);

public sealed record BrokerMessage(string MessageId, string EventType, ReadOnlyMemory<byte> Body, string? TraceId, DateTimeOffset OccurredAt);

/// <summary>Settles one delivery: acknowledge it, redeliver it after a delay with its attempt count incremented, or dead-letter it.</summary>
public delegate Task Settle(MessageOutcome outcome, TimeSpan retryDelay, CancellationToken cancellationToken);

public sealed record Delivery(ReadOnlyMemory<byte> Body, int PriorAttempts, Settle Settle);

/// <summary>
/// The broker seam. Adapters only move bytes and settle deliveries; decoding, retry counting, the delay curve and
/// dead-letter decisions are made above the seam so every adapter behaves the same. The four operations share one
/// broker session and one topology, so they are one cohesive interface.
/// </summary>
public interface IBrokerTransport
{
    /// <summary>Idempotently declares the topology (including dead-letter queues). Adapters retry until the broker is ready.</summary>
    Task DeclareAsync(IReadOnlyList<Subscription> subscriptions, CancellationToken cancellationToken);

    /// <summary>Completes only once the broker has taken responsibility for the message.</summary>
    Task PublishAsync(BrokerMessage message, CancellationToken cancellationToken);

    /// <summary>Delivers messages for one subscription until cancelled. Cancellation ends the call; any other exception means the transport failed.</summary>
    Task ConsumeAsync(Subscription subscription, Func<Delivery, CancellationToken, Task> onDelivery, CancellationToken cancellationToken);
}
