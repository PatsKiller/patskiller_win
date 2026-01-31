using System;

namespace PatsKillerPro.Services.Workflow
{
    public sealed class RetryPolicy
    {
        public int MaxAttempts { get; init; } = 3;
        public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(150);
        public double BackoffMultiplier { get; init; } = 2.0;
        public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(2);

        public static RetryPolicy NoRetry => new() { MaxAttempts = 1 };
        public static RetryPolicy Standard => new() { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(150) };
        public static RetryPolicy Patient => new() { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(500), MaxDelay = TimeSpan.FromSeconds(5) };
    }
}
