using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Communication;
using PatsKillerPro.J2534;
using PatsKillerPro.Utils;
using PatsKillerPro.Vehicle;

namespace PatsKillerPro
{
    public partial class MainForm : Form
    {
        // Colors
        private readonly Color BG = Color.FromArgb(22, 22, 26);
        private readonly Color SURFACE = Color.FromArgb(32, 32, 38);
        private readonly Color CARD = Color.FromArgb(40, 40, 48);
        private readonly Color BORDER = Color.FromArgb(60, 60, 70);
        private readonly Color TEXT = Color.FromArgb(245, 245, 245);
        private readonly Color TEXT_DIM = Color.FromArgb(180, 180, 185);
        private readonly Color TEXT_MUTED = Color.FromArgb(120, 120, 128);
        private readonly Color ACCENT = Color.FromArgb(59, 130, 246);
        private readonly Color SUCCESS = Color.FromArgb(34, 197, 94);
        private readonly Color WARNING = Color.FromArgb(234, 179, 8);
        private readonly Color DANGER = Color.FromArgb(239, 68, 68);
        private readonly Color BTN_BG = Color.FromArgb(55, 55, 65);

        // State
        private string _userEmail = "";
        private string _authToken = "";
        private int _tokenBalance = 0;
        private J2534DeviceManager? _deviceManager;
        private J2534Device? _device;
        private J2534Channel? _channel;
        private int _activeTab = 0;

        // Controls
        private Panel _header = null!, _tabBar = null!, _content = null!, _logPanel = null!, _loginPanel = null!;
        private Panel _patsTab = null!, _diagTab = null!, _freeTab = null!;
        private Button _btnTab1 = null!, _btnTab2 = null!, _btnTab3 = null!, _btnLogout = null!;
        private Label _lblTokens = null!, _lblUser = null!, _lblStatus = null!, _lblVin = null!, _lblKeys = null!;
        private ComboBox _cmbDevices = null!, _cmbVehicles = null!;
        private TextBox _txtOutcode = null!, _txtIncode = null!, _txtEmail = null!, _txtPassword = null!;
        private RichTextBox _txtLog = null!;

        public MainForm()
        {
            InitializeComponent();
            ApplyDarkTitleBar();
            BuildUI();
            LoadSession();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.Text = "PatsKiller Pro 2026";
            this.ClientSize = new Size(1000, 750);
            this.MinimumSize = new Size(900, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = BG;
            this.ForeColor = TEXT;
            this.Font = new Font("Segoe UI", 9F);
            this.DoubleBuffered = true;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.ResumeLayout(false);
        }

        private void ApplyDarkTitleBar()
        {
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        private void BuildUI()
        {
            // HEADER
            _header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = SURFACE };
            var logo = new Label { Text = "PK", Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.White, BackColor = ACCENT, Size = new Size(36, 36), Location = new Point(15, 17), TextAlign = ContentAlignment.MiddleCenter };
            var title = new Label { Text = "PatsKiller Pro", Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = TEXT, Location = new Point(60, 14), AutoSize = true };
            var subtitle = new Label { Text = "Ford/Lincoln PATS Key Programming", Font = new Font("Segoe UI", 8), ForeColor = TEXT_MUTED, Location = new Point(62, 40), AutoSize = true };
            _lblTokens = new Label { Text = "Tokens: --", Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = SUCCESS, AutoSize = true };
            _lblUser = new Label { Font = new Font("Segoe UI", 8), ForeColor = TEXT_DIM, AutoSize = true };
            _btnLogout = Btn("Logout", 70, 28, BTN_BG); _btnLogout.Font = new Font("Segoe UI", 8); _btnLogout.Click += (s, e) => Logout(); _btnLogout.Visible = false;
            _header.Controls.AddRange(new Control[] { logo, title, subtitle, _lblTokens, _lblUser, _btnLogout });
            _header.Resize += (s, e) => { _btnLogout.Location = new Point(_header.Width - 85, 21); _lblTokens.Location = new Point(_btnLogout.Left - _lblTokens.Width - 15, 18); _lblUser.Location = new Point(_btnLogout.Left - _lblUser.Width - 15, 42); };
            Controls.Add(_header);

            // TAB BAR
            _tabBar = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = SURFACE, Visible = false };
            _btnTab1 = TabBtn("PATS Key Programming", true); _btnTab1.Location = new Point(15, 7); _btnTab1.Click += (s, e) => SwitchTab(0);
            _btnTab2 = TabBtn("Diagnostics", false); _btnTab2.Location = new Point(195, 7); _btnTab2.Click += (s, e) => SwitchTab(1);
            _btnTab3 = TabBtn("Free Functions", false); _btnTab3.Location = new Point(315, 7); _btnTab3.Click += (s, e) => SwitchTab(2);
            _tabBar.Controls.AddRange(new Control[] { _btnTab1, _btnTab2, _btnTab3 });
            Controls.Add(_tabBar);

            // LOG
            _logPanel = new Panel { Dock = DockStyle.Bottom, Height = 100, BackColor = SURFACE, Visible = false };
            var logLbl = new Label { Text = "ACTIVITY LOG", Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = TEXT_DIM, Location = new Point(15, 6), AutoSize = true };
            _txtLog = new RichTextBox { Location = new Point(15, 24), BackColor = BG, ForeColor = TEXT, Font = new Font("Consolas", 9), BorderStyle = BorderStyle.None, ReadOnly = true };
            _logPanel.Controls.AddRange(new Control[] { logLbl, _txtLog });
            _logPanel.Resize += (s, e) => { _txtLog.Size = new Size(_logPanel.Width - 30, _logPanel.Height - 32); };
            Controls.Add(_logPanel);

            // CONTENT
            _content = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false, AutoScroll = true, Padding = new Padding(15) };
            Controls.Add(_content);

