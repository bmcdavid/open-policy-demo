using AwesomeAssertions;
using CedarPolicyTool;
using Xunit;

namespace Tests.Cedar;

public sealed class CedarPolicySplitterTests
{
    [Fact]
    public void SplitsOnePolicyAndNormalizesWhitespace()
    {
        const string source = """
            // 1. Read
            @id("role_read")
            permit (
                principal,
                action in [Action::"list", Action::"read"],
                resource
            )
            when {
                context.roles.containsAny(["reader", "admin"])
            };
            """;

        var result = CedarPolicySplitter.Split(source);

        result.Errors.Should().BeEmpty();
        result.Policies.Should().Equal(new AgentPolicy(
            "role_read",
            """permit (principal, action in [Action::"list", Action::"read"], resource) when { context.roles.containsAny(["reader", "admin"]) };"""));
    }

    [Fact]
    public void SplitsSeveralPoliciesInSourceOrder()
    {
        const string source = """
            @id("a") permit (principal, action, resource);
            @id("b") forbid (principal, action, resource);
            """;

        var result = CedarPolicySplitter.Split(source);

        result.Errors.Should().BeEmpty();
        result.Policies.Select(p => p.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void KeepsStringContentsVerbatim()
    {
        const string source = """
            @id("s")
            permit (principal, action, resource) when { context.note == "a;b // c { } ) \" \\  d" };
            """;

        var result = CedarPolicySplitter.Split(source);

        result.Errors.Should().BeEmpty();
        result.Policies.Should().ContainSingle().Which.Content.Should().Be(
            """permit (principal, action, resource) when { context.note == "a;b // c { } ) \" \\  d" };""");
    }

    [Fact]
    public void DropsCommentsIncludingTrailingAndBetweenAnnotationAndEffect()
    {
        const string source = """
            @id("c") // trailing; with semicolon
            // between annotation and effect
            permit (principal, action, resource); // after the statement
            """;

        var result = CedarPolicySplitter.Split(source);

        result.Errors.Should().BeEmpty();
        result.Policies.Should().Equal(new AgentPolicy("c", "permit (principal, action, resource);"));
    }

    [Fact]
    public void CrlfAndLfProduceTheSameOutputAndBomIsIgnored()
    {
        const string lf = "@id(\"a\")\npermit (\n  principal,\n  action,\n  resource\n);\n";
        var crlf = "﻿" + lf.Replace("\n", "\r\n", StringComparison.Ordinal);

        CedarPolicySplitter.Split(crlf).Should().BeEquivalentTo(CedarPolicySplitter.Split(lf));
        CedarPolicySplitter.Split(crlf).Policies.Should().ContainSingle();
    }

    [Fact]
    public void SupportsUnicodeIdsAndContent()
    {
        const string source = "@id(\"zugriff verweigert ü\")\nforbid (principal, action, resource) when { context.name == \"é中\" };";

        var result = CedarPolicySplitter.Split(source);

        result.Errors.Should().BeEmpty();
        result.Policies.Should().Equal(new AgentPolicy(
            "zugriff verweigert ü",
            "forbid (principal, action, resource) when { context.name == \"é中\" };"));
    }

    [Fact]
    public void UnescapesTheIdButKeepsOtherAnnotationsAsWritten()
    {
        const string source = """
            @id("say \"hi\"") @reason("a \\ b") permit (principal, action, resource);
            """;

        var result = CedarPolicySplitter.Split(source);

        result.Errors.Should().BeEmpty();
        result.Policies.Should().Equal(new AgentPolicy(
            "say \"hi\"",
            """@reason("a \\ b") permit (principal, action, resource);"""));
    }

    [Theory]
    [InlineData("permit (principal, action, resource);", 1, "missing an @id")]
    [InlineData("@id(\"\") permit (principal, action, resource);", 1, "id must not be empty")]
    [InlineData("@id(\" a\") permit (principal, action, resource);", 1, "leading or trailing whitespace")]
    [InlineData("\n@id(\"a\")\nallow (principal, action, resource);", 2, "permit or forbid")]
    [InlineData("@id(\"a\") @id(\"b\") permit (principal, action, resource);", 1, "duplicate annotation @id")]
    [InlineData("@id permit (principal, action, resource);", 1, "expected '('")]
    [InlineData("@id(7) permit (principal, action, resource);", 1, "string")]
    [InlineData("@id(\"a\") permit (principal, action, resource) when { context.x == \"abc };", 1, "unterminated string")]
    [InlineData("@id(\"a\")\npermit (principal, action, resource] ;", 2, "does not match")]
    [InlineData("@id(\"a\")\npermit (principal, action, resource));", 2, "unexpected ')'")]
    [InlineData("\n\n@id(\"a\") permit (principal, action, resource)", 3, "missing the terminating ';'")]
    [InlineData("@id(\"a\") permit (principal, action, resource);\n@id(\"b\")\n", 2, "annotation without a policy")]
    [InlineData("// nothing here;\n", 1, "no policies")]
    [InlineData("", 1, "no policies")]
    public void ReportsErrorWithLineNumber(string source, int line, string fragment)
    {
        var result = CedarPolicySplitter.Split(source);

        result.Errors.Should().Contain(e => e.Line == line && e.Message.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsDuplicateIdWithBothLines()
    {
        const string source = """
            @id("same") permit (principal, action, resource);
            @id("same") forbid (principal, action, resource);
            """;

        var result = CedarPolicySplitter.Split(source);

        var error = result.Errors.Should().ContainSingle().Which;
        error.Line.Should().Be(2);
        error.Message.Should().Contain("line 1");
    }

    [Fact]
    public void ReportsEverythingWrongInOnePass()
    {
        const string source = """
            permit (principal, action, resource);
            @id("ok") permit (principal, action, resource);
            @id("") forbid (principal, action, resource);
            """;

        var result = CedarPolicySplitter.Split(source);

        result.Errors.Select(e => e.Line).Should().Equal(1, 3);
        result.Policies.Should().ContainSingle().Which.Id.Should().Be("ok");
    }
}
