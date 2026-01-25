using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Ford PATS (Passive Anti-Theft System) Service
    /// Replicates EZimmo functionality for Ford key programming
    /// 
    /// Supported Vehicles:
    /// - Ford Focus 3 (C346) 2011-2018
    /// - Ford Focus 3+ (C519) 2018+
    /// - Ford Fiesta MK7 2008-2017
    /// - Ford Transit 2014-2020
    /// - Ford Mondeo/Fusion 2013-2020
    /// - Ford M5 (FNA) 2021+ (40-char outcode)
    /// - Ford Ranger 2012-2020
    /// - Ford F-150, Escape, Explorer, etc.
    /// </summary>
    public class FordPatsService : IDisposable
    {
        private readonly J2534Api _j2534Api;
        private readonly FordUdsProtocol _uds;
        private System.Timers.Timer? _keepAliveTimer;
        private bool _isConnected;
        private bool _securityUnlocked;

        // Events
        public event EventHandler<string>? LogMessage;
        public event EventHandler<double>? VoltageChanged;
        public event EventHandler<PatsProgressEventArgs>? ProgressChanged;

        // Current state
        public string? CurrentVin { get; private set; }
        public VehicleInfo? CurrentVehicle { get; private set; }
        public string? CurrentOutcode { get; private set; }
        public int KeyCount { get; private set; }
        public double BatteryVoltage { get; private set; }
        public bool IsSecurityUnlocked => _securityUnlocked;

        public FordPatsService(J2534Api j2534Api)
        {
            _j2534Api = j2534Api ?? throw new ArgumentNullException(nameof(j2534Api));
            _uds = new FordUdsProtocol(j2534Api);
        }

        #region Connection

        /// <summary>
        /// Connect to vehicle on HS-CAN (500K)
        /// </summary>
        public async Task<PatsResult> ConnectAsync(uint baudRate = 500000)
        {
            try
            {
                Log($"Connecting to vehicle at {baudRate} baud...");
                await Task.Delay(10); // Small delay for initialization

                var result = _uds.Connect(baudRate);
                if (result != J2534Error.STATUS_NOERROR)
                    return PatsResult.Fail($"Failed to connect: {result}");

                // Set BCM as default target
                result = _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                if (result != J2534Error.STATUS_NOERROR)
                    return PatsResult.Fail($"Failed to set filter: {result}");

                _isConnected = true;

                // Start keep-alive timer
                StartKeepAlive();

                // Read battery voltage
                BatteryVoltage = _uds.ReadBatteryVoltage();
                VoltageChanged?.Invoke(this, BatteryVoltage);

                // Enter extended diagnostic session
                var response = _uds.DiagnosticSessionControl(DiagnosticSession.Extended);
                if (!response.Success)
                {
                    Log($"Warning: Could not enter extended session: {response.GetErrorMessage()}");
                }

                Log($"Connected successfully. Battery: {BatteryVoltage:F1}V");
                return PatsResult.Ok();
            }
            catch (Exception ex)
            {
                return PatsResult.Fail($"Connection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnect from vehicle
        /// </summary>
        public void Disconnect()
        {
            StopKeepAlive();
            _uds.Disconnect();
            _isConnected = false;
            _securityUnlocked = false;
            CurrentVin = null;
            CurrentOutcode = null;
            Log("Disconnected from vehicle");
        }

        private void StartKeepAlive()
        {
            _keepAliveTimer?.Stop();
            _keepAliveTimer = new System.Timers.Timer(2000); // Every 2 seconds
            _keepAliveTimer.Elapsed += (s, e) =>
            {
                if (_isConnected)
                {
                    _uds.TesterPresent(true);
                    BatteryVoltage = _uds.ReadBatteryVoltage();
                    VoltageChanged?.Invoke(this, BatteryVoltage);
                }
            };
            _keepAliveTimer.Start();
        }

        private void StopKeepAlive()
        {
            _keepAliveTimer?.Stop();
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;
        }

        #endregion

        #region VIN Reading

        /// <summary>
        /// Read VIN from BCM
        /// </summary>
        public async Task<PatsResult<string>> ReadVinAsync()
        {
            try
            {
                Log("Reading VIN from BCM...");
                await Task.Delay(10); // Small delay for diagnostic communication

                var response = _uds.ReadDataByIdentifier(FordDids.VIN);
                if (!response.Success)
                    return PatsResult<string>.Fail($"Failed to read VIN: {response.GetErrorMessage()}");

                // VIN is 17 ASCII characters
                if (response.Data.Length >= 17)
                {
                    // Skip DID echo (2 bytes) if present
                    int offset = response.Data.Length > 17 ? response.Data.Length - 17 : 0;
                    CurrentVin = Encoding.ASCII.GetString(response.Data, offset, 17);
                    CurrentVehicle = VehicleInfo.FromVin(CurrentVin);

                    Log($"VIN: {CurrentVin}");
                    Log($"Vehicle: {CurrentVehicle.Year} {CurrentVehicle.Make} {CurrentVehicle.Model}");

                    return PatsResult<string>.Ok(CurrentVin);
                }

                return PatsResult<string>.Fail("Invalid VIN response");
            }
            catch (Exception ex)
            {
                return PatsResult<string>.Fail($"VIN read error: {ex.Message}");
            }
        }

        #endregion

        #region Outcode Reading

        /// <summary>
        /// Read PATS outcode from BCM
        /// This is the "seed" used to calculate the incode
        /// </summary>
        public async Task<PatsResult<string>> ReadOutcodeAsync()
        {
            try
            {
                Log("Reading PATS outcode from BCM...");
                ReportProgress("Reading outcode...", 10);
                await Task.Delay(10); // Small delay for diagnostic communication

                // Method 1: Read PrePATS outcode (0x22 C1A1)
                var response = _uds.ReadDataByIdentifier(FordDids.PATS_OUTCODE);
                if (response.Success && response.Data.Length >= 4)
                {
                    CurrentOutcode = BitConverter.ToString(response.Data).Replace("-", "");
                    Log($"Outcode (PrePATS): {CurrentOutcode}");
                    ReportProgress("Outcode read successfully", 100);
                    return PatsResult<string>.Ok(CurrentOutcode);
                }

                // Method 2: Security Access Seed Request (0x27 01)
                Log("Trying security access seed request...");
                response = _uds.SecurityAccessRequestSeed(0x01);
                if (response.Success && response.Data.Length >= 4)
                {
                    // Skip sub-function echo (1 byte)
                    int offset = response.Data.Length > 4 ? 1 : 0;
                    var seedBytes = new byte[response.Data.Length - offset];
                    Array.Copy(response.Data, offset, seedBytes, 0, seedBytes.Length);

                    CurrentOutcode = BitConverter.ToString(seedBytes).Replace("-", "");
                    Log($"Outcode (Seed): {CurrentOutcode}");
                    ReportProgress("Outcode read successfully", 100);
                    return PatsResult<string>.Ok(CurrentOutcode);
                }

                return PatsResult<string>.Fail($"Failed to read outcode: {response.GetErrorMessage()}");
            }
            catch (Exception ex)
            {
                return PatsResult<string>.Fail($"Outcode read error: {ex.Message}");
            }
        }

        /// <summary>
        /// Read outcode from specific module (BCM, PCM, ABS, RFA)
        /// </summary>
        public async Task<PatsResult<string>> ReadOutcodeFromModuleAsync(string module)
        {
            try
            {
                // Set target module
                switch (module.ToUpperInvariant())
                {
                    case "BCM":
                        _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                        break;
                    case "PCM":
                        _uds.SetTargetModule(FordModules.PCM_TX, FordModules.PCM_RX);
                        break;
                    case "ABS":
                        _uds.SetTargetModule(FordModules.ABS_TX, FordModules.ABS_RX);
                        break;
                    case "RFA":
                        _uds.SetTargetModule(FordModules.RFA_TX, FordModules.RFA_RX);
                        break;
                    default:
                        return PatsResult<string>.Fail($"Unknown module: {module}");
                }

                Log($"Reading outcode from {module}...");

                // Enter extended session
                _uds.DiagnosticSessionControl(DiagnosticSession.Extended);
                await Task.Delay(50);

                // Request seed
                var response = _uds.SecurityAccessRequestSeed(0x01);
                if (!response.Success)
                    return PatsResult<string>.Fail($"Failed to read {module} outcode: {response.GetErrorMessage()}");

                if (response.Data.Length < 4)
                    return PatsResult<string>.Fail($"Invalid {module} outcode response");

                // Skip sub-function byte
                int offset = response.Data[0] == 0x01 ? 1 : 0;
                var seedBytes = new byte[response.Data.Length - offset];
                Array.Copy(response.Data, offset, seedBytes, 0, seedBytes.Length);

                var outcode = BitConverter.ToString(seedBytes).Replace("-", "");
                Log($"{module} Outcode: {outcode}");

                // Reset to BCM
                _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);

                return PatsResult<string>.Ok(outcode);
            }
            catch (Exception ex)
            {
                // Reset to BCM on error
                _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                return PatsResult<string>.Fail($"Module outcode error: {ex.Message}");
            }
        }

        #endregion

        #region Incode Submission

        /// <summary>
        /// Submit incode to unlock security access
        /// After this, key programming operations are allowed
        /// </summary>
        public async Task<PatsResult> SubmitIncodeAsync(string incode)
        {
            try
            {
                Log($"Submitting incode: {incode}");
                ReportProgress("Submitting incode...", 20);
                await Task.Delay(10); // Small delay for diagnostic communication

                // Convert incode string to bytes
                var incodeBytes = HexStringToBytes(incode);
                if (incodeBytes == null || incodeBytes.Length == 0)
                    return PatsResult.Fail("Invalid incode format");

                // Method 1: Routine Control - PATS Access (0x31 01 71 6D)
                var response = _uds.RoutineControl(RoutineControlType.StartRoutine, FordRoutines.PATS_ACCESS, incodeBytes);
                if (response.Success)
                {
                    _securityUnlocked = true;
                    Log("Incode accepted via routine control");
                    ReportProgress("Incode accepted", 100);
                    return PatsResult.Ok();
                }

                // Method 2: Security Access Send Key (0x27 02)
                Log("Trying security access key...");
                response = _uds.SecurityAccessSendKey(incodeBytes, 0x02);
                if (response.Success)
                {
                    _securityUnlocked = true;
                    Log("Incode accepted via security access");
                    ReportProgress("Incode accepted", 100);
                    return PatsResult.Ok();
                }

                // Check for specific error codes
                if (response.NegativeResponseCode == NegativeResponseCode.InvalidKey ||
                    response.NegativeResponseCode == NegativeResponseCode.IncorrectIncode)
                {
                    return PatsResult.Fail("Invalid incode - please verify and try again");
                }

                if (response.NegativeResponseCode == NegativeResponseCode.ExceededNumberOfAttempts)
                {
                    return PatsResult.Fail("Exceeded maximum attempts - wait 10 minutes and try again");
                }

                return PatsResult.Fail($"Failed to submit incode: {response.GetErrorMessage()}");
            }
            catch (Exception ex)
            {
                return PatsResult.Fail($"Incode submission error: {ex.Message}");
            }
        }

        #endregion

        #region Key Programming

        /// <summary>
        /// Read number of programmed keys
        /// </summary>
        public async Task<PatsResult<int>> ReadKeyCountAsync()
        {
            try
            {
                Log("Reading key count...");
                await Task.Delay(10); // Small delay for diagnostic communication

                var response = _uds.ReadDataByIdentifier(FordDids.KEY_COUNT);
                if (response.Success && response.Data.Length >= 1)
                {
                    // Key count is usually in the last byte
                    KeyCount = response.Data[response.Data.Length - 1];
                    Log($"Key count: {KeyCount}");
                    return PatsResult<int>.Ok(KeyCount);
                }

                return PatsResult<int>.Fail($"Failed to read key count: {response.GetErrorMessage()}");
            }
            catch (Exception ex)
            {
                return PatsResult<int>.Fail($"Key count error: {ex.Message}");
            }
        }

        /// <summary>
        /// Program a new key to specified slot
        /// Requires security access (incode submitted first)
        /// </summary>
        public async Task<PatsResult> ProgramKeyAsync(int slot = 0)
        {
            try
            {
                if (!_securityUnlocked)
                    return PatsResult.Fail("Security access required - submit incode first");

                Log($"Programming key to slot {slot}...");
                ReportProgress("Starting key programming...", 10);

                // Start WKIP (Write Key In Progress) routine
                var data = slot > 0 ? new byte[] { (byte)slot } : null;
                var response = _uds.RoutineControl(RoutineControlType.StartRoutine, FordRoutines.WKIP, data);

                if (!response.Success)
                {
                    // Try alternative routine
                    response = _uds.RoutineControl(RoutineControlType.StartRoutine, FordRoutines.PROGRAM_KEY, data);
                }

                if (!response.Success)
                    return PatsResult.Fail($"Failed to start key programming: {response.GetErrorMessage()}");

                Log("Key programming started - present key to ignition...");
                ReportProgress("Present key to ignition...", 30);

                // Wait for key to be detected and programmed
                for (int i = 0; i < 30; i++) // 30 second timeout
                {
                    await Task.Delay(1000);
                    ReportProgress("Waiting for key...", 30 + (i * 2));

                    // Check routine results
                    response = _uds.RoutineControl(RoutineControlType.RequestRoutineResults, FordRoutines.WKIP);
                    if (response.Success)
                    {
                        // Check if programming completed
                        if (response.Data.Length >= 1 && response.Data[0] == 0x00)
                        {
                            Log("Key programmed successfully!");
                            ReportProgress("Key programmed!", 100);

                            // Update key count
                            await ReadKeyCountAsync();
                            return PatsResult.Ok();
                        }
                    }

                    // Send tester present to keep session alive
                    _uds.TesterPresent(true);
                }

                return PatsResult.Fail("Key programming timed out - no key detected");
            }
            catch (Exception ex)
            {
                return PatsResult.Fail($"Key programming error: {ex.Message}");
            }
        }

        /// <summary>
        /// Erase all programmed keys
        /// WARNING: This will remove all keys from the vehicle!
        /// </summary>
        public async Task<PatsResult> EraseAllKeysAsync()
        {
            try
            {
                if (!_securityUnlocked)
                    return PatsResult.Fail("Security access required - submit incode first");

                Log("Erasing all keys...");
                ReportProgress("Erasing all keys...", 20);

                var response = _uds.RoutineControl(RoutineControlType.StartRoutine, FordRoutines.ERASE_KEYS);
                if (!response.Success)
                    return PatsResult.Fail($"Failed to erase keys: {response.GetErrorMessage()}");

                await Task.Delay(500);

                Log("All keys erased successfully");
                ReportProgress("Keys erased", 100);
                KeyCount = 0;

                return PatsResult.Ok();
            }
            catch (Exception ex)
            {
                return PatsResult.Fail($"Key erase error: {ex.Message}");
            }
        }

        #endregion

        #region PATS Initialization

        /// <summary>
        /// Initialize PATS system (full reset)
        /// WARNING: Removes all keys and resets PATS!
        /// </summary>
        public async Task<PatsResult> InitializePatsAsync()
        {
            try
            {
                if (!_securityUnlocked)
                    return PatsResult.Fail("Security access required - submit incode first");

                Log("Initializing PATS system...");
                ReportProgress("Initializing PATS...", 10);
                await Task.Delay(10); // Small delay for diagnostic communication

                // Write PATS reset command
                var response = _uds.WriteDataByIdentifier(FordDids.PARAM_RESET, new byte[] { 0x01 });
                if (!response.Success)
                {
                    // Try routine control method
                    response = _uds.RoutineControl(RoutineControlType.StartRoutine, FordRoutines.ERASE_KEYS);
                }

                if (!response.Success)
                    return PatsResult.Fail($"Failed to initialize PATS: {response.GetErrorMessage()}");

                await Task.Delay(2000);

                Log("PATS initialized successfully");
                ReportProgress("PATS initialized", 100);
                KeyCount = 0;

                return PatsResult.Ok();
            }
            catch (Exception ex)
            {
                return PatsResult.Fail($"PATS init error: {ex.Message}");
            }
        }

        #endregion

        #region DTC Operations

        /// <summary>
        /// Clear all DTCs (broadcast to all modules)
        /// </summary>
        public async Task<PatsResult> ClearAllDtcsAsync()
        {
            try
            {
                Log("Clearing all DTCs...");
                ReportProgress("Clearing DTCs...", 20);

                // Clear on BCM
                _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                _uds.ClearDtc();
                await Task.Delay(100);

                // Clear on PCM
                _uds.SetTargetModule(FordModules.PCM_TX, FordModules.PCM_RX);
                _uds.ClearDtc();
                await Task.Delay(100);

                // Clear on ABS
                _uds.SetTargetModule(FordModules.ABS_TX, FordModules.ABS_RX);
                _uds.ClearDtc();
                await Task.Delay(100);

                // Broadcast clear
                _uds.BroadcastClearDtc();

                // Reset to BCM
                _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);

                Log("All DTCs cleared");
                ReportProgress("DTCs cleared", 100);
                return PatsResult.Ok();
            }
            catch (Exception ex)
            {
                return PatsResult.Fail($"DTC clear error: {ex.Message}");
            }
        }

        /// <summary>
        /// Read DTCs from BCM
        /// </summary>
        public async Task<PatsResult<string[]>> ReadDtcsAsync()
        {
            try
            {
                Log("Reading DTCs from BCM...");
                await Task.Delay(10); // Small delay for diagnostic communication

                var response = _uds.ReadDtcInformation(DtcReportType.ReportDtcByStatusMask, 0xFF);
                if (!response.Success)
                    return PatsResult<string[]>.Fail($"Failed to read DTCs: {response.GetErrorMessage()}");

                // Parse DTC response
                var dtcs = new System.Collections.Generic.List<string>();
                if (response.Data.Length >= 3)
                {
                    // Skip sub-function (1 byte), availability mask (1 byte), format (1 byte)
                    for (int i = 3; i < response.Data.Length - 3; i += 4)
                    {
                        var dtcBytes = new byte[] { response.Data[i], response.Data[i + 1], response.Data[i + 2] };
                        var dtc = FormatDtc(dtcBytes);
                        dtcs.Add(dtc);
                    }
                }

                Log($"Found {dtcs.Count} DTCs");
                return PatsResult<string[]>.Ok(dtcs.ToArray());
            }
            catch (Exception ex)
            {
                return PatsResult<string[]>.Fail($"DTC read error: {ex.Message}");
            }
        }

        private string FormatDtc(byte[] bytes)
        {
            if (bytes.Length < 2) return "Unknown";

            // Standard OBD-II DTC format
            char prefix = (bytes[0] >> 6) switch
            {
                0 => 'P', // Powertrain
                1 => 'C', // Chassis
                2 => 'B', // Body
                3 => 'U', // Network
                _ => 'P'
            };

            int code = ((bytes[0] & 0x3F) << 8) | bytes[1];
            return $"{prefix}{code:X4}";
        }

        #endregion

        #region BCM Operations

        /// <summary>
        /// Clear crash/crush event flag
        /// </summary>
        public async Task<PatsResult> ClearCrashEventAsync()
        {
            try
            {
                if (!_securityUnlocked)
                    return PatsResult.Fail("Security access required");

                Log("Clearing crash event flag...");
                await Task.Delay(10); // Small delay for diagnostic communication

                var response = _uds.WriteDataByIdentifier(FordDids.CRASH_EVENT, new byte[] { 0x00 });
                if (!response.Success)
                    return PatsResult.Fail($"Failed to clear crash event: {response.GetErrorMessage()}");

                Log("Crash event cleared");
                return PatsResult.Ok();
            }
            catch (Exception ex)
            {
                return PatsResult.Fail($"Clear crash error: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore BCM to factory defaults
        /// </summary>
        public async Task<PatsResult> RestoreBcmDefaultsAsync()
        {
            try
            {
                if (!_securityUnlocked)
                    return PatsResult.Fail("Security access required");

                Log("Restoring BCM to factory defaults...");
                ReportProgress("Restoring BCM defaults...", 20);

                var response = _uds.WriteDataByIdentifier(FordDids.BCM_DEFAULTS, new byte[] { 0x01 });
                if (!response.Success)
                    return PatsResult.Fail($"Failed to restore BCM defaults: {response.GetErrorMessage()}");

                await Task.Delay(2000);

                Log("BCM defaults restored");
                ReportProgress("BCM restored", 100);
                return PatsResult.Ok();
            }
            catch (Exception ex)
            {
                return PatsResult.Fail($"BCM restore error: {ex.Message}");
            }
        }

        /// <summary>
        /// Perform ECU/vehicle reset
        /// </summary>
        public async Task<PatsResult> VehicleResetAsync()
        {
            try
            {
                Log("Performing vehicle reset...");

                // Reset BCM
                _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                _uds.EcuReset(ResetType.SoftReset);
                await Task.Delay(100);

                // Reset PCM
                _uds.SetTargetModule(FordModules.PCM_TX, FordModules.PCM_RX);
                _uds.EcuReset(ResetType.SoftReset);

                // Back to BCM
                _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);

                Log("Vehicle reset complete");
                return PatsResult.Ok();
            }
            catch (Exception ex)
            {
                return PatsResult.Fail($"Vehicle reset error: {ex.Message}");
            }
        }

        #endregion

        #region Gateway Unlock (2020+ Vehicles)

        /// <summary>
        /// Check if vehicle requires gateway unlock (2020+)
        /// </summary>
        public bool RequiresGatewayUnlock()
        {
            return CurrentVehicle?.Year >= 2020;
        }

        /// <summary>
        /// Unlock security gateway for 2020+ vehicles
        /// </summary>
        public async Task<PatsResult> UnlockGatewayAsync(string incode)
        {
            try
            {
                Log("Unlocking security gateway (SGWM)...");
                await Task.Delay(10); // Small delay for diagnostic communication

                _uds.SetTargetModule(FordModules.SGWM_TX, FordModules.SGWM_RX);

                // Enter extended session
                var response = _uds.DiagnosticSessionControl(DiagnosticSession.Extended);
                if (!response.Success)
                {
                    // Gateway may not exist on this vehicle
                    _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                    return PatsResult.Fail("Gateway not responding - may not be required for this vehicle");
                }

                // Request seed
                response = _uds.SecurityAccessRequestSeed(0x01);
                if (!response.Success)
                {
                    _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                    return PatsResult.Fail($"Gateway seed request failed: {response.GetErrorMessage()}");
                }

                // Send key (incode)
                var incodeBytes = HexStringToBytes(incode);
                if (incodeBytes == null || incodeBytes.Length == 0)
                {
                    _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                    return PatsResult.Fail("Invalid incode format for gateway unlock");
                }
                response = _uds.SecurityAccessSendKey(incodeBytes, 0x02);
                if (!response.Success)
                {
                    _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                    return PatsResult.Fail($"Gateway unlock failed: {response.GetErrorMessage()}");
                }

                Log("Gateway unlocked successfully - 10 minute session started");

                // Return to BCM
                _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);

                return PatsResult.Ok();
            }
            catch (Exception ex)
            {
                _uds.SetTargetModule(FordModules.BCM_TX, FordModules.BCM_RX);
                return PatsResult.Fail($"Gateway unlock error: {ex.Message}");
            }
        }

        #endregion

        #region PATS Status

        /// <summary>
        /// Read PATS status (0xAA = success/ready)
        /// </summary>
        public async Task<PatsResult<byte>> ReadPatsStatusAsync()
        {
            try
            {
                await Task.Delay(10); // Small delay for diagnostic communication
                var response = _uds.ReadDataByIdentifier(FordDids.PATS_STATUS);
                if (response.Success && response.Data.Length >= 1)
                {
                    var status = response.Data[response.Data.Length - 1];
                    Log($"PATS Status: 0x{status:X2} ({(status == 0xAA ? "Ready" : "Not Ready")})");
                    return PatsResult<byte>.Ok(status);
                }
                return PatsResult<byte>.Fail($"Failed to read PATS status: {response.GetErrorMessage()}");
            }
            catch (Exception ex)
            {
                return PatsResult<byte>.Fail($"PATS status error: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        private void ReportProgress(string message, int percent)
        {
            ProgressChanged?.Invoke(this, new PatsProgressEventArgs(message, percent));
        }

        private byte[]? HexStringToBytes(string hex)
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

        public void Dispose()
        {
            Disconnect();
            _uds.Dispose();
        }
    }

    #region Result Classes

    public class PatsResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }

        public static PatsResult Ok() => new PatsResult { Success = true };
        public static PatsResult Fail(string error) => new PatsResult { Success = false, Error = error };
    }

    public class PatsResult<T>
    {
        public bool Success { get; set; }
        public T? Value { get; set; }
        public string? Error { get; set; }

        public static PatsResult<T> Ok(T value) => new PatsResult<T> { Success = true, Value = value };
        public static PatsResult<T> Fail(string error) => new PatsResult<T> { Success = false, Error = error };
    }

    public class PatsProgressEventArgs : EventArgs
    {
        public string Message { get; }
        public int Percent { get; }

        public PatsProgressEventArgs(string message, int percent)
        {
            Message = message;
            Percent = percent;
        }
    }

    /// <summary>
    /// Vehicle information decoded from VIN
    /// </summary>
    public class VehicleInfo
    {
        public string Vin { get; set; } = "";
        public int Year { get; set; }
        public string Make { get; set; } = "Ford";
        public string Model { get; set; } = "Unknown";
        public string Platform { get; set; } = "Unknown";
        public bool Is2020Plus => Year >= 2020;
        public bool HasKeyless { get; set; }

        public static VehicleInfo FromVin(string vin)
        {
            if (string.IsNullOrEmpty(vin) || vin.Length < 17)
                return new VehicleInfo { Vin = vin ?? "" };

            var info = new VehicleInfo { Vin = vin };

            // Year decoding (position 10, 0-indexed = 9)
            info.Year = vin[9] switch
            {
                'A' => 2010, 'B' => 2011, 'C' => 2012, 'D' => 2013,
                'E' => 2014, 'F' => 2015, 'G' => 2016, 'H' => 2017,
                'J' => 2018, 'K' => 2019, 'L' => 2020, 'M' => 2021,
                'N' => 2022, 'P' => 2023, 'R' => 2024, 'S' => 2025,
                'T' => 2026, 'V' => 2027, 'W' => 2028, 'X' => 2029,
                _ => 2020
            };

            // Model decoding (positions 4-8)
            var modelCode = vin.Substring(3, 5).ToUpperInvariant();
            (info.Model, info.Platform, info.HasKeyless) = modelCode.Substring(0, 3) switch
            {
                "P8C" or "P8G" or "P8T" => ("Mustang", "S550/S650", false),
                "W1E" or "W1C" or "X1E" => ("F-150", "P552/P702", false),
                "K8A" or "K8B" => ("Explorer", "U625", true),
                "U0G" or "U0D" or "U0E" => ("Escape", "CX482", true),
                "P0C" or "P0H" => ("Fusion", "CD4", true),
                "P3C" or "P8A" => ("Focus", "C346", true),
                "N5E" or "N5L" => ("Bronco", "U725", true),
                "M8A" or "M8E" => ("Maverick", "P758", true),
                "E2X" or "E2Y" => ("Transit", "V363", false),
                _ => ("Unknown", "Unknown", false)
            };

            return info;
        }
    }

    #endregion
}
