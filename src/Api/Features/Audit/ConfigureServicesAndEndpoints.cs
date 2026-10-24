using Api.Features.Authorization;
using Core;
using Core.Features.Audit;
using Core.Features.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Api.Features.Audit;

internal static class ConfigureServicesAndEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public static void MapAuditEndpoints(this WebApplication app)
    {
        var audit = app.MapGroup(ApiRoutes.Audit)
            .RequireAuthorization();

        audit.MapGet("/", Query).AuthorizeInTenant(PolicyActions.Audit, Resource.Audit);
    }

    private static async Task<IResult> Query(
        TenantScope scope, DateTimeOffset? from, DateTimeOffset? to, string? continuation, int? pageSize,
        QueryAudit queryAudit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope.Tenant))
        {
            return Results.BadRequest(new { error = "tenant is required" });
        }

        var page = await queryAudit(new AuditQuery(scope.Tenant, from, to, continuation, ClampPageSize(pageSize)), cancellationToken);
        return Results.Ok(page);
    }

    private static int ClampPageSize(int? requested) =>
        requested is null or <= 0 ? DefaultPageSize : Math.Min(requested.Value, MaxPageSize);
}
