using System;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// Retry policy configuration with NRC-aware error classification.
    /// </summary>
    public sealed class RetryPolicy
    {
        /// <summary>Maximum retry attempts</summary>
        public int MaxAttempts { get; init; } = 3;

        /// <summary>Initial delay between retries</summary>
        public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(150);

        /// <summary>Backoff multiplier for exponential backoff</summary>
        public double BackoffMultiplier { get; init; } = 2.0;

        /// <summary>Maximum delay cap</summary>
        public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>Whether to use jitter (randomization) in delays</summary>
        public bool UseJitter { get; init; } = true;

        /// <summary>Custom predicate to determine if an exception should trigger retry</summary>
        public Func<Exception, bool>? ShouldRetryPredicate { get; init; }

        /// <summary>
        /// Calculates delay for a given attempt number.
        /// </summary>
        public TimeSpan GetDelayForAttempt(int attemptNumber)
        {
            if (attemptNumber <= 1) return InitialDelay;

            var delay = InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attemptNumber - 1);
            delay = Math.Min(delay, MaxDelay.TotalMilliseconds);

            if (UseJitter)
            {
                // Add ±25% jitter
                var jitter = delay * 0.25 * (Random.Shared.NextDouble() * 2 - 1);
                delay = Math.Max(0, delay + jitter);
            }

            return TimeSpan.FromMilliseconds(delay);
        }

        /// <summary>
        /// Determines if an exception should trigger a retry based on NRC classification.
        /// </summary>
        public bool ShouldRetry(Exception ex, int currentAttempt, NrcContext context = NrcContext.Default)
        {
            if (currentAttempt >= MaxAttempts)
                return false;

            // Check custom predicate first
            if (ShouldRetryPredicate != null)
                return ShouldRetryPredicate(ex);

            // Check for StepException with category
            if (ex is StepException stepEx)
            {
                return stepEx.Category == ErrorCategory.Retryable;
            }

            // Check for NRC in exception data
            if (ex.Data.Contains("NRC") && ex.Data["NRC"] is byte nrc)
            {
                var category = NrcClassifier.Classify(nrc, context);
                return category == ErrorCategory.Retryable;
            }

            // Default: check common transient error patterns
            return IsTransientErrorMessage(ex.Message);
        }

        /// <summary>
        /// Determines if an NRC code is retryable.
        /// </summary>
        public static bool IsNrcRetryable(byte nrc, NrcContext context = NrcContext.Default)
        {
            return NrcClassifier.Classify(nrc, context) == ErrorCategory.Retryable;
        }

        /// <summary>
        /// Checks if error message indicates a transient error.
        /// </summary>
        private static bool IsTransientErrorMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.ToLowerInvariant();
            return lower.Contains("timeout") ||
                   lower.Contains("timed out") ||
                   lower.Contains("no response") ||
                   lower.Contains("buffer empty") ||
                   lower.Contains("busy") ||
                   lower.Contains("lost") ||
                   lower.Contains("err_timeout") ||
                   lower.Contains("communication");
        }

        // ========================================
        // PRESET POLICIES
        // ========================================

        /// <summary>No retry - single attempt only</summary>
        public static RetryPolicy NoRetry => new() { MaxAttempts = 1 };

        /// <summary>Standard retry - 3 attempts with 150ms initial delay</summary>
        public static RetryPolicy Standard => new()
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(150),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(2)
        };

        /// <summary>Aggressive retry - 5 attempts with faster backoff</summary>
        public static RetryPolicy Aggressive => new()
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            BackoffMultiplier = 1.5,
            MaxDelay = TimeSpan.FromSeconds(1)
        };

        /// <summary>Patient retry - 3 attempts with longer delays</summary>
        public static RetryPolicy Patient => new()
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(500),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(5)
        };

        /// <summary>Security access retry - 2 attempts (avoid lockout)</summary>
        public static RetryPolicy Security => new()
        {
            MaxAttempts = 2,
            InitialDelay = TimeSpan.FromMilliseconds(300),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(2),
            // Don't retry on invalid key or security denied
            ShouldRetryPredicate = ex =>
            {
                if (ex is StepException stepEx && stepEx.Nrc.HasValue)
                {
                    var nrc = stepEx.Nrc.Value;
                    // Never retry invalid key or exceeded attempts
                    return nrc != NrcClassifier.NRC_INVALID_KEY &&
                           nrc != NrcClassifier.NRC_EXCEEDED_NUMBER_OF_ATTEMPTS &&
                           nrc != NrcClassifier.NRC_SECURITY_ACCESS_DENIED;
                }
                return IsTransientErrorMessage(ex.Message);
            }
        };

        /// <summary>Routine poll retry - many attempts with fixed interval</summary>
        public static RetryPolicy RoutinePoll => new()
        {
            MaxAttempts = 30,
            InitialDelay = TimeSpan.FromMilliseconds(500),
            BackoffMultiplier = 1.0, // No backoff - fixed interval
            MaxDelay = TimeSpan.FromMilliseconds(500),
            UseJitter = false
        };

        /// <summary>Connection retry - patient with long delays</summary>
        public static RetryPolicy Connection => new()
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(500),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(3)
        };

        /// <summary>
        /// Creates a retry policy from NRC response.
        /// Uses recommended delays based on the specific NRC code.
        /// </summary>
        public static RetryPolicy FromNrc(byte nrc, int maxAttempts = 3)
        {
            var category = NrcClassifier.Classify(nrc);
            if (category != ErrorCategory.Retryable)
            {
                return NoRetry;
            }

            var recommendedDelay = NrcClassifier.GetRecommendedRetryDelay(nrc, 1);
            return new RetryPolicy
            {
                MaxAttempts = maxAttempts,
                InitialDelay = recommendedDelay,
                BackoffMultiplier = 2.0,
                MaxDelay = TimeSpan.FromSeconds(2)
            };
        }
    }
}
