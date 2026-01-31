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
    /// </summary>
    public sealed class EraseKeysOperation : OperationBase
    {
        private readonly UdsCommunication _uds;
        private readonly KeepAliveTimer _keepAlive;
        private readonly VehicleSession _session;
        private readonly string _incode;
        private readonly Action<string>? _log;

        private int _initialKeyCount;
        private int _finalKeyCount;

        public override string Name => "Erase All Keys";
        public override string Description => "Erases all programmed keys from the PATS system";
        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(15);
        public override int TokenCost => 1;
        public override bool RequiresIncode => true;

        /// <summary>Initial key count before erase</summary>
        public int InitialKeyCount => _initialKeyCount;

        /// <summary>Final key count after erase (should be 0)</summary>
        public int FinalKeyCount => _finalKeyCount;

        public EraseKeysOperation(
            UdsCommunication uds,
            KeepAliveTimer keepAlive,
            VehicleSession session,
            string incode,
            PlatformPacingConfig? pacing = null,
            PlatformRoutingConfig? routing = null,
            Action<string>? log = null)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
            _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _incode = incode ?? throw new ArgumentNullException(nameof(incode));
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

            // Step 1: Check Preconditions & Read Key Count
            steps.Add(CreateStep(
                "Check Preconditions",
                CheckPreconditionsAsync,
                "Reading current key count",
                retryPolicy: RetryPolicy.Standard
            ));

            // Step 2: Unlock Gateway (if required)
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

            // Step 3: Start Diagnostic Session
            steps.Add(CreateStep(
                "Start Diagnostic Session",
                StartDiagnosticSessionAsync,
                "Entering extended diagnostic mode",
                postDelay: pacing.PostSessionStartDelay,
                retryPolicy: RetryPolicy.Standard
            ));

            // Step 4: Start Keep-Alive
            steps.Add(CreateStep(
                "Start Keep-Alive",
                StartKeepAliveAsync,
                "Starting session maintenance",
                retryPolicy: RetryPolicy.NoRetry
            ));

            // Step 5: Unlock Security
            steps.Add(CreateStep(
                "Unlock Security",
                UnlockSecurityAsync,
                "Submitting incode for security access",
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

            // Step 9: Cleanup
            steps.Add(CreateStep(
                "Cleanup",
                CleanupAsync,
                "Cleaning up session",
                retryPolicy: RetryPolicy.NoRetry,
                critical: false
            ));

            return steps;
        }

        #region Step Implementations

        private async Task CheckPreconditionsAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

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

            _log?.Invoke("Gateway unlocked");
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

        private Task StartKeepAliveAsync(CancellationToken ct)
        {
            _keepAlive.ConfigureFromPlatform(PacingConfig!, RoutingConfig!);
            _keepAlive.Start();
            _log?.Invoke("Keep-alive started");
            return Task.CompletedTask;
        }

        private async Task UnlockSecurityAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

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
            _log?.Invoke("Security unlocked");
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

            _log?.Invoke("All keys erased successfully");
        }

        private async Task CleanupAsync(CancellationToken ct)
        {
            try
            {
                await _keepAlive.StopAsync();
            }
            catch { }

            _log?.Invoke("Cleanup complete");
        }

        #endregion
    }
}
