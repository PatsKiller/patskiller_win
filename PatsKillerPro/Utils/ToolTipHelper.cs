using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// Centralized tooltip definitions for all buttons
    /// </summary>
    public static class ToolTipHelper
    {
        // Tooltip dictionary for GetToolTip method
        private static readonly Dictionary<string, string> _tooltips = new()
        {
            // Connection
            ["ScanDevices"] = "Scan for connected J2534 pass-thru devices\n[FREE]",
            ["Connect"] = "Connect to selected J2534 device\n[FREE]",
            ["Disconnect"] = "Disconnect from J2534 device\n[FREE]",
            // Vehicle Info
            ["ReadVin"] = "Read Vehicle Identification Number from BCM\n[FREE]",
            // Security Codes
            ["GetIncode"] = "Calculate incode from outcode at patskiller.com\n[1 TOKEN]",
            ["CopyOutcode"] = "Copy outcode to clipboard\n[FREE]",
            // Key Programming
            ["ProgramKey"] = "Program new transponder key to vehicle\n[FREE if session active, 1 TOKEN if new session]",
            ["EraseAllKeys"] = "Erase all programmed keys from vehicle\n⚠️ Vehicle will NOT start until 2+ keys programmed!\n[FREE if session active, 1 TOKEN if new session]",
            ["ReadKeys"] = "Read current programmed key count\n[FREE]",
            // Gateway
            ["UnlockGateway"] = "Unlock security gateway + BCM for key operations\nRequired for 2020+ vehicles before any PATS operation\n[1 TOKEN]",
            ["CloseSession"] = "Close current security session and stop keep-alive\n[FREE]",
            // DTC Operations
            ["ClearP160A"] = "Clear P160A - PCM calibration parameter reset\nRequired after PCM replacement or PATS initialization\n⚠️ Requires ignition ON/OFF sequence during procedure\n[1 TOKEN]",
            ["ClearB10A2"] = "Clear B10A2 - BCM configuration error code\nRequired when BCM reports configuration mismatch\n[1 TOKEN]",
            ["ClearCrashEvent"] = "Clear crash sensor activation flag from BCM\nRequired after collision repairs when airbag deployed\n[1 TOKEN]",
            ["ClearAllDtcs"] = "Clear all diagnostic trouble codes from all modules\n[FREE]",
            ["ClearKam"] = "Clear Keep Alive Memory (adaptive learning data)\nResets fuel trims, idle learning, throttle position\n[FREE]",
            // BCM Operations
            ["KeypadCode"] = "Read or write 5-digit door keypad entry code\n[1 TOKEN]",
            ["BcmFactoryDefaults"] = "⚠️ BCM FACTORY DEFAULTS ⚠️\n\nFull BCM reset to factory configuration\n\n⚠️ IMPORTANT: Vehicle MUST be adapted with\nfactory scanner (IDS/FDRS) after this operation!\n\n[2-3 TOKENS]",
            // Module Operations
            ["ReadModuleInfo"] = "Scan all vehicle modules and read information\n[FREE]",
            ["ParameterReset"] = "Reset minimum keys parameter on selected modules\nOpens module selection dialog to choose BCM, PCM, ABS, etc.\n[1 TOKEN per module]",
            // ESCL
            ["EsclInit"] = "Initialize Electronic Steering Column Lock\nRequired after ESCL replacement or mismatch\n[1 TOKEN]",
            // Other
            ["DisableBcmSecurity"] = "Disable BCM security mode\n[1 TOKEN]",
            ["VehicleReset"] = "Soft reset BCM + PCM + ABS together\nForces modules to reload from memory\n[FREE]",
            // Resources
            ["UserGuide"] = "Open PatsKiller Pro user guide and FAQs\n[FREE]",
            ["BuyTokens"] = "Purchase additional tokens at patskiller.com\n[FREE]",
            ["ContactSupport"] = "Contact PatsKiller support team\n[FREE]",
            // === PHASE 2: TARGET BLOCKS ===
            ["TargetsReadAll"] = "Read PATS target blocks from all modules\n(BCM, PCM, ABS, RFA, ESCL)\n[FREE]",
            ["TargetsWrite"] = "Write target blocks to selected modules\n⚠️ Requires security access\n[1 TOKEN per module]",
            ["TargetsImport"] = "Import target blocks from JSON file\n[FREE]",
            ["TargetsExport"] = "Export current target blocks to JSON file\n[FREE]",
            ["OpenTargets"] = "Open PATS Target Blocks editor\nRead/Write target data for BCM, PCM, ABS, RFA, ESCL\n[Read=FREE, Write=1 TOKEN/module]",
            // === PHASE 2: ENGINEERING MODE ===
            ["EngineeringDidRead"] = "Read data by identifier (DID) from module\n[FREE]",
            ["EngineeringDidWrite"] = "Write data by identifier (DID) to module\n⚠️ Use with caution!\n[1 TOKEN]",
            ["EngineeringRoutineStart"] = "Start routine control (0x01)\n[1 TOKEN]",
            ["EngineeringRoutineStop"] = "Stop routine control (0x02)\n[FREE]",
            ["EngineeringRawSend"] = "Send raw UDS request to module\n⚠️ DANGER: May damage module!\n[1 TOKEN for write operations]",
            ["OpenEngineering"] = "Open Engineering Mode\nAdvanced DID read/write, routine control, raw UDS\n⚠️ For experts only!\n[Various token costs]",
            // === PHASE 2: KEY COUNTERS ===
            ["KeyCountersRead"] = "Read Min/Max key counters from BCM\nDIDs: 0x5B13 (Min), 0x5B14 (Max)\n[FREE]",
            ["KeyCountersWriteMin"] = "Write minimum key counter value\nMin = minimum keys required for vehicle start\nTypical value: 2 (always need 2+ keys)\n[FREE]",
            ["KeyCountersWriteMax"] = "Write maximum key counter value\nMax = maximum keys that can be programmed\nTypical value: 8 (8 slots available)\n[FREE]",
            ["KeyCountersWriteBoth"] = "Write BOTH Min and Max counters\nSingle BCM unlock session, writes both values\n⚠️ Min must be ≤ Max\n[FREE]",
            ["OpenKeyCounters"] = "Open Key Counters manager\nRead/Set Min/Max key counter values\n[ALL OPERATIONS FREE]",
            // === PHASE 2: ENGINEERING MODE DETAILED ===
            ["EngineeringDidReadCommon"] = "Read common DID from module\nSelect from dropdown: VIN, PATS Status, Key Count, etc.\n[FREE]",
            ["EngineeringDidReadCustom"] = "Read custom DID (4 hex digits)\nEnter DID like F190, C126, 5B13\n[FREE]",
            ["EngineeringDidWriteCustom"] = "Write data to custom DID\n⚠️ CAUTION: May affect module operation!\nRequires security access (incode)\n[FREE]",
            ["EngineeringRoutineStart"] = "Start routine control (0x01)\n[FREE]",
            ["EngineeringRoutineStop"] = "Stop routine control (0x02)\n[FREE]",
            ["EngineeringRoutineResults"] = "Read routine control results (0x03)\nCheck status of previously started routine\n[FREE]",
            ["EngineeringPatsOutcode"] = "Read PATS outcode from current module\nUsed for incode calculation\n[FREE]",
            ["EngineeringPatsIncode"] = "Calculate incode from outcode via API\nRequires valid outcode\n[1 TOKEN - only incode calc costs tokens]",
            ["EngineeringPatsUnlock"] = "Submit incode to unlock PATS\nRuns routine 0x716D with incode\n[FREE]",
            ["EngineeringRawRead"] = "Send raw UDS read request\nFormat: Service ID + Data (hex)\nExample: 22 F1 90 (Read VIN)\n[FREE]",
            ["EngineeringRawWrite"] = "Send raw UDS write request\n⚠️ DANGER: May damage module!\nExample: 2E XX XX YY (Write DID)\n[FREE]",
            // === PHASE 2: TARGETS ===
            ["TargetsReadAll"] = "Read PATS target blocks from all modules\n(BCM, PCM, ABS, RFA, ESCL)\n[FREE]",
            ["TargetsWrite"] = "Write target blocks to selected modules\n⚠️ Requires security access\n[FREE]",
            ["TargetsImport"] = "Import target blocks from JSON file\n[FREE]",
            ["TargetsExport"] = "Export current target blocks to JSON file\n[FREE]",
            ["OpenTargets"] = "Open PATS Target Blocks editor\nRead/Write target data for BCM, PCM, ABS, RFA, ESCL\n[ALL OPERATIONS FREE]",
            ["OpenEngineering"] = "Open Engineering Mode\nAdvanced DID read/write, routine control, raw UDS\n⚠️ For experts only!\n[ALL FREE except Get Incode = 1 TOKEN]",
        };

        // Tooltip text definitions (legacy static class)
        public static class Tips
        {
            public const string ScanDevices = "Scan for connected J2534 pass-thru devices\n[FREE]";
            public const string Connect = "Connect to selected J2534 device\n[FREE]";
            public const string Disconnect = "Disconnect from J2534 device\n[FREE]";
            public const string ReadVin = "Read Vehicle Identification Number from BCM\n[FREE]";
            public const string GetIncode = "Calculate incode from outcode at patskiller.com\n[1 TOKEN]";
            public const string CopyOutcode = "Copy outcode to clipboard\n[FREE]";
            public const string ProgramKey = "Program new transponder key to vehicle\n[FREE if session active, 1 TOKEN if new session]";
            public const string EraseAllKeys = "Erase all programmed keys from vehicle\n⚠️ Vehicle will NOT start until 2+ keys programmed!\n[FREE if session active, 1 TOKEN if new session]";
            public const string ReadKeys = "Read current programmed key count\n[FREE]";
            public const string UnlockGateway = "Unlock security gateway + BCM for key operations\nRequired for 2020+ vehicles before any PATS operation\n[1 TOKEN]";
            public const string CloseSession = "Close current security session and stop keep-alive\n[FREE]";
            public const string ClearP160A = "Clear P160A - PCM calibration parameter reset\n[1 TOKEN]";
            public const string ClearB10A2 = "Clear B10A2 - BCM configuration error code\n[1 TOKEN]";
            public const string ClearCrashEvent = "Clear crash sensor activation flag from BCM\n[1 TOKEN]";
            public const string ClearAllDtcs = "Clear all diagnostic trouble codes from all modules\n[FREE]";
            public const string ClearKam = "Clear Keep Alive Memory\n[FREE]";
            public const string KeypadCode = "Read or write 5-digit door keypad entry code\n[1 TOKEN]";
            public const string BcmFactoryDefaults = "⚠️ BCM FACTORY DEFAULTS ⚠️\n[2-3 TOKENS]";
            public const string ReadModuleInfo = "Scan all vehicle modules and read information\n[FREE]";
            public const string ParameterReset = "Reset minimum keys parameter\n[1 TOKEN per module]";
            public const string EsclInit = "Initialize Electronic Steering Column Lock\n[1 TOKEN]";
            public const string DisableBcmSecurity = "Disable BCM security mode\n[1 TOKEN]";
            public const string VehicleReset = "Soft reset BCM + PCM + ABS\n[FREE]";
            public const string UserGuide = "Open PatsKiller Pro user guide\n[FREE]";
            public const string BuyTokens = "Purchase additional tokens\n[FREE]";
            public const string ContactSupport = "Contact PatsKiller support team\n[FREE]";
        }

        /// <summary>
        /// Get tooltip text by key name
        /// </summary>
        public static string GetToolTip(string key) => _tooltips.TryGetValue(key, out var tip) ? tip : $"{key}\n[Unknown]";

        /// <summary>
        /// Create and configure a ToolTip instance
        /// </summary>
        public static ToolTip CreateToolTip()
        {
            var tip = new ToolTip
            {
                AutoPopDelay = 15000,  // 15 seconds - long enough to read
                InitialDelay = 300,     // 300ms - show quickly on hover
                ReshowDelay = 100,      // 100ms - fast reshow when moving between buttons
                ShowAlways = true,
                IsBalloon = false,
                UseAnimation = true,
                UseFading = true
            };
            // Note: BackColor/ForeColor don't work on Windows 10/11 themed tooltips
            // They show with default Windows styling which is fine
            return tip;
        }

        /// <summary>
        /// Add help icon button next to a control
        /// </summary>
        public static Button CreateHelpButton(ToolTip toolTip, string tooltipText, Control parent)
        {
            var btn = new Button
            {
                Text = "?",
                Size = new Size(24, 24),
                BackColor = Color.FromArgb(54, 54, 64),
                ForeColor = Color.FromArgb(160, 160, 165),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Help,
                TabStop = false
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 90);
            
            toolTip.SetToolTip(btn, tooltipText);
            
            return btn;
        }
    }
}