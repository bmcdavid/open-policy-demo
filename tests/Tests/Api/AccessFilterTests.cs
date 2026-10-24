using System.Security.Claims;
using System.Text.Json;
using Api.Features.Authorization;
using AwesomeAssertions;
using Core.Features.Authorization;
using Core.Features.Documents;
using Core.Features.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Support;
using Xunit;

namespace Tests.Api;

public sealed class AccessFilterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Fixture
    {
        public FakePolicyClient Policy { get; } = new();

        public InMemoryDocumentStore Store { get; } = new();

        public List<OutboxEvent> Events { get; } = [];

        public Exception? OutboxThrows { get; set; }

        public ResolveEnvironment Environment { get; set; } = DeploymentEnvironment.Create(null, null);

        /// <summary>Set when the endpoint behind the filter ran.</summary>
        public bool Acted { get; private set; }

        public DefaultHttpContext Http { get; } = Context();

        public AuthorizedDocument Document { get; } = new();

        public TenantScope Tenant { get; } = new();

        /// <summary>Runs the filter for <paramref name="filter"/> the way the routing pipeline does and returns what the endpoint would return.</summary>
        public async Task<object?> Run(AccessFilter filter, params object?[] arguments)
        {
            Http.RequestServices = new ServiceCollection()
                .AddSingleton<IDocumentStore>(Store)
                .AddSingleton<DecidePolicy>(Policy.DecideAsync)
                .AddSingleton<WriteOutboxEvent>((e, _) =>
                {
                    if (OutboxThrows is not null)
                    {
                        return Task.FromException(OutboxThrows);
                    }

                    Events.Add(e);
                    return Task.CompletedTask;
                })
                .AddSingleton(Environment)
                .AddSingleton(Document)
                .AddSingleton(Tenant)
                .BuildServiceProvider();

            // Create(Http, object?[]) would bind to the generic one-argument overload and nest the array, so spell out the arguments.
            var context = arguments.Length == 0
                ? EndpointFilterInvocationContext.Create(Http)
                : EndpointFilterInvocationContext.Create(Http, arguments[0]);

            return await filter.InvokeAsync(
                context,
                _ =>
                {
                    Acted = true;
                    return ValueTask.FromResult<object?>(Results.Ok());
                });
        }

        public Task<object?> RunExisting(string action, Guid id)
        {
            Http.Request.RouteValues["id"] = id.ToString();
            return Run(new AccessFilter(new AccessRequirement(action, Resource.Document, AccessScope.Document)));
        }

        public Task<object?> RunInTenant(string action, string resourceType = Resource.Document, params object?[] arguments) =>
            Run(new AccessFilter(new AccessRequirement(action, resourceType, AccessScope.Tenant)), arguments);
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "u1"), new Claim("roles", "reader"), new Claim("tenant", "acme")], "Test")),
        };
        context.Request.Headers[DemoEnvironments.HeaderName] = "dev";
        return context;
    }

    private static PolicyDecisionMade Payload(OutboxEvent outboxEvent) =>
        outboxEvent.Payload.Deserialize<PolicyDecisionMade>(EventJson.Options)!;

    private static int Status(object? result) => result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject.StatusCode!.Value;

    [Fact]
    public async Task AllowedExistingRunsTheEndpointAndSendsSubjectEnvironmentActionAndResourceToPolicy()
    {
        var f = new Fixture();
        var document = DocumentSeed.Documents[0];

        await f.RunExisting(PolicyActions.Read, document.Id);

        f.Acted.Should().BeTrue();
        f.Document.Value.Should().Be(document);
        var input = f.Policy.Inputs.Should().ContainSingle().Subject;
        input.Environment.Should().Be("dev");
        input.Action.Should().Be("read");
        input.Resource.Should().Be(new PolicyResource("document", document.Tenant, document.Id.ToString()));
        input.Subject.Id.Should().Be("u1");
        input.Subject.Tenant.Should().Be("acme");
        input.Subject.Roles.Should().Equal("reader");
    }

    [Fact]
    public async Task MissingDocumentIs404WithoutCallingPolicyOrTheEndpoint()
    {
        var f = new Fixture();

        var result = await f.RunExisting(PolicyActions.Read, Guid.NewGuid());

        Status(result).Should().Be(404);
        f.Policy.Inputs.Should().BeEmpty();
        f.Events.Should().BeEmpty();
        f.Acted.Should().BeFalse();
    }

    [Fact]
    public async Task MissingEnvironmentIs400BeforeTheDocumentIsLoadedOrPolicyIsAsked()
    {
        var f = new Fixture();
        f.Http.Request.Headers.Remove(DemoEnvironments.HeaderName);

        var existing = await f.RunExisting(PolicyActions.Read, Guid.NewGuid());
        var inTenant = await f.RunInTenant(PolicyActions.List);

        Status(existing).Should().Be(400);
        Status(inTenant).Should().Be(400);
        f.Policy.Inputs.Should().BeEmpty();
        f.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task EnvironmentOutsideTheAllowlistIs400BeforeTheDocumentIsLoadedOrPolicyIsAsked()
    {
        var f = new Fixture { Environment = DeploymentEnvironment.Create(["prod"], "prod") };

        var existing = await f.RunExisting(PolicyActions.Read, DocumentSeed.Documents[0].Id);
        var inTenant = await f.RunInTenant(PolicyActions.List);

        Status(existing).Should().Be(400);
        Status(inTenant).Should().Be(400);
        f.Policy.Inputs.Should().BeEmpty();
        f.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task DeniedIs403WithReasonsAndTheEndpointNeverRuns()
    {
        var f = new Fixture();
        f.Policy.Decide = _ => new PolicyDecision(false, ["nope"]);

        var result = await f.RunExisting(PolicyActions.Update, DocumentSeed.Documents[0].Id);

        Status(result).Should().Be(403);
        ((IValueHttpResult)result!).Value.Should().BeOfType<DenyBody>().Which.Reasons.Should().Equal("nope");
        f.Acted.Should().BeFalse();
    }

    [Fact]
    public async Task EngineUnavailableIs503AndRecordsNoEvent()
    {
        var f = new Fixture();
        f.Policy.Throws = new PolicyEngineUnavailableException("down");

        var result = await f.RunExisting(PolicyActions.Read, DocumentSeed.Documents[0].Id);

        Status(result).Should().Be(503);
        f.Events.Should().BeEmpty();
        f.Acted.Should().BeFalse();
    }

    [Fact]
    public async Task InTenantDefaultsToTheSubjectTenantAndAsksPolicyAboutTheGivenResourceType()
    {
        var f = new Fixture();

        await f.RunInTenant(PolicyActions.Audit, Resource.Audit);

        f.Tenant.Tenant.Should().Be("acme");
        f.Policy.Inputs.Should().ContainSingle().Which.Resource.Should().Be(new PolicyResource("audit", "acme", null));
    }

    [Fact]
    public async Task InTenantUsesTheQueryTenantInsteadOfTheSubjects()
    {
        var f = new Fixture();
        f.Http.Request.QueryString = new QueryString("?tenant=globex");

        await f.RunInTenant(PolicyActions.List);

        f.Tenant.Tenant.Should().Be("globex");
        f.Policy.Inputs.Should().ContainSingle().Which.Resource.Tenant.Should().Be("globex");
    }

    [Fact]
    public async Task InTenantPrefersTheBodyTenantToTheQueryTenant()
    {
        var f = new Fixture();
        f.Http.Request.QueryString = new QueryString("?tenant=globex");

        await f.RunInTenant(PolicyActions.Create, Resource.Document, new TenantBody("initech"));

        f.Tenant.Tenant.Should().Be("initech");
    }

    [Fact]
    public async Task InTenantFallsBackToTheSubjectTenantWhenTheBodyNamesNone()
    {
        var f = new Fixture();

        await f.RunInTenant(PolicyActions.Create, Resource.Document, new TenantBody(null));

        f.Tenant.Tenant.Should().Be("acme");
    }

    [Fact]
    public async Task AllowedDecisionIsRecordedInTheResourceTenantPartition()
    {
        var f = new Fixture();
        var document = DocumentSeed.Documents[0];

        await f.RunExisting(PolicyActions.Read, document.Id);

        var recorded = f.Events.Should().ContainSingle().Subject;
        recorded.Tenant.Should().Be(document.Tenant);
        recorded.EventType.Should().Be(EventTypes.PolicyDecisionMade);
        var payload = Payload(recorded);
        payload.Allow.Should().BeTrue();
        payload.SubjectId.Should().Be("u1");
        payload.Environment.Should().Be("dev");
        payload.Action.Should().Be("read");
        payload.ResourceId.Should().Be(document.Id.ToString());
    }

    [Fact]
    public async Task DeniedDecisionIsRecordedWithItsReasons()
    {
        var f = new Fixture();
        f.Policy.Decide = _ => new PolicyDecision(false, ["nope"]);

        await f.RunExisting(PolicyActions.Update, DocumentSeed.Documents[0].Id);

        var payload = Payload(f.Events.Should().ContainSingle().Subject);
        payload.Allow.Should().BeFalse();
        payload.Reasons.Should().Equal("nope");
    }

    [Fact]
    public async Task UnauditableDecisionFailsClosedAndTheEndpointNeverRuns()
    {
        var f = new Fixture { OutboxThrows = new StoreUnavailableException("cosmos down") };

        var act = () => f.RunExisting(PolicyActions.Read, DocumentSeed.Documents[0].Id);

        await act.Should().ThrowAsync<StoreUnavailableException>();
        f.Acted.Should().BeFalse();
    }

    [Fact]
    public async Task ResourceWithoutTenantIsRecordedUnderTheUnassignedPartition()
    {
        var f = new Fixture();
        f.Http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1"), new Claim("roles", "editor")], "Test"));

        await f.RunInTenant(PolicyActions.Create);

        f.Events.Should().ContainSingle().Which.Tenant.Should().Be(OutboxEvent.UnassignedTenant);
    }

    [Fact]
    public void AccessorsRefuseToBeReadOnARouteThatWasNeverAuthorized()
    {
        var document = () => new AuthorizedDocument().Value;
        var tenant = () => new TenantScope().Tenant;

        document.Should().Throw<InvalidOperationException>();
        tenant.Should().Throw<InvalidOperationException>();
    }

    private sealed record TenantBody(string? Tenant) : ITenantRequest;
}
