using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Tests.E2E;

public sealed class AppHostFixture : IAsyncLifetime
{
    private const string TokenPath = "/realms/opa-demo/protocol/openid-connect/token";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private DistributedApplication? _app;

    public Uri KeycloakUri { get; private set; } = null!;

    public Uri OpaUri { get; private set; } = null!;

    public HttpClient ApiClient { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        _app = await builder.BuildAsync();
        await _app.StartAsync();

        using var timeout = new CancellationTokenSource(StartupTimeout);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("api", timeout.Token);

        OpaUri = _app.GetEndpoint("opa", "http");
        KeycloakUri = _app.GetEndpoint("keycloak", "oidc");
        ApiClient = _app.CreateHttpClient("api");
    }

    public async ValueTask DisposeAsync()
    {
        ApiClient?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    public async Task<string> GetTokenAsync(string username)
    {
        using var http = new HttpClient { BaseAddress = KeycloakUri };
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "demo-api",
            ["username"] = username,
            ["password"] = "Passw0rd!",
        });
        using var response = await http.PostAsync(new Uri(TokenPath, UriKind.Relative), form);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }
}

[CollectionDefinition(Name)]
public sealed class AppHostDefinition : ICollectionFixture<AppHostFixture>
{
    public const string Name = "AppHost";
}
