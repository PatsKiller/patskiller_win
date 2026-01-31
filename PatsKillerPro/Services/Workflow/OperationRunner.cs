using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// Enhanced workflow runner with NRC-aware error classification,
    /// user action prompts, and proper state machine enforcement.
    /// </summary>
    public sealed class OperationRunner : IDisposable
    {
        private readonly object _stateLock = new();
        private OperationState _state = OperationState.Idle;
        private CancellationTokenSource? _internalCts;
        private TaskCompletionSource<bool>? _userActionTcs;
        private bool _disposed;

        #region Events

        /// <summary>Fired when operation state changes</summary>
        public event EventHandler<OperationProgressEventArgs>? ProgressUpdated;

        /// <summary>Fired when an error occurs (includes retry info)</summary>
        public event EventHandler<OperationErrorEventArgs>? ErrorOccurred;

        /// <summary>Fired when user action is required</summary>
        public event EventHandler<UserActionRequiredEventArgs>? UserActionRequired;

        /// <summary>Fired when operation completes (success or failure)</summary>
        public event EventHandler<OperationCompleteEventArgs>? OperationCompleted;

        /// <summary>Fired for logging messages</summary>
        public event EventHandler<OperationLogEventArgs>? LogMessage;

        #endregion

        #region Properties

        /// <summary>Current operation state</summary>
        public OperationState State
        {
            get { lock (_stateLock) return _state; }
            private set { lock (_stateLock) _state = value; }
        }

        /// <summary>Name of current step being executed</summary>
        public string? CurrentStepName { get; private set; }

        /// <summary>Index of current step (1-based)</summary>
        public int CurrentStepIndex { get; private set; }

        /// <summary>Total number of steps in current operation</summary>
        public int TotalSteps { get; private set; }

        /// <summary>Elapsed time since operation started</summary>
        public TimeSpan ElapsedTime => _stopwatch?.Elapsed ?? TimeSpan.Zero;

        /// <summary>Last error message</summary>
        public string? LastError { get; private set; }

        /// <summary>Whether the runner is currently busy</summary>
        public bool IsBusy => State != OperationState.Idle;

        #endregion

        private Stopwatch? _stopwatch;

        /// <summary>
        /// Runs an operation with full state machine enforcement.
        /// </summary>
        public async Task<OperationResult> RunAsync(IOperation operation, CancellationToken ct = default)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            // Prevent concurrent operations
            lock (_stateLock)
            {
                if (_state != OperationState.Idle)
                {
                    return OperationResult.Fail("Operation runner is busy", ErrorCategory.FailFast);
                }
                _state = OperationState.Prepare;
            }

            _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var linkedCt = _internalCts.Token;
            _stopwatch = Stopwatch.StartNew();

            TotalSteps = operation.Steps.Count;
            CurrentStepIndex = 0;
            LastError = null;

            Log(LogLevel.Info, $"Starting operation: {operation.Name} ({TotalSteps} steps)");
            EmitProgress(0, "(preparing)", OperationState.Prepare);

            try
            {
                // Execute each step
                for (int i = 0; i < operation.Steps.Count; i++)
                {
                    linkedCt.ThrowIfCancellationRequested();

                    var step = operation.Steps[i];
                    CurrentStepIndex = i + 1;
                    CurrentStepName = step.Name;

                    Log(LogLevel.Info, $"Step {CurrentStepIndex}/{TotalSteps}: {step.Name}");

                    // Check for user action prompt before step
                    if (step.UserActionPrompt != null)
                    {
                        var actionResult = await WaitForUserActionAsync(step.UserActionPrompt, linkedCt);
                        if (!actionResult)
                        {
                            return CompleteWithResult(OperationResult.Cancelled(), operation.Name);
                        }
                    }

                    // Pre-delay
                    if (step.PreDelay > TimeSpan.Zero)
                    {
                        Log(LogLevel.Debug, $"Pre-delay: {step.PreDelay.TotalMilliseconds}ms");
                        await Task.Delay(step.PreDelay, linkedCt);
                    }

                    // Execute with retry
                    State = OperationState.Execute;
                    EmitProgress(CurrentStepIndex, step.Name, OperationState.Execute, step.Description);

                    var executeResult = await ExecuteStepWithRetryAsync(step, linkedCt);
                    if (!executeResult.Success)
                    {
                        if (step.IsCritical)
                        {
                            return CompleteWithResult(executeResult, operation.Name);
                        }
                        Log(LogLevel.Warning, $"Non-critical step failed: {step.Name}");
                    }

                    // Post-delay
                    if (step.PostDelay > TimeSpan.Zero)
                    {
                        Log(LogLevel.Debug, $"Post-delay: {step.PostDelay.TotalMilliseconds}ms");
                        await Task.Delay(step.PostDelay, linkedCt);
                    }

                    // Verification
                    if (step.VerifyAsync != null)
                    {
                        State = OperationState.Verify;
                        EmitProgress(CurrentStepIndex, step.Name, OperationState.Verify, "Verifying...");

                        var verifyResult = await VerifyStepAsync(step, linkedCt);
                        if (!verifyResult && step.IsCritical)
                        {
                            LastError = $"Verification failed for step '{step.Name}'";
                            return CompleteWithResult(
                                OperationResult.Fail(LastError, ErrorCategory.FailFast),
                                operation.Name);
                        }
                    }
                }

                // Success
                Log(LogLevel.Info, $"Operation completed successfully: {operation.Name}");
                State = OperationState.Complete;
                EmitProgress(TotalSteps, "(complete)", OperationState.Complete);

                return CompleteWithResult(OperationResult.Ok(), operation.Name);
            }
            catch (OperationCanceledException)
            {
                Log(LogLevel.Info, "Operation cancelled");
                State = OperationState.Cancelled;
                return CompleteWithResult(OperationResult.Cancelled(), operation.Name);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Unexpected error: {ex.Message}");
                State = OperationState.Error;
                LastError = ex.Message;
                return CompleteWithResult(
                    OperationResult.Fail(ex.Message, ErrorCategory.Unknown),
                    operation.Name);
            }
            finally
            {
                _stopwatch?.Stop();
                _internalCts?.Dispose();
                _internalCts = null;
                
                lock (_stateLock)
                {
                    _state = OperationState.Idle;
                }
            }
        }

        private async Task<OperationResult> ExecuteStepWithRetryAsync(OperationStep step, CancellationToken ct)
        {
            Exception? lastException = null;
            var policy = step.RetryPolicy;

            for (int attempt = 1; attempt <= policy.MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await step.ExecuteAsync(ct);
                    return OperationResult.Ok();
                }
                catch (StepException ex)
                {
                    lastException = ex;

                    // Classify error
                    var category = ex.Category;
                    var willRetry = category == ErrorCategory.Retryable && attempt < policy.MaxAttempts;

                    Log(willRetry ? LogLevel.Warning : LogLevel.Error,
                        $"Step '{step.Name}' failed (attempt {attempt}/{policy.MaxAttempts}): {ex.Message}");

                    EmitError(step.Name, ex.Message, category, ex.Nrc, willRetry, attempt);

                    // Handle user action required
                    if (category == ErrorCategory.UserActionRequired && ex.RequiredAction != null)
                    {
                        var actionResult = await WaitForUserActionAsync(ex.RequiredAction, ct);
                        if (actionResult)
                        {
                            // User completed action, retry
                            continue;
                        }
                        return OperationResult.Cancelled();
                    }

                    // Fail-fast: don't retry
                    if (category == ErrorCategory.FailFast)
                    {
                        LastError = ex.Message;
                        return OperationResult.Fail(ex.Message, category, ex.Nrc);
                    }

                    // Retryable: wait and retry
                    if (willRetry)
                    {
                        var delay = policy.GetDelayForAttempt(attempt);
                        Log(LogLevel.Debug, $"Retrying in {delay.TotalMilliseconds}ms");
                        await Task.Delay(delay, ct);
                    }
                }
                catch (Exception ex) when (attempt < policy.MaxAttempts)
                {
                    lastException = ex;

                    // Check if this is a transient error
                    if (policy.ShouldRetry(ex, attempt, step.NrcContext))
                    {
                        Log(LogLevel.Warning, $"Step '{step.Name}' error (attempt {attempt}): {ex.Message}");
                        EmitError(step.Name, ex.Message, ErrorCategory.Retryable, null, true, attempt);

                        var delay = policy.GetDelayForAttempt(attempt);
                        await Task.Delay(delay, ct);
                    }
                    else
                    {
                        // Not retryable
                        throw;
                    }
                }
            }

            // Exhausted retries
            LastError = lastException?.Message ?? "Step failed after all retries";
            var errorCategory = lastException is StepException se ? se.Category : ErrorCategory.FailFast;
            return OperationResult.Fail(LastError, errorCategory);
        }

        private async Task<bool> VerifyStepAsync(OperationStep step, CancellationToken ct)
        {
            const int maxVerifyAttempts = 10;
            var pollInterval = TimeSpan.FromMilliseconds(500);

            for (int v = 0; v < maxVerifyAttempts; v++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (await step.VerifyAsync!(ct))
                    {
                        Log(LogLevel.Debug, $"Verification passed (attempt {v + 1})");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"Verification error: {ex.Message}");
                }

                if (v < maxVerifyAttempts - 1)
                {
                    await Task.Delay(pollInterval, ct);
                }
            }

            Log(LogLevel.Warning, $"Verification failed after {maxVerifyAttempts} attempts");
            return false;
        }

        private async Task<bool> WaitForUserActionAsync(UserActionPrompt prompt, CancellationToken ct)
        {
            State = OperationState.Waiting;
            EmitProgress(CurrentStepIndex, CurrentStepName ?? "", OperationState.Waiting, prompt.Instruction);

            _userActionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Log(LogLevel.Info, $"Waiting for user action: {prompt.Title}");

            // Fire event for UI to handle
            UserActionRequired?.Invoke(this, new UserActionRequiredEventArgs
            {
                Prompt = prompt,
                ResponseSource = _userActionTcs
            });

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(prompt.Timeout);

                var completedTask = await Task.WhenAny(
                    _userActionTcs.Task,
                    Task.Delay(Timeout.Infinite, timeoutCts.Token)
                );

                if (completedTask == _userActionTcs.Task)
                {
                    return await _userActionTcs.Task;
                }

                // Timeout
                if (prompt.AutoContinueOnTimeout)
                {
                    Log(LogLevel.Warning, "User action timed out - auto-continuing");
                    return true;
                }

                Log(LogLevel.Warning, "User action timed out");
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _userActionTcs = null;
            }
        }

        /// <summary>
        /// Signals that the user has completed the required action.
        /// </summary>
        public void ResumeAfterUserAction(bool success = true)
        {
            _userActionTcs?.TrySetResult(success);
        }

        /// <summary>
        /// Requests cancellation of the current operation.
        /// </summary>
        public void RequestCancel()
        {
            _internalCts?.Cancel();
            _userActionTcs?.TrySetResult(false);
        }

        private OperationResult CompleteWithResult(OperationResult result, string operationName)
        {
            OperationCompleted?.Invoke(this, new OperationCompleteEventArgs
            {
                Result = result,
                Duration = ElapsedTime,
                OperationName = operationName
            });

            return result with
            {
                Duration = ElapsedTime,
                TotalSteps = TotalSteps,
                CompletedSteps = CurrentStepIndex
            };
        }

        private void EmitProgress(int stepIndex, string stepName, OperationState state, string? description = null)
        {
            ProgressUpdated?.Invoke(this, new OperationProgressEventArgs
            {
                StepIndex = stepIndex,
                TotalSteps = TotalSteps,
                StepName = stepName,
                State = state,
                StepDescription = description
            });
        }

        private void EmitError(string stepName, string message, ErrorCategory category, byte? nrc, bool willRetry, int attempt)
        {
            ErrorOccurred?.Invoke(this, new OperationErrorEventArgs
            {
                StepName = stepName,
                ErrorMessage = message,
                Category = category,
                Nrc = nrc,
                WillRetry = willRetry,
                RetryAttempt = attempt
            });
        }

        private void Log(LogLevel level, string message)
        {
            LogMessage?.Invoke(this, new OperationLogEventArgs
            {
                Message = message,
                Level = level
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            RequestCancel();
            _internalCts?.Dispose();
        }
    }
}
