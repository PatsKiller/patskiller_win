using System;
using System.Collections.Generic;

namespace PatsKillerPro.Services.Workflow
{
    /// <summary>
    /// Platform-specific timing/pacing configuration per EZimmo spec.
    /// Centralizes all critical timing delays to prevent "works on bench, fails in car" behavior.
    /// </summary>
    public sealed class PlatformPacingConfig
    {
        /// <summary>Platform identifier (e.g., "F3", "M5", "Transit")</summary>
        public string PlatformCode { get; init; } = "DEFAULT";

        /// <summary>Human-readable platform name</summary>
        public string DisplayName { get; init; } = "Default Platform";

        // ========================================
        // PHASE BOUNDARY DELAYS (Critical!)
        // ========================================

        /// <summary>Delay after 0x10 (Diagnostic Session Control) success</summary>
        public TimeSpan PostSessionStartDelay { get; init; } = TimeSpan.FromMilliseconds(50);

        /// <summary>Delay after 0x27 (Security Access) success - CRITICAL!</summary>
        public TimeSpan PostSecurityUnlockDelay { get; init; } = TimeSpan.FromMilliseconds(100);

        /// <summary>Delay after 0x31 01 (Routine Start) success</summary>
        public TimeSpan PostRoutineStartDelay { get; init; } = TimeSpan.FromMilliseconds(200);

        /// <summary>Delay after 0x2E (Write Data) success - NVM write time</summary>
        public TimeSpan PostWriteDelay { get; init; } = TimeSpan.FromMilliseconds(100);

        /// <summary>Delay after module reset before re-establishing communication</summary>
        public TimeSpan PostResetDelay { get; init; } = TimeSpan.FromMilliseconds(2000);

        /// <summary>Delay after ignition cycle (user action) before continuing</summary>
        public TimeSpan PostIgnitionCycleDelay { get; init; } = TimeSpan.FromMilliseconds(5000);

        // ========================================
        // KEEP-ALIVE CONFIGURATION
        // ========================================

        /// <summary>Interval for Tester Present (0x3E) messages - Single CAN</summary>
        public TimeSpan TesterPresentInterval { get; init; } = TimeSpan.FromMilliseconds(2000);

        /// <summary>Whether to send keep-alive on both HS-CAN and MS-CAN channels</summary>
        public bool DualChannelKeepAlive { get; init; } = false;

        /// <summary>Interval for dual-channel keep-alive (faster for keyless)</summary>
        public TimeSpan DualChannelKeepAliveInterval { get; init; } = TimeSpan.FromMilliseconds(150);

        // ========================================
        // VERIFICATION POLLING
        // ========================================

        /// <summary>Interval between verification poll attempts</summary>
        public TimeSpan VerifyPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

        /// <summary>Maximum number of verification poll attempts</summary>
        public int MaxVerifyAttempts { get; init; } = 10;

        /// <summary>Total timeout for routine completion polling</summary>
        public TimeSpan RoutineCompletionTimeout { get; init; } = TimeSpan.FromSeconds(15);

        /// <summary>Longer timeout for PATS Init (takes more time)</summary>
        public TimeSpan PatsInitTimeout { get; init; } = TimeSpan.FromSeconds(30);

        // ========================================
        // MESSAGE TIMEOUTS
        // ========================================

        /// <summary>Single message response timeout</summary>
        public TimeSpan MessageTimeout { get; init; } = TimeSpan.FromMilliseconds(3000);

        /// <summary>Extended timeout for slow operations</summary>
        public TimeSpan ExtendedMessageTimeout { get; init; } = TimeSpan.FromMilliseconds(5000);

        // ========================================
        // SECURITY SESSION
        // ========================================

        /// <summary>How long the security session remains valid after unlock</summary>
        public TimeSpan SecuritySessionDuration { get; init; } = TimeSpan.FromMinutes(15);

        /// <summary>Warning threshold before session expires</summary>
        public TimeSpan SecuritySessionWarningThreshold { get; init; } = TimeSpan.FromMinutes(2);

        // ========================================
        // EFFECTIVE KEEP-ALIVE INTERVAL
        // ========================================

        /// <summary>Gets the effective keep-alive interval based on dual-channel setting</summary>
        public TimeSpan EffectiveKeepAliveInterval =>
            DualChannelKeepAlive ? DualChannelKeepAliveInterval : TesterPresentInterval;

        // ========================================
        // PRESET CONFIGURATIONS
        // ========================================

        /// <summary>Default configuration - safe for most vehicles</summary>
        public static PlatformPacingConfig Default => new()
        {
            PlatformCode = "DEFAULT",
            DisplayName = "Default (Safe Timing)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(75),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(150),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(250),
            PostWriteDelay = TimeSpan.FromMilliseconds(150),
            TesterPresentInterval = TimeSpan.FromMilliseconds(2000),
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 12
        };

