using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using CedarPolicyTool;
using Xunit;

namespace Tests.Cedar;

public sealed class CedarPolicyToolCliTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cedar-cli-");
    private readonly StringWriter _error = new();

    private string Path(string name) => System.IO.Path.Combine(_dir.FullName, name);

    public void Dispose()
    {
        _error.Dispose();
        _dir.Delete(recursive: true);
    }

    private static readonly string[] _noArguments = [];
    private static readonly string[] _missingOutput = ["generate", "a"];
    private static readonly string[] _unknownCommand = ["build", "a", "b"];

    public static TheoryData<string[]> WrongArguments => new(_noArguments, _missingOutput, _unknownCommand);

    [Fact]
    public void GenerateWritesThePoliciesFile()
    {
        File.WriteAllText(Path("authz.cedar"),
            "@id(\"a\") permit (principal, action, resource);\n@id(\"b\") forbid (principal, action, resource);");

        var exit = Cli.Run(["generate", Path("authz.cedar"), Path("policies.json")], _error);

        exit.Should().Be(0);
        using var json = JsonDocument.Parse(File.ReadAllText(Path("policies.json")));
        json.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        json.RootElement.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void InvalidSourceReturnsOneWithLineNumbersAndWritesNothing()
    {
        File.WriteAllText(Path("authz.cedar"), "permit (principal, action, resource);");

        var exit = Cli.Run(["generate", Path("authz.cedar"), Path("policies.json")], _error);

        exit.Should().Be(1);
        _error.ToString().Should().Contain("line ");
        File.Exists(Path("policies.json")).Should().BeFalse();
    }

    [Fact]
    public void MissingInputReturnsOneAndWritesNothing()
    {
        var exit = Cli.Run(["generate", Path("missing.cedar"), Path("policies.json")], _error);

        exit.Should().Be(1);
        _error.ToString().Should().Contain(Path("missing.cedar"));
        File.Exists(Path("policies.json")).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(WrongArguments))]
    public void WrongArgumentsReturnTwoWithUsage(string[] args)
    {
        var exit = Cli.Run(args, _error);

        exit.Should().Be(2);
        _error.ToString().Should().StartWith("usage: CedarPolicyTool generate");
    }
}
