using System.Security.Claims;
using System.Text.Json;
using Api.Features.Authorization;
using AwesomeAssertions;
using Core.Features.Authorization;
using Core.Features.Documents;
using Core.Features.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Tests.Support;
using Xunit;

namespace Tests.Api;

public sealed class DocumentAccessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Fixture
    {
        public FakePolicyClient Policy { get; } = new();

        public InMemoryDocumentStore Store { get; } = new();

        public List<OutboxEvent> Events { get; } = [];

        public Exception? OutboxThrows { get; set; }

        public DocumentAccess Access => new(Store, Policy.DecideAsync, (e, _) =>
        {
            if (OutboxThrows is not null)
            {
                return Task.FromException(OutboxThrows);
            }

            Events.Add(e);
            return Task.CompletedTask;
        });
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

    private static Task<IResult> Allowed(Document document) => Task.FromResult(Results.Ok(document));

    private static Task<IResult> AllowedTenant(string tenant) => Task.FromResult(Results.Ok(tenant));

    private static PolicyDecisionMade Payload(OutboxEvent outboxEvent) =>
        outboxEvent.Payload.Deserialize<PolicyDecisionMade>(EventJson.Options)!;

    [Fact]
    public async Task AllowedExistingRunsTheActionAndSendsSubjectEnvironmentActionAndResourceToPolicy()
    {
        var f = new Fixture();
        var document = DocumentSeed.Documents[0];
        var acted = false;

        var result = await f.Access.Existing(Context(), document.Id, PolicyActions.Read, d =>
        {
            acted = true;
            return Allowed(d);
        }, Ct);

        acted.Should().BeTrue();
        result.Should().BeOfType<Ok<Document>>().Which.Value.Should().Be(document);
        var input = f.Policy.Inputs.Should().ContainSingle().Subject;
        input.Environment.Should().Be("dev");
        input.Action.Should().Be("read");
        input.Resource.Should().Be(new PolicyResource("document", document.Tenant, document.Id.ToString()));
        input.Subject.Id.Should().Be("u1");
        input.Subject.Tenant.Should().Be("acme");
        input.Subject.Roles.Should().Equal("reader");
    }

    [Fact]
    public async Task MissingDocumentIs404WithoutCallingPolicyOrAction()
    {
        var f = new Fixture();
        var acted = false;

        var result = await f.Access.Existing(Context(), Guid.NewGuid(), PolicyActions.Read, d =>
        {
            acted = true;
            return Allowed(d);
        }, Ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
        f.Policy.Inputs.Should().BeEmpty();
        f.Events.Should().BeEmpty();
        acted.Should().BeFalse();
    }

    [Fact]
    public async Task MissingEnvironmentIs400BeforeTheDocumentIsLoadedOrPolicyIsAsked()
    {
        var f = new Fixture();
        var context = Context();
        context.Request.Headers.Remove(DemoEnvironments.HeaderName);

        var existing = await f.Access.Existing(context, Guid.NewGuid(), PolicyActions.Read, Allowed, Ct);
        var inTenant = await f.Access.InTenant(context, "document", PolicyActions.List, null, AllowedTenant, Ct);

        existing.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(400);
        inTenant.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(400);
        f.Policy.Inputs.Should().BeEmpty();
        f.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task DeniedIs403WithReasonsAndTheActionNeverRuns()
    {
        var f = new Fixture();
        f.Policy.Decide = _ => new PolicyDecision(false, ["nope"]);
        var acted = false;

        var result = await f.Access.Existing(Context(), DocumentSeed.Documents[0].Id, PolicyActions.Update, d =>
        {
            acted = true;
            return Allowed(d);
        }, Ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(403);
        ((IValueHttpResult)result).Value.Should().BeOfType<DenyBody>().Which.Reasons.Should().Equal("nope");
        acted.Should().BeFalse();
    }

    [Fact]
    public async Task EngineUnavailableIs503AndRecordsNoEvent()
    {
        var f = new Fixture();
        f.Policy.Throws = new PolicyEngineUnavailableException("down");

        var result = await f.Access.Existing(Context(), DocumentSeed.Documents[0].Id, PolicyActions.Read, Allowed, Ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(503);
        f.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task InTenantDefaultsToTheSubjectTenantAndAsksPolicyAboutTheGivenResourceType()
    {
        var f = new Fixture();
        string? seenTenant = null;

        await f.Access.InTenant(Context(), "audit", PolicyActions.Audit, null, t =>
        {
            seenTenant = t;
            return AllowedTenant(t);
        }, Ct);

        seenTenant.Should().Be("acme");
        f.Policy.Inputs.Should().ContainSingle().Which.Resource.Should().Be(new PolicyResource("audit", "acme", null));
    }

    [Fact]
    public async Task InTenantUsesTheRequestedTenantInsteadOfTheSubjects()
    {
        var f = new Fixture();

        await f.Access.InTenant(Context(), "document", PolicyActions.List, "globex", AllowedTenant, Ct);

        f.Policy.Inputs.Should().ContainSingle().Which.Resource.Tenant.Should().Be("globex");
    }

    [Fact]
    public async Task AllowedDecisionIsRecordedInTheResourceTenantPartition()
    {
        var f = new Fixture();
        var document = DocumentSeed.Documents[0];

        await f.Access.Existing(Context(), document.Id, PolicyActions.Read, Allowed, Ct);

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

        await f.Access.Existing(Context(), DocumentSeed.Documents[0].Id, PolicyActions.Update, Allowed, Ct);

        var payload = Payload(f.Events.Should().ContainSingle().Subject);
        payload.Allow.Should().BeFalse();
        payload.Reasons.Should().Equal("nope");
    }

    [Fact]
    public async Task UnauditableDecisionFailsClosedAndTheActionNeverRuns()
    {
        var f = new Fixture { OutboxThrows = new StoreUnavailableException("cosmos down") };
        var acted = false;

        var act = () => f.Access.Existing(Context(), DocumentSeed.Documents[0].Id, PolicyActions.Read, d =>
        {
            acted = true;
            return Allowed(d);
        }, Ct);

        await act.Should().ThrowAsync<StoreUnavailableException>();
        acted.Should().BeFalse();
    }

    [Fact]
    public async Task ResourceWithoutTenantIsRecordedUnderTheUnassignedPartition()
    {
        var f = new Fixture();
        var context = Context();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1"), new Claim("roles", "editor")], "Test"));

        await f.Access.InTenant(context, "document", PolicyActions.Create, null, AllowedTenant, Ct);

        f.Events.Should().ContainSingle().Which.Tenant.Should().Be(OutboxEvent.UnassignedTenant);
    }
}
