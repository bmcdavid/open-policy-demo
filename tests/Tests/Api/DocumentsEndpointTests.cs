using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Core.Features.Authorization;
using Core.Features.Documents;
using Microsoft.Extensions.DependencyInjection;
using Tests.Support;
using Xunit;

namespace Tests.Api;

public sealed class DocumentsEndpointTests
{
    private static readonly string[] Reader = ["reader"];

    private static Uri Route(string path) => new(path, UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Document> SeededDocumentAsync(ApiFactory factory, string tenant) =>
        (await factory.Services.GetRequiredService<IDocumentStore>().ListAsync(tenant, Ct))[0];

    [Fact]
    public async Task DocumentsWithoutTokenReturns401()
    {
        await using var factory = new ApiFactory { UseTestAuthentication = false };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingEnvironmentHeaderReturns400()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", Reader, "acme");

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Policy.Inputs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("PROD")]
    [InlineData("production")]
    [InlineData("dev;prod")]
    [InlineData("")]
    public async Task InvalidEnvironmentValuesReturn400(string environment)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", Reader, "acme", environment);

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Policy.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task DeniedDecisionReturns403WithReasons()
    {
        await using var factory = new ApiFactory();
        factory.Policy.Decide = _ => new PolicyDecision(false, ["cross-tenant access denied"]);
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");
        var document = await SeededDocumentAsync(factory, "globex");

        using var response = await client.GetAsync(Route($"/documents/{document.Id}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("reasons")[0].GetString().Should().Be("cross-tenant access denied");
    }

    [Fact]
    public async Task AllowedGetReturnsDocument()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");
        var document = await SeededDocumentAsync(factory, "acme");

        var body = await client.GetFromJsonAsync<Document>(Route($"/documents/{document.Id}"), Ct);

        body.Should().Be(document);
    }

    [Fact]
    public async Task MissingDocumentReturns404WithoutCallingPolicy()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");

        using var response = await client.GetAsync(Route($"/documents/{Guid.NewGuid()}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Policy.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task EngineUnavailableReturns503Never200()
    {
        await using var factory = new ApiFactory();
        factory.Policy.Throws = new PolicyEngineUnavailableException("down");
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task UnauditableDecisionReturns503Never200()
    {
        await using var factory = new ApiFactory { OutboxThrows = new StoreUnavailableException("cosmos down") };
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task EveryDecisionIsRecordedAsAnEvent()
    {
        await using var factory = new ApiFactory();
        factory.Policy.Decide = _ => new PolicyDecision(false, ["nope"]);
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Events.Should().ContainSingle().Which.EventType.Should().Be(Core.Features.Events.EventTypes.PolicyDecisionMade);
    }

    [Fact]
    public async Task CreateWithoutAnyTenantReturns400()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", ["editor"], environment: "dev");

        using var response = await client.PostAsJsonAsync(Route("/documents"), new { title = "hello" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TokenWithoutTenantOrRolesIsPassedToPolicyAsEmptyAndDeniedWhenPolicyDenies()
    {
        await using var factory = new ApiFactory();
        factory.Policy.Decide = input => new PolicyDecision(input.Subject.Roles.Count > 0, ["role does not permit action"]);
        using var client = factory.CreateClientFor("u1", environment: "dev");

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var input = factory.Policy.Inputs.Should().ContainSingle().Subject;
        input.Subject.Roles.Should().BeEmpty();
        input.Subject.Tenant.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateDefaultsTenantToSubjectTenant()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", ["editor"], "acme", "dev");

        using var response = await client.PostAsJsonAsync(Route("/documents"), new { title = "hello" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<Document>(Ct);
        created!.Tenant.Should().Be("acme");
        factory.Policy.Inputs.Should().ContainSingle().Which.Resource.Tenant.Should().Be("acme");
    }

    [Fact]
    public async Task ListPassesRequestedTenantToPolicy()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");

        using var response = await client.GetAsync(Route("/documents?tenant=globex"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var documents = await response.Content.ReadFromJsonAsync<List<Document>>(Ct);
        documents.Should().NotBeEmpty().And.OnlyContain(d => d.Tenant == "globex");
        var input = factory.Policy.Inputs.Should().ContainSingle().Subject;
        input.Action.Should().Be(PolicyActions.List);
        input.Resource.Tenant.Should().Be("globex");
    }

    [Fact]
    public async Task PolicyInputCarriesEnvironmentActionAndDocumentTenant()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", ["admin"], "acme", "prod");
        var document = await SeededDocumentAsync(factory, "globex");

        using var response = await client.DeleteAsync(Route($"/documents/{document.Id}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var input = factory.Policy.Inputs.Should().ContainSingle().Subject;
        input.Environment.Should().Be("prod");
        input.Action.Should().Be(PolicyActions.Delete);
        input.Resource.Should().Be(new PolicyResource("document", "globex", document.Id.ToString()));
    }

    [Fact]
    public async Task UpdateChangesTitleWhenAllowed()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", ["editor"], "acme", "dev");
        var document = await SeededDocumentAsync(factory, "acme");

        using var response = await client.PutAsJsonAsync(Route($"/documents/{document.Id}"), new { title = "renamed" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Document>(Ct))!.Title.Should().Be("renamed");
    }
}
