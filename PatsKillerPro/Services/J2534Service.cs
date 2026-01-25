using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PatsKillerPro.Services
{
    /// <summary>
    /// Service for J2534 device communication with Ford vehicles.
    /// 
    /// INTEGRATION NOTE: When the PatsKiller.J2534 library is ready, replace the
    /// simulation code in each method with actual J2534 calls. The interface
    /// and flow will remain the same.
    /// 
    /// Current Status: Simulation mode (ready for J2534 library integration)
    /// </summary>
    public class J2534Service
    {
        private static J2534Service? _instance;
        private static readonly object _lock = new object();

        public static J2534Service Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new J2534Service();
                    }
                }
                return _instance;
            }
        }

        // Connection state
        public bool IsDeviceConnected { get; private set; }
        public bool IsVehicleConnected { get; private set; }
        public string? ConnectedDeviceName { get; private set; }
        public string? CurrentVin { get; private set; }
        public string? CurrentOutcode { get; private set; }
        public VehicleInfo? CurrentVehicle { get; private set; }

        // Device handle (for real J2534 integration)
        private IntPtr _deviceHandle = IntPtr.Zero;
        private int _channelId = -1;

        private J2534Service() { }

        // ============ DEVICE MANAGEMENT ============

        /// <summary>
        /// Scan for installed J2534 devices from Windows Registry
        /// </summary>
        public async Task<List<J2534DeviceInfo>> ScanForDevicesAsync()
        {
            var devices = new List<J2534DeviceInfo>();

            await Task.Run(() =>
            {
                // Scan 32-bit registry
                ScanRegistryForDevices(devices, @"SOFTWARE\WOW6432Node\PassThruSupport.04.04");
                // Scan 64-bit registry
                ScanRegistryForDevices(devices, @"SOFTWARE\PassThruSupport.04.04");
            });

            // If no devices found in registry, add common ones for testing
            if (devices.Count == 0)
            {
                devices.Add(new J2534DeviceInfo
                {
                    Name = "VCM II",
                    Vendor = "Ford",
                    DllPath = @"C:\Program Files\Ford\VCM II\vcmii32.dll",
                    IsAvailable = true
                });
                devices.Add(new J2534DeviceInfo
                {
                    Name = "VXDIAG VCX Nano",
                    Vendor = "VXDIAG",
                    DllPath = @"C:\Program Files\VXDIAG\vxdiag.dll",
                    IsAvailable = true
                });
            }

            return devices;
        }

        private void ScanRegistryForDevices(List<J2534DeviceInfo> devices, string basePath)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(basePath);
                if (baseKey == null) return;

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var deviceKey = baseKey.OpenSubKey(subKeyName);
                        if (deviceKey == null) continue;

                        var name = deviceKey.GetValue("Name")?.ToString();
                        var vendor = deviceKey.GetValue("Vendor")?.ToString();
                        var dllPath = deviceKey.GetValue("FunctionLibrary")?.ToString();

                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(dllPath))
                        {
                            devices.Add(new J2534DeviceInfo
                            {
                                Name = name,
                                Vendor = vendor ?? "Unknown",
                                DllPath = dllPath,
                                IsAvailable = System.IO.File.Exists(dllPath)
                            });
                        }
                    }
                    catch { /* Skip invalid entries */ }
                }
            }
            catch { /* Registry path doesn't exist */ }
        }

        /// <summary>
        /// Connect to a J2534 device
        /// </summary>
        public async Task<J2534Result> ConnectDeviceAsync(J2534DeviceInfo device)
        {
            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // Replace this simulation with:
                // 
                // var dll = new J2534Dll(device.DllPath);
                // _deviceHandle = dll.PassThruOpen();
                // if (_deviceHandle == IntPtr.Zero)
                //     return new J2534Result { Success = false, Error = "Failed to open device" };
                //
                // _channelId = dll.PassThruConnect(_deviceHandle, ProtocolId.CAN, 500000);
                // if (_channelId < 0)
                //     return new J2534Result { Success = false, Error = "Failed to open CAN channel" };
                // ========================================

                await Task.Delay(800); // Simulate connection time

                IsDeviceConnected = true;
                ConnectedDeviceName = device.Name;

                return new J2534Result { Success = true };
            }
            catch (Exception ex)
            {
                return new J2534Result { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Disconnect from J2534 device
        /// </summary>
        public async Task<J2534Result> DisconnectDeviceAsync()
        {
            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // Replace with:
                // if (_channelId >= 0) dll.PassThruDisconnect(_channelId);
                // if (_deviceHandle != IntPtr.Zero) dll.PassThruClose(_deviceHandle);
                // ========================================

                await Task.Delay(200);

                IsDeviceConnected = false;
                IsVehicleConnected = false;
                ConnectedDeviceName = null;
                CurrentVin = null;
                CurrentOutcode = null;
                CurrentVehicle = null;
                _deviceHandle = IntPtr.Zero;
                _channelId = -1;

                return new J2534Result { Success = true };
            }
            catch (Exception ex)
            {
                return new J2534Result { Success = false, Error = ex.Message };
            }
        }

        // ============ VEHICLE COMMUNICATION ============

        /// <summary>
        /// Read vehicle VIN and detect vehicle type
        /// </summary>
        public async Task<VehicleReadResult> ReadVehicleAsync()
        {
            if (!IsDeviceConnected)
                return new VehicleReadResult { Success = false, Error = "Device not connected" };

            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // Replace with actual UDS VIN read:
                //
                // // Send VIN request to BCM (Service 0x22, DID 0xF190)
                // var vinRequest = new byte[] { 0x22, 0xF1, 0x90 };
                // await SendMessageAsync(ModuleAddresses.BCM_TX, vinRequest);
                // var response = await ReceiveMessageAsync(ModuleAddresses.BCM_RX, timeout: 2000);
                // 
                // if (response == null || response[0] != 0x62)
                //     return new VehicleReadResult { Success = false, Error = "No VIN response" };
                //
                // string vin = Encoding.ASCII.GetString(response, 3, 17);
                // ========================================

                await Task.Delay(1500); // Simulate CAN communication time

                // Simulate reading VIN from vehicle
                var vin = "1FA6P8CF5L5" + new Random().Next(100000, 999999).ToString();
                
                // Decode VIN to get vehicle info
                var vehicleInfo = DecodeVin(vin);
                
                // Read BCM outcode
                var outcodeResult = await ReadModuleOutcodeAsync("BCM");
                
                CurrentVin = vin;
                CurrentVehicle = vehicleInfo;
                CurrentOutcode = outcodeResult.Success ? outcodeResult.Outcode : null;
                IsVehicleConnected = true;

                return new VehicleReadResult
                {
                    Success = true,
                    Vin = vin,
                    VehicleInfo = vehicleInfo,
                    Outcode = CurrentOutcode,
                    BatteryVoltage = await ReadBatteryVoltageAsync()
                };
            }
            catch (Exception ex)
            {
                return new VehicleReadResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Read outcode from a specific module (BCM, ABS, PCM)
        /// </summary>
        public async Task<OutcodeResult> ReadModuleOutcodeAsync(string moduleName, bool useMsCan = false)
        {
            if (!IsDeviceConnected)
                return new OutcodeResult { Success = false, Error = "Device not connected" };

            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // Replace with actual outcode read:
                //
                // uint txAddress = ModuleAddresses.GetTxAddress(moduleName);
                // uint rxAddress = ModuleAddresses.GetRxAddress(moduleName);
                //
                // // Enter extended diagnostic session
                // await SendMessageAsync(txAddress, new byte[] { 0x10, 0x03 });
                // await ReceiveMessageAsync(rxAddress, 1000);
                //
                // // Request security seed (outcode)
                // await SendMessageAsync(txAddress, new byte[] { 0x27, 0x01 });
                // var response = await ReceiveMessageAsync(rxAddress, 2000);
                //
                // if (response == null || response[0] != 0x67)
                //     return new OutcodeResult { Success = false, Error = "No seed response" };
                //
                // string outcode = BitConverter.ToString(response, 2, 4).Replace("-", "");
                // ========================================

                await Task.Delay(800); // Simulate CAN communication

                // Generate realistic outcode format based on module
                string prefix = moduleName.ToUpper() switch
                {
                    "BCM" => "BC",
                    "ABS" => "AB",
                    "PCM" => "PC",
                    "IPC" => "IP",
                    "TCM" => "TC",
                    _ => "XX"
                };

                var outcode = $"{prefix}{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

                return new OutcodeResult
                {
                    Success = true,
                    Outcode = outcode,
                    ModuleName = moduleName
                };
            }
            catch (Exception ex)
            {
                return new OutcodeResult { Success = false, Error = ex.Message, ModuleName = moduleName };
            }
        }

        /// <summary>
        /// Submit incode to a module for security access
        /// </summary>
        public async Task<J2534Result> SubmitIncodeAsync(string moduleName, string incode)
        {
            if (!IsDeviceConnected)
                return new J2534Result { Success = false, Error = "Device not connected" };

            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // Replace with actual incode submission:
                //
                // uint txAddress = ModuleAddresses.GetTxAddress(moduleName);
                // uint rxAddress = ModuleAddresses.GetRxAddress(moduleName);
                //
                // // Convert incode string to bytes
                // var incodeBytes = ConvertHexStringToBytes(incode);
                //
                // // Send security key (incode)
                // var keyRequest = new byte[2 + incodeBytes.Length];
                // keyRequest[0] = 0x27;  // Security Access
                // keyRequest[1] = 0x02;  // Send Key
                // Array.Copy(incodeBytes, 0, keyRequest, 2, incodeBytes.Length);
                //
                // await SendMessageAsync(txAddress, keyRequest);
                // var response = await ReceiveMessageAsync(rxAddress, 2000);
                //
                // if (response == null || response[0] == 0x7F)
                //     return new J2534Result { Success = false, Error = "Security access denied" };
                //
                // if (response[0] != 0x67 || response[1] != 0x02)
                //     return new J2534Result { Success = false, Error = "Invalid response" };
                // ========================================

                await Task.Delay(1000); // Simulate security access time

                return new J2534Result { Success = true };
            }
            catch (Exception ex)
            {
                return new J2534Result { Success = false, Error = ex.Message };
            }
        }

        // ============ KEY OPERATIONS ============

        /// <summary>
        /// Erase all programmed keys
        /// </summary>
        public async Task<KeyOperationResult> EraseAllKeysAsync(string incode)
        {
            if (!IsVehicleConnected)
                return new KeyOperationResult { Success = false, Error = "Vehicle not connected" };

            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // Replace with actual key erase command:
                //
                // // Verify security access first
                // var secResult = await SubmitIncodeAsync("BCM", incode);
                // if (!secResult.Success) return new KeyOperationResult { Success = false, Error = "Security access failed" };
                //
                // // Send key erase command (varies by platform)
                // // For F3: Service 0x31, routine 0xF0F0
                // await SendMessageAsync(ModuleAddresses.BCM_TX, new byte[] { 0x31, 0x01, 0xF0, 0xF0 });
                // var response = await ReceiveMessageAsync(ModuleAddresses.BCM_RX, 5000);
                // ========================================

                await Task.Delay(2000); // Simulate erase time

                var keyCount = await ReadKeyCountAsync();

                return new KeyOperationResult
                {
                    Success = true,
                    KeysAffected = 3, // Number of keys erased
                    CurrentKeyCount = keyCount.KeyCount
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
        public async Task<KeyOperationResult> ProgramKeyAsync(string incode, int keySlot = 0)
        {
            if (!IsVehicleConnected)
                return new KeyOperationResult { Success = false, Error = "Vehicle not connected" };

            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // Replace with actual key programming:
                //
                // // Security access
                // var secResult = await SubmitIncodeAsync("BCM", incode);
                // if (!secResult.Success) return new KeyOperationResult { Success = false, Error = "Security access failed" };
                //
                // // Start key learn mode
                // await SendMessageAsync(ModuleAddresses.BCM_TX, new byte[] { 0x31, 0x01, 0xF0, 0xF1, (byte)keySlot });
                // 
                // // Wait for key to be presented
                // // Read key transponder
                // // Write key to slot
                // ========================================

                await Task.Delay(1500); // Simulate programming time

                var keyCount = await ReadKeyCountAsync();

                return new KeyOperationResult
                {
                    Success = true,
                    KeysAffected = 1,
                    CurrentKeyCount = keyCount.KeyCount,
                    KeySlot = keySlot
                };
            }
            catch (Exception ex)
            {
                return new KeyOperationResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Read current key count
        /// </summary>
        public async Task<KeyCountResult> ReadKeyCountAsync()
        {
            if (!IsVehicleConnected)
                return new KeyCountResult { Success = false, Error = "Vehicle not connected" };

            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // await SendMessageAsync(ModuleAddresses.BCM_TX, new byte[] { 0x22, 0x5B, 0x10 });
                // var response = await ReceiveMessageAsync(ModuleAddresses.BCM_RX, 1000);
                // int keyCount = response[3];
                // ========================================

                await Task.Delay(300);

                return new KeyCountResult
                {
                    Success = true,
                    KeyCount = new Random().Next(0, 4),
                    MaxKeys = 8
                };
            }
            catch (Exception ex)
            {
                return new KeyCountResult { Success = false, Error = ex.Message };
            }
        }

        // ============ GATEWAY OPERATIONS (2020+) ============

        /// <summary>
        /// Check if vehicle requires gateway unlock
        /// </summary>
        public async Task<bool> RequiresGatewayUnlockAsync()
        {
            if (CurrentVehicle == null) return false;

            // 2020+ Ford vehicles require gateway unlock
            if (int.TryParse(CurrentVehicle.Year, out int year))
            {
                return year >= 2020;
            }

            // ===== REAL J2534 INTEGRATION POINT =====
            // Try to communicate with SGWM (Security Gateway Module)
            // If present and responding, gateway unlock is required
            // ========================================

            await Task.Delay(100);
            return false;
        }

        /// <summary>
        /// Unlock security gateway (10-minute session)
        /// </summary>
        public async Task<GatewayResult> UnlockGatewayAsync(string incode)
        {
            if (!IsVehicleConnected)
                return new GatewayResult { Success = false, Error = "Vehicle not connected" };

            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // 
                // // Request seed from gateway
                // await SendMessageAsync(ModuleAddresses.SGWM_TX, new byte[] { 0x27, 0x01 });
                // var seedResponse = await ReceiveMessageAsync(ModuleAddresses.SGWM_RX, 2000);
                //
                // // Send calculated key
                // var keyBytes = CalculateGatewayKey(seedResponse, incode);
                // await SendMessageAsync(ModuleAddresses.SGWM_TX, new byte[] { 0x27, 0x02, ...keyBytes });
                // var keyResponse = await ReceiveMessageAsync(ModuleAddresses.SGWM_RX, 2000);
                //
                // if (keyResponse[0] != 0x67)
                //     return new GatewayResult { Success = false, Error = "Gateway unlock failed" };
                // ========================================

                await Task.Delay(2000); // Simulate gateway unlock time

                return new GatewayResult
                {
                    Success = true,
                    SessionDurationSeconds = 600 // 10 minutes
                };
            }
            catch (Exception ex)
            {
                return new GatewayResult { Success = false, Error = ex.Message };
            }
        }

        // ============ UTILITY OPERATIONS ============

        /// <summary>
        /// Read battery voltage
        /// </summary>
        public async Task<double> ReadBatteryVoltageAsync()
        {
            // ===== REAL J2534 INTEGRATION POINT =====
            // return await _device.ReadVoltageAsync();
            // ========================================

            await Task.Delay(100);
            return 12.4 + new Random().NextDouble() * 0.8; // 12.4V - 13.2V
        }

        /// <summary>
        /// Clear crash flag (DID 0x5B17)
        /// </summary>
        public async Task<J2534Result> ClearCrashFlagAsync()
        {
            if (!IsVehicleConnected)
                return new J2534Result { Success = false, Error = "Vehicle not connected" };

            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // await SendMessageAsync(ModuleAddresses.BCM_TX, new byte[] { 0x2E, 0x5B, 0x17, 0x00 });
                // var response = await ReceiveMessageAsync(ModuleAddresses.BCM_RX, 2000);
                // ========================================

                await Task.Delay(1500);
                return new J2534Result { Success = true };
            }
            catch (Exception ex)
            {
                return new J2534Result { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Clear theft flag
        /// </summary>
        public async Task<J2534Result> ClearTheftFlagAsync()
        {
            if (!IsVehicleConnected)
                return new J2534Result { Success = false, Error = "Vehicle not connected" };

            try
            {
                await Task.Delay(1500);
                return new J2534Result { Success = true };
            }
            catch (Exception ex)
            {
                return new J2534Result { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Restore BCM to factory defaults
        /// </summary>
        public async Task<J2534Result> RestoreBcmDefaultsAsync()
        {
            if (!IsVehicleConnected)
                return new J2534Result { Success = false, Error = "Vehicle not connected" };

            try
            {
                await Task.Delay(2000);
                return new J2534Result { Success = true };
            }
            catch (Exception ex)
            {
                return new J2534Result { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Read DTCs from all modules
        /// </summary>
        public async Task<DtcResult> ReadDtcsAsync()
        {
            if (!IsVehicleConnected)
                return new DtcResult { Success = false, Error = "Vehicle not connected" };

            try
            {
                await Task.Delay(1000);
                return new DtcResult
                {
                    Success = true,
                    Dtcs = new List<string> { "B1000", "U0100" },
                    DtcCount = 2
                };
            }
            catch (Exception ex)
            {
                return new DtcResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Clear all DTCs
        /// </summary>
        public async Task<J2534Result> ClearDtcsAsync()
        {
            if (!IsVehicleConnected)
                return new J2534Result { Success = false, Error = "Vehicle not connected" };

            try
            {
                // ===== REAL J2534 INTEGRATION POINT =====
                // // Broadcast clear DTCs to all modules
                // await SendMessageAsync(ModuleAddresses.BROADCAST_TX, new byte[] { 0x14, 0xFF, 0x00 });
                // ========================================

                await Task.Delay(1000);
                return new J2534Result { Success = true };
            }
            catch (Exception ex)
            {
                return new J2534Result { Success = false, Error = ex.Message };
            }
        }

        // ============ HELPERS ============

        private VehicleInfo DecodeVin(string vin)
        {
            // VIN position 10 = model year
            // VIN positions 4-8 = vehicle attributes
            var yearCode = vin.Length >= 10 ? vin[9] : '0';
            var year = DecodeModelYear(yearCode);

            // Simplified Ford model detection from VIN
            var model = "Ford Vehicle";
            if (vin.Length >= 8)
            {
                var modelCode = vin.Substring(3, 5);
                model = modelCode switch
                {
                    var c when c.StartsWith("P8") => "Ford Mustang",
                    var c when c.StartsWith("P7") => "Ford F-150",
                    var c when c.StartsWith("C4") => "Ford Escape",
                    var c when c.StartsWith("U6") => "Ford Explorer",
                    var c when c.StartsWith("V3") => "Ford Transit",
                    _ => "Ford Vehicle"
                };
            }

            return new VehicleInfo
            {
                Vin = vin,
                Year = year.ToString(),
                Make = "Ford",
                Model = model,
                Platform = DetectPlatform(vin)
            };
        }

        private int DecodeModelYear(char code)
        {
            return code switch
            {
                'A' => 2010, 'B' => 2011, 'C' => 2012, 'D' => 2013,
                'E' => 2014, 'F' => 2015, 'G' => 2016, 'H' => 2017,
                'J' => 2018, 'K' => 2019, 'L' => 2020, 'M' => 2021,
                'N' => 2022, 'P' => 2023, 'R' => 2024, 'S' => 2025,
                'T' => 2026, 'V' => 2027, 'W' => 2028, 'X' => 2029,
                _ => 2020
            };
        }

        private string DetectPlatform(string vin)
        {
            // Simplified platform detection
            if (vin.Length < 8) return "Unknown";
            
            var modelCode = vin.Substring(3, 5);
            return modelCode switch
            {
                var c when c.StartsWith("P8") => "S550",
                var c when c.StartsWith("P7") => "P702",
                var c when c.StartsWith("C4") => "CX482",
                var c when c.StartsWith("U6") => "U625",
                _ => "Generic"
            };
        }
    }

    // ============ RESULT CLASSES ============

    public class J2534DeviceInfo
    {
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public string DllPath { get; set; } = "";
        public bool IsAvailable { get; set; }
        public override string ToString() => $"{Vendor} {Name}";
    }

    public class J2534Result
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public class VehicleReadResult : J2534Result
    {
        public string? Vin { get; set; }
        public VehicleInfo? VehicleInfo { get; set; }
        public string? Outcode { get; set; }
        public double BatteryVoltage { get; set; }
    }

    public class VehicleInfo
    {
        public string Vin { get; set; } = "";
        public string Year { get; set; } = "";
        public string Make { get; set; } = "";
        public string Model { get; set; } = "";
        public string Platform { get; set; } = "";

        public bool Is2020Plus => int.TryParse(Year, out int y) && y >= 2020;

        public override string ToString() => $"{Year} {Make} {Model}";
    }

    public class OutcodeResult : J2534Result
    {
        public string? Outcode { get; set; }
        public string? ModuleName { get; set; }
    }

    public class KeyOperationResult : J2534Result
    {
        public int KeysAffected { get; set; }
        public int CurrentKeyCount { get; set; }
        public int KeySlot { get; set; }
    }

    public class KeyCountResult : J2534Result
    {
        public int KeyCount { get; set; }
        public int MaxKeys { get; set; } = 8;
    }

    public class GatewayResult : J2534Result
    {
        public int SessionDurationSeconds { get; set; }
    }

    public class DtcResult : J2534Result
    {
        public List<string> Dtcs { get; set; } = new();
        public int DtcCount { get; set; }
    }
}
