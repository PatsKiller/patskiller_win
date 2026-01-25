using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// J2534 API v04.04 Native Interop
    /// Supports: VCM II, VCM III, Mongoose, CarDAQ, Autel, Topdon, VXDIAG, etc.
    /// 
    /// Registry Path (64-bit): HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\PassThruSupport.04.04
    /// Registry Path (32-bit): HKEY_LOCAL_MACHINE\SOFTWARE\PassThruSupport.04.04
    /// </summary>
    public class J2534Api : IDisposable
    {
        private IntPtr _dllHandle = IntPtr.Zero;
        private bool _disposed = false;

        // Function pointers
        private PassThruOpenDelegate? _passThruOpen;
        private PassThruCloseDelegate? _passThruClose;
        private PassThruConnectDelegate? _passThruConnect;
        private PassThruDisconnectDelegate? _passThruDisconnect;
        private PassThruReadMsgsDelegate? _passThruReadMsgs;
        private PassThruWriteMsgsDelegate? _passThruWriteMsgs;
        private PassThruStartMsgFilterDelegate? _passThruStartMsgFilter;
        private PassThruStopMsgFilterDelegate? _passThruStopMsgFilter;
        private PassThruIoctlDelegate? _passThruIoctl;
        private PassThruReadVersionDelegate? _passThruReadVersion;
        private PassThruGetLastErrorDelegate? _passThruGetLastError;
        private PassThruStartPeriodicMsgDelegate? _passThruStartPeriodicMsg;
        private PassThruStopPeriodicMsgDelegate? _passThruStopPeriodicMsg;
        private PassThruSetProgrammingVoltageDelegate? _passThruSetProgrammingVoltage;

        public string DllPath { get; private set; } = "";
        public bool IsLoaded => _dllHandle != IntPtr.Zero;

        #region Delegates

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruOpenDelegate(IntPtr pName, out uint deviceId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruCloseDelegate(uint deviceId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruConnectDelegate(uint deviceId, ProtocolId protocolId, ConnectFlags flags, uint baudRate, out uint channelId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruDisconnectDelegate(uint channelId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruReadMsgsDelegate(uint channelId, IntPtr pMsg, ref uint numMsgs, uint timeout);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruWriteMsgsDelegate(uint channelId, IntPtr pMsg, ref uint numMsgs, uint timeout);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruStartMsgFilterDelegate(uint channelId, FilterType filterType, IntPtr pMaskMsg, IntPtr pPatternMsg, IntPtr pFlowControlMsg, out uint filterId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruStopMsgFilterDelegate(uint channelId, uint filterId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruIoctlDelegate(uint channelId, IoctlId ioctlId, IntPtr pInput, IntPtr pOutput);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruReadVersionDelegate(uint deviceId, StringBuilder firmwareVersion, StringBuilder dllVersion, StringBuilder apiVersion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruGetLastErrorDelegate(StringBuilder errorDescription);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruStartPeriodicMsgDelegate(uint channelId, IntPtr pMsg, out uint msgId, uint timeInterval);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruStopPeriodicMsgDelegate(uint channelId, uint msgId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruSetProgrammingVoltageDelegate(uint deviceId, uint pinNumber, uint voltage);

        #endregion

        #region Win32 API

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        #endregion

        /// <summary>
        /// Load a J2534 DLL
        /// </summary>
        public J2534Api(string dllPath)
        {
            DllPath = dllPath;
            LoadDll(dllPath);
        }

        private void LoadDll(string dllPath)
        {
            _dllHandle = LoadLibrary(dllPath);
            if (_dllHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new J2534Exception($"Failed to load J2534 DLL: {dllPath} (Error: {error})");
            }

            // Load all function pointers
            _passThruOpen = GetFunction<PassThruOpenDelegate>("PassThruOpen");
            _passThruClose = GetFunction<PassThruCloseDelegate>("PassThruClose");
            _passThruConnect = GetFunction<PassThruConnectDelegate>("PassThruConnect");
            _passThruDisconnect = GetFunction<PassThruDisconnectDelegate>("PassThruDisconnect");
            _passThruReadMsgs = GetFunction<PassThruReadMsgsDelegate>("PassThruReadMsgs");
            _passThruWriteMsgs = GetFunction<PassThruWriteMsgsDelegate>("PassThruWriteMsgs");
            _passThruStartMsgFilter = GetFunction<PassThruStartMsgFilterDelegate>("PassThruStartMsgFilter");
            _passThruStopMsgFilter = GetFunction<PassThruStopMsgFilterDelegate>("PassThruStopMsgFilter");
            _passThruIoctl = GetFunction<PassThruIoctlDelegate>("PassThruIoctl");
            _passThruReadVersion = GetFunction<PassThruReadVersionDelegate>("PassThruReadVersion");
            _passThruGetLastError = GetFunction<PassThruGetLastErrorDelegate>("PassThruGetLastError");
            _passThruStartPeriodicMsg = GetFunction<PassThruStartPeriodicMsgDelegate>("PassThruStartPeriodicMsg");
            _passThruStopPeriodicMsg = GetFunction<PassThruStopPeriodicMsgDelegate>("PassThruStopPeriodicMsg");
            _passThruSetProgrammingVoltage = GetFunction<PassThruSetProgrammingVoltageDelegate>("PassThruSetProgrammingVoltage");
        }

        private T? GetFunction<T>(string functionName) where T : Delegate
        {
            IntPtr procAddr = GetProcAddress(_dllHandle, functionName);
            if (procAddr == IntPtr.Zero)
                return null;
            return Marshal.GetDelegateForFunctionPointer<T>(procAddr);
        }

        #region J2534 API Methods

        /// <summary>
        /// Open connection to J2534 device
        /// </summary>
        public J2534Error PassThruOpen(out uint deviceId)
        {
            deviceId = 0;
            if (_passThruOpen == null)
                throw new J2534Exception("PassThruOpen not available");
            return _passThruOpen(IntPtr.Zero, out deviceId);
        }

        /// <summary>
        /// Close connection to J2534 device
        /// </summary>
        public J2534Error PassThruClose(uint deviceId)
        {
            if (_passThruClose == null)
                throw new J2534Exception("PassThruClose not available");
            return _passThruClose(deviceId);
        }

        /// <summary>
        /// Connect to a protocol channel (CAN, ISO15765, etc.)
        /// </summary>
        public J2534Error PassThruConnect(uint deviceId, ProtocolId protocolId, ConnectFlags flags, uint baudRate, out uint channelId)
        {
            channelId = 0;
            if (_passThruConnect == null)
                throw new J2534Exception("PassThruConnect not available");
            return _passThruConnect(deviceId, protocolId, flags, baudRate, out channelId);
        }

        /// <summary>
        /// Disconnect from protocol channel
        /// </summary>
        public J2534Error PassThruDisconnect(uint channelId)
        {
            if (_passThruDisconnect == null)
                throw new J2534Exception("PassThruDisconnect not available");
            return _passThruDisconnect(channelId);
        }

        /// <summary>
        /// Read messages from channel
        /// </summary>
        public J2534Error PassThruReadMsgs(uint channelId, PassThruMsg[] msgs, ref uint numMsgs, uint timeout)
        {
            if (_passThruReadMsgs == null)
                throw new J2534Exception("PassThruReadMsgs not available");

            int msgSize = Marshal.SizeOf<PassThruMsg>();
            IntPtr pMsgs = Marshal.AllocHGlobal(msgSize * msgs.Length);
            try
            {
                // Initialize the memory
                for (int i = 0; i < msgs.Length; i++)
                {
                    msgs[i] = new PassThruMsg();
                    Marshal.StructureToPtr(msgs[i], IntPtr.Add(pMsgs, i * msgSize), false);
                }

                var result = _passThruReadMsgs(channelId, pMsgs, ref numMsgs, timeout);

                // Copy back the messages
                for (int i = 0; i < numMsgs && i < msgs.Length; i++)
                {
                    msgs[i] = Marshal.PtrToStructure<PassThruMsg>(IntPtr.Add(pMsgs, i * msgSize));
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsgs);
            }
        }

        /// <summary>
        /// Write messages to channel
        /// </summary>
        public J2534Error PassThruWriteMsgs(uint channelId, PassThruMsg[] msgs, ref uint numMsgs, uint timeout)
        {
            if (_passThruWriteMsgs == null)
                throw new J2534Exception("PassThruWriteMsgs not available");

            int msgSize = Marshal.SizeOf<PassThruMsg>();
            IntPtr pMsgs = Marshal.AllocHGlobal(msgSize * msgs.Length);
            try
            {
                for (int i = 0; i < msgs.Length; i++)
                {
                    Marshal.StructureToPtr(msgs[i], IntPtr.Add(pMsgs, i * msgSize), false);
                }

                return _passThruWriteMsgs(channelId, pMsgs, ref numMsgs, timeout);
            }
            finally
            {
                Marshal.FreeHGlobal(pMsgs);
            }
        }

        /// <summary>
        /// Start a message filter (required to receive messages)
        /// </summary>
        public J2534Error PassThruStartMsgFilter(uint channelId, FilterType filterType, PassThruMsg? maskMsg, PassThruMsg? patternMsg, PassThruMsg? flowControlMsg, out uint filterId)
        {
            filterId = 0;
            if (_passThruStartMsgFilter == null)
                throw new J2534Exception("PassThruStartMsgFilter not available");

            int msgSize = Marshal.SizeOf<PassThruMsg>();
            IntPtr pMask = IntPtr.Zero, pPattern = IntPtr.Zero, pFlowControl = IntPtr.Zero;

            try
            {
                if (maskMsg.HasValue)
                {
                    pMask = Marshal.AllocHGlobal(msgSize);
                    Marshal.StructureToPtr(maskMsg.Value, pMask, false);
                }
                if (patternMsg.HasValue)
                {
                    pPattern = Marshal.AllocHGlobal(msgSize);
                    Marshal.StructureToPtr(patternMsg.Value, pPattern, false);
                }
                if (flowControlMsg.HasValue)
                {
                    pFlowControl = Marshal.AllocHGlobal(msgSize);
                    Marshal.StructureToPtr(flowControlMsg.Value, pFlowControl, false);
                }

                return _passThruStartMsgFilter(channelId, filterType, pMask, pPattern, pFlowControl, out filterId);
            }
            finally
            {
                if (pMask != IntPtr.Zero) Marshal.FreeHGlobal(pMask);
                if (pPattern != IntPtr.Zero) Marshal.FreeHGlobal(pPattern);
                if (pFlowControl != IntPtr.Zero) Marshal.FreeHGlobal(pFlowControl);
            }
        }

        /// <summary>
        /// Stop a message filter
        /// </summary>
        public J2534Error PassThruStopMsgFilter(uint channelId, uint filterId)
        {
            if (_passThruStopMsgFilter == null)
                throw new J2534Exception("PassThruStopMsgFilter not available");
            return _passThruStopMsgFilter(channelId, filterId);
        }

        /// <summary>
        /// IOCTL command (read voltage, clear buffers, etc.)
        /// </summary>
        public J2534Error PassThruIoctl(uint channelId, IoctlId ioctlId, IntPtr pInput, IntPtr pOutput)
        {
            if (_passThruIoctl == null)
                throw new J2534Exception("PassThruIoctl not available");
            return _passThruIoctl(channelId, ioctlId, pInput, pOutput);
        }

        /// <summary>
        /// Read battery voltage via IOCTL
        /// </summary>
        public J2534Error ReadVoltage(uint deviceId, out double voltage)
        {
            voltage = 0;
            IntPtr pOutput = Marshal.AllocHGlobal(4);
            try
            {
                var result = PassThruIoctl(deviceId, IoctlId.READ_VBATT, IntPtr.Zero, pOutput);
                if (result == J2534Error.STATUS_NOERROR)
                {
                    uint millivolts = (uint)Marshal.ReadInt32(pOutput);
                    voltage = millivolts / 1000.0;
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(pOutput);
            }
        }

        /// <summary>
        /// Clear receive buffer
        /// </summary>
        public J2534Error ClearRxBuffer(uint channelId)
        {
            return PassThruIoctl(channelId, IoctlId.CLEAR_RX_BUFFER, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Clear transmit buffer
        /// </summary>
        public J2534Error ClearTxBuffer(uint channelId)
        {
            return PassThruIoctl(channelId, IoctlId.CLEAR_TX_BUFFER, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Read version information
        /// </summary>
        public J2534Error PassThruReadVersion(uint deviceId, out string firmware, out string dll, out string api)
        {
            firmware = dll = api = "";
            if (_passThruReadVersion == null)
                throw new J2534Exception("PassThruReadVersion not available");

            var sbFirmware = new StringBuilder(256);
            var sbDll = new StringBuilder(256);
            var sbApi = new StringBuilder(256);

            var result = _passThruReadVersion(deviceId, sbFirmware, sbDll, sbApi);
            if (result == J2534Error.STATUS_NOERROR)
            {
                firmware = sbFirmware.ToString();
                dll = sbDll.ToString();
                api = sbApi.ToString();
            }
            return result;
        }

        /// <summary>
        /// Get last error description
        /// </summary>
        public string GetLastError()
        {
            if (_passThruGetLastError == null)
                return "PassThruGetLastError not available";

            var sb = new StringBuilder(256);
            _passThruGetLastError(sb);
            return sb.ToString();
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_dllHandle != IntPtr.Zero)
                {
                    FreeLibrary(_dllHandle);
                    _dllHandle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~J2534Api()
        {
            Dispose(false);
        }
    }

    #region Enums

    /// <summary>
    /// J2534 Error codes
    /// </summary>
    public enum J2534Error : uint
    {
        STATUS_NOERROR = 0x00,
        ERR_NOT_SUPPORTED = 0x01,
        ERR_INVALID_CHANNEL_ID = 0x02,
        ERR_INVALID_PROTOCOL_ID = 0x03,
        ERR_NULL_PARAMETER = 0x04,
        ERR_INVALID_IOCTL_VALUE = 0x05,
        ERR_INVALID_FLAGS = 0x06,
        ERR_FAILED = 0x07,
        ERR_DEVICE_NOT_CONNECTED = 0x08,
        ERR_TIMEOUT = 0x09,
        ERR_INVALID_MSG = 0x0A,
        ERR_INVALID_TIME_INTERVAL = 0x0B,
        ERR_EXCEEDED_LIMIT = 0x0C,
        ERR_INVALID_MSG_ID = 0x0D,
        ERR_DEVICE_IN_USE = 0x0E,
        ERR_INVALID_IOCTL_ID = 0x0F,
        ERR_BUFFER_EMPTY = 0x10,
        ERR_BUFFER_FULL = 0x11,
        ERR_BUFFER_OVERFLOW = 0x12,
        ERR_PIN_INVALID = 0x13,
        ERR_CHANNEL_IN_USE = 0x14,
        ERR_MSG_PROTOCOL_ID = 0x15,
        ERR_INVALID_FILTER_ID = 0x16,
        ERR_NO_FLOW_CONTROL = 0x17,
        ERR_NOT_UNIQUE = 0x18,
        ERR_INVALID_BAUDRATE = 0x19,
        ERR_INVALID_DEVICE_ID = 0x1A
    }

    /// <summary>
    /// J2534 Protocol IDs
    /// </summary>
    public enum ProtocolId : uint
    {
        J1850VPW = 0x01,
        J1850PWM = 0x02,
        ISO9141 = 0x03,
        ISO14230 = 0x04,
        CAN = 0x05,
        ISO15765 = 0x06,  // CAN with ISO-TP (used for Ford PATS)
        SCI_A_ENGINE = 0x07,
        SCI_A_TRANS = 0x08,
        SCI_B_ENGINE = 0x09,
        SCI_B_TRANS = 0x0A
    }

    /// <summary>
    /// Connection flags
    /// </summary>
    [Flags]
    public enum ConnectFlags : uint
    {
        NONE = 0x00,
        CAN_29BIT_ID = 0x100,
        ISO9141_NO_CHECKSUM = 0x200,
        CAN_ID_BOTH = 0x800,      // Listen to both 11-bit and 29-bit CAN IDs
        ISO9141_K_LINE_ONLY = 0x1000
    }

    /// <summary>
    /// Filter types
    /// </summary>
    public enum FilterType : uint
    {
        PASS_FILTER = 0x01,
        BLOCK_FILTER = 0x02,
        FLOW_CONTROL_FILTER = 0x03
    }

    /// <summary>
    /// IOCTL command IDs
    /// </summary>
    public enum IoctlId : uint
    {
        GET_CONFIG = 0x01,
        SET_CONFIG = 0x02,
        READ_VBATT = 0x03,
        FIVE_BAUD_INIT = 0x04,
        FAST_INIT = 0x05,
        CLEAR_TX_BUFFER = 0x07,
        CLEAR_RX_BUFFER = 0x08,
        CLEAR_PERIODIC_MSGS = 0x09,
        CLEAR_MSG_FILTERS = 0x0A,
        CLEAR_FUNCT_MSG_LOOKUP_TABLE = 0x0B,
        ADD_TO_FUNCT_MSG_LOOKUP_TABLE = 0x0C,
        DELETE_FROM_FUNCT_MSG_LOOKUP_TABLE = 0x0D,
        READ_PROG_VOLTAGE = 0x0E
    }

    /// <summary>
    /// Message transmit flags
    /// </summary>
    [Flags]
    public enum TxFlags : uint
    {
        NONE = 0x00,
        ISO15765_FRAME_PAD = 0x40,
        ISO15765_ADDR_TYPE = 0x80,
        CAN_29BIT_ID = 0x100,
        WAIT_P3_MIN_ONLY = 0x200,
        SW_CAN_HV_TX = 0x400,
        SCI_MODE = 0x400000,
        SCI_TX_VOLTAGE = 0x800000
    }

    /// <summary>
    /// Message receive flags
    /// </summary>
    [Flags]
    public enum RxStatus : uint
    {
        NONE = 0x00,
        TX_MSG_TYPE = 0x01,
        START_OF_MESSAGE = 0x02,
        ISO15765_FIRST_FRAME = 0x02,
        ISO15765_EXT_ADDR = 0x80,
        RX_BREAK = 0x04,
        TX_DONE = 0x08,
        ISO15765_PADDING_ERROR = 0x10,
        ISO15765_ADDR_TYPE = 0x80,
        CAN_29BIT_ID = 0x100
    }

    #endregion

    #region Structures

    /// <summary>
    /// J2534 Message structure (4128 bytes)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PassThruMsg
    {
        public ProtocolId ProtocolID;
        public RxStatus RxStatus;
        public TxFlags TxFlags;
        public uint Timestamp;
        public uint DataSize;
        public uint ExtraDataIndex;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4128)]
        public byte[] Data;

        public PassThruMsg(ProtocolId protocolId)
        {
            ProtocolID = protocolId;
            RxStatus = RxStatus.NONE;
            TxFlags = TxFlags.NONE;
            Timestamp = 0;
            DataSize = 0;
            ExtraDataIndex = 0;
            Data = new byte[4128];
        }

        /// <summary>
        /// Create message for ISO15765 (CAN with ISO-TP)
        /// </summary>
        public static PassThruMsg CreateISO15765Message(uint canId, byte[] data, TxFlags flags = TxFlags.ISO15765_FRAME_PAD)
        {
            var msg = new PassThruMsg(ProtocolId.ISO15765);
            msg.TxFlags = flags;
            msg.Data = new byte[4128];

            // First 4 bytes are the CAN ID
            msg.Data[0] = (byte)((canId >> 24) & 0xFF);
            msg.Data[1] = (byte)((canId >> 16) & 0xFF);
            msg.Data[2] = (byte)((canId >> 8) & 0xFF);
            msg.Data[3] = (byte)(canId & 0xFF);

            // Copy payload after CAN ID
            Array.Copy(data, 0, msg.Data, 4, Math.Min(data.Length, 4124));
            msg.DataSize = (uint)(4 + data.Length);

            return msg;
        }

        /// <summary>
        /// Get the CAN ID from a received message
        /// </summary>
        public uint GetCanId()
        {
            if (Data == null || Data.Length < 4)
                return 0;
            return (uint)((Data[0] << 24) | (Data[1] << 16) | (Data[2] << 8) | Data[3]);
        }

        /// <summary>
        /// Get the payload (without CAN ID header)
        /// </summary>
        public byte[] GetPayload()
        {
            if (Data == null || DataSize <= 4)
                return Array.Empty<byte>();

            int payloadSize = (int)DataSize - 4;
            byte[] payload = new byte[payloadSize];
            Array.Copy(Data, 4, payload, 0, payloadSize);
            return payload;
        }

        public string ToHexString()
        {
            if (Data == null || DataSize == 0)
                return "";
            return $"[{GetCanId():X3}] {BitConverter.ToString(GetPayload()).Replace("-", " ")}";
        }
    }

    #endregion

    #region Exceptions

    public class J2534Exception : Exception
    {
        public J2534Error? ErrorCode { get; }

        public J2534Exception(string message) : base(message) { }

        public J2534Exception(string message, J2534Error errorCode) : base($"{message} (Error: {errorCode})")
        {
            ErrorCode = errorCode;
        }

        public J2534Exception(string message, Exception inner) : base(message, inner) { }
    }

    #endregion
}
