using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CedarPolicyTool;

internal static class AgentPolicyJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(IReadOnlyList<AgentPolicy> policies) =>
        JsonSerializer.Serialize(policies, Options) + "\n";
}
