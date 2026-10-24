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

    public static WebApplicationBuilder ConfigureAudit(this WebApplicationBuilder builder) => builder;

    public static void MapAuditEndpoints(this WebApplication app)
    {
        var audit = app.MapGroup(ApiRoutes.Audit)
            .RequireAuthorization();

        audit.MapGet("/", Query);
    }

    private static Task<IResult> Query(
        HttpContext http, string? tenant, DateTimeOffset? from, DateTimeOffset? to, string? continuation, int? pageSize,
        DocumentAccess access, QueryAudit queryAudit) =>
        access.InTenant(http, "audit", PolicyActions.Audit, tenant, async effectiveTenant =>
        {
            if (string.IsNullOrWhiteSpace(effectiveTenant))
            {
                return Results.BadRequest(new { error = "tenant is required" });
            }

            var page = await queryAudit(
                new AuditQuery(effectiveTenant, from, to, continuation, ClampPageSize(pageSize)),
                http.RequestAborted);
            return Results.Ok(page);
        }, http.RequestAborted);

    private static int ClampPageSize(int? requested) =>
        requested is null or <= 0 ? DefaultPageSize : Math.Min(requested.Value, MaxPageSize);
}