        /// <summary>Focus 3 (2011-2018) - Standard BCM-based PATS</summary>
        public static PlatformPacingConfig F3 => new()
        {
            PlatformCode = "F3",
            DisplayName = "Focus 3 (2011-2018)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(50),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(100),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(200),
            PostWriteDelay = TimeSpan.FromMilliseconds(100),
            TesterPresentInterval = TimeSpan.FromMilliseconds(2000),
            DualChannelKeepAlive = false,
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 10
        };

        /// <summary>Focus 3+ (2019+) - Gateway required</summary>
        public static PlatformPacingConfig F3Plus => new()
        {
            PlatformCode = "F3+",
            DisplayName = "Focus 3+ (2019+)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(75),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(150),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(250),
            PostWriteDelay = TimeSpan.FromMilliseconds(150),
            TesterPresentInterval = TimeSpan.FromMilliseconds(2000),
            DualChannelKeepAlive = false,
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 12
        };

        /// <summary>Mondeo 4 (2007-2014) - Dual CAN, RFA + BCM</summary>
        public static PlatformPacingConfig M4 => new()
        {
            PlatformCode = "M4",
            DisplayName = "Mondeo 4 (2007-2014)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(100),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(100),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(250),
            PostWriteDelay = TimeSpan.FromMilliseconds(150),
            TesterPresentInterval = TimeSpan.FromMilliseconds(150), // Fast for dual-CAN
            DualChannelKeepAlive = true,
            DualChannelKeepAliveInterval = TimeSpan.FromMilliseconds(150),
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 12
        };

        /// <summary>Mondeo 5 (2015-2022) - Dual CAN, RFA + BCM</summary>
        public static PlatformPacingConfig M5 => new()
        {
            PlatformCode = "M5",
            DisplayName = "Mondeo 5 (2015-2022)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(100),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(150),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(250),
            PostWriteDelay = TimeSpan.FromMilliseconds(150),
            TesterPresentInterval = TimeSpan.FromMilliseconds(150),
            DualChannelKeepAlive = true,
            DualChannelKeepAliveInterval = TimeSpan.FromMilliseconds(150),
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 15
        };

        /// <summary>Fiesta/Focus 2 (2008-2017) - RFA primary</summary>
        public static PlatformPacingConfig FF2 => new()
        {
            PlatformCode = "FF2",
            DisplayName = "Fiesta/Focus 2 (2008-2017)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(50),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(100),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(200),
            PostWriteDelay = TimeSpan.FromMilliseconds(100),
            TesterPresentInterval = TimeSpan.FromMilliseconds(2000),
            DualChannelKeepAlive = false,
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 10
        };

        /// <summary>Transit (2014-2024) - Commercial, BCM-based</summary>
        public static PlatformPacingConfig Transit => new()
        {
            PlatformCode = "TRANSIT",
            DisplayName = "Transit (2014-2024)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(75),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(100),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(200),
            PostWriteDelay = TimeSpan.FromMilliseconds(100),
            TesterPresentInterval = TimeSpan.FromMilliseconds(2000),
            DualChannelKeepAlive = false,
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 10
        };

        /// <summary>Fiesta (2017-2023) - RFA primary</summary>
        public static PlatformPacingConfig Fiesta => new()
        {
            PlatformCode = "FIESTA",
            DisplayName = "Fiesta (2017-2023)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(50),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(100),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(200),
            PostWriteDelay = TimeSpan.FromMilliseconds(100),
            TesterPresentInterval = TimeSpan.FromMilliseconds(2000),
            DualChannelKeepAlive = false,
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 10
        };

        /// <summary>Explorer/Expedition (2020+) - Gateway required</summary>
        public static PlatformPacingConfig ExplorerModern => new()
        {
            PlatformCode = "EXPLORER_2020",
            DisplayName = "Explorer/Expedition (2020+)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(100),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(200),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(300),
            PostWriteDelay = TimeSpan.FromMilliseconds(200),
            TesterPresentInterval = TimeSpan.FromMilliseconds(2000),
            DualChannelKeepAlive = false,
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 15,
            SecuritySessionDuration = TimeSpan.FromMinutes(10)
        };

        /// <summary>F-150 (2015-2020) - Truck platform</summary>
        public static PlatformPacingConfig F150_13 => new()
        {
            PlatformCode = "F150_13",
            DisplayName = "F-150 13th Gen (2015-2020)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(75),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(150),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(250),
            PostWriteDelay = TimeSpan.FromMilliseconds(150),
            TesterPresentInterval = TimeSpan.FromMilliseconds(2000),
            DualChannelKeepAlive = false,
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 12
        };

