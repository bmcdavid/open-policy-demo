using AwesomeAssertions;
using Core.Features.Authorization;
using Core.Features.Documents;
using Xunit;

namespace Tests.CoreFeatures;

public sealed class InMemoryDocumentStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListReturnsOnlyRequestedTenant()
    {
        var store = new InMemoryDocumentStore();

        (await store.ListAsync("acme", Ct)).Should().NotBeEmpty().And.OnlyContain(d => d.Tenant == "acme");
        (await store.ListAsync("globex", Ct)).Should().NotBeEmpty().And.OnlyContain(d => d.Tenant == "globex");
        (await store.ListAsync("nobody", Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateMissingReturnsNull() =>
        (await new InMemoryDocumentStore().UpdateAsync(Guid.NewGuid(), "x", Ct)).Should().BeNull();

    [Fact]
    public async Task RemoveMissingReturnsNull() =>
        (await new InMemoryDocumentStore().RemoveAsync(Guid.NewGuid(), Ct)).Should().BeNull();

    [Fact]
    public async Task AddThenGetRoundTrips()
    {
        var store = new InMemoryDocumentStore();
        var added = await store.AddAsync("acme", "hello", Ct);

        (await store.FindAsync(added.Id, Ct)).Should().Be(added);
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
    public void TryParseAcceptsOnlyExactLowercaseNames(string? value, bool expected) =>
        DemoEnvironments.TryParse(value, out _).Should().Be(expected);
}
