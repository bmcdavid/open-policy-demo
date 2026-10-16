using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Core.Features.Authorization;
using Core.Features.Documents;
using Xunit;

namespace Tests.E2E;

[Collection(AppHostDefinition.Name)]
public sealed class DocumentsE2ETests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> SendAsync(
        string user, HttpMethod method, string path, string environment = "dev", object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await fixture.GetTokenAsync(user));
        request.Headers.Add(DemoEnvironments.HeaderName, environment);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await fixture.ApiClient.SendAsync(request, Ct);
    }

    private async Task<Document> FirstDocumentAsync(string user, string tenant)
    {
        using var response = await SendAsync(user, HttpMethod.Get, "/documents?tenant=" + tenant);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<Document>>(Ct))![0];
    }

    private async Task<Document> CreateAsync(string user, string tenant)
    {
        using var response = await SendAsync(user, HttpMethod.Post, "/documents", body: new { title = "temp", tenant });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Document>(Ct))!;
    }

    private static async Task<string[]> ReasonsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.GetProperty("reasons").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    [Fact]
    public async Task reader_acme_can_get_acme_document_in_dev()
    {
        var document = await FirstDocumentAsync("reader-acme", "acme");

        using var response = await SendAsync("reader-acme", HttpMethod.Get, "/documents/" + document.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task reader_acme_cannot_update()
    {
        var document = await FirstDocumentAsync("reader-acme", "acme");

        using var response = await SendAsync("reader-acme", HttpMethod.Put, "/documents/" + document.Id, body: new { title = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonsAsync(response)).Should().Contain("role does not permit action");
    }

    [Fact]
    public async Task editor_acme_gets_403_updating_prod()
    {
        var document = await FirstDocumentAsync("editor-acme", "acme");

        using var response = await SendAsync("editor-acme", HttpMethod.Put, "/documents/" + document.Id, "prod", new { title = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonsAsync(response)).Should().Contain("writes in prod require admin");
    }

    [Fact]
    public async Task admin_acme_gets_403_deleting_prod()
    {
        var document = await CreateAsync("admin-acme", "acme");

        using var response = await SendAsync("admin-acme", HttpMethod.Delete, "/documents/" + document.Id, "prod");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonsAsync(response)).Should().Contain("deletes in prod are restricted to platform-admin");
    }

    [Fact]
    public async Task platform_admin_can_delete_prod()
    {
        var document = await CreateAsync("platform-admin", "acme");

        using var response = await SendAsync("platform-admin", HttpMethod.Delete, "/documents/" + document.Id, "prod");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task editor_globex_cannot_read_acme_document()
    {
        var document = await FirstDocumentAsync("platform-admin", "acme");

        using var response = await SendAsync("editor-globex", HttpMethod.Get, "/documents/" + document.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonsAsync(response)).Should().Contain("cross-tenant access denied");
    }

    [Fact]
    public async Task no_token_returns_401()
    {
        using var response = await fixture.ApiClient.GetAsync(new Uri("/documents", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
