using AwesomeAssertions;
using Core.Features.Authorization;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests.CoreFeatures;

public sealed class DeploymentEnvironmentTests
{
    [Fact]
    public void UnsetBehavesAsToday()
    {
        var resolve = DeploymentEnvironment.Create(null, null);
        resolve("prod").Environment.Should().Be("prod");
        resolve(null).Error.Should().Be("X-Environment must be one of dev, staging, prod");
        resolve("qa").Error.Should().Be("X-Environment must be one of dev, staging, prod");
    }

    [Fact]
    public void KnownValueOutsideTheAllowlistIsRejected() =>
        DeploymentEnvironment.Create(["prod"], "prod")("dev").Error.Should().Be("environment not allowed in this deployment");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingOrEmptyHeaderUsesTheDefault(string? header) =>
        DeploymentEnvironment.Create(["staging", "prod"], "staging")(header).Environment.Should().Be("staging");

    [Fact]
    public void ExplicitAllowedValueWinsOverTheDefault() =>
        DeploymentEnvironment.Create(["dev", "staging", "prod"], "dev")("prod").Environment.Should().Be("prod");

    [Theory]
    [InlineData(new[] { "qa" }, null)]
    [InlineData(new[] { "prod" }, "dev")]
    [InlineData(new[] { "Prod" }, null)]
    public void InvalidConfigurationThrowsAtCreation(string[] allowed, string? defaultEnvironment) =>
        FluentActions.Invoking(() => DeploymentEnvironment.Create(allowed, defaultEnvironment)).Should().Throw<InvalidOperationException>();

    [Fact]
    public void FromConfigurationReadsTheDeploymentSection()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Deployment:AllowedEnvironments:0"] = "prod",
            ["Deployment:DefaultEnvironment"] = "prod",
        }).Build();
        var resolve = DeploymentEnvironment.FromConfiguration(configuration);
        resolve(null).Environment.Should().Be("prod");
        resolve("staging").Error.Should().Be("environment not allowed in this deployment");
    }
}
