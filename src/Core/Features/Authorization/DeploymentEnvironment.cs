using Microsoft.Extensions.Configuration;

namespace Core.Features.Authorization;

/// <summary>The environment a request runs as, or why the request is a 400. Exactly one of the two is non-null.</summary>
public readonly record struct EnvironmentResolution(string? Environment, string? Error);

/// <summary>Resolves the <c>X-Environment</c> header for this deployment. A null or empty value means the header is missing.</summary>
public delegate EnvironmentResolution ResolveEnvironment(string? headerValue);

/// <summary>
/// Which environments one deployment of the Api accepts (<c>Deployment:AllowedEnvironments</c>, default all) and which it
/// assumes when the header is missing (<c>Deployment:DefaultEnvironment</c>, default none, so a missing header is 400).
/// Unset, it behaves exactly as before. Invalid configuration throws at creation, so the Api fails at startup.
/// </summary>
public static class DeploymentEnvironment
{
    private const string _unknown = DemoEnvironments.HeaderName + " must be one of dev, staging, prod";
    private const string _notAllowed = "environment not allowed in this deployment";

    public static ResolveEnvironment Create(IReadOnlyCollection<string>? allowed, string? defaultEnvironment)
    {
        var allowList = allowed is { Count: > 0 } given ? given : DemoEnvironments.All;
        foreach (var name in allowList)
        {
            if (!DemoEnvironments.TryParse(name, out _))
            {
                throw new InvalidOperationException($"Deployment:AllowedEnvironments contains unknown environment '{name}'.");
            }
        }

        var fallback = string.IsNullOrEmpty(defaultEnvironment) ? null : defaultEnvironment;
        if (fallback is not null && !allowList.Contains(fallback, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Deployment:DefaultEnvironment '{fallback}' is not in Deployment:AllowedEnvironments.");
        }

        var allowedSet = allowList.ToHashSet(StringComparer.Ordinal);
        return headerValue =>
        {
            var value = string.IsNullOrEmpty(headerValue) ? fallback : headerValue;
            if (!DemoEnvironments.TryParse(value, out var environment))
            {
                return new EnvironmentResolution(null, _unknown);
            }

            return allowedSet.Contains(environment)
                ? new EnvironmentResolution(environment, null)
                : new EnvironmentResolution(null, _notAllowed);
        };
    }

    public static ResolveEnvironment FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("Deployment");
        var allowed = section.GetSection("AllowedEnvironments").GetChildren().Select(child => child.Value ?? string.Empty).ToArray();
        return Create(allowed, section["DefaultEnvironment"]);
    }
}
