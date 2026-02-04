using System;
using System.Threading;
using PatsKillerPro.Communication;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Vehicle
{
    /// <summary>
    /// PATS (Passive Anti-Theft System) key programming operations
    /// All token costs are documented for user notification
    /// </summary>
    public class PatsOperations
    {
        private readonly UdsService _uds;

        // PATS Status Codes
        private const byte PATS_STATUS_OK = 0xAA;
        private const byte PATS_STATUS_FAILED = 0x55;

        // Token Cost Constants (for UI warnings)
        public const int TOKEN_COST_KEY_PROGRAM = 1;      // 1 token (same incode = unlimited keys)
        public const int TOKEN_COST_KEY_ERASE = 1;        // 1 token
        public const int TOKEN_COST_BCM_FACTORY = 3;      // 2-3 tokens
        public const int TOKEN_COST_CLEAR_P160A = 1;      // 1 token
        public const int TOKEN_COST_CLEAR_B10A2 = 1;      // 1 token
        public const int TOKEN_COST_CLEAR_CRASH = 1;      // 1 token
        public const int TOKEN_COST_CLEAR_KAM = 0;        // FREE
        public const int TOKEN_COST_ESCL_INIT = 1;        // 1 token
        public const int TOKEN_COST_KEYPAD_READ = 1;      // 1 token
        public const int TOKEN_COST_KEYPAD_WRITE = 1;     // 1 token
        public const int TOKEN_COST_GATEWAY_UNLOCK = 1;   // 1 token
        public const int TOKEN_COST_VEHICLE_RESET = 0;    // FREE
        public const int TOKEN_COST_CLEAR_DTCS = 0;       // FREE

        public PatsOperations(UdsService uds)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
        }

        #region Key Operations

        /// <summary>
        /// Programs new keys using the provided incode
        /// Token Cost: 1 token (same incode allows unlimited keys until outcode changes)
        /// </summary>
        public bool ProgramKeys(string incode)
        {
            if (string.IsNullOrEmpty(incode))
                throw new ArgumentException("Incode cannot be empty", nameof(incode));

            // SECURITY: Never log incodes to disk
            Logger.Info("Starting key programming [incode provided]");

            try
            {
                // Step 1: Start diagnostic session
                _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(100);

                // Step 2: Request security access
                Logger.Info("Requesting security access...");
                if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX))
                {
                    throw new Exception("Security access denied - check incode or wait for anti-scan timeout");
                }
                Thread.Sleep(100);

                // Step 3: Submit incode via routine control
                Logger.Info("Submitting security code...");
                var incodeBytes = ParseIncodeToBytes(incode);
                var routineData = new byte[3 + incodeBytes.Length];
                routineData[0] = 0x71;  // Routine identifier high
                routineData[1] = 0x6D;  // Routine identifier low
                routineData[2] = 0xCA;  // PATS incode submission
                Array.Copy(incodeBytes, 0, routineData, 3, incodeBytes.Length);

                var response = _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, routineData);
                
                if (response == null || response.Length < 4)
                {
                    throw new Exception("Invalid response from BCM");
                }

                // Step 4: Check if incode was accepted
                Thread.Sleep(200);
                var status = ReadPatsStatus();
                if (status != PATS_STATUS_OK)
                {
                    throw new Exception($"PATS incode not accepted. Status: 0x{status:X2}");
                }

                // Step 5: Execute Write Key In Progress (WKIP) routine
                Logger.Info("Executing key write routine...");
                var wkipData = new byte[] { 0x71, 0x6C };
                var wkipResponse = _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, wkipData);

                // Step 6: Wait for key programming to complete
                Logger.Info("Waiting for key programming to complete...");
                Thread.Sleep(3000);

                // Step 7: Stop the routine
                _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x02, new byte[] { 0x71, 0x6C });

                // Step 8: Verify success
                status = ReadPatsStatus();
                Logger.Info($"Key programming complete. Status: 0x{status:X2}");

                return status == PATS_STATUS_OK;
            }
            catch (Exception ex)
            {
                Logger.Error($"Key programming failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Erases all keys from the vehicle
        /// Token Cost: 1 token
        /// </summary>
        public bool EraseAllKeys(string incode)
        {
            if (string.IsNullOrEmpty(incode))
                throw new ArgumentException("Incode cannot be empty", nameof(incode));

            Logger.Info("Starting key erase operation");

            try
            {
                _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(100);

                if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX))
                {
                    throw new Exception("Security access denied");
                }
                Thread.Sleep(100);

                // Submit incode first
                var incodeBytes = ParseIncodeToBytes(incode);
                var routineData = new byte[3 + incodeBytes.Length];
                routineData[0] = 0x71;
                routineData[1] = 0x6D;
                routineData[2] = 0xCA;
                Array.Copy(incodeBytes, 0, routineData, 3, incodeBytes.Length);
                _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, routineData);

                Thread.Sleep(200);

                // Execute erase routine
                Logger.Info("Executing key erase routine...");
                var eraseData = new byte[] { 0x71, 0x6C, 0xFF };  // 0xFF = erase all
                _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, eraseData);

                Thread.Sleep(3000);

                var status = ReadPatsStatus();
                Logger.Info($"Key erase complete. Status: 0x{status:X2}");

                return status == PATS_STATUS_OK;
            }
            catch (Exception ex)
            {
                Logger.Error($"Key erase failed: {ex.Message}", ex);
                throw;
            }
        }

        #endregion

        #region BCM Operations

        /// <summary>
        /// Restores BCM to factory defaults
        /// Token Cost: 2-3 tokens
        /// WARNING: Requires scanner adaptation after completion!
        /// </summary>
        public void BcmFactoryDefaults(string[] incodes)
        {
            if (incodes == null || incodes.Length < 2)
                throw new ArgumentException("BCM Factory Defaults requires 2-3 incodes", nameof(incodes));

            Logger.Info("Starting BCM Factory Defaults operation");

            try
            {
                // Start programming session
                _uds.StartProgrammingSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(100);

                // Level 1 security access
                Logger.Info("Requesting Level 1 security access...");
                if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX, 0x01))
                {
                    throw new Exception("Level 1 security access denied");
                }
                Thread.Sleep(100);

                // Submit first incode
                var incode1Bytes = ParseIncodeToBytes(incodes[0]);
                SubmitIncode(incode1Bytes);
                Thread.Sleep(200);

                // Level 2 security access (if needed)
                if (incodes.Length >= 2)
                {
                    Logger.Info("Requesting Level 2 security access...");
                    if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX, 0x03))
                    {
                        throw new Exception("Level 2 security access denied");
                    }
                    Thread.Sleep(100);

                    var incode2Bytes = ParseIncodeToBytes(incodes[1]);
                    SubmitIncode(incode2Bytes);
                    Thread.Sleep(200);
                }

                // Level 3 security access (if needed)
                if (incodes.Length >= 3)
                {
                    Logger.Info("Requesting Level 3 security access...");
                    if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX, 0x05))
                    {
                        throw new Exception("Level 3 security access denied");
                    }
                    Thread.Sleep(100);

                    var incode3Bytes = ParseIncodeToBytes(incodes[2]);
                    SubmitIncode(incode3Bytes);
                    Thread.Sleep(200);
                }

                // Execute factory defaults routine
                Logger.Info("Executing factory defaults routine...");
                var routineData = new byte[] { 0xFF, 0x00 };  // Factory defaults routine ID
                _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, routineData);

                // Wait for operation to complete (this takes several seconds)
                Logger.Info("Waiting for factory defaults to complete...");
                Thread.Sleep(5000);

                // Reset BCM
                Logger.Info("Resetting BCM...");
                _uds.EcuReset(ModuleAddresses.BCM_TX);
                Thread.Sleep(2000);

                Logger.Info("BCM Factory Defaults completed - SCANNER ADAPTATION REQUIRED");
            }
            catch (Exception ex)
            {
                Logger.Error($"BCM Factory Defaults failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Clears P160A DTC from PCM (Calibration Parameter Reset Required)
        /// Token Cost: 1 token
        /// </summary>
        public void ClearP160A()
        {
            Logger.Info("Clearing P160A from PCM");

            try
            {
                _uds.StartExtendedSession(ModuleAddresses.PCM_TX);
                Thread.Sleep(100);

                // Request security access on PCM
                if (!_uds.RequestSecurityAccess(ModuleAddresses.PCM_TX))
                {
                    throw new Exception("PCM security access denied");
                }
                Thread.Sleep(100);

                // Clear specific DTC P160A (0x160A)
                // Service 0x14 with DTC group
                var clearRequest = new byte[] { 0x14, 0x16, 0x0A, 0x00 };
                _uds.SendRawRequest(ModuleAddresses.PCM_TX, clearRequest);
                Thread.Sleep(500);

                // Also try general DTC clear as fallback
                _uds.ClearModuleDTCs(ModuleAddresses.PCM_TX);

                Logger.Info("P160A cleared from PCM");
            }
            catch (Exception ex)
            {
                Logger.Error($"Clear P160A failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Clears B10A2 DTC from BCM (Configuration Incompatible)
        /// Token Cost: 1 token
        /// </summary>
        public void ClearB10A2()
        {
            Logger.Info("Clearing B10A2 from BCM");

            try
            {
                _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(100);

                if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX))
                {
                    throw new Exception("BCM security access denied");
                }
                Thread.Sleep(100);

                // Clear specific DTC B10A2 (0xB10A2 -> high byte B1, mid 0A, low 02)
                var clearRequest = new byte[] { 0x14, 0xB1, 0x0A, 0x02 };
                _uds.SendRawRequest(ModuleAddresses.BCM_TX, clearRequest);
                Thread.Sleep(500);

                Logger.Info("B10A2 cleared from BCM");
            }
            catch (Exception ex)
            {
                Logger.Error($"Clear B10A2 failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Clears crash/crush event flag from BCM
        /// Token Cost: 1 token
        /// Used after collision repairs
        /// </summary>
        public void ClearCrushEvent()
        {
            Logger.Info("Clearing crush event from BCM");

            try
            {
                _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(100);

                if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX))
                {
                    throw new Exception("BCM security access denied");
                }
                Thread.Sleep(100);

                // Read crush event status first
                var crushStatus = _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, 0x5B17);
                if (crushStatus != null && crushStatus.Length > 0 && crushStatus[0] != 0x00)
                {
                    Logger.Info($"Crush event detected (status: 0x{crushStatus[0]:X2}), clearing...");
                    
                    // Write 0x00 to clear crush event flag
                    _uds.WriteDataByIdentifier(ModuleAddresses.BCM_TX, 0x5B17, new byte[] { 0x00 });
                    Thread.Sleep(500);
                    
                    Logger.Info("Crush event cleared");
                }
                else
                {
                    Logger.Info("No crush event detected");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Clear crush event failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Clears Keep Alive Memory (KAM) - resets adaptive learning
        /// Token Cost: FREE
        /// </summary>
        public void ClearKAM()
        {
            Logger.Info("Clearing Keep Alive Memory (KAM)");

            try
            {
                _uds.StartExtendedSession(ModuleAddresses.PCM_TX);
                Thread.Sleep(100);

                // KAM clear routine (0x31 01 02 01)
                var kamRoutine = new byte[] { 0x02, 0x01 };
                _uds.RoutineControl(ModuleAddresses.PCM_TX, 0x01, kamRoutine);
                Thread.Sleep(500);

                Logger.Info("KAM cleared successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Clear KAM failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Performs PATS parameter reset
        /// Token Cost: FREE
        /// </summary>
        public void ParameterReset()
        {
            Logger.Info("Starting parameter reset");

            try
            {
                _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                _uds.StartExtendedSession(ModuleAddresses.PCM_TX);
                Thread.Sleep(100);

                _uds.RequestSecurityAccess(ModuleAddresses.BCM_TX);
                _uds.RequestSecurityAccess(ModuleAddresses.PCM_TX);
                Thread.Sleep(100);

                // Perform PATS sync between BCM and PCM
                Logger.Info("Syncing BCM and PCM...");
                var syncData = new byte[] { 0xC1, 0x99, 0x00 };
                _uds.WriteDataByIdentifier(ModuleAddresses.BCM_TX, 0x2E, syncData);
                Thread.Sleep(500);

                _uds.WriteDataByIdentifier(ModuleAddresses.PCM_TX, 0x2E, syncData);
                Thread.Sleep(500);

                _uds.ClearDTCs();

                Logger.Info("Parameter reset completed");
            }
            catch (Exception ex)
            {
                Logger.Error($"Parameter reset failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Initializes the ESCL (Electronic Steering Column Lock) / CEI
        /// Token Cost: 1 token
        /// </summary>
        public void InitializeESCL()
        {
            Logger.Info("Starting ESCL initialization");

            try
            {
                // Try BCM first (F3/M5 platforms)
                try
                {
                    _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                    Thread.Sleep(100);

                    if (_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX))
                    {
                        var initData = new byte[] { 0xF1, 0x10 };  // ESCL init routine
                        _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, initData);
                        Thread.Sleep(10000);  // ESCL init takes up to 10 seconds
                        Logger.Info("ESCL initialization completed via BCM");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info($"BCM ESCL init failed, trying RFA: {ex.Message}");
                }

                // Try RFA module (M4/FF2/Fiesta)
                _uds.StartExtendedSession(ModuleAddresses.RFA_TX);
                Thread.Sleep(100);

                if (!_uds.RequestSecurityAccess(ModuleAddresses.RFA_TX))
                {
                    throw new Exception("ESCL security access denied on both BCM and RFA");
                }
                Thread.Sleep(100);

                var rfaInitData = new byte[] { 0xC1, 0x01 };
                _uds.RoutineControl(ModuleAddresses.RFA_TX, 0x01, rfaInitData);
                Thread.Sleep(10000);

                Logger.Info("ESCL initialization completed via RFA");
            }
            catch (Exception ex)
            {
                Logger.Error($"ESCL initialization failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Disables BCM security for all-keys-lost situations
        /// Token Cost: Requires incode
        /// </summary>
        public void DisableBcmSecurity()
        {
            Logger.Info("Disabling BCM security");

            try
            {
                _uds.StartProgrammingSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(100);

                if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX, 0x03))
                {
                    throw new Exception("Extended security access denied");
                }
                Thread.Sleep(100);

                var disableData = new byte[] { 0x71, 0x6D, 0x00 };
                _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, disableData);
                Thread.Sleep(1000);

                Logger.Info("BCM security disabled");
            }
            catch (Exception ex)
            {
                Logger.Error($"BCM security disable failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Disables the vehicle alarm
        /// Token Cost: FREE
        /// </summary>
        public void DisableAlarm()
        {
            Logger.Info("Disabling alarm");

            try
            {
                // Try RFA first
                try
                {
                    _uds.StartExtendedSession(ModuleAddresses.RFA_TX);
                    Thread.Sleep(50);
                    var disarmData = new byte[] { 0x01, 0x00, 0x00 };
                    _uds.InputOutputControl(ModuleAddresses.RFA_TX, 0x2F, disarmData);
                    Thread.Sleep(500);
                    Logger.Info("Alarm disabled via RFA");
                    return;
                }
                catch { }

                // Fallback to BCM
                _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(50);
                var bcmDisarmData = new byte[] { 0x03, 0x00 };
                _uds.InputOutputControl(ModuleAddresses.BCM_TX, 0x30, bcmDisarmData);
                Thread.Sleep(500);

                Logger.Info("Alarm disabled via BCM");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to disable alarm: {ex.Message}");
            }
        }

        #endregion

        #region Keypad Operations

        /// <summary>
        /// Reads door keypad entry code from BCM
        /// Token Cost: 1 token
        /// Returns: 5-digit code (digits 1-9)
        /// </summary>
        public string ReadKeypadCode()
        {
            Logger.Info("Reading keypad code from BCM");

            try
            {
                _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(100);

                if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX))
                {
                    throw new Exception("BCM security access denied");
                }
                Thread.Sleep(100);

                // Try DID 0x421E (F3 platforms) first
                byte[]? response = null;
                try
                {
                    response = _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, 0x421E);
                }
                catch
                {
                    // Try DID 0x4072 (M5 platforms)
                    response = _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, 0x4072);
                }

                if (response == null || response.Length < 2)
                {
                    throw new Exception("Could not read keypad code - vehicle may not have keypad");
                }

                // Decode 16-bit value to 5-digit code
                ushort rawValue = (ushort)((response[0] << 8) | response[1]);
                string code = DecodeKeypadValue(rawValue);
                
                Logger.Info($"Keypad code read successfully: {code}");
                return code;
            }
            catch (Exception ex)
            {
                Logger.Error($"Read keypad code failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Writes new door keypad entry code to BCM
        /// Token Cost: 1 token
        /// </summary>
        /// <param name="code">5-digit code (digits 1-9 only)</param>
        public void WriteKeypadCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 5)
                throw new ArgumentException("Keypad code must be exactly 5 digits", nameof(code));

            // Validate all digits are 1-9
            foreach (char c in code)
            {
                if (c < '1' || c > '9')
                    throw new ArgumentException("Keypad code digits must be 1-9 only", nameof(code));
            }

            Logger.Info($"Writing keypad code to BCM: {code}");

            try
            {
                _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                Thread.Sleep(100);

                if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX))
                {
                    throw new Exception("BCM security access denied");
                }
                Thread.Sleep(100);

                // Encode 5-digit code to 16-bit value
                ushort rawValue = EncodeKeypadValue(code);
                var data = new byte[] { (byte)(rawValue >> 8), (byte)(rawValue & 0xFF) };

                // Try DID 0x421E first, then 0x4072
                try
                {
                    _uds.WriteDataByIdentifier(ModuleAddresses.BCM_TX, 0x421E, data);
                }
                catch
                {
                    _uds.WriteDataByIdentifier(ModuleAddresses.BCM_TX, 0x4072, data);
                }

                Thread.Sleep(500);
                Logger.Info("Keypad code written successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Write keypad code failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Decodes 16-bit BCM value to 5-digit keypad code
        /// </summary>
        private string DecodeKeypadValue(ushort value)
        {
            // Ford uses base-9 encoding (digits 1-9, no 0)
            char[] digits = new char[5];
            for (int i = 4; i >= 0; i--)
            {
                int digit = (value % 9) + 1;
                digits[i] = (char)('0' + digit);
                value /= 9;
            }
            return new string(digits);
        }

        /// <summary>
        /// Encodes 5-digit keypad code to 16-bit BCM value
        /// </summary>
        private ushort EncodeKeypadValue(string code)
        {
            ushort value = 0;
            foreach (char c in code)
            {
                value = (ushort)(value * 9 + (c - '1'));
            }
            return value;
        }

        #endregion

        #region Gateway Operations

        /// <summary>
        /// Detects if vehicle has security gateway (2020+ vehicles)
        /// Token Cost: FREE
        /// </summary>
        public bool DetectGateway()
        {
            Logger.Info("Checking for security gateway (2020+ vehicles)");

            try
            {
                // Gateway module address: 0x7E5/0x7ED
                var response = _uds.ReadDataByIdentifier(ModuleAddresses.GWM_TX, 0xF190);
                bool hasGateway = response != null && response.Length > 0;
                
                Logger.Info($"Gateway detected: {hasGateway}");
                return hasGateway;
            }
            catch
            {
                Logger.Info("No gateway detected");
                return false;
            }
        }

        /// <summary>
        /// Unlocks security gateway for diagnostic access
        /// Token Cost: 1 token
        /// Required for 2020+ Ford vehicles before diagnostic operations
        /// </summary>
        public void UnlockGateway(string incode)
        {
            if (string.IsNullOrEmpty(incode))
                throw new ArgumentException("Incode required for gateway unlock", nameof(incode));

            Logger.Info("Unlocking security gateway");

            try
            {
                _uds.StartExtendedSession(ModuleAddresses.GWM_TX);
                Thread.Sleep(100);

                // Request seed
                if (!_uds.RequestSecurityAccess(ModuleAddresses.GWM_TX))
                {
                    throw new Exception("Gateway security access denied");
                }
                Thread.Sleep(100);

                // Submit incode
                var incodeBytes = ParseIncodeToBytes(incode);
                var unlockData = new byte[2 + incodeBytes.Length];
                unlockData[0] = 0x27;
                unlockData[1] = 0x02;
                Array.Copy(incodeBytes, 0, unlockData, 2, incodeBytes.Length);
                
                _uds.SendRawRequest(ModuleAddresses.GWM_TX, unlockData);
                Thread.Sleep(500);

                Logger.Info("Gateway unlocked successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Gateway unlock failed: {ex.Message}", ex);
                throw;
            }
        }

        #endregion

        #region Vehicle Reset

        /// <summary>
        /// Performs soft reset on BCM + PCM + ABS together
        /// Token Cost: FREE
        /// Does NOT erase keys, DTCs, or configuration data
        /// </summary>
        public void VehicleReset()
        {
            Logger.Info("Starting vehicle reset (BCM + PCM + ABS)");

            try
            {
                // Reset BCM
                Logger.Info("Resetting BCM...");
                try
                {
                    _uds.EcuReset(ModuleAddresses.BCM_TX);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"BCM reset warning: {ex.Message}");
                }
                Thread.Sleep(500);

                // Reset PCM
                Logger.Info("Resetting PCM...");
                try
                {
                    _uds.EcuReset(ModuleAddresses.PCM_TX);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"PCM reset warning: {ex.Message}");
                }
                Thread.Sleep(500);

                // Reset ABS
                Logger.Info("Resetting ABS...");
                try
                {
                    _uds.EcuReset(ModuleAddresses.ABS_TX);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"ABS reset warning: {ex.Message}");
                }
                Thread.Sleep(500);

                // Also send broadcast reset
                Logger.Info("Sending broadcast reset...");
                _uds.EcuReset(0x7DF);
                
                // Wait for modules to restart
                Thread.Sleep(2000);

                Logger.Info("Vehicle reset completed (BCM + PCM + ABS)");
            }
            catch (Exception ex)
            {
                Logger.Error($"Vehicle reset failed: {ex.Message}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Reads PATS status from BCM
        /// </summary>
        private byte ReadPatsStatus()
        {
            try
            {
                var response = _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, 0xC126);
                if (response != null && response.Length > 0)
                {
                    return response[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to read PATS status: {ex.Message}");
            }
            return 0x00;
        }

        /// <summary>
        /// Submits incode to BCM
        /// </summary>
        private void SubmitIncode(byte[] incodeBytes)
        {
            var routineData = new byte[3 + incodeBytes.Length];
            routineData[0] = 0x71;
            routineData[1] = 0x6D;
            routineData[2] = 0xCA;
            Array.Copy(incodeBytes, 0, routineData, 3, incodeBytes.Length);
            _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, routineData);
        }

        /// <summary>
        /// Parses incode string to bytes
        /// </summary>
        private static byte[] ParseIncodeToBytes(string incode)
        {
            incode = incode.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

            if (incode.Length == 4)
            {
                return new byte[]
                {
                    Convert.ToByte(incode.Substring(0, 2), 16),
                    Convert.ToByte(incode.Substring(2, 2), 16)
                };
            }
            else if (incode.Length == 8)
            {
                return new byte[]
                {
                    Convert.ToByte(incode.Substring(0, 2), 16),
                    Convert.ToByte(incode.Substring(2, 2), 16),
                    Convert.ToByte(incode.Substring(4, 2), 16),
                    Convert.ToByte(incode.Substring(6, 2), 16)
                };
            }
            else
            {
                var bytes = new byte[incode.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(incode.Substring(i * 2, 2), 16);
                }
                return bytes;
            }
        }

        #endregion
    }
}
