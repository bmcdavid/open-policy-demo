using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using Core.Features.Events;
using Core.Features.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Support;
using Xunit;

namespace Tests.CoreFeatures;

public sealed class EventDeliveryTests
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Running : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _run;

        public Running(EventDelivery delivery, IReadOnlyList<Subscription> subscriptions, CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _run = delivery.RunAsync(subscriptions, _cts.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try
            {
                await _run;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }

    private static (EventDelivery Delivery, InMemoryTransport Bus) Create(int maxAttempts = 5)
    {
        var bus = new InMemoryTransport();
        var delivery = new EventDelivery(bus, NullLogger<EventDelivery>.Instance, new EventDeliveryOptions(maxAttempts, _ => TimeSpan.Zero));
        return (delivery, bus);
    }

    private static Running Start(EventDelivery delivery, params Subscription[] subscriptions) =>
        new(delivery, subscriptions, Ct);

    private static OutboxEvent Changed() =>
        OutboxEvent.Create("acme", EventTypes.DocumentChanged, new DocumentChanged(Guid.NewGuid(), "acme", DocumentChangeKind.Updated, "t"));

    private static OutboxEvent Decision() =>
        OutboxEvent.Create("acme", EventTypes.PolicyDecisionMade, new { allow = true });

    private static Subscription Sub(string name, string bindingKey, HandleEvent handle) => new(name, bindingKey, handle);

    [Fact]
    public async Task EventsReachOnlyTheSubscriptionsWhoseBindingMatches()
    {
        var (delivery, bus) = Create();
        var everything = new List<string>();
        var documentsOnly = new List<string>();
        await using var running = Start(
            delivery,
            Sub("audit", "#", (e, _) => { everything.Add(e.EventType); return Task.CompletedTask; }),
            Sub("cache", EventTypes.DocumentChanged, (e, _) => { documentsOnly.Add(e.EventType); return Task.CompletedTask; }));

        await delivery.Publish(Changed(), Ct);
        await delivery.Publish(Decision(), Ct);
        await bus.DrainAsync(DrainTimeout);

        everything.Should().BeEquivalentTo([EventTypes.DocumentChanged, EventTypes.PolicyDecisionMade]);
        documentsOnly.Should().Equal(EventTypes.DocumentChanged);
    }

    [Fact]
    public async Task HandlerRunsInsideASpanThatContinuesTheTraceTheEventWasWrittenIn()
    {
        using var recorder = new ActivityRecorder();
        using var requests = new ActivitySource(ActivityRecorder.ParentSourceName);
        OutboxEvent sent;
        ActivityContext request;
        using (var incoming = requests.StartActivity("request")!)
        {
            sent = Changed();
            request = incoming.Context;
        }

        var (delivery, bus) = Create();
        Activity? during = null;
        await using var running = Start(delivery, Sub("audit", "#", (_, _) => { during = Activity.Current; return Task.CompletedTask; }));

        await delivery.Publish(sent, Ct);
        await bus.DrainAsync(DrainTimeout);

        during.Should().NotBeNull();
        during!.Source.Name.Should().Be(EventTracing.SourceName);
        during.DisplayName.Should().Be("handle document.changed");
        during.Kind.Should().Be(ActivityKind.Consumer);
        during.TraceId.Should().Be(request.TraceId);
        during.ParentSpanId.Should().Be(request.SpanId);
    }

    [Fact]
    public async Task EventWithoutATraceParentIsHandledInANewTrace()
    {
        using var recorder = new ActivityRecorder();
        var sent = Changed() with { TraceId = null, TraceParent = null };
        var (delivery, bus) = Create();
        Activity? during = null;
        await using var running = Start(delivery, Sub("audit", "#", (_, _) => { during = Activity.Current; return Task.CompletedTask; }));

        await delivery.Publish(sent, Ct);
        await bus.DrainAsync(DrainTimeout);

        during.Should().NotBeNull();
        during!.DisplayName.Should().Be("handle document.changed");
        during.ParentSpanId.Should().Be(default(ActivitySpanId));
    }

    [Fact]
    public async Task HandlerReceivesTheEventIntact()
    {
        var (delivery, bus) = Create();
        OutboxEvent? seen = null;
        await using var running = Start(delivery, Sub("audit", "#", (e, _) => { seen = e; return Task.CompletedTask; }));
        var sent = Changed();

        await delivery.Publish(sent, Ct);
        await bus.DrainAsync(DrainTimeout);

        seen!.Id.Should().Be(sent.Id);
        seen.Tenant.Should().Be("acme");
        seen.TraceId.Should().Be(sent.TraceId);
        seen.OccurredAt.Should().Be(sent.OccurredAt);
        seen.Payload.GetRawText().Should().Be(sent.Payload.GetRawText());
    }

    [Fact]
    public async Task FailingHandlerIsRetriedUntilItSucceeds()
    {
        var (delivery, bus) = Create();
        var calls = 0;
        await using var running = Start(delivery, Sub("audit", "#", (_, _) =>
        {
            calls++;
            return calls < 3 ? throw new InvalidOperationException("transient") : Task.CompletedTask;
        }));

        await delivery.Publish(Changed(), Ct);
        await bus.DrainAsync(DrainTimeout);

        calls.Should().Be(3);
        bus.DeadLettered("audit").Should().BeEmpty();
    }

    [Fact]
    public async Task HandlerThatAlwaysFailsIsDeadLetteredAfterTheMaximumAttempts()
    {
        var (delivery, bus) = Create(maxAttempts: 5);
        var calls = 0;
        await using var running = Start(delivery, Sub("audit", "#", (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("permanent");
        }));

        await delivery.Publish(Changed(), Ct);
        await bus.DrainAsync(DrainTimeout);

        calls.Should().Be(5);
        bus.DeadLettered("audit").Should().ContainSingle();
    }

    [Fact]
    public async Task UnreadableMessageIsDeadLetteredWithoutCallingTheHandler()
    {
        var (delivery, bus) = Create();
        var called = false;
        await using var running = Start(delivery, Sub("audit", "#", (_, _) => { called = true; return Task.CompletedTask; }));
        await delivery.Publish(Changed(), Ct);
        await bus.DrainAsync(DrainTimeout);
        called = false;

        bus.PublishRaw(EventTypes.DocumentChanged, "not json"u8.ToArray());
        await bus.DrainAsync(DrainTimeout);

        called.Should().BeFalse();
        bus.DeadLettered("audit").Should().ContainSingle();
    }

    [Fact]
    public async Task OneFailingSubscriptionDoesNotBlockAnother()
    {
        var (delivery, bus) = Create();
        var cacheCalls = 0;
        await using var running = Start(
            delivery,
            Sub("audit", "#", (_, _) => throw new InvalidOperationException("audit store down")),
            Sub("cache", EventTypes.DocumentChanged, (_, _) => { cacheCalls++; return Task.CompletedTask; }));

        await delivery.Publish(Changed(), Ct);
        await bus.DrainAsync(DrainTimeout);

        cacheCalls.Should().Be(1);
        bus.DeadLettered("audit").Should().ContainSingle();
        bus.DeadLettered("cache").Should().BeEmpty();
    }

    [Fact]
    public async Task PublishWaitsUntilTheTopologyIsDeclared()
    {
        var (delivery, bus) = Create();
        var received = new List<OutboxEvent>();

        var publishing = delivery.Publish(Changed(), Ct);
        publishing.IsCompleted.Should().BeFalse();
        await using var running = Start(delivery, Sub("audit", "#", (e, _) => { received.Add(e); return Task.CompletedTask; }));
        await publishing.WaitAsync(DrainTimeout, Ct);
        await bus.DrainAsync(DrainTimeout);

        received.Should().ContainSingle();
    }

    [Fact]
    public async Task ShutdownIsNotCountedAsAHandlerFailure()
    {
        var (delivery, bus) = Create();
        var started = new TaskCompletionSource();
        var running = Start(delivery, Sub("audit", "#", async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }));
        await delivery.Publish(Changed(), Ct);
        await started.Task.WaitAsync(DrainTimeout, Ct);

        await running.DisposeAsync();

        bus.DeadLettered("audit").Should().BeEmpty();
    }

    [Fact]
    public async Task TransportFaultStopsEverySubscriptionAndSurfaces()
    {
        var (delivery, bus) = Create();
        bus.FaultConsumers = true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var act = () => delivery.RunAsync(
            [Sub("audit", "#", (_, _) => Task.CompletedTask), Sub("cache", EventTypes.DocumentChanged, (_, _) => Task.CompletedTask)],
            cts.Token).WaitAsync(DrainTimeout, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
