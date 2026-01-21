namespace PatsKillerPro.Vehicle
{
    /// <summary>
    /// CAN module addresses for Ford/Lincoln vehicles
    /// </summary>
    public static class ModuleAddresses
    {
        // ========================================
        // HIGH SPEED CAN (HS-CAN) - 500 kbps
        // ========================================
        
        // BCM - Body Control Module
        public const uint BCM_TX = 0x726;      // Transmit to BCM
        public const uint BCM_RX = 0x72E;      // Receive from BCM
        
        // PCM - Powertrain Control Module
        public const uint PCM_TX = 0x7E0;      // Transmit to PCM
        public const uint PCM_RX = 0x7E8;      // Receive from PCM
        
        // TCM - Transmission Control Module
        public const uint TCM_TX = 0x7E1;      // Transmit to TCM
        public const uint TCM_RX = 0x7E9;      // Receive from TCM
        
        // ABS - Anti-lock Brake System
        public const uint ABS_TX = 0x760;      // Transmit to ABS
        public const uint ABS_RX = 0x768;      // Receive from ABS
        
        // IPC - Instrument Panel Cluster
        public const uint IPC_TX = 0x720;      // Transmit to IPC
        public const uint IPC_RX = 0x728;      // Receive from IPC
        
        // APIM - Accessory Protocol Interface Module (SYNC)
        public const uint APIM_TX = 0x7D0;     // Transmit to APIM
        public const uint APIM_RX = 0x7D8;     // Receive from APIM
        
        // PSCM - Power Steering Control Module
        public const uint PSCM_TX = 0x730;     // Transmit to PSCM
        public const uint PSCM_RX = 0x738;     // Receive from PSCM
        
        // GWM - Gateway Module
        public const uint GWM_TX = 0x716;      // Transmit to GWM
        public const uint GWM_RX = 0x71E;      // Receive from GWM
        
        // SCCM - Steering Column Control Module
        public const uint SCCM_TX = 0x724;     // Transmit to SCCM
        public const uint SCCM_RX = 0x72C;     // Receive from SCCM
        
        // ESCL - Electronic Steering Column Lock
        public const uint ESCL_TX = 0x733;     // Transmit to ESCL
        public const uint ESCL_RX = 0x73B;     // Receive from ESCL
        
        // GPSM - Global Positioning System Module
        public const uint GPSM_TX = 0x701;     // Transmit to GPSM
        public const uint GPSM_RX = 0x709;     // Receive from GPSM
        
        // DDM - Driver Door Module
        public const uint DDM_TX = 0x740;      // Transmit to DDM
        public const uint DDM_RX = 0x748;      // Receive from DDM
        
        // PDM - Passenger Door Module
        public const uint PDM_TX = 0x741;      // Transmit to PDM
        public const uint PDM_RX = 0x749;      // Receive from PDM
        
        // FCIM - Front Controls Interface Module
        public const uint FCIM_TX = 0x727;     // Transmit to FCIM
        public const uint FCIM_RX = 0x72F;     // Receive from FCIM
        
        // RCM - Restraints Control Module (Airbag)
        public const uint RCM_TX = 0x737;      // Transmit to RCM
        public const uint RCM_RX = 0x73F;      // Receive from RCM
        
        // Broadcast address (all modules)
        public const uint BROADCAST_TX = 0x7DF;
        
        // ========================================
        // MEDIUM SPEED CAN (MS-CAN) - 125 kbps
        // ========================================
        
        // RFA - Remote Function Actuator (Keyless)
        public const uint RFA_TX = 0x731;      // Transmit to RFA
        public const uint RFA_RX = 0x739;      // Receive from RFA
        
        // TRM - Trailer Module
        public const uint TRM_TX = 0x765;      // Transmit to TRM
        public const uint TRM_RX = 0x76D;      // Receive from TRM
        
        // HVAC - Heating/Ventilation/Air Conditioning
        public const uint HVAC_TX = 0x733;     // Transmit to HVAC
        public const uint HVAC_RX = 0x73B;     // Receive from HVAC
        
        // ACM - Audio Control Module
        public const uint ACM_TX = 0x727;      // Transmit to ACM
        public const uint ACM_RX = 0x72F;      // Receive from ACM
        
        // ========================================
        // PATS Specific Addresses
        // ========================================
        
        // These are module-specific for PATS operations
        public const uint PATS_BCM_TX = BCM_TX;
        public const uint PATS_BCM_RX = BCM_RX;
        public const uint PATS_PCM_TX = PCM_TX;
        public const uint PATS_PCM_RX = PCM_RX;
        public const uint PATS_RFA_TX = RFA_TX;
        public const uint PATS_RFA_RX = RFA_RX;
        
        /// <summary>
        /// Gets module name from CAN ID
        /// </summary>
        public static string GetModuleName(uint canId)
        {
            return canId switch
            {
                BCM_TX or BCM_RX => "BCM",
                PCM_TX or PCM_RX => "PCM",
                TCM_TX or TCM_RX => "TCM",
                ABS_TX or ABS_RX => "ABS",
                IPC_TX or IPC_RX => "IPC",
                RFA_TX or RFA_RX => "RFA",
                APIM_TX or APIM_RX => "APIM",
                PSCM_TX or PSCM_RX => "PSCM",
                GWM_TX or GWM_RX => "GWM",
                SCCM_TX or SCCM_RX => "SCCM",
                ESCL_TX or ESCL_RX => "ESCL",
                DDM_TX or DDM_RX => "DDM",
                PDM_TX or PDM_RX => "PDM",
                FCIM_TX or FCIM_RX => "FCIM",
                RCM_TX or RCM_RX => "RCM",
                BROADCAST_TX => "BROADCAST",
                _ => $"Unknown (0x{canId:X3})"
            };
        }
    }
}
