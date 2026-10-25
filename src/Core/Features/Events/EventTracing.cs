using System.Diagnostics;

namespace Core.Features.Events;

/// <summary>
/// Spans for the asynchronous half of a request: relaying its outbox events and handling them. Each span continues the
/// trace the event was written in, so the dashboard shows the request and its side effects as one trace.
/// </summary>
public static class EventTracing
{
    public const string SourceName = "Core.Events";

    private static readonly ActivitySource _source = new(SourceName);

    /// <summary>Starts <c>{operation} {eventType}</c> as a child of the event's trace context, or as a new trace without one.</summary>
    internal static Activity? Start(string operation, OutboxEvent outboxEvent, ActivityKind kind)
    {
        ActivityContext.TryParse(outboxEvent.TraceParent, traceState: null, isRemote: true, out var parent);
        return _source.StartActivity($"{operation} {outboxEvent.EventType}", kind, parent);
    }
}
