using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Core;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class BuildInfoAttribute(string key, string value) : Attribute
{
    public string Key { get; } = key ?? throw new ArgumentNullException(nameof(key));

    public string Value { get; } = value ?? throw new ArgumentNullException(nameof(value));

    internal static readonly IReadOnlyDictionary<string, object> BuildInfoAttributes = typeof(BuildInfoAttribute)
        .Assembly.GetCustomAttributes(inherit: false)
        .OfType<BuildInfoAttribute>()
        .ToArray()
        .ToDictionary(o => o.Key, o => (object)o.Value, StringComparer.OrdinalIgnoreCase);
}

internal static class BuildInfoExtensions
{
    public static WebApplication UseBuildInfoRoute(this WebApplication app, string route)
    {
        app.MapGet(route, (IHostEnvironment hostEnvironment) => hostEnvironment.GetBuildInfo()).ShortCircuit();

        return app;
    }

    private static Dictionary<string,object> GetBuildInfo(this IHostEnvironment hostEnvironment)
    {
        var buildData = BuildInfoAttribute.BuildInfoAttributes;
        var info = new Dictionary<string, object>(buildData)
        {
            [nameof(IHostEnvironment.EnvironmentName)] = hostEnvironment.EnvironmentName // runtime value set by "ASPNETCORE_ENVIRONMENT"
        };

        return info;
    }
}