        /// <summary>F-150 (2021+) - 14th Gen, Gateway required</summary>
        public static PlatformPacingConfig F150_14 => new()
        {
            PlatformCode = "F150_14",
            DisplayName = "F-150 14th Gen (2021+)",
            PostSessionStartDelay = TimeSpan.FromMilliseconds(100),
            PostSecurityUnlockDelay = TimeSpan.FromMilliseconds(200),
            PostRoutineStartDelay = TimeSpan.FromMilliseconds(300),
            PostWriteDelay = TimeSpan.FromMilliseconds(200),
            TesterPresentInterval = TimeSpan.FromMilliseconds(2000),
            DualChannelKeepAlive = false,
            VerifyPollInterval = TimeSpan.FromMilliseconds(500),
            MaxVerifyAttempts = 15,
            SecuritySessionDuration = TimeSpan.FromMinutes(10)
        };
    }

    /// <summary>
    /// Registry of platform pacing configurations.
    /// </summary>
    public static class PlatformPacingRegistry
    {
        private static readonly Dictionary<string, PlatformPacingConfig> _configs = new(StringComparer.OrdinalIgnoreCase)
        {
            // Focus
            ["C346"] = PlatformPacingConfig.F3,           // Focus 3
            ["C519"] = PlatformPacingConfig.F3Plus,       // Focus 4
            ["F3"] = PlatformPacingConfig.F3,
            ["F3+"] = PlatformPacingConfig.F3Plus,
            
            // Mondeo/Fusion
            ["CD4"] = PlatformPacingConfig.M5,            // Mondeo 5 / Fusion
            ["M4"] = PlatformPacingConfig.M4,
            ["M5"] = PlatformPacingConfig.M5,
            
            // Fiesta
            ["B299"] = PlatformPacingConfig.FF2,          // Fiesta 6
            ["B479"] = PlatformPacingConfig.Fiesta,       // Fiesta 7
            ["FF2"] = PlatformPacingConfig.FF2,
            ["FIESTA"] = PlatformPacingConfig.Fiesta,
            
            // Transit
            ["V363"] = PlatformPacingConfig.Transit,
            ["V408"] = PlatformPacingConfig.Transit,
            ["TRANSIT"] = PlatformPacingConfig.Transit,
            
            // Trucks
            ["P552"] = PlatformPacingConfig.F150_13,      // F-150 13th
            ["P702"] = PlatformPacingConfig.F150_14,      // F-150 14th
            ["F150_13"] = PlatformPacingConfig.F150_13,
            ["F150_14"] = PlatformPacingConfig.F150_14,
            
            // SUVs
            ["U625"] = PlatformPacingConfig.ExplorerModern,  // Explorer 2020+
            ["U611"] = PlatformPacingConfig.ExplorerModern,  // Aviator
            ["CX482"] = PlatformPacingConfig.M5,             // Escape/Kuga 2020+
            ["U725"] = PlatformPacingConfig.ExplorerModern,  // Bronco
            ["CX430"] = PlatformPacingConfig.M5,             // Bronco Sport
            
            // Edge/Lincoln
            ["CD539"] = PlatformPacingConfig.M5,          // Edge/Nautilus
            ["D544"] = PlatformPacingConfig.M5,           // Continental
            ["CX483"] = PlatformPacingConfig.M5,          // Corsair
            
            // Default
            ["DEFAULT"] = PlatformPacingConfig.Default
        };

        /// <summary>
        /// Gets the pacing configuration for a platform code.
        /// </summary>
        public static PlatformPacingConfig GetConfig(string? platformCode)
        {
            if (string.IsNullOrEmpty(platformCode))
                return PlatformPacingConfig.Default;

            return _configs.TryGetValue(platformCode, out var config)
                ? config
                : PlatformPacingConfig.Default;
        }

        /// <summary>
        /// Determines the pacing configuration from a VIN.
        /// </summary>
        public static PlatformPacingConfig GetConfigFromVin(string? vin)
        {
            if (string.IsNullOrEmpty(vin) || vin.Length < 10)
                return PlatformPacingConfig.Default;

            // Get model year from position 10
            var yearChar = vin[9];
            var year = DecodeModelYear(yearChar);

            // For 2020+ vehicles, use more conservative timing
            if (year >= 2020)
            {
                return PlatformPacingConfig.ExplorerModern;
            }

            return PlatformPacingConfig.Default;
        }

        private static int DecodeModelYear(char c)
        {
            return c switch
            {
                'L' => 2020, 'M' => 2021, 'N' => 2022, 'P' => 2023,
                'R' => 2024, 'S' => 2025, 'T' => 2026,
                'J' => 2018, 'K' => 2019,
                'H' => 2017, 'G' => 2016, 'F' => 2015, 'E' => 2014,
                'D' => 2013, 'C' => 2012, 'B' => 2011, 'A' => 2010,
                _ => 2020
            };
        }

        /// <summary>
        /// Registers a custom pacing configuration.
        /// </summary>
        public static void RegisterConfig(string platformCode, PlatformPacingConfig config)
        {
            _configs[platformCode] = config;
        }
    }
}
