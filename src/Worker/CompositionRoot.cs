using Core;
using Core.Features.Cosmos;
using Core.Features.Documents;
using Core.Features.Events;
using Core.Features.Messaging;
using Core.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Worker.Features.Audit;
using Worker.Features.CacheInvalidation;
using Worker.Features.Relay;

namespace Worker;

// https://blog.ploeh.dk/2011/07/28/CompositionRoot/
internal static class CompositionRoot
{
    public static void Compose(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddServiceDefaults();

        builder.AddCosmosStorage();
        builder.AddDocumentCache();
        builder.AddRabbitMQClient(ConnectionNames.RabbitMq);

        // Delegates are stateless closures over the singleton clients, so they are singletons too.
        builder.Services.AddSingleton<IBrokerTransport>(sp => new RabbitMqTransport(
            sp.GetRequiredService<IConnection>(), sp.GetRequiredService<ILogger<RabbitMqTransport>>()));
        builder.Services.AddSingleton(sp => new EventDelivery(
            sp.GetRequiredService<IBrokerTransport>(), sp.GetRequiredService<ILogger<EventDelivery>>()));
        builder.Services.AddSingleton(sp => sp.GetRequiredService<EventDelivery>().Publish);

        // Hosted services start in registration order: storage (initialized by AddCosmosStorage) exists before anything reads it. Publishing waits for the
        // delivery service to declare the topology, so the relay and the consumers can start together.
        builder.Services.AddHostedService<OutboxRelayService>();
        builder.Services.AddHostedService<DeliveryService>();
    }
}
