using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.Support;
using Xunit;

namespace Tests.Api;

public sealed class AuthOptionsTests
{
    private static bool RequireHttpsMetadata(string? configured)
    {
        using var factory = new ApiFactory();
        using var production = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Auth:Authority", "https://keycloak.example/realms/opa-demo");
            if (configured is not null)
            {
                b.UseSetting("Auth:RequireHttpsMetadata", configured);
            }
        });

        return production.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).RequireHttpsMetadata;
    }

    [Fact]
    public void RequireHttpsMetadataDefaultsToTrueOutsideDevelopment() => RequireHttpsMetadata(null).Should().BeTrue();

    [Fact]
    public void RequireHttpsMetadataFollowsConfiguration() => RequireHttpsMetadata("false").Should().BeFalse();
}
