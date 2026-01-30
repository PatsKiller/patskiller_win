using System;
using PatsKillerPro.J2534;

namespace PatsKillerPro.Communication
{
    /// <summary>
    /// UDS Service - Backward compatibility wrapper for PatsOperations.cs
    /// All RequestSecurityAccess methods return BOOL for PatsOperations compatibility
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
        
        public byte[]? ReadDataByIdentifier(uint moduleAddress, uint did)
        {
            return ReadDataByIdentifier(moduleAddress, (ushort)did);
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
        
        public bool WriteDataByIdentifier(uint moduleAddress, uint did, byte[] data)
        {
            return WriteDataByIdentifier(moduleAddress, (ushort)did, data);
        }

        #endregion

        #region Session Control

        public bool StartDiagnosticSession(byte sessionType)
        {
            var response = _uds?.DiagnosticSessionControl((DiagnosticSession)sessionType);
            return response?.Success ?? false;
        }

        public bool StartExtendedSession() => StartDiagnosticSession(0x03);
        
        public bool StartExtendedSession(uint moduleAddress) 
        { 
            SetTargetModule(moduleAddress); 
            return StartExtendedSession(); 
        }

        public bool StartProgrammingSession() => StartDiagnosticSession(0x02);
        
        public bool StartProgrammingSession(uint moduleAddress) 
        { 
            SetTargetModule(moduleAddress); 
            return StartProgrammingSession(); 
        }

        public bool StartDefaultSession() => StartDiagnosticSession(0x01);
        
        public bool StartDefaultSession(uint moduleAddress) 
        { 
            SetTargetModule(moduleAddress); 
            return StartDefaultSession(); 
        }

        #endregion

        #region Security Access - ALL RETURN BOOL for PatsOperations.cs

        /// <summary>
        /// Request security access with default level - returns BOOL
        /// </summary>
        public bool RequestSecurityAccess(uint moduleAddress)
        {
            SetTargetModule(moduleAddress);
            var response = _uds?.SecurityAccessRequestSeed(0x01);
            return response?.Success ?? false;
        }

        /// <summary>
        /// Request security access with specific level (byte) - returns BOOL
        /// </summary>
        public bool RequestSecurityAccess(uint moduleAddress, byte level)
        {
            SetTargetModule(moduleAddress);
            var response = _uds?.SecurityAccessRequestSeed(level);
            return response?.Success ?? false;
        }

        /// <summary>
        /// Request security access with specific level (int) - returns BOOL
        /// </summary>
        public bool RequestSecurityAccess(uint moduleAddress, int level)
        {
            return RequestSecurityAccess(moduleAddress, (byte)level);
        }

        /// <summary>
        /// Request security access with specific level (uint) - returns BOOL
        /// </summary>
        public bool RequestSecurityAccess(uint moduleAddress, uint level)
        {
            return RequestSecurityAccess(moduleAddress, (byte)level);
        }

        public bool SendSecurityKey(byte[] key, byte level = 0x02)
        {
            var response = _uds?.SecurityAccessSendKey(key, level);
            return response?.Success ?? false;
        }

        #endregion

        #region Routine Control - Returns byte[]? for PatsOperations.cs

        /// <summary>
        /// Routine control with module address - returns byte[]? response
        /// </summary>
        public byte[]? RoutineControl(uint moduleAddress, byte controlType, byte[] routineData)
        {
            SetTargetModule(moduleAddress);
            
            // Build routine control request: 0x31 + controlType + routineData
            var request = new byte[2 + routineData.Length];
            request[0] = 0x31;
            request[1] = controlType;
            Array.Copy(routineData, 0, request, 2, routineData.Length);
            
            return SendRequest(request);
        }

        /// <summary>
        /// Routine control with int controlType - returns byte[]?
        /// </summary>
        public byte[]? RoutineControl(uint moduleAddress, int controlType, byte[] routineData)
        {
            return RoutineControl(moduleAddress, (byte)controlType, routineData);
        }

        /// <summary>
        /// Routine control with uint controlType - returns byte[]?
        /// </summary>
        public byte[]? RoutineControl(uint moduleAddress, uint controlType, byte[] routineData)
        {
            return RoutineControl(moduleAddress, (byte)controlType, routineData);
        }

        #endregion

        #region Input/Output Control

        public byte[]? InputOutputControl(uint moduleAddress, int did, byte[] controlState)
        {
            SetTargetModule(moduleAddress);
            int dataLen = controlState?.Length ?? 0;
            var request = new byte[4 + dataLen];
            request[0] = 0x2F;
            request[1] = (byte)((did >> 8) & 0xFF);
            request[2] = (byte)(did & 0xFF);
            request[3] = 0x03; // Short term adjustment
            if (controlState != null)
                Array.Copy(controlState, 0, request, 4, controlState.Length);
            return SendRequest(request);
        }

        public byte[]? InputOutputControl(uint moduleAddress, uint did, byte[] controlState)
        {
            return InputOutputControl(moduleAddress, (int)did, controlState);
        }

        #endregion

        #region DTC Operations

        public bool ClearDtcs()
        {
            var response = _uds?.ClearDtc();
            return response?.Success ?? false;
        }

        public bool ClearDTCs() => ClearDtcs();
        
        public bool ClearModuleDTCs(uint moduleAddress)
        {
            SetTargetModule(moduleAddress);
            return ClearDtcs();
        }

        #endregion

        #region ECU Reset

        public bool EcuReset(byte resetType = 0x01)
        {
            var response = _uds?.EcuReset((ResetType)resetType);
            return response?.Success ?? false;
        }
        
        public bool EcuReset(uint moduleAddress, byte resetType = 0x01)
        {
            SetTargetModule(moduleAddress);
            return EcuReset(resetType);
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