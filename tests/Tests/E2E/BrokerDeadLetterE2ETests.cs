using AwesomeAssertions;
using Core.Features.Events;
using Core.Features.Messaging;
using RabbitMQ.Client;
using Xunit;

namespace Tests.E2E;

/// <summary>Checks on a real broker what the in-memory suite cannot: the RabbitMQ adapter's topology and dead-letter path.</summary>
[Collection(AppHostDefinition.Name)]
public sealed class BrokerDeadLetterE2ETests(AppHostFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnreadableMessageEndsUpInTheAuditDeadLetterQueue()
    {
        var connectionString = await fixture.GetConnectionStringAsync("rabbitmq");
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var connection = await factory.CreateConnectionAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        var marker = "not-json-" + Guid.NewGuid();

        await channel.BasicPublishAsync(
            RabbitMqTransport.Exchange, EventTypes.DocumentChanged, mandatory: false, new BasicProperties(), System.Text.Encoding.UTF8.GetBytes(marker), Ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Timeout);
        var found = false;
        while (!found)
        {
            var message = await channel.BasicGetAsync("audit.dlq", autoAck: true, timeout.Token);
            if (message is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
                continue;
            }

            found = System.Text.Encoding.UTF8.GetString(message.Body.Span) == marker;
        }

        found.Should().BeTrue();
    }
}
