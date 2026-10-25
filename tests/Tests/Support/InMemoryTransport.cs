using System.Collections.Concurrent;
using System.Threading.Channels;
using Core.Features.Messaging;

namespace Tests.Support;

/// <summary>An in-memory <see cref="IBrokerTransport"/> with topic matching ('#' or an exact event type) and test-only inspection.</summary>
internal sealed class InMemoryTransport : IBrokerTransport
{
    private sealed record Entry(byte[] Body, int Attempts);

    private sealed record Queue(string BindingKey, Channel<Entry> Channel);

    private readonly ConcurrentDictionary<string, Queue> _queues = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<byte[]>> _deadLettered = new();
    private int _unsettled;

    /// <summary>When true, every consumer throws as soon as it starts, like a lost broker connection.</summary>
    public bool FaultConsumers { get; set; }

    public Task DeclareAsync(IReadOnlyList<Subscription> subscriptions, CancellationToken cancellationToken)
    {
        foreach (var subscription in subscriptions)
        {
            _queues[subscription.Name] = new Queue(subscription.BindingKey, Channel.CreateUnbounded<Entry>());
            _deadLettered[subscription.Name] = new ConcurrentQueue<byte[]>();
        }

        return Task.CompletedTask;
    }

    public Task PublishAsync(BrokerMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        PublishRaw(message.EventType, message.Body.ToArray());
        return Task.CompletedTask;
    }

    /// <summary>Publishes bytes as they are, so tests can inject messages the module would never produce.</summary>
    public void PublishRaw(string eventType, byte[] body)
    {
        foreach (var queue in _queues.Values.Where(q => Matches(q.BindingKey, eventType)))
        {
            Interlocked.Increment(ref _unsettled);
            queue.Channel.Writer.TryWrite(new Entry(body, 0));
        }
    }

    public async Task ConsumeAsync(Subscription subscription, Func<Delivery, CancellationToken, Task> onDelivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(onDelivery);

        if (FaultConsumers)
        {
            throw new InvalidOperationException("broker connection lost");
        }

        var queue = _queues[subscription.Name];
        await foreach (var entry in queue.Channel.Reader.ReadAllAsync(cancellationToken))
        {
            var delivery = new Delivery(entry.Body, entry.Attempts, (outcome, delay, token) => SettleAsync(subscription.Name, queue, entry, outcome, delay, token));
            await onDelivery(delivery, cancellationToken);
        }
    }

    public IReadOnlyList<byte[]> DeadLettered(string queue) => [.. _deadLettered[queue]];

    /// <summary>Waits until every published message has been acknowledged or dead-lettered, including retries.</summary>
    public async Task DrainAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (Volatile.Read(ref _unsettled) > 0)
        {
            await Task.Delay(2, cts.Token);
        }
    }

    private async Task SettleAsync(string name, Queue queue, Entry entry, MessageOutcome outcome, TimeSpan delay, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case MessageOutcome.Ack:
                Interlocked.Decrement(ref _unsettled);
                break;
            case MessageOutcome.Retry:
                await Task.Delay(delay, cancellationToken);
                queue.Channel.Writer.TryWrite(entry with { Attempts = entry.Attempts + 1 });
                break;
            default:
                _deadLettered[name].Enqueue(entry.Body);
                Interlocked.Decrement(ref _unsettled);
                break;
        }
    }

    private static bool Matches(string bindingKey, string eventType) => bindingKey == "#" || bindingKey == eventType;
}
