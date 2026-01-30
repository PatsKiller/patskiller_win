using System;
using PatsKillerPro.J2534;

namespace PatsKillerPro.Communication
{
    /// <summary>
    /// UDS Service - Backward compatibility wrapper for PatsOperations.cs
    /// </summary>
    public class UdsService : IDisposable
    {
        private FordUdsProtocol? _uds;
        private J2534Api? _api;
        private bool _disposed;

        public bool IsConnected => _uds?.IsConnected ?? false;

        public bool Initialize(J2534DeviceInfo device)
        {
            try
            {
                _api = new J2534Api(device.FunctionLibrary);
                _uds = new FordUdsProtocol(_api);
                return true;
            }
            catch { return false; }
        }

        public bool Connect(uint baudRate = 500000)
        {
            if (_uds == null) return false;
            return _uds.Connect(baudRate) == J2534Error.STATUS_NOERROR;
        }

        public void Disconnect() => _uds?.Disconnect();

        public void SetTargetModule(uint txId, uint rxId) => _uds?.SetTargetModule(txId, rxId);
        public void SetTargetModule(uint moduleAddress) => _uds?.SetTargetModule(moduleAddress, moduleAddress + 8);

        #region Raw Request

        public byte[]? SendRequest(byte[] request, int timeout = 2000)
        {
            var response = _uds?.SendRequest(request, timeout);
            return response?.Success == true ? response.Data : null;
        }

        public byte[]? SendRawRequest(byte[] request, int timeout = 2000) => SendRequest(request, timeout);
        public byte[]? SendRawRequest(uint moduleAddress, byte[] request, int timeout = 2000)
        {
            SetTargetModule(moduleAddress);
            return SendRequest(request, timeout);
        }

        #endregion

        #region Read/Write Data

        public byte[]? ReadDataByIdentifier(ushort did)
        {
            var response = _uds?.ReadDataByIdentifier(did);
            return response?.Success == true ? response.Data : null;
        }

        public byte[]? ReadDataByIdentifier(uint moduleAddress, ushort did)
        {
            SetTargetModule(moduleAddress);
            return ReadDataByIdentifier(did);
        }

        public bool WriteDataByIdentifier(ushort did, byte[] data)
        {
            var response = _uds?.WriteDataByIdentifier(did, data);
            return response?.Success ?? false;
        }

        public bool WriteDataByIdentifier(uint moduleAddress, ushort did, byte[] data)
        {
            SetTargetModule(moduleAddress);
            return WriteDataByIdentifier(did, data);
        }

        #endregion

        #region Session Control

        public bool StartDiagnosticSession(byte sessionType)
        {
            var response = _uds?.DiagnosticSessionControl((DiagnosticSession)sessionType);
            return response?.Success ?? false;
        }

        public bool StartDiagnosticSession(uint sessionType) => StartDiagnosticSession((byte)sessionType);
        public bool StartDiagnosticSession(int sessionType) => StartDiagnosticSession((byte)sessionType);

        public bool StartExtendedSession() => StartDiagnosticSession((byte)0x03);
        public bool StartExtendedSession(uint moduleAddress) { SetTargetModule(moduleAddress); return StartExtendedSession(); }
        public bool StartExtendedSession(int moduleAddress) => StartExtendedSession((uint)moduleAddress);

        public bool StartProgrammingSession() => StartDiagnosticSession((byte)0x02);
        public bool StartProgrammingSession(uint moduleAddress) { SetTargetModule(moduleAddress); return StartProgrammingSession(); }

        public bool StartDefaultSession() => StartDiagnosticSession((byte)0x01);
        public bool StartDefaultSession(uint moduleAddress) { SetTargetModule(moduleAddress); return StartDefaultSession(); }

        #endregion

        #region Security Access

        public byte[]? RequestSecuritySeed(byte level = 0x01)
        {
            var response = _uds?.SecurityAccessRequestSeed(level);
            return response?.Success == true ? response.Data : null;
        }

        public byte[]? RequestSecuritySeed(uint level) => RequestSecuritySeed((byte)level);
        public byte[]? RequestSecuritySeed(int level) => RequestSecuritySeed((byte)level);
        public byte[]? RequestSecurityAccess(byte level = 0x01) => RequestSecuritySeed(level);
        public byte[]? RequestSecurityAccess(uint level) => RequestSecuritySeed((byte)level);
        public byte[]? RequestSecurityAccess(int level) => RequestSecuritySeed((byte)level);
        
