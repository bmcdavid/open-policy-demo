using AwesomeAssertions;
using Core.Hosting;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using Xunit;

namespace Tests.Core;

public sealed partial class ServiceDefaultsTests
{
    [LoggerMessage(Level = LogLevel.Information, Message = "hello {Tenant}")]
    private static partial void LogHello(ILogger logger, string tenant);

    private static HostApplicationBuilder Builder(string? otlpEndpoint = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        if (otlpEndpoint is not null)
        {
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlpEndpoint;
        }

        return builder;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:4317")]
    public void LoggingBuildsAndWritesWithAndWithoutAnOtlpEndpoint(string? otlpEndpoint)
    {
        var builder = Builder(otlpEndpoint);
        builder.AddServiceDefaults();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        var act = () => LogHello(logger, "acme");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("http://localhost:4317", true)]
    public void TracesAreExportedOnlyWhenAnOtlpEndpointIsConfigured(string? otlpEndpoint, bool exported)
    {
        var builder = Builder(otlpEndpoint);
        builder.AddServiceDefaults();

        using var host = builder.Build();

        (host.Services.GetService<TracerProvider>() is not null).Should().Be(exported);
    }

    [Fact]
    public void AddServiceDefaultsReturnsTheBuilderForChaining()
    {
        var builder = Builder();

        builder.AddServiceDefaults().Should().BeSameAs(builder);
    }

    private const string _emulatorConnectionString =
        "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private static string ResolvedCosmosDatabase(string? configured)
    {
        var builder = Builder();
        builder.Configuration["ConnectionStrings:cosmos"] = _emulatorConnectionString;
        if (configured is not null)
        {
            builder.Configuration["Cosmos:Database"] = configured;
        }

        builder.AddCosmosClient();
        using var host = builder.Build();
        return host.Services.GetRequiredService<Database>().Id;
    }

    [Fact]
    public void CosmosDatabaseDefaultsToPolicyDemo() => ResolvedCosmosDatabase(null).Should().Be("policy-demo");

    [Fact]
    public void CosmosDatabaseFollowsConfiguration() => ResolvedCosmosDatabase("policy-demo-dev").Should().Be("policy-demo-dev");

    [Fact]
    public void ValkeyClientUsesShortTimeoutsSoAnOutageDoesNotSlowRequests()
    {
        var options = new ConfigurationOptions();

        ValkeyDefaults.Configure(options);

        options.AsyncTimeout.Should().Be(500);
        options.ConnectTimeout.Should().Be(2000);
    }
}
