using System;
using System.Threading.Tasks;
using PatsKillerPro.J2534;

namespace PatsKillerPro.Communication
{
    /// <summary>
    /// UDS Service - Full backward compatibility wrapper
    /// Provides ALL methods that PatsOperations.cs and legacy code expects
    /// </summary>
    public class UdsService : IDisposable
    {
        private FordUdsProtocol? _uds;
        private J2534Api? _api;
        private bool _disposed;
        private uint _currentModuleAddress;

        public bool IsConnected => _uds?.IsConnected ?? false;

        /// <summary>
        /// Initialize UDS service with a J2534 device
        /// </summary>
        public bool Initialize(J2534DeviceInfo device)
        {
            try
            {
                _api = new J2534Api(device.FunctionLibrary);
                _uds = new FordUdsProtocol(_api);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Connect to vehicle CAN bus
        /// </summary>
        public bool Connect(uint baudRate = 500000)
        {
            if (_uds == null) return false;
            var result = _uds.Connect(baudRate);
            return result == J2534Error.STATUS_NOERROR;
        }

        /// <summary>
        /// Disconnect from vehicle
        /// </summary>
        public void Disconnect()
        {
            _uds?.Disconnect();
        }

        #region Module Targeting

        /// <summary>
        /// Set target module for communication
        /// </summary>
        public void SetTargetModule(uint txId, uint rxId)
        {
            _currentModuleAddress = txId;
            _uds?.SetTargetModule(txId, rxId);
        }

        /// <summary>
        /// Set target module by address (calculates Tx/Rx)
        /// </summary>
        public void SetTargetModule(uint moduleAddress)
        {
            _currentModuleAddress = moduleAddress;
            _uds?.SetTargetModule(moduleAddress, moduleAddress + 8);
        }

        #endregion

        #region Raw Request

        /// <summary>
        /// Send raw UDS request and get response
        /// </summary>
        public byte[]? SendRequest(byte[] request, int timeout = 2000)
        {
            if (_uds == null) return null;
            var response = _uds.SendRequest(request, timeout);
            return response.Success ? response.Data : null;
        }

        /// <summary>
        /// Send raw request (alias)
        /// </summary>
        public byte[]? SendRawRequest(byte[] request, int timeout = 2000)
        {
            return SendRequest(request, timeout);
        }

        /// <summary>
        /// Send raw request with module address
        /// </summary>
        public byte[]? SendRawRequest(uint moduleAddress, byte[] request, int timeout = 2000)
        {
            SetTargetModule(moduleAddress);
            return SendRequest(request, timeout);
        }

        #endregion

        #region Read/Write Data By Identifier

        /// <summary>
        /// Read data by identifier (0x22) - single DID
        /// </summary>
        public byte[]? ReadDataByIdentifier(ushort did)
        {
            if (_uds == null) return null;
            var response = _uds.ReadDataByIdentifier(did);
            return response.Success ? response.Data : null;
        }

        /// <summary>
        /// Read data by identifier - with module address
        /// </summary>
        public byte[]? ReadDataByIdentifier(uint moduleAddress, ushort did)
        {
            SetTargetModule(moduleAddress);
            return ReadDataByIdentifier(did);
        }

        /// <summary>
        /// Write data by identifier (0x2E) - 2 args
        /// </summary>
        public bool WriteDataByIdentifier(ushort did, byte[] data)
        {
            if (_uds == null) return false;
            var response = _uds.WriteDataByIdentifier(did, data);
            return response.Success;
        }

        /// <summary>
        /// Write data by identifier - 3 args with module address
        /// </summary>
        public bool WriteDataByIdentifier(uint moduleAddress, ushort did, byte[] data)
        {
            SetTargetModule(moduleAddress);
            return WriteDataByIdentifier(did, data);
        }

        #endregion

        #region Diagnostic Session Control - ALL OVERLOADS

        /// <summary>
        /// Start diagnostic session (0x10) - byte
        /// </summary>
        public bool StartDiagnosticSession(byte sessionType)
        {
            if (_uds == null) return false;
            var response = _uds.DiagnosticSessionControl((DiagnosticSession)sessionType);
            return response.Success;
        }

        /// <summary>
        /// Start diagnostic session - uint
        /// </summary>
        public bool StartDiagnosticSession(uint sessionType)
        {
            return StartDiagnosticSession((byte)sessionType);
        }

        /// <summary>
        /// Start diagnostic session - int
        /// </summary>
        public bool StartDiagnosticSession(int sessionType)
        {
            return StartDiagnosticSession((byte)sessionType);
        }

        /// <summary>
        /// Start extended session - no args
        /// </summary>
        public bool StartExtendedSession()
        {
            return StartDiagnosticSession((byte)0x03);
        }

        /// <summary>
        /// Start extended session - with module address (uint)
        /// </summary>
        public bool StartExtendedSession(uint moduleAddress)
        {
            SetTargetModule(moduleAddress);
            return StartDiagnosticSession((byte)0x03);
        }

        /// <summary>
        /// Start extended session - with module address (int)
        /// </summary>
        public bool StartExtendedSession(int moduleAddress)
        {
            SetTargetModule((uint)moduleAddress);
            return StartDiagnosticSession((byte)0x03);
        }

        /// <summary>
        /// Start programming session
        /// </summary>
        public bool StartProgrammingSession()
        {
            return StartDiagnosticSession((byte)0x02);
        }

        /// <summary>
        /// Start programming session - with module
        /// </summary>
        public bool StartProgrammingSession(uint moduleAddress)
        {
            SetTargetModule(moduleAddress);
            return StartDiagnosticSession((byte)0x02);
        }

        /// <summary>
        /// Start default session
        /// </summary>
        public bool StartDefaultSession()
        {
            return StartDiagnosticSession((byte)0x01);
        }

        /// <summary>
        /// Start default session - with module
        /// </summary>
        public bool StartDefaultSession(uint moduleAddress)
        {
            SetTargetModule(moduleAddress);
            return StartDiagnosticSession((byte)0x01);
        }

        #endregion

        #region Security Access - ALL OVERLOADS

        /// <summary>
        /// Request security seed - byte level
        /// </summary>
        public byte[]? RequestSecuritySeed(byte level = 0x01)
        {
            if (_uds == null) return null;
            var response = _uds.SecurityAccessRequestSeed(level);
            return response.Success ? response.Data : null;
        }

        /// <summary>
        /// Request security seed - uint level
        /// </summary>
        public byte[]? RequestSecuritySeed(uint level)
        {
            return RequestSecuritySeed((byte)level);
        }

        /// <summary>
        /// Request security seed - int level
        /// </summary>
        public byte[]? RequestSecuritySeed(int level)
        {
            return RequestSecuritySeed((byte)level);
        }

        /// <summary>
        /// Request security access (alias) - byte
        /// </summary>
        public byte[]? RequestSecurityAccess(byte level = 0x01)
        {
            return RequestSecuritySeed(level);
        }

        /// <summary>
        /// Request security access - uint
        /// </summary>
        public byte[]? RequestSecurityAccess(uint level)
        {
            return RequestSecuritySeed((byte)level);
        }

        /// <summary>
        /// Request security access - int
        /// </summary>
        public byte[]? RequestSecurityAccess(int level)
        {
            return RequestSecuritySeed((byte)level);
        }

        /// <summary>
        /// Send security key - byte level
        /// </summary>
        public bool SendSecurityKey(byte[] key, byte level = 0x02)
        {
            if (_uds == null) return false;
            var response = _uds.SecurityAccessSendKey(key, level);
            return response.Success;
        }

        /// <summary>
        /// Send security key - uint level
        /// </summary>
        public bool SendSecurityKey(byte[] key, uint level)
        {
            return SendSecurityKey(key, (byte)level);
        }

        /// <summary>
        /// Send security key - int level
        /// </summary>
        public bool SendSecurityKey(byte[] key, int level)
        {
            return SendSecurityKey(key, (byte)level);
        }

        #endregion

        #region Routine Control - ALL OVERLOADS

        /// <summary>
        /// Routine control - byte controlType
        /// </summary>
        public bool RoutineControl(byte controlType, ushort routineId, byte[]? data = null)
        {
            if (_uds == null) return false;
            var response = _uds.RoutineControl((RoutineControlType)controlType, routineId, data);
            return response.Success;
        }

        /// <summary>
        /// Routine control - uint controlType
        /// </summary>
        public bool RoutineControl(uint controlType, ushort routineId, byte[]? data = null)
        {
            return RoutineControl((byte)controlType, routineId, data);
        }

        /// <summary>
        /// Routine control - int controlType
        /// </summary>
        public bool RoutineControl(int controlType, ushort routineId, byte[]? data = null)
        {
            return RoutineControl((byte)controlType, routineId, data);
        }

        /// <summary>
        /// Start routine
        /// </summary>
        public bool StartRoutine(ushort routineId, byte[]? data = null)
        {
            return RoutineControl((byte)0x01, routineId, data);
        }

        /// <summary>
        /// Stop routine
        /// </summary>
        public bool StopRoutine(ushort routineId)
        {
            return RoutineControl((byte)0x02, routineId, null);
        }

        /// <summary>
        /// Request routine results
        /// </summary>
        public bool RequestRoutineResults(ushort routineId)
        {
            return RoutineControl((byte)0x03, routineId, null);
        }

        #endregion

        #region Input/Output Control (0x2F)

        /// <summary>
        /// Input/Output Control by Identifier (0x2F)
        /// </summary>
        public byte[]? InputOutputControl(ushort did, byte controlParam, byte[]? controlState = null)
        {
            if (_uds == null) return null;
            
            int dataLen = controlState?.Length ?? 0;
            var request = new byte[4 + dataLen];
            request[0] = 0x2F; // InputOutputControlByIdentifier
            request[1] = (byte)(did >> 8);
            request[2] = (byte)(did & 0xFF);
            request[3] = controlParam;
            if (controlState != null)
                Array.Copy(controlState, 0, request, 4, controlState.Length);
            
            return SendRequest(request);
        }

        /// <summary>
        /// Input/Output Control - with module address
        /// </summary>
        public byte[]? InputOutputControl(uint moduleAddress, ushort did, byte controlParam, byte[]? controlState = null)
        {
            SetTargetModule(moduleAddress);
            return InputOutputControl(did, controlParam, controlState);
        }

        /// <summary>
        /// Return control to ECU (0x2F xx xx 00)
        /// </summary>
        public bool ReturnControlToEcu(ushort did)
        {
            var result = InputOutputControl(did, 0x00);
            return result != null;
        }

        /// <summary>
        /// Short term adjustment (0x2F xx xx 03)
        /// </summary>
        public bool ShortTermAdjustment(ushort did, byte[] value)
        {
            var result = InputOutputControl(did, 0x03, value);
            return result != null;
        }

        #endregion

        #region Other UDS Services

        /// <summary>
        /// Tester present (0x3E)
        /// </summary>
        public bool SendTesterPresent()
        {
            if (_uds == null) return false;
            var response = _uds.TesterPresent();
            return response.Success;
        }

        /// <summary>
        /// Clear DTCs (0x14)
        /// </summary>
        public bool ClearDtcs()
        {
            if (_uds == null) return false;
            var response = _uds.ClearDtc();
            return response.Success;
        }

        /// <summary>
        /// ECU Reset (0x11) - byte
        /// </summary>
        public bool EcuReset(byte resetType = 0x01)
        {
            if (_uds == null) return false;
            var response = _uds.EcuReset((ResetType)resetType);
            return response.Success;
        }

        /// <summary>
        /// ECU Reset - uint
        /// </summary>
        public bool EcuReset(uint resetType)
        {
            return EcuReset((byte)resetType);
        }

        /// <summary>
        /// ECU Reset - int
        /// </summary>
        public bool EcuReset(int resetType)
        {
            return EcuReset((byte)resetType);
        }

        /// <summary>
        /// Read DTCs (0x19)
        /// </summary>
        public byte[]? ReadDtcs(byte reportType = 0x02, byte statusMask = 0xFF)
        {
            if (_uds == null) return null;
            var response = _uds.ReadDtcInformation((DtcReportType)reportType, statusMask);
            return response.Success ? response.Data : null;
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
                if (disposing)
                {
                    _uds?.Disconnect();
                    _api?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
