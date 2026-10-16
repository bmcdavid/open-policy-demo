using System.Net;
using System.Text;
using AwesomeAssertions;
using Core.Features.Authorization;
using Xunit;

namespace Tests.CoreFeatures;

public sealed class OpaPolicyClientTests
{
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

    private static (OpaPolicyClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://opa.test/") };
        return (new OpaPolicyClient(http), handler);
    }

    [Fact]
    public async Task DecideAsync_returns_decision_from_result()
    {
        var (client, _) = Create(_ => Task.FromResult(Json("""{"result":{"allow":false,"reasons":["cross-tenant access denied"]}}""")));

        var decision = await client.DecideAsync(Input, CancellationToken.None);

        decision.Allow.Should().BeFalse();
        decision.Reasons.Should().ContainSingle().Which.Should().Be("cross-tenant access denied");
    }

    [Fact]
    public async Task DecideAsync_posts_input_wrapper_to_authz_decision_path()
    {
        var (client, handler) = Create(_ => Task.FromResult(Json("""{"result":{"allow":true,"reasons":[]}}""")));

        await client.DecideAsync(Input, CancellationToken.None);

        handler.RequestUri!.AbsolutePath.Should().Be("/v1/data/authz/decision");
        handler.RequestBody.Should().Contain("\"input\":{\"subject\":{\"id\":\"u1\",\"roles\":[\"reader\"],\"tenant\":\"acme\"}")
            .And.Contain("\"environment\":\"dev\"").And.Contain("\"action\":\"read\"");
    }

    [Fact]
    public async Task DecideAsync_throws_unavailable_on_500()
    {
        var (client, _) = Create(_ => Task.FromResult(Json("{}", HttpStatusCode.InternalServerError)));

        await FluentActions.Awaiting(() => client.DecideAsync(Input, CancellationToken.None))
            .Should().ThrowAsync<PolicyEngineUnavailableException>();
    }

    [Fact]
    public async Task DecideAsync_throws_unavailable_on_connection_failure()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("refused"));

        await FluentActions.Awaiting(() => client.DecideAsync(Input, CancellationToken.None))
            .Should().ThrowAsync<PolicyEngineUnavailableException>();
    }

    [Fact]
    public async Task DecideAsync_throws_unavailable_when_result_missing()
    {
        var (client, _) = Create(_ => Task.FromResult(Json("{}")));

        await FluentActions.Awaiting(() => client.DecideAsync(Input, CancellationToken.None))
            .Should().ThrowAsync<PolicyEngineUnavailableException>();
    }

    [Fact]
    public async Task DecideAsync_throws_unavailable_on_timeout()
    {
        var (client, _) = Create(_ => throw new TaskCanceledException("timeout"));

        await FluentActions.Awaiting(() => client.DecideAsync(Input, CancellationToken.None))
            .Should().ThrowAsync<PolicyEngineUnavailableException>();
    }
}
