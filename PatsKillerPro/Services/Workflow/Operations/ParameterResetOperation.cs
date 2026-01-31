using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.Services.Workflow.Operations
{
    /// <summary>
    /// Parameter Reset Operation - Resets the minimum keys parameter.
    /// This allows the vehicle to start with fewer than the factory-required number of keys.
    /// Token Cost: 1 token
    /// </summary>
    public sealed class ParameterResetOperation : OperationBase
    {
        private readonly UdsCommunication _uds;
        private readonly KeepAliveTimer _keepAlive;
        private readonly VehicleSession _session;
        private readonly string _incode;
        private readonly int _minKeys;
        private readonly Action<string>? _log;

        public override string Name => "Reset Min Keys Parameter";
        public override string Description => $"Sets minimum keys to {_minKeys}";
        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(10);
        public override int TokenCost => 1;
        public override bool RequiresIncode => true;

        public ParameterResetOperation(
            UdsCommunication uds,
            KeepAliveTimer keepAlive,
            VehicleSession session,
            string incode,
            int minKeys = 1,
            PlatformPacingConfig? pacing = null,
            PlatformRoutingConfig? routing = null,
            Action<string>? log = null)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
            _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _incode = incode ?? throw new ArgumentNullException(nameof(incode));
            _minKeys = Math.Max(1, Math.Min(minKeys, 8));
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

            // Gateway unlock if required
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

            steps.Add(CreateStep(
                "Start Session",
                StartSessionAsync,
                "Starting extended diagnostic session",
                postDelay: pacing.PostSessionStartDelay,
                retryPolicy: RetryPolicy.Standard
            ));

            steps.Add(CreateStep(
                "Start Keep-Alive",
                ct => { _keepAlive.ConfigureFromPlatform(pacing, routing); _keepAlive.Start(); return Task.CompletedTask; },
                "Starting session maintenance"
            ));

            steps.Add(CreateStep(
                "Unlock Security",
                UnlockSecurityAsync,
                "Submitting security incode",
                postDelay: pacing.PostSecurityUnlockDelay,
                retryPolicy: RetryPolicy.Security,
                nrcContext: NrcContext.SecurityAccess
            ));

            steps.Add(CreateStep(
                "Write Min Keys",
                WriteMinKeysAsync,
                $"Setting minimum keys to {_minKeys}",
                postDelay: pacing.PostWriteDelay,
                retryPolicy: RetryPolicy.Standard,
                nrcContext: NrcContext.DataTransfer
            ));

            steps.Add(CreateStep(
                "Verify Parameter",
                VerifyParameterAsync,
                "Verifying parameter was written",
                retryPolicy: RetryPolicy.Standard
            ));

            steps.Add(CreateStep(
                "Cleanup",
                async ct => { await _keepAlive.StopAsync(); },
                "Cleaning up",
                critical: false
            ));

            return steps;
        }

        private async Task UnlockGatewayAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var sessResp = await _uds.DiagnosticSessionControlAsync(
                routing.GatewayModule, DiagnosticSessionType.Extended, ct);
            sessResp.ThrowIfFailed();

            await Task.Delay(50, ct);

            var seedResp = await _uds.SecurityAccessRequestSeedAsync(routing.GatewayModule, 0x01, ct);
            seedResp.ThrowIfFailed(NrcContext.SecurityAccess);

            var gwIncode = UdsCommunication.HexToBytes(_incode.Substring(0, Math.Min(4, _incode.Length)));
            if (gwIncode == null) throw new StepException("Invalid gateway incode", ErrorCategory.FailFast);

            var keyResp = await _uds.SecurityAccessSendKeyAsync(routing.GatewayModule, gwIncode, 0x02, ct);
            keyResp.ThrowIfFailed(NrcContext.SecurityAccess);

            _log?.Invoke("Gateway unlocked");
        }

        private async Task StartSessionAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var response = await _uds.DiagnosticSessionControlAsync(
                routing.PrimaryModule, DiagnosticSessionType.Extended, ct);
            response.ThrowIfFailed();

            _session.RecordDiagnosticSession(routing.PrimaryModule, DiagnosticSessionType.Extended);
            _log?.Invoke("Diagnostic session started");
        }

        private async Task UnlockSecurityAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var seedResp = await _uds.SecurityAccessRequestSeedAsync(routing.PrimaryModule, 0x01, ct);
            seedResp.ThrowIfFailed(NrcContext.SecurityAccess);

            var incodeBytes = UdsCommunication.HexToBytes(
                routing.HasKeyless && _incode.Length >= 8
                    ? routing.SplitKeylessIncode(_incode).BcmIncode
                    : _incode);

            if (incodeBytes == null)
                throw new StepException("Invalid incode format", ErrorCategory.FailFast);

            var keyResp = await _uds.SecurityAccessSendKeyAsync(routing.PrimaryModule, incodeBytes, 0x02, ct);
            keyResp.ThrowIfFailed(NrcContext.SecurityAccess);

            _session.RecordSecurityUnlock(routing.PrimaryModule);
            _log?.Invoke("Security unlocked");
        }

        private async Task WriteMinKeysAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            // Write minimum keys DID (0x5B13)
            var data = new byte[] { (byte)_minKeys };
            var response = await _uds.WriteDataByIdentifierAsync(
                routing.PrimaryModule,
                routing.MinKeysDid,
                data,
                ct);

            response.ThrowIfFailed(NrcContext.DataTransfer);
            _log?.Invoke($"Min keys parameter written: {_minKeys}");
        }

        private async Task VerifyParameterAsync(CancellationToken ct)
        {
            var routing = RoutingConfig!;

            var response = await _uds.ReadDataByIdentifierAsync(
                routing.PrimaryModule,
                routing.MinKeysDid,
                ct);

            if (response.Success && response.Data != null && response.Data.Length >= 4)
            {
                var readValue = response.Data[3];
                if (readValue == _minKeys)
                {
                    _log?.Invoke($"Parameter verified: {readValue}");
                    return;
                }
                throw new StepException($"Verification failed: expected {_minKeys}, got {readValue}", ErrorCategory.FailFast);
            }

            _log?.Invoke("Warning: Could not verify parameter (read failed)");
        }
    }
}
