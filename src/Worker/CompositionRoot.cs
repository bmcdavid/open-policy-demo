using Core;
using Core.Features.Audit;
using Core.Features.Cosmos;
using Core.Features.Documents;
using Core.Features.Events;
using Core.Features.Messaging;
using Core.Features.Valkey;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Core.Hosting;
using StackExchange.Redis;
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

        builder.AddCosmosClient();
        builder.AddRabbitMQClient(ConnectionNames.RabbitMq);
        builder.AddValkeyClient();

        // Delegates are stateless closures over the singleton clients, so they are singletons too.
        builder.Services.AddSingleton<IBrokerTransport>(sp => new RabbitMqTransport(
            sp.GetRequiredService<IConnection>(), sp.GetRequiredService<ILogger<RabbitMqTransport>>()));
        builder.Services.AddSingleton(sp => new EventDelivery(
            sp.GetRequiredService<IBrokerTransport>(), sp.GetRequiredService<ILogger<EventDelivery>>()));
        builder.Services.AddSingleton(sp => sp.GetRequiredService<EventDelivery>().Publish);
        builder.Services.AddSingleton(sp => CosmosAudit.Upsert(sp.GetRequiredService<Database>()));
        builder.Services.AddSingleton<ICacheBackend>(sp => new ValkeyCacheBackend(sp.GetRequiredService<IConnectionMultiplexer>()));
        builder.Services.AddSingleton<DocumentCache>();
        builder.Services.AddSingleton<InvalidateDocument>(sp => sp.GetRequiredService<DocumentCache>().InvalidateAsync);

        // Hosted services start in registration order: storage exists before anything reads it. Publishing waits for the
        // delivery service to declare the topology, so the relay and the consumers can start together.
        builder.Services.AddHostedService<CosmosInitializerService>();
        builder.Services.AddHostedService<OutboxRelayService>();
        builder.Services.AddHostedService<DeliveryService>();
    }
}
