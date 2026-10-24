using Api.Features.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Api.Features.Home;

internal sealed record WhoAmIResponse(string Id, IReadOnlyList<string> Roles, string Tenant);

internal static class ConfigureServicesAndEndpoints
{
    public static void MapHomeEndpoints(this WebApplication app)
    {
        app.MapGet("/whoami", (HttpContext http) =>
            {
                var subject = CurrentSubject.From(http.User);
                return Results.Ok(new WhoAmIResponse(subject.Id, subject.Roles, subject.Tenant));
            })
            .RequireAuthorization();
    }
}
