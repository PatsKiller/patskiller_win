using System;
using System.Runtime.InteropServices;
using PatsKillerPro.Utils;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Represents a connected J2534 device
    /// </summary>
    public class J2534Device : IDisposable
    {
        private readonly J2534DeviceInfo _deviceInfo;
        private IntPtr _libraryHandle = IntPtr.Zero;
        private uint _deviceId = 0;
        private bool _isConnected = false;
        private bool _disposed = false;

        // Function delegates
        private delegate int PassThruOpenDelegate(IntPtr pName, out uint pDeviceID);
        private delegate int PassThruCloseDelegate(uint DeviceID);
        private delegate int PassThruConnectDelegate(uint DeviceID, uint ProtocolID, uint Flags, uint BaudRate, out uint pChannelID);
        private delegate int PassThruDisconnectDelegate(uint ChannelID);
        private delegate int PassThruReadMsgsDelegate(uint ChannelID, IntPtr pMsg, ref uint pNumMsgs, uint Timeout);
        private delegate int PassThruWriteMsgsDelegate(uint ChannelID, IntPtr pMsg, ref uint pNumMsgs, uint Timeout);
        private delegate int PassThruStartPeriodicMsgDelegate(uint ChannelID, IntPtr pMsg, out uint pMsgID, uint TimeInterval);
        private delegate int PassThruStopPeriodicMsgDelegate(uint ChannelID, uint MsgID);
        private delegate int PassThruStartMsgFilterDelegate(uint ChannelID, uint FilterType, IntPtr pMaskMsg, IntPtr pPatternMsg, IntPtr pFlowControlMsg, out uint pFilterID);
        private delegate int PassThruStopMsgFilterDelegate(uint ChannelID, uint FilterID);
        private delegate int PassThruSetProgrammingVoltageDelegate(uint DeviceID, uint PinNumber, uint Voltage);
        private delegate int PassThruReadVersionDelegate(uint DeviceID, IntPtr pFirmwareVersion, IntPtr pDllVersion, IntPtr pApiVersion);
        private delegate int PassThruGetLastErrorDelegate(IntPtr pErrorDescription);
        private delegate int PassThruIoctlDelegate(uint ChannelID, uint IoctlID, IntPtr pInput, IntPtr pOutput);

        private PassThruOpenDelegate? _passThruOpen;
        private PassThruCloseDelegate? _passThruClose;
        private PassThruConnectDelegate? _passThruConnect;
        private PassThruDisconnectDelegate? _passThruDisconnect;
        private PassThruReadMsgsDelegate? _passThruReadMsgs;
        private PassThruWriteMsgsDelegate? _passThruWriteMsgs;
        private PassThruStartPeriodicMsgDelegate? _passThruStartPeriodicMsg;
        private PassThruStopPeriodicMsgDelegate? _passThruStopPeriodicMsg;
        private PassThruStartMsgFilterDelegate? _passThruStartMsgFilter;
        private PassThruStopMsgFilterDelegate? _passThruStopMsgFilter;
        private PassThruSetProgrammingVoltageDelegate? _passThruSetProgrammingVoltage;
        private PassThruReadVersionDelegate? _passThruReadVersion;
        private PassThruGetLastErrorDelegate? _passThruGetLastError;
        private PassThruIoctlDelegate? _passThruIoctl;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        public J2534Device(J2534DeviceInfo deviceInfo)
        {
            _deviceInfo = deviceInfo ?? throw new ArgumentNullException(nameof(deviceInfo));
        }

        public string Name => _deviceInfo.Name;
        public bool IsConnected => _isConnected;
        public uint DeviceId => _deviceId;

        /// <summary>
        /// Connects to the J2534 device
        /// </summary>
        public void Connect()
        {
            if (_isConnected)
                throw new J2534Exception("Device already connected");

            Logger.Info($"Loading J2534 DLL: {_deviceInfo.FunctionLibrary}");

            // Load the DLL
            _libraryHandle = LoadLibrary(_deviceInfo.FunctionLibrary);
            if (_libraryHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                throw new J2534Exception($"Failed to load J2534 DLL: {_deviceInfo.FunctionLibrary} (Error: {error})");
            }

            // Get function pointers
            LoadFunctions();

            // Open device
            Logger.Info($"Opening J2534 device: {_deviceInfo.Name}");
            var result = _passThruOpen!(IntPtr.Zero, out _deviceId);
            if (result != 0)
            {
                var errorMsg = GetLastError();
                throw new J2534Exception($"PassThruOpen failed: {errorMsg} (Code: {result})");
            }

            _isConnected = true;
            Logger.Info($"J2534 device opened successfully. DeviceID: {_deviceId}");

            // Log version info
            LogVersionInfo();
        }

        private void LoadFunctions()
        {
            _passThruOpen = GetFunction<PassThruOpenDelegate>("PassThruOpen");
            _passThruClose = GetFunction<PassThruCloseDelegate>("PassThruClose");
            _passThruConnect = GetFunction<PassThruConnectDelegate>("PassThruConnect");
            _passThruDisconnect = GetFunction<PassThruDisconnectDelegate>("PassThruDisconnect");
            _passThruReadMsgs = GetFunction<PassThruReadMsgsDelegate>("PassThruReadMsgs");
            _passThruWriteMsgs = GetFunction<PassThruWriteMsgsDelegate>("PassThruWriteMsgs");
            _passThruStartPeriodicMsg = GetFunction<PassThruStartPeriodicMsgDelegate>("PassThruStartPeriodicMsg");
            _passThruStopPeriodicMsg = GetFunction<PassThruStopPeriodicMsgDelegate>("PassThruStopPeriodicMsg");
            _passThruStartMsgFilter = GetFunction<PassThruStartMsgFilterDelegate>("PassThruStartMsgFilter");
            _passThruStopMsgFilter = GetFunction<PassThruStopMsgFilterDelegate>("PassThruStopMsgFilter");
            _passThruSetProgrammingVoltage = GetFunction<PassThruSetProgrammingVoltageDelegate>("PassThruSetProgrammingVoltage");
            _passThruReadVersion = GetFunction<PassThruReadVersionDelegate>("PassThruReadVersion");
            _passThruGetLastError = GetFunction<PassThruGetLastErrorDelegate>("PassThruGetLastError");
            _passThruIoctl = GetFunction<PassThruIoctlDelegate>("PassThruIoctl");
        }

        private T GetFunction<T>(string functionName) where T : Delegate
        {
            var ptr = GetProcAddress(_libraryHandle, functionName);
            if (ptr == IntPtr.Zero)
            {
                throw new J2534Exception($"Failed to get function: {functionName}");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        private void LogVersionInfo()
        {
            try
            {
                var firmwareVersion = Marshal.AllocHGlobal(80);
                var dllVersion = Marshal.AllocHGlobal(80);
                var apiVersion = Marshal.AllocHGlobal(80);

                try
                {
                    var result = _passThruReadVersion!(_deviceId, firmwareVersion, dllVersion, apiVersion);
                    if (result == 0)
                    {
                        var fw = Marshal.PtrToStringAnsi(firmwareVersion);
                        var dll = Marshal.PtrToStringAnsi(dllVersion);
                        var api = Marshal.PtrToStringAnsi(apiVersion);
                        Logger.Info($"J2534 Version - Firmware: {fw}, DLL: {dll}, API: {api}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(firmwareVersion);
                    Marshal.FreeHGlobal(dllVersion);
                    Marshal.FreeHGlobal(apiVersion);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to read version info: {ex.Message}");
            }
        }

        /// <summary>
        /// Opens a communication channel
        /// </summary>
        public J2534Channel OpenChannel(Protocol protocol, uint baudRate, ConnectFlags flags = ConnectFlags.NONE)
        {
            if (!_isConnected)
                throw new J2534Exception("Device not connected");

            Logger.Info($"Opening channel - Protocol: {protocol}, BaudRate: {baudRate}");

            var result = _passThruConnect!(_deviceId, (uint)protocol, (uint)flags, baudRate, out uint channelId);
            if (result != 0)
            {
                var errorMsg = GetLastError();
                throw new J2534Exception($"PassThruConnect failed: {errorMsg} (Code: {result})");
            }

            Logger.Info($"Channel opened. ChannelID: {channelId}");

            return new J2534Channel(this, channelId, protocol, baudRate);
        }

        /// <summary>
        /// Reads battery voltage
        /// </summary>
        public double ReadBatteryVoltage()
        {
            if (!_isConnected)
                throw new J2534Exception("Device not connected");

            var input = IntPtr.Zero;
            var output = Marshal.AllocHGlobal(4);

            try
            {
                var result = _passThruIoctl!(_deviceId, (uint)IoctlId.READ_VBATT, input, output);
                if (result != 0)
                {
                    Logger.Warning($"ReadBatteryVoltage failed: {GetLastError()}");
                    return 0;
                }

                var millivolts = Marshal.ReadInt32(output);
                return millivolts / 1000.0;
            }
            finally
            {
                Marshal.FreeHGlobal(output);
            }
        }

        internal int PassThruDisconnect(uint channelId)
        {
            return _passThruDisconnect!(channelId);
        }

        internal int PassThruReadMsgs(uint channelId, IntPtr pMsg, ref uint numMsgs, uint timeout)
        {
            return _passThruReadMsgs!(channelId, pMsg, ref numMsgs, timeout);
        }

        internal int PassThruWriteMsgs(uint channelId, IntPtr pMsg, ref uint numMsgs, uint timeout)
        {
            return _passThruWriteMsgs!(channelId, pMsg, ref numMsgs, timeout);
        }

        internal int PassThruStartMsgFilter(uint channelId, uint filterType, IntPtr mask, IntPtr pattern, IntPtr flowControl, out uint filterId)
        {
            return _passThruStartMsgFilter!(channelId, filterType, mask, pattern, flowControl, out filterId);
        }

        internal int PassThruStopMsgFilter(uint channelId, uint filterId)
        {
            return _passThruStopMsgFilter!(channelId, filterId);
        }

        internal int PassThruIoctl(uint channelId, uint ioctlId, IntPtr input, IntPtr output)
        {
            return _passThruIoctl!(channelId, ioctlId, input, output);
        }

        internal int PassThruStartPeriodicMsg(uint channelId, IntPtr msg, out uint msgId, uint interval)
        {
            return _passThruStartPeriodicMsg!(channelId, msg, out msgId, interval);
        }

        internal int PassThruStopPeriodicMsg(uint channelId, uint msgId)
        {
            return _passThruStopPeriodicMsg!(channelId, msgId);
        }

        /// <summary>
        /// Gets the last error message from the J2534 device
        /// </summary>
        public string GetLastError()
        {
            var errorPtr = Marshal.AllocHGlobal(256);
            try
            {
                _passThruGetLastError!(errorPtr);
                return Marshal.PtrToStringAnsi(errorPtr) ?? "Unknown error";
            }
            finally
            {
                Marshal.FreeHGlobal(errorPtr);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_isConnected && _passThruClose != null)
                {
                    try
                    {
                        Logger.Info("Closing J2534 device");
                        _passThruClose(_deviceId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error closing device: {ex.Message}");
                    }
                }

                if (_libraryHandle != IntPtr.Zero)
                {
                    FreeLibrary(_libraryHandle);
                    _libraryHandle = IntPtr.Zero;
                }

                _isConnected = false;
                _disposed = true;
            }
        }
    }
}
