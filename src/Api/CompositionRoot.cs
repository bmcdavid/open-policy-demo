using Api.Features.Audit;
using Api.Features.Authorization;
using Api.Features.Documents;
using Api.Features.Home;
using Core.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Api;

// https://blog.ploeh.dk/2011/07/28/CompositionRoot/
internal static class CompositionRoot
{
    public static void Compose(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();
        builder.Services.AddExceptionHandler<StoreUnavailableExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.ConfigureAuthorization();
        builder.ConfigureDocuments();
    }
}
