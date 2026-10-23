using System;

namespace CedarPolicyTool;

internal static class CedarPolicySplitter
{
    /// <summary>Splits Cedar source into one <see cref="AgentPolicy"/> per statement, collecting every error found.</summary>
    public static SplitResult Split(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CedarLexer(source).Run();
    }
}
