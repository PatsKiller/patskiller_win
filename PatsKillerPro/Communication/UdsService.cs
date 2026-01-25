using System;
using System.Threading.Tasks;
using PatsKillerPro.J2534;

namespace PatsKillerPro.Communication
{
    /// <summary>
    /// UDS Service - Wrapper for backward compatibility
    /// This class wraps the new J2534 library (FordUdsProtocol) for any code
    /// that was using the old UdsService.
    /// 
    /// NOTE: For new code, use J2534Service directly instead of this wrapper.
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

        /// <summary>
        /// Send UDS request and get response
        /// </summary>
        public byte[]? SendRequest(byte[] request, int timeout = 2000)
        {
            if (_uds == null) return null;
            var response = _uds.SendRequest(request, timeout);
            return response.Success ? response.Data : null;
        }

        /// <summary>
        /// Read data by identifier (0x22)
        /// </summary>
        public byte[]? ReadDataByIdentifier(ushort did)
        {
            if (_uds == null) return null;
            var response = _uds.ReadDataByIdentifier(did);
            return response.Success ? response.Data : null;
        }

        /// <summary>
        /// Write data by identifier (0x2E)
        /// </summary>
        public bool WriteDataByIdentifier(ushort did, byte[] data)
        {
            if (_uds == null) return false;
            var response = _uds.WriteDataByIdentifier(did, data);
            return response.Success;
        }

        /// <summary>
        /// Start diagnostic session (0x10)
        /// </summary>
        public bool StartDiagnosticSession(byte sessionType)
        {
            if (_uds == null) return false;
            var response = _uds.DiagnosticSessionControl((DiagnosticSession)sessionType);
            return response.Success;
        }

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
        /// Security access - send key (0x27 02)
        /// </summary>
        public bool SendSecurityKey(byte[] key, byte level = 0x02)
        {
            if (_uds == null) return false;
            var response = _uds.SecurityAccessSendKey(key, level);
            return response.Success;
        }

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
