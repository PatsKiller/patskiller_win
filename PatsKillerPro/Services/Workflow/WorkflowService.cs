using System;
using System.Threading;
using System.Threading.Tasks;
using PatsKillerPro.Services.Workflow.Operations;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// High-level workflow service coordinating PATS operations.
    /// Provides the bridge between MainForm UI and the workflow runner system.
    /// </summary>
    public sealed class WorkflowService : IDisposable
    {
        private readonly OperationRunner _runner;
        private readonly KeepAliveTimer _keepAlive;
        private readonly VehicleSession _session;
        private readonly VerificationService _verification;
        private readonly Action<string>? _log;

        private UdsCommunication? _uds;
        private PlatformPacingConfig? _pacingConfig;
        private PlatformRoutingConfig? _routingConfig;
        private bool _disposed;

        #region Events

        /// <summary>Operation progress updated</summary>
        public event EventHandler<OperationProgressEventArgs>? ProgressUpdated;

        /// <summary>Operation error occurred</summary>
        public event EventHandler<OperationErrorEventArgs>? ErrorOccurred;

        /// <summary>User action required</summary>
        public event EventHandler<UserActionRequiredEventArgs>? UserActionRequired;

        /// <summary>Operation completed</summary>
        public event EventHandler<OperationCompleteEventArgs>? OperationCompleted;

        /// <summary>Log message</summary>
        public event EventHandler<OperationLogEventArgs>? LogMessage;

        /// <summary>Security session expiring soon</summary>
        public event EventHandler<SessionExpiringEventArgs>? SessionExpiring;

        /// <summary>Security session expired</summary>
        public event EventHandler<SessionExpiredEventArgs>? SessionExpired;

        #endregion

        #region Properties

        /// <summary>Current operation state</summary>
        public OperationState State => _runner.State;

        /// <summary>Whether an operation is currently running</summary>
        public bool IsBusy => _runner.IsBusy;

        /// <summary>Whether security is currently unlocked</summary>
        public bool IsSecurityUnlocked => _session.HasActiveSecuritySession;

        /// <summary>Time remaining on security session</summary>
        public TimeSpan? SecurityTimeRemaining => _session.SecurityTimeRemaining;

        /// <summary>Current platform pacing configuration</summary>
        public PlatformPacingConfig? PacingConfig => _pacingConfig;

        /// <summary>Current platform routing configuration</summary>
        public PlatformRoutingConfig? RoutingConfig => _routingConfig;

        #endregion

        public WorkflowService(Action<string>? log = null)
        {
            _log = log;

            _runner = new OperationRunner();
            _runner.ProgressUpdated += (s, e) => ProgressUpdated?.Invoke(s, e);
            _runner.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(s, e);
            _runner.UserActionRequired += (s, e) => UserActionRequired?.Invoke(s, e);
            _runner.OperationCompleted += (s, e) => OperationCompleted?.Invoke(s, e);
            _runner.LogMessage += (s, e) => LogMessage?.Invoke(s, e);

            _keepAlive = new KeepAliveTimer(SendTesterPresentAsync, log);
            _session = new VehicleSession(log);
            _session.SessionExpiring += (s, e) => SessionExpiring?.Invoke(s, e);
            _session.SessionExpired += (s, e) => SessionExpired?.Invoke(s, e);

            _verification = new VerificationService(log);
        }

        /// <summary>
        /// Configures the workflow service with UDS communication and platform settings.
        /// Must be called before running any operations.
        /// </summary>
        /// <param name="sendUdsAsync">Function to send UDS messages: (moduleAddress, data) => response</param>
        /// <param name="platformCode">Platform code (e.g., "F3", "M5") or null for auto-detect</param>
        /// <param name="vin">Vehicle VIN for platform detection</param>
        public void Configure(
            Func<uint, byte[], Task<UdsResponse>> sendUdsAsync,
            string? platformCode = null,
            string? vin = null)
        {
            _uds = new UdsCommunication(sendUdsAsync, _log);

            // Get platform configurations
            if (!string.IsNullOrEmpty(platformCode))
            {
                _pacingConfig = PlatformPacingRegistry.GetConfig(platformCode);
                _routingConfig = PlatformRoutingRegistry.GetConfig(platformCode);
            }
            else if (!string.IsNullOrEmpty(vin))
            {
                _pacingConfig = PlatformPacingRegistry.GetConfigFromVin(vin);
                _routingConfig = PlatformRoutingConfig.Default; // VIN decode would be needed
            }
            else
            {
                _pacingConfig = PlatformPacingConfig.Default;
                _routingConfig = PlatformRoutingConfig.Default;
            }

            _session.Configure(_pacingConfig, _routingConfig, vin);

            _log?.Invoke($"WorkflowService configured: Platform={_routingConfig.PlatformCode}");
        }

        /// <summary>
        /// Configures with explicit platform configurations.
        /// </summary>
        public void Configure(
            Func<uint, byte[], Task<UdsResponse>> sendUdsAsync,
            PlatformPacingConfig pacing,
            PlatformRoutingConfig routing,
            string? vin = null)
        {
            _uds = new UdsCommunication(sendUdsAsync, _log);
            _pacingConfig = pacing;
            _routingConfig = routing;
            _session.Configure(pacing, routing, vin);

            _log?.Invoke($"WorkflowService configured: Platform={routing.PlatformCode}");
        }

        #region Operations

        /// <summary>
        /// Programs a new key using the workflow runner.
        /// </summary>
        public async Task<OperationResult> ProgramKeyAsync(string incode, int targetSlot, CancellationToken ct = default)
        {
            EnsureConfigured();

            var operation = new ProgramKeyOperation(
                _uds!,
                _keepAlive,
                _session,
                incode,
                targetSlot,
                _pacingConfig,
                _routingConfig,
                _log);

            return await _runner.RunAsync(operation, ct);
        }

        /// <summary>
        /// Erases all keys using the workflow runner.
        /// </summary>
        public async Task<OperationResult> EraseAllKeysAsync(string incode, CancellationToken ct = default)
        {
            EnsureConfigured();

            var operation = new EraseKeysOperation(
                _uds!,
                _keepAlive,
                _session,
                incode,
                _pacingConfig,
                _routingConfig,
                _log);

            return await _runner.RunAsync(operation, ct);
        }

        /// <summary>
        /// Reads current key count.
        /// </summary>
        public async Task<(bool Success, int KeyCount, string? Error)> ReadKeyCountAsync(CancellationToken ct = default)
        {
            EnsureConfigured();

            try
            {
                var response = await _uds!.ReadDataByIdentifierAsync(
                    _routingConfig!.PrimaryModule,
                    _routingConfig.KeyCountDid,
                    ct);

                if (response.Success)
                {
                    var count = UdsCommunication.ExtractKeyCount(response.Data);
                    return (true, count, null);
                }

                return (false, 0, response.ErrorMessage ?? "Failed to read key count");
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        /// <summary>
        /// Reads VIN from vehicle.
        /// </summary>
        public async Task<(bool Success, string? Vin, string? Error)> ReadVinAsync(CancellationToken ct = default)
        {
            EnsureConfigured();

            try
            {
                // Try BCM first
                var response = await _uds!.ReadDataByIdentifierAsync(
                    _routingConfig!.PrimaryModule,
                    _routingConfig.VinDid,
                    ct);

                if (response.Success && response.Data != null)
                {
                    var vin = UdsCommunication.ExtractVin(response.Data);
                    if (!string.IsNullOrEmpty(vin))
                        return (true, vin, null);
                }

                // Fallback to PCM
                response = await _uds.ReadDataByIdentifierAsync(
                    _routingConfig.PcmModule,
                    _routingConfig.VinDid,
                    ct);

                if (response.Success && response.Data != null)
                {
                    var vin = UdsCommunication.ExtractVin(response.Data);
                    return (true, vin, null);
                }

                return (false, null, "Could not read VIN");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Reads outcode from vehicle.
        /// </summary>
        public async Task<(bool Success, string? Outcode, string? Error)> ReadOutcodeAsync(CancellationToken ct = default)
        {
            EnsureConfigured();

            try
            {
                // Start extended session
                var sessResp = await _uds!.DiagnosticSessionControlAsync(
                    _routingConfig!.PrimaryModule,
                    DiagnosticSessionType.Extended,
                    ct);

                if (!sessResp.Success)
                    return (false, null, "Failed to start diagnostic session");

                await Task.Delay(50, ct);

                // Request security seed (this gives us the outcode)
                var seedResp = await _uds.SecurityAccessRequestSeedAsync(
                    _routingConfig.PrimaryModule,
                    0x01,
                    ct);

                if (!seedResp.Success)
                    return (false, null, seedResp.ErrorMessage ?? "Failed to read outcode");

                var outcode = UdsCommunication.ExtractOutcode(seedResp.Data);

                // For keyless, also get RFA outcode
                if (_routingConfig.HasKeyless && _routingConfig.SecondaryModule != 0)
                {
                    var rfaSess = await _uds.DiagnosticSessionControlAsync(
                        _routingConfig.SecondaryModule,
                        DiagnosticSessionType.Extended,
                        ct);

                    if (rfaSess.Success)
                    {
                        await Task.Delay(50, ct);
                        var rfaSeed = await _uds.SecurityAccessRequestSeedAsync(
                            _routingConfig.SecondaryModule,
                            0x01,
                            ct);

                        if (rfaSeed.Success)
                        {
                            var rfaOutcode = UdsCommunication.ExtractOutcode(rfaSeed.Data);
                            if (!string.IsNullOrEmpty(rfaOutcode))
                            {
                                outcode = $"{outcode}-{rfaOutcode}";
                            }
                        }
                    }
                }

                return (true, outcode, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Clears DTCs from a module.
        /// </summary>
        public async Task<(bool Success, string? Error)> ClearDtcsAsync(uint? moduleAddress = null, CancellationToken ct = default)
        {
            EnsureConfigured();

            try
            {
                var target = moduleAddress ?? _routingConfig!.PrimaryModule;
                var response = await _uds!.ClearDtcAsync(target, 0xFFFFFF, ct);

                return response.Success
                    ? (true, null)
                    : (false, response.ErrorMessage ?? "Failed to clear DTCs");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        #endregion

        #region Control Methods

        /// <summary>
        /// Signals that the user has completed a required action.
        /// </summary>
        public void ResumeAfterUserAction(bool success = true)
        {
            _runner.ResumeAfterUserAction(success);
        }

        /// <summary>
        /// Requests cancellation of the current operation.
        /// </summary>
        public void CancelCurrentOperation()
        {
            _runner.RequestCancel();
        }

        /// <summary>
        /// Clears all session state (security unlock, etc).
        /// </summary>
        public void ClearSession()
        {
            _session.ClearAll();
            _keepAlive.Stop();
        }

        #endregion

        #region Private Methods

        private void EnsureConfigured()
        {
            if (_uds == null || _pacingConfig == null || _routingConfig == null)
            {
                throw new InvalidOperationException("WorkflowService not configured. Call Configure() first.");
            }
        }

        private async Task<bool> SendTesterPresentAsync(uint moduleAddress, byte[] data)
        {
            if (_uds == null) return false;

            try
            {
                var response = await _uds.TesterPresentAsync(moduleAddress, false);
                return response.Success;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _runner.Dispose();
            _keepAlive.Dispose();
            _session.Dispose();
        }
    }
}
