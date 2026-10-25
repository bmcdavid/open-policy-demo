namespace Core.Features.Events;

/// <summary>Durably records an event outside of any document write (for example a policy decision).</summary>
public delegate Task WriteOutboxEvent(OutboxEvent outboxEvent, CancellationToken cancellationToken);

/// <summary>Publishes an outbox event to the message broker. Completes only once the broker has confirmed it.</summary>
public delegate Task PublishEvent(OutboxEvent outboxEvent, CancellationToken cancellationToken);
