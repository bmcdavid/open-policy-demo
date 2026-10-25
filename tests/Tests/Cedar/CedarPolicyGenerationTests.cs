using System.IO;
using AwesomeAssertions;
using CedarPolicyTool;
using Xunit;

namespace Tests.Cedar;

public sealed class CedarPolicyGenerationTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cedar-generation-");

    private string Path(string name) => System.IO.Path.Combine(_dir.FullName, name);

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void WritesTheAgentJsonForValidSource()
    {
        File.WriteAllText(Path("authz.cedar"), "@id(\"a\") permit (principal, action, resource);");

        CedarPolicyGeneration.Generate(Path("authz.cedar"), Path("out.json"));

        File.ReadAllText(Path("out.json")).Should().Be(
            AgentPolicyJson.Serialize([new AgentPolicy("a", "permit (principal, action, resource);")]));
    }

    [Fact]
    public void CreatesMissingOutputDirectories()
    {
        File.WriteAllText(Path("authz.cedar"), "@id(\"a\") permit (principal, action, resource);");

        CedarPolicyGeneration.Generate(Path("authz.cedar"), System.IO.Path.Combine(Path("nested"), "out.json"));

        File.Exists(System.IO.Path.Combine(Path("nested"), "out.json")).Should().BeTrue();
    }

    [Fact]
    public void InvalidSourceThrowsListingEveryErrorAndWritesNothing()
    {
        File.WriteAllText(Path("authz.cedar"), "permit (principal, action, resource);\n@id(\"\") forbid (principal, action, resource);");

        var act = () => CedarPolicyGeneration.Generate(Path("authz.cedar"), Path("out.json"));

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("line 1").And.Contain("line 2");
        File.Exists(Path("out.json")).Should().BeFalse();
        Directory.GetFiles(_dir.FullName).Should().NotContain(f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void LeavesAnExistingOutputFileUntouchedWhenGenerationFails()
    {
        File.WriteAllText(Path("out.json"), "previous");
        File.WriteAllText(Path("authz.cedar"), "permit (principal, action, resource);");

        var act = () => CedarPolicyGeneration.Generate(Path("authz.cedar"), Path("out.json"));

        act.Should().Throw<InvalidOperationException>();
        File.ReadAllText(Path("out.json")).Should().Be("previous");
    }
}
