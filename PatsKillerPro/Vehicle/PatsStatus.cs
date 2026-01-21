namespace PatsKillerPro.Vehicle
{
    /// <summary>
    /// PATS operation status codes
    /// </summary>
    public enum PatsStatus
    {
        /// <summary>Operation completed successfully</summary>
        Success = 0,
        
        /// <summary>Operation failed</summary>
        Failed = 1,
        
        /// <summary>Security access denied - wrong incode or lockout</summary>
        SecurityDenied = 2,
        
        /// <summary>No response from module</summary>
        NoResponse = 3,
        
        /// <summary>Invalid incode format</summary>
        InvalidIncode = 4,
        
        /// <summary>Key programming in progress</summary>
        InProgress = 5,
        
        /// <summary>Maximum keys reached (8)</summary>
        MaxKeysReached = 6,
        
        /// <summary>BCM not in correct mode</summary>
        WrongMode = 7,
        
        /// <summary>Communication error</summary>
        CommError = 8,
        
        /// <summary>Vehicle not supported</summary>
        NotSupported = 9
    }
}
