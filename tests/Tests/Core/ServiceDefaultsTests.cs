using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Core.Hosting;
using StackExchange.Redis;
using Xunit;

namespace Tests.CoreFeatures;

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

    [Fact]
    public void AddServiceDefaultsReturnsTheBuilderForChaining()
    {
        var builder = Builder();

        builder.AddServiceDefaults().Should().BeSameAs(builder);
    }

    [Fact]
    public void ValkeyClientUsesShortTimeoutsSoAnOutageDoesNotSlowRequests()
    {
        var options = new ConfigurationOptions();

        ValkeyDefaults.Configure(options);

        options.AsyncTimeout.Should().Be(500);
        options.ConnectTimeout.Should().Be(2000);
    }
}
