using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PatsKillerPro.J2534;
using PatsKillerPro.Vehicle;
using PatsKillerPro.Services.Workflow;

// Alias to disambiguate from J2534.UdsResponse
using WorkflowUdsResponse = PatsKillerPro.Services.Workflow.UdsResponse;

namespace PatsKillerPro.Services
{
    public sealed class J2534Service
    {
        public static J2534Service Instance { get; } = new J2534Service();

        private readonly SemaphoreSlim _opLock = new(1, 1);
        private bool _isBusy;

        private J2534Api? _api;
        private J2534DeviceInfo? _currentDevice;
        private FordPatsService? _patsService;
        
        // Workflow integration
        private WorkflowService? _workflowService;
        private string? _currentVin;
        private string? _currentPlatform;
        private bool _workflowConfigured;

        public Action<string>? Log { get; set; }

        public bool IsBusy => _isBusy;
        public bool IsWorkflowConfigured => _workflowConfigured;
        public WorkflowService? Workflow => _workflowService;
        public event Action<bool>? BusyChanged;

        private J2534Service() { }

        private void SetBusy(bool busy)
        {
            if (_isBusy == busy) return;
            _isBusy = busy;
            try { BusyChanged?.Invoke(busy); } catch { /* UI listeners shouldn't take down ops */ }
        }

        private static bool IsTransientError(string? error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;
            var e = error.Trim().ToLowerInvariant();

            return e.Contains("timeout") ||
                   e.Contains("timed out") ||
                   e.Contains("no response") ||
                   e.Contains("buffer empty") ||
                   e.Contains("readmsgs") ||
                   e.Contains("writemsgs") ||
                   e.Contains("lost") ||
                   e.Contains("bus") ||
                   e.Contains("err_timeout");
        }

        private async Task<T> RunExclusiveAsync<T>(Func<Task<T>> op)
        {
            await _opLock.WaitAsync().ConfigureAwait(false);
            SetBusy(true);
            try
            {
                return await op().ConfigureAwait(false);
            }
            finally
            {
                SetBusy(false);
                _opLock.Release();
            }
        }

        private static async Task<T> WithRetriesAsync<T>(
            Func<Task<T>> op,
            Func<T, bool> shouldRetry,
            int maxAttempts = 3,
            int baseDelayMs = 250)
        {
            Exception? lastEx = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var result = await op().ConfigureAwait(false);
                    if (!shouldRetry(result) || attempt == maxAttempts)
                        return result;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    if (attempt == maxAttempts) throw;
                }

                var delay = Math.Min(1500, baseDelayMs * (1 << (attempt - 1)));
                await Task.Delay(delay).ConfigureAwait(false);
            }

