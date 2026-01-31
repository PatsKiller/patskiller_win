using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PatsKillerPro.J2534;
using PatsKillerPro.Vehicle;

namespace PatsKillerPro.Services
{
    public sealed class J2534Service
    {
        public static J2534Service Instance { get; } = new J2534Service();

        private readonly SemaphoreSlim _opLock = new(1, 1);
        private bool _isBusy;

        private J2534Api? _api;
        private int _channelId = 0;
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

            // Common "works 7/10 times" culprits: timeouts, no response, bus glitches.
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

            // should never hit
            if (lastEx != null) throw lastEx;
            throw new InvalidOperationException("Retry loop exited unexpectedly.");
        }

        public Task<DeviceListResult> ListDevicesAsync() => RunExclusiveAsync(async () =>
        {
            try
            {
                Log?.Invoke("Scanning for J2534 devices...");

                var scanner = new J2534DeviceScanner();
                var devices = scanner.GetDevices().ToList();

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
                Log?.Invoke($"Opening device: {device.Name} ({device.DllPath})");

                _api = new J2534Api();
                _api.LoadDllAndOpenDevice(device.DllPath);

                // ISO15765, 500kbps (most HS CAN). Bus-specific variants are handled deeper.
                _channelId = _api.PassThruConnect(Protocols.ISO15765, 500000);

                _patsService = new FordPatsService(_api, device);
                _patsService.SetChannel(_channelId);

                // Retry the first handshake; the first attempt can fail if the adapter is "waking up".
                var patsConnect = await WithRetriesAsync(
                    () => _patsService.ConnectAsync(),
                    r => !r.Success && IsTransientError(r.Error),
                    maxAttempts: 3,
                    baseDelayMs: 300
                ).ConfigureAwait(false);

                if (!patsConnect.Success)
                    return OperationResult.Fail(patsConnect.Error ?? "PATS service connect failed.");

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
                if (_api != null)
                {
                    try
                    {
                        if (_channelId != 0)
                            _api.PassThruDisconnect(_channelId);
                    }
                    catch { /* ignore */ }

                    try { _api.PassThruCloseDevice(); } catch { /* ignore */ }
                }

                _channelId = 0;
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

                var result = await WithRetriesAsync(
                    () => _patsService.ReadVehicleInfoAsync(),
                    r => !r.Success && IsTransientError(r.Error),
                    maxAttempts: 2,
                    baseDelayMs: 250
                ).ConfigureAwait(false);

                if (!result.Success || result.VehicleInfo == null)
                    return VehicleInfoResult.Fail(result.Error ?? "Unable to read vehicle info.");

                return VehicleInfoResult.Ok(result.VehicleInfo, _patsService.CurrentVin, _patsService.BatteryVoltage);
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

                var r = await WithRetriesAsync(
                    () => _patsService.ReadOutcodeAsync(),
                    x => !x.Success && IsTransientError(x.Error),
                    maxAttempts: 3,
                    baseDelayMs: 300
                ).ConfigureAwait(false);

                if (!r.Success || string.IsNullOrWhiteSpace(r.Outcode))
                    return OutcodeResult.Fail(r.Error ?? "Failed to read outcode.");

                return OutcodeResult.Ok(r.Outcode);
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

                var r = await WithRetriesAsync(
                    () => _patsService.SubmitIncodeAsync(module, incode),
                    x => !x.Success && IsTransientError(x.Error),
                    maxAttempts: 2,
                    baseDelayMs: 300
                ).ConfigureAwait(false);

                return r.Success ? OperationResult.Ok() : OperationResult.Fail(r.Error ?? "Incode rejected.");
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

                var r = await WithRetriesAsync(
                    () => _patsService.ProgramKeyAsync(incode, slot),
                    x => !x.Success && IsTransientError(x.Error),
                    maxAttempts: 2,
                    baseDelayMs: 400
                ).ConfigureAwait(false);

                return r.Success
                    ? KeyOperationResult.Ok(r.CurrentKeyCount)
                    : KeyOperationResult.Fail(r.Error ?? "Key programming failed.");
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

                var r = await WithRetriesAsync(
                    () => _patsService.EraseAllKeysAsync(incode),
                    x => !x.Success && IsTransientError(x.Error),
                    maxAttempts: 2,
                    baseDelayMs: 400
                ).ConfigureAwait(false);

                return r.Success
                    ? KeyOperationResult.Ok(r.CurrentKeyCount)
                    : KeyOperationResult.Fail(r.Error ?? "Erase failed.");
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

                var r = await WithRetriesAsync(
                    () => _patsService.ReadKeyCountAsync(),
                    x => !x.Success && IsTransientError(x.Error),
                    maxAttempts: 2,
                    baseDelayMs: 250
                ).ConfigureAwait(false);

                return r.Success
                    ? KeyCountResult.Ok(r.KeyCount)
                    : KeyCountResult.Fail(r.Error ?? "Failed to read key count.");
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

                var r = await WithRetriesAsync(
                    () => _patsService.UnlockGatewayAsync(incode),
                    x => !x.Success && IsTransientError(x.Error),
                    maxAttempts: 2,
                    baseDelayMs: 400
                ).ConfigureAwait(false);

                return r.Success
                    ? GatewayResult.Ok(r.HasGateway)
                    : GatewayResult.Fail(r.Error ?? "Gateway op failed.");
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

                var r = await WithRetriesAsync(
                    () => _patsService.ClearCrashEventAsync(),
                    x => !x.Success && IsTransientError(x.Error),
                    maxAttempts: 2,
                    baseDelayMs: 300
                ).ConfigureAwait(false);

                return r.Success ? OperationResult.Ok() : OperationResult.Fail(r.Error ?? "Crash clear failed.");
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

                var r = await WithRetriesAsync(
                    () => _patsService.ReadDtcsAsync(),
                    x => !x.Success && IsTransientError(x.Error),
                    maxAttempts: 2,
                    baseDelayMs: 300
                ).ConfigureAwait(false);

                return r.Success
                    ? DtcResult.Ok(r.Dtcs ?? new List<string>())
                    : DtcResult.Fail(r.Error ?? "DTC read failed.");
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

                var r = await WithRetriesAsync(
                    () => _patsService.InitializePatsAsync(),
                    x => !x.Success && IsTransientError(x.Error),
                    maxAttempts: 2,
                    baseDelayMs: 400
                ).ConfigureAwait(false);

                return r.Success ? OperationResult.Ok() : OperationResult.Fail(r.Error ?? "Init failed.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail(ex.Message);
            }
        });

        // ---------------- Results ----------------

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

        public record VehicleInfoResult(bool Success, VehicleInfo? VehicleInfo, string? Vin, double BatteryVoltage, string? Error = null)
        {
            public static VehicleInfoResult Ok(VehicleInfo info, string vin, double battery) => new(true, info, vin, battery);
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

        public record KeyCountResult(bool Success, int KeyCount = 0, string? Error = null)
        {
            public static KeyCountResult Ok(int count) => new(true, count);
            public static KeyCountResult Fail(string error) => new(false, 0, error);
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
