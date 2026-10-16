using Api.Features.Authorization;
using Api.Features.Documents;
using Api.Features.Home;
using Microsoft.AspNetCore.Builder;

namespace Api;

// https://blog.ploeh.dk/2011/07/28/CompositionRoot/
internal static class CompositionRoot
{
    public static void Compose(this WebApplicationBuilder builder)
    {
        builder.ConfigureAuthorization();
        builder.ConfigureHome();
        builder.ConfigureDocuments();
    }
}
