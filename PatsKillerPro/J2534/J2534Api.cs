using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// J2534 API v04.04 Native Interop
    /// Supports: VCM II, VCM III, Mongoose, CarDAQ, Autel, Topdon, VXDIAG, etc.
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
        private delegate J2534Error PassThruReadMsgsDelegate(uint channelId, IntPtr pMsg, ref uint pNumMsgs, uint timeout);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruWriteMsgsDelegate(uint channelId, IntPtr pMsg, ref uint pNumMsgs, uint timeout);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruStartMsgFilterDelegate(uint channelId, FilterType filterType, IntPtr pMaskMsg, IntPtr pPatternMsg, IntPtr pFlowControlMsg, out uint pMsgId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruStopMsgFilterDelegate(uint channelId, uint msgId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruIoctlDelegate(uint handle, IoctlId ioctlId, IntPtr pInput, IntPtr pOutput);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruReadVersionDelegate(uint deviceId, StringBuilder firmwareVersion, StringBuilder dllVersion, StringBuilder apiVersion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate J2534Error PassThruGetLastErrorDelegate(StringBuilder errorDescription);

        #endregion

        #region Native Methods

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        #endregion

        public J2534Api(string dllPath)
        {
            DllPath = dllPath;
            LoadLibrary(dllPath);
        }

        private void LoadLibrary(string dllPath)
        {
            _dllHandle = LoadLibraryW(dllPath);
            if (_dllHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new J2534Exception($"Failed to load J2534 DLL: {dllPath}. Error: {error}");
            }

            // Load function pointers
            _passThruOpen = GetDelegate<PassThruOpenDelegate>("PassThruOpen");
            _passThruClose = GetDelegate<PassThruCloseDelegate>("PassThruClose");
            _passThruConnect = GetDelegate<PassThruConnectDelegate>("PassThruConnect");
            _passThruDisconnect = GetDelegate<PassThruDisconnectDelegate>("PassThruDisconnect");
            _passThruReadMsgs = GetDelegate<PassThruReadMsgsDelegate>("PassThruReadMsgs");
            _passThruWriteMsgs = GetDelegate<PassThruWriteMsgsDelegate>("PassThruWriteMsgs");
            _passThruStartMsgFilter = GetDelegate<PassThruStartMsgFilterDelegate>("PassThruStartMsgFilter");
            _passThruStopMsgFilter = GetDelegate<PassThruStopMsgFilterDelegate>("PassThruStopMsgFilter");
            _passThruIoctl = GetDelegate<PassThruIoctlDelegate>("PassThruIoctl");
            _passThruReadVersion = GetDelegate<PassThruReadVersionDelegate>("PassThruReadVersion");
            _passThruGetLastError = GetDelegate<PassThruGetLastErrorDelegate>("PassThruGetLastError");
        }

        private T? GetDelegate<T>(string functionName) where T : Delegate
        {
            IntPtr procAddress = GetProcAddress(_dllHandle, functionName);
            if (procAddress == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer<T>(procAddress);
        }

        #region PassThru Methods

        public J2534Error PassThruOpen(out uint deviceId)
        {
            deviceId = 0;
            if (_passThruOpen == null) return J2534Error.ERR_FAILED;
            return _passThruOpen(IntPtr.Zero, out deviceId);
        }

        public J2534Error PassThruClose(uint deviceId)
        {
            if (_passThruClose == null) return J2534Error.ERR_FAILED;
            return _passThruClose(deviceId);
        }

        public J2534Error PassThruConnect(uint deviceId, ProtocolId protocolId, ConnectFlags flags, uint baudRate, out uint channelId)
        {
            channelId = 0;
            if (_passThruConnect == null) return J2534Error.ERR_FAILED;
            return _passThruConnect(deviceId, protocolId, flags, baudRate, out channelId);
        }

        public J2534Error PassThruDisconnect(uint channelId)
        {
            if (_passThruDisconnect == null) return J2534Error.ERR_FAILED;
            return _passThruDisconnect(channelId);
        }

        public J2534Error PassThruReadMsgs(uint channelId, out PassThruMsg[] msgs, uint timeout, int maxMsgs = 1)
        {
            msgs = new PassThruMsg[maxMsgs];
            if (_passThruReadMsgs == null) return J2534Error.ERR_FAILED;

            int msgSize = Marshal.SizeOf(typeof(PassThruMsg));
            IntPtr pMsgs = Marshal.AllocHGlobal(msgSize * maxMsgs);
            try
            {
                uint numMsgs = (uint)maxMsgs;
                var result = _passThruReadMsgs(channelId, pMsgs, ref numMsgs, timeout);
                
                if (result == J2534Error.STATUS_NOERROR && numMsgs > 0)
                {
                    msgs = new PassThruMsg[numMsgs];
                    for (int i = 0; i < numMsgs; i++)
                    {
                        IntPtr msgPtr = IntPtr.Add(pMsgs, i * msgSize);
                        msgs[i] = Marshal.PtrToStructure<PassThruMsg>(msgPtr);
                    }
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(pMsgs);
            }
        }

        public J2534Error PassThruWriteMsgs(uint channelId, PassThruMsg[] msgs, out uint numMsgs, uint timeout)
        {
            numMsgs = 0;
            if (_passThruWriteMsgs == null) return J2534Error.ERR_FAILED;

            int msgSize = Marshal.SizeOf(typeof(PassThruMsg));
            IntPtr pMsgs = Marshal.AllocHGlobal(msgSize * msgs.Length);
            try
            {
                for (int i = 0; i < msgs.Length; i++)
                {
                    IntPtr msgPtr = IntPtr.Add(pMsgs, i * msgSize);
                    Marshal.StructureToPtr(msgs[i], msgPtr, false);
                }
                numMsgs = (uint)msgs.Length;
                return _passThruWriteMsgs(channelId, pMsgs, ref numMsgs, timeout);
            }
            finally
            {
                Marshal.FreeHGlobal(pMsgs);
            }
        }

        public J2534Error PassThruStartMsgFilter(uint channelId, FilterType filterType, PassThruMsg? maskMsg, PassThruMsg? patternMsg, PassThruMsg? flowControlMsg, out uint filterId)
        {
            filterId = 0;
            if (_passThruStartMsgFilter == null) return J2534Error.ERR_FAILED;

            IntPtr pMask = IntPtr.Zero, pPattern = IntPtr.Zero, pFlow = IntPtr.Zero;
            try
            {
                if (maskMsg.HasValue)
                {
                    pMask = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PassThruMsg)));
                    Marshal.StructureToPtr(maskMsg.Value, pMask, false);
                }
                if (patternMsg.HasValue)
                {
                    pPattern = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PassThruMsg)));
                    Marshal.StructureToPtr(patternMsg.Value, pPattern, false);
                }
                if (flowControlMsg.HasValue)
                {
                    pFlow = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PassThruMsg)));
                    Marshal.StructureToPtr(flowControlMsg.Value, pFlow, false);
                }
                return _passThruStartMsgFilter(channelId, filterType, pMask, pPattern, pFlow, out filterId);
            }
            finally
            {
                if (pMask != IntPtr.Zero) Marshal.FreeHGlobal(pMask);
                if (pPattern != IntPtr.Zero) Marshal.FreeHGlobal(pPattern);
                if (pFlow != IntPtr.Zero) Marshal.FreeHGlobal(pFlow);
            }
        }

        public J2534Error PassThruStopMsgFilter(uint channelId, uint filterId)
        {
            if (_passThruStopMsgFilter == null) return J2534Error.ERR_FAILED;
            return _passThruStopMsgFilter(channelId, filterId);
        }

        public J2534Error PassThruIoctl(uint handle, IoctlId ioctlId, IntPtr pInput = default, IntPtr pOutput = default)
        {
            if (_passThruIoctl == null) return J2534Error.ERR_FAILED;
            return _passThruIoctl(handle, ioctlId, pInput, pOutput);
        }

        public J2534Error PassThruReadVersion(uint deviceId, out string firmwareVersion, out string dllVersion, out string apiVersion)
        {
            firmwareVersion = dllVersion = apiVersion = "";
            if (_passThruReadVersion == null) return J2534Error.ERR_FAILED;

            var fw = new StringBuilder(256);
            var dll = new StringBuilder(256);
            var api = new StringBuilder(256);
            var result = _passThruReadVersion(deviceId, fw, dll, api);
            if (result == J2534Error.STATUS_NOERROR)
            {
                firmwareVersion = fw.ToString();
                dllVersion = dll.ToString();
                apiVersion = api.ToString();
            }
            return result;
        }

        public string GetLastError()
        {
            if (_passThruGetLastError == null) return "Unknown error";
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

        ~J2534Api() => Dispose(false);
    }

    #region Enums

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

    public enum ProtocolId : uint
    {
        J1850VPW = 0x01,
        J1850PWM = 0x02,
        ISO9141 = 0x03,
        ISO14230 = 0x04,
        CAN = 0x05,
        ISO15765 = 0x06,
        SCI_A_ENGINE = 0x07,
        SCI_A_TRANS = 0x08,
        SCI_B_ENGINE = 0x09,
        SCI_B_TRANS = 0x0A
    }

    [Flags]
    public enum ConnectFlags : uint
    {
        NONE = 0x0000,
        ISO9141_NO_CHECKSUM = 0x0200,
        CAN_29BIT_ID = 0x0100,
        ISO9141_K_LINE_ONLY = 0x1000
    }

    public enum FilterType : uint
    {
        PASS_FILTER = 0x01,
        BLOCK_FILTER = 0x02,
        FLOW_CONTROL_FILTER = 0x03
    }

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

    [Flags]
    public enum TxFlags : uint
    {
        NONE = 0x00000000,
        ISO15765_FRAME_PAD = 0x00000040,
        ISO15765_ADDR_TYPE = 0x00000080,
        CAN_29BIT_ID = 0x00000100,
        WAIT_P3_MIN_ONLY = 0x00000200,
        TX_NORMAL_TRANSMIT = 0x00000000
    }

    public enum DiagnosticSession : byte
    {
        Default = 0x01,
        Programming = 0x02,
        Extended = 0x03
    }

    public enum ResetType : byte
    {
        HardReset = 0x01,
        KeyOffOnReset = 0x02,
        SoftReset = 0x03
    }

    public enum RoutineControlType : byte
    {
        StartRoutine = 0x01,
        StopRoutine = 0x02,
        RequestResults = 0x03
    }

    public enum DtcReportType : byte
    {
        ReportNumberOfDtcByStatusMask = 0x01,
        ReportDtcByStatusMask = 0x02,
        ReportDtcSnapshotIdentification = 0x03,
        ReportDtcSnapshotRecordByDtcNumber = 0x04,
        ReportSupportedDtc = 0x0A
    }

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct PassThruMsg
    {
        public ProtocolId ProtocolID;
        public uint RxStatus;
        public TxFlags TxFlags;
        public uint Timestamp;
        public uint DataSize;
        public uint ExtraDataIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4128)]
        public byte[] Data;

        public static PassThruMsg Create(ProtocolId protocol, byte[] data, TxFlags flags = TxFlags.NONE)
        {
            var msg = new PassThruMsg
            {
                ProtocolID = protocol,
                TxFlags = flags,
                DataSize = (uint)data.Length,
                ExtraDataIndex = 0,
                Data = new byte[4128]
            };
            Array.Copy(data, msg.Data, data.Length);
            return msg;
        }

        public byte[] GetData()
        {
            if (Data == null || DataSize == 0) return Array.Empty<byte>();
            var result = new byte[DataSize];
            Array.Copy(Data, result, DataSize);
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SConfig
    {
        public uint Parameter;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SConfigList
    {
        public uint NumOfParams;
        public IntPtr ConfigPtr;
    }

    #endregion

    #region Exception

    public class J2534Exception : Exception
    {
        public J2534Error ErrorCode { get; }

        public J2534Exception(string message) : base(message) { }

        public J2534Exception(string message, J2534Error errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public J2534Exception(J2534Error errorCode) : base($"J2534 Error: {errorCode}")
        {
            ErrorCode = errorCode;
        }
    }

    #endregion
}
