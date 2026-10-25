using System.Diagnostics;
using AwesomeAssertions;
using Core.Features.Events;
using Tests.Support;
using Xunit;

namespace Tests.Core;

public sealed class EventTracingTests
{
    private static OutboxEvent Changed() =>
        OutboxEvent.Create("acme", EventTypes.DocumentChanged, new DocumentChanged(Guid.NewGuid(), "acme", DocumentChangeKind.Updated, "t"));

    [Fact]
    public void CreateCapturesTheTraceParentOfTheCurrentActivity()
    {
        using var recorder = new ActivityRecorder();
        using var requests = new ActivitySource(ActivityRecorder.ParentSourceName);
        using var incoming = requests.StartActivity("request")!;

        var outbox = Changed();

        outbox.TraceParent.Should().Be(incoming.Id);
        outbox.TraceId.Should().Be(incoming.TraceId.ToString());
    }

    [Fact]
    public void CreateOutsideAnActivityHasNoTraceParent()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            Changed().TraceParent.Should().BeNull();
        }
        finally
        {
            Activity.Current = previous;
        }
    }
}
