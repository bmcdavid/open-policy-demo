using System.Text.Json;
using Core.Features.Cosmos;
using Core.Features.Events;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Worker.Features.Relay;

/// <summary>Reads the Cosmos change feed of the documents container and relays outbox items to RabbitMQ, at least once.</summary>
internal sealed partial class OutboxRelayService(CosmosClient client, PublishEvent publish, ILogger<OutboxRelayService> logger) : IHostedService
{
    private ChangeFeedProcessor? _processor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var database = client.GetDatabase(CosmosNames.Database);

        _processor = database.GetContainer(CosmosNames.Documents)
            .GetChangeFeedProcessorBuilder<JsonElement>("outbox-relay", (changes, token) => CosmosOutbox.RelayAsync(changes, WithLogging(publish), token))
            .WithInstanceName($"{Environment.MachineName}-{Environment.ProcessId}")
            .WithLeaseContainer(database.GetContainer(CosmosNames.Leases))
            .WithStartTime(DateTime.UnixEpoch)
            .WithPollInterval(TimeSpan.FromSeconds(1))
            .WithErrorNotification((leaseToken, exception) =>
            {
                LogProcessorError(exception, leaseToken);
                return Task.CompletedTask;
            })
            .Build();

        await _processor.StartAsync();
    }

    private PublishEvent WithLogging(PublishEvent inner) => async (outboxEvent, cancellationToken) =>
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = outboxEvent.Id,
            ["TraceId"] = outboxEvent.TraceId,
            ["Tenant"] = outboxEvent.Tenant,
        });
        await inner(outboxEvent, cancellationToken);
        LogRelayed(outboxEvent.EventType);
    };

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Relayed {EventType} to RabbitMQ")]
    private partial void LogRelayed(string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Change feed processor error on lease {LeaseToken}; the batch will be retried")]
    private partial void LogProcessorError(Exception exception, string leaseToken);
}
