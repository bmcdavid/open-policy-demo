namespace Core.Features.Messaging;

internal enum RetryDecision
{
    Retry,
    DeadLetter,
}

internal static class RetryPolicy
{
    public const int DefaultMaxAttempts = 5;

    /// <param name="attempt">How many times processing has already failed for this message, including this failure.</param>
    public static RetryDecision Decide(int attempt, int maxAttempts) => attempt >= maxAttempts ? RetryDecision.DeadLetter : RetryDecision.Retry;

    public static TimeSpan DefaultDelay(int attempt) => TimeSpan.FromSeconds(Math.Min(attempt, 5));
}
