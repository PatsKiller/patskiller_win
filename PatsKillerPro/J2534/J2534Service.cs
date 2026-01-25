using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PatsKillerPro.J2534;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// J2534 Service for PatsKiller Pro Desktop Application
    /// Wraps the J2534 library for use by MainForm
    /// 
    /// This replaces the simulation code with real J2534 device communication
    /// </summary>
    public class J2534Service : IDisposable
    {
        private static J2534Service? _instance;
        public static J2534Service Instance => _instance ??= new J2534Service();

        private J2534Api? _api;
        private FordPatsService? _patsService;
        private J2534DeviceInfo? _connectedDevice;
        private bool _disposed;

        // Events for UI updates
        public event EventHandler<string>? LogMessage;
        public event EventHandler<double>? VoltageChanged;
        public event EventHandler<J2534ProgressEventArgs>? ProgressChanged;

        public bool IsConnected => _patsService != null;
        public J2534DeviceInfo? ConnectedDevice => _connectedDevice;
        public string? CurrentVin => _patsService?.CurrentVin;
        public VehicleInfo? CurrentVehicle => _patsService?.CurrentVehicle;
        public double BatteryVoltage => _patsService?.BatteryVoltage ?? 0;
        
        // Additional properties for MainForm compatibility
        public string? ConnectedDeviceName => _connectedDevice?.Name;
        public string? CurrentOutcode => _patsService?.CurrentOutcode;
        public int KeyCount => _patsService?.KeyCount ?? 0;
        public bool IsSecurityUnlocked => _patsService?.IsSecurityUnlocked ?? false;

        private J2534Service()
        {
        }

        #region Device Scanning

        /// <summary>
        /// Scan for all installed J2534 devices
        /// </summary>
        public Task<List<J2534DeviceInfo>> ScanForDevicesAsync()
        {
            return Task.Run(() =>
            {
                Log("Scanning for J2534 devices...");
                var devices = J2534DeviceScanner.ScanForDevices();

                foreach (var device in devices)
                {
                    var status = device.IsAvailable ? "Available" : "Not Found";
                    Log($"  [{status}] {device.Name} ({device.Vendor})");
                }

                Log($"Found {devices.Count} device(s)");
                return devices;
            });
        }

        /// <summary>
        /// Get first available device
        /// </summary>
        public J2534DeviceInfo? GetFirstAvailableDevice()
        {
            return J2534DeviceScanner.GetFirstAvailableDevice();
        }

        #endregion

        #region Connection

        /// <summary>
        /// Connect to a J2534 device and vehicle
        /// </summary>
        public async Task<J2534Result> ConnectDeviceAsync(J2534DeviceInfo device)
        {
            try
            {
                if (!device.IsAvailable)
                    return J2534Result.Fail($"Device DLL not found: {device.FunctionLibrary}");

                Log($"Loading J2534 DLL: {device.FunctionLibrary}");

                // Load the J2534 DLL
                _api = new J2534Api(device.FunctionLibrary);

                // Create PATS service
                _patsService = new FordPatsService(_api);
                _patsService.LogMessage += (s, msg) => Log(msg);
                _patsService.VoltageChanged += (s, v) => VoltageChanged?.Invoke(this, v);
                _patsService.ProgressChanged += (s, e) => ProgressChanged?.Invoke(this, 
                    new J2534ProgressEventArgs(e.Message, e.Percent));

                // Connect to vehicle
                Log($"Connecting to vehicle via {device.Name}...");
                var result = await _patsService.ConnectAsync();

                if (!result.Success)
                {
                    _patsService.Dispose();
                    _patsService = null;
                    _api.Dispose();
                    _api = null;
                    return J2534Result.Fail(result.Error ?? "Connection failed");
                }

                _connectedDevice = device;
                Log($"Connected to {device.Name}");

                return J2534Result.Ok();
            }
            catch (Exception ex)
            {
                Log($"Connection error: {ex.Message}");
                return J2534Result.Fail($"Connection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnect from device
        /// </summary>
        public Task<J2534Result> DisconnectDeviceAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    _patsService?.Disconnect();
                    _patsService?.Dispose();
                    _patsService = null;

                    _api?.Dispose();
                    _api = null;

                    _connectedDevice = null;

                    Log("Disconnected from device");
                    return J2534Result.Ok();
                }
                catch (Exception ex)
                {
                    return J2534Result.Fail($"Disconnect error: {ex.Message}");
                }
            });
        }

        #endregion

        #region Vehicle Operations

        /// <summary>
        /// Read vehicle information (VIN, outcode, battery voltage)
        /// </summary>
        public async Task<VehicleReadResult> ReadVehicleAsync()
        {
            if (_patsService == null)
                return new VehicleReadResult { Success = false, Error = "Not connected" };

            try
            {
                // Read VIN
                var vinResult = await _patsService.ReadVinAsync();
                if (!vinResult.Success)
                    return new VehicleReadResult { Success = false, Error = vinResult.Error };

                // Read outcode
                var outcodeResult = await _patsService.ReadOutcodeAsync();
                // Outcode read is optional - may fail on some vehicles

                // Read key count
                var keyCountResult = await _patsService.ReadKeyCountAsync();

                return new VehicleReadResult
                {
                    Success = true,
                    Vin = vinResult.Value ?? "",
                    VehicleInfo = _patsService.CurrentVehicle,
                    Outcode = outcodeResult.Value ?? "",
                    KeyCount = keyCountResult.Value,
                    BatteryVoltage = _patsService.BatteryVoltage
                };
            }
            catch (Exception ex)
            {
                return new VehicleReadResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Read outcode from specific module
        /// </summary>
        public async Task<OutcodeResult> ReadModuleOutcodeAsync(string module, bool useMsCan = false)
        {
            if (_patsService == null)
                return new OutcodeResult { Success = false, Error = "Not connected" };

            try
            {
                var result = await _patsService.ReadOutcodeFromModuleAsync(module);
                return new OutcodeResult
                {
                    Success = result.Success,
                    Outcode = result.Value ?? "",
                    ModuleName = module,
                    Error = result.Error
                };
            }
            catch (Exception ex)
            {
                return new OutcodeResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Submit incode to vehicle
        /// </summary>
        public async Task<J2534Result> SubmitIncodeAsync(string module, string incode)
        {
            if (_patsService == null)
                return J2534Result.Fail("Not connected");

            try
            {
                var result = await _patsService.SubmitIncodeAsync(incode);
                return result.Success ? J2534Result.Ok() : J2534Result.Fail(result.Error ?? "Failed");
            }
            catch (Exception ex)
            {
                return J2534Result.Fail(ex.Message);
            }
        }

        #endregion

        #region Key Operations

        /// <summary>
        /// Erase all keys from vehicle
        /// </summary>
        public async Task<KeyOperationResult> EraseAllKeysAsync(string incode)
        {
            if (_patsService == null)
                return new KeyOperationResult { Success = false, Error = "Not connected" };

            try
            {
                // Submit incode first if not already unlocked
                if (!_patsService.IsSecurityUnlocked)
                {
                    var incodeResult = await _patsService.SubmitIncodeAsync(incode);
                    if (!incodeResult.Success)
                        return new KeyOperationResult { Success = false, Error = incodeResult.Error };
                }

                var result = await _patsService.EraseAllKeysAsync();
                var keyCount = await _patsService.ReadKeyCountAsync();

                return new KeyOperationResult
                {
                    Success = result.Success,
                    KeysAffected = 0,
                    CurrentKeyCount = keyCount.Value,
                    Error = result.Error
                };
            }
            catch (Exception ex)
            {
                return new KeyOperationResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Program a new key
        /// </summary>
        public async Task<KeyOperationResult> ProgramKeyAsync(string incode, int slot = 0)
        {
            if (_patsService == null)
                return new KeyOperationResult { Success = false, Error = "Not connected" };

            try
            {
                // Submit incode first if not already unlocked
                if (!_patsService.IsSecurityUnlocked)
                {
                    var incodeResult = await _patsService.SubmitIncodeAsync(incode);
                    if (!incodeResult.Success)
                        return new KeyOperationResult { Success = false, Error = incodeResult.Error };
                }

                var result = await _patsService.ProgramKeyAsync(slot);
                var keyCount = await _patsService.ReadKeyCountAsync();

                return new KeyOperationResult
                {
                    Success = result.Success,
                    KeysAffected = 1,
                    CurrentKeyCount = keyCount.Value,
                    KeySlot = slot,
                    Error = result.Error
                };
            }
            catch (Exception ex)
            {
                return new KeyOperationResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Read key count from vehicle
        /// </summary>
        public async Task<KeyCountResult> ReadKeyCountAsync()
        {
            if (_patsService == null)
                return new KeyCountResult { Success = false, Error = "Not connected" };

            try
            {
                var result = await _patsService.ReadKeyCountAsync();
                return new KeyCountResult
                {
                    Success = result.Success,
                    KeyCount = result.Value,
                    MaxKeys = 8,
                    Error = result.Error
                };
            }
            catch (Exception ex)
            {
                return new KeyCountResult { Success = false, Error = ex.Message };
            }
        }

        #endregion

        #region Gateway Operations

        /// <summary>
        /// Check if vehicle requires gateway unlock (2020+)
        /// </summary>
        public Task<bool> RequiresGatewayUnlockAsync()
        {
            return Task.FromResult(_patsService?.RequiresGatewayUnlock() ?? false);
        }

        /// <summary>
        /// Unlock security gateway (2020+ vehicles)
        /// </summary>
        public async Task<GatewayResult> UnlockGatewayAsync(string incode)
        {
            if (_patsService == null)
                return new GatewayResult { Success = false, Error = "Not connected" };

            try
            {
                var result = await _patsService.UnlockGatewayAsync(incode);
                return new GatewayResult
                {
                    Success = result.Success,
                    SessionDurationSeconds = 600, // 10 minutes
                    Error = result.Error
                };
            }
            catch (Exception ex)
            {
                return new GatewayResult { Success = false, Error = ex.Message };
            }
        }

        #endregion

        #region Utility Operations

        /// <summary>
        /// Read battery voltage
        /// </summary>
        public Task<double> ReadBatteryVoltageAsync()
        {
            return Task.FromResult(_patsService?.BatteryVoltage ?? 0);
        }

        /// <summary>
        /// Clear crash/theft flag
        /// </summary>
        public async Task<J2534Result> ClearCrashFlagAsync()
        {
            if (_patsService == null)
                return J2534Result.Fail("Not connected");

            var result = await _patsService.ClearCrashEventAsync();
            return result.Success ? J2534Result.Ok() : J2534Result.Fail(result.Error ?? "Failed");
        }

        /// <summary>
        /// Clear theft flag (alias for crash flag)
        /// </summary>
        public Task<J2534Result> ClearTheftFlagAsync()
        {
            return ClearCrashFlagAsync();
        }

        /// <summary>
        /// Restore BCM to factory defaults
        /// </summary>
        public async Task<J2534Result> RestoreBcmDefaultsAsync()
        {
            if (_patsService == null)
                return J2534Result.Fail("Not connected");

            var result = await _patsService.RestoreBcmDefaultsAsync();
            return result.Success ? J2534Result.Ok() : J2534Result.Fail(result.Error ?? "Failed");
        }

        /// <summary>
        /// Read DTCs from vehicle
        /// </summary>
        public async Task<DtcResult> ReadDtcsAsync()
        {
            if (_patsService == null)
                return new DtcResult { Success = false, Error = "Not connected" };

            var result = await _patsService.ReadDtcsAsync();
            return new DtcResult
            {
                Success = result.Success,
                Dtcs = result.Value ?? Array.Empty<string>(),
                DtcCount = result.Value?.Length ?? 0,
                Error = result.Error
            };
        }

        /// <summary>
        /// Clear all DTCs
        /// </summary>
        public async Task<J2534Result> ClearDtcsAsync()
        {
            if (_patsService == null)
                return J2534Result.Fail("Not connected");

            var result = await _patsService.ClearAllDtcsAsync();
            return result.Success ? J2534Result.Ok() : J2534Result.Fail(result.Error ?? "Failed");
        }

        /// <summary>
        /// Initialize PATS system
        /// </summary>
        public async Task<J2534Result> InitializePatsAsync()
        {
            if (_patsService == null)
                return J2534Result.Fail("Not connected");

            var result = await _patsService.InitializePatsAsync();
            return result.Success ? J2534Result.Ok() : J2534Result.Fail(result.Error ?? "Failed");
        }

        /// <summary>
        /// Perform vehicle reset
        /// </summary>
        public async Task<J2534Result> VehicleResetAsync()
        {
            if (_patsService == null)
                return J2534Result.Fail("Not connected");

            var result = await _patsService.VehicleResetAsync();
            return result.Success ? J2534Result.Ok() : J2534Result.Fail(result.Error ?? "Failed");
        }

        #endregion

        #region Helpers

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _patsService?.Dispose();
                _api?.Dispose();
                _disposed = true;
            }
        }
    }

    #region Result Classes

    public class J2534Result
    {
        public bool Success { get; set; }
        public string? Error { get; set; }

        public static J2534Result Ok() => new J2534Result { Success = true };
        public static J2534Result Fail(string error) => new J2534Result { Success = false, Error = error };
    }

    public class VehicleReadResult
    {
        public bool Success { get; set; }
        public string Vin { get; set; } = "";
        public VehicleInfo? VehicleInfo { get; set; }
        public string Outcode { get; set; } = "";
        public int KeyCount { get; set; }
        public double BatteryVoltage { get; set; }
        public string? Error { get; set; }
    }

    public class OutcodeResult
    {
        public bool Success { get; set; }
        public string Outcode { get; set; } = "";
        public string ModuleName { get; set; } = "";
        public string? Error { get; set; }
    }

    public class KeyOperationResult
    {
        public bool Success { get; set; }
        public int KeysAffected { get; set; }
        public int CurrentKeyCount { get; set; }
        public int KeySlot { get; set; }
        public string? Error { get; set; }
    }

    public class KeyCountResult
    {
        public bool Success { get; set; }
        public int KeyCount { get; set; }
        public int MaxKeys { get; set; } = 8;
        public string? Error { get; set; }
    }

    public class GatewayResult
    {
        public bool Success { get; set; }
        public int SessionDurationSeconds { get; set; }
        public string? Error { get; set; }
    }

    public class DtcResult
    {
        public bool Success { get; set; }
        public string[] Dtcs { get; set; } = Array.Empty<string>();
        public int DtcCount { get; set; }
        public string? Error { get; set; }
    }

    public class J2534ProgressEventArgs : EventArgs
    {
        public string Message { get; }
        public int Percent { get; }

        public J2534ProgressEventArgs(string message, int percent)
        {
            Message = message;
            Percent = percent;
        }
    }

    #endregion
}
