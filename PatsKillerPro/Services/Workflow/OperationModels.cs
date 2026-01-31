using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// Operation state machine states
    /// </summary>
    public enum OperationState
    {
        Idle,
        Prepare,
        Execute,
        Verify,
        Waiting,      // Paused for user action or delay
        Complete,
        Error,
        Cancelled
    }

    /// <summary>
    /// Error categories per EZimmo spec
    /// </summary>
    public enum ErrorCategory
    {
        /// <summary>Cannot recover - stop immediately, don't retry</summary>
        FailFast,
        
        /// <summary>Transient error - auto-retry with backoff</summary>
        Retryable,
        
        /// <summary>Requires physical user action before continuing</summary>
        UserActionRequired,
        
        /// <summary>Category not determined</summary>
        Unknown
    }

    /// <summary>
    /// Result of an operation
    /// </summary>
    public sealed class OperationResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public ErrorCategory? Category { get; init; }
        public byte? Nrc { get; init; }
        public object? ResultData { get; init; }
        public TimeSpan Duration { get; init; }
        public int TotalSteps { get; init; }
        public int CompletedSteps { get; init; }

        public static OperationResult Ok(object? data = null) => new() { Success = true, ResultData = data };
        
        public static OperationResult Fail(string message, ErrorCategory category = ErrorCategory.Unknown, byte? nrc = null) =>
            new() { Success = false, ErrorMessage = message, Category = category, Nrc = nrc };

        public static OperationResult FailWithNrc(byte nrc, NrcContext context = NrcContext.Default)
        {
            var classification = NrcClassification.FromNrc(nrc, context);
            return new()
            {
                Success = false,
                ErrorMessage = classification.UserMessage,
                Category = classification.Category,
                Nrc = nrc
            };
        }

        public static OperationResult Cancelled() => 
            new() { Success = false, ErrorMessage = "Operation cancelled", Category = ErrorCategory.UserActionRequired };
    }

    /// <summary>
    /// A step within an operation
    /// </summary>
    public sealed class OperationStep
    {
        /// <summary>Step name for logging/display</summary>
        public required string Name { get; init; }

        /// <summary>Description for user display</summary>
        public string? Description { get; init; }

        /// <summary>The actual work to perform. Throw StepException to signal failure with category.</summary>
        public required Func<CancellationToken, Task> ExecuteAsync { get; init; }

        /// <summary>Optional verification after execution. Return true if verified.</summary>
        public Func<CancellationToken, Task<bool>>? VerifyAsync { get; init; }

        /// <summary>Delay before executing this step</summary>
        public TimeSpan PreDelay { get; init; } = TimeSpan.Zero;

        /// <summary>Delay after executing this step (before verify)</summary>
        public TimeSpan PostDelay { get; init; } = TimeSpan.Zero;

        /// <summary>Retry policy for this step</summary>
        public RetryPolicy RetryPolicy { get; init; } = RetryPolicy.Standard;

        /// <summary>If true, failure stops the entire operation</summary>
        public bool IsCritical { get; init; } = true;

        /// <summary>NRC context for error classification</summary>
        public NrcContext NrcContext { get; init; } = NrcContext.Default;

        /// <summary>Optional user action instruction if this step requires intervention</summary>
        public UserActionPrompt? UserActionPrompt { get; init; }
    }

    /// <summary>
    /// User action prompt configuration
    /// </summary>
    public sealed class UserActionPrompt
    {
        /// <summary>Instruction to display to user</summary>
        public required string Instruction { get; init; }

        /// <summary>Title for dialog/notification</summary>
        public string Title { get; init; } = "Action Required";

        /// <summary>How long to wait for user action</summary>
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>Whether to auto-continue after timeout (vs. fail)</summary>
        public bool AutoContinueOnTimeout { get; init; } = false;

        /// <summary>Text for confirm button</summary>
        public string ConfirmText { get; init; } = "Done";

        /// <summary>Text for cancel button</summary>
        public string CancelText { get; init; } = "Cancel";

        // Common prompts
        public static UserActionPrompt CycleIgnition => new()
        {
            Title = "Cycle Ignition",
            Instruction = "Turn ignition OFF, wait 5 seconds, then turn back ON.\n\nClick Done when ready.",
            Timeout = TimeSpan.FromSeconds(60)
        };

        public static UserActionPrompt InsertKey => new()
        {
            Title = "Insert Key",
            Instruction = "Insert the new key into the ignition and turn to ON position.\n\nClick Done when ready.",
            Timeout = TimeSpan.FromSeconds(60)
        };

        public static UserActionPrompt RemoveKey => new()
        {
            Title = "Remove Key",
            Instruction = "Remove the key from the ignition.\n\nClick Done when ready.",
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static UserActionPrompt CloseDoors => new()
        {
            Title = "Close Doors",
            Instruction = "Close all vehicle doors.\n\nClick Done when ready.",
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static UserActionPrompt IgnitionOff => new()
        {
            Title = "Ignition OFF",
            Instruction = "Turn the ignition to the OFF position.\n\nClick Done when ready.",
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static UserActionPrompt IgnitionOn => new()
        {
            Title = "Turn Ignition ON",
            Instruction = "Turn the ignition to the ON position within 10 seconds.\n\nClick Done when ready.",
            Timeout = TimeSpan.FromSeconds(15)
        };

        public static UserActionPrompt WaitForSecurityLockout => new()
        {
            Title = "Security Lockout",
            Instruction = "Security is temporarily locked. Please wait 10 minutes before retrying.",
            Timeout = TimeSpan.FromMinutes(10),
            AutoContinueOnTimeout = false
        };
    }

    /// <summary>
    /// Interface for operations
    /// </summary>
    public interface IOperation
    {
        /// <summary>Operation name for logging/display</summary>
        string Name { get; }

        /// <summary>Description for user display</summary>
        string? Description { get; }

        /// <summary>Ordered list of steps</summary>
        IReadOnlyList<OperationStep> Steps { get; }

        /// <summary>Estimated duration for progress display</summary>
        TimeSpan? EstimatedDuration { get; }

        /// <summary>Token cost for this operation</summary>
        int TokenCost { get; }

        /// <summary>Whether this operation requires an incode</summary>
        bool RequiresIncode { get; }

        /// <summary>Platform pacing configuration</summary>
        PlatformPacingConfig? PacingConfig { get; }

        /// <summary>Platform routing configuration</summary>
        PlatformRoutingConfig? RoutingConfig { get; }
    }

    /// <summary>
    /// Base class for operations
    /// </summary>
    public abstract class OperationBase : IOperation
    {
        public abstract string Name { get; }
        public virtual string? Description => null;
        public abstract IReadOnlyList<OperationStep> Steps { get; }
        public virtual TimeSpan? EstimatedDuration => null;
        public virtual int TokenCost => 0;
        public virtual bool RequiresIncode => false;
        public PlatformPacingConfig? PacingConfig { get; set; }
        public PlatformRoutingConfig? RoutingConfig { get; set; }

        protected OperationStep CreateStep(
            string name,
            Func<CancellationToken, Task> execute,
            string? description = null,
            Func<CancellationToken, Task<bool>>? verify = null,
            TimeSpan? preDelay = null,
            TimeSpan? postDelay = null,
            RetryPolicy? retryPolicy = null,
            bool critical = true,
            NrcContext nrcContext = NrcContext.Default,
            UserActionPrompt? userAction = null)
        {
            return new OperationStep
            {
                Name = name,
                Description = description,
                ExecuteAsync = execute,
                VerifyAsync = verify,
                PreDelay = preDelay ?? TimeSpan.Zero,
                PostDelay = postDelay ?? TimeSpan.Zero,
                RetryPolicy = retryPolicy ?? RetryPolicy.Standard,
                IsCritical = critical,
                NrcContext = nrcContext,
                UserActionPrompt = userAction
            };
        }
    }

    /// <summary>
    /// Exception thrown from step execution with error category
    /// </summary>
    public class StepException : Exception
    {
        public ErrorCategory Category { get; }
        public byte? Nrc { get; }
        public UserActionPrompt? RequiredAction { get; }

        public StepException(string message, ErrorCategory category = ErrorCategory.Unknown, byte? nrc = null)
            : base(message)
        {
            Category = category;
            Nrc = nrc;
        }

        public StepException(string message, UserActionPrompt requiredAction)
            : base(message)
        {
            Category = ErrorCategory.UserActionRequired;
            RequiredAction = requiredAction;
        }

        /// <summary>Creates a StepException from an NRC code</summary>
        public static StepException FromNrc(byte nrc, NrcContext context = NrcContext.Default)
        {
            var classification = NrcClassification.FromNrc(nrc, context);
            return new StepException(classification.UserMessage, classification.Category, nrc);
        }
    }

    #region Event Args

    public sealed class OperationProgressEventArgs : EventArgs
    {
        public required int StepIndex { get; init; }
        public required int TotalSteps { get; init; }
        public required string StepName { get; init; }
        public required OperationState State { get; init; }
        public string? StepDescription { get; init; }
        public double ProgressPercent => TotalSteps > 0 ? (StepIndex * 100.0 / TotalSteps) : 0;
    }

    public sealed class OperationErrorEventArgs : EventArgs
    {
        public required string StepName { get; init; }
        public required string ErrorMessage { get; init; }
        public ErrorCategory Category { get; init; } = ErrorCategory.Unknown;
        public byte? Nrc { get; init; }
        public bool WillRetry { get; init; }
        public int RetryAttempt { get; init; }
    }

    public sealed class UserActionRequiredEventArgs : EventArgs
    {
        public required UserActionPrompt Prompt { get; init; }
        public TaskCompletionSource<bool>? ResponseSource { get; init; }
    }

    public sealed class OperationCompleteEventArgs : EventArgs
    {
        public required OperationResult Result { get; init; }
        public required TimeSpan Duration { get; init; }
        public required string OperationName { get; init; }
    }

    public sealed class OperationLogEventArgs : EventArgs
    {
        public required string Message { get; init; }
        public LogLevel Level { get; init; } = LogLevel.Info;
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    #endregion
}
