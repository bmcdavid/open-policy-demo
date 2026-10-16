using Api.Features.Authorization;
using Core.Features.Authorization;

namespace Api.Features.Documents;

internal sealed class EnvironmentHeaderFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        if (!DemoEnvironments.TryParse(http.Request.Headers[DemoEnvironments.HeaderName].ToString(), out var environment))
        {
            return ValueTask.FromResult<object?>(
                Results.BadRequest(new { error = DemoEnvironments.HeaderName + " must be one of dev, staging, prod" }));
        }

        http.Items[PolicyEnforcer.EnvironmentItemKey] = environment;
        return next(context);
    }
}
