using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow
{
    public enum OperationState
    {
        Idle,
        Prepare,
        Execute,
        Verify,
        Waiting,
        Complete,
        Error,
        Cancelled
    }

    public enum ErrorCategory
    {
        FailFast,
        Retryable,
        UserActionRequired,
        Unknown
    }

    public sealed class OperationResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public ErrorCategory? Category { get; init; }

        public static OperationResult Ok() => new() { Success = true };
        public static OperationResult Fail(string message, ErrorCategory category = ErrorCategory.Unknown) =>
            new() { Success = false, ErrorMessage = message, Category = category };
    }

    public sealed class OperationStep
    {
        public required string Name { get; init; }
        public string? Description { get; init; }

        /// <summary>Actual work for the step. Throw to signal failure.</summary>
        public required Func<CancellationToken, Task> ExecuteAsync { get; init; }

        /// <summary>Optional verification. Return true if verified; false if not yet verified.</summary>
        public Func<CancellationToken, Task<bool>>? VerifyAsync { get; init; }

        public TimeSpan PreDelay { get; init; } = TimeSpan.Zero;
        public TimeSpan PostDelay { get; init; } = TimeSpan.Zero;

        public RetryPolicy RetryPolicy { get; init; } = RetryPolicy.Standard;
        public bool IsCritical { get; init; } = true;
    }

    public interface IOperation
    {
        string Name { get; }
        string? Description { get; }
        IReadOnlyList<OperationStep> Steps { get; }
        TimeSpan? EstimatedDuration { get; }
    }

    public sealed class UserActionRequiredEventArgs : EventArgs
    {
        public required string Instruction { get; init; }
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    }

    public sealed class OperationProgressEventArgs : EventArgs
    {
        public required int StepIndex { get; init; }
        public required int TotalSteps { get; init; }
        public required string StepName { get; init; }
        public required OperationState State { get; init; }
    }

    public sealed class OperationErrorEventArgs : EventArgs
    {
        public required string StepName { get; init; }
        public required string ErrorMessage { get; init; }
        public ErrorCategory Category { get; init; } = ErrorCategory.Unknown;
    }
}
