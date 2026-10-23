namespace Core.Features.Authorization;

public static class DemoEnvironments
{
    public const string HeaderName = "X-Environment";

    private static readonly string[] Names = ["dev", "staging", "prod"];

    /// <summary>Every environment the demo knows, in order.</summary>
    public static IReadOnlyList<string> All => Names;

    public static bool TryParse(string? value, out string environment)
    {
        environment = string.Empty;
        if (value is null || !Names.Contains(value, StringComparer.Ordinal))
        {
            return false;
        }

        environment = value;
        return true;
    }
}
