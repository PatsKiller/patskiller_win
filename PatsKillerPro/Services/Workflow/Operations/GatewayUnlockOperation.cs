using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow.Operations
{
    /// <summary>
    /// Gateway Unlock Operation for 2020+ Ford vehicles.
    /// Required before any diagnostic operations on newer vehicles.
    /// Token Cost: 1 token
    /// </summary>
    public sealed class GatewayUnlockOperation : OperationBase
    {
        private readonly UdsCommunication _uds;
        private readonly VehicleSession _session;
        private readonly string _incode;
        private readonly Action<string>? _log;

        public override string Name => "Unlock Gateway";
        public override string Description => "Unlocks security gateway for diagnostic access";
        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(5);
        public override int TokenCost => 1;
        public override bool RequiresIncode => true;

        public GatewayUnlockOperation(
            UdsCommunication uds,
            VehicleSession session,
            string incode,
            PlatformPacingConfig? pacing = null,
            PlatformRoutingConfig? routing = null,
            Action<string>? log = null)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
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
                        ct => Task.CompletedTask,
                        "No gateway required for this vehicle"
                    )
                };
            }

            return new List<OperationStep>
            {
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
                )
            };
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

            _log?.Invoke("Gateway unlocked successfully");
        }
    }
}
