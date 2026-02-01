using System.Drawing;
using System.Windows.Forms;

namespace PatsKillerPro.Utils
{
    /// <summary>
    /// Centralized tooltip definitions for all buttons
    /// </summary>
    public static class ToolTipHelper
    {
        // Tooltip text definitions
        public static class Tips
        {
            // Connection
            public const string ScanDevices = "Scan for connected J2534 pass-thru devices\n[FREE]";
            public const string Connect = "Connect to selected J2534 device\n[FREE]";
            public const string Disconnect = "Disconnect from J2534 device\n[FREE]";

            // Vehicle Info
            public const string ReadVin = "Read Vehicle Identification Number from BCM\n[FREE]";

            // Security Codes
            public const string GetIncode = "Calculate incode from outcode at patskiller.com\n[1 TOKEN]";
            public const string CopyOutcode = "Copy outcode to clipboard\n[FREE]";

            // Key Programming
            public const string ProgramKey = "Program new transponder key to vehicle\n[FREE if session active, 1 TOKEN if new session]";
            public const string EraseAllKeys = "Erase all programmed keys from vehicle\n⚠️ Vehicle will NOT start until 2+ keys programmed!\n[FREE if session active, 1 TOKEN if new session]";
            public const string ReadKeys = "Read current programmed key count\n[FREE]";

            // Gateway
            public const string UnlockGateway = "Unlock security gateway + BCM for key operations\nRequired for 2020+ vehicles before any PATS operation\n[1 TOKEN]";
            public const string CloseSession = "Close current security session and stop keep-alive\n[FREE]";

            // DTC Operations
            public const string ClearP160A = "Clear P160A - PCM calibration parameter reset\nRequired after PCM replacement or PATS initialization\n⚠️ Requires ignition ON/OFF sequence during procedure\n[1 TOKEN]";
            public const string ClearB10A2 = "Clear B10A2 - BCM configuration error code\nRequired when BCM reports configuration mismatch\n[1 TOKEN]";
            public const string ClearCrushEvent = "Clear crash sensor activation flag from BCM\nRequired after collision repairs when airbag deployed\n[1 TOKEN]";
            public const string ClearAllDtcs = "Clear all diagnostic trouble codes from all modules\n[FREE]";
            public const string ClearKam = "Clear Keep Alive Memory (adaptive learning data)\nResets fuel trims, idle learning, throttle position\n[FREE]";

            // BCM Operations
            public const string KeypadCode = "Read or write 5-digit door keypad entry code\n[1 TOKEN]";
            public const string BcmFactoryDefaults = "⚠️ BCM FACTORY DEFAULTS ⚠️\n\nFull BCM reset to factory configuration\n\n• Resets ALL BCM settings:\n  - Window positions and settings\n  - Door lock configurations\n  - Lighting preferences\n  - Remote start settings\n  - Custom programming\n\n⚠️ IMPORTANT: Vehicle MUST be adapted with\nfactory scanner (IDS/FDRS) after this operation!\n\n[2-3 TOKENS]";

            // Module Operations
            public const string ReadModuleInfo = "Scan all vehicle modules and read information\n[FREE]";
            public const string ParameterReset = "Reset minimum keys parameter on selected modules\nOpens module selection dialog to choose BCM, PCM, ABS, etc.\n[1 TOKEN per module]";

            // ESCL
            public const string EsclInit = "Initialize Electronic Steering Column Lock\nRequired after ESCL replacement or mismatch\n[1 TOKEN]";

            // Other
            public const string DisableBcmSecurity = "Disable BCM security mode\n[1 TOKEN]";
            public const string VehicleReset = "Soft reset BCM + PCM + ABS together\nForces modules to reload from memory\n[FREE]";

            // Resources
            public const string UserGuide = "Open PatsKiller Pro user guide and FAQs\n[FREE]";
            public const string BuyTokens = "Purchase additional tokens at patskiller.com\n[FREE]";
            public const string ContactSupport = "Contact PatsKiller support team\n[FREE]";
        }

        /// <summary>
        /// Create and configure a ToolTip instance
        /// </summary>
        public static ToolTip CreateToolTip()
        {
            return new ToolTip
            {
                AutoPopDelay = 15000,  // 15 seconds
                InitialDelay = 500,
                ReshowDelay = 200,
                ShowAlways = true,
                IsBalloon = false,
                BackColor = Color.FromArgb(45, 45, 50),
                ForeColor = Color.FromArgb(240, 240, 240)
            };
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
