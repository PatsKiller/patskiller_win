using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PatsKillerPro.Utils;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Represents a J2534 communication channel
    /// </summary>
    public class J2534Channel : IDisposable
    {
        private readonly J2534Device _device;
        private readonly uint _channelId;
        private readonly Protocol _protocol;
        private readonly uint _baudRate;
        private readonly List<uint> _filterIds = new();
        private readonly List<uint> _periodicMsgIds = new();
        private bool _disposed = false;

        public J2534Channel(J2534Device device, uint channelId, Protocol protocol, uint baudRate)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _channelId = channelId;
            _protocol = protocol;
            _baudRate = baudRate;
        }

        public uint ChannelId => _channelId;
        public Protocol Protocol => _protocol;
        public uint BaudRate => _baudRate;

        /// <summary>
        /// Sets up a flow control filter for ISO 15765 (CAN with ISO-TP)
        /// </summary>
        public uint SetupFlowControlFilter(uint txId, uint rxId)
        {
            Logger.Info($"Setting up flow control filter - TX: 0x{txId:X3}, RX: 0x{rxId:X3}");

            var maskMsg = CreateMsg(_protocol);
            var patternMsg = CreateMsg(_protocol);
            var flowControlMsg = CreateMsg(_protocol);

            var maskPtr = IntPtr.Zero;
            var patternPtr = IntPtr.Zero;
            var flowControlPtr = IntPtr.Zero;

            try
            {
                // Mask message (0xFFFF for 11-bit CAN ID)
                maskMsg.Data[0] = 0xFF;
                maskMsg.Data[1] = 0xFF;
                maskMsg.Data[2] = 0xFF;
                maskMsg.Data[3] = 0xFF;
                maskMsg.DataSize = 4;

                // Pattern message (RX ID - what we receive)
                patternMsg.Data[0] = (byte)(rxId >> 24);
                patternMsg.Data[1] = (byte)(rxId >> 16);
                patternMsg.Data[2] = (byte)(rxId >> 8);
                patternMsg.Data[3] = (byte)rxId;
                patternMsg.DataSize = 4;

                // Flow control message (TX ID - what we send)
                flowControlMsg.Data[0] = (byte)(txId >> 24);
                flowControlMsg.Data[1] = (byte)(txId >> 16);
                flowControlMsg.Data[2] = (byte)(txId >> 8);
                flowControlMsg.Data[3] = (byte)txId;
                flowControlMsg.DataSize = 4;
                flowControlMsg.TxFlags = (uint)TxFlags.ISO15765_FRAME_PAD;

                maskPtr = AllocateMsg(maskMsg);
                patternPtr = AllocateMsg(patternMsg);
                flowControlPtr = AllocateMsg(flowControlMsg);

                var result = _device.PassThruStartMsgFilter(
                    _channelId,
                    (uint)FilterType.FLOW_CONTROL,
                    maskPtr,
                    patternPtr,
                    flowControlPtr,
                    out uint filterId);

                if (result != 0)
                {
                    throw new J2534Exception($"Failed to set flow control filter: {_device.GetLastError()}");
                }

                _filterIds.Add(filterId);
                Logger.Info($"Flow control filter created. FilterID: {filterId}");
                return filterId;
            }
            finally
            {
                if (maskPtr != IntPtr.Zero) Marshal.FreeHGlobal(maskPtr);
                if (patternPtr != IntPtr.Zero) Marshal.FreeHGlobal(patternPtr);
                if (flowControlPtr != IntPtr.Zero) Marshal.FreeHGlobal(flowControlPtr);
            }
        }

        /// <summary>
        /// Sets up a pass filter
        /// </summary>
        public uint SetupPassFilter(uint canId)
        {
            Logger.Info($"Setting up pass filter for CAN ID: 0x{canId:X3}");

            var maskMsg = CreateMsg(_protocol);
            var patternMsg = CreateMsg(_protocol);

            var maskPtr = IntPtr.Zero;
            var patternPtr = IntPtr.Zero;

            try
            {
                // Mask message
                maskMsg.Data[0] = 0xFF;
                maskMsg.Data[1] = 0xFF;
                maskMsg.Data[2] = 0xFF;
                maskMsg.Data[3] = 0xFF;
                maskMsg.DataSize = 4;

                // Pattern message
                patternMsg.Data[0] = (byte)(canId >> 24);
                patternMsg.Data[1] = (byte)(canId >> 16);
                patternMsg.Data[2] = (byte)(canId >> 8);
                patternMsg.Data[3] = (byte)canId;
                patternMsg.DataSize = 4;

                maskPtr = AllocateMsg(maskMsg);
                patternPtr = AllocateMsg(patternMsg);

                var result = _device.PassThruStartMsgFilter(
                    _channelId,
                    (uint)FilterType.PASS_FILTER,
                    maskPtr,
                    patternPtr,
                    IntPtr.Zero,
                    out uint filterId);

                if (result != 0)
                {
                    throw new J2534Exception($"Failed to set pass filter: {_device.GetLastError()}");
                }

                _filterIds.Add(filterId);
                return filterId;
            }
            finally
            {
                if (maskPtr != IntPtr.Zero) Marshal.FreeHGlobal(maskPtr);
                if (patternPtr != IntPtr.Zero) Marshal.FreeHGlobal(patternPtr);
            }
        }

        /// <summary>
        /// Sends a message and waits for response
        /// </summary>
        public byte[]? SendAndReceive(uint txId, byte[] data, uint timeout = 1000)
        {
            // Send message
            SendMessage(txId, data);

            // Receive response
            return ReceiveMessage(timeout);
        }

        /// <summary>
        /// Sends a message
        /// </summary>
        public void SendMessage(uint canId, byte[] data)
        {
            var msg = CreateMsg(_protocol);
            
            // Set CAN ID in first 4 bytes
            msg.Data[0] = (byte)(canId >> 24);
            msg.Data[1] = (byte)(canId >> 16);
            msg.Data[2] = (byte)(canId >> 8);
            msg.Data[3] = (byte)canId;
            
            // Copy data after CAN ID
            Array.Copy(data, 0, msg.Data, 4, data.Length);
            msg.DataSize = (uint)(4 + data.Length);
            msg.TxFlags = (uint)TxFlags.ISO15765_FRAME_PAD;

            var msgPtr = AllocateMsg(msg);
            try
            {
                uint numMsgs = 1;
                var result = _device.PassThruWriteMsgs(_channelId, msgPtr, ref numMsgs, 1000);
                
                if (result != 0)
                {
                    throw new J2534Exception($"Failed to send message: {_device.GetLastError()}");
                }

                Logger.Debug($"TX [{canId:X3}]: {BitConverter.ToString(data)}");
            }
            finally
            {
                Marshal.FreeHGlobal(msgPtr);
            }
        }

        /// <summary>
        /// Receives a message
        /// </summary>
        public byte[]? ReceiveMessage(uint timeout = 1000)
        {
            var msg = CreateMsg(_protocol);
            var msgPtr = AllocateMsg(msg);

            try
            {
                uint numMsgs = 1;
                var result = _device.PassThruReadMsgs(_channelId, msgPtr, ref numMsgs, timeout);

                if (result == (int)J2534Error.ERR_BUFFER_EMPTY || result == (int)J2534Error.ERR_TIMEOUT)
                {
                    return null;
                }

                if (result != 0)
                {
                    throw new J2534Exception($"Failed to receive message: {_device.GetLastError()}");
                }

                if (numMsgs == 0)
                {
                    return null;
                }

                // Read message back from pointer
                var receivedMsg = Marshal.PtrToStructure<PassThruMsg>(msgPtr);
                
                if (receivedMsg.DataSize <= 4)
                {
                    return null;
                }

                // Extract data (skip CAN ID in first 4 bytes)
                var dataLength = (int)receivedMsg.DataSize - 4;
                var responseData = new byte[dataLength];
                Array.Copy(receivedMsg.Data, 4, responseData, 0, dataLength);

                var canId = (uint)((receivedMsg.Data[0] << 24) | (receivedMsg.Data[1] << 16) | 
                                   (receivedMsg.Data[2] << 8) | receivedMsg.Data[3]);
                
                Logger.Debug($"RX [{canId:X3}]: {BitConverter.ToString(responseData)}");

                return responseData;
            }
            finally
            {
                Marshal.FreeHGlobal(msgPtr);
            }
        }

        /// <summary>
        /// Starts a periodic message (tester present)
        /// </summary>
        public uint StartPeriodicMessage(uint canId, byte[] data, uint intervalMs)
        {
            var msg = CreateMsg(_protocol);
            
            msg.Data[0] = (byte)(canId >> 24);
            msg.Data[1] = (byte)(canId >> 16);
            msg.Data[2] = (byte)(canId >> 8);
            msg.Data[3] = (byte)canId;
            
            Array.Copy(data, 0, msg.Data, 4, data.Length);
            msg.DataSize = (uint)(4 + data.Length);
            msg.TxFlags = (uint)TxFlags.ISO15765_FRAME_PAD;

            var msgPtr = AllocateMsg(msg);
            try
            {
                var result = _device.PassThruStartPeriodicMsg(_channelId, msgPtr, out uint msgId, intervalMs);
                
                if (result != 0)
                {
                    throw new J2534Exception($"Failed to start periodic message: {_device.GetLastError()}");
                }

                _periodicMsgIds.Add(msgId);
                Logger.Info($"Started periodic message ID: {msgId}, Interval: {intervalMs}ms");
                return msgId;
            }
            finally
            {
                Marshal.FreeHGlobal(msgPtr);
            }
        }

        /// <summary>
        /// Stops a periodic message
        /// </summary>
        public void StopPeriodicMessage(uint msgId)
        {
            var result = _device.PassThruStopPeriodicMsg(_channelId, msgId);
            if (result != 0)
            {
                Logger.Warning($"Failed to stop periodic message: {_device.GetLastError()}");
            }
            _periodicMsgIds.Remove(msgId);
        }

        /// <summary>
        /// Clears receive buffer
        /// </summary>
        public void ClearRxBuffer()
        {
            _device.PassThruIoctl(_channelId, (uint)IoctlId.CLEAR_RX_BUFFER, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Clears transmit buffer
        /// </summary>
        public void ClearTxBuffer()
        {
            _device.PassThruIoctl(_channelId, (uint)IoctlId.CLEAR_TX_BUFFER, IntPtr.Zero, IntPtr.Zero);
        }

        private static PassThruMsg CreateMsg(Protocol protocol)
        {
            return new PassThruMsg(protocol);
        }

        private static IntPtr AllocateMsg(PassThruMsg msg)
        {
            var size = Marshal.SizeOf<PassThruMsg>();
            var ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(msg, ptr, false);
            return ptr;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Stop all periodic messages
                foreach (var msgId in _periodicMsgIds.ToArray())
                {
                    try
                    {
                        _device.PassThruStopPeriodicMsg(_channelId, msgId);
                    }
                    catch { }
                }
                _periodicMsgIds.Clear();

                // Remove all filters
                foreach (var filterId in _filterIds.ToArray())
                {
                    try
                    {
                        _device.PassThruStopMsgFilter(_channelId, filterId);
                    }
                    catch { }
                }
                _filterIds.Clear();

                // Disconnect channel
                try
                {
                    Logger.Info($"Disconnecting channel {_channelId}");
                    _device.PassThruDisconnect(_channelId);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error disconnecting channel: {ex.Message}");
                }

                _disposed = true;
            }
        }
    }
}
