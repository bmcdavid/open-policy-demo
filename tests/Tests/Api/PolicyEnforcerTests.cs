using System.Security.Claims;
using Api.Features.Authorization;
using AwesomeAssertions;
using Core.Features.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Tests.Support;
using Xunit;

namespace Tests.Api;

public sealed class PolicyEnforcerTests
{
    private static readonly PolicyResource Resource = new("document", "acme", "d1");

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "u1"), new Claim("roles", "reader"), new Claim("tenant", "acme")], "Test")),
        };
        context.Items[PolicyEnforcer.EnvironmentItemKey] = "dev";
        return context;
    }

    [Fact]
    public async Task EnforceAsync_returns_null_when_allowed_and_sends_subject_environment_action_resource()
    {
        var policy = new FakePolicyClient();
        var result = await new PolicyEnforcer(policy).EnforceAsync(Context(), PolicyActions.Read, Resource, CancellationToken.None);

        result.Should().BeNull();
        var input = policy.Inputs.Should().ContainSingle().Subject;
        input.Environment.Should().Be("dev");
        input.Action.Should().Be("read");
        input.Resource.Should().Be(Resource);
        input.Subject.Id.Should().Be("u1");
        input.Subject.Tenant.Should().Be("acme");
        input.Subject.Roles.Should().Equal("reader");
    }

    [Fact]
    public async Task EnforceAsync_returns_403_with_reasons_when_denied()
    {
        var policy = new FakePolicyClient { Decide = _ => new PolicyDecision(false, ["nope"]) };
        var result = await new PolicyEnforcer(policy).EnforceAsync(Context(), PolicyActions.Update, Resource, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(403);
        ((IValueHttpResult)result!).Value.Should().BeOfType<DenyBody>().Which.Reasons.Should().Equal("nope");
    }

    [Fact]
    public async Task EnforceAsync_returns_503_when_engine_unavailable()
    {
        var policy = new FakePolicyClient { Throws = new PolicyEngineUnavailableException("down") };
        var result = await new PolicyEnforcer(policy).EnforceAsync(Context(), PolicyActions.Read, Resource, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(503);
    }
}

public sealed class CurrentSubjectTests
{
    [Fact]
    public void Roles_read_from_roles_claim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "u1"), new Claim("roles", "reader"), new Claim("roles", "editor"), new Claim("tenant", "acme")], "Test"));

        var subject = CurrentSubject.From(user);

        subject.Id.Should().Be("u1");
        subject.Tenant.Should().Be("acme");
        subject.Roles.Should().BeEquivalentTo(["reader", "editor"]);
    }

    [Fact]
    public void Missing_tenant_and_roles_yield_empty_values()
    {
        var subject = CurrentSubject.From(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], "Test")));

        subject.Tenant.Should().BeEmpty();
        subject.Roles.Should().BeEmpty();
    }

    [Fact]
    public void Anonymous_user_yields_empty_subject()
    {
        var subject = CurrentSubject.From(new ClaimsPrincipal(new ClaimsIdentity()));

        subject.Id.Should().BeEmpty();
        subject.Roles.Should().BeEmpty();
    }
}
