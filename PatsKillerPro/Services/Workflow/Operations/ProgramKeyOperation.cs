using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow.Operations
{
    /// <summary>
    /// Key Programming Operation per EZimmo Workflow Audit.
    /// Implements the complete 9-step workflow with proper timing, verification, and error handling.
    /// </summary>
    public sealed class ProgramKeyOperation : OperationBase
    {
        private readonly UdsCommunication _uds;
        private readonly KeepAliveTimer _keepAlive;
        private readonly VehicleSession _session;
        private readonly string _incode;
        private readonly int _targetSlot;
        private readonly Action<string>? _log;

        private int _initialKeyCount;
        private int _finalKeyCount;
        private byte[]? _seed;

        public override string Name => "Program Key";
        public override string Description => $"Programs a new key to slot {_targetSlot}";
        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(20);
        public override int TokenCost => 1;
        public override bool RequiresIncode => true;

        /// <summary>Final key count after operation</summary>
        public int FinalKeyCount => _finalKeyCount;

        public ProgramKeyOperation(
            UdsCommunication uds,
            KeepAliveTimer keepAlive,
            VehicleSession session,
            string incode,
            int targetSlot,
            PlatformPacingConfig? pacing = null,
            PlatformRoutingConfig? routing = null,
            Action<string>? log = null)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
            _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _incode = incode ?? throw new ArgumentNullException(nameof(incode));
            _targetSlot = targetSlot;
            _log = log;

            PacingConfig = pacing ?? PlatformPacingConfig.Default;
            RoutingConfig = routing ?? PlatformRoutingConfig.Default;
        }

        public override IReadOnlyList<OperationStep> Steps => BuildSteps();

        private List<OperationStep> BuildSteps()
        {
            var pacing = PacingConfig!;
            var routing = RoutingConfig!;
            var steps = new List<OperationStep>();

            // Step 1: Check Preconditions
            steps.Add(CreateStep(
                "Check Preconditions",
                CheckPreconditionsAsync,
                "Verifying vehicle communication and key count",
                retryPolicy: RetryPolicy.Standard,
                nrcContext: NrcContext.Default
            ));

            // Step 2: Unlock Gateway (if required)
            if (routing.RequiresGatewayUnlock && routing.GatewayModule != 0)
            {
                steps.Add(CreateStep(
                    "Unlock Gateway",
                    UnlockGatewayAsync,
                    "Unlocking security gateway (2020+ vehicles)",
                    postDelay: pacing.PostSecurityUnlockDelay,
                    retryPolicy: RetryPolicy.Security,
                    nrcContext: NrcContext.SecurityAccess
                ));
            }

            // Step 3: Start Diagnostic Session
            steps.Add(CreateStep(
                "Start Diagnostic Session",
                StartDiagnosticSessionAsync,
                "Entering extended diagnostic mode",
                postDelay: pacing.PostSessionStartDelay,
                retryPolicy: RetryPolicy.Standard,
                nrcContext: NrcContext.DiagnosticSession
            ));

            // Step 4: Start Keep-Alive Timer
            steps.Add(CreateStep(
                "Start Keep-Alive",
                StartKeepAliveAsync,
                "Starting session maintenance timer",
                retryPolicy: RetryPolicy.NoRetry
            ));

            // Step 5: Request Security Seed
            steps.Add(CreateStep(
                "Request Security Seed",
                RequestSecuritySeedAsync,
                "Requesting security access seed",
                retryPolicy: RetryPolicy.Security,
                nrcContext: NrcContext.SecurityAccess
            ));

            // Step 6: Submit Security Key (Incode) - CRITICAL timing!
            steps.Add(CreateStep(
                "Submit Incode",
                SubmitSecurityKeyAsync,
                "Submitting incode for security access",
                postDelay: pacing.PostSecurityUnlockDelay, // CRITICAL delay after unlock
                retryPolicy: RetryPolicy.Security,
                nrcContext: NrcContext.SecurityAccess
            ));

            // Step 7: Start Key Programming Routine
            steps.Add(CreateStep(
                "Start Programming Routine",
                StartProgrammingRoutineAsync,
                "Starting WKIP (Write Key In Progress) routine",
                postDelay: pacing.PostRoutineStartDelay,
                retryPolicy: RetryPolicy.Standard,
                nrcContext: NrcContext.RoutineControl
            ));

            // Step 8: Poll for Completion
            steps.Add(CreateStep(
                "Poll Routine Completion",
                PollRoutineCompletionAsync,
                "Waiting for key programming to complete",
                retryPolicy: RetryPolicy.NoRetry // Polling handles its own retries
            ));

            // Step 9: Verify Key Count
            steps.Add(CreateStep(
                "Verify Key Count",
                VerifyKeyCountAsync,
                "Verifying key was programmed successfully",
                retryPolicy: RetryPolicy.Standard
            ));

            // Step 10: Cleanup
            steps.Add(CreateStep(
                "Cleanup",
                CleanupAsync,
                "Stopping keep-alive and cleaning up",
                retryPolicy: RetryPolicy.NoRetry,
                critical: false
            ));

            return steps;
        }

        #region Step Implementations

        private async Task CheckPreconditionsAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Verify incode format
            var expectedLength = routing.IncodeLength;
            var cleanIncode = _incode.Replace("-", "").Replace(" ", "").Trim();
            if (cleanIncode.Length != expectedLength)
            {
                throw new StepException(
                    $"Invalid incode length: expected {expectedLength} characters, got {cleanIncode.Length}",
                    ErrorCategory.FailFast);
            }

            // Read current key count
            var response = await _uds.ReadDataByIdentifierAsync(
                routing.PrimaryModule, routing.KeyCountDid, ct);

            if (!response.Success)
            {
                response.ThrowIfFailed();
            }

            _initialKeyCount = UdsCommunication.ExtractKeyCount(response.Data);
            _log?.Invoke($"Current key count: {_initialKeyCount}");

            // Check if we can add more keys
            if (_initialKeyCount >= 8)
            {
                throw new StepException(
                    "Maximum keys already programmed (8). Erase keys first.",
                    ErrorCategory.FailFast);
            }

            _log?.Invoke("Preconditions OK");
        }

        private async Task UnlockGatewayAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Start extended session on gateway
            var sessResp = await _uds.DiagnosticSessionControlAsync(
                routing.GatewayModule, DiagnosticSessionType.Extended, ct);
            sessResp.ThrowIfFailed(NrcContext.DiagnosticSession);

            await Task.Delay(50, ct);

            // Request seed
            var seedResp = await _uds.SecurityAccessRequestSeedAsync(routing.GatewayModule, 0x01, ct);
            seedResp.ThrowIfFailed(NrcContext.SecurityAccess);

            // Use first 4 chars of incode for gateway
            var gwIncode = UdsCommunication.HexToBytes(_incode.Substring(0, 4));
            if (gwIncode == null)
            {
                throw new StepException("Invalid gateway incode format", ErrorCategory.FailFast);
            }

            // Submit key
            var keyResp = await _uds.SecurityAccessSendKeyAsync(routing.GatewayModule, gwIncode, 0x02, ct);
            keyResp.ThrowIfFailed(NrcContext.SecurityAccess);

            _log?.Invoke("Gateway unlocked");
        }

        private async Task StartDiagnosticSessionAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Start extended session on primary module
            var response = await _uds.DiagnosticSessionControlAsync(
                routing.PrimaryModule, DiagnosticSessionType.Extended, ct);
            response.ThrowIfFailed(NrcContext.DiagnosticSession);

            _session.RecordDiagnosticSession(routing.PrimaryModule, DiagnosticSessionType.Extended);

            // For keyless, also start session on secondary module
            if (routing.HasKeyless && routing.SecondaryModule != 0)
            {
                var rfaResp = await _uds.DiagnosticSessionControlAsync(
                    routing.SecondaryModule, DiagnosticSessionType.Extended, ct);
                rfaResp.ThrowIfFailed(NrcContext.DiagnosticSession);

                _session.RecordDiagnosticSession(routing.SecondaryModule, DiagnosticSessionType.Extended);
            }

            _log?.Invoke("Diagnostic session started");
        }

        private Task StartKeepAliveAsync(CancellationToken ct)
        {
            var pacing = PacingConfig!;
            var routing = RoutingConfig!;

            _keepAlive.ConfigureFromPlatform(pacing, routing);
            _keepAlive.Start();

            _log?.Invoke($"Keep-alive started: {pacing.EffectiveKeepAliveInterval.TotalMilliseconds}ms interval");
            return Task.CompletedTask;
        }

        private async Task RequestSecuritySeedAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var response = await _uds.SecurityAccessRequestSeedAsync(routing.PrimaryModule, 0x01, ct);
            response.ThrowIfFailed(NrcContext.SecurityAccess);

            _seed = UdsCommunication.ExtractSeed(response.Data);
            if (_seed == null || _seed.Length < 2)
            {
                throw new StepException("Failed to extract security seed", ErrorCategory.FailFast);
            }

            _log?.Invoke($"Security seed received: {BitConverter.ToString(_seed)}");
        }

        private async Task SubmitSecurityKeyAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Convert incode to bytes
            string bcmIncode, rfaIncode = "";
            if (routing.HasKeyless && _incode.Length >= 8)
            {
                (bcmIncode, rfaIncode) = routing.SplitKeylessIncode(_incode);
            }
            else
            {
                bcmIncode = _incode;
            }

            var incodeBytes = UdsCommunication.HexToBytes(bcmIncode);
            if (incodeBytes == null)
            {
                throw new StepException("Invalid incode format", ErrorCategory.FailFast);
            }

            // Submit to primary module
            var response = await _uds.SecurityAccessSendKeyAsync(routing.PrimaryModule, incodeBytes, 0x02, ct);
            response.ThrowIfFailed(NrcContext.SecurityAccess);

            _session.RecordSecurityUnlock(routing.PrimaryModule, PacingConfig?.SecuritySessionDuration);
            _log?.Invoke("Security unlocked on primary module");

            // For keyless, also unlock secondary module
            if (routing.HasKeyless && routing.SecondaryModule != 0 && !string.IsNullOrEmpty(rfaIncode))
            {
                var rfaIncodeBytes = UdsCommunication.HexToBytes(rfaIncode);
                if (rfaIncodeBytes != null)
                {
                    // Request seed from RFA
                    var rfaSeed = await _uds.SecurityAccessRequestSeedAsync(routing.SecondaryModule, 0x01, ct);
                    rfaSeed.ThrowIfFailed(NrcContext.SecurityAccess);

                    // Submit key to RFA
                    var rfaKey = await _uds.SecurityAccessSendKeyAsync(routing.SecondaryModule, rfaIncodeBytes, 0x02, ct);
                    rfaKey.ThrowIfFailed(NrcContext.SecurityAccess);

                    _session.RecordSecurityUnlock(routing.SecondaryModule, PacingConfig?.SecuritySessionDuration);
                    _log?.Invoke("Security unlocked on secondary module (RFA)");
                }
            }
        }

        private async Task StartProgrammingRoutineAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Start WKIP routine on primary module
            var response = await _uds.RoutineControlStartAsync(
                routing.PrimaryModule, routing.WkipRoutineId, null, ct);

            // Check for "conditions not correct" (key not detected)
            if (response.Nrc == NrcClassifier.NRC_CONDITIONS_NOT_CORRECT)
            {
                throw new StepException(
                    "Key not detected in ignition. Insert key and turn to ON position.",
                    UserActionPrompt.InsertKey);
            }

            response.ThrowIfFailed(NrcContext.RoutineControl);
            _log?.Invoke("Programming routine started");

            // For keyless, also start WKKIP on RFA
            if (routing.HasKeyless && routing.SecondaryModule != 0)
            {
                var rfaResp = await _uds.RoutineControlStartAsync(
                    routing.SecondaryModule, routing.WkkipRoutineId, null, ct);
                // RFA failure is not necessarily fatal
                if (!rfaResp.Success)
                {
                    _log?.Invoke($"RFA WKKIP start warning: {rfaResp.ErrorMessage}");
                }
            }
        }

        private async Task PollRoutineCompletionAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;
            var pacing = PacingConfig!;

            var maxAttempts = (int)(pacing.RoutineCompletionTimeout.TotalMilliseconds / pacing.VerifyPollInterval.TotalMilliseconds);
            
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var response = await _uds.RoutineControlResultsAsync(routing.PrimaryModule, routing.WkipRoutineId, ct);

                if (response.Success && response.Data != null)
                {
                    var status = UdsCommunication.ParseRoutineStatus(response.Data);

                    if (status.IsComplete)
                    {
                        if (status.IsSuccess)
                        {
                            _log?.Invoke($"Routine completed successfully (attempt {attempt + 1})");
                            return;
                        }
                        throw new StepException(
                            $"Programming routine failed: status 0x{status.StatusCode:X2}",
                            ErrorCategory.FailFast);
                    }

                    _log?.Invoke($"Routine in progress (attempt {attempt + 1}/{maxAttempts})");
                }
                else if (response.IsResponsePending)
                {
                    _log?.Invoke("Response pending - routine still executing");
                }

                await Task.Delay(pacing.VerifyPollInterval, ct);
            }

            throw new StepException(
                $"Routine did not complete within {pacing.RoutineCompletionTimeout.TotalSeconds} seconds",
                ErrorCategory.FailFast);
        }

        private async Task VerifyKeyCountAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Read key count
            var response = await _uds.ReadDataByIdentifierAsync(routing.PrimaryModule, routing.KeyCountDid, ct);
            response.ThrowIfFailed();

            _finalKeyCount = UdsCommunication.ExtractKeyCount(response.Data);
            _log?.Invoke($"Key count after programming: {_finalKeyCount}");

            // Verify count increased
            if (_finalKeyCount <= _initialKeyCount)
            {
                throw new StepException(
                    $"Key programming verification failed: count did not increase (was {_initialKeyCount}, now {_finalKeyCount})",
                    ErrorCategory.FailFast);
            }

            _log?.Invoke($"Key programmed successfully! Total keys: {_finalKeyCount}");
        }

        private async Task CleanupAsync(CancellationToken ct)
        {
            try
            {
                await _keepAlive.StopAsync();
            }
            catch
            {
                // Cleanup errors are non-fatal
            }

            _log?.Invoke("Cleanup complete");
        }

        #endregion
    }
}
