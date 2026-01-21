using System;

namespace PatsKillerPro.J2534
{
    /// <summary>
    /// Exception thrown for J2534 errors
    /// </summary>
    public class J2534Exception : Exception
    {
        public J2534Error? ErrorCode { get; }

        public J2534Exception(string message) : base(message)
        {
        }

        public J2534Exception(string message, J2534Error errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public J2534Exception(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Gets a user-friendly error description
        /// </summary>
        public static string GetErrorDescription(J2534Error error)
        {
            return error switch
            {
                J2534Error.STATUS_NOERROR => "No error",
                J2534Error.ERR_NOT_SUPPORTED => "Function not supported",
                J2534Error.ERR_INVALID_CHANNEL_ID => "Invalid channel ID",
                J2534Error.ERR_INVALID_PROTOCOL_ID => "Invalid protocol ID",
                J2534Error.ERR_NULL_PARAMETER => "Null parameter provided",
                J2534Error.ERR_INVALID_IOCTL_VALUE => "Invalid IOCTL value",
                J2534Error.ERR_INVALID_FLAGS => "Invalid flags",
                J2534Error.ERR_FAILED => "Operation failed",
                J2534Error.ERR_DEVICE_NOT_CONNECTED => "Device not connected",
                J2534Error.ERR_TIMEOUT => "Operation timed out",
                J2534Error.ERR_INVALID_MSG => "Invalid message",
                J2534Error.ERR_INVALID_TIME_INTERVAL => "Invalid time interval",
                J2534Error.ERR_EXCEEDED_LIMIT => "Exceeded limit",
                J2534Error.ERR_INVALID_MSG_ID => "Invalid message ID",
                J2534Error.ERR_DEVICE_IN_USE => "Device in use",
                J2534Error.ERR_INVALID_IOCTL_ID => "Invalid IOCTL ID",
                J2534Error.ERR_BUFFER_EMPTY => "Buffer empty",
                J2534Error.ERR_BUFFER_FULL => "Buffer full",
                J2534Error.ERR_BUFFER_OVERFLOW => "Buffer overflow",
                J2534Error.ERR_PIN_INVALID => "Invalid pin",
                J2534Error.ERR_CHANNEL_IN_USE => "Channel in use",
                J2534Error.ERR_MSG_PROTOCOL_ID => "Message protocol ID mismatch",
                J2534Error.ERR_INVALID_FILTER_ID => "Invalid filter ID",
                J2534Error.ERR_NO_FLOW_CONTROL => "No flow control",
                J2534Error.ERR_NOT_UNIQUE => "Not unique",
                J2534Error.ERR_INVALID_BAUDRATE => "Invalid baud rate",
                J2534Error.ERR_INVALID_DEVICE_ID => "Invalid device ID",
                _ => $"Unknown error (0x{(uint)error:X2})"
            };
        }
    }
}
