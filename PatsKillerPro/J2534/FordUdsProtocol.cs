using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Ford UDS Protocol Implementation over J2534
    /// Handles ISO 15765-2 (CAN) communication with Ford ECUs
    /// </summary>
    public class FordUdsProtocol
    {
        private readonly J2534Api _api;
        private uint _deviceId;
        private uint _channelId;
        private uint _flowFilterId;
        private bool _isConnected;

        // Default Ford BCM addresses
        private uint _txId = 0x726;
        private uint _rxId = 0x72E;

        public bool IsConnected => _isConnected;
        public uint TxId => _txId;
        public uint RxId => _rxId;

        public FordUdsProtocol(J2534Api api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public J2534Error Connect(uint baudRate = 500000)
        {
            // Open device
            var result = _api.PassThruOpen(out _deviceId);
            if (result != J2534Error.STATUS_NOERROR) return result;

            // Connect to CAN channel
            result = _api.PassThruConnect(_deviceId, ProtocolId.ISO15765, ConnectFlags.NONE, baudRate, out _channelId);
            if (result != J2534Error.STATUS_NOERROR)
            {
                _api.PassThruClose(_deviceId);
                return result;
            }

            // Set up flow control filter
            result = SetupFlowControlFilter();
            if (result != J2534Error.STATUS_NOERROR)
            {
                _api.PassThruDisconnect(_channelId);
                _api.PassThruClose(_deviceId);
                return result;
            }

            _isConnected = true;
            return J2534Error.STATUS_NOERROR;
        }

        public void Disconnect()
        {
            if (_isConnected)
            {
                if (_flowFilterId != 0)
                    _api.PassThruStopMsgFilter(_channelId, _flowFilterId);
                _api.PassThruDisconnect(_channelId);
                _api.PassThruClose(_deviceId);
                _isConnected = false;
            }
        }

        public void SetTargetModule(uint txId, uint rxId)
        {
            _txId = txId;
            _rxId = rxId;
            
            // Re-setup flow control if connected
            if (_isConnected)
            {
                if (_flowFilterId != 0)
                    _api.PassThruStopMsgFilter(_channelId, _flowFilterId);
                SetupFlowControlFilter();
            }
        }

        private J2534Error SetupFlowControlFilter()
        {
            // Mask message - all bits matter for 11-bit CAN ID
            var maskData = new byte[] { 0x00, 0x00, 0x07, 0xFF };
            var maskMsg = PassThruMsg.Create(ProtocolId.ISO15765, maskData, TxFlags.ISO15765_FRAME_PAD);

            // Pattern message - match our Rx ID
            var patternData = new byte[4];
            patternData[0] = (byte)((_rxId >> 24) & 0xFF);
            patternData[1] = (byte)((_rxId >> 16) & 0xFF);
            patternData[2] = (byte)((_rxId >> 8) & 0xFF);
            patternData[3] = (byte)(_rxId & 0xFF);
            var patternMsg = PassThruMsg.Create(ProtocolId.ISO15765, patternData, TxFlags.ISO15765_FRAME_PAD);

            // Flow control message - our Tx ID for flow control responses
            var flowData = new byte[4];
            flowData[0] = (byte)((_txId >> 24) & 0xFF);
            flowData[1] = (byte)((_txId >> 16) & 0xFF);
            flowData[2] = (byte)((_txId >> 8) & 0xFF);
            flowData[3] = (byte)(_txId & 0xFF);
            var flowMsg = PassThruMsg.Create(ProtocolId.ISO15765, flowData, TxFlags.ISO15765_FRAME_PAD);

            return _api.PassThruStartMsgFilter(_channelId, FilterType.FLOW_CONTROL_FILTER, maskMsg, patternMsg, flowMsg, out _flowFilterId);
        }

        public UdsResponse SendRequest(byte[] request, int timeout = 2000)
        {
            if (!_isConnected)
                return new UdsResponse { Success = false, ErrorMessage = "Not connected" };

            try
            {
                // Clear buffers
                _api.PassThruIoctl(_channelId, IoctlId.CLEAR_TX_BUFFER);
                _api.PassThruIoctl(_channelId, IoctlId.CLEAR_RX_BUFFER);

                // Build message with CAN ID prefix
                var msgData = new byte[4 + request.Length];
                msgData[0] = (byte)((_txId >> 24) & 0xFF);
                msgData[1] = (byte)((_txId >> 16) & 0xFF);
                msgData[2] = (byte)((_txId >> 8) & 0xFF);
                msgData[3] = (byte)(_txId & 0xFF);
                Array.Copy(request, 0, msgData, 4, request.Length);

                var txMsg = PassThruMsg.Create(ProtocolId.ISO15765, msgData, TxFlags.ISO15765_FRAME_PAD);
                var msgs = new PassThruMsg[] { txMsg };

                // Send
                var result = _api.PassThruWriteMsgs(_channelId, msgs, out uint numSent, (uint)timeout);
                if (result != J2534Error.STATUS_NOERROR)
                    return new UdsResponse { Success = false, ErrorMessage = $"Send failed: {result}" };

                // Read response
                result = _api.PassThruReadMsgs(_channelId, out PassThruMsg[] rxMsgs, (uint)timeout);
                if (result != J2534Error.STATUS_NOERROR)
                    return new UdsResponse { Success = false, ErrorMessage = $"Read failed: {result}" };

                if (rxMsgs.Length == 0 || rxMsgs[0].DataSize <= 4)
                    return new UdsResponse { Success = false, ErrorMessage = "No response" };

                // Extract data (skip 4-byte CAN ID)
                var responseData = new byte[rxMsgs[0].DataSize - 4];
                Array.Copy(rxMsgs[0].Data, 4, responseData, 0, responseData.Length);

                // Check for negative response
                if (responseData.Length >= 3 && responseData[0] == 0x7F)
                {
                    return new UdsResponse 
                    { 
                        Success = false, 
                        Data = responseData,
                        NegativeResponse = true,
                        NRC = responseData[2],
                        ErrorMessage = $"NRC: 0x{responseData[2]:X2}"
                    };
                }

                return new UdsResponse { Success = true, Data = responseData };
            }
            catch (Exception ex)
            {
                return new UdsResponse { Success = false, ErrorMessage = ex.Message };
            }
        }

        #region UDS Services

        public UdsResponse DiagnosticSessionControl(DiagnosticSession session)
        {
            return SendRequest(new byte[] { 0x10, (byte)session });
        }

        public UdsResponse EcuReset(ResetType resetType)
        {
            return SendRequest(new byte[] { 0x11, (byte)resetType });
        }

        public UdsResponse SecurityAccessRequestSeed(byte level = 0x01)
        {
            return SendRequest(new byte[] { 0x27, level });
        }

        public UdsResponse SecurityAccessSendKey(byte[] key, byte level = 0x02)
        {
            var request = new byte[2 + key.Length];
            request[0] = 0x27;
            request[1] = level;
            Array.Copy(key, 0, request, 2, key.Length);
            return SendRequest(request);
        }

        public UdsResponse ReadDataByIdentifier(ushort did)
        {
            return SendRequest(new byte[] { 0x22, (byte)(did >> 8), (byte)(did & 0xFF) });
        }

        public UdsResponse WriteDataByIdentifier(ushort did, byte[] data)
        {
            var request = new byte[3 + data.Length];
            request[0] = 0x2E;
            request[1] = (byte)(did >> 8);
            request[2] = (byte)(did & 0xFF);
            Array.Copy(data, 0, request, 3, data.Length);
            return SendRequest(request);
        }

        public UdsResponse RoutineControl(RoutineControlType controlType, ushort routineId, byte[]? data = null)
        {
            int dataLen = data?.Length ?? 0;
            var request = new byte[4 + dataLen];
            request[0] = 0x31;
            request[1] = (byte)controlType;
            request[2] = (byte)(routineId >> 8);
            request[3] = (byte)(routineId & 0xFF);
            if (data != null)
                Array.Copy(data, 0, request, 4, data.Length);
            return SendRequest(request);
        }

        public UdsResponse TesterPresent()
        {
            return SendRequest(new byte[] { 0x3E, 0x00 });
        }

        public UdsResponse ClearDtc(uint groupOfDtc = 0xFFFFFF)
        {
            return SendRequest(new byte[] { 0x14, (byte)(groupOfDtc >> 16), (byte)(groupOfDtc >> 8), (byte)groupOfDtc });
        }

        public UdsResponse ReadDtcInformation(DtcReportType reportType, byte statusMask = 0xFF)
        {
            return SendRequest(new byte[] { 0x19, (byte)reportType, statusMask });
        }

        #endregion

        #region Ford Specific - Battery Voltage

        public double ReadBatteryVoltage()
        {
            try
            {
                IntPtr output = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
                try
                {
                    var result = _api.PassThruIoctl(_deviceId, IoctlId.READ_VBATT, IntPtr.Zero, output);
                    if (result == J2534Error.STATUS_NOERROR)
                    {
                        int millivolts = System.Runtime.InteropServices.Marshal.ReadInt32(output);
                        return millivolts / 1000.0;
                    }
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(output);
                }
            }
            catch { }
            return 0;
        }

        #endregion
    }

    public class UdsResponse
    {
        public bool Success { get; set; }
        public byte[]? Data { get; set; }
        public bool NegativeResponse { get; set; }
        public byte NRC { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
