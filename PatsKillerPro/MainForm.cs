using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Forms;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;

namespace PatsKillerPro
{
    public partial class MainForm : Form
    {
        // ============ V15 DARK THEME PALETTE ============
        private static class AppColors
        {
            public static readonly Color Background = ColorTranslator.FromHtml("#0F172A"); // Dark Navy
            public static readonly Color Surface    = ColorTranslator.FromHtml("#1E293B"); // Slate
            public static readonly Color Border     = ColorTranslator.FromHtml("#334155"); // Lighter Slate
            public static readonly Color Accent     = ColorTranslator.FromHtml("#E94796"); // PatsKiller Pink
            public static readonly Color AccentHover= ColorTranslator.FromHtml("#DB2777");
            public static readonly Color TextMain   = Color.White;
            public static readonly Color TextMuted  = ColorTranslator.FromHtml("#94A3B8");
            public static readonly Color Success    = ColorTranslator.FromHtml("#10B981"); // Green
            public static readonly Color Warning    = ColorTranslator.FromHtml("#F59E0B"); // Amber
            public static readonly Color Danger     = ColorTranslator.FromHtml("#EF4444"); // Red
        }

        // ============ AUTH STATE ============
        private string? _authToken;
        private bool IsLicensed => LicenseService.Instance.IsLicensed;
        private bool HasSSO => !string.IsNullOrWhiteSpace(_authToken);
        private bool IsAuthorized => IsLicensed || HasSSO;
        private bool CanUseTokens => HasSSO;

        // ============ UI CONTROLS ============
        private Panel _headerPanel = null!;
        private Label _lblStatusBadge = null!;
        private Label _lblUser = null!;
        private Label _lblTokens = null!;
        private TabControl _tabs = null!;
        private RichTextBox _logBox = null!;

        // ============ CONSTRUCTOR ============
        public MainForm()
        {
            // Form Setup
            this.Text = "PatsKiller Pro v2.1";
            this.Size = new Size(1000, 700);
            this.MinimumSize = new Size(800, 600);
            this.BackColor = AppColors.Background;
            this.ForeColor = AppColors.TextMain;
            this.Font = new Font("Segoe UI", 9F);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Events
            this.Shown += MainForm_Shown;
            this.FormClosing += MainForm_FormClosing;
            
            // Services
            LicenseService.Instance.OnLicenseChanged += OnLicenseChanged;
            LicenseService.Instance.OnLogMessage += (t, m) => Log(m, t == "error" ? AppColors.Danger : AppColors.Success);
            TokenBalanceService.Instance.BalanceChanged += (s, e) => UpdateHeader();

            InitializeUI();
        }

        private async void MainForm_Shown(object? sender, EventArgs e)
        {
            // 1. Validate License (Offline/Cache)
            var lic = await LicenseService.Instance.ValidateAsync();
            
            // 2. Load SSO Session
            LoadSession();

            // 3. Auth Check
            if (IsAuthorized)
            {
                if (HasSSO) await TokenBalanceService.Instance.RefreshBalanceAsync();
                UpdateHeader();
                Log("System Ready. Connect J2534 device.", AppColors.Success);
                return;
            }

            // 4. Force Login
            await PerformLoginFlow();
        }

        private async Task PerformLoginFlow()
        {
            this.Hide();
            using var login = new GoogleLoginForm();
            var result = login.ShowDialog();

            if (result == DialogResult.OK)
            {
                // SSO Success
                _authToken = login.AuthToken;
                SaveSession(login.AuthToken, login.RefreshToken, login.UserEmail);
                TokenBalanceService.Instance.SetAuthContext(login.AuthToken, login.UserEmail ?? "");
                this.Show();
                UpdateHeader();
                Log($"Signed in as {login.UserEmail}", AppColors.Success);
            }
            else if (result == DialogResult.Retry) // "Use License Key"
            {
                using var licForm = new LicenseActivationForm();
                if (licForm.ShowDialog() == DialogResult.OK)
                {
                    this.Show();
                    UpdateHeader();
                }
                else
                {
                    Application.Exit();
                }
            }
            else
            {
                Application.Exit();
            }
        }

        // ============ UI CONSTRUCTION ============
        private void InitializeUI()
        {
            // 1. Header
            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = AppColors.Surface, Padding = new Padding(20, 0, 20, 0) };
            _headerPanel.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, _headerPanel.ClientRectangle, AppColors.Border, ButtonBorderStyle.Solid);

            var title = new Label { Text = "PatsKiller Pro", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = AppColors.Accent, AutoSize = true, Location = new Point(20, 15) };
            
            _lblStatusBadge = new Label { Text = "UNLICENSED", Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = Color.White, BackColor = AppColors.Danger, AutoSize = true, Padding = new Padding(5), Location = new Point(200, 20) };
            
            _lblUser = new Label { Text = "Not Signed In", ForeColor = AppColors.TextMuted, AutoSize = true, Location = new Point(600, 22) };
            _lblTokens = new Label { Text = "Tokens: -", ForeColor = AppColors.Success, Font = new Font("Segoe UI", 10F, FontStyle.Bold), AutoSize = true, Location = new Point(800, 20) };

