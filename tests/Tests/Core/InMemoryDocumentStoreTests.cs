using AwesomeAssertions;
using Core.Features.Authorization;
using Core.Features.Documents;
using Xunit;

namespace Tests.CoreFeatures;

public sealed class InMemoryDocumentStoreTests
{
    [Fact]
    public void List_returns_only_requested_tenant()
    {
        var store = new InMemoryDocumentStore();

        store.List("acme").Should().NotBeEmpty().And.OnlyContain(d => d.Tenant == "acme");
        store.List("globex").Should().NotBeEmpty().And.OnlyContain(d => d.Tenant == "globex");
        store.List("nobody").Should().BeEmpty();
    }

    [Fact]
    public void Update_missing_returns_null() =>
        new InMemoryDocumentStore().Update(Guid.NewGuid(), "x").Should().BeNull();

    [Fact]
    public void Remove_missing_returns_false() =>
        new InMemoryDocumentStore().Remove(Guid.NewGuid()).Should().BeFalse();

    [Fact]
    public void Add_then_get_round_trips()
    {
        var store = new InMemoryDocumentStore();
        var added = store.Add("acme", "hello");

        store.Find(added.Id).Should().Be(added);
    }
}

public sealed class DemoEnvironmentsTests
{
    [Theory]
    [InlineData("dev", true)]
    [InlineData("staging", true)]
    [InlineData("prod", true)]
    [InlineData("PROD", false)]
    [InlineData(" prod", false)]
    [InlineData("production", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_accepts_only_exact_lowercase_names(string? value, bool expected) =>
        DemoEnvironments.TryParse(value, out _).Should().Be(expected);
}
