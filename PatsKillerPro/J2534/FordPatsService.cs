using System;
using System.Text;
using System.Threading.Tasks;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Ford PATS Service - High-level key programming operations
    /// Handles BCM, PCM, ABS communication for Ford vehicles
    /// </summary>
    public class FordPatsService
    {
        private readonly J2534Api _api;
        private readonly FordUdsProtocol _uds;

        // Ford Module Addresses
        public static class ModuleAddress
        {
            public const uint BCM = 0x726;
            public const uint PCM = 0x7E0;
            public const uint ABS = 0x760;
            public const uint IPC = 0x720;
            public const uint APIM = 0x7D0;
            public const uint GWM = 0x716;  // Gateway Module for 2020+
        }

        // State
        public string? CurrentVin { get; private set; }
        public string? CurrentOutcode { get; private set; }
        public VehicleInfo? CurrentVehicle { get; private set; }
        public int KeyCount { get; private set; }
        public bool IsSecurityUnlocked { get; private set; }
        public double BatteryVoltage { get; private set; }

        public FordPatsService(J2534Api api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _uds = new FordUdsProtocol(api);
        }

        #region Connection

        public async Task<bool> ConnectAsync()
        {
            return await Task.Run(() =>
            {
                var result = _uds.Connect();
                return result == J2534Error.STATUS_NOERROR;
            });
        }

        public void Disconnect()
        {
            _uds.Disconnect();
            IsSecurityUnlocked = false;
        }

        #endregion

        #region Vehicle Reading

        public async Task<VehicleInfo?> ReadVehicleInfoAsync()
        {
            return await Task.Run(() =>
            {
                await Task.Delay(10); // Async placeholder
                _uds.SetTargetModule(ModuleAddress.BCM, ModuleAddress.BCM + 8);

                // Read VIN (DID F190)
                var vinResponse = _uds.ReadDataByIdentifier(0xF190);
                if (vinResponse.Success && vinResponse.Data != null && vinResponse.Data.Length >= 20)
                {
                    // Skip first 3 bytes (service ID, DID)
                    CurrentVin = Encoding.ASCII.GetString(vinResponse.Data, 3, 17).Trim();
                }

                // Decode vehicle from VIN
                if (!string.IsNullOrEmpty(CurrentVin) && CurrentVin.Length >= 11)
                {
                    CurrentVehicle = DecodeVehicle(CurrentVin);
                }

                // Read battery voltage
                BatteryVoltage = _uds.ReadBatteryVoltage();

                return CurrentVehicle;
            });
        }

        public async Task<string?> ReadOutcodeAsync(string module = "BCM")
        {
            return await Task.Run(() =>
            {
                await Task.Delay(10); // Async placeholder
                uint address = module.ToUpper() switch
                {
                    "BCM" => ModuleAddress.BCM,
                    "PCM" => ModuleAddress.PCM,
                    "ABS" => ModuleAddress.ABS,
                    _ => ModuleAddress.BCM
                };

                _uds.SetTargetModule(address, address + 8);

                // Enter extended session
                var sessResp = _uds.DiagnosticSessionControl(DiagnosticSession.Extended);
                if (!sessResp.Success) return null;

                // Request security seed (outcode)
                var seedResp = _uds.SecurityAccessRequestSeed(0x01);
                if (!seedResp.Success || seedResp.Data == null) return null;

                // Extract outcode from seed (typically bytes 2-5 or 2-9)
                if (seedResp.Data.Length >= 6)
                {
                    var outcodeBytes = new byte[4];
                    Array.Copy(seedResp.Data, 2, outcodeBytes, 0, 4);
                    CurrentOutcode = BytesToHex(outcodeBytes);
                    return CurrentOutcode;
                }

                return null;
            });
        }

        #endregion

        #region Security Access

        public async Task<bool> SubmitIncodeAsync(string module, string incode)
        {
            return await Task.Run(() =>
            {
                await Task.Delay(10); // Async placeholder
                uint address = module.ToUpper() switch
                {
                    "BCM" => ModuleAddress.BCM,
                    "PCM" => ModuleAddress.PCM,
                    "ABS" => ModuleAddress.ABS,
                    _ => ModuleAddress.BCM
                };

                _uds.SetTargetModule(address, address + 8);

                // Convert incode to bytes
                var incodeBytes = HexStringToBytes(incode);
                if (incodeBytes == null || incodeBytes.Length == 0) return false;

                // Send security key
                var keyResp = _uds.SecurityAccessSendKey(incodeBytes, 0x02);
                if (keyResp.Success)
                {
                    IsSecurityUnlocked = true;
                }
                return keyResp.Success;
            });
        }

        #endregion

        #region Key Operations

        public async Task<int> ReadKeyCountAsync()
        {
            return await Task.Run(() =>
            {
                await Task.Delay(10); // Async placeholder
                _uds.SetTargetModule(ModuleAddress.BCM, ModuleAddress.BCM + 8);

                // Read key count DID (Ford specific: DE00 or similar)
                var response = _uds.ReadDataByIdentifier(0xDE00);
                if (response.Success && response.Data != null && response.Data.Length >= 4)
                {
                    KeyCount = response.Data[3];
                    return KeyCount;
                }
                return 0;
            });
        }

        public async Task<bool> InitializePatsAsync()
        {
            return await Task.Run(() =>
            {
                await Task.Delay(10); // Async placeholder
                _uds.SetTargetModule(ModuleAddress.BCM, ModuleAddress.BCM + 8);

                // Start PATS programming routine
                var response = _uds.RoutineControl(RoutineControlType.StartRoutine, 0x0203);
                return response.Success;
            });
        }

        public async Task<bool> EraseAllKeysAsync()
        {
            return await Task.Run(() =>
            {
                if (!IsSecurityUnlocked) return false;

                _uds.SetTargetModule(ModuleAddress.BCM, ModuleAddress.BCM + 8);

                // Erase keys routine (Ford: 0x0205)
                var response = _uds.RoutineControl(RoutineControlType.StartRoutine, 0x0205);
                if (response.Success)
                {
                    KeyCount = 0;
                }
                return response.Success;
            });
        }

        public async Task<bool> ProgramKeyAsync(int keySlot)
        {
            return await Task.Run(() =>
            {
                if (!IsSecurityUnlocked) return false;

                _uds.SetTargetModule(ModuleAddress.BCM, ModuleAddress.BCM + 8);

                // Program key routine (Ford: 0x0204)
                var response = _uds.RoutineControl(RoutineControlType.StartRoutine, 0x0204, new byte[] { (byte)keySlot });
                if (response.Success)
                {
                    KeyCount++;
                }
                return response.Success;
            });
        }

        #endregion

        #region DTCs

        public async Task<string[]> ReadDtcsAsync()
        {
            return await Task.Run(() =>
            {
                await Task.Delay(10); // Async placeholder
                _uds.SetTargetModule(ModuleAddress.BCM, ModuleAddress.BCM + 8);

                var response = _uds.ReadDtcInformation(DtcReportType.ReportDtcByStatusMask, 0xFF);
                if (!response.Success || response.Data == null) return Array.Empty<string>();

                var dtcs = new System.Collections.Generic.List<string>();
                // Parse DTC data (format: each DTC is 4 bytes)
                for (int i = 3; i + 3 < response.Data.Length; i += 4)
                {
                    var dtcCode = $"{response.Data[i]:X2}{response.Data[i + 1]:X2}-{response.Data[i + 2]:X2}";
                    dtcs.Add(dtcCode);
                }
                return dtcs.ToArray();
            });
        }

        public async Task<bool> ClearCrashEventAsync()
        {
            return await Task.Run(() =>
            {
                await Task.Delay(10); // Async placeholder
                _uds.SetTargetModule(ModuleAddress.BCM, ModuleAddress.BCM + 8);
                var response = _uds.ClearDtc();
                return response.Success;
            });
        }

        #endregion

        #region Gateway (2020+)

        public async Task<bool> UnlockGatewayAsync(string incode)
        {
            return await Task.Run(() =>
            {
                await Task.Delay(10); // Async placeholder
                _uds.SetTargetModule(ModuleAddress.GWM, ModuleAddress.GWM + 8);

                // Extended session
                var sessResp = _uds.DiagnosticSessionControl(DiagnosticSession.Extended);
                if (!sessResp.Success) return false;

                // Request seed
                var seedResp = _uds.SecurityAccessRequestSeed(0x01);
                if (!seedResp.Success) return false;

                // Convert incode and send key
                var incodeBytes = HexStringToBytes(incode);
                if (incodeBytes == null) return false;

                var keyResp = _uds.SecurityAccessSendKey(incodeBytes, 0x02);
                return keyResp.Success;
            });
        }

        public async Task<PatsStatus> ReadPatsStatusAsync()
        {
            return await Task.Run(() =>
            {
                await Task.Delay(10); // Async placeholder
                _uds.SetTargetModule(ModuleAddress.BCM, ModuleAddress.BCM + 8);

                var status = new PatsStatus();
                
                // Read various status DIDs
                var keyCountResp = _uds.ReadDataByIdentifier(0xDE00);
                if (keyCountResp.Success && keyCountResp.Data != null && keyCountResp.Data.Length >= 4)
                {
                    status.KeyCount = keyCountResp.Data[3];
                    status.MaxKeys = 8;
                }

                return status;
            });
        }

        #endregion

        #region Helpers

        private VehicleInfo DecodeVehicle(string vin)
        {
            var info = new VehicleInfo();

            // Position 10 = model year
            if (vin.Length >= 10)
            {
                info.Year = DecodeModelYear(vin[9]);
                info.Is2020Plus = info.Year >= 2020;
            }

            // Position 4-8 = model info (simplified)
            if (vin.Length >= 8)
            {
                var modelCode = vin.Substring(3, 5);
                info.Model = DecodeModel(modelCode);
            }

            return info;
        }

        private int DecodeModelYear(char c)
        {
            return c switch
            {
                'L' => 2020, 'M' => 2021, 'N' => 2022, 'P' => 2023,
                'R' => 2024, 'S' => 2025, 'T' => 2026,
                'J' => 2018, 'K' => 2019,
                'H' => 2017, 'G' => 2016, 'F' => 2015, 'E' => 2014,
                _ => 2020
            };
        }

        private string DecodeModel(string code)
        {
            // Simplified model decoding
            if (code.StartsWith("U5")) return "Explorer";
            if (code.StartsWith("K8")) return "Escape";
            if (code.StartsWith("W1")) return "F-150";
            if (code.StartsWith("A1")) return "Mustang";
            if (code.StartsWith("P3")) return "Expedition";
            return "Ford Vehicle";
        }

        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.AppendFormat("{0:X2}", b);
            return sb.ToString();
        }

        private static byte[]? HexStringToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            hex = hex.Replace(" ", "").Replace("-", "");
            if (hex.Length % 2 != 0) return null;

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        #endregion
    }

    #region Supporting Classes

    public class VehicleInfo
    {
        public int Year { get; set; }
        public string Model { get; set; } = "";
        public bool Is2020Plus { get; set; }

        public override string ToString() => $"{Year} {Model}";
    }

    public class PatsStatus
    {
        public int KeyCount { get; set; }
        public int MaxKeys { get; set; } = 8;
        public bool TheftIndicator { get; set; }
        public bool SecurityLocked { get; set; }
    }

    #endregion
}