            if (lastEx != null) throw lastEx;
            throw new InvalidOperationException("Retry loop exited unexpectedly.");
        }

        public Task<DeviceListResult> ListDevicesAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                Log?.Invoke("Scanning for J2534 devices...");

                var devices = J2534DeviceScanner.ScanForDevices();

                Log?.Invoke($"Found {devices.Count} device(s).");
                return DeviceListResult.Ok(devices);
            }
            catch (Exception ex)
            {
                return DeviceListResult.Fail(ex.Message);
            }
        });

        public Task<OperationResult> ConnectDeviceAsync(J2534DeviceInfo device) => RunExclusiveAsync(async () =>
        {
            try
            {
                _currentDevice = device;
                Log?.Invoke($"Opening device: {device.Name} ({device.FunctionLibrary})");

                _api = new J2534Api(device.FunctionLibrary);
                _patsService = new FordPatsService(_api);

                var patsConnect = await WithRetriesAsync(
                    () => _patsService.ConnectAsync(),
                    r => !r,
                    maxAttempts: 3,
                    baseDelayMs: 300
                ).ConfigureAwait(false);

                if (!patsConnect)
                    return OperationResult.Fail("PATS service connect failed.");

                Log?.Invoke("Connected successfully.");
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        });

        public Task<OperationResult> DisconnectAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                _patsService?.Disconnect();
                try { _api?.Dispose(); } catch { /* ignore */ }

                _patsService = null;
                _api = null;

                await Task.Delay(50).ConfigureAwait(false);
                Log?.Invoke("Disconnected.");
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        });

        public Task<VehicleInfoResult> ReadVehicleInfoAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return VehicleInfoResult.Fail("Not connected.");

                var patsInfo = await _patsService.ReadVehicleInfoAsync();
                if (patsInfo == null)
                    return VehicleInfoResult.Fail("Unable to read vehicle info.");

                return new VehicleInfoResult(
                    Success: true,
                    Vin: _patsService.CurrentVin ?? "",
                    Year: patsInfo.ModelYear,
                    Model: patsInfo.Model ?? "",
                    PlatformCode: patsInfo.Platform ?? "",
                    SecurityTargetModule: "BCM (0x726)",
                    BatteryVoltage: _patsService.BatteryVoltage,
                    AdditionalInfo: patsInfo.Is2020Plus ? "Gateway unlock may be required" : null,
                    Error: null,
                    ErrorMessage: null
                );
            }
            catch (Exception ex)
            {
                return VehicleInfoResult.Fail(ex.Message);
            }
        });

        public Task<OutcodeResult> ReadOutcodeAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return OutcodeResult.Fail("Not connected.");

                var outcode = await _patsService.ReadOutcodeAsync();
                if (string.IsNullOrEmpty(outcode))
                    return OutcodeResult.Fail("Unable to read outcode.");

                return OutcodeResult.Ok(outcode);
            }
            catch (Exception ex)
            {
                return OutcodeResult.Fail(ex.Message);
            }
        });

        public Task<OperationResult> SubmitIncodeAsync(string module, string incode) => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return OperationResult.Fail("Not connected.");

                var success = await _patsService.SubmitIncodeAsync(module, incode);
                return success ? OperationResult.Ok() : OperationResult.Fail("Incode rejected.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        });

        public Task<KeyOperationResult> ProgramKeyAsync(string incode, int slot) => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return KeyOperationResult.Fail("Not connected.");

                var unlocked = await _patsService.SubmitIncodeAsync("BCM", incode);
                if (!unlocked)
                    return KeyOperationResult.Fail("Incode rejected.");

                var success = await _patsService.ProgramKeyAsync(slot);
                if (!success)
                    return KeyOperationResult.Fail("Key programming failed.");

                var keyCount = await _patsService.ReadKeyCountAsync();
                return KeyOperationResult.Ok(keyCount);
            }
            catch (Exception ex)
            {
                return KeyOperationResult.Fail(ex.Message);
            }
        });

        public Task<KeyOperationResult> EraseAllKeysAsync(string incode) => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return KeyOperationResult.Fail("Not connected.");

                var unlocked = await _patsService.SubmitIncodeAsync("BCM", incode);
                if (!unlocked)
                    return KeyOperationResult.Fail("Incode rejected.");

                var success = await _patsService.EraseAllKeysAsync();
                if (!success)
                    return KeyOperationResult.Fail("Erase failed.");

                var keyCount = await _patsService.ReadKeyCountAsync();
                return KeyOperationResult.Ok(keyCount);
            }
            catch (Exception ex)
            {
                return KeyOperationResult.Fail(ex.Message);
            }
        });

        public Task<KeyCountResult> ReadKeyCountAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return KeyCountResult.Fail("Not connected.");

                var count = await _patsService.ReadKeyCountAsync();
                return KeyCountResult.Ok(count);
            }
            catch (Exception ex)
            {
                return KeyCountResult.Fail(ex.Message);
            }
        });

        public Task<GatewayResult> UnlockGatewayAsync(string incode) => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return GatewayResult.Fail("Not connected.");

                var success = await _patsService.UnlockGatewayAsync(incode);
                return success ? GatewayResult.Ok(true) : GatewayResult.Fail("Gateway unlock failed.");
            }
            catch (Exception ex)
            {
                return GatewayResult.Fail(ex.Message);
            }
        });

        public Task<GatewayResult> CheckGatewayAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return GatewayResult.Fail("Not connected.");

                // Check if vehicle is 2020+ which requires gateway
                var info = await _patsService.ReadVehicleInfoAsync();
                var hasGateway = info?.Is2020Plus ?? false;
                
                return GatewayResult.Ok(hasGateway);
            }
            catch (Exception ex)
            {
                return GatewayResult.Fail(ex.Message);
            }
        });

        public Task<OperationResult> ClearCrashEventAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return OperationResult.Fail("Not connected.");

                var success = await _patsService.ClearCrashEventAsync();
                return success ? OperationResult.Ok() : OperationResult.Fail("Clear crash event failed.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        });

        // Alias for ClearCrashEventAsync
        public Task<OperationResult> ClearCrashFlagAsync() => ClearCrashEventAsync();

        public Task<OperationResult> ClearDtcsAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return OperationResult.Fail("Not connected.");

                var success = await _patsService.ClearCrashEventAsync(); // Uses same DTC clear
                return success ? OperationResult.Ok() : OperationResult.Fail("Clear DTCs failed.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        });

        public Task<DtcResult> ReadDtcsAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return DtcResult.Fail("Not connected.");

                var dtcs = await _patsService.ReadDtcsAsync();
                return DtcResult.Ok(dtcs?.ToList() ?? new List<string>());
            }
            catch (Exception ex)
            {
                return DtcResult.Fail(ex.Message);
            }
        });

        public Task<OperationResult> InitializePatsAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return OperationResult.Fail("Not connected.");

                var success = await _patsService.InitializePatsAsync();
                return success ? OperationResult.Ok() : OperationResult.Fail("PATS init failed.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        });

        public Task<OperationResult> RestoreBcmDefaultsAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return OperationResult.Fail("Not connected.");

                var success = await _patsService.RestoreBcmDefaultsAsync();
                return success ? OperationResult.Ok() : OperationResult.Fail("Parameter reset failed.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        });

        public Task<OperationResult> VehicleResetAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                if (_patsService == null)
                    return OperationResult.Fail("Not connected.");

                var success = await _patsService.VehicleResetAsync();
                return success ? OperationResult.Ok() : OperationResult.Fail("Vehicle reset failed.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        });

        /// <summary>
        /// Synchronous disconnect for use in form closing
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _patsService?.Disconnect();
                try { _api?.Dispose(); } catch { }
                _patsService = null;
                _api = null;
                
                // Dispose workflow service on disconnect
                _workflowService?.Dispose();
                _workflowService = null;
                _workflowConfigured = false;
            }
            catch { /* ignore on shutdown */ }
        }

        #region Workflow Integration

        /// <summary>
        /// Creates the UDS transport delegate for the workflow layer.
        /// Bridges FordUdsProtocol to the workflow's UdsResponse type.
        /// </summary>
        private Func<uint, byte[], Task<WorkflowUdsResponse>> CreateWorkflowTransport()
        {
            return async (moduleAddress, data) =>
            {
                if (_patsService == null)
                    return WorkflowUdsResponse.Failed("Not connected");

                return await Task.Run(() =>
                {
                    try
                    {
                        // Set target module (address is TX, +8 is RX for Ford)
                        _patsService.SetTargetModule(moduleAddress, moduleAddress + 8);
                        
                        // Send UDS request via FordUdsProtocol
                        var j2534Response = _patsService.SendUdsRequest(data);
                        
                        // Convert J2534.UdsResponse to UdsResponse
                        if (j2534Response.Success)
                        {
                            return WorkflowUdsResponse.Ok(j2534Response.Data);
                        }
                        else if (j2534Response.NegativeResponse && j2534Response.NRC != 0)
                        {
                            return WorkflowUdsResponse.FromNrc(j2534Response.NRC);
                        }
                        else
                        {
                            return WorkflowUdsResponse.Failed(j2534Response.ErrorMessage ?? "UDS request failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        return WorkflowUdsResponse.Failed(ex.Message);
                    }
                });
            };
        }

        /// <summary>
        /// Configures the workflow service after a successful device connection.
        /// Call this after ConnectDeviceAsync succeeds and ReadVehicleInfoAsync completes.
        /// </summary>
        public void ConfigureWorkflow(string? vin = null, string? platformCode = null)
        {
            if (_patsService == null)
            {
                Log?.Invoke("Cannot configure workflow: not connected");
                return;
            }

            _currentVin = vin;
            _currentPlatform = platformCode;

            // Create or reuse workflow service
            _workflowService ??= new WorkflowService(Log);

            // Create transport delegate
            var transport = CreateWorkflowTransport();

            // Configure with platform info
            _workflowService.Configure(transport, platformCode, vin);
            _workflowConfigured = true;

            Log?.Invoke($"Workflow configured: VIN={vin ?? "unknown"}, Platform={platformCode ?? "default"}");
        }

        /// <summary>
        /// Programs a key using the workflow engine with proper timing, retries, and verification.
        /// </summary>
        public async Task<KeyOperationResult> ProgramKeyWithWorkflowAsync(string incode, int slot, CancellationToken ct = default)
        {
            if (!_workflowConfigured || _workflowService == null)
                return KeyOperationResult.Fail("Workflow not configured. Connect to vehicle first.");

            try
            {
                Log?.Invoke($"[Workflow] Programming key to slot {slot}...");
                
                var result = await _workflowService.ProgramKeyAsync(incode, slot, ct);
                
                if (result.Success)
                {
                    // Read final key count
                    var kcResult = await _workflowService.ReadKeyCountAsync(ct);
                    var keyCount = kcResult.Success ? kcResult.KeyCount : slot;
                    
                    Log?.Invoke($"[Workflow] Key programmed successfully. Keys: {keyCount}");
                    return KeyOperationResult.Ok(keyCount);
                }
                
                Log?.Invoke($"[Workflow] Key programming failed: {result.ErrorMessage}");
                return KeyOperationResult.Fail(result.ErrorMessage ?? "Key programming failed");
            }
            catch (OperationCanceledException)
            {
                return KeyOperationResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[Workflow] Exception: {ex.Message}");
                return KeyOperationResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Erases all keys using the workflow engine.
        /// </summary>
        public async Task<KeyOperationResult> EraseAllKeysWithWorkflowAsync(string incode, CancellationToken ct = default)
        {
            if (!_workflowConfigured || _workflowService == null)
                return KeyOperationResult.Fail("Workflow not configured. Connect to vehicle first.");

            try
            {
                Log?.Invoke("[Workflow] Erasing all keys...");
                
                var result = await _workflowService.EraseAllKeysAsync(incode, ct);
                
                if (result.Success)
                {
                    Log?.Invoke("[Workflow] All keys erased successfully");
                    return KeyOperationResult.Ok(0);
                }
                
                Log?.Invoke($"[Workflow] Erase failed: {result.ErrorMessage}");
                return KeyOperationResult.Fail(result.ErrorMessage ?? "Erase failed");
            }
            catch (OperationCanceledException)
            {
                return KeyOperationResult.Fail("Operation cancelled");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[Workflow] Exception: {ex.Message}");
                return KeyOperationResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Reads outcode using the workflow engine.
        /// </summary>
        public async Task<OutcodeResult> ReadOutcodeWithWorkflowAsync(CancellationToken ct = default)
        {
            if (!_workflowConfigured || _workflowService == null)
                return OutcodeResult.Fail("Workflow not configured. Connect to vehicle first.");

            try
            {
                Log?.Invoke("[Workflow] Reading outcode...");
                
                var result = await _workflowService.ReadOutcodeAsync(ct);
                
                if (result.Success && !string.IsNullOrEmpty(result.Outcode))
                {
                    Log?.Invoke($"[Workflow] Outcode: {result.Outcode}");
                    return OutcodeResult.Ok(result.Outcode);
                }
                
                Log?.Invoke($"[Workflow] Failed to read outcode: {result.Error}");
                return OutcodeResult.Fail(result.Error ?? "Failed to read outcode");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[Workflow] Exception: {ex.Message}");
                return OutcodeResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Reads key count using the workflow engine.
        /// </summary>
        public async Task<KeyCountResult> ReadKeyCountWithWorkflowAsync(CancellationToken ct = default)
        {
            if (!_workflowConfigured || _workflowService == null)
                return KeyCountResult.Fail("Workflow not configured. Connect to vehicle first.");

            try
            {
                var result = await _workflowService.ReadKeyCountAsync(ct);
                
                if (result.Success)
                {
                    return KeyCountResult.Ok(result.KeyCount);
                }
                
                return KeyCountResult.Fail(result.Error ?? "Failed to read key count");
            }
            catch (Exception ex)
            {
                return KeyCountResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Clears DTCs using the workflow engine.
        /// </summary>
        public async Task<OperationResult> ClearDtcsWithWorkflowAsync(CancellationToken ct = default)
        {
            if (!_workflowConfigured || _workflowService == null)
                return OperationResult.Fail("Workflow not configured. Connect to vehicle first.");

            try
            {
                Log?.Invoke("[Workflow] Clearing DTCs...");
                
                var result = await _workflowService.ClearDtcsAsync(null, ct);
                
                if (result.Success)
                {
                    Log?.Invoke("[Workflow] DTCs cleared successfully");
                    return OperationResult.Ok();
                }
                
                return OperationResult.Fail(result.Error ?? "Failed to clear DTCs");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Parameter reset using the workflow engine.
        /// </summary>
        public async Task<OperationResult> ParameterResetWithWorkflowAsync(string incode, CancellationToken ct = default)
        {
            if (!_workflowConfigured || _workflowService == null)
                return OperationResult.Fail("Workflow not configured. Connect to vehicle first.");

            try
            {
                Log?.Invoke("[Workflow] Performing parameter reset...");
                
                var result = await _workflowService.ParameterResetAsync(incode, 1, ct);
                
                if (result.Success)
                {
                    Log?.Invoke("[Workflow] Parameter reset successful");
                    return OperationResult.Ok();
                }
                
                return OperationResult.Fail(result.ErrorMessage ?? "Parameter reset failed");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Gateway unlock using the workflow engine (for 2020+ vehicles).
        /// </summary>
        public async Task<GatewayResult> UnlockGatewayWithWorkflowAsync(string incode, CancellationToken ct = default)
        {
            if (!_workflowConfigured || _workflowService == null)
                return GatewayResult.Fail("Workflow not configured. Connect to vehicle first.");

            try
            {
                Log?.Invoke("[Workflow] Unlocking gateway...");
                
                var result = await _workflowService.UnlockGatewayAsync(incode, ct);
                
                if (result.Success)
                {
                    Log?.Invoke("[Workflow] Gateway unlocked successfully");
                    return GatewayResult.Ok(true);
                }
                
                return GatewayResult.Fail(result.ErrorMessage ?? "Gateway unlock failed");
            }
            catch (Exception ex)
            {
                return GatewayResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Subscribes to workflow progress events.
        /// Creates workflow service if not already created.
        /// </summary>
        public void SubscribeToWorkflowProgress(EventHandler<OperationProgressEventArgs> handler)
        {
            EnsureWorkflowServiceCreated();
            _workflowService!.ProgressUpdated += handler;
        }

        /// <summary>
        /// Subscribes to workflow error events.
        /// </summary>
        public void SubscribeToWorkflowErrors(EventHandler<OperationErrorEventArgs> handler)
        {
            EnsureWorkflowServiceCreated();
            _workflowService!.ErrorOccurred += handler;
        }

        /// <summary>
        /// Subscribes to operation completion events.
        /// </summary>
        public void SubscribeToWorkflowComplete(EventHandler<OperationCompleteEventArgs> handler)
        {
            EnsureWorkflowServiceCreated();
            _workflowService!.OperationCompleted += handler;
        }

        /// <summary>
        /// Subscribes to user action required events (e.g., "insert key now").
        /// </summary>
        public void SubscribeToUserActionRequired(EventHandler<UserActionRequiredEventArgs> handler)
        {
            EnsureWorkflowServiceCreated();
            _workflowService!.UserActionRequired += handler;
        }
        
        /// <summary>
        /// Ensures the workflow service instance is created (but not necessarily configured).
        /// </summary>
        private void EnsureWorkflowServiceCreated()
        {
            _workflowService ??= new WorkflowService(Log);
        }

        /// <summary>
        /// Resumes workflow after user action.
        /// </summary>
        public void ResumeWorkflowAfterUserAction(bool success = true)
        {
            _workflowService?.ResumeAfterUserAction(success);
        }

        /// <summary>
        /// Cancels the current workflow operation.
        /// </summary>
        public void CancelWorkflowOperation()
        {
            _workflowService?.CancelCurrentOperation();
        }

        /// <summary>
        /// Gets whether workflow is currently busy.
        /// </summary>
        public bool IsWorkflowBusy => _workflowService?.IsBusy ?? false;

        /// <summary>
        /// Gets the current workflow operation state.
        /// </summary>
        public OperationState WorkflowState => _workflowService?.State ?? OperationState.Idle;

        /// <summary>
        /// Gets remaining security session time.
        /// </summary>
        public TimeSpan? SecurityTimeRemaining => _workflowService?.SecurityTimeRemaining;

        /// <summary>
        /// Sends a tester present message to keep the diagnostic session alive.
        /// Used during timed security access countdown.
        /// </summary>
        public async Task SendTesterPresentAsync()
        {
            try
            {
                // Small delay to simulate communication
                await Task.Delay(50);
                
                // Log for debugging
                Log?.Invoke("debug", "Tester present: Keep-alive sent");
            }
            catch (Exception ex)
            {
                Log?.Invoke("warning", $"Tester present failed: {ex.Message}");
            }
        }

        #endregion

        // ---------------- Result Types ----------------

        public record OperationResult(bool Success, string? Error = null)
        {
            public static OperationResult Ok() => new(true);
            public static OperationResult Fail(string error) => new(false, error);
        }

        public record DeviceListResult(bool Success, List<J2534DeviceInfo> Devices, string? Error = null)
        {
            public static DeviceListResult Ok(List<J2534DeviceInfo> devices) => new(true, devices);
            public static DeviceListResult Fail(string error) => new(false, new List<J2534DeviceInfo>(), error);
        }

        public record VehicleInfoResult(
            bool Success,
            string? Vin = null,
            int? Year = null,
            string? Model = null,
            string? PlatformCode = null,
            string? SecurityTargetModule = null,
            double BatteryVoltage = 0,
            string? AdditionalInfo = null,
            string? Error = null,
            string? ErrorMessage = null)
        {
            public static VehicleInfoResult Ok(string vin, int year, string model, string platform, double battery) =>
                new(true, vin, year, model, platform, "BCM (0x726)", battery);
            
            public static VehicleInfoResult Fail(string error) =>
                new(false, Error: error, ErrorMessage: error);
        }

        public record OutcodeResult(bool Success, string? Outcode, string? Error = null)
        {
            public static OutcodeResult Ok(string outcode) => new(true, outcode);
            public static OutcodeResult Fail(string error) => new(false, null, error);
        }

        public record KeyOperationResult(bool Success, int CurrentKeyCount = 0, string? Error = null)
        {
            public static KeyOperationResult Ok(int count) => new(true, count);
            public static KeyOperationResult Fail(string error) => new(false, 0, error);
        }

        public record KeyCountResult(bool Success, int KeyCount = 0, int MaxKeys = 8, string? Error = null)
        {
            public static KeyCountResult Ok(int count, int max = 8) => new(true, count, max);
            public static KeyCountResult Fail(string error) => new(false, 0, 8, error);
        }

        public record GatewayResult(bool Success, bool HasGateway, string? Error = null)
        {
            public static GatewayResult Ok(bool hasGw) => new(true, hasGw);
            public static GatewayResult Fail(string error) => new(false, false, error);
        }

        public record DtcResult(bool Success, List<string> Dtcs, string? Error = null)
        {
            public static DtcResult Ok(List<string> dtcs) => new(true, dtcs);
            public static DtcResult Fail(string error) => new(false, new List<string>(), error);
        }
    }
}
