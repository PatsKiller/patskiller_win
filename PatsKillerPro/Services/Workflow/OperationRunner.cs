using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// Generic, reusable workflow runner for multi-step operations:
    /// Execute → Delay → Verify with retries, plus cancellation + progress events.
    /// 
    /// NOTE: This class is intentionally protocol-agnostic; plug in step ExecuteAsync/VerifyAsync delegates from your service layer.
    /// </summary>
    public sealed class OperationRunner
    {
        public event EventHandler<OperationProgressEventArgs>? ProgressUpdated;
        public event EventHandler<OperationErrorEventArgs>? ErrorOccurred;

        public OperationState State { get; private set; } = OperationState.Idle;

        public async Task<OperationResult> RunAsync(IOperation operation, CancellationToken ct)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (State != OperationState.Idle) return OperationResult.Fail("Operation runner is busy.", ErrorCategory.FailFast);

            var sw = Stopwatch.StartNew();

            try
            {
                State = OperationState.Prepare;
                EmitProgress(0, operation.Steps.Count, "(prepare)", State);

                for (var i = 0; i < operation.Steps.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var step = operation.Steps[i];
                    State = OperationState.Execute;
                    EmitProgress(i + 1, operation.Steps.Count, step.Name, State);

                    if (step.PreDelay > TimeSpan.Zero)
                        await Task.Delay(step.PreDelay, ct);

                    // Execute with retry policy
                    Exception? lastEx = null;
                    var delay = step.RetryPolicy.InitialDelay;

                    for (var attempt = 1; attempt <= step.RetryPolicy.MaxAttempts; attempt++)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            await step.ExecuteAsync(ct);
                            lastEx = null;
                            break;
                        }
                        catch (Exception ex) when (attempt < step.RetryPolicy.MaxAttempts)
                        {
                            lastEx = ex;
                            await Task.Delay(delay, ct);

                            var nextMs = Math.Min(
                                (int)(delay.TotalMilliseconds * step.RetryPolicy.BackoffMultiplier),
                                (int)step.RetryPolicy.MaxDelay.TotalMilliseconds);

                            delay = TimeSpan.FromMilliseconds(Math.Max(0, nextMs));
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                            break;
                        }
                    }

                    if (lastEx != null)
                    {
                        State = OperationState.Error;
                        ErrorOccurred?.Invoke(this, new OperationErrorEventArgs
                        {
                            StepName = step.Name,
                            ErrorMessage = lastEx.Message,
                            Category = ErrorCategory.Unknown
                        });

                        return OperationResult.Fail($"Step '{step.Name}' failed: {lastEx.Message}");
                    }

                    if (step.PostDelay > TimeSpan.Zero)
                        await Task.Delay(step.PostDelay, ct);

                    // Verify (optional)
                    if (step.VerifyAsync != null)
                    {
                        State = OperationState.Verify;
                        EmitProgress(i + 1, operation.Steps.Count, step.Name, State);

                        // Basic verify loop: 10 tries, 500ms spacing (caller can embed smarter logic)
                        for (var v = 0; v < 10; v++)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (await step.VerifyAsync(ct))
                                break;

                            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);

                            if (v == 9)
                                return OperationResult.Fail($"Verification failed for step '{step.Name}'.", ErrorCategory.FailFast);
                        }
                    }
                }

                State = OperationState.Complete;
                EmitProgress(operation.Steps.Count, operation.Steps.Count, "(complete)", State);

                return OperationResult.Ok();
            }
            catch (OperationCanceledException)
            {
                State = OperationState.Cancelled;
                return OperationResult.Fail("Operation cancelled.", ErrorCategory.UserActionRequired);
            }
            catch (Exception ex)
            {
                State = OperationState.Error;
                return OperationResult.Fail(ex.Message);
            }
            finally
            {
                sw.Stop();
                State = OperationState.Idle;
            }
        }

        private void EmitProgress(int idx, int total, string stepName, OperationState state)
        {
            ProgressUpdated?.Invoke(this, new OperationProgressEventArgs
            {
                StepIndex = idx,
                TotalSteps = total,
                StepName = stepName,
                State = state
            });
        }
    }
}
