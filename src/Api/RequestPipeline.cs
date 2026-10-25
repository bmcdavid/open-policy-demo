using Api.Features.Audit;
using Api.Features.Authorization;
using Api.Features.Documents;
using Api.Features.Home;
using Core;
using Core.Features.Authorization;
using Microsoft.AspNetCore.Builder;
using Serilog;

namespace Api;

internal static class RequestPipeline
{
    // Pure JSON API, no HTML/JS/CSS served, so the policy can be maximally strict.
    private const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public static WebApplication ConfigureRequestPipelineForApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostics, http) =>
        {
            // Only identifiers: never the Authorization header, token, or document contents.
            diagnostics.Set("Tenant", CurrentSubject.From(http.User).Tenant);
            var environment = DemoEnvironments.TryParse(http.Request.Headers[DemoEnvironments.HeaderName].ToString(), out var parsed) ? parsed : string.Empty;
            diagnostics.Set("Environment", environment);
        });
        app.Use(async (context, next) =>
        {
            context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next(context);
        });
        app.UseAuthentication(); // MUST Always be before UseAuthorization
        app.UseAuthorization();
        app.UseBuildInfoRoute("/buildinfo");
        app.MapHomeEndpoints();
        app.MapDocumentEndpoints();
        app.MapAuditEndpoints();

        return app;
    }
}