            BuildPatsTab();
            BuildDiagTab();
            BuildFreeTab();
            BuildLogin();

            ShowLogin();
        }

        private void BuildPatsTab()
        {
            _patsTab = new Panel { Location = new Point(15, 10), Size = new Size(950, 520), BackColor = BG };
            int y = 0, W = 920;

            // SECTION 1: J2534 Device
            var s1 = Section("J2534 DEVICE CONNECTION", W, 85); s1.Location = new Point(0, y);
            _cmbDevices = new ComboBox { Location = new Point(20, 45), Size = new Size(280, 30), BackColor = SURFACE, ForeColor = TEXT, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbDevices.Items.Add("Select J2534 Device..."); _cmbDevices.SelectedIndex = 0;
            var btnScan = Btn("Scan Devices", 110, 32, BTN_BG); btnScan.Location = new Point(315, 44); btnScan.Click += BtnScan_Click;
            var btnConn = Btn("Connect", 90, 32, SUCCESS); btnConn.Location = new Point(440, 44); btnConn.Click += BtnConnect_Click;
            _lblStatus = new Label { Text = "● Not Connected", Font = new Font("Segoe UI", 9), ForeColor = WARNING, Location = new Point(550, 50), AutoSize = true };
            s1.Controls.AddRange(new Control[] { _cmbDevices, btnScan, btnConn, _lblStatus });
            _patsTab.Controls.Add(s1); y += 95;

            // SECTION 2: Vehicle
            var s2 = Section("VEHICLE INFORMATION", W, 85); s2.Location = new Point(0, y);
            var btnVin = Btn("Read VIN", 90, 32, ACCENT); btnVin.Location = new Point(20, 45); btnVin.Click += BtnReadVin_Click;
            _lblVin = new Label { Text = "VIN: -----------------", Font = new Font("Consolas", 10), ForeColor = TEXT_DIM, Location = new Point(125, 51), AutoSize = true };
            var lblSel = new Label { Text = "Or select:", Font = new Font("Segoe UI", 9), ForeColor = TEXT_DIM, Location = new Point(360, 51), AutoSize = true };
            _cmbVehicles = new ComboBox { Location = new Point(430, 45), Size = new Size(260, 30), BackColor = SURFACE, ForeColor = TEXT, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var v in VehiclePlatforms.GetAllVehicles()) _cmbVehicles.Items.Add(v.DisplayName);
            if (_cmbVehicles.Items.Count > 0) _cmbVehicles.SelectedIndex = 0;
            // Keys badge
            var keysBg = new Panel { Location = new Point(820, 38), Size = new Size(80, 45), BackColor = SURFACE };
            keysBg.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, 79, 44); };
            var keysLbl = new Label { Text = "KEYS", Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = TEXT_MUTED, Location = new Point(0, 4), Size = new Size(80, 14), TextAlign = ContentAlignment.MiddleCenter };
            _lblKeys = new Label { Text = "--", Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = SUCCESS, Location = new Point(0, 18), Size = new Size(80, 24), TextAlign = ContentAlignment.MiddleCenter };
            keysBg.Controls.AddRange(new Control[] { keysLbl, _lblKeys });
            s2.Controls.AddRange(new Control[] { btnVin, _lblVin, lblSel, _cmbVehicles, keysBg });
            _patsTab.Controls.Add(s2); y += 95;

