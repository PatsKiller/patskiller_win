using System;
using System.Threading.Tasks;
using PatsKillerPro.J2534;

namespace PatsKillerPro.Communication
{
    /// <summary>
    /// UDS Service - Complete backward compatibility wrapper
    /// Provides all methods that PatsOperations.cs and other legacy code expects
    /// </summary>
    public class UdsService : IDisposable
    {
        private FordUdsProtocol? _uds;
        private J2534Api? _api;
        private bool _disposed;

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

        #region UDS Services - Multiple Overloads for Compatibility

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
        /// Send raw request (alias for SendRequest)
        /// </summary>
        public byte[]? SendRawRequest(byte[] request, int timeout = 2000)
        {
            return SendRequest(request, timeout);
        }

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
        /// Read data by identifier (0x22) - with module address (legacy compatibility)
        /// </summary>
        public byte[]? ReadDataByIdentifier(uint moduleAddress, ushort did)
        {
            // Set target module if needed
            if (_uds != null && moduleAddress != 0)
            {
                _uds.SetTargetModule(moduleAddress, moduleAddress + 8);
            }
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
        /// Write data by identifier (0x2E) - 3 args with module address (legacy compatibility)
        /// </summary>
        public bool WriteDataByIdentifier(uint moduleAddress, ushort did, byte[] data)
        {
            // Set target module if needed
            if (_uds != null && moduleAddress != 0)
            {
                _uds.SetTargetModule(moduleAddress, moduleAddress + 8);
            }
            return WriteDataByIdentifier(did, data);
        }

        #endregion

        #region Diagnostic Session Control

        /// <summary>
        /// Start diagnostic session (0x10) - byte parameter
        /// </summary>
        public bool StartDiagnosticSession(byte sessionType)
        {
            if (_uds == null) return false;
            var response = _uds.DiagnosticSessionControl((DiagnosticSession)sessionType);
            return response.Success;
        }

        /// <summary>
        /// Start diagnostic session (0x10) - uint parameter (legacy compatibility)
        /// </summary>
        public bool StartDiagnosticSession(uint sessionType)
        {
            return StartDiagnosticSession((byte)sessionType);
        }

        /// <summary>
        /// Start diagnostic session (0x10) - int parameter (legacy compatibility)
        /// </summary>
        public bool StartDiagnosticSession(int sessionType)
        {
            return StartDiagnosticSession((byte)sessionType);
        }

        /// <summary>
        /// Start extended diagnostic session (0x10 03)
        /// </summary>
        public bool StartExtendedSession()
        {
            return StartDiagnosticSession(0x03);
        }

        /// <summary>
        /// Start programming session (0x10 02)
        /// </summary>
        public bool StartProgrammingSession()
        {
            return StartDiagnosticSession(0x02);
        }

        /// <summary>
        /// Start default session (0x10 01)
        /// </summary>
        public bool StartDefaultSession()
        {
            return StartDiagnosticSession(0x01);
        }

        #endregion

        #region Security Access

        /// <summary>
        /// Security access - request seed (0x27 01)
        /// </summary>
        public byte[]? RequestSecuritySeed(byte level = 0x01)
        {
            if (_uds == null) return null;
            var response = _uds.SecurityAccessRequestSeed(level);
            return response.Success ? response.Data : null;
        }

        /// <summary>
        /// Security access - request seed (alias for legacy code)
        /// </summary>
        public byte[]? RequestSecurityAccess(byte level = 0x01)
        {
            return RequestSecuritySeed(level);
        }

        /// <summary>
        /// Security access - send key (0x27 02)
        /// </summary>
        public bool SendSecurityKey(byte[] key, byte level = 0x02)
        {
            if (_uds == null) return false;
            var response = _uds.SecurityAccessSendKey(key, level);
            return response.Success;
        }

        #endregion

        #region Routine Control

        /// <summary>
        /// Routine control (0x31)
        /// </summary>
        public bool RoutineControl(byte controlType, ushort routineId, byte[]? data = null)
        {
            if (_uds == null) return false;
            var response = _uds.RoutineControl((RoutineControlType)controlType, routineId, data);
            return response.Success;
        }

        /// <summary>
        /// Start routine (0x31 01)
        /// </summary>
        public bool StartRoutine(ushort routineId, byte[]? data = null)
        {
            return RoutineControl(0x01, routineId, data);
        }

        /// <summary>
        /// Stop routine (0x31 02)
        /// </summary>
        public bool StopRoutine(ushort routineId)
        {
            return RoutineControl(0x02, routineId, null);
        }

        /// <summary>
        /// Request routine results (0x31 03)
        /// </summary>
        public bool RequestRoutineResults(ushort routineId)
        {
            return RoutineControl(0x03, routineId, null);
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
        /// ECU Reset (0x11)
        /// </summary>
        public bool EcuReset(byte resetType = 0x01)
        {
            if (_uds == null) return false;
            var response = _uds.EcuReset((ResetType)resetType);
            return response.Success;
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

        #region Module Targeting

        /// <summary>
        /// Set target module for communication
        /// </summary>
        public void SetTargetModule(uint txId, uint rxId)
        {
            _uds?.SetTargetModule(txId, rxId);
        }

        /// <summary>
        /// Set target module by address (calculates Tx/Rx)
        /// </summary>
        public void SetTargetModule(uint moduleAddress)
        {
            // Standard Ford addressing: Tx = address, Rx = address + 8
            _uds?.SetTargetModule(moduleAddress, moduleAddress + 8);
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
