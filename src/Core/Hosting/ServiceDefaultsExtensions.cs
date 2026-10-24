using Core.Features.Cosmos;
using Core.Features.Events;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace Core.Hosting;

/// <summary>Registration shared by the Api and the Worker, so each setting exists in exactly one place.</summary>
internal static class ServiceDefaultsExtensions
{
    /// <summary>Structured JSON to the console, plus OTLP logs and traces to the Aspire dashboard when an endpoint is configured. Call first.</summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var otlp = !string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console(new CompactJsonFormatter());

            if (otlp)
            {
                logger.WriteTo.OpenTelemetry();
            }
        });

        if (otlp)
        {
            // Service name and endpoint come from OTEL_SERVICE_NAME and OTEL_EXPORTER_OTLP_ENDPOINT. The Aspire Cosmos,
            // RabbitMQ and Redis clients add their own sources to this provider.
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
                .AddSource(EventTracing.SourceName)
                // Readiness probes hit /buildinfo every few seconds; their spans would bury real requests.
                .AddAspNetCoreInstrumentation(o => o.Filter = http => http.Request.Path != "/buildinfo")
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());
        }

        return builder;
    }

    /// <summary>
    /// Registers the Cosmos client with the serializer and gateway-mode options this repo needs, and the
    /// <see cref="Database"/> every Cosmos consumer uses (<c>Cosmos:Database</c>, default <c>policy-demo</c>).
    /// </summary>
    public static TBuilder AddCosmosClient<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAzureCosmosClient(ConnectionNames.Cosmos, configureClientOptions: CosmosSetup.ConfigureClientOptions);
        var databaseName = builder.Configuration["Cosmos:Database"] ?? CosmosNames.DefaultDatabase;
        builder.Services.AddSingleton(sp => sp.GetRequiredService<CosmosClient>().GetDatabase(databaseName));
        return builder;
    }

    /// <summary>Registers the Valkey (Redis protocol) client with short timeouts, so an outage falls through to the store quickly.</summary>
    public static TBuilder AddValkeyClient<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddRedisClient(ConnectionNames.Valkey, configureOptions: ValkeyDefaults.Configure);
        return builder;
    }
}
