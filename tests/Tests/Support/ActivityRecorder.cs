using System.Collections.Concurrent;
using System.Diagnostics;
using Core.Features.Events;

namespace Tests.Support;

/// <summary>
/// Records every finished activity from the event tracing source. Listeners are process-wide and tests run in
/// parallel, so callers filter by the trace id they created.
/// </summary>
internal sealed class ActivityRecorder : IDisposable
{
    private readonly ActivityListener _listener;

    public ActivityRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is EventTracing.SourceName or ParentSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Stopped.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Source for the "incoming request" activities tests start themselves.</summary>
    public const string ParentSourceName = "Tests.Requests";

    public ConcurrentBag<Activity> Stopped { get; } = [];

    public IReadOnlyList<Activity> InTrace(ActivityTraceId traceId) => Stopped.Where(a => a.TraceId == traceId).ToList();

    public void Dispose() => _listener.Dispose();
}
