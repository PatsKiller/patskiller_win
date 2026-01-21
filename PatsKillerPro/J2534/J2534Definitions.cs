using System;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// J2534 API Protocol IDs
    /// </summary>
    public enum Protocol : uint
    {
        J1850VPW = 1,
        J1850PWM = 2,
        ISO9141 = 3,
        ISO14230 = 4,
        CAN = 5,
        ISO15765 = 6,
        SCI_A_ENGINE = 7,
        SCI_A_TRANS = 8,
        SCI_B_ENGINE = 9,
        SCI_B_TRANS = 10
    }

    /// <summary>
    /// J2534 Filter Types
    /// </summary>
    public enum FilterType : uint
    {
        PASS_FILTER = 1,
        BLOCK_FILTER = 2,
        FLOW_CONTROL = 3
    }

    /// <summary>
    /// J2534 Connect Flags
    /// </summary>
    [Flags]
    public enum ConnectFlags : uint
    {
        NONE = 0,
        CAN_29BIT_ID = 0x100,
        ISO9141_NO_CHECKSUM = 0x200,
        CAN_ID_BOTH = 0x800,
        ISO9141_K_LINE_ONLY = 0x1000
    }

    /// <summary>
    /// J2534 Transmit Flags
    /// </summary>
    [Flags]
    public enum TxFlags : uint
    {
        NONE = 0,
        ISO15765_FRAME_PAD = 0x40,
        ISO15765_ADDR_TYPE = 0x80,
        CAN_29BIT_ID = 0x100,
        WAIT_P3_MIN_ONLY = 0x200,
        SW_CAN_HV_TX = 0x400,
        SCI_MODE = 0x400000,
        SCI_TX_VOLTAGE = 0x800000
    }

    /// <summary>
    /// J2534 IOCTL IDs
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
    /// J2534 Configuration Parameter IDs
    /// </summary>
    public enum ConfigParamId : uint
    {
        DATA_RATE = 0x01,
        LOOPBACK = 0x03,
        NODE_ADDRESS = 0x04,
        NETWORK_LINE = 0x05,
        P1_MIN = 0x06,
        P1_MAX = 0x07,
        P2_MIN = 0x08,
        P2_MAX = 0x09,
        P3_MIN = 0x0A,
        P3_MAX = 0x0B,
        P4_MIN = 0x0C,
        P4_MAX = 0x0D,
        W1 = 0x0E,
        W2 = 0x0F,
        W3 = 0x10,
        W4 = 0x11,
        W5 = 0x12,
        TIDLE = 0x13,
        TINIL = 0x14,
        TWUP = 0x15,
        PARITY = 0x16,
        BIT_SAMPLE_POINT = 0x17,
        SYNC_JUMP_WIDTH = 0x18,
        W0 = 0x19,
        T1_MAX = 0x1A,
        T2_MAX = 0x1B,
        T4_MAX = 0x1C,
        T5_MAX = 0x1D,
        ISO15765_BS = 0x1E,
        ISO15765_STMIN = 0x1F,
        DATA_BITS = 0x20,
        FIVE_BAUD_MOD = 0x21,
        BS_TX = 0x22,
        STMIN_TX = 0x23,
        T3_MAX = 0x24,
        ISO15765_WFT_MAX = 0x25,
        CAN_MIXED_FORMAT = 0x8000,
        J1962_PINS = 0x8001,
        SW_CAN_HS_DATA_RATE = 0x8010,
        SW_CAN_SPEEDCHANGE_ENABLE = 0x8011,
        SW_CAN_RES_SWITCH = 0x8012,
        ACTIVE_CHANNELS = 0x8020,
        SAMPLE_RATE = 0x8021,
        SAMPLES_PER_READING = 0x8022,
        READINGS_PER_MSG = 0x8023,
        AVERAGING_METHOD = 0x8024,
        SAMPLE_RESOLUTION = 0x8025,
        INPUT_RANGE_LOW = 0x8026,
        INPUT_RANGE_HIGH = 0x8027
    }

    /// <summary>
    /// J2534 Error Codes
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
    /// PassThru message structure for native interop
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PassThruMsg
    {
        public uint ProtocolID;
        public uint RxStatus;
        public uint TxFlags;
        public uint Timestamp;
        public uint DataSize;
        public uint ExtraDataIndex;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 4128)]
        public byte[] Data;

        public PassThruMsg(Protocol protocol)
        {
            ProtocolID = (uint)protocol;
            RxStatus = 0;
            TxFlags = 0;
            Timestamp = 0;
            DataSize = 0;
            ExtraDataIndex = 0;
            Data = new byte[4128];
        }
    }

    /// <summary>
    /// Common CAN baud rates
    /// </summary>
    public static class BaudRates
    {
        public const uint HS_CAN_500K = 500000;
        public const uint MS_CAN_125K = 125000;
        public const uint SW_CAN_33K = 33333;
        public const uint LS_CAN_125K = 125000;
    }
}