            // SECTION 3: Codes
            var s3 = Section("PATS SECURITY CODES", W, 85); s3.Location = new Point(0, y);
            var lblOut = new Label { Text = "OUTCODE:", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = TEXT, Location = new Point(20, 51), AutoSize = true };
            _txtOutcode = new TextBox { Location = new Point(100, 45), Size = new Size(120, 30), BackColor = SURFACE, ForeColor = TEXT, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11), TextAlign = HorizontalAlignment.Center, ReadOnly = true };
            var btnCopy = Btn("Copy", 60, 32, BTN_BG); btnCopy.Location = new Point(235, 44); btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) { Clipboard.SetText(_txtOutcode.Text); Log("info", "Copied"); } };
            var btnGet = Btn("Get Incode Online", 140, 32, WARNING); btnGet.ForeColor = Color.Black; btnGet.Location = new Point(310, 44); btnGet.Click += (s, e) => OpenUrl("https://patskiller.com/calculator");
            var lblIn = new Label { Text = "INCODE:", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = TEXT, Location = new Point(480, 51), AutoSize = true };
            _txtIncode = new TextBox { Location = new Point(550, 45), Size = new Size(120, 30), BackColor = SURFACE, ForeColor = TEXT, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11), TextAlign = HorizontalAlignment.Center };
            s3.Controls.AddRange(new Control[] { lblOut, _txtOutcode, btnCopy, btnGet, lblIn, _txtIncode });
            _patsTab.Controls.Add(s3); y += 95;

            // SECTION 4: Operations
            var s4 = Section("KEY PROGRAMMING OPERATIONS", W, 105); s4.Location = new Point(0, y);
            int bx = 20;
            var btnProg = Btn("Program Key", 115, 38, SUCCESS); btnProg.Location = new Point(bx, 45); btnProg.Click += BtnProgram_Click; bx += 130;
            var btnErase = Btn("Erase All Keys", 115, 38, DANGER); btnErase.Location = new Point(bx, 45); btnErase.Click += BtnErase_Click; bx += 130;
            var btnParam = Btn("Parameter Reset", 125, 38, BTN_BG); btnParam.Location = new Point(bx, 45); btnParam.Click += BtnParam_Click; bx += 140;
            var btnEscl = Btn("Initialize ESCL", 115, 38, BTN_BG); btnEscl.Location = new Point(bx, 45); btnEscl.Click += BtnEscl_Click; bx += 130;
            var btnDisable = Btn("Disable BCM", 105, 38, BTN_BG); btnDisable.Location = new Point(bx, 45); btnDisable.Click += BtnDisable_Click;
            var tip = new Label { Text = "TIP: Program Key = 1 token/session (unlimited keys). Insert key → Program → Remove → Repeat.", Font = new Font("Segoe UI", 8), ForeColor = TEXT_MUTED, Location = new Point(20, 88), AutoSize = true };
            s4.Controls.AddRange(new Control[] { btnProg, btnErase, btnParam, btnEscl, btnDisable, tip });
            _patsTab.Controls.Add(s4);

            _content.Controls.Add(_patsTab);
        }

        private void BuildDiagTab()
        {
            _diagTab = new Panel { Location = new Point(15, 10), Size = new Size(950, 420), BackColor = BG, Visible = false };
            int y = 0, W = 920;

            var s1 = Section("DTC CLEAR OPERATIONS (1 TOKEN EACH)", W, 85); s1.Location = new Point(0, y);
            var b1 = Btn("Clear P160A", 110, 34, ACCENT); b1.Location = new Point(20, 45); b1.Click += BtnP160A_Click;
            var b2 = Btn("Clear B10A2", 110, 34, ACCENT); b2.Location = new Point(145, 45); b2.Click += BtnB10A2_Click;
            var b3 = Btn("Clear Crush", 100, 34, ACCENT); b3.Location = new Point(270, 45); b3.Click += BtnCrush_Click;
            var b4 = Btn("Unlock Gateway", 120, 34, ACCENT); b4.Location = new Point(385, 45); b4.Click += BtnGateway_Click;
            s1.Controls.AddRange(new Control[] { b1, b2, b3, b4 });
            _diagTab.Controls.Add(s1); y += 95;

            var s2 = Section("KEYPAD CODE OPERATIONS", W, 85); s2.Location = new Point(0, y);
            var k1 = Btn("Read Keypad Code", 140, 34, BTN_BG); k1.Location = new Point(20, 45); k1.Click += BtnKeypad_Click;
            var k2 = Btn("Write Keypad Code", 145, 34, BTN_BG); k2.Location = new Point(175, 45); k2.Click += BtnKeypad_Click;
            var kn = new Label { Text = "For vehicles with door keypad entry", Font = new Font("Segoe UI", 8), ForeColor = TEXT_MUTED, Location = new Point(340, 52), AutoSize = true };
            s2.Controls.AddRange(new Control[] { k1, k2, kn });
            _diagTab.Controls.Add(s2); y += 95;

            var s3 = Section("BCM ADVANCED OPERATIONS", W, 85); s3.Location = new Point(0, y);
            var bcm = Btn("BCM Factory Reset", 145, 34, DANGER); bcm.Location = new Point(20, 45); bcm.Click += BtnBcm_Click;
            var warn = new Label { Text = "⚠ WARNING: Requires As-Built reprogramming after reset!", Font = new Font("Segoe UI", 8), ForeColor = DANGER, Location = new Point(180, 52), AutoSize = true };
            s3.Controls.AddRange(new Control[] { bcm, warn });
            _diagTab.Controls.Add(s3); y += 95;

            var s4 = Section("MODULE INFORMATION", W, 85); s4.Location = new Point(0, y);
            var mod = Btn("Read All Module Info", 155, 34, BTN_BG); mod.Location = new Point(20, 45); mod.Click += BtnModInfo_Click;
            s4.Controls.Add(mod);
            _diagTab.Controls.Add(s4);

            _content.Controls.Add(_diagTab);
        }

        private void BuildFreeTab()
        {
            _freeTab = new Panel { Location = new Point(15, 10), Size = new Size(950, 380), BackColor = BG, Visible = false };
            int y = 0, W = 920;

            var banner = new Panel { Location = new Point(0, y), Size = new Size(W, 40), BackColor = Color.FromArgb(20, 34, 197, 94) };
            banner.Paint += (s, e) => { using var p = new Pen(SUCCESS, 2); e.Graphics.DrawRectangle(p, 1, 1, W - 3, 37); };
            var bl = new Label { Text = "✓ All operations on this tab are FREE - No token cost!", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = SUCCESS, Location = new Point(20, 10), AutoSize = true };
            banner.Controls.Add(bl);
            _freeTab.Controls.Add(banner); y += 55;

            var s1 = Section("BASIC VEHICLE OPERATIONS", W, 85); s1.Location = new Point(0, y);
            var f1 = Btn("Clear All DTCs", 115, 34, BTN_BG); f1.Location = new Point(20, 45); f1.Click += BtnDtc_Click;
            var f2 = Btn("Clear KAM", 95, 34, BTN_BG); f2.Location = new Point(150, 45); f2.Click += BtnKam_Click;
            var f3 = Btn("Vehicle Reset", 105, 34, BTN_BG); f3.Location = new Point(260, 45); f3.Click += BtnReset_Click;
            s1.Controls.AddRange(new Control[] { f1, f2, f3 });
            _freeTab.Controls.Add(s1); y += 95;

            var s2 = Section("READ OPERATIONS", W, 85); s2.Location = new Point(0, y);
            var r1 = Btn("Read Keys Count", 130, 34, BTN_BG); r1.Location = new Point(20, 45); r1.Click += BtnReadKeys_Click;
            var r2 = Btn("Read Module Info", 135, 34, BTN_BG); r2.Location = new Point(165, 45); r2.Click += BtnModInfo_Click;
            s2.Controls.AddRange(new Control[] { r1, r2 });
            _freeTab.Controls.Add(s2); y += 95;

            var s3 = Section("RESOURCES & SUPPORT", W, 85); s3.Location = new Point(0, y);
            var u1 = Btn("User Guide", 100, 34, ACCENT); u1.Location = new Point(20, 45); u1.Click += (s, e) => OpenUrl("https://patskiller.com/faqs");
            var u2 = Btn("Buy Tokens", 100, 34, SUCCESS); u2.Location = new Point(135, 45); u2.Click += (s, e) => OpenUrl("https://patskiller.com/buy-tokens");
            var u3 = Btn("Contact Support", 120, 34, BTN_BG); u3.Location = new Point(250, 45); u3.Click += (s, e) => OpenUrl("https://patskiller.com/contact");
            s3.Controls.AddRange(new Control[] { u1, u2, u3 });
            _freeTab.Controls.Add(s3);

            _content.Controls.Add(_freeTab);
        }

        private void BuildLogin()
        {
            _loginPanel = new Panel { Dock = DockStyle.Fill, BackColor = BG };
            var card = new Panel { Size = new Size(380, 440), BackColor = CARD };
            card.Paint += (s, e) => { using var p = new Pen(BORDER, 2); e.Graphics.DrawRectangle(p, 1, 1, 377, 437); };

            int cy = 30;
            var lw = new Label { Text = "Welcome to PatsKiller Pro", Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = TEXT, Size = new Size(360, 30), Location = new Point(10, cy), TextAlign = ContentAlignment.MiddleCenter }; cy += 35;
            var ls = new Label { Text = "Sign in to access your tokens", Font = new Font("Segoe UI", 9), ForeColor = TEXT_MUTED, Size = new Size(360, 20), Location = new Point(10, cy), TextAlign = ContentAlignment.MiddleCenter }; cy += 40;
            var btnG = Btn("Continue with Google", 300, 44, Color.White); btnG.ForeColor = Color.FromArgb(50, 50, 50); btnG.Font = new Font("Segoe UI", 10, FontStyle.Bold); btnG.Location = new Point(40, cy); btnG.Click += BtnGoogle_Click; cy += 60;
            var lo = new Label { Text = "───── or sign in with email ─────", Font = new Font("Segoe UI", 8), ForeColor = TEXT_MUTED, Size = new Size(300, 18), Location = new Point(40, cy), TextAlign = ContentAlignment.MiddleCenter }; cy += 30;
            var le = new Label { Text = "Email", Font = new Font("Segoe UI", 8), ForeColor = TEXT_DIM, Location = new Point(40, cy), AutoSize = true }; cy += 20;
            _txtEmail = new TextBox { Location = new Point(40, cy), Size = new Size(300, 34), BackColor = SURFACE, ForeColor = TEXT, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) }; cy += 45;
            var lp = new Label { Text = "Password", Font = new Font("Segoe UI", 8), ForeColor = TEXT_DIM, Location = new Point(40, cy), AutoSize = true }; cy += 20;
            _txtPassword = new TextBox { Location = new Point(40, cy), Size = new Size(300, 34), BackColor = SURFACE, ForeColor = TEXT, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10), UseSystemPasswordChar = true }; _txtPassword.KeyPress += (s, e) => { if (e.KeyChar == 13) DoLogin(); }; cy += 50;
            var btnL = Btn("Sign In", 300, 42, ACCENT); btnL.Font = new Font("Segoe UI", 11, FontStyle.Bold); btnL.Location = new Point(40, cy); btnL.Click += (s, e) => DoLogin();

            card.Controls.AddRange(new Control[] { lw, ls, btnG, lo, le, _txtEmail, lp, _txtPassword, btnL });
            _loginPanel.Controls.Add(card);
            _loginPanel.Resize += (s, e) => { card.Location = new Point((_loginPanel.Width - 380) / 2, (_loginPanel.Height - 440) / 2 - 20); };
            Controls.Add(_loginPanel);
        }

        // UI Helpers
        private Panel Section(string title, int w, int h)
        {
            var p = new Panel { Size = new Size(w, h), BackColor = CARD };
            p.Paint += (s, e) => {
                using var pen = new Pen(BORDER);
                e.Graphics.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                using var f = new Font("Segoe UI", 9F, FontStyle.Bold);
                using var b = new SolidBrush(TEXT_DIM);
                e.Graphics.DrawString(title, f, b, 18, 10);
                e.Graphics.DrawLine(pen, 12, 32, w - 12, 32);
            };
            return p;
        }

        private Button Btn(string text, int w, int h, Color bg)
        {
            var b = new Button { Text = text, Size = new Size(w, h), FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = TEXT, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = BORDER;
            return b;
        }

        private Button TabBtn(string text, bool active)
        {
            var b = new Button { Text = text, Size = new Size(text.Length * 9 + 30, 32), FlatStyle = FlatStyle.Flat, BackColor = active ? ACCENT : Color.FromArgb(48, 48, 56), ForeColor = TEXT, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void Log(string t, string m) { if (_txtLog == null) return; if (_txtLog.InvokeRequired) { _txtLog.Invoke(() => Log(t, m)); return; } var c = t == "success" ? SUCCESS : t == "error" ? DANGER : t == "warning" ? WARNING : TEXT_DIM; _txtLog.SelectionColor = TEXT_MUTED; _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] "); _txtLog.SelectionColor = c; _txtLog.AppendText($"{m}\n"); _txtLog.ScrollToCaret(); }
        private void OpenUrl(string u) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = u, UseShellExecute = true }); } catch { } }
        private void ShowError(string t, string m, Exception? ex = null) { MessageBox.Show(ex != null ? $"{m}\n\n{ex.Message}" : m, t, MessageBoxButtons.OK, MessageBoxIcon.Error); Log("error", m); }
        private bool Confirm(int cost, string op) { if (cost == 0) return true; if (_tokenBalance < cost) { MessageBox.Show($"Need {cost} tokens", "Insufficient", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; } return MessageBox.Show($"{op}\nCost: {cost} token(s)\n\nProceed?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes; }

        // Navigation
        private void ShowLogin() { _loginPanel.Visible = true; _tabBar.Visible = _content.Visible = _logPanel.Visible = _btnLogout.Visible = false; }
        private void ShowMain() { _loginPanel.Visible = false; _tabBar.Visible = _content.Visible = _logPanel.Visible = _btnLogout.Visible = true; SwitchTab(0); }
        private void SwitchTab(int i) { _activeTab = i; _btnTab1.BackColor = i == 0 ? ACCENT : Color.FromArgb(48, 48, 56); _btnTab2.BackColor = i == 1 ? ACCENT : Color.FromArgb(48, 48, 56); _btnTab3.BackColor = i == 2 ? ACCENT : Color.FromArgb(48, 48, 56); _patsTab.Visible = i == 0; _diagTab.Visible = i == 1; _freeTab.Visible = i == 2; }

        // Auth
        private void LoadSession() { var e = Settings.GetString("email", ""); var t = Settings.GetString("auth_token", ""); if (!string.IsNullOrEmpty(e) && !string.IsNullOrEmpty(t)) { _userEmail = e; _authToken = t; _tokenBalance = 10; _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail; ShowMain(); Log("info", $"Logged in as {_userEmail}"); } }
        private async void DoLogin() { var e = _txtEmail.Text.Trim(); var p = _txtPassword.Text; if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p)) { MessageBox.Show("Enter email and password"); return; } await Task.Delay(200); _userEmail = e; _authToken = "t_" + DateTime.Now.Ticks; _tokenBalance = 10; Settings.SetString("email", e); Settings.SetString("auth_token", _authToken); Settings.Save(); _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail; ShowMain(); Log("success", $"Logged in as {e}"); }
        private void BtnGoogle_Click(object? s, EventArgs e) { try { using var f = new GoogleLoginForm(); if (f.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(f.AuthToken)) { _authToken = f.AuthToken; _userEmail = f.UserEmail ?? "Google User"; _tokenBalance = f.TokenCount; Settings.SetString("auth_token", _authToken); Settings.SetString("email", _userEmail); Settings.Save(); _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail; ShowMain(); Log("success", $"Logged in as {_userEmail}"); } } catch (Exception ex) { ShowError("Login Failed", ex.Message, ex); } }
        private void Logout() { _userEmail = _authToken = ""; _tokenBalance = 0; Settings.Remove("auth_token"); Settings.Save(); _txtPassword.Text = ""; _lblTokens.Text = "Tokens: --"; _lblUser.Text = ""; ShowLogin(); }

        // Device
        private void BtnScan_Click(object? s, EventArgs e) { try { _cmbDevices.Items.Clear(); _deviceManager?.Dispose(); _deviceManager = new J2534DeviceManager(); _deviceManager.ScanForDevices(); var n = _deviceManager.GetDeviceNames(); if (n.Count == 0) _cmbDevices.Items.Add("No devices"); else foreach (var x in n) _cmbDevices.Items.Add(x); _cmbDevices.SelectedIndex = 0; Log(n.Count > 0 ? "success" : "warning", n.Count > 0 ? $"Found {n.Count}" : "No devices"); } catch (Exception ex) { ShowError("Scan", "Failed", ex); } }
        private void BtnConnect_Click(object? s, EventArgs e) { if (_cmbDevices.SelectedItem == null || _deviceManager == null) return; try { var nm = _cmbDevices.SelectedItem.ToString()!; if (nm.Contains("No") || nm.Contains("Select")) { MessageBox.Show("Select device"); return; } _device = _deviceManager.ConnectToDevice(nm); _channel = _device.OpenChannel(Protocol.ISO15765, BaudRates.HS_CAN_500K, ConnectFlags.NONE); _lblStatus.Text = "● Connected"; _lblStatus.ForeColor = SUCCESS; Log("success", $"Connected to {nm}"); } catch (Exception ex) { ShowError("Connect", "Failed", ex); } }
        private async void BtnReadVin_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var uds = new UdsService(_channel); var vin = await Task.Run(() => uds.ReadVIN()); if (!string.IsNullOrEmpty(vin)) { _lblVin.Text = $"VIN: {vin}"; _lblVin.ForeColor = SUCCESS; _txtOutcode.Text = await Task.Run(() => uds.ReadOutcode()); Log("success", $"VIN: {vin}"); } else { _lblVin.Text = "VIN: Could not read"; _lblVin.ForeColor = DANGER; } } catch (Exception ex) { ShowError("Read", "Failed", ex); } }

        // PATS Ops
        private async void BtnProgram_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } var ic = _txtIncode.Text.Trim(); if (string.IsNullOrEmpty(ic)) { MessageBox.Show("Enter incode"); return; } try { var uds = new UdsService(_channel); var pats = new PatsOperations(uds); if (await Task.Run(() => pats.ProgramKeys(ic))) { MessageBox.Show("Key programmed!\n\nRemove key, insert next, click Program."); Log("success", "Programmed"); } } catch (Exception ex) { ShowError("Program", "Failed", ex); } }
        private async void BtnErase_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_KEY_ERASE, "Erase All Keys")) return; if (MessageBox.Show("ERASE ALL KEYS?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; var ic = _txtIncode.Text.Trim(); if (string.IsNullOrEmpty(ic)) { MessageBox.Show("Enter incode"); return; } try { var uds = new UdsService(_channel); var pats = new PatsOperations(uds); await Task.Run(() => pats.EraseAllKeys(ic)); MessageBox.Show("Erased!"); Log("success", "Erased"); } catch (Exception ex) { ShowError("Erase", "Failed", ex); } }
        private async void BtnParam_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var uds = new UdsService(_channel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ParameterReset()); MessageBox.Show("Done! Turn ignition OFF 15s."); Log("success", "Reset"); } catch (Exception ex) { ShowError("Reset", "Failed", ex); } }
        private async void BtnEscl_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_ESCL_INIT, "ESCL")) return; try { var uds = new UdsService(_channel); var pats = new PatsOperations(uds); await Task.Run(() => pats.InitializeESCL()); MessageBox.Show("ESCL initialized!"); Log("success", "ESCL done"); } catch (Exception ex) { ShowError("ESCL", "Failed", ex); } }
        private async void BtnDisable_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var uds = new UdsService(_channel); var pats = new PatsOperations(uds); await Task.Run(() => pats.DisableBcmSecurity()); MessageBox.Show("BCM disabled"); Log("success", "BCM disabled"); } catch (Exception ex) { ShowError("BCM", "Failed", ex); } }

        // Diag
        private async void BtnP160A_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_CLEAR_P160A, "P160A")) return; try { var pats = new PatsOperations(new UdsService(_channel)); await Task.Run(() => pats.ClearP160A()); MessageBox.Show("Cleared!"); Log("success", "P160A"); } catch (Exception ex) { ShowError("P160A", "Failed", ex); } }
        private async void BtnB10A2_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_CLEAR_B10A2, "B10A2")) return; try { var pats = new PatsOperations(new UdsService(_channel)); await Task.Run(() => pats.ClearB10A2()); MessageBox.Show("Cleared!"); Log("success", "B10A2"); } catch (Exception ex) { ShowError("B10A2", "Failed", ex); } }
        private async void BtnCrush_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_CLEAR_CRUSH, "Crush")) return; try { var pats = new PatsOperations(new UdsService(_channel)); await Task.Run(() => pats.ClearCrushEvent()); MessageBox.Show("Cleared!"); Log("success", "Crush"); } catch (Exception ex) { ShowError("Crush", "Failed", ex); } }
        private async void BtnGateway_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var pats = new PatsOperations(new UdsService(_channel)); if (!await Task.Run(() => pats.DetectGateway())) { MessageBox.Show("No gateway"); return; } if (!Confirm(PatsOperations.TOKEN_COST_GATEWAY_UNLOCK, "Gateway")) return; var ic = _txtIncode.Text.Trim(); if (string.IsNullOrEmpty(ic)) { MessageBox.Show("Enter incode"); return; } await Task.Run(() => pats.UnlockGateway(ic)); MessageBox.Show("Unlocked!"); Log("success", "Gateway"); } catch (Exception ex) { ShowError("Gateway", "Failed", ex); } }
        private async void BtnKeypad_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } var r = MessageBox.Show("YES=Read, NO=Write", "Keypad", MessageBoxButtons.YesNoCancel); if (r == DialogResult.Cancel) return; if (r == DialogResult.Yes) { if (!Confirm(PatsOperations.TOKEN_COST_KEYPAD_READ, "Read Keypad")) return; try { var c = await Task.Run(() => new PatsOperations(new UdsService(_channel)).ReadKeypadCode()); MessageBox.Show($"Code: {c}"); Log("success", $"Keypad: {c}"); } catch (Exception ex) { ShowError("Keypad", "Failed", ex); } } else { var nc = Microsoft.VisualBasic.Interaction.InputBox("Enter 5-digit code:", "Keypad", ""); if (nc.Length != 5) return; if (!Confirm(PatsOperations.TOKEN_COST_KEYPAD_WRITE, "Write Keypad")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).WriteKeypadCode(nc)); MessageBox.Show($"Set: {nc}"); Log("success", "Keypad set"); } catch (Exception ex) { ShowError("Keypad", "Failed", ex); } } }
        private async void BtnBcm_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (MessageBox.Show("This resets ALL BCM!", "WARNING", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; if (!Confirm(PatsOperations.TOKEN_COST_BCM_FACTORY, "BCM Reset")) return; var i1 = Microsoft.VisualBasic.Interaction.InputBox("Incode 1:", "BCM", _txtIncode.Text); if (string.IsNullOrEmpty(i1)) return; var i2 = Microsoft.VisualBasic.Interaction.InputBox("Incode 2:", "BCM", ""); if (string.IsNullOrEmpty(i2)) return; var i3 = Microsoft.VisualBasic.Interaction.InputBox("Incode 3 (opt):", "BCM", ""); var codes = string.IsNullOrEmpty(i3) ? new[] { i1, i2 } : new[] { i1, i2, i3 }; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).BcmFactoryDefaults(codes)); MessageBox.Show("BCM reset!"); Log("success", "BCM reset"); } catch (Exception ex) { ShowError("BCM", "Failed", ex); } }
        private async void BtnModInfo_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var info = await Task.Run(() => new UdsService(_channel).ReadAllModuleInfo()); MessageBox.Show(info, "Module Info"); Log("success", "Module info"); } catch (Exception ex) { ShowError("Module", "Failed", ex); } }

        // Free
        private async void BtnDtc_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new UdsService(_channel).ClearDTCs()); MessageBox.Show("DTCs cleared!"); Log("success", "DTCs"); } catch (Exception ex) { ShowError("DTC", "Failed", ex); } }
        private async void BtnKam_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ClearKAM()); MessageBox.Show("KAM cleared!"); Log("success", "KAM"); } catch (Exception ex) { ShowError("KAM", "Failed", ex); } }
        private async void BtnReset_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).VehicleReset()); MessageBox.Show("Reset!"); Log("success", "Reset"); } catch (Exception ex) { ShowError("Reset", "Failed", ex); } }
        private async void BtnReadKeys_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var c = await Task.Run(() => new UdsService(_channel).ReadKeysCount()); _lblKeys.Text = c.ToString(); MessageBox.Show($"Keys: {c}"); Log("success", $"Keys: {c}"); } catch (Exception ex) { ShowError("Keys", "Failed", ex); } }

        protected override void OnFormClosing(FormClosingEventArgs e) { try { _channel?.Dispose(); _device?.Dispose(); _deviceManager?.Dispose(); } catch { } base.OnFormClosing(e); }
    }
}