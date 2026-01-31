using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PatsKillerPro.J2534;

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

        public Action<string>? Log { get; set; }

        public bool IsBusy => _isBusy;
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

                // Map from FordPatsService's VehicleInfo to our result
                var info = new VehicleInfoData
                {
                    Year = patsInfo.ModelYear,
                    Model = patsInfo.Model ?? "",
                    Is2020Plus = patsInfo.ModelYear >= 2020
                };

                return VehicleInfoResult.Ok(info, _patsService.CurrentVin ?? "", _patsService.BatteryVoltage);
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

        // ---------------- Data Types ----------------

        /// <summary>
        /// Vehicle information data transfer object
        /// </summary>
        public sealed class VehicleInfoData
        {
            public int Year { get; set; }
            public string Model { get; set; } = "";
            public bool Is2020Plus { get; set; }
            public override string ToString() => $"{Year} {Model}";
        }

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

        public record VehicleInfoResult(bool Success, VehicleInfoData? VehicleInfo, string? Vin, double BatteryVoltage, string? Error = null)
        {
            public static VehicleInfoResult Ok(VehicleInfoData info, string vin, double battery) => new(true, info, vin, battery);
            public static VehicleInfoResult Fail(string error) => new(false, null, null, 0, error);
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
