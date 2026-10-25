using System.Net;
using System.Text;
using AwesomeAssertions;
using Core.Features.Authorization;
using Xunit;

namespace Tests.CoreFeatures;

public sealed class CedarPolicyTests : IDisposable
{
    private HttpClient? _http;

    private static readonly PolicyInput Input = new(
        new PolicySubject("u1", ["reader"], "acme"), "dev", PolicyActions.Read, new PolicyResource("document", "acme", "d1"));

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private (DecidePolicy Client, StubHandler Handler) Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://cedar.test/") };
        return (CedarPolicy.Decide(_http), handler);
    }

    public void Dispose() => _http?.Dispose();

    [Fact]
    public async Task DecideAsyncReturnsAllowDecision()
    {
        var (client, _) = Create(_ => Task.FromResult(Json("""{"decision":"Allow","diagnostics":{"reason":["role_read"],"errors":[]}}""")));

        var decision = await client(Input, CancellationToken.None);

        decision.Allow.Should().BeTrue();
        decision.Reasons.Should().BeEmpty();
    }

    [Fact]
    public async Task DecideAsyncReturnsDecisionWithForbidReasons()
    {
        var (client, _) = Create(_ => Task.FromResult(Json("""{"decision":"Deny","diagnostics":{"reason":["cross-tenant access denied"],"errors":[]}}""")));

        var decision = await client(Input, CancellationToken.None);

        decision.Allow.Should().BeFalse();
        decision.Reasons.Should().ContainSingle().Which.Should().Be("cross-tenant access denied");
    }

    [Fact]
    public async Task DecideAsyncDefaultsToRoleDoesNotPermitActionWhenReasonsEmptyOnDeny()
    {
        var (client, _) = Create(_ => Task.FromResult(Json("""{"decision":"Deny","diagnostics":{"reason":[],"errors":[]}}""")));

        var decision = await client(Input, CancellationToken.None);

        decision.Allow.Should().BeFalse();
        decision.Reasons.Should().ContainSingle().Which.Should().Be("role does not permit action");
    }

    [Fact]
    public async Task DecideAsyncPostsParcAndContextToIsAuthorizedPath()
    {
        var (client, handler) = Create(_ => Task.FromResult(Json("""{"decision":"Allow","diagnostics":{"reason":[],"errors":[]}}""")));

        await client(Input, CancellationToken.None);

        handler.RequestUri!.AbsolutePath.Should().Be("/v1/is_authorized");
        handler.RequestBody.Should().Contain("\"principal\":\"User::\\\"u1\\\"\"")
            .And.Contain("\"action\":\"Action::\\\"read\\\"\"")
            .And.Contain("\"resource\":\"Resource::\\\"document\\\"\"")
            .And.Contain("\"roles\":[\"reader\"]")
            .And.Contain("\"tenant\":\"acme\"")
            .And.Contain("\"resource_tenant\":\"acme\"")
            .And.Contain("\"environment\":\"dev\"");
    }

    [Fact]
    public async Task DecideAsyncThrowsUnavailableOn500()
    {
        var (client, _) = Create(_ => Task.FromResult(Json("{}", HttpStatusCode.InternalServerError)));

        await FluentActions.Awaiting(() => client(Input, CancellationToken.None))
            .Should().ThrowAsync<PolicyEngineUnavailableException>();
    }

    [Fact]
    public async Task DecideAsyncThrowsUnavailableOnConnectionFailure()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("refused"));

        await FluentActions.Awaiting(() => client(Input, CancellationToken.None))
            .Should().ThrowAsync<PolicyEngineUnavailableException>();
    }

    [Fact]
    public async Task DecideAsyncThrowsUnavailableWhenResultMissing()
    {
        var (client, _) = Create(_ => Task.FromResult(Json("null")));

        await FluentActions.Awaiting(() => client(Input, CancellationToken.None))
            .Should().ThrowAsync<PolicyEngineUnavailableException>();
    }

    [Fact]
    public async Task DecideAsyncThrowsUnavailableOnTimeout()
    {
        var (client, _) = Create(_ => throw new TaskCanceledException("timeout"));

        await FluentActions.Awaiting(() => client(Input, CancellationToken.None))
            .Should().ThrowAsync<PolicyEngineUnavailableException>();
    }
}
