using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PatsKillerPro.J2534;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// J2534 Service for PatsKiller Pro Desktop Application
    /// Wraps the J2534 library for use by MainForm
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
        public string? CurrentOutcode => _patsService?.CurrentOutcode;
        public VehicleInfo? CurrentVehicle => _patsService?.CurrentVehicle;
        public double BatteryVoltage => _patsService?.BatteryVoltage ?? 0;
        public string? ConnectedDeviceName => _connectedDevice?.Name;
        public int KeyCount => _patsService?.KeyCount ?? 0;
        public bool IsSecurityUnlocked => _patsService?.IsSecurityUnlocked ?? false;

        private J2534Service() { }

        #region Device Scanning

        public Task<List<J2534DeviceInfo>> ScanForDevicesAsync()
        {
            return Task.Run(() =>
            {
                Log("Scanning for J2534 devices...");
                var devices = J2534DeviceScanner.ScanForDevices();
                Log($"Found {devices.Count} device(s)");
                foreach (var d in devices)
                    Log($"  - {d.Name} ({d.Vendor})");
                return devices;
            });
        }

        public J2534DeviceInfo? GetFirstAvailableDevice()
        {
            return J2534DeviceScanner.GetFirstAvailableDevice();
        }

        #endregion

        #region Connection

        public async Task<J2534Result> ConnectDeviceAsync(J2534DeviceInfo device)
        {
            try
            {
                Log($"Connecting to {device.Name}...");
                
                _api = new J2534Api(device.FunctionLibrary);
                _patsService = new FordPatsService(_api);
                
                var connected = await _patsService.ConnectAsync();
                if (connected)
                {
                    _connectedDevice = device;
                    Log($"Connected to {device.Name}");
                    return J2534Result.Ok();
                }
                else
                {
                    _api?.Dispose();
                    _api = null;
                    _patsService = null;
                    return J2534Result.Fail("Connection failed");
                }
            }
            catch (Exception ex)
            {
                Log($"Connection error: {ex.Message}");
                _api?.Dispose();
                _api = null;
                _patsService = null;
                return J2534Result.Fail(ex.Message);
            }
        }

        public Task DisconnectDeviceAsync()
        {
            return Task.Run(() =>
            {
                _patsService?.Disconnect();
                _api?.Dispose();
                _api = null;
                _patsService = null;
                _connectedDevice = null;
                Log("Disconnected");
            });
        }

        #endregion

        #region Vehicle Operations

        public async Task<VehicleReadResult> ReadVehicleAsync()
        {
            if (_patsService == null) return new VehicleReadResult { Success = false, Error = "Not connected" };

            try
            {
                Log("Reading vehicle...");
                var vehicle = await _patsService.ReadVehicleInfoAsync();
                var outcode = await _patsService.ReadOutcodeAsync();

                return new VehicleReadResult
                {
                    Success = true,
                    Vin = _patsService.CurrentVin,
                    Outcode = outcode,
                    VehicleInfo = vehicle,
                    BatteryVoltage = _patsService.BatteryVoltage
                };
            }
            catch (Exception ex)
            {
                return new VehicleReadResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<OutcodeResult> ReadModuleOutcodeAsync(string module)
        {
            if (_patsService == null) return new OutcodeResult { Success = false, Error = "Not connected" };

            try
            {
                var outcode = await _patsService.ReadOutcodeAsync(module);
                return new OutcodeResult { Success = !string.IsNullOrEmpty(outcode), Outcode = outcode };
            }
            catch (Exception ex)
            {
                return new OutcodeResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<J2534Result> SubmitIncodeAsync(string module, string incode)
        {
            if (_patsService == null) return J2534Result.Fail("Not connected");

            try
            {
                var result = await _patsService.SubmitIncodeAsync(module, incode);
                return result ? J2534Result.Ok() : J2534Result.Fail("Incode rejected");
            }
            catch (Exception ex)
            {
                return J2534Result.Fail(ex.Message);
            }
        }

        #endregion

        #region Key Operations

        public async Task<KeyOperationResult> EraseAllKeysAsync(string incode)
        {
            if (_patsService == null) return new KeyOperationResult { Success = false, Error = "Not connected" };

            try
            {
                Log("Erasing all keys...");
                var result = await _patsService.EraseAllKeysAsync();
                var keyCount = await _patsService.ReadKeyCountAsync();
                return new KeyOperationResult 
                { 
                    Success = result, 
                    CurrentKeyCount = keyCount,
                    KeysAffected = result ? 8 : 0 
                };
            }
            catch (Exception ex)
            {
                return new KeyOperationResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<KeyOperationResult> ProgramKeyAsync(string incode, int slot)
        {
            if (_patsService == null) return new KeyOperationResult { Success = false, Error = "Not connected" };

            try
            {
                Log($"Programming key slot {slot}...");
                var result = await _patsService.ProgramKeyAsync(slot);
                var keyCount = await _patsService.ReadKeyCountAsync();
                return new KeyOperationResult 
                { 
                    Success = result, 
                    CurrentKeyCount = keyCount,
                    KeysAffected = result ? 1 : 0
                };
            }
            catch (Exception ex)
            {
                return new KeyOperationResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<KeyCountResult> ReadKeyCountAsync()
        {
            if (_patsService == null) return new KeyCountResult { Success = false };

            try
            {
                var count = await _patsService.ReadKeyCountAsync();
                return new KeyCountResult { Success = true, KeyCount = count, MaxKeys = 8 };
            }
            catch
            {
                return new KeyCountResult { Success = false };
            }
        }

        #endregion

        #region Gateway Operations

        public Task<bool> RequiresGatewayUnlockAsync()
        {
            return Task.FromResult(_patsService?.CurrentVehicle?.Is2020Plus ?? false);
        }

        public Task<GatewayResult> CheckGatewayAsync()
        {
            var hasGateway = _patsService?.CurrentVehicle?.Is2020Plus ?? false;
            return Task.FromResult(new GatewayResult 
            { 
                Success = true, 
                HasGateway = hasGateway 
            });
        }

        public async Task<GatewayResult> UnlockGatewayAsync(string incode)
        {
            if (_patsService == null) return new GatewayResult { Success = false, Error = "Not connected" };

            try
            {
                Log("Unlocking gateway...");
                var result = await _patsService.UnlockGatewayAsync(incode);
                return new GatewayResult { Success = result, SessionDurationSeconds = result ? 600 : 0 };
            }
            catch (Exception ex)
            {
                return new GatewayResult { Success = false, Error = ex.Message };
            }
        }

        #endregion

        #region Utility Operations

        public async Task<double> ReadBatteryVoltageAsync()
        {
            return await Task.FromResult(_patsService?.BatteryVoltage ?? 0);
        }

        public async Task<J2534Result> ClearCrashFlagAsync()
        {
            if (_patsService == null) return J2534Result.Fail("Not connected");
            var result = await _patsService.ClearCrashEventAsync();
            return result ? J2534Result.Ok() : J2534Result.Fail("Clear failed");
        }

        public Task<J2534Result> ClearTheftFlagAsync()
        {
            return ClearCrashFlagAsync(); // Same operation
        }

        public async Task<J2534Result> RestoreBcmDefaultsAsync()
        {
            if (_patsService == null) return J2534Result.Fail("Not connected");
            // TODO: Implement BCM defaults restore
            await Task.Delay(100);
            return J2534Result.Ok();
        }

        public async Task<DtcResult> ReadDtcsAsync()
        {
            if (_patsService == null) return new DtcResult { Success = false };

            try
            {
                var dtcs = await _patsService.ReadDtcsAsync();
                return new DtcResult { Success = true, Dtcs = dtcs, DtcCount = dtcs.Length };
            }
            catch
            {
                return new DtcResult { Success = false };
            }
        }

        public async Task<J2534Result> ClearDtcsAsync()
        {
            if (_patsService == null) return J2534Result.Fail("Not connected");
            var result = await _patsService.ClearCrashEventAsync();
            return result ? J2534Result.Ok() : J2534Result.Fail("Clear DTCs failed");
        }

        public async Task<J2534Result> InitializePatsAsync()
        {
            if (_patsService == null) return J2534Result.Fail("Not connected");
            var result = await _patsService.InitializePatsAsync();
            return result ? J2534Result.Ok() : J2534Result.Fail("Init failed");
        }

        public async Task<J2534Result> VehicleResetAsync()
        {
            await Task.Delay(100);
            return J2534Result.Ok();
        }

        #endregion

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _patsService?.Disconnect();
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

        public static J2534Result Ok() => new() { Success = true };
        public static J2534Result Fail(string error) => new() { Success = false, Error = error };
    }

    public class VehicleReadResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Vin { get; set; }
        public string? Outcode { get; set; }
        public VehicleInfo? VehicleInfo { get; set; }
        public double BatteryVoltage { get; set; }
    }

    public class OutcodeResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Outcode { get; set; }
    }

    public class KeyOperationResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int CurrentKeyCount { get; set; }
        public int KeysAffected { get; set; }
    }

    public class KeyCountResult
    {
        public bool Success { get; set; }
        public int KeyCount { get; set; }
        public int MaxKeys { get; set; } = 8;
    }

    public class GatewayResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int SessionDurationSeconds { get; set; }
        public bool HasGateway { get; set; }
    }

    public class DtcResult
    {
        public bool Success { get; set; }
        public string[] Dtcs { get; set; } = Array.Empty<string>();
        public int DtcCount { get; set; }
    }

    public class J2534ProgressEventArgs : EventArgs
    {
        public string Operation { get; set; } = "";
        public int Progress { get; set; }
        public int Total { get; set; }
        public string? Message { get; set; }
    }

    #endregion
}