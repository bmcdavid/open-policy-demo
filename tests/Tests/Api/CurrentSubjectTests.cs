using System.Security.Claims;
using Api.Features.Authorization;
using AwesomeAssertions;
using Xunit;

namespace Tests.Api;

public sealed class CurrentSubjectTests
{
    [Fact]
    public void RolesReadFromRolesClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "u1"), new Claim("roles", "reader"), new Claim("roles", "editor"), new Claim("tenant", "acme")], "Test"));

        var subject = CurrentSubject.From(user);

        subject.Id.Should().Be("u1");
        subject.Tenant.Should().Be("acme");
        subject.Roles.Should().BeEquivalentTo(["reader", "editor"]);
    }

    [Fact]
    public void MissingTenantAndRolesYieldEmptyValues()
    {
        var subject = CurrentSubject.From(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], "Test")));

        subject.Tenant.Should().BeEmpty();
        subject.Roles.Should().BeEmpty();
    }

    [Fact]
    public void AnonymousUserYieldsEmptySubject()
    {
        var subject = CurrentSubject.From(new ClaimsPrincipal(new ClaimsIdentity()));

        subject.Id.Should().BeEmpty();
        subject.Roles.Should().BeEmpty();
    }
}
