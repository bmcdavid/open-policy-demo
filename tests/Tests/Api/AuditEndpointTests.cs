using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Core.Features.Audit;
using Core.Features.Authorization;
using Core.Features.Documents;
using Tests.Support;
using Xunit;

namespace Tests.Api;

public sealed class AuditEndpointTests
{
    private static readonly string[] _admin = ["admin"];

    private static Uri Route(string path) => new(path, UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AuditEntry Entry(string id) =>
        new(id, "acme", "document.changed", DateTimeOffset.Parse("2026-10-22T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture), "trace", JsonDocument.Parse("{}").RootElement);

    [Fact]
    public async Task AuditWithoutTokenReturns401()
    {
        await using var factory = new ApiFactory { UseTestAuthentication = false };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route("/audit"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuditAsksPolicyForTheAuditActionOnTheRequestedTenant()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", _admin, "acme", "dev");

        using var response = await client.GetAsync(Route("/audit?tenant=globex"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var input = factory.Policy.Inputs.Should().ContainSingle().Subject;
        input.Action.Should().Be(PolicyActions.Audit);
        input.Resource.Should().Be(new PolicyResource("audit", "globex", null));
    }

    [Fact]
    public async Task AuditDefaultsToTheSubjectTenant()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", _admin, "acme", "dev");

        using var response = await client.GetAsync(Route("/audit"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.AuditQueries.Should().ContainSingle().Which.Tenant.Should().Be("acme");
    }

    [Fact]
    public async Task DeniedAuditReturns403AndNeverQueriesTheStore()
    {
        await using var factory = new ApiFactory();
        factory.Policy.Decide = _ => new PolicyDecision(false, ["role does not permit action"]);
        using var client = factory.CreateClientFor("u1", ["reader"], "acme", "dev");

        using var response = await client.GetAsync(Route("/audit"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.AuditQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task AuditReturnsTheStoredEntriesAndContinuationToken()
    {
        await using var factory = new ApiFactory();
        factory.Audit = (_, _) => Task.FromResult(new AuditPage([Entry("e1"), Entry("e2")], "next"));
        using var client = factory.CreateClientFor("u1", _admin, "acme", "dev");

        using var response = await client.GetAsync(Route("/audit"), Ct);

        var page = await response.Content.ReadFromJsonAsync<AuditPage>(Ct);
        page!.Entries.Select(e => e.Id).Should().Equal("e1", "e2");
        page.ContinuationToken.Should().Be("next");
    }

    [Theory]
    [InlineData("", 50)]
    [InlineData("&pageSize=10", 10)]
    [InlineData("&pageSize=0", 50)]
    [InlineData("&pageSize=100000", 100)]
    public async Task PageSizeDefaultsAndIsClamped(string query, int expected)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", _admin, "acme", "dev");

        using var response = await client.GetAsync(Route("/audit?tenant=acme" + query), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.AuditQueries.Should().ContainSingle().Which.PageSize.Should().Be(expected);
    }

    [Fact]
    public async Task AuditPassesTimeRangeAndContinuationToTheStore()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", _admin, "acme", "dev");

        using var response = await client.GetAsync(
            Route("/audit?tenant=acme&from=2026-10-01T00:00:00Z&to=2026-10-31T00:00:00Z&continuation=abc"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var query = factory.AuditQueries.Should().ContainSingle().Subject;
        query.From.Should().Be(DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        query.To.Should().Be(DateTimeOffset.Parse("2026-10-31T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        query.ContinuationToken.Should().Be("abc");
    }

    [Fact]
    public async Task AuditStoreOutageReturns503()
    {
        await using var factory = new ApiFactory();
        factory.Audit = (_, _) => throw new StoreUnavailableException("cosmos down");
        using var client = factory.CreateClientFor("u1", _admin, "acme", "dev");

        using var response = await client.GetAsync(Route("/audit"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