        // Two-argument overloads for PatsOperations.cs compatibility
        public byte[]? RequestSecurityAccess(uint moduleAddress, byte level)
        {
            SetTargetModule(moduleAddress);
            return RequestSecuritySeed(level);
        }
        public byte[]? RequestSecurityAccess(uint moduleAddress, uint level) => RequestSecurityAccess(moduleAddress, (byte)level);
        public byte[]? RequestSecurityAccess(uint moduleAddress, int level) => RequestSecurityAccess(moduleAddress, (byte)level);

        public bool SendSecurityKey(byte[] key, byte level = 0x02)
        {
            var response = _uds?.SecurityAccessSendKey(key, level);
            return response?.Success ?? false;
        }

        public bool SendSecurityKey(byte[] key, uint level) => SendSecurityKey(key, (byte)level);
        public bool SendSecurityKey(byte[] key, int level) => SendSecurityKey(key, (byte)level);

        #endregion

        #region Routine Control

        public bool RoutineControl(byte controlType, ushort routineId, byte[]? data = null)
        {
            var response = _uds?.RoutineControl((RoutineControlType)controlType, routineId, data);
            return response?.Success ?? false;
        }

        public bool RoutineControl(uint controlType, ushort routineId, byte[]? data = null) => RoutineControl((byte)controlType, routineId, data);
        public bool RoutineControl(int controlType, ushort routineId, byte[]? data = null) => RoutineControl((byte)controlType, routineId, data);

        public bool StartRoutine(ushort routineId, byte[]? data = null) => RoutineControl((byte)0x01, routineId, data);
        public bool StopRoutine(ushort routineId) => RoutineControl((byte)0x02, routineId, null);
        public bool RequestRoutineResults(ushort routineId) => RoutineControl((byte)0x03, routineId, null);

        #endregion

        #region Input/Output Control

        public byte[]? InputOutputControl(ushort did, byte controlParam, byte[]? controlState = null)
        {
            int dataLen = controlState?.Length ?? 0;
            var request = new byte[4 + dataLen];
            request[0] = 0x2F;
            request[1] = (byte)(did >> 8);
            request[2] = (byte)(did & 0xFF);
            request[3] = controlParam;
            if (controlState != null)
                Array.Copy(controlState, 0, request, 4, controlState.Length);
            return SendRequest(request);
        }

        // Overloads with uint DID for PatsOperations.cs compatibility
        public byte[]? InputOutputControl(uint did, byte controlParam, byte[]? controlState = null)
            => InputOutputControl((ushort)did, controlParam, controlState);

        public byte[]? InputOutputControl(uint moduleAddress, ushort did, byte controlParam, byte[]? controlState = null)
        {
            SetTargetModule(moduleAddress);
            return InputOutputControl(did, controlParam, controlState);
        }
        
        public byte[]? InputOutputControl(uint moduleAddress, uint did, byte controlParam, byte[]? controlState = null)
        {
            SetTargetModule(moduleAddress);
            return InputOutputControl((ushort)did, controlParam, controlState);
        }

        #endregion

        #region Other UDS Services

        public bool SendTesterPresent()
        {
            var response = _uds?.TesterPresent();
            return response?.Success ?? false;
        }

        public bool ClearDtcs()
        {
            var response = _uds?.ClearDtc();
            return response?.Success ?? false;
        }

        // Alias for PatsOperations.cs compatibility
        public bool ClearDTCs() => ClearDtcs();
        
        public bool ClearModuleDTCs(uint moduleAddress)
        {
            SetTargetModule(moduleAddress);
            return ClearDtcs();
        }

        public bool EcuReset(byte resetType = 0x01)
        {
            var response = _uds?.EcuReset((ResetType)resetType);
            return response?.Success ?? false;
        }

        public bool EcuReset(uint resetType) => EcuReset((byte)resetType);
        public bool EcuReset(int resetType) => EcuReset((byte)resetType);

        public byte[]? ReadDtcs(byte reportType = 0x02, byte statusMask = 0xFF)
        {
            var response = _uds?.ReadDtcInformation((DtcReportType)reportType, statusMask);
            return response?.Success == true ? response.Data : null;
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _uds?.Disconnect();
                _api?.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