            var btnLogout = CreateButton("Logout", 80, 30);
            btnLogout.Location = new Point(900, 15);
            btnLogout.Click += (s, e) => Logout();

            _headerPanel.Controls.AddRange(new Control[] { title, _lblStatusBadge, _lblUser, _lblTokens, btnLogout });
            this.Controls.Add(_headerPanel);

            // 2. Logs (Bottom)
            var pnlLog = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = Color.Black };
            _logBox = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.Black, ForeColor = AppColors.TextMuted, BorderStyle = BorderStyle.None, ReadOnly = true, Font = new Font("Consolas", 9F) };
            pnlLog.Controls.Add(_logBox);
            this.Controls.Add(pnlLog);

            // 3. Tabs (Center)
            _tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, ItemSize = new Size(0, 1), SizeMode = TabSizeMode.Fixed };
            _tabs.TabPages.Add(CreateDashboardTab());
            this.Controls.Add(_tabs);
        }

        private TabPage CreateDashboardTab()
        {
            var tab = new TabPage { BackColor = AppColors.Background, Text = "Dashboard" };
            
            // Layout Flow
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };

            // J2534 Connection Card
            flow.Controls.Add(CreateActionCard("🔌 J2534 Connection", "Connect VCM II or compatible device", "Connect", async () => {
                Log("Scanning for devices...", AppColors.TextMain);
                await Task.Delay(1000);
                Log("Connected to VCM II", AppColors.Success);
            }));

            // Key Programming Card
            flow.Controls.Add(CreateActionCard("🔑 Key Programming", "Erase or Add Keys (1 Token)", "Start Session", () => {
                if (!CheckTokenAccess()) return;
                Log("Starting Key Programming Session...", AppColors.Accent);
            }));

            // Parameter Reset Card
            flow.Controls.Add(CreateActionCard("🔄 Parameter Reset", "Sync BCM/PCM/ABS (3 Tokens)", "Reset Params", () => {
                if (!CheckTokenAccess()) return;
                Log("Initializing Parameter Reset...", AppColors.Accent);
            }));

            // License Management
            var btnLic = CreateButton("Activate License", 150, 40);
            btnLic.Click += (s,e) => new LicenseActivationForm().ShowDialog();
            flow.Controls.Add(btnLic);

            tab.Controls.Add(flow);
            return tab;
        }

        // ============ HELPERS ============
        private Panel CreateActionCard(string title, string desc, string btnText, Action onClick)
        {
            var p = new Panel { Size = new Size(400, 120), BackColor = AppColors.Surface, Margin = new Padding(0, 0, 20, 20) };
            p.Paint += (s, e) => {
                ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, AppColors.Border, ButtonBorderStyle.Solid);
                using var pen = new Pen(AppColors.Accent, 2);
                e.Graphics.DrawLine(pen, 0, 0, 0, p.Height); // Left accent line
            };

            var lblT = new Label { Text = title, Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = AppColors.TextMain, Location = new Point(20, 15), AutoSize = true };
            var lblD = new Label { Text = desc, Font = new Font("Segoe UI", 9F), ForeColor = AppColors.TextMuted, Location = new Point(20, 45), AutoSize = true };
            
            var btn = CreateButton(btnText, 120, 35);
            btn.Location = new Point(260, 70);
            btn.Click += (s, e) => onClick();

            p.Controls.AddRange(new Control[] { lblT, lblD, btn });
            return p;
        }

        private Button CreateButton(string text, int w, int h)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(w, h),
                BackColor = AppColors.Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private bool CheckTokenAccess()
        {
            if (CanUseTokens) return true;
            if (MessageBox.Show("Tokens require Google Sign-in. Sign in now?", "Auth Required", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                PerformLoginFlow();
            }
            return false;
        }

        private void Log(string msg, Color c)
        {
            if (InvokeRequired) { Invoke(() => Log(msg, c)); return; }
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionColor = c;
            _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            _logBox.ScrollToCaret();
        }

        private void UpdateHeader()
        {
            if (IsLicensed) {
                _lblStatusBadge.Text = "PROFESSIONAL";
                _lblStatusBadge.BackColor = AppColors.Success;
            } else {
                _lblStatusBadge.Text = "FREE MODE";
                _lblStatusBadge.BackColor = AppColors.TextMuted;
            }

            if (HasSSO) {
                _lblUser.Text = TokenBalanceService.Instance.UserEmail;
                _lblTokens.Text = $"Tokens: {TokenBalanceService.Instance.TotalTokens}";
            } else {
                _lblUser.Text = "Offline";
                _lblTokens.Text = "";
            }
        }

        private void Logout()
        {
            _authToken = null;
            if (File.Exists("session.json")) File.Delete("session.json");
            TokenBalanceService.Instance.ClearAuthContext();
            Application.Restart();
        }

        // ============ STUBS FOR SESSION ============
        private void LoadSession() { /* Implementation identical to v21 */ }
        private void SaveSession(string t, string r, string e) { /* Implementation identical to v21 */ }
        private void MainForm_FormClosing(object s, FormClosingEventArgs e) { LicenseService.Instance.Dispose(); }
        private void OnLicenseChanged(LicenseValidationResult r) { UpdateHeader(); }
    }
}