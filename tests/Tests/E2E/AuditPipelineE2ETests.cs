using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Features.Audit;
using Core.Features.Authorization;
using Core.Features.Documents;
using Core.Features.Events;
using Xunit;

namespace Tests.E2E;

/// <summary>Exercises Cosmos, the outbox change feed relay, RabbitMQ, the audit worker and Valkey through the real API.</summary>
[Collection(AppHostDefinition.Name)]
public sealed class AuditPipelineE2ETests(AppHostFixture fixture)
{
    private static readonly TimeSpan PipelineTimeout = TimeSpan.FromSeconds(90);

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

    private async Task<Document> CreateAsync(string user, string tenant, string title)
    {
        using var response = await SendAsync(user, HttpMethod.Post, "/documents", body: new { title, tenant });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Document>(Ct))!;
    }

    private async Task<IReadOnlyList<AuditEntry>> AuditAsync(string user, string tenant)
    {
        using var response = await SendAsync(user, HttpMethod.Get, $"/audit?tenant={tenant}&pageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuditPage>(Ct))!.Entries;
    }

    /// <summary>The pipeline is asynchronous, so poll for the expected entry instead of sleeping a fixed time.</summary>
    private async Task<AuditEntry> EventuallyAsync(string user, string tenant, Func<AuditEntry, bool> match)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(PipelineTimeout);

        while (true)
        {
            var found = (await AuditAsync(user, tenant)).FirstOrDefault(match);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
        }
    }

    [Fact]
    public async Task CreatingADocumentProducesAnAuditEntryThroughTheOutboxAndBroker()
    {
        var document = await CreateAsync("editor-acme", "acme", "audited");

        var entry = await EventuallyAsync("admin-acme", "acme", e =>
            e.EventType == EventTypes.DocumentChanged && e.Payload.GetProperty("documentId").GetGuid() == document.Id);

        entry.Tenant.Should().Be("acme");
        entry.Payload.GetProperty("change").GetString().Should().Be("Created");
    }

    [Fact]
    public async Task PolicyDecisionsAreAudited()
    {
        var started = DateTimeOffset.UtcNow;
        await CreateAsync("editor-acme", "acme", "decided");

        var entry = await EventuallyAsync("admin-acme", "acme", e =>
            e.EventType == EventTypes.PolicyDecisionMade
            && e.OccurredAt >= started
            && e.Payload.GetProperty("action").GetString() == PolicyActions.Create
            && e.Payload.GetProperty("resourceType").GetString() == "document");

        entry.Payload.GetProperty("allow").GetBoolean().Should().BeTrue();
        entry.Payload.GetProperty("environment").GetString().Should().Be("dev");
        entry.Payload.GetProperty("subjectId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UpdatesAreVisibleImmediatelyDespiteTheReadCache()
    {
        var document = await CreateAsync("editor-acme", "acme", "before");
        using (var warm = await SendAsync("reader-acme", HttpMethod.Get, "/documents/" + document.Id))
        {
            warm.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var update = await SendAsync("editor-acme", HttpMethod.Put, "/documents/" + document.Id, body: new { title = "after" }))
        {
            update.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var read = await SendAsync("reader-acme", HttpMethod.Get, "/documents/" + document.Id);
        (await read.Content.ReadFromJsonAsync<Document>(Ct))!.Title.Should().Be("after");
    }

    [Fact]
    public async Task EditorCannotReadTheAuditTrail()
    {
        using var response = await SendAsync("editor-acme", HttpMethod.Get, "/audit?tenant=acme");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantAdminCannotReadAnotherTenantsAuditTrail()
    {
        using var response = await SendAsync("admin-acme", HttpMethod.Get, "/audit?tenant=globex");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlatformAdminCanReadAnyTenantsAuditTrail()
    {
        using var response = await SendAsync("platform-admin", HttpMethod.Get, "/audit?tenant=globex");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
