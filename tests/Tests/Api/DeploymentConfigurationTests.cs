using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Tests.Support;
using Xunit;

namespace Tests.Api;

public sealed class DeploymentConfigurationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void InvalidDeploymentConfigurationStopsTheApiFromStarting()
    {
        using var factory = new ApiFactory();
        using var misconfigured = factory.WithWebHostBuilder(b => b
            .UseSetting("Deployment:AllowedEnvironments:0", "prod")
            .UseSetting("Deployment:DefaultEnvironment", "dev"));

        FluentActions.Invoking(() => misconfigured.CreateClient()).Should().Throw<InvalidOperationException>()
            .WithMessage("*Deployment:DefaultEnvironment*");
    }

    [Fact]
    public async Task ProdDeploymentRejectsDevAndDefaultsAMissingHeaderToProd()
    {
        using var factory = new ApiFactory();
        using var prod = factory.WithWebHostBuilder(b => b
            .UseSetting("Deployment:AllowedEnvironments:0", "prod")
            .UseSetting("Deployment:DefaultEnvironment", "prod"));
        var policy = factory.Policy;
        using var dev = prod.CreateClient();
        dev.DefaultRequestHeaders.Add(ApiFactory.UserHeader, "u1");
        dev.DefaultRequestHeaders.Add(ApiFactory.RolesHeader, "reader");
        dev.DefaultRequestHeaders.Add(ApiFactory.TenantHeader, "acme");

        dev.DefaultRequestHeaders.Add("X-Environment", "dev");
        using var rejected = await dev.GetAsync(new Uri("/documents", UriKind.Relative), Ct);
        dev.DefaultRequestHeaders.Remove("X-Environment");
        using var defaulted = await dev.GetAsync(new Uri("/documents", UriKind.Relative), Ct);

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("environment not allowed in this deployment");
        defaulted.StatusCode.Should().Be(HttpStatusCode.OK);
        policy.Inputs.Should().ContainSingle().Which.Environment.Should().Be("prod");
    }
}
