using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow.Operations
{
    /// <summary>
    /// Gateway Unlock Operation for 2020+ Ford vehicles.
    /// Unlocks BOTH gateway AND BCM, starting a session that enables all key operations for FREE.
    /// Token Cost: 1 token (covers all subsequent key operations within session)
    /// </summary>
    public sealed class GatewayUnlockOperation : OperationBase
    {
        private readonly UdsCommunication _uds;
        private readonly KeepAliveTimer _keepAlive;
        private readonly VehicleSession _session;
        private readonly string _incode;
        private readonly Action<string>? _log;

        public override string Name => "Unlock Gateway";
        public override string Description => "Unlocks gateway and BCM for all key operations";
        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(8);
        public override int TokenCost => 1;
        public override bool RequiresIncode => true;

        public GatewayUnlockOperation(
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

            if (routing.GatewayModule == 0)
            {
                // No gateway on this platform
                return new List<OperationStep>
                {
                    CreateStep(
                        "Check Gateway",
                        ct => { _log?.Invoke("No gateway required for this vehicle (pre-2020)"); return Task.CompletedTask; },
                        "No gateway required for this vehicle"
                    )
                };
            }

            var steps = new List<OperationStep>
            {
                // Step 1: Unlock Gateway Module
                CreateStep(
                    "Start Gateway Session",
                    StartGatewaySessionAsync,
                    "Starting extended session on gateway module",
                    postDelay: pacing.PostSessionStartDelay,
                    retryPolicy: RetryPolicy.Standard,
                    nrcContext: NrcContext.DiagnosticSession
                ),

                CreateStep(
                    "Request Gateway Seed",
                    RequestGatewaySeedAsync,
                    "Requesting security access seed",
                    retryPolicy: RetryPolicy.Security,
                    nrcContext: NrcContext.SecurityAccess
                ),

                CreateStep(
                    "Submit Gateway Key",
                    SubmitGatewayKeyAsync,
                    "Submitting gateway incode",
                    postDelay: pacing.PostSecurityUnlockDelay,
                    retryPolicy: RetryPolicy.Security,
                    nrcContext: NrcContext.SecurityAccess
                ),

                // Step 2: Start Diagnostic Session on BCM
                CreateStep(
                    "Start BCM Session",
                    StartBcmSessionAsync,
                    "Starting extended session on BCM",
                    postDelay: pacing.PostSessionStartDelay,
                    retryPolicy: RetryPolicy.Standard,
                    nrcContext: NrcContext.DiagnosticSession
                ),

                // Step 3: Start Keep-Alive
                CreateStep(
                    "Start Keep-Alive",
                    StartKeepAliveAsync,
                    "Starting session maintenance",
                    retryPolicy: RetryPolicy.NoRetry
                ),

                // Step 4: Unlock BCM Security
                CreateStep(
                    "Unlock BCM Security",
                    UnlockBcmSecurityAsync,
                    "Unlocking BCM for key operations",
                    postDelay: pacing.PostSecurityUnlockDelay,
                    retryPolicy: RetryPolicy.Security,
                    nrcContext: NrcContext.SecurityAccess
                ),

                // Step 5: Confirm Ready
                CreateStep(
                    "Confirm Ready",
                    ConfirmReadyAsync,
                    "Session ready for key operations",
                    retryPolicy: RetryPolicy.NoRetry
                )
            };

            return steps;
        }

        private async Task StartGatewaySessionAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var response = await _uds.DiagnosticSessionControlAsync(
                routing.GatewayModule,
                DiagnosticSessionType.Extended,
                ct);

            response.ThrowIfFailed(NrcContext.DiagnosticSession);
            _session.RecordDiagnosticSession(routing.GatewayModule, DiagnosticSessionType.Extended);

            _log?.Invoke("Gateway session started");
        }

        private async Task RequestGatewaySeedAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var response = await _uds.SecurityAccessRequestSeedAsync(
                routing.GatewayModule,
                0x01,
                ct);

            response.ThrowIfFailed(NrcContext.SecurityAccess);
            _log?.Invoke("Gateway seed received");
        }

        private async Task SubmitGatewayKeyAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Gateway uses first 4 characters of incode
            var gwIncode = _incode.Length >= 4 ? _incode.Substring(0, 4) : _incode;
            var incodeBytes = UdsCommunication.HexToBytes(gwIncode);

            if (incodeBytes == null)
            {
                throw new StepException("Invalid gateway incode format", ErrorCategory.FailFast);
            }

            var response = await _uds.SecurityAccessSendKeyAsync(
                routing.GatewayModule,
                incodeBytes,
                0x02,
                ct);

            response.ThrowIfFailed(NrcContext.SecurityAccess);
            _session.RecordSecurityUnlock(routing.GatewayModule);

            _log?.Invoke("Gateway unlocked ✓");
        }

        private async Task StartBcmSessionAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var response = await _uds.DiagnosticSessionControlAsync(
                routing.PrimaryModule,
                DiagnosticSessionType.Extended,
                ct);

            response.ThrowIfFailed(NrcContext.DiagnosticSession);
            _session.RecordDiagnosticSession(routing.PrimaryModule, DiagnosticSessionType.Extended);

            _log?.Invoke("BCM session started");
        }

        private Task StartKeepAliveAsync(CancellationToken ct)
        {
            _keepAlive.ConfigureFromPlatform(PacingConfig!, RoutingConfig!);
            _keepAlive.Start();
            _log?.Invoke("Keep-alive started - session will be maintained");
            return Task.CompletedTask;
        }

        private async Task UnlockBcmSecurityAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Request seed from BCM
            var seedResp = await _uds.SecurityAccessRequestSeedAsync(routing.PrimaryModule, 0x01, ct);
            seedResp.ThrowIfFailed(NrcContext.SecurityAccess);

            // Submit BCM incode
            string bcmIncode;
            if (routing.HasKeyless && _incode.Length >= 8)
            {
                (bcmIncode, _) = routing.SplitKeylessIncode(_incode);
            }
            else
            {
                bcmIncode = _incode;
            }

            var incodeBytes = UdsCommunication.HexToBytes(bcmIncode);
            if (incodeBytes == null)
            {
                throw new StepException("Invalid BCM incode format", ErrorCategory.FailFast);
            }

            var keyResp = await _uds.SecurityAccessSendKeyAsync(routing.PrimaryModule, incodeBytes, 0x02, ct);
            keyResp.ThrowIfFailed(NrcContext.SecurityAccess);

            _session.RecordSecurityUnlock(routing.PrimaryModule);
            _log?.Invoke("BCM unlocked ✓");
        }

        private Task ConfirmReadyAsync(CancellationToken ct)
        {
            var remaining = _session.SecurityTimeRemaining;
            _log?.Invoke($"✓ Gateway + BCM unlocked - ALL key operations are FREE for {remaining?.TotalMinutes:F0} minutes!");
            _log?.Invoke("→ Program keys, erase keys - no additional tokens needed!");
            return Task.CompletedTask;
        }
    }
}
