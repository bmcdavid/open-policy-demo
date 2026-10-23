using CedarPolicyTool;
using AwesomeAssertions;
using Xunit;

namespace Tests.Cedar;

public sealed class AgentPolicyJsonTests
{
    [Fact]
    public void WritesTwoSpaceIndentLfAndTrailingNewline()
    {
        var json = AgentPolicyJson.Serialize([new AgentPolicy("a", "permit (principal, action, resource);")]);

        json.Should().Be("[\n  {\n    \"id\": \"a\",\n    \"content\": \"permit (principal, action, resource);\"\n  }\n]\n");
    }

    [Fact]
    public void EscapesQuotesButNotOperatorsOrNonAscii()
    {
        var json = AgentPolicyJson.Serialize([new AgentPolicy("ü", "a && !b == \"x\"")]);

        json.Should().Contain("\"id\": \"ü\"").And.Contain("\"content\": \"a && !b == \\\"x\\\"\"");
    }

    [Fact]
    public void WritesAnEmptyArrayForNoPolicies()
    {
        AgentPolicyJson.Serialize([]).Should().Be("[]\n");
    }
}
