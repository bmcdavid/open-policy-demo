using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Tests.Support;
using Xunit;

namespace Tests.Api;

public sealed class WhoAmITests
{
    private static readonly Uri WhoAmI = new("/whoami", UriKind.Relative);

    [Fact]
    public async Task whoami_without_token_returns_401()
    {
        await using var factory = new ApiFactory { UseTestAuthentication = false };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(WhoAmI, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task whoami_returns_claims_for_authenticated_user()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", ["reader", "editor"], "acme");

        var body = await client.GetFromJsonAsync<Dictionary<string, JsonElement>>(WhoAmI, TestContext.Current.CancellationToken);

        body!["id"].GetString().Should().Be("u1");
        body["tenant"].GetString().Should().Be("acme");
        body["roles"].GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task buildinfo_is_public()
    {
        await using var factory = new ApiFactory { UseTestAuthentication = false };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/buildinfo", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
