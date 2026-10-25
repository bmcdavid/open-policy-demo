using Api.Features.Authorization;
using AwesomeAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Support;
using Xunit;

namespace Tests.Api;

/// <summary>
/// Guards the route wiring that <see cref="AccessFilter"/> cannot check for itself: a protected route that forgot its
/// declaration would be reachable by any authenticated caller, and an accessor read on the wrong kind of route throws.
/// </summary>
public sealed class AccessWiringTests
{
    private static readonly string[] _protectedPrefixes = ["/documents", "/audit"];

    private static List<RouteEndpoint> Endpoints()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        return [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];
    }

    private static IEnumerable<Type> ParameterTypes(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<System.Reflection.MethodInfo>()?.GetParameters().Select(p => p.ParameterType) ?? [];

    [Fact]
    public void EveryRouteUnderAProtectedPrefixDeclaresItsAccessRequirement()
    {
        var undeclared = Endpoints()
            .Where(e => _protectedPrefixes.Any(p => e.RoutePattern.RawText!.StartsWith(p, StringComparison.Ordinal)))
            .Where(e => e.Metadata.GetMetadata<AccessRequirement>() is null)
            .Select(e => $"{string.Join(',', e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {e.RoutePattern.RawText}");

        undeclared.Should().BeEmpty("a route without AuthorizeExisting or AuthorizeInTenant skips the policy check");
    }

    [Fact]
    public void AccessorsAreOnlyTakenByRoutesDeclaredForThatScope()
    {
        var mismatched = new List<string>();
        foreach (var endpoint in Endpoints())
        {
            var scope = endpoint.Metadata.GetMetadata<AccessRequirement>()?.Scope;
            var types = ParameterTypes(endpoint).ToList();
            if (types.Contains(typeof(AuthorizedDocument)) && scope != AccessScope.Document)
            {
                mismatched.Add($"{endpoint.RoutePattern.RawText} takes AuthorizedDocument but is declared {scope?.ToString() ?? "undeclared"}");
            }

            if (types.Contains(typeof(TenantScope)) && scope != AccessScope.Tenant)
            {
                mismatched.Add($"{endpoint.RoutePattern.RawText} takes TenantScope but is declared {scope?.ToString() ?? "undeclared"}");
            }
        }

        mismatched.Should().BeEmpty();
    }

    [Fact]
    public void ExistingRoutesCarryTheDocumentIdRouteValue()
    {
        var missing = Endpoints()
            .Where(e => e.Metadata.GetMetadata<AccessRequirement>()?.Scope == AccessScope.Document)
            .Where(e => e.RoutePattern.GetParameter(AccessFilter.DocumentIdRouteValue) is null)
            .Select(e => e.RoutePattern.RawText);

        missing.Should().BeEmpty();
    }
}
