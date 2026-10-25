using System.Diagnostics;
using System.Text.Json;
using Core.Features.Events;
using Microsoft.Extensions.Logging;

namespace Core.Features.Messaging;

public delegate Task HandleEvent(OutboxEvent outboxEvent, CancellationToken cancellationToken);

public enum MessageOutcome
{
    Ack,
    Retry,
    DeadLetter,
}

internal static partial class EventMessageProcessor
{
    /// <summary>Decides what to do with one delivered message. Redelivery is safe because every handler is idempotent.</summary>
    public static async Task<MessageOutcome> ProcessAsync(
        ReadOnlyMemory<byte> body,
        int priorAttempts,
        HandleEvent handle,
        ILogger logger,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(logger);

        OutboxEvent? outboxEvent;
        try
        {
            outboxEvent = JsonSerializer.Deserialize<OutboxEvent>(body.Span, EventJson.Options);
        }
        catch (JsonException ex)
        {
            LogUnreadable(logger, ex);
            return MessageOutcome.DeadLetter;
        }

        if (outboxEvent is null)
        {
            LogUnreadable(logger, null);
            return MessageOutcome.DeadLetter;
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = outboxEvent.Id,
            ["EventType"] = outboxEvent.EventType,
            ["TraceId"] = outboxEvent.TraceId,
            ["Tenant"] = outboxEvent.Tenant,
        });

        using var activity = EventTracing.Start("handle", outboxEvent, ActivityKind.Consumer);
        try
        {
            await handle(outboxEvent, cancellationToken);
            return MessageOutcome.Ack;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var attempt = priorAttempts + 1;
            var decision = RetryPolicy.Decide(attempt, maxAttempts);
            LogFailed(logger, ex, outboxEvent.Id, outboxEvent.EventType, attempt, decision);
            return decision == RetryDecision.Retry ? MessageOutcome.Retry : MessageOutcome.DeadLetter;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Dropping an unreadable message to the dead-letter queue")]
    private static partial void LogUnreadable(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling event {EventId} ({EventType}) failed on attempt {Attempt}; decision {Decision}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string eventId, string eventType, int attempt, RetryDecision decision);
}
