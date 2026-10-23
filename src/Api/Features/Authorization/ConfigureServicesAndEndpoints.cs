using System.Net.Http;
using Core.Features.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Api.Features.Authorization;

internal static class ConfigureServicesAndEndpoints
{
    public static WebApplicationBuilder ConfigureAuthorization(this WebApplicationBuilder builder)
    {
        var authority = builder.Configuration["Auth:Authority"];
        var audience = builder.Configuration["Auth:Audience"];

        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = authority;
                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = builder.Configuration.GetValue<bool?>("Auth:RequireHttpsMetadata") ?? !builder.Environment.IsDevelopment();
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = authority,
                    ValidAudience = audience,
                    NameClaimType = CurrentSubject.SubjectClaim,
                };
            });

        var policyEngine = builder.Configuration["POLICY_ENGINE"]
            ?? builder.Configuration["PolicyEngine:Provider"]
            ?? builder.Configuration["Policy:Engine"]
            ?? "opa";

        if (string.Equals(policyEngine, "cedar", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddHttpClient(nameof(CedarPolicy), http =>
            {
                http.BaseAddress = new Uri(builder.Configuration["Cedar:BaseAddress"] ?? "http://localhost:8180");
                http.Timeout = TimeSpan.FromSeconds(2);
            });
            builder.Services.AddTransient(sp => CedarPolicy.Decide(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CedarPolicy))));
        }
        else
        {
            builder.Services.AddHttpClient(nameof(OpaPolicy), http =>
            {
                http.BaseAddress = new Uri(builder.Configuration["Opa:BaseAddress"] ?? "http://localhost:8181");
                http.Timeout = TimeSpan.FromSeconds(2);
            });
            builder.Services.AddTransient(sp => OpaPolicy.Decide(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpaPolicy))));
        }

        // Built here, not lazily, so invalid Deployment:* configuration fails at startup rather than on the first request.
        builder.Services.AddSingleton(DeploymentEnvironment.FromConfiguration(builder.Configuration));
        builder.Services.AddTransient<DocumentAccess>();
        return builder;
    }
}
