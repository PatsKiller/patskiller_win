using System;
using System.Text;
using System.Threading;
using PatsKillerPro.J2534;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Communication
{
    /// <summary>
    /// UDS (Unified Diagnostic Services) ISO 14229 implementation
    /// </summary>
    public class UdsService
    {
        private readonly J2534Channel _channel;
        private uint _testerPresentMsgId = 0;

        // UDS Service IDs
        public const byte SID_DIAGNOSTIC_SESSION_CONTROL = 0x10;
        public const byte SID_ECU_RESET = 0x11;
        public const byte SID_CLEAR_DTC = 0x14;
        public const byte SID_READ_DTC = 0x19;
        public const byte SID_READ_DATA_BY_ID = 0x22;
        public const byte SID_READ_MEMORY_BY_ADDRESS = 0x23;
        public const byte SID_SECURITY_ACCESS = 0x27;
        public const byte SID_COMMUNICATION_CONTROL = 0x28;
        public const byte SID_WRITE_DATA_BY_ID = 0x2E;
        public const byte SID_IO_CONTROL = 0x2F;
        public const byte SID_ROUTINE_CONTROL = 0x31;
        public const byte SID_REQUEST_DOWNLOAD = 0x34;
        public const byte SID_REQUEST_UPLOAD = 0x35;
        public const byte SID_TRANSFER_DATA = 0x36;
        public const byte SID_REQUEST_TRANSFER_EXIT = 0x37;
        public const byte SID_TESTER_PRESENT = 0x3E;

        // Session Types
        public const byte SESSION_DEFAULT = 0x01;
        public const byte SESSION_PROGRAMMING = 0x02;
        public const byte SESSION_EXTENDED = 0x03;

        // Common DIDs (Data Identifiers)
        public const ushort DID_VIN = 0xF190;
        public const ushort DID_PART_NUMBER = 0xF187;
        public const ushort DID_ECU_NAME = 0xF18C;
        public const ushort DID_HARDWARE_VERSION = 0xF191;
        public const ushort DID_SOFTWARE_VERSION = 0xF195;
        public const ushort DID_CALIBRATION_ID = 0xF197;
        public const ushort DID_PATS_OUTCODE = 0xC100;
        public const ushort DID_PATS_KEYS_COUNT = 0xC101;
        public const ushort DID_PATS_STATUS = 0xC126;

        public UdsService(J2534Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        /// <summary>
        /// Reads VIN from vehicle
        /// </summary>
        public string? ReadVIN()
        {
            try
            {
                // Setup filter for PCM response
                _channel.SetupFlowControlFilter(Vehicle.ModuleAddresses.PCM_TX, Vehicle.ModuleAddresses.PCM_RX);
                
                // Read VIN from PCM
                var response = ReadDataByIdentifier(Vehicle.ModuleAddresses.PCM_TX, DID_VIN);
                
                if (response != null && response.Length >= 17)
                {
                    var vin = Encoding.ASCII.GetString(response, 0, 17);
                    return vin.Trim();
                }

                // Try BCM if PCM doesn't respond
                _channel.SetupFlowControlFilter(Vehicle.ModuleAddresses.BCM_TX, Vehicle.ModuleAddresses.BCM_RX);
                response = ReadDataByIdentifier(Vehicle.ModuleAddresses.BCM_TX, DID_VIN);
                
                if (response != null && response.Length >= 17)
                {
                    var vin = Encoding.ASCII.GetString(response, 0, 17);
                    return vin.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error reading VIN: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Reads part number from a module
        /// </summary>
        public string ReadPartNumber(uint txId)
        {
            try
            {
                var response = ReadDataByIdentifier(txId, DID_PART_NUMBER);
                if (response != null && response.Length > 0)
                {
                    return Encoding.ASCII.GetString(response).Trim();
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error reading part number: {ex.Message}");
            }
            return "Unknown";
        }

        /// <summary>
        /// Reads PATS outcode from BCM
        /// </summary>
        public string ReadOutcode()
        {
            try
            {
                // Ensure BCM filter is set
                _channel.SetupFlowControlFilter(Vehicle.ModuleAddresses.BCM_TX, Vehicle.ModuleAddresses.BCM_RX);
                
                // Start extended session
                StartExtendedSession(Vehicle.ModuleAddresses.BCM_TX);
                Thread.Sleep(100);

                // Read outcode
                var response = ReadDataByIdentifier(Vehicle.ModuleAddresses.BCM_TX, DID_PATS_OUTCODE);
                
                if (response != null && response.Length >= 6)
                {
                    // Format outcode as XXXX-XXXXXX-XXXXXX
                    var hex = BitConverter.ToString(response).Replace("-", "");
                    if (hex.Length >= 18)
                    {
                        return $"{hex.Substring(0, 4)}-{hex.Substring(4, 6)}-{hex.Substring(10, 6)}";
                    }
                    return hex;
                }

                return "Unable to read outcode";
            }
            catch (Exception ex)
            {
                Logger.Error($"Error reading outcode: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Reads number of programmed keys
        /// </summary>
        public int ReadKeysCount()
        {
            try
            {
                var response = ReadDataByIdentifier(Vehicle.ModuleAddresses.BCM_TX, DID_PATS_KEYS_COUNT);
                if (response != null && response.Length > 0)
                {
                    return response[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error reading keys count: {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// Starts a diagnostic session
        /// </summary>
        public void StartDiagnosticSession(uint txId, byte sessionType)
        {
            var request = new byte[] { SID_DIAGNOSTIC_SESSION_CONTROL, sessionType };
            var response = SendAndReceive(txId, request);
            
            if (response == null || response.Length < 2 || response[0] != (SID_DIAGNOSTIC_SESSION_CONTROL + 0x40))
            {
                var nrc = response != null && response.Length >= 3 ? response[2] : 0;
                throw new UdsException($"Failed to start session 0x{sessionType:X2}", nrc);
            }

            Logger.Info($"Started session 0x{sessionType:X2} on module 0x{txId:X3}");
        }

        /// <summary>
        /// Starts extended diagnostic session
        /// </summary>
        public void StartExtendedSession(uint txId)
        {
            StartDiagnosticSession(txId, SESSION_EXTENDED);
        }

        /// <summary>
        /// Starts programming session
        /// </summary>
        public void StartProgrammingSession(uint txId)
        {
            StartDiagnosticSession(txId, SESSION_PROGRAMMING);
        }

        /// <summary>
        /// Requests security access (seed-key)
        /// </summary>
        public bool RequestSecurityAccess(uint txId, byte level = 0x01)
        {
            try
            {
                // Request seed
                Logger.Info($"Requesting security seed (level 0x{level:X2})");
                var seedRequest = new byte[] { SID_SECURITY_ACCESS, level };
                var seedResponse = SendAndReceive(txId, seedRequest);

                if (seedResponse == null || seedResponse.Length < 4 || seedResponse[0] != (SID_SECURITY_ACCESS + 0x40))
                {
                    Logger.Warning("Security seed request failed");
                    return false;
                }

                // Extract seed (bytes 2-5)
                var seed = new byte[seedResponse.Length - 2];
                Array.Copy(seedResponse, 2, seed, 0, seed.Length);
                Logger.Info($"Received seed: {BitConverter.ToString(seed)}");

                // Calculate key (simple XOR-based algorithm - actual Ford algorithm is more complex)
                var key = CalculateSecurityKey(seed, txId);
                Logger.Info($"Calculated key: {BitConverter.ToString(key)}");

                // Send key
                var keyRequest = new byte[2 + key.Length];
                keyRequest[0] = SID_SECURITY_ACCESS;
                keyRequest[1] = (byte)(level + 1);  // Key level is seed level + 1
                Array.Copy(key, 0, keyRequest, 2, key.Length);

                var keyResponse = SendAndReceive(txId, keyRequest);

                if (keyResponse != null && keyResponse.Length >= 2 && keyResponse[0] == (SID_SECURITY_ACCESS + 0x40))
                {
                    Logger.Info("Security access granted");
                    return true;
                }

                Logger.Warning("Security key rejected");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Security access error: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Calculates security key from seed
        /// Note: This is a simplified algorithm. Real Ford algorithms vary by module and part number.
        /// </summary>
        private byte[] CalculateSecurityKey(byte[] seed, uint moduleId)
        {
            // This is a placeholder - actual Ford security algorithms are proprietary
            // The real implementation would use the module's specific algorithm based on part number
            
            // Simple demonstration algorithm (NOT actual Ford algorithm)
            var key = new byte[seed.Length];
            uint magic = moduleId ^ 0x1234;
            
            for (int i = 0; i < seed.Length; i++)
            {
                key[i] = (byte)(seed[i] ^ ((magic >> (i * 8)) & 0xFF));
            }
            
            return key;
        }

        /// <summary>
        /// Reads data by identifier
        /// </summary>
        public byte[]? ReadDataByIdentifier(uint txId, ushort did)
        {
            var request = new byte[] 
            { 
                SID_READ_DATA_BY_ID, 
                (byte)(did >> 8), 
                (byte)(did & 0xFF) 
            };
            
            var response = SendAndReceive(txId, request);
            
            if (response == null || response.Length < 3)
            {
                return null;
            }

            if (response[0] == (SID_READ_DATA_BY_ID + 0x40))
            {
                // Remove service ID and DID from response
                var data = new byte[response.Length - 3];
                Array.Copy(response, 3, data, 0, data.Length);
                return data;
            }

            if (response[0] == 0x7F)
            {
                var nrc = response.Length >= 3 ? response[2] : 0;
                Logger.Warning($"ReadDataByIdentifier 0x{did:X4} failed with NRC 0x{nrc:X2}");
            }

            return null;
        }

        /// <summary>
        /// Writes data by identifier
        /// </summary>
        public void WriteDataByIdentifier(uint txId, ushort did, byte[] data)
        {
            var request = new byte[3 + data.Length];
            request[0] = SID_WRITE_DATA_BY_ID;
            request[1] = (byte)(did >> 8);
            request[2] = (byte)(did & 0xFF);
            Array.Copy(data, 0, request, 3, data.Length);

            var response = SendAndReceive(txId, request);

            if (response == null || response.Length < 1 || response[0] != (SID_WRITE_DATA_BY_ID + 0x40))
            {
                var nrc = response != null && response.Length >= 3 ? response[2] : 0;
                throw new UdsException($"WriteDataByIdentifier 0x{did:X4} failed", nrc);
            }
        }

        /// <summary>
        /// Input/Output control
        /// </summary>
        public byte[]? InputOutputControl(uint txId, ushort did, byte[] controlParam)
        {
            var request = new byte[3 + controlParam.Length];
            request[0] = SID_IO_CONTROL;
            request[1] = (byte)(did >> 8);
            request[2] = (byte)(did & 0xFF);
            Array.Copy(controlParam, 0, request, 3, controlParam.Length);

            return SendAndReceive(txId, request);
        }

        /// <summary>
        /// Routine control
        /// </summary>
        public byte[]? RoutineControl(uint txId, byte subFunction, byte[] routineData)
        {
            var request = new byte[1 + routineData.Length];
            request[0] = (byte)(SID_ROUTINE_CONTROL | (subFunction << 4));
            Array.Copy(routineData, 0, request, 1, routineData.Length);

            return SendAndReceive(txId, request, 5000);  // Longer timeout for routines
        }

        /// <summary>
        /// Clears all DTCs
        /// </summary>
        public void ClearDTCs()
        {
            Logger.Info("Clearing DTCs...");

            // Clear DTCs on all common modules
            uint[] modules = { 
                Vehicle.ModuleAddresses.BCM_TX, 
                Vehicle.ModuleAddresses.PCM_TX, 
                Vehicle.ModuleAddresses.TCM_TX,
                Vehicle.ModuleAddresses.ABS_TX
            };

            foreach (var module in modules)
            {
                try
                {
                    var request = new byte[] { SID_CLEAR_DTC, 0xFF, 0xFF, 0xFF };
                    _channel.SendMessage(module, request);
                    Thread.Sleep(100);
                }
                catch
                {
                    // Ignore errors - module may not be present
                }
            }

            Logger.Info("DTCs cleared");
        }

        /// <summary>
        /// Clears DTCs from a specific module
        /// </summary>
        public void ClearModuleDTCs(uint txId)
        {
            Logger.Info($"Clearing DTCs from module 0x{txId:X3}");
            var request = new byte[] { SID_CLEAR_DTC, 0xFF, 0xFF, 0xFF };
            _channel.SendMessage(txId, request);
            Thread.Sleep(100);
        }

        /// <summary>
        /// Sends a raw UDS request without processing
        /// </summary>
        public byte[]? SendRawRequest(uint txId, byte[] request, uint timeout = 1000)
        {
            return SendAndReceive(txId, request, timeout);
        }

        /// <summary>
        /// Resets ECU
        /// </summary>
        public void EcuReset(uint txId = 0x7DF)
        {
            var request = new byte[] { SID_ECU_RESET, 0x01 };  // Hard reset
            _channel.SendMessage(txId, request);
            Logger.Info("ECU reset command sent");
        }

        /// <summary>
        /// Starts tester present periodic message
        /// </summary>
        public void StartTesterPresent(uint txId, uint intervalMs = 2000)
        {
            if (_testerPresentMsgId != 0)
            {
                _channel.StopPeriodicMessage(_testerPresentMsgId);
            }

            var testerPresent = new byte[] { SID_TESTER_PRESENT, 0x00 };
            _testerPresentMsgId = _channel.StartPeriodicMessage(txId, testerPresent, intervalMs);
        }

        /// <summary>
        /// Stops tester present periodic message
        /// </summary>
        public void StopTesterPresent()
        {
            if (_testerPresentMsgId != 0)
            {
                _channel.StopPeriodicMessage(_testerPresentMsgId);
                _testerPresentMsgId = 0;
            }
        }

        /// <summary>
        /// Reads all module information
        /// </summary>
        public string ReadAllModuleInfo()
        {
            var sb = new StringBuilder();
            
            var modules = new (string Name, uint TxId, uint RxId)[]
            {
                ("BCM", Vehicle.ModuleAddresses.BCM_TX, Vehicle.ModuleAddresses.BCM_RX),
                ("PCM", Vehicle.ModuleAddresses.PCM_TX, Vehicle.ModuleAddresses.PCM_RX),
                ("IPC", Vehicle.ModuleAddresses.IPC_TX, Vehicle.ModuleAddresses.IPC_RX),
                ("ABS", Vehicle.ModuleAddresses.ABS_TX, Vehicle.ModuleAddresses.ABS_RX),
            };

            foreach (var (name, txId, rxId) in modules)
            {
                try
                {
                    _channel.SetupFlowControlFilter(txId, rxId);
                    
                    var partNum = ReadDataByIdentifier(txId, DID_PART_NUMBER);
                    var swVer = ReadDataByIdentifier(txId, DID_SOFTWARE_VERSION);
                    
                    sb.AppendLine($"{name}:");
                    sb.AppendLine($"  Part#: {(partNum != null ? Encoding.ASCII.GetString(partNum).Trim() : "N/A")}");
                    sb.AppendLine($"  SW Ver: {(swVer != null ? Encoding.ASCII.GetString(swVer).Trim() : "N/A")}");
                    sb.AppendLine();
                }
                catch
                {
                    sb.AppendLine($"{name}: Not responding");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Sends a request and waits for response
        /// </summary>
        private byte[]? SendAndReceive(uint txId, byte[] request, uint timeout = 1000)
        {
            _channel.ClearRxBuffer();
            return _channel.SendAndReceive(txId, request, timeout);
        }
    }

    /// <summary>
    /// UDS exception with NRC (Negative Response Code)
    /// </summary>
    public class UdsException : Exception
    {
        public byte NegativeResponseCode { get; }

        public UdsException(string message, byte nrc = 0) : base($"{message} (NRC: 0x{nrc:X2} - {GetNrcDescription(nrc)})")
        {
            NegativeResponseCode = nrc;
        }

        public static string GetNrcDescription(byte nrc)
        {
            return nrc switch
            {
                0x10 => "General reject",
                0x11 => "Service not supported",
                0x12 => "Sub-function not supported",
                0x13 => "Incorrect message length or invalid format",
                0x14 => "Response too long",
                0x21 => "Busy repeat request",
                0x22 => "Conditions not correct",
                0x24 => "Request sequence error",
                0x25 => "No response from subnet component",
                0x26 => "Failure prevents execution",
                0x31 => "Request out of range",
                0x33 => "Security access denied",
                0x35 => "Invalid key",
                0x36 => "Exceeded number of attempts",
                0x37 => "Required time delay not expired",
                0x70 => "Upload/download not accepted",
                0x71 => "Transfer data suspended",
                0x72 => "General programming failure",
                0x73 => "Wrong block sequence counter",
                0x78 => "Request correctly received - response pending",
                0x7E => "Sub-function not supported in active session",
                0x7F => "Service not supported in active session",
                _ => "Unknown"
            };
        }
    }
}
