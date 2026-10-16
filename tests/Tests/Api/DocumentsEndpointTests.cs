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

    private static Document SeededDocument(ApiFactory factory, string tenant) =>
        factory.Services.GetRequiredService<IDocumentStore>().List(tenant)[0];

    [Fact]
    public async Task Documents_without_token_returns_401()
    {
        await using var factory = new ApiFactory { UseTestAuthentication = false };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Missing_environment_header_returns_400()
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
    public async Task Invalid_environment_values_return_400(string environment)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", Reader, "acme", environment);

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Policy.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task Denied_decision_returns_403_with_reasons()
    {
        await using var factory = new ApiFactory();
        factory.Policy.Decide = _ => new PolicyDecision(false, ["cross-tenant access denied"]);
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");
        var document = SeededDocument(factory, "globex");

        using var response = await client.GetAsync(Route($"/documents/{document.Id}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("reasons")[0].GetString().Should().Be("cross-tenant access denied");
    }

    [Fact]
    public async Task Allowed_get_returns_document()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");
        var document = SeededDocument(factory, "acme");

        var body = await client.GetFromJsonAsync<Document>(Route($"/documents/{document.Id}"), Ct);

        body.Should().Be(document);
    }

    [Fact]
    public async Task Missing_document_returns_404_without_calling_policy()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");

        using var response = await client.GetAsync(Route($"/documents/{Guid.NewGuid()}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Policy.Inputs.Should().BeEmpty();
    }

    [Fact]
    public async Task Engine_unavailable_returns_503_never_200()
    {
        await using var factory = new ApiFactory();
        factory.Policy.Throws = new PolicyEngineUnavailableException("down");
        using var client = factory.CreateClientFor("u1", Reader, "acme", "dev");

        using var response = await client.GetAsync(Route("/documents"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Token_without_tenant_or_roles_is_passed_to_policy_as_empty_and_denied_when_policy_denies()
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
    public async Task Create_defaults_tenant_to_subject_tenant()
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
    public async Task List_passes_requested_tenant_to_policy()
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
    public async Task Policy_input_carries_environment_action_and_document_tenant()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", ["admin"], "acme", "prod");
        var document = SeededDocument(factory, "globex");

        using var response = await client.DeleteAsync(Route($"/documents/{document.Id}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var input = factory.Policy.Inputs.Should().ContainSingle().Subject;
        input.Environment.Should().Be("prod");
        input.Action.Should().Be(PolicyActions.Delete);
        input.Resource.Should().Be(new PolicyResource("document", "globex", document.Id.ToString()));
    }

    [Fact]
    public async Task Update_changes_title_when_allowed()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("u1", ["editor"], "acme", "dev");
        var document = SeededDocument(factory, "acme");

        using var response = await client.PutAsJsonAsync(Route($"/documents/{document.Id}"), new { title = "renamed" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Document>(Ct))!.Title.Should().Be("renamed");
    }
}
