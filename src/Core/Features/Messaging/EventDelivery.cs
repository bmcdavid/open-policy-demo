using System.Text.Json;
using Core.Features.Events;
using Microsoft.Extensions.Logging;

namespace Core.Features.Messaging;

public sealed record EventDeliveryOptions(int MaxAttempts = RetryPolicy.DefaultMaxAttempts, Func<int, TimeSpan>? RetryDelay = null);

/// <summary>
/// Reliable, at-least-once delivery of outbox events to handlers, as one module. A caller publishes events and runs
/// subscriptions; topology, retry counting and delays, dead-lettering, per-subscription isolation, log scopes and
/// shutdown are all handled here, over an <see cref="IBrokerTransport"/>.
/// </summary>
public sealed partial class EventDelivery(IBrokerTransport transport, ILogger<EventDelivery> logger, EventDeliveryOptions? options = null)
{
    private readonly EventDeliveryOptions _options = options ?? new EventDeliveryOptions();
    private readonly TaskCompletionSource _declared = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Publishes an event. Waits until <see cref="RunAsync"/> has declared the topology, then completes once the broker has the message.</summary>
    public PublishEvent Publish => PublishAsync;

    /// <summary>
    /// Declares the topology, then consumes every subscription until cancelled. A failing handler is retried and
    /// dead-lettered without affecting other subscriptions. A transport failure stops all subscriptions and surfaces.
    /// </summary>
    public async Task RunAsync(IReadOnlyList<Subscription> subscriptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        try
        {
            await transport.DeclareAsync(subscriptions, cancellationToken);
            _declared.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            _declared.TrySetCanceled(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            _declared.TrySetException(ex);
            throw;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await Task.WhenAll(subscriptions.Select(subscription => ConsumeAsync(subscription, linked)));
    }

    private async Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outboxEvent);

        await _declared.Task.WaitAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(outboxEvent, EventJson.Options);
        await transport.PublishAsync(
            new BrokerMessage(outboxEvent.Id, outboxEvent.EventType, body, outboxEvent.TraceId, outboxEvent.OccurredAt),
            cancellationToken);
    }

    private async Task ConsumeAsync(Subscription subscription, CancellationTokenSource linked)
    {
        try
        {
            await transport.ConsumeAsync(subscription, (delivery, token) => OnDeliveryAsync(subscription, delivery, token), linked.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogConsumerFailed(ex, subscription.Name);
            await linked.CancelAsync();
            throw;
        }
    }

    private async Task OnDeliveryAsync(Subscription subscription, Delivery delivery, CancellationToken cancellationToken)
    {
        var outcome = await EventMessageProcessor.ProcessAsync(
            delivery.Body, delivery.PriorAttempts, subscription.Handle, logger, _options.MaxAttempts, cancellationToken);

        var delay = outcome == MessageOutcome.Retry
            ? (_options.RetryDelay ?? RetryPolicy.DefaultDelay)(delivery.PriorAttempts + 1)
            : TimeSpan.Zero;
        await delivery.Settle(outcome, delay, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The transport failed while consuming {Subscription}; stopping all subscriptions")]
    private partial void LogConsumerFailed(Exception exception, string subscription);
}
