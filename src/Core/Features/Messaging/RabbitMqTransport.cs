using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Core.Features.Messaging;

/// <summary>
/// RabbitMQ adapter. Everything broker-specific lives here: the topic exchange and dead-letter exchange, queue and
/// dead-letter queue naming, the attempt and trace headers, publisher confirms, and the republish-after-delay retry.
/// </summary>
public sealed partial class RabbitMqTransport(IConnection connection, ILogger<RabbitMqTransport> logger) : IBrokerTransport
{
    internal const string Exchange = "policy-demo.events";
    private const string DeadLetterExchange = "policy-demo.dlx";
    private const string TraceIdHeader = "trace-id";
    private const string AttemptHeader = "x-attempt";
    private const ushort Prefetch = 10;
    private const int MaxDeclareAttempts = 20;

    private static readonly TimeSpan DeclareRetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>Retries because the broker can accept connections before it is ready.</summary>
    public async Task DeclareAsync(IReadOnlyList<Subscription> subscriptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await DeclareOnceAsync(subscriptions, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < MaxDeclareAttempts && ex is AlreadyClosedException or BrokerUnreachableException or OperationInterruptedException)
            {
                LogBrokerNotReady(ex, attempt, MaxDeclareAttempts);
                await Task.Delay(DeclareRetryDelay, cancellationToken);
            }
        }
    }

    /// <summary>Opens a short-lived channel per call: a channel is not safe for concurrent publishers.</summary>
    public async Task PublishAsync(BrokerMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var options = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(options, cancellationToken);

        var properties = new BasicProperties
        {
            MessageId = message.MessageId,
            Type = message.EventType,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Headers = message.TraceId is null ? null : new Dictionary<string, object?> { [TraceIdHeader] = message.TraceId },
        };

        await channel.BasicPublishAsync(Exchange, message.EventType, mandatory: false, properties, message.Body, cancellationToken);
    }

    public async Task ConsumeAsync(Subscription subscription, Func<Delivery, CancellationToken, Task> onDelivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(onDelivery);

        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, Prefetch, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (sender, received) =>
            OnReceivedAsync(((AsyncEventingBasicConsumer)sender).Channel, subscription.Name, received, onDelivery, cancellationToken);
        await channel.BasicConsumeAsync(subscription.Name, autoAck: false, consumer, cancellationToken);

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static async Task DeclareOnceAsync(IReadOnlyList<Subscription> subscriptions, IConnection connection, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);

        foreach (var subscription in subscriptions)
        {
            var deadLetterQueue = subscription.Name + ".dlq";
            await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(deadLetterQueue, DeadLetterExchange, subscription.Name, cancellationToken: cancellationToken);

            var arguments = new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-dead-letter-routing-key"] = subscription.Name,
            };
            await channel.QueueDeclareAsync(subscription.Name, durable: true, exclusive: false, autoDelete: false, arguments, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(subscription.Name, Exchange, subscription.BindingKey, cancellationToken: cancellationToken);
        }
    }

    private Task DeclareOnceAsync(IReadOnlyList<Subscription> subscriptions, CancellationToken cancellationToken) =>
        DeclareOnceAsync(subscriptions, connection, cancellationToken);

    private static Task OnReceivedAsync(
        IChannel channel, string queue, BasicDeliverEventArgs received, Func<Delivery, CancellationToken, Task> onDelivery, CancellationToken cancellationToken)
    {
        var prior = AttemptsFrom(received.BasicProperties.Headers);
        var delivery = new Delivery(
            received.Body,
            prior,
            (outcome, delay, token) => SettleAsync(channel, queue, received, prior, outcome, delay, token));
        return onDelivery(delivery, cancellationToken);
    }

    private static async Task SettleAsync(
        IChannel channel, string queue, BasicDeliverEventArgs received, int prior, MessageOutcome outcome, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case MessageOutcome.Ack:
                await channel.BasicAckAsync(received.DeliveryTag, false, cancellationToken);
                break;
            case MessageOutcome.Retry:
                await Task.Delay(retryDelay, cancellationToken);
                await RepublishAsync(channel, queue, received, prior + 1, cancellationToken);
                await channel.BasicAckAsync(received.DeliveryTag, false, cancellationToken);
                break;
            default:
                await channel.BasicNackAsync(received.DeliveryTag, false, requeue: false, cancellationToken);
                break;
        }
    }

    private static async Task RepublishAsync(IChannel channel, string queue, BasicDeliverEventArgs received, int attempt, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, object?>(received.BasicProperties.Headers ?? new Dictionary<string, object?>())
        {
            [AttemptHeader] = attempt,
        };
        var properties = new BasicProperties(received.BasicProperties) { Headers = headers };

        // The default exchange routes straight to the named queue, so the retry reaches only this consumer.
        await channel.BasicPublishAsync(string.Empty, queue, mandatory: false, properties, received.Body, cancellationToken);
    }

    private static int AttemptsFrom(IDictionary<string, object?>? headers) =>
        headers is not null && headers.TryGetValue(AttemptHeader, out var value) && value is int attempts ? attempts : 0;

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ is not ready (attempt {Attempt} of {Max}); retrying")]
    private partial void LogBrokerNotReady(Exception exception, int attempt, int max);
}
