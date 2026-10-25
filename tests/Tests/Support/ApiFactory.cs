using System.Security.Claims;
using System.Text.Encodings.Web;
using Core.Features.Audit;
using Core.Features.Authorization;
using Core.Features.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Support;

internal sealed class ApiFactory : WebApplicationFactory<global::Api.ApiMarker>
{
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";
    public const string TenantHeader = "X-Test-Tenant";

    public FakePolicyClient Policy { get; } = new();

    public bool UseTestAuthentication { get; init; } = true;

    public List<OutboxEvent> Events { get; } = [];

    /// <summary>When set, writing a decision event fails with this exception (Cosmos down).</summary>
    public Exception? OutboxThrows { get; set; }

    public List<AuditQuery> AuditQueries { get; } = [];

    public QueryAudit Audit { get; set; } = (_, _) => Task.FromResult(new AuditPage([], null));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<DecidePolicy>(Policy.DecideAsync);
            services.AddSingleton<WriteOutboxEvent>((outboxEvent, _) =>
            {
                if (OutboxThrows is not null)
                {
                    return Task.FromException(OutboxThrows);
                }

                Events.Add(outboxEvent);
                return Task.CompletedTask;
            });
            services.AddSingleton<QueryAudit>((query, cancellationToken) =>
            {
                AuditQueries.Add(query);
                return Audit(query, cancellationToken);
            });
            if (UseTestAuthentication)
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            }
        });
    }

    public HttpClient CreateClientFor(string? user, string[]? roles = null, string? tenant = null, string? environment = null)
    {
        var client = CreateClient();
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add(UserHeader, user);
        }

        if (roles is not null)
        {
            client.DefaultRequestHeaders.Add(RolesHeader, string.Join(',', roles));
        }

        if (tenant is not null)
        {
            client.DefaultRequestHeaders.Add(TenantHeader, tenant);
        }

        if (environment is not null)
        {
            client.DefaultRequestHeaders.Add(DemoEnvironments.HeaderName, environment);
        }

        return client;
    }
}

internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiFactory.UserHeader, out var user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", user.ToString()) };
        if (Request.Headers.TryGetValue(ApiFactory.RolesHeader, out var roles))
        {
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim("roles", r)));
        }

        if (Request.Headers.TryGetValue(ApiFactory.TenantHeader, out var tenant))
        {
            claims.Add(new Claim("tenant", tenant.ToString()));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
