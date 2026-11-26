namespace SingularFlow.Application.SimulationJobs;

public sealed record SimulationJobRetryPolicy
{
    public const int MinimumAttempts = 1;
    public const int MaximumAttemptsLimit = 10;

    public static TimeSpan MaximumAllowedDelay { get; } =
        TimeSpan.FromMinutes(5);

    public static SimulationJobRetryPolicy Default { get; } = new(
        maxAttempts: 3,
        baseDelay: TimeSpan.FromSeconds(5),
        maximumDelay: TimeSpan.FromMinutes(5));

    public SimulationJobRetryPolicy(
        int maxAttempts,
        TimeSpan baseDelay,
        TimeSpan maximumDelay)
    {
        if (maxAttempts < MinimumAttempts ||
            maxAttempts > MaximumAttemptsLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        if (baseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelay));
        }

        if (maximumDelay <= TimeSpan.Zero ||
            maximumDelay < baseDelay ||
            maximumDelay > MaximumAllowedDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelay));
        }

        MaxAttempts = maxAttempts;
        BaseDelay = baseDelay;
        MaximumDelay = maximumDelay;
    }

    public int MaxAttempts { get; }

    public TimeSpan BaseDelay { get; }

    public TimeSpan MaximumDelay { get; }

    public TimeSpan GetDelayAfterAttempt(int attemptCount)
    {
        if (attemptCount < MinimumAttempts ||
            attemptCount > MaximumAttemptsLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        }

        long delayTicks = BaseDelay.Ticks;
        long maximumTicks = MaximumDelay.Ticks;

        for (int attempt = 1;
             attempt < attemptCount && delayTicks < maximumTicks;
             attempt++)
        {
            delayTicks = delayTicks > maximumTicks / 2
                ? maximumTicks
                : delayTicks * 2;
        }

        return TimeSpan.FromTicks(Math.Min(delayTicks, maximumTicks));
    }
}