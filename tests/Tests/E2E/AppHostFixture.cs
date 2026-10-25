using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Tests.E2E;

/// <summary>
/// Starts the Aspire AppHost, or with <c>E2E_TARGET=k8s</c> targets a running cluster through <c>E2E_API_URL</c> and
/// <c>E2E_KEYCLOAK_URL</c> instead. In k8s mode the sidecars and the broker are not reachable, so the members that need
/// them throw; tests that use them carry <c>[Trait("Target", "aspire-only")]</c> and are filtered out.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private const string TokenPath = "/realms/opa-demo/protocol/openid-connect/token";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;
    private Uri? _opaUri;
    private Uri? _cedarUri;

    public Uri KeycloakUri { get; private set; } = null!;

    public Uri OpaUri => _opaUri ?? throw AspireOnly();

    public Uri CedarUri => _cedarUri ?? throw AspireOnly();

    public HttpClient ApiClient { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("E2E_TARGET") == "k8s")
        {
            KeycloakUri = RequiredUri("E2E_KEYCLOAK_URL");
            ApiClient = new HttpClient { BaseAddress = RequiredUri("E2E_API_URL") };
            return;
        }

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        _app = await builder.BuildAsync();

        // Covers image pulls too, so a stuck container fails the run with a timeout instead of hanging it.
        using var timeout = new CancellationTokenSource(StartupTimeout);
        await _app.StartAsync(timeout.Token);
        await WaitForApiAsync(timeout.Token);

        _opaUri = _app.GetEndpoint("opa", "http");
        _cedarUri = _app.GetEndpoint("cedar", "http");
        KeycloakUri = _app.GetEndpoint("keycloak", "oidc");
        ApiClient = _app.CreateHttpClient("api");
    }

    /// <summary>Waits for the api, but fails at once, naming the resource, if anything fails to start (the api would then wait forever).</summary>
    private async Task WaitForApiAsync(CancellationToken cancellationToken)
    {
        await foreach (var resourceEvent in _app!.ResourceNotifications.WatchAsync(cancellationToken))
        {
            var snapshot = resourceEvent.Snapshot;
            if (snapshot.State?.Text == KnownResourceStates.FailedToStart)
            {
                throw new InvalidOperationException($"Resource '{resourceEvent.Resource.Name}' failed to start; see the AppHost resource logs above.");
            }

            if (resourceEvent.Resource.Name == "api"
                && snapshot.State?.Text == KnownResourceStates.Running
                && snapshot.HealthStatus == HealthStatus.Healthy)
            {
                return;
            }
        }
    }

    public async Task<string> GetConnectionStringAsync(string resourceName) =>
        await (_app ?? throw AspireOnly()).GetConnectionStringAsync(resourceName)
            ?? throw new InvalidOperationException($"No connection string for {resourceName}");

    private static InvalidOperationException AspireOnly() => new("aspire-only");

    private static Uri RequiredUri(string variable) =>
        new(Environment.GetEnvironmentVariable(variable) ?? throw new InvalidOperationException($"{variable} is required when E2E_TARGET=k8s"));

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
