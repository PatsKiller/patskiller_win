using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PatsKillerPro
{
    /// <summary>
    /// Professional OAuth login form matching PatsKiller Pro mockup v2
    /// Features: Google OAuth primary, Email/password secondary, No QR code
    /// </summary>
    public class GoogleLoginForm : Form
    {

        // ============ LOCAL LOGGER (BUILD-SAFE) ============
        private static class Logger
        {
            public static void Info(string message) => Write("INFO", message, null);
            public static void Debug(string message) => Write("DEBUG", message, null);
            public static void Warning(string message) => Write("WARN", message, null);
            public static void Error(string message) => Write("ERROR", message, null);
            public static void Error(string message, Exception ex) => Write("ERROR", message, ex);

            private static void Write(string level, string message, Exception? ex)
            {
                try
                {
                    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                    if (ex != null) line += $"\n{ex}";
                    System.Diagnostics.Trace.WriteLine(line);
                }
                catch { }
            }
        }

        // ============ UI CONTROLS ============
        private Panel _headerPanel = null!;
        private Panel _contentPanel = null!;
        private PictureBox _logoBox = null!;
        private Label _lblTitle = null!;
        private Label _lblSubtitle = null!;
        private Label _lblTokens = null!;
        private Label _lblStatus = null!;

        // Content panels for different states
        private Panel _loginPanel = null!;
        private FlowLayoutPanel _loginStack = null!;
        private Panel _waitingPanel = null!;
        private Panel _successPanel = null!;
        private Panel _errorPanel = null!;

        // ============ THEME COLORS (Matching Mockup) ============
        private readonly Color _colorBackground = ColorTranslator.FromHtml("#111827");     // Top (deep navy)
        private readonly Color _colorBackground2 = ColorTranslator.FromHtml("#0B1220");    // Bottom (near-black navy)

        private readonly Color _colorHeader = ColorTranslator.FromHtml("#2D3346");
        private readonly Color _colorHeader2 = ColorTranslator.FromHtml("#242A3B");
        private readonly Color _colorHeaderBorder = ColorTranslator.FromHtml("#3A4158");

        private readonly Color _colorPanel = ColorTranslator.FromHtml("#1B2433");
        private readonly Color _colorInput = ColorTranslator.FromHtml("#1F2937");
        private readonly Color _colorBorder = ColorTranslator.FromHtml("#374151");

        private readonly Color _colorText = ColorTranslator.FromHtml("#F8FAFC");
        private readonly Color _colorTextDim = ColorTranslator.FromHtml("#CBD5E1");     // secondary text
        private readonly Color _colorTextMuted = ColorTranslator.FromHtml("#94A3B8");   // tertiary text
        private readonly Color _colorLabelText = ColorTranslator.FromHtml("#D1D5DB");   // field labels

        private readonly Color _colorRed = ColorTranslator.FromHtml("#E94796");            // PatsKiller Pink
        private readonly Color _colorRedDark = ColorTranslator.FromHtml("#DB2777");        // Button Pink (darker)
        private readonly Color _colorGreen = ColorTranslator.FromHtml("#22C55E");          // Success Green
        private readonly Color _colorGoogleBtn = Color.White;                              // Google button surface

        // ============ RESULTS ============
        public string? AuthToken { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? UserEmail { get; private set; }
        public int TokenCount { get; private set; }

        // ============ API CONFIGURATION ============
        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjac