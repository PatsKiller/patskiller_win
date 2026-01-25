using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Ford UDS (Unified Diagnostic Services) Protocol Implementation
    /// Based on EZimmo reverse engineering and ISO 14229 standard
    /// 
    /// Supports all Ford PATS operations via CAN bus
    /// </summary>
    public class FordUdsProtocol : IDisposable
    {
        private readonly J2534Api _api;
        private uint _deviceId;
        private uint _channelId;
        private uint _filterId;
        private bool _isConnected;
        private readonly object _lock = new object();

        // Timeouts (ms)
        public int ReadTimeout { get; set; } = 2000;
        public int WriteTimeout { get; set; } = 1000;

        // Current target module
        public uint TargetTxId { get; private set; } = FordModules.BCM_TX;
        public uint TargetRxId { get; private set; } = FordModules.BCM_RX;

        public bool IsConnected => _isConnected;

        public FordUdsProtocol(J2534Api api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        #region Connection Management

        /// <summary>
        /// Open device and connect to CAN channel
        /// </summary>
        public J2534Error Connect(uint baudRate = 500000)
        {
            lock (_lock)
            {
                if (_isConnected)
                    return J2534Error.STATUS_NOERROR;

                // Open device
                var result = _api.PassThruOpen(out _deviceId);
                if (result != J2534Error.STATUS_NOERROR)
                    return result;

                // Connect to ISO15765 channel (CAN with ISO-TP)
                result = _api.PassThruConnect(
                    _deviceId,
                    ProtocolId.ISO15765,
                    ConnectFlags.CAN_ID_BOTH,
                    baudRate,
                    out _channelId);

                if (result != J2534Error.STATUS_NOERROR)
                {
                    _api.PassThruClose(_deviceId);
                    return result;
                }

                _isConnected = true;
                return J2534Error.STATUS_NOERROR;
            }
        }

        /// <summary>
        /// Disconnect and close device
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                if (!_isConnected)
                    return;

                try
                {
                    if (_filterId != 0)
                    {
                        _api.PassThruStopMsgFilter(_channelId, _filterId);
                        _filterId = 0;
                    }
                    _api.PassThruDisconnect(_channelId);
                    _api.PassThruClose(_deviceId);
                }
                catch { }

                _isConnected = false;
            }
        }

        /// <summary>
        /// Set target module for communication
        /// </summary>
        public J2534Error SetTargetModule(uint txId, uint rxId)
        {
            lock (_lock)
            {
                if (!_isConnected)
                    return J2534Error.ERR_DEVICE_NOT_CONNECTED;

                // Stop existing filter
                if (_filterId != 0)
                {
                    _api.PassThruStopMsgFilter(_channelId, _filterId);
                    _filterId = 0;
                }

                TargetTxId = txId;
                TargetRxId = rxId;

                // Create flow control filter for ISO-TP
                var maskMsg = PassThruMsg.CreateISO15765Message(0xFFFFFFFF, Array.Empty<byte>());
                var patternMsg = PassThruMsg.CreateISO15765Message(rxId, Array.Empty<byte>());
                var flowControlMsg = PassThruMsg.CreateISO15765Message(txId, Array.Empty<byte>());

                return _api.PassThruStartMsgFilter(
                    _channelId,
                    FilterType.FLOW_CONTROL_FILTER,
                    maskMsg,
                    patternMsg,
                    flowControlMsg,
                    out _filterId);
            }
        }

        #endregion

        #region UDS Services

        /// <summary>
        /// Send UDS request and receive response
        /// </summary>
        public UdsResponse SendRequest(byte[] request, int timeout = 0)
        {
            if (timeout == 0) timeout = ReadTimeout;

            lock (_lock)
            {
                if (!_isConnected)
                    return new UdsResponse { Error = J2534Error.ERR_DEVICE_NOT_CONNECTED };

                // Clear buffers
                _api.ClearRxBuffer(_channelId);
                _api.ClearTxBuffer(_channelId);

                // Build and send message
                var txMsg = PassThruMsg.CreateISO15765Message(TargetTxId, request);
                var msgs = new[] { txMsg };
                uint numMsgs = 1;

                var result = _api.PassThruWriteMsgs(_channelId, msgs, ref numMsgs, (uint)WriteTimeout);
                if (result != J2534Error.STATUS_NOERROR)
                    return new UdsResponse { Error = result };

                // Wait for response
                var startTime = Environment.TickCount;
                var rxMsgs = new PassThruMsg[1];

                while (Environment.TickCount - startTime < timeout)
                {
                    numMsgs = 1;
                    result = _api.PassThruReadMsgs(_channelId, rxMsgs, ref numMsgs, 100);

                    if (result == J2534Error.STATUS_NOERROR && numMsgs > 0)
                    {
                        var response = rxMsgs[0];
                        var responseId = response.GetCanId();

                        // Check if this is from our target module
                        if (responseId == TargetRxId)
                        {
                            var payload = response.GetPayload();
                            return ParseUdsResponse(payload);
                        }
                    }
                    else if (result != J2534Error.ERR_BUFFER_EMPTY && result != J2534Error.ERR_TIMEOUT)
                    {
                        return new UdsResponse { Error = result };
                    }

                    Thread.Sleep(10);
                }

                return new UdsResponse { Error = J2534Error.ERR_TIMEOUT };
            }
        }

        private UdsResponse ParseUdsResponse(byte[] data)
        {
            if (data == null || data.Length == 0)
                return new UdsResponse { Error = J2534Error.ERR_INVALID_MSG };

            var response = new UdsResponse
            {
                ServiceId = data[0],
                RawData = data
            };

            // Check for negative response
            if (data[0] == 0x7F)
            {
                response.IsNegativeResponse = true;
                if (data.Length >= 3)
                {
                    response.NegativeResponseCode = (NegativeResponseCode)data[2];
                    response.RequestedServiceId = data[1];
                }
            }
            else
            {
                // Positive response - extract data (skip service ID byte)
                if (data.Length > 1)
                {
                    response.Data = new byte[data.Length - 1];
                    Array.Copy(data, 1, response.Data, 0, data.Length - 1);
                }
            }

            return response;
        }

        /// <summary>
        /// Diagnostic Session Control (0x10)
        /// </summary>
        public UdsResponse DiagnosticSessionControl(DiagnosticSession session)
        {
            return SendRequest(new byte[] { 0x10, (byte)session });
        }

        /// <summary>
        /// ECU Reset (0x11)
        /// </summary>
        public UdsResponse EcuReset(ResetType resetType = ResetType.SoftReset)
        {
            return SendRequest(new byte[] { 0x11, (byte)resetType });
        }

        /// <summary>
        /// Read Data By Identifier (0x22)
        /// </summary>
        public UdsResponse ReadDataByIdentifier(ushort did)
        {
            return SendRequest(new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });
        }

        /// <summary>
        /// Read Data By Identifier - multiple DIDs
        /// </summary>
        public UdsResponse ReadDataByIdentifier(params ushort[] dids)
        {
            var request = new byte[1 + dids.Length * 2];
            request[0] = 0x22;
            for (int i = 0; i < dids.Length; i++)
            {
                request[1 + i * 2] = (byte)(dids[i] >> 8);
                request[2 + i * 2] = (byte)(dids[i] & 0xFF);
            }
            return SendRequest(request);
        }

        /// <summary>
        /// Write Data By Identifier (0x2E)
        /// </summary>
        public UdsResponse WriteDataByIdentifier(ushort did, byte[] data)
        {
            var request = new byte[3 + data.Length];
            request[0] = 0x2E;
            request[1] = (byte)(did >> 8);
            request[2] = (byte)(did & 0xFF);
            Array.Copy(data, 0, request, 3, data.Length);
            return SendRequest(request);
        }

        /// <summary>
        /// Security Access - Request Seed (0x27 01)
        /// </summary>
        public UdsResponse SecurityAccessRequestSeed(byte level = 0x01)
        {
            return SendRequest(new byte[] { 0x27, level }, 5000);
        }

        /// <summary>
        /// Security Access - Send Key (0x27 02)
        /// </summary>
        public UdsResponse SecurityAccessSendKey(byte[] key, byte level = 0x02)
        {
            var request = new byte[2 + key.Length];
            request[0] = 0x27;
            request[1] = level;
            Array.Copy(key, 0, request, 2, key.Length);
            return SendRequest(request, 5000);
        }

        /// <summary>
        /// Routine Control (0x31)
        /// </summary>
        public UdsResponse RoutineControl(RoutineControlType controlType, ushort routineId, byte[]? data = null)
        {
            var dataLen = data?.Length ?? 0;
            var request = new byte[4 + dataLen];
            request[0] = 0x31;
            request[1] = (byte)controlType;
            request[2] = (byte)(routineId >> 8);
            request[3] = (byte)(routineId & 0xFF);
            if (data != null)
                Array.Copy(data, 0, request, 4, data.Length);
            return SendRequest(request, 10000);
        }

        /// <summary>
        /// Tester Present (0x3E)
        /// </summary>
        public UdsResponse TesterPresent(bool suppressResponse = true)
        {
            return SendRequest(new byte[] { 0x3E, (byte)(suppressResponse ? 0x80 : 0x00) });
        }

        /// <summary>
        /// Clear Diagnostic Information / DTCs (0x14)
        /// </summary>
        public UdsResponse ClearDtc(uint groupOfDtc = 0xFFFFFF)
        {
            return SendRequest(new byte[]
            {
                0x14,
                (byte)((groupOfDtc >> 16) & 0xFF),
                (byte)((groupOfDtc >> 8) & 0xFF),
                (byte)(groupOfDtc & 0xFF)
            });
        }

        /// <summary>
        /// Read DTC Information (0x19)
        /// </summary>
        public UdsResponse ReadDtcInformation(DtcReportType reportType, byte statusMask = 0xFF)
        {
            return SendRequest(new byte[] { 0x19, (byte)reportType, statusMask });
        }

        #endregion

        #region Broadcast Operations

        /// <summary>
        /// Send tester present to all modules (broadcast)
        /// </summary>
        public void BroadcastTesterPresent()
        {
            var originalTx = TargetTxId;
            var originalRx = TargetRxId;

            SetTargetModule(FordModules.BROADCAST, 0x7FF);
            TesterPresent(true);

            SetTargetModule(originalTx, originalRx);
        }

        /// <summary>
        /// Clear DTCs on all modules (broadcast)
        /// </summary>
        public void BroadcastClearDtc()
        {
            var originalTx = TargetTxId;
            var originalRx = TargetRxId;

            SetTargetModule(FordModules.BROADCAST, 0x7FF);
            ClearDtc();

            SetTargetModule(originalTx, originalRx);
        }

        #endregion

        #region Battery Voltage

        /// <summary>
        /// Read battery voltage from J2534 device
        /// </summary>
        public double ReadBatteryVoltage()
        {
            if (!_isConnected) return 0;

            _api.ReadVoltage(_deviceId, out double voltage);
            return voltage;
        }

        #endregion

        public void Dispose()
        {
            Disconnect();
        }
    }

    #region Response Classes

    /// <summary>
    /// UDS Response container
    /// </summary>
    public class UdsResponse
    {
        public J2534Error Error { get; set; } = J2534Error.STATUS_NOERROR;
        public byte ServiceId { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public byte[] RawData { get; set; } = Array.Empty<byte>();
        public bool IsNegativeResponse { get; set; }
        public NegativeResponseCode NegativeResponseCode { get; set; }
        public byte RequestedServiceId { get; set; }

        public bool Success => Error == J2534Error.STATUS_NOERROR && !IsNegativeResponse;

        public string GetErrorMessage()
        {
            if (Error != J2534Error.STATUS_NOERROR)
                return $"J2534 Error: {Error}";
            if (IsNegativeResponse)
                return $"NRC: {NegativeResponseCode} (0x{(byte)NegativeResponseCode:X2})";
            return "OK";
        }

        /// <summary>
        /// Get data as hex string
        /// </summary>
        public string ToHexString()
        {
            return BitConverter.ToString(RawData).Replace("-", " ");
        }
    }

    #endregion

    #region Enums

    /// <summary>
    /// UDS Diagnostic Session types
    /// </summary>
    public enum DiagnosticSession : byte
    {
        Default = 0x01,
        Programming = 0x02,
        Extended = 0x03,
        SafetySystem = 0x04,
        // Ford-specific sessions
        FordDeveloper = 0x60,
        FordManufacturing = 0x7F
    }

    /// <summary>
    /// ECU Reset types
    /// </summary>
    public enum ResetType : byte
    {
        HardReset = 0x01,
        KeyOffOnReset = 0x02,
        SoftReset = 0x03,
        EnableRapidPowerShutdown = 0x04,
        DisableRapidPowerShutdown = 0x05
    }

    /// <summary>
    /// Routine Control types
    /// </summary>
    public enum RoutineControlType : byte
    {
        StartRoutine = 0x01,
        StopRoutine = 0x02,
        RequestRoutineResults = 0x03
    }

    /// <summary>
    /// DTC Report types
    /// </summary>
    public enum DtcReportType : byte
    {
        ReportNumberOfDtcByStatusMask = 0x01,
        ReportDtcByStatusMask = 0x02,
        ReportDtcSnapshotIdentification = 0x03,
        ReportDtcSnapshotRecordByDtcNumber = 0x04,
        ReportSupportedDtc = 0x0A,
        ReportFirstTestFailedDtc = 0x0B,
        ReportFirstConfirmedDtc = 0x0C,
        ReportMostRecentTestFailedDtc = 0x0D,
        ReportMostRecentConfirmedDtc = 0x0E,
        ReportDtcWithPermanentStatus = 0x15
    }

    /// <summary>
    /// UDS Negative Response Codes
    /// </summary>
    public enum NegativeResponseCode : byte
    {
        GeneralReject = 0x10,
        ServiceNotSupported = 0x11,
        SubFunctionNotSupported = 0x12,
        IncorrectMessageLengthOrInvalidFormat = 0x13,
        ResponseTooLong = 0x14,
        BusyRepeatRequest = 0x21,
        ConditionsNotCorrect = 0x22,
        RequestSequenceError = 0x24,
        NoResponseFromSubnetComponent = 0x25,
        FailurePreventsExecutionOfRequestedAction = 0x26,
        RequestOutOfRange = 0x31,
        SecurityAccessDenied = 0x33,
        InvalidKey = 0x35,
        ExceededNumberOfAttempts = 0x36,
        RequiredTimeDelayNotExpired = 0x37,
        UploadDownloadNotAccepted = 0x70,
        TransferDataSuspended = 0x71,
        GeneralProgrammingFailure = 0x72,
        WrongBlockSequenceCounter = 0x73,
        RequestCorrectlyReceivedResponsePending = 0x78,
        SubFunctionNotSupportedInActiveSession = 0x7E,
        ServiceNotSupportedInActiveSession = 0x7F,
        // Ford-specific
        SecurityAccessLockout = 0x85,
        KeyNotProgrammed = 0x86,
        IncorrectIncode = 0x87
    }

    #endregion

    #region Ford Module Addresses

    /// <summary>
    /// Ford CAN Module Addresses (from EZimmo analysis)
    /// </summary>
    public static class FordModules
    {
        // Body Control Module (BCM) - HS-CAN 500K
        public const uint BCM_TX = 0x726;
        public const uint BCM_RX = 0x72E;

        // Powertrain Control Module (PCM) - HS-CAN 500K
        public const uint PCM_TX = 0x7E0;
        public const uint PCM_RX = 0x7E8;

        // Remote Function Actuator (RFA) - MS-CAN 125K
        public const uint RFA_TX = 0x731;
        public const uint RFA_RX = 0x739;

        // Instrument Panel Cluster (IPC) - HS-CAN 500K
        public const uint IPC_TX = 0x720;
        public const uint IPC_RX = 0x728;

        // Anti-lock Brake System (ABS) - HS-CAN 500K
        public const uint ABS_TX = 0x760;
        public const uint ABS_RX = 0x768;

        // Transmission Control Module (TCM) - HS-CAN 500K
        public const uint TCM_TX = 0x7E1;
        public const uint TCM_RX = 0x7E9;

        // Steering Column Control Module (SCCM/ESCL)
        public const uint SCCM_TX = 0x730;
        public const uint SCCM_RX = 0x738;

        // Security Gateway Module (SGWM) - 2020+ vehicles
        public const uint SGWM_TX = 0x716;
        public const uint SGWM_RX = 0x71E;

        // Broadcast address (all modules)
        public const uint BROADCAST = 0x7DF;
    }

    /// <summary>
    /// Ford UDS Data Identifiers (DIDs) - from EZimmo analysis
    /// </summary>
    public static class FordDids
    {
        // Standard DIDs
        public const ushort VIN = 0xF190;               // Vehicle Identification Number
        public const ushort ECU_SERIAL = 0xF18C;        // ECU Serial Number
        public const ushort PART_NUMBER = 0xF113;       // BCM Part Number
        public const ushort SOFTWARE_VERSION = 0xF195;  // Application Software ID

        // PATS DIDs
        public const ushort PATS_OUTCODE = 0xC1A1;      // PrePATS Outcode
        public const ushort PATS_STATUS = 0xC126;       // PATS Status (0xAA = success)
        public const ushort KEY_COUNT = 0xDE01;         // Number of programmed keys
        public const ushort KEY_STATUS = 0xDE02;        // Key programming status

        // BCM DIDs
        public const ushort CRASH_EVENT = 0x5B17;       // Crash/Crush Event Flag
        public const ushort KEY_MIN_MAX = 0x5B13;       // Min/Max key settings
        public const ushort KEYPAD_CODE = 0x421E;       // Keypad entry code (F3)
        public const ushort KEYPAD_CODE_M5 = 0x4072;    // Keypad entry code (M5)

        // Parameter Reset DIDs
        public const ushort PARAM_RESET = 0xC199;       // Full PATS sync
        public const ushort BCM_DEFAULTS = 0xC19C;      // BCM factory defaults
    }

    /// <summary>
    /// Ford Routine IDs - from EZimmo analysis
    /// </summary>
    public static class FordRoutines
    {
        // PATS Routines
        public const ushort PATS_ACCESS = 0x716D;       // Submit incode
        public const ushort PROGRAM_KEY = 0x0E10;       // Program key to slot
        public const ushort ERASE_KEYS = 0x0E11;        // Erase all keys
        public const ushort CLEAR_P160A = 0x0E06;       // Clear P160A parameter

        // WKIP/WKKIP (Write Key In Progress)
        public const ushort WKIP = 0xF0F1;              // Standard key programming
        public const ushort WKKIP = 0xF0F2;             // Keyless key programming
    }

    #endregion
}
