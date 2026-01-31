using System;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// UDS Negative Response Code (NRC) classifier.
    /// Determines error category based on protocol-level NRC codes per EZimmo spec.
    /// </summary>
    public static class NrcClassifier
    {
        #region NRC Constants

        // Category 1: FAIL-FAST - Stop immediately, cannot recover
        public const byte NRC_SERVICE_NOT_SUPPORTED = 0x11;
        public const byte NRC_SUBFUNCTION_NOT_SUPPORTED = 0x12;
        public const byte NRC_INCORRECT_MESSAGE_LENGTH = 0x13;
        public const byte NRC_CONDITIONS_NOT_CORRECT = 0x22;
        public const byte NRC_REQUEST_SEQUENCE_ERROR = 0x24;
        public const byte NRC_FAILURE_PREVENTS_EXECUTION = 0x26;
        public const byte NRC_REQUEST_OUT_OF_RANGE = 0x31;
        public const byte NRC_SECURITY_ACCESS_DENIED = 0x33;
        public const byte NRC_INVALID_KEY = 0x35;
        public const byte NRC_EXCEEDED_NUMBER_OF_ATTEMPTS = 0x36;
        public const byte NRC_UPLOAD_DOWNLOAD_NOT_ACCEPTED = 0x70;
        public const byte NRC_GENERAL_PROGRAMMING_FAILURE = 0x72;
        public const byte NRC_WRONG_BLOCK_SEQUENCE_COUNTER = 0x73;
        public const byte NRC_SUBFUNCTION_NOT_SUPPORTED_IN_ACTIVE_SESSION = 0x7E;
        public const byte NRC_SERVICE_NOT_SUPPORTED_IN_ACTIVE_SESSION = 0x7F;

        // Category 2: RETRYABLE - Transient errors, retry with backoff
        public const byte NRC_GENERAL_REJECT = 0x10;
        public const byte NRC_RESPONSE_TOO_LONG = 0x14;
        public const byte NRC_BUSY_REPEAT_REQUEST = 0x21;
        public const byte NRC_NO_RESPONSE_FROM_SUBNET = 0x25;
        public const byte NRC_TRANSFER_DATA_SUSPENDED = 0x71;

        // Category 3: USER-ACTION or WAIT
        public const byte NRC_REQUIRED_TIME_DELAY_NOT_EXPIRED = 0x37;

        // Special: Not an error, means "still processing"
        public const byte NRC_RESPONSE_PENDING = 0x78;

        #endregion

        /// <summary>
        /// Classifies an NRC code into an ErrorCategory.
        /// </summary>
        /// <param name="nrc">The UDS Negative Response Code</param>
        /// <param name="context">Optional context for NRC 0x22 (conditions not correct)</param>
        /// <returns>The error category for workflow handling</returns>
        public static ErrorCategory Classify(byte nrc, NrcContext context = NrcContext.Default)
        {
            return nrc switch
            {
                // FAIL-FAST: Cannot recover, stop immediately
                NRC_SERVICE_NOT_SUPPORTED => ErrorCategory.FailFast,
                NRC_SUBFUNCTION_NOT_SUPPORTED => ErrorCategory.FailFast,
                NRC_INCORRECT_MESSAGE_LENGTH => ErrorCategory.FailFast,
                NRC_REQUEST_SEQUENCE_ERROR => ErrorCategory.FailFast,
                NRC_FAILURE_PREVENTS_EXECUTION => ErrorCategory.FailFast,
                NRC_REQUEST_OUT_OF_RANGE => ErrorCategory.FailFast,
                NRC_SECURITY_ACCESS_DENIED => ErrorCategory.FailFast,
                NRC_INVALID_KEY => ErrorCategory.FailFast,
                NRC_EXCEEDED_NUMBER_OF_ATTEMPTS => ErrorCategory.FailFast,
                NRC_UPLOAD_DOWNLOAD_NOT_ACCEPTED => ErrorCategory.FailFast,
                NRC_GENERAL_PROGRAMMING_FAILURE => ErrorCategory.FailFast,
                NRC_WRONG_BLOCK_SEQUENCE_COUNTER => ErrorCategory.FailFast,
                NRC_SUBFUNCTION_NOT_SUPPORTED_IN_ACTIVE_SESSION => ErrorCategory.FailFast,
                NRC_SERVICE_NOT_SUPPORTED_IN_ACTIVE_SESSION => ErrorCategory.FailFast,

                // NRC 0x22: Context-dependent (usually user-action, sometimes fail-fast)
                NRC_CONDITIONS_NOT_CORRECT => context switch
                {
                    NrcContext.SecurityAccess => ErrorCategory.UserActionRequired,  // Check ignition
                    NrcContext.RoutineControl => ErrorCategory.UserActionRequired,  // Key not detected
                    _ => ErrorCategory.FailFast
                },

                // RETRYABLE: Transient, retry with backoff
                NRC_GENERAL_REJECT => ErrorCategory.Retryable,
                NRC_RESPONSE_TOO_LONG => ErrorCategory.Retryable,
                NRC_BUSY_REPEAT_REQUEST => ErrorCategory.Retryable,
                NRC_NO_RESPONSE_FROM_SUBNET => ErrorCategory.Retryable,
                NRC_TRANSFER_DATA_SUSPENDED => ErrorCategory.Retryable,

                // USER-ACTION: Wait for delay to expire
                NRC_REQUIRED_TIME_DELAY_NOT_EXPIRED => ErrorCategory.UserActionRequired,

                // Response pending is NOT an error - special handling
                NRC_RESPONSE_PENDING => ErrorCategory.Retryable,

                // Unknown NRC: Default to fail-fast for safety
                _ => ErrorCategory.FailFast
            };
        }

        /// <summary>
        /// Gets a human-readable description for an NRC code.
        /// </summary>
        public static string GetDescription(byte nrc)
        {
            return nrc switch
            {
                NRC_GENERAL_REJECT => "General reject",
                NRC_SERVICE_NOT_SUPPORTED => "Service not supported",
                NRC_SUBFUNCTION_NOT_SUPPORTED => "Sub-function not supported",
                NRC_INCORRECT_MESSAGE_LENGTH => "Incorrect message length or format",
                NRC_RESPONSE_TOO_LONG => "Response too long",
                NRC_BUSY_REPEAT_REQUEST => "Busy, repeat request",
                NRC_CONDITIONS_NOT_CORRECT => "Conditions not correct",
                NRC_REQUEST_SEQUENCE_ERROR => "Request sequence error",
                NRC_NO_RESPONSE_FROM_SUBNET => "No response from sub-net component",
                NRC_FAILURE_PREVENTS_EXECUTION => "Failure prevents execution of requested action",
                NRC_REQUEST_OUT_OF_RANGE => "Request out of range",
                NRC_SECURITY_ACCESS_DENIED => "Security access denied",
                NRC_INVALID_KEY => "Invalid key (wrong incode)",
                NRC_EXCEEDED_NUMBER_OF_ATTEMPTS => "Exceeded number of attempts (security locked)",
                NRC_REQUIRED_TIME_DELAY_NOT_EXPIRED => "Required time delay not expired (wait 10 min)",
                NRC_UPLOAD_DOWNLOAD_NOT_ACCEPTED => "Upload/download not accepted",
                NRC_TRANSFER_DATA_SUSPENDED => "Transfer data suspended",
                NRC_GENERAL_PROGRAMMING_FAILURE => "General programming failure",
                NRC_WRONG_BLOCK_SEQUENCE_COUNTER => "Wrong block sequence counter",
                NRC_RESPONSE_PENDING => "Response pending (operation in progress)",
                NRC_SUBFUNCTION_NOT_SUPPORTED_IN_ACTIVE_SESSION => "Sub-function not supported in active session",
                NRC_SERVICE_NOT_SUPPORTED_IN_ACTIVE_SESSION => "Service not supported in active session",
                _ => $"Unknown NRC (0x{nrc:X2})"
            };
        }

        /// <summary>
        /// Gets the recommended user action message for an NRC code.
        /// </summary>
        public static string GetUserActionMessage(byte nrc, NrcContext context = NrcContext.Default)
        {
            return nrc switch
            {
                NRC_INVALID_KEY => "The incode was rejected. Please verify the incode is correct for this vehicle.",
                NRC_SECURITY_ACCESS_DENIED => "Security access denied. Verify the incode and try again.",
                NRC_EXCEEDED_NUMBER_OF_ATTEMPTS => "Security is temporarily locked due to too many failed attempts. Wait 10 minutes and try again.",
                NRC_REQUIRED_TIME_DELAY_NOT_EXPIRED => "Security lockout timer has not expired. Wait 10 minutes and try again.",
                NRC_CONDITIONS_NOT_CORRECT when context == NrcContext.SecurityAccess => "Conditions not correct. Ensure ignition is ON and doors are closed.",
                NRC_CONDITIONS_NOT_CORRECT when context == NrcContext.RoutineControl => "Key not detected. Insert key and turn to ON position.",
                NRC_SERVICE_NOT_SUPPORTED => "This operation is not supported by this vehicle/module.",
                NRC_BUSY_REPEAT_REQUEST => "Module is busy. The operation will retry automatically.",
                NRC_GENERAL_PROGRAMMING_FAILURE => "Programming failed. Try cycling the ignition and retry.",
                _ => $"Operation failed: {GetDescription(nrc)}"
            };
        }

        /// <summary>
        /// Checks if this NRC means the operation is still in progress (response pending).
        /// </summary>
        public static bool IsResponsePending(byte nrc) => nrc == NRC_RESPONSE_PENDING;

        /// <summary>
        /// Checks if this NRC indicates a security lockout that requires waiting.
        /// </summary>
        public static bool IsSecurityLockout(byte nrc) =>
            nrc == NRC_EXCEEDED_NUMBER_OF_ATTEMPTS || nrc == NRC_REQUIRED_TIME_DELAY_NOT_EXPIRED;

        /// <summary>
        /// Gets the recommended retry delay for retryable NRCs.
        /// </summary>
        public static TimeSpan GetRecommendedRetryDelay(byte nrc, int attemptNumber = 1)
        {
            var baseDelay = nrc switch
            {
                NRC_BUSY_REPEAT_REQUEST => TimeSpan.FromMilliseconds(500),
                NRC_GENERAL_REJECT => TimeSpan.FromMilliseconds(300),
                NRC_RESPONSE_PENDING => TimeSpan.FromMilliseconds(200),
                NRC_NO_RESPONSE_FROM_SUBNET => TimeSpan.FromMilliseconds(400),
                _ => TimeSpan.FromMilliseconds(200)
            };

            // Exponential backoff: base * 2^(attempt-1), capped at 2 seconds
            var multiplier = Math.Pow(2, attemptNumber - 1);
            var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * multiplier);
            return delay > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : delay;
        }
    }

    /// <summary>
    /// Context for NRC classification (affects how NRC 0x22 is classified)
    /// </summary>
    public enum NrcContext
    {
        Default,
        SecurityAccess,
        RoutineControl,
        DiagnosticSession,
        DataTransfer
    }

    /// <summary>
    /// Result of an NRC classification for workflow handling.
    /// </summary>
    public sealed class NrcClassification
    {
        public byte Nrc { get; init; }
        public ErrorCategory Category { get; init; }
        public string Description { get; init; } = "";
        public string UserMessage { get; init; } = "";
        public TimeSpan? RetryDelay { get; init; }
        public bool IsResponsePending { get; init; }
        public bool IsSecurityLockout { get; init; }

        public static NrcClassification FromNrc(byte nrc, NrcContext context = NrcContext.Default, int attemptNumber = 1)
        {
            var category = NrcClassifier.Classify(nrc, context);
            return new NrcClassification
            {
                Nrc = nrc,
                Category = category,
                Description = NrcClassifier.GetDescription(nrc),
                UserMessage = NrcClassifier.GetUserActionMessage(nrc, context),
                RetryDelay = category == ErrorCategory.Retryable
                    ? NrcClassifier.GetRecommendedRetryDelay(nrc, attemptNumber)
                    : null,
                IsResponsePending = NrcClassifier.IsResponsePending(nrc),
                IsSecurityLockout = NrcClassifier.IsSecurityLockout(nrc)
            };
        }
    }
}
