using System.Collections.Generic;

namespace CedarPolicyTool;

/// <summary>One entry of the JSON array that permitio/cedar-agent loads: an id and the raw Cedar statement.</summary>
internal sealed record AgentPolicy(string Id, string Content);

internal sealed record CedarSourceError(int Line, int Column, string Message);

internal sealed record SplitResult(IReadOnlyList<AgentPolicy> Policies, IReadOnlyList<CedarSourceError> Errors);
