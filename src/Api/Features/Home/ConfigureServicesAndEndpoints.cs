using Api.Features.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Features.Home;

internal sealed record WhoAmIResponse(string Id, IReadOnlyList<string> Roles, string Tenant);

internal sealed class WhoAmIHttpRequestHandler : IEndpointHandler<Nil, IResult>
{
    public Task<IResult> HandleAsync(Nil input, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var subject = CurrentSubject.From(httpContext.User);
        return Task.FromResult(Results.Ok(new WhoAmIResponse(subject.Id, subject.Roles, subject.Tenant)));
    }
}

internal static class ConfigureServicesAndEndpoints
{
    public static WebApplicationBuilder ConfigureHome(this WebApplicationBuilder builder)
    {
        builder.Services.AddTransient<WhoAmIHttpRequestHandler>();
        return builder;
    }

    public static void MapHomeEndpoints(this WebApplication app)
    {
        app.MapGet("/whoami", (HttpContext h, WhoAmIHttpRequestHandler handler) =>
                handler.HandleAsync(Nil.Empty, h, h.RequestAborted))
            .RequireAuthorization();
    }
}
