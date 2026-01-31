using System;
using System.Collections.Generic;
using PatsKillerPro.Vehicle;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// Platform-specific module routing configuration.
    /// Determines which modules to target for each operation based on vehicle platform.
    /// This is the AUTHORITATIVE source for module routing decisions.
    /// </summary>
    public sealed class PlatformRoutingConfig
    {
        /// <summary>Platform identifier</summary>
        public string PlatformCode { get; init; } = "DEFAULT";

        /// <summary>Human-readable platform name</summary>
        public string DisplayName { get; init; } = "Default Platform";

        // ========================================
        // MODULE ADDRESSES
        // ========================================

        /// <summary>Primary PATS module (BCM or RFA)</summary>
        public uint PrimaryModule { get; init; } = ModuleAddresses.BCM_TX;

        /// <summary>Secondary PATS module for keyless (RFA), or 0 if none</summary>
        public uint SecondaryModule { get; init; } = 0;

        /// <summary>Gateway module address (2020+), or 0 if none</summary>
        public uint GatewayModule { get; init; } = 0;

        /// <summary>PCM address for P160A clearing</summary>
        public uint PcmModule { get; init; } = ModuleAddresses.PCM_TX;

        /// <summary>RCM address for crash event clearing</summary>
        public uint RcmModule { get; init; } = ModuleAddresses.RCM_TX;

        /// <summary>SCCM address for ESCL operations</summary>
        public uint SccmModule { get; init; } = ModuleAddresses.SCCM_TX;

        // ========================================
        // CAPABILITY FLAGS
        // ========================================

        /// <summary>Whether vehicle has keyless entry (RFA module)</summary>
        public bool HasKeyless { get; init; } = false;

        /// <summary>Whether gateway unlock is required before BCM operations</summary>
        public bool RequiresGatewayUnlock { get; init; } = false;

        /// <summary>Whether dual-CAN communication is needed</summary>
        public bool RequiresDualCan { get; init; } = false;

        /// <summary>Whether vehicle has ESCL (Electronic Steering Column Lock)</summary>
        public bool HasEscl { get; init; } = false;

        // ========================================
        // CAN BUS CONFIGURATION
        // ========================================

        /// <summary>HS-CAN baud rate (typically 500000)</summary>
        public uint HsCanBaud { get; init; } = 500000;

        /// <summary>MS-CAN baud rate for keyless (typically 125000)</summary>
        public uint MsCanBaud { get; init; } = 125000;

        /// <summary>Which CAN bus the primary module uses</summary>
        public CanBusType PrimaryModuleBus { get; init; } = CanBusType.HsCan;

        /// <summary>Which CAN bus the secondary module uses</summary>
        public CanBusType SecondaryModuleBus { get; init; } = CanBusType.MsCan;

        // ========================================
        // ROUTINE IDS
        // ========================================

        /// <summary>Routine ID for Write Key In Progress (BCM)</summary>
        public ushort WkipRoutineId { get; init; } = 0x716D;

        /// <summary>Routine ID for Write Keyless Key In Progress (RFA)</summary>
        public ushort WkkipRoutineId { get; init; } = 0x716E;

        /// <summary>Routine ID for Erase Keys</summary>
        public ushort EraseKeysRoutineId { get; init; } = 0x716C;

        /// <summary>Routine ID for PATS Initialization</summary>
        public ushort PatsInitRoutineId { get; init; } = 0x716B;

        /// <summary>Routine ID for ESCL Initialization</summary>
        public ushort EsclInitRoutineId { get; init; } = 0xE000;

        // ========================================
        // DATA IDENTIFIERS (DIDs/PIDs)
        // ========================================

        /// <summary>DID for reading VIN</summary>
        public ushort VinDid { get; init; } = 0xF190;

        /// <summary>DID for reading outcode (PrePATS)</summary>
        public ushort OutcodeDid { get; init; } = 0xC1A1;

        /// <summary>DID for reading PATS status (key count)</summary>
        public ushort PatsStatusDid { get; init; } = 0xC126;

        /// <summary>DID for reading key count (alternative)</summary>
        public ushort KeyCountDid { get; init; } = 0xDE00;

        /// <summary>DID for min keys configuration</summary>
        public ushort MinKeysDid { get; init; } = 0x5B13;

        /// <summary>DID for max keys configuration</summary>
        public ushort MaxKeysDid { get; init; } = 0x5B14;

        // ========================================
        // INCODE FORMAT
        // ========================================

        /// <summary>Expected incode length in hex characters (4 for standard, 8 for keyless)</summary>
        public int IncodeLength { get; init; } = 4;

        /// <summary>For keyless: which chars go to BCM (e.g., 0-3)</summary>
        public (int Start, int Length) BcmIncodeRange { get; init; } = (0, 4);

        /// <summary>For keyless: which chars go to RFA (e.g., 4-7)</summary>
        public (int Start, int Length) RfaIncodeRange { get; init; } = (4, 4);

        // ========================================
        // HELPER METHODS
        // ========================================

        /// <summary>Gets the response address for a module (TX + 8)</summary>
        public uint GetResponseAddress(uint txAddress) => txAddress + 8;

        /// <summary>Gets modules that need security unlock for key operations</summary>
        public IEnumerable<uint> GetSecurityTargets()
        {
            yield return PrimaryModule;
            if (HasKeyless && SecondaryModule != 0)
                yield return SecondaryModule;
        }

        /// <summary>Gets modules that need keep-alive during operations</summary>
        public IEnumerable<(uint Module, CanBusType Bus)> GetKeepAliveTargets()
        {
            yield return (PrimaryModule, PrimaryModuleBus);
            if (HasKeyless && SecondaryModule != 0)
                yield return (SecondaryModule, SecondaryModuleBus);
        }

        /// <summary>Splits a keyless incode into BCM and RFA portions</summary>
        public (string BcmIncode, string RfaIncode) SplitKeylessIncode(string incode)
        {
            if (string.IsNullOrEmpty(incode) || incode.Length < 8)
                return (incode, "");

            var bcm = incode.Substring(BcmIncodeRange.Start, BcmIncodeRange.Length);
            var rfa = incode.Substring(RfaIncodeRange.Start, RfaIncodeRange.Length);
            return (bcm, rfa);
        }

        // ========================================
        // PRESET CONFIGURATIONS
        // ========================================

        public static PlatformRoutingConfig Default => new()
        {
            PlatformCode = "DEFAULT",
            DisplayName = "Default (BCM-based)",
            PrimaryModule = ModuleAddresses.BCM_TX,
            SecondaryModule = 0,
            GatewayModule = 0,
            HasKeyless = false,
            RequiresGatewayUnlock = false,
            RequiresDualCan = false,
            IncodeLength = 4
        };

        public static PlatformRoutingConfig F3 => new()
        {
            PlatformCode = "F3",
            DisplayName = "Focus 3 (2011-2018)",
            PrimaryModule = ModuleAddresses.BCM_TX,
            SecondaryModule = 0,
            GatewayModule = 0,
            HasKeyless = false,
            RequiresGatewayUnlock = false,
            RequiresDualCan = false,
            HasEscl = false,
            IncodeLength = 4
        };

        public static PlatformRoutingConfig F3Plus => new()
        {
            PlatformCode = "F3+",
            DisplayName = "Focus 3+ (2019+)",
            PrimaryModule = ModuleAddresses.BCM_TX,
            SecondaryModule = 0,
            GatewayModule = ModuleAddresses.GWM_TX,
            HasKeyless = false,
            RequiresGatewayUnlock = true,  // Gateway REQUIRED
            RequiresDualCan = false,
            HasEscl = true,
            IncodeLength = 4
        };

        public static PlatformRoutingConfig M4 => new()
        {
            PlatformCode = "M4",
            DisplayName = "Mondeo 4 (2007-2014)",
            PrimaryModule = ModuleAddresses.BCM_TX,
            SecondaryModule = ModuleAddresses.RFA_TX,
            GatewayModule = 0,
            HasKeyless = true,
            RequiresGatewayUnlock = false,
            RequiresDualCan = true,
            PrimaryModuleBus = CanBusType.HsCan,
            SecondaryModuleBus = CanBusType.MsCan,
            HasEscl = false,
            IncodeLength = 8,
            BcmIncodeRange = (0, 4),
            RfaIncodeRange = (4, 4)
        };

        public static PlatformRoutingConfig M5 => new()
        {
            PlatformCode = "M5",
            DisplayName = "Mondeo 5 (2015-2022)",
            PrimaryModule = ModuleAddresses.BCM_TX,
            SecondaryModule = ModuleAddresses.RFA_TX,
            GatewayModule = 0,
            HasKeyless = true,
            RequiresGatewayUnlock = false,
            RequiresDualCan = true,
            PrimaryModuleBus = CanBusType.HsCan,
            SecondaryModuleBus = CanBusType.MsCan,
            HasEscl = true,
            IncodeLength = 8,
            BcmIncodeRange = (0, 4),
            RfaIncodeRange = (4, 4)
        };

        public static PlatformRoutingConfig FF2 => new()
        {
            PlatformCode = "FF2",
            DisplayName = "Fiesta/Focus 2 (2008-2017)",
            PrimaryModule = ModuleAddresses.RFA_TX,  // RFA is primary
            SecondaryModule = 0,
            GatewayModule = 0,
            HasKeyless = true,
            RequiresGatewayUnlock = false,
            RequiresDualCan = false,
            PrimaryModuleBus = CanBusType.HsCan,  // RFA on HS-CAN for this platform
            HasEscl = false,
            IncodeLength = 4,
            WkipRoutineId = 0x716E  // Uses WKKIP routine
        };

        public static PlatformRoutingConfig Transit => new()
        {
            PlatformCode = "TRANSIT",
            DisplayName = "Transit (2014-2024)",
            PrimaryModule = ModuleAddresses.BCM_TX,
            SecondaryModule = 0,
            GatewayModule = 0,
            HasKeyless = false,
            RequiresGatewayUnlock = false,
            RequiresDualCan = false,
            HasEscl = false,
            IncodeLength = 4
        };

        public static PlatformRoutingConfig Fiesta => new()
        {
            PlatformCode = "FIESTA",
            DisplayName = "Fiesta (2017-2023)",
            PrimaryModule = ModuleAddresses.RFA_TX,  // RFA is primary
            SecondaryModule = 0,
            GatewayModule = 0,
            HasKeyless = true,
            RequiresGatewayUnlock = false,
            RequiresDualCan = false,
            PrimaryModuleBus = CanBusType.HsCan,
            HasEscl = false,
            IncodeLength = 4,
            WkipRoutineId = 0x716E
        };

        public static PlatformRoutingConfig Explorer2020 => new()
        {
            PlatformCode = "EXPLORER_2020",
            DisplayName = "Explorer/Expedition (2020+)",
            PrimaryModule = ModuleAddresses.BCM_TX,
            SecondaryModule = ModuleAddresses.RFA_TX,
            GatewayModule = ModuleAddresses.GWM_TX,
            HasKeyless = true,
            RequiresGatewayUnlock = true,  // Gateway REQUIRED
            RequiresDualCan = false,
            HasEscl = true,
            IncodeLength = 8,
            BcmIncodeRange = (0, 4),
            RfaIncodeRange = (4, 4)
        };

        public static PlatformRoutingConfig F150_14 => new()
        {
            PlatformCode = "F150_14",
            DisplayName = "F-150 14th Gen (2021+)",
            PrimaryModule = ModuleAddresses.BCM_TX,
            SecondaryModule = 0,
            GatewayModule = ModuleAddresses.GWM_TX,
            HasKeyless = false,
            RequiresGatewayUnlock = true,  // Gateway REQUIRED
            RequiresDualCan = false,
            HasEscl = true,
            IncodeLength = 4
        };
    }

    /// <summary>
    /// CAN bus type identifier
    /// </summary>
    public enum CanBusType
    {
        HsCan,   // High-Speed CAN (500 kbps)
        MsCan    // Medium-Speed CAN (125 kbps)
    }

    /// <summary>
    /// Registry of platform routing configurations.
    /// </summary>
    public static class PlatformRoutingRegistry
    {
        private static readonly Dictionary<string, PlatformRoutingConfig> _configs = new(StringComparer.OrdinalIgnoreCase)
        {
            // Focus
            ["C346"] = PlatformRoutingConfig.F3,
            ["C519"] = PlatformRoutingConfig.F3Plus,
            ["F3"] = PlatformRoutingConfig.F3,
            ["F3+"] = PlatformRoutingConfig.F3Plus,

            // Mondeo/Fusion
            ["CD4"] = PlatformRoutingConfig.M5,
            ["M4"] = PlatformRoutingConfig.M4,
            ["M5"] = PlatformRoutingConfig.M5,

            // Fiesta
            ["B299"] = PlatformRoutingConfig.FF2,
            ["B479"] = PlatformRoutingConfig.Fiesta,
            ["FF2"] = PlatformRoutingConfig.FF2,
            ["FIESTA"] = PlatformRoutingConfig.Fiesta,

            // Transit
            ["V363"] = PlatformRoutingConfig.Transit,
            ["V408"] = PlatformRoutingConfig.Transit,
            ["TRANSIT"] = PlatformRoutingConfig.Transit,

            // Trucks
            ["P552"] = PlatformRoutingConfig.Default,  // F-150 13th, no gateway
            ["P702"] = PlatformRoutingConfig.F150_14,  // F-150 14th, gateway required
            ["F150_14"] = PlatformRoutingConfig.F150_14,

            // SUVs
            ["U625"] = PlatformRoutingConfig.Explorer2020,
            ["U611"] = PlatformRoutingConfig.Explorer2020,
            ["U725"] = PlatformRoutingConfig.Explorer2020,
            ["EXPLORER_2020"] = PlatformRoutingConfig.Explorer2020,

            // Edge/Escape/Lincoln
            ["CD539"] = PlatformRoutingConfig.M5,
            ["CX482"] = PlatformRoutingConfig.M5,
            ["CX483"] = PlatformRoutingConfig.M5,
            ["CX430"] = PlatformRoutingConfig.M5,
            ["D544"] = PlatformRoutingConfig.M5,

            // Default
            ["DEFAULT"] = PlatformRoutingConfig.Default
        };

        /// <summary>
        /// Gets the routing configuration for a platform code.
        /// </summary>
        public static PlatformRoutingConfig GetConfig(string? platformCode)
        {
            if (string.IsNullOrEmpty(platformCode))
                return PlatformRoutingConfig.Default;

            return _configs.TryGetValue(platformCode, out var config)
                ? config
                : PlatformRoutingConfig.Default;
        }

        /// <summary>
        /// Determines if gateway unlock is required based on model year.
        /// </summary>
        public static bool RequiresGateway(int modelYear)
        {
            return modelYear >= 2020;
        }

        /// <summary>
        /// Registers a custom routing configuration.
        /// </summary>
        public static void RegisterConfig(string platformCode, PlatformRoutingConfig config)
        {
            _configs[platformCode] = config;
        }
    }
}
