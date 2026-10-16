using Core;
using Api.Features.Documents;
using Api.Features.Home;
using Microsoft.AspNetCore.Builder;

namespace Api;

internal static class RequestPipeline
{
    // Pure JSON API, no HTML/JS/CSS served, so the policy can be maximally strict.
    private const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public static WebApplication ConfigureRequestPipelineForApi(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next(context).ConfigureAwait(false);
        });
        app.UseAuthentication(); // MUST Always be before UseAuthorization
        app.UseAuthorization();
        app.UseBuildInfoRoute("/buildinfo");
        app.MapHomeEndpoints();
        app.MapDocumentEndpoints();

        return app;
    }
}
