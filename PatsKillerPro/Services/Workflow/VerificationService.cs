using System;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// Verification service implementing the Execute→Delay→Verify→Report pattern.
    /// Ensures state actually changed after mutating operations.
    /// </summary>
    public sealed class VerificationService
    {
        private readonly Action<string>? _log;

        public VerificationService(Action<string>? log = null)
        {
            _log = log;
        }

        /// <summary>
        /// Executes an operation with verification polling.
        /// </summary>
        /// <typeparam name="T">Type of state to verify</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="verify">Function to check if state changed as expected</param>
        /// <param name="config">Verification configuration</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Verification result</returns>
        public async Task<VerificationResult<T>> ExecuteWithVerificationAsync<T>(
            Func<CancellationToken, Task<OperationResponse>> operation,
            Func<CancellationToken, Task<VerifyState<T>>> verify,
            VerificationConfig config,
            CancellationToken ct = default)
        {
            // Execute the operation
            _log?.Invoke($"Executing operation: {config.OperationName}");

            var executeResult = await operation(ct).ConfigureAwait(false);

            if (!executeResult.Success)
            {
                return VerificationResult<T>.Failed(
                    $"Operation failed: {executeResult.ErrorMessage}",
                    executeResult.Nrc,
                    default);
            }

            // Post-execute delay (critical for some operations)
            if (config.PostExecuteDelay > TimeSpan.Zero)
            {
                _log?.Invoke($"Waiting {config.PostExecuteDelay.TotalMilliseconds}ms after execution");
                await Task.Delay(config.PostExecuteDelay, ct).ConfigureAwait(false);
            }

            // Verification polling loop
            _log?.Invoke($"Starting verification polling: max {config.MaxAttempts} attempts, {config.PollInterval.TotalMilliseconds}ms interval");

            for (int attempt = 1; attempt <= config.MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var verifyResult = await verify(ct).ConfigureAwait(false);

                    if (verifyResult.IsVerified)
                    {
                        _log?.Invoke($"Verification succeeded on attempt {attempt}");
                        return VerificationResult<T>.Succeeded(verifyResult.State, attempt);
                    }

                    if (verifyResult.IsFatalError)
                    {
                        _log?.Invoke($"Verification encountered fatal error: {verifyResult.ErrorMessage}");
                        return VerificationResult<T>.Failed(
                            verifyResult.ErrorMessage ?? "Verification failed with fatal error",
                            verifyResult.Nrc,
                            verifyResult.State);
                    }

                    _log?.Invoke($"Verification attempt {attempt}/{config.MaxAttempts}: Not yet verified");
                }
                catch (Exception ex) when (attempt < config.MaxAttempts)
                {
                    _log?.Invoke($"Verification attempt {attempt} error (will retry): {ex.Message}");
                }

                // Wait before next attempt
                if (attempt < config.MaxAttempts)
                {
                    await Task.Delay(config.PollInterval, ct).ConfigureAwait(false);
                }
            }

            // Exhausted all attempts
            _log?.Invoke($"Verification failed after {config.MaxAttempts} attempts");

            // Do one final check to get current state for partial success reporting
            try
            {
                var finalState = await verify(ct).ConfigureAwait(false);
                return VerificationResult<T>.PartialSuccess(
                    "Operation completed but verification failed - state may have changed",
                    finalState.State,
                    config.MaxAttempts);
            }
            catch
            {
                return VerificationResult<T>.Failed(
                    $"Verification failed after {config.MaxAttempts} attempts",
                    null,
                    default);
            }
        }

        /// <summary>
        /// Polls for routine completion (0x31 0x03).
        /// </summary>
        public async Task<RoutineCompletionResult> PollRoutineCompletionAsync(
            Func<CancellationToken, Task<RoutineStatusResponse>> pollStatus,
            VerificationConfig config,
            CancellationToken ct = default)
        {
            _log?.Invoke($"Polling routine completion: max {config.MaxAttempts} attempts");

            for (int attempt = 1; attempt <= config.MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var status = await pollStatus(ct).ConfigureAwait(false);

                // Check for completion
                if (status.IsComplete)
                {
                    if (status.IsSuccess)
                    {
                        _log?.Invoke($"Routine completed successfully on attempt {attempt}");
                        return RoutineCompletionResult.Success(status.StatusCode, attempt);
                    }
                    else
                    {
                        _log?.Invoke($"Routine completed with error: 0x{status.StatusCode:X2}");
                        return RoutineCompletionResult.Failed(status.StatusCode, status.ErrorMessage);
                    }
                }

                // Still in progress
                if (status.IsPending)
                {
                    _log?.Invoke($"Routine still in progress (attempt {attempt}/{config.MaxAttempts})");
                }

                // NRC response pending (0x78)
                if (status.IsResponsePending)
                {
                    _log?.Invoke("Response pending - continuing poll");
                }

                // Wait before next poll
                if (attempt < config.MaxAttempts)
                {
                    await Task.Delay(config.PollInterval, ct).ConfigureAwait(false);
                }
            }

            return RoutineCompletionResult.Timeout(config.MaxAttempts);
        }

        /// <summary>
        /// Simple verification with retry on read failure.
        /// </summary>
        public async Task<bool> VerifyStateAsync<T>(
            Func<CancellationToken, Task<T>> readState,
            Func<T, bool> isExpected,
            int maxAttempts = 3,
            TimeSpan? pollInterval = null,
            CancellationToken ct = default)
        {
            var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var state = await readState(ct).ConfigureAwait(false);
                    if (isExpected(state))
                    {
                        return true;
                    }
                }
                catch when (attempt < maxAttempts)
                {
                    // Retry on read failure
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Configuration for verification polling
    /// </summary>
    public sealed class VerificationConfig
    {
        public string OperationName { get; init; } = "Operation";
        public TimeSpan PostExecuteDelay { get; init; } = TimeSpan.FromMilliseconds(200);
        public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
        public int MaxAttempts { get; init; } = 10;
        public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(15);

        public static VerificationConfig Default => new()
        {
            PostExecuteDelay = TimeSpan.FromMilliseconds(200),
            PollInterval = TimeSpan.FromMilliseconds(500),
            MaxAttempts = 10
        };

        public static VerificationConfig ForKeyProgramming => new()
        {
            OperationName = "Key Programming",
            PostExecuteDelay = TimeSpan.FromMilliseconds(300),
            PollInterval = TimeSpan.FromMilliseconds(500),
            MaxAttempts = 30,
            TotalTimeout = TimeSpan.FromSeconds(15)
        };

        public static VerificationConfig ForPatsInit => new()
        {
            OperationName = "PATS Initialization",
            PostExecuteDelay = TimeSpan.FromMilliseconds(500),
            PollInterval = TimeSpan.FromMilliseconds(1000),
            MaxAttempts = 30,
            TotalTimeout = TimeSpan.FromSeconds(30)
        };

        public static VerificationConfig ForDtcClear => new()
        {
            OperationName = "DTC Clear",
            PostExecuteDelay = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(300),
            MaxAttempts = 5
        };
    }

    /// <summary>
    /// Response from an operation execution
    /// </summary>
    public sealed class OperationResponse
    {
        public bool Success { get; init; }
        public byte[]? Data { get; init; }
        public byte? Nrc { get; init; }
        public string? ErrorMessage { get; init; }

        public static OperationResponse Ok(byte[]? data = null) => new() { Success = true, Data = data };
        public static OperationResponse Failed(string message, byte? nrc = null) => 
            new() { Success = false, ErrorMessage = message, Nrc = nrc };
    }

    /// <summary>
    /// State verification result
    /// </summary>
    public sealed class VerifyState<T>
    {
        public bool IsVerified { get; init; }
        public bool IsFatalError { get; init; }
        public T? State { get; init; }
        public byte? Nrc { get; init; }
        public string? ErrorMessage { get; init; }

        public static VerifyState<T> Verified(T state) => new() { IsVerified = true, State = state };
        public static VerifyState<T> NotYet(T currentState) => new() { IsVerified = false, State = currentState };
        public static VerifyState<T> Fatal(string error, byte? nrc = null) => 
            new() { IsFatalError = true, ErrorMessage = error, Nrc = nrc };
    }

    /// <summary>
    /// Result of verification with state
    /// </summary>
    public sealed class VerificationResult<T>
    {
        public bool Success { get; init; }
        public bool IsPartialSuccess { get; init; }
        public T? State { get; init; }
        public byte? Nrc { get; init; }
        public string? Message { get; init; }
        public int AttemptsUsed { get; init; }

        public static VerificationResult<T> Succeeded(T? state, int attempts) => 
            new() { Success = true, State = state, AttemptsUsed = attempts };
        
        public static VerificationResult<T> Failed(string message, byte? nrc, T? state) => 
            new() { Success = false, Message = message, Nrc = nrc, State = state };
        
        public static VerificationResult<T> PartialSuccess(string message, T? state, int attempts) => 
            new() { Success = false, IsPartialSuccess = true, Message = message, State = state, AttemptsUsed = attempts };
    }

    /// <summary>
    /// Response from routine status poll
    /// </summary>
    public sealed class RoutineStatusResponse
    {
        public bool IsComplete { get; init; }
        public bool IsSuccess { get; init; }
        public bool IsPending { get; init; }
        public bool IsResponsePending { get; init; }
        public byte StatusCode { get; init; }
        public string? ErrorMessage { get; init; }

        public static RoutineStatusResponse Pending() => new() { IsPending = true };
        public static RoutineStatusResponse ResponsePending() => new() { IsResponsePending = true };
        public static RoutineStatusResponse Complete(byte status, bool success) => 
            new() { IsComplete = true, IsSuccess = success, StatusCode = status };
        public static RoutineStatusResponse Error(byte status, string message) => 
            new() { IsComplete = true, IsSuccess = false, StatusCode = status, ErrorMessage = message };
    }

    /// <summary>
    /// Result of routine completion polling
    /// </summary>
    public sealed class RoutineCompletionResult
    {
        public bool IsSuccess { get; init; }
        public bool IsTimeout { get; init; }
        public byte StatusCode { get; init; }
        public string? ErrorMessage { get; init; }
        public int AttemptsUsed { get; init; }

        public static RoutineCompletionResult Success(byte status, int attempts) => 
            new() { IsSuccess = true, StatusCode = status, AttemptsUsed = attempts };
        
        public static RoutineCompletionResult Failed(byte status, string? message) => 
            new() { IsSuccess = false, StatusCode = status, ErrorMessage = message ?? $"Routine failed with status 0x{status:X2}" };
        
        public static RoutineCompletionResult Timeout(int attempts) => 
            new() { IsSuccess = false, IsTimeout = true, ErrorMessage = $"Routine did not complete within {attempts} attempts", AttemptsUsed = attempts };
    }
}
