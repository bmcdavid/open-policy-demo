using AwesomeAssertions;
using Core.Features.Audit;
using Core.Features.Documents;
using Core.Features.Events;
using Worker.Features.Audit;
using Worker.Features.CacheInvalidation;
using Xunit;

namespace Tests.WorkerFeatures;

public sealed class WorkerHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OutboxEvent Changed(Guid id, string tenant = "acme") =>
        OutboxEvent.Create(tenant, EventTypes.DocumentChanged, new DocumentChanged(id, tenant, DocumentChangeKind.Updated, "t"));

    private static OutboxEvent Decision() =>
        OutboxEvent.Create("acme", EventTypes.PolicyDecisionMade, new { allow = true });

    [Fact]
    public async Task AuditHandlerUpsertsAnEntryKeyedByTheEventId()
    {
        AuditEntry? written = null;
        var handler = AuditHandler.Create((entry, _) =>
        {
            written = entry;
            return Task.CompletedTask;
        });
        var outboxEvent = Decision();

        await handler(outboxEvent, Ct);

        written!.Id.Should().Be(outboxEvent.Id);
        written.Tenant.Should().Be("acme");
        written.EventType.Should().Be(EventTypes.PolicyDecisionMade);
        written.OccurredAt.Should().Be(outboxEvent.OccurredAt);
    }

    [Fact]
    public async Task CacheInvalidationPassesTheDocumentChangeToTheCache()
    {
        DocumentChanged? seen = null;
        var handler = CacheInvalidationHandler.Create((change, _) =>
        {
            seen = change;
            return Task.CompletedTask;
        });
        var id = Guid.NewGuid();

        await handler(Changed(id), Ct);

        seen.Should().NotBeNull();
        seen!.DocumentId.Should().Be(id);
        seen.Tenant.Should().Be("acme");
    }

    [Fact]
    public async Task CacheInvalidationIgnoresEventsThatAreNotDocumentChanges()
    {
        var called = false;
        var handler = CacheInvalidationHandler.Create((_, _) =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await handler(Decision(), Ct);

        called.Should().BeFalse();
    }

    [Fact]
    public async Task CacheFailureSurfacesSoTheMessageIsRetried()
    {
        var handler = CacheInvalidationHandler.Create((_, _) => throw new CacheUnavailableException("valkey down"));

        var act = () => handler(Changed(Guid.NewGuid()), Ct);

        await act.Should().ThrowAsync<CacheUnavailableException>();
    }
}
