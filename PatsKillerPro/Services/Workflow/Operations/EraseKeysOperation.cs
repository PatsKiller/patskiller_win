using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow.Operations
{
    /// <summary>
    /// Erase Keys Operation per EZimmo Workflow Audit.
    /// Erases all programmed keys from the PATS system.
    /// WARNING: Vehicle will NOT start until at least 2 keys are programmed!
    /// IMPORTANT: Reuses existing security sessions - no token if session active.
    /// </summary>
    public sealed class EraseKeysOperation : OperationBase
    {
        private readonly UdsCommunication _uds;
        private readonly KeepAliveTimer _keepAlive;
        private readonly VehicleSession _session;
        private readonly string _incode;
        private readonly Action<string>? _log;
        private readonly bool _keepSessionOpen;

        private int _initialKeyCount;
        private int _finalKeyCount;
        private bool _wasAlreadyUnlocked;

        public override string Name => "Erase All Keys";
        public override string Description => "Erases all programmed keys from the PATS system";
        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(15);
        
        /// <summary>Token cost: 0 if reusing existing session, 1 if new unlock required</summary>
        public override int TokenCost => _wasAlreadyUnlocked ? 0 : 1;
        public override bool RequiresIncode => true;

        /// <summary>Initial key count before erase</summary>
        public int InitialKeyCount => _initialKeyCount;

        /// <summary>Final key count after erase (should be 0)</summary>
        public int FinalKeyCount => _finalKeyCount;
        
        /// <summary>Whether this operation reused an existing security session</summary>
        public bool ReusedExistingSession => _wasAlreadyUnlocked;

        /// <summary>
        /// Creates an erase keys operation.
        /// </summary>
        /// <param name="keepSessionOpen">If true, keep session open for subsequent key programming. Default true.</param>
        public EraseKeysOperation(
            UdsCommunication uds,
            KeepAliveTimer keepAlive,
            VehicleSession session,
            string incode,
            PlatformPacingConfig? pacing = null,
            PlatformRoutingConfig? routing = null,
            Action<string>? log = null,
            bool keepSessionOpen = true)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
            _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _incode = incode ?? throw new ArgumentNullException(nameof(incode));
            _log = log;
            _keepSessionOpen = keepSessionOpen;

            PacingConfig = pacing ?? PlatformPacingConfig.Default;
            RoutingConfig = routing ?? PlatformRoutingConfig.Default;
        }

        public override IReadOnlyList<OperationStep> Steps => BuildSteps();

        private List<OperationStep> BuildSteps()
        {
            var pacing = PacingConfig!;
            var routing = RoutingConfig!;
            var steps = new List<OperationStep>();

            // Step 1: Check Preconditions & Session State
            steps.Add(CreateStep(
                "Check Preconditions",
                CheckPreconditionsAsync,
                "Checking key count and session state",
                retryPolicy: RetryPolicy.Standard
            ));

            // Step 2: Unlock Gateway (if required AND not already unlocked)
            if (routing.RequiresGatewayUnlock && routing.GatewayModule != 0)
            {
                steps.Add(CreateStep(
                    "Unlock Gateway",
                    UnlockGatewayAsync,
                    "Unlocking security gateway",
                    postDelay: pacing.PostSecurityUnlockDelay,
                    retryPolicy: RetryPolicy.Security,
                    nrcContext: NrcContext.SecurityAccess
                ));
            }

            // Step 3: Start/Refresh Diagnostic Session
            steps.Add(CreateStep(
                "Start Diagnostic Session",
                StartDiagnosticSessionAsync,
                "Entering extended diagnostic mode",
                postDelay: pacing.PostSessionStartDelay,
                retryPolicy: RetryPolicy.Standard
            ));

            // Step 4: Ensure Keep-Alive Running
            steps.Add(CreateStep(
                "Ensure Keep-Alive",
                EnsureKeepAliveAsync,
                "Ensuring session maintenance is active",
                retryPolicy: RetryPolicy.NoRetry
            ));

            // Step 5: Security Access (SKIPPED if session active!)
            steps.Add(CreateStep(
                "Security Access",
                SecurityAccessAsync,
                "Obtaining security access",
                postDelay: pacing.PostSecurityUnlockDelay,
                retryPolicy: RetryPolicy.Security,
                nrcContext: NrcContext.SecurityAccess
            ));

            // Step 6: Start Erase Routine
            steps.Add(CreateStep(
                "Start Erase Routine",
                StartEraseRoutineAsync,
                "Starting key erase routine",
                postDelay: TimeSpan.FromMilliseconds(300),
                retryPolicy: RetryPolicy.Standard,
                nrcContext: NrcContext.RoutineControl
            ));

            // Step 7: Poll for Completion
            steps.Add(CreateStep(
                "Poll Completion",
                PollRoutineCompletionAsync,
                "Waiting for erase to complete",
                retryPolicy: RetryPolicy.NoRetry
            ));

            // Step 8: Verify Key Count is Zero
            steps.Add(CreateStep(
                "Verify Key Count",
                VerifyKeyCountAsync,
                "Verifying all keys erased",
                retryPolicy: RetryPolicy.Standard
            ));

            // Step 9: Finalize (keep session open for key programming!)
            steps.Add(CreateStep(
                "Finalize",
                FinalizeAsync,
                _keepSessionOpen ? "Ready for key programming" : "Closing session",
                retryPolicy: RetryPolicy.NoRetry,
                critical: false
            ));

            return steps;
        }

        #region Step Implementations

        private async Task CheckPreconditionsAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Check if BCM specifically is unlocked (required for key functions)
            // Note: Gateway unlock alone is NOT enough - BCM must be unlocked
            _wasAlreadyUnlocked = _session.IsSecurityUnlocked(routing.PrimaryModule);
            
            if (_wasAlreadyUnlocked)
            {
                var remaining = _session.GetTimeRemaining(routing.PrimaryModule);
                _log?.Invoke($"✓ Reusing active BCM session (time remaining: {remaining?.TotalSeconds:F0}s)");
            }
            else
            {
                _log?.Invoke("BCM unlock required (will consume 1 token)");
            }

            // Read current key count
            var response = await _uds.ReadDataByIdentifierAsync(
                routing.PrimaryModule, routing.KeyCountDid, ct);

            if (response.Success)
            {
                _initialKeyCount = UdsCommunication.ExtractKeyCount(response.Data);
            }
            else
            {
                _initialKeyCount = 0;
            }

            _log?.Invoke($"Current key count: {_initialKeyCount}");

            if (_initialKeyCount == 0)
            {
                throw new StepException("No keys to erase", ErrorCategory.FailFast);
            }
        }

        private async Task UnlockGatewayAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Skip if gateway already unlocked
            if (_session.IsSecurityUnlocked(routing.GatewayModule))
            {
                _log?.Invoke("✓ Gateway already unlocked - skipping");
                return;
            }

            var sessResp = await _uds.DiagnosticSessionControlAsync(
                routing.GatewayModule, DiagnosticSessionType.Extended, ct);
            sessResp.ThrowIfFailed(NrcContext.DiagnosticSession);

            await Task.Delay(50, ct);

            var seedResp = await _uds.SecurityAccessRequestSeedAsync(routing.GatewayModule, 0x01, ct);
            seedResp.ThrowIfFailed(NrcContext.SecurityAccess);

            var gwIncode = UdsCommunication.HexToBytes(_incode.Length >= 4 ? _incode.Substring(0, 4) : _incode);
            if (gwIncode == null)
            {
                throw new StepException("Invalid gateway incode", ErrorCategory.FailFast);
            }

            var keyResp = await _uds.SecurityAccessSendKeyAsync(routing.GatewayModule, gwIncode, 0x02, ct);
            keyResp.ThrowIfFailed(NrcContext.SecurityAccess);

            _session.RecordSecurityUnlock(routing.GatewayModule);
            _log?.Invoke("Gateway unlocked ✓");
        }

        private async Task StartDiagnosticSessionAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var response = await _uds.DiagnosticSessionControlAsync(
                routing.PrimaryModule, DiagnosticSessionType.Extended, ct);
            response.ThrowIfFailed(NrcContext.DiagnosticSession);

            _session.RecordDiagnosticSession(routing.PrimaryModule, DiagnosticSessionType.Extended);

            if (routing.HasKeyless && routing.SecondaryModule != 0)
            {
                var rfaResp = await _uds.DiagnosticSessionControlAsync(
                    routing.SecondaryModule, DiagnosticSessionType.Extended, ct);
                if (rfaResp.Success)
                {
                    _session.RecordDiagnosticSession(routing.SecondaryModule, DiagnosticSessionType.Extended);
                }
            }

            _log?.Invoke("Diagnostic session started");
        }

        private Task EnsureKeepAliveAsync(CancellationToken ct)
        {
            if (!_keepAlive.IsRunning)
            {
                _keepAlive.ConfigureFromPlatform(PacingConfig!, RoutingConfig!);
                _keepAlive.Start();
                _log?.Invoke("Keep-alive started");
            }
            else
            {
                _log?.Invoke("Keep-alive already running (continuing session)");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Security access step that checks for existing BCM session.
        /// SKIPS if BCM already unlocked!
        /// </summary>
        private async Task SecurityAccessAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Check if BCM specifically is already unlocked
            if (_session.IsSecurityUnlocked(routing.PrimaryModule))
            {
                var remaining = _session.GetTimeRemaining(routing.PrimaryModule);
                _log?.Invoke($"✓ BCM already unlocked - SKIPPING incode submission (time left: {remaining?.TotalSeconds:F0}s)");
                return;
            }

            // BCM not unlocked - need to unlock it (consumes token)
            _log?.Invoke("Unlocking BCM security (consuming 1 token)...");

            // Request seed
            var seedResp = await _uds.SecurityAccessRequestSeedAsync(routing.PrimaryModule, 0x01, ct);
            seedResp.ThrowIfFailed(NrcContext.SecurityAccess);

            // Submit key
            var incodeBytes = UdsCommunication.HexToBytes(
                routing.HasKeyless && _incode.Length >= 8 
                    ? routing.SplitKeylessIncode(_incode).BcmIncode 
                    : _incode);

            if (incodeBytes == null)
            {
                throw new StepException("Invalid incode format", ErrorCategory.FailFast);
            }

            var keyResp = await _uds.SecurityAccessSendKeyAsync(routing.PrimaryModule, incodeBytes, 0x02, ct);
            keyResp.ThrowIfFailed(NrcContext.SecurityAccess);

            _session.RecordSecurityUnlock(routing.PrimaryModule);
            _log?.Invoke("Security unlocked ✓");
        }

        private async Task StartEraseRoutineAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var response = await _uds.RoutineControlStartAsync(
                routing.PrimaryModule, routing.EraseKeysRoutineId, null, ct);
            response.ThrowIfFailed(NrcContext.RoutineControl);

            _log?.Invoke("Erase routine started");

            // Also erase on RFA if keyless
            if (routing.HasKeyless && routing.SecondaryModule != 0)
            {
                var rfaResp = await _uds.RoutineControlStartAsync(
                    routing.SecondaryModule, routing.EraseKeysRoutineId, null, ct);
                if (rfaResp.Success)
                {
                    _log?.Invoke("RFA erase routine started");
                }
            }
        }

        private async Task PollRoutineCompletionAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;
            var pacing = PacingConfig!;

            var maxAttempts = 20; // 10 seconds at 500ms intervals
            
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var response = await _uds.RoutineControlResultsAsync(
                    routing.PrimaryModule, routing.EraseKeysRoutineId, ct);

                if (response.Success && response.Data != null)
                {
                    var status = UdsCommunication.ParseRoutineStatus(response.Data);

                    if (status.IsComplete)
                    {
                        if (status.IsSuccess)
                        {
                            _log?.Invoke("Erase routine completed");
                            return;
                        }
                        throw new StepException($"Erase failed: 0x{status.StatusCode:X2}", ErrorCategory.FailFast);
                    }
                }

                await Task.Delay(pacing.VerifyPollInterval, ct);
            }

            throw new StepException("Erase routine timed out", ErrorCategory.FailFast);
        }

        private async Task VerifyKeyCountAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var response = await _uds.ReadDataByIdentifierAsync(
                routing.PrimaryModule, routing.KeyCountDid, ct);

            if (response.Success)
            {
                _finalKeyCount = UdsCommunication.ExtractKeyCount(response.Data);
            }
            else
            {
                _finalKeyCount = -1;
            }

            _log?.Invoke($"Key count after erase: {_finalKeyCount}");

            if (_finalKeyCount != 0)
            {
                throw new StepException(
                    $"Erase verification failed: {_finalKeyCount} keys remaining",
                    ErrorCategory.FailFast);
            }

            _log?.Invoke("All keys erased successfully ✓");
        }

        /// <summary>
        /// Finalize: Keep session open for subsequent key programming.
        /// </summary>
        private async Task FinalizeAsync(CancellationToken ct)
        {
            if (_keepSessionOpen)
            {
                var remaining = _session.SecurityTimeRemaining;
                _log?.Invoke($"✓ Session kept open for key programming (time remaining: {remaining?.TotalSeconds:F0}s)");
                _log?.Invoke("→ Program new keys now - NO additional token needed!");
            }
            else
            {
                try
                {
                    await _keepAlive.StopAsync();
                    _log?.Invoke("Session closed");
                }
                catch { }
            }
        }

        #endregion
    }
}
