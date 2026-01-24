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
        // Theme Colors (matching mockup)
        private readonly Color BG = Color.FromArgb(26, 26, 30);
        private readonly Color SURFACE = Color.FromArgb(35, 35, 40);
        private readonly Color CARD = Color.FromArgb(42, 42, 48);
        private readonly Color BORDER = Color.FromArgb(58, 58, 66);
        private readonly Color TEXT = Color.FromArgb(240, 240, 240);
        private readonly Color TEXT_DIM = Color.FromArgb(160, 160, 165);
        private readonly Color TEXT_MUTED = Color.FromArgb(112, 112, 117);
        private readonly Color ACCENT = Color.FromArgb(59, 130, 246);
        private readonly Color SUCCESS = Color.FromArgb(34, 197, 94);
        private readonly Color WARNING = Color.FromArgb(234, 179, 8);
        private readonly Color DANGER = Color.FromArgb(239, 68, 68);
        private readonly Color BTN_BG = Color.FromArgb(54, 54, 64);

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
            this.ClientSize = new Size(1300, 900);
            this.MinimumSize = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = BG;
            this.ForeColor = TEXT;
            this.Font = new Font("Segoe UI", 9.5F);
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

        // Calculate button width based on text
        private int CalcWidth(string text, int extraPadding = 50)
        {
            using var g = CreateGraphics();
            var size = TextRenderer.MeasureText(g, text, new Font("Segoe UI", 9.5F, FontStyle.Bold));
            return size.Width + extraPadding;
        }

        private void BuildUI()
        {
            // === HEADER (80px) ===
            _header = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = SURFACE };
            _header.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawLine(p, 0, 79, Width, 79); };

            var logo = new Label
            {
                Text = "PK",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = ACCENT,
                Size = new Size(48, 48),
                Location = new Point(24, 16),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var title = new Label
            {
                Text = "PatsKiller Pro",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = TEXT,
                Location = new Point(84, 14),
                AutoSize = true
            };

            var subtitle = new Label
            {
                Text = "Ford & Lincoln PATS Key Programming",
                Font = new Font("Segoe UI", 9),
                ForeColor = TEXT_MUTED,
                Location = new Point(86, 46),
                AutoSize = true
            };

            _lblTokens = new Label
            {
                Text = "Tokens: --",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = SUCCESS,
                AutoSize = true
            };

            _lblUser = new Label
            {
                Font = new Font("Segoe UI", 9),
                ForeColor = TEXT_DIM,
                AutoSize = true
            };

            _btnLogout = MakeButton("Logout", 80, 32, BTN_BG);
            _btnLogout.Click += (s, e) => Logout();
            _btnLogout.Visible = false;

            _header.Controls.AddRange(new Control[] { logo, title, subtitle, _lblTokens, _lblUser, _btnLogout });
            _header.Resize += (s, e) => LayoutHeader();
            Controls.Add(_header);

            // === TAB BAR (50px) ===
            _tabBar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = SURFACE, Visible = false };
            _tabBar.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawLine(p, 0, 49, Width, 49); };

            _btnTab1 = MakeTabButton("PATS Key Programming", true);
            _btnTab1.Location = new Point(24, 10);
            _btnTab1.Click += (s, e) => SwitchTab(0);

            _btnTab2 = MakeTabButton("Diagnostics", false);
            _btnTab2.Location = new Point(_btnTab1.Right + 8, 10);
            _btnTab2.Click += (s, e) => SwitchTab(1);

            _btnTab3 = MakeTabButton("Free Functions", false);
            _btnTab3.Location = new Point(_btnTab2.Right + 8, 10);
            _btnTab3.Click += (s, e) => SwitchTab(2);

            _tabBar.Controls.AddRange(new Control[] { _btnTab1, _btnTab2, _btnTab3 });
            Controls.Add(_tabBar);

            // === LOG PANEL (120px) ===
            _logPanel = new Panel { Dock = DockStyle.Bottom, Height = 120, BackColor = SURFACE, Visible = false };
            _logPanel.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawLine(p, 0, 0, Width, 0); };

            var logTitle = new Label
            {
                Text = "ACTIVITY LOG",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = TEXT_DIM,
                Location = new Point(24, 10),
                AutoSize = true
            };

            _txtLog = new RichTextBox
            {
                Location = new Point(24, 32),
                BackColor = BG,
                ForeColor = TEXT,
                Font = new Font("Consolas", 10),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            _logPanel.Controls.AddRange(new Control[] { logTitle, _txtLog });
            _logPanel.Resize += (s, e) => { _txtLog.Size = new Size(_logPanel.Width - 48, _logPanel.Height - 44); };
            Controls.Add(_logPanel);

            // === CONTENT PANEL ===
            _content = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false, AutoScroll = true };
            Controls.Add(_content);

            // Build tab content
            BuildPatsTab();
            BuildDiagTab();
            BuildFreeTab();
            BuildLoginPanel();

            ShowLogin();
        }

        private void LayoutHeader()
        {
            int r = _header.Width - 24;
            _btnLogout.Location = new Point(r - _btnLogout.Width, 24);
            _lblTokens.Location = new Point(_btnLogout.Left - _lblTokens.Width - 20, 20);
            _lblUser.Location = new Point(_btnLogout.Left - _lblUser.Width - 20, 48);
        }

        private void BuildPatsTab()
        {
            _patsTab = new Panel { Location = new Point(24, 16), BackColor = BG };
            int y = 0;
            int W = 1220;

            // === SECTION 1: J2534 Device Connection ===
            var sec1 = MakeSection("J2534 DEVICE CONNECTION", W, 95);
            sec1.Location = new Point(0, y);

            _cmbDevices = MakeComboBox(380);
            _cmbDevices.Location = new Point(24, 48);
            _cmbDevices.Items.Add("Select J2534 Device...");
            _cmbDevices.SelectedIndex = 0;

            var btnScan = MakeButton("Scan Devices", 130, 36, BTN_BG);
            btnScan.Location = new Point(420, 47);
            btnScan.Click += BtnScan_Click;

            var btnConnect = MakeButton("Connect", 100, 36, SUCCESS);
            btnConnect.Location = new Point(565, 47);
            btnConnect.Click += BtnConnect_Click;

            _lblStatus = new Label
            {
                Text = "Status: Not Connected",
                Font = new Font("Segoe UI", 10),
                ForeColor = WARNING,
                Location = new Point(690, 53),
                AutoSize = true
            };

            sec1.Controls.AddRange(new Control[] { _cmbDevices, btnScan, btnConnect, _lblStatus });
            _patsTab.Controls.Add(sec1);
            y += 110;

            // === SECTION 2: Vehicle Information ===
            var sec2 = MakeSection("VEHICLE INFORMATION", W, 110);
            sec2.Location = new Point(0, y);

            var btnReadVin = MakeButton("Read VIN", 110, 36, ACCENT);
            btnReadVin.Location = new Point(24, 48);
            btnReadVin.Click += BtnReadVin_Click;

            _lblVin = new Label
            {
                Text = "VIN: —————————————————",
                Font = new Font("Consolas", 11),
                ForeColor = TEXT_DIM,
                Location = new Point(150, 54),
                AutoSize = true
            };

            var lblSelect = new Label
            {
                Text = "Or select vehicle:",
                Font = new Font("Segoe UI", 9),
                ForeColor = TEXT_DIM,
                Location = new Point(450, 54),
                AutoSize = true
            };

            _cmbVehicles = MakeComboBox(320);
            _cmbVehicles.Location = new Point(565, 48);
            foreach (var v in VehiclePlatforms.GetAllVehicles()) _cmbVehicles.Items.Add(v.DisplayName);
            if (_cmbVehicles.Items.Count > 0) _cmbVehicles.SelectedIndex = 0;

            // Keys badge
            var keysBg = new Panel
            {
                Location = new Point(W - 160, 42),
                Size = new Size(130, 55),
                BackColor = SURFACE
            };
            keysBg.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, 129, 54); };

            var keysLbl = new Label
            {
                Text = "KEYS PROGRAMMED",
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                ForeColor = TEXT_MUTED,
                Location = new Point(0, 6),
                Size = new Size(130, 14),
                TextAlign = ContentAlignment.MiddleCenter
            };

            _lblKeys = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = SUCCESS,
                Location = new Point(0, 22),
                Size = new Size(130, 30),
                TextAlign = ContentAlignment.MiddleCenter
            };

            keysBg.Controls.AddRange(new Control[] { keysLbl, _lblKeys });
            sec2.Controls.AddRange(new Control[] { btnReadVin, _lblVin, lblSelect, _cmbVehicles, keysBg });
            _patsTab.Controls.Add(sec2);
            y += 125;

            // === SECTION 3: PATS Security Codes ===
            var sec3 = MakeSection("PATS SECURITY CODES", W, 95);
            sec3.Location = new Point(0, y);

            var lblOut = new Label
            {
                Text = "OUTCODE:",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = TEXT,
                Location = new Point(24, 55),
                AutoSize = true
            };

            _txtOutcode = MakeTextBox(150);
            _txtOutcode.Location = new Point(110, 48);
            _txtOutcode.ReadOnly = true;

            var btnCopy = MakeButton("Copy", 70, 36, BTN_BG);
            btnCopy.Location = new Point(275, 47);
            btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) { Clipboard.SetText(_txtOutcode.Text); Log("info", "Copied to clipboard"); } };

            var btnGetIncode = MakeButton("Get Incode Online", 160, 36, WARNING);
            btnGetIncode.ForeColor = Color.Black;
            btnGetIncode.Location = new Point(365, 47);
            btnGetIncode.Click += (s, e) => OpenUrl("https://patskiller.com/calculator");

            var lblIn = new Label
            {
                Text = "INCODE:",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = TEXT,
                Location = new Point(560, 55),
                AutoSize = true
            };

            _txtIncode = MakeTextBox(150);
            _txtIncode.Location = new Point(640, 48);

            sec3.Controls.AddRange(new Control[] { lblOut, _txtOutcode, btnCopy, btnGetIncode, lblIn, _txtIncode });
            _patsTab.Controls.Add(sec3);
            y += 110;

            // === SECTION 4: Key Programming Operations ===
            var sec4 = MakeSection("KEY PROGRAMMING OPERATIONS", W, 130);
            sec4.Location = new Point(0, y);

            int bx = 24;
            var btnProgram = MakeButton("Program Key", 140, 42, SUCCESS);
            btnProgram.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            btnProgram.Location = new Point(bx, 48);
            btnProgram.Click += BtnProgram_Click;
            bx += 155;

            var btnErase = MakeButton("Erase All Keys", 140, 42, DANGER);
            btnErase.Location = new Point(bx, 48);
            btnErase.Click += BtnErase_Click;
            bx += 155;

            var btnParam = MakeButton("Parameter Reset", 150, 42, BTN_BG);
            btnParam.Location = new Point(bx, 48);
            btnParam.Click += BtnParam_Click;
            bx += 165;

            var btnEscl = MakeButton("Initialize ESCL", 140, 42, BTN_BG);
            btnEscl.Location = new Point(bx, 48);
            btnEscl.Click += BtnEscl_Click;
            bx += 155;

            var btnDisable = MakeButton("Disable BCM Security", 180, 42, BTN_BG);
            btnDisable.Location = new Point(bx, 48);
            btnDisable.Click += BtnDisable_Click;

            var tip = new Label
            {
                Text = "💡 Tip: Program Key costs 1 token per session (unlimited keys). Insert key, click Program, repeat for additional keys.",
                Font = new Font("Segoe UI", 9),
                ForeColor = TEXT_MUTED,
                Location = new Point(24, 100),
                AutoSize = true
            };

            sec4.Controls.AddRange(new Control[] { btnProgram, btnErase, btnParam, btnEscl, btnDisable, tip });
            _patsTab.Controls.Add(sec4);

            _patsTab.Size = new Size(W + 20, y + 145);
            _content.Controls.Add(_patsTab);
        }

        private void BuildDiagTab()
        {
            _diagTab = new Panel { Location = new Point(24, 16), BackColor = BG, Visible = false };
            int y = 0;
            int W = 1220;

            // DTC Operations
            var sec1 = MakeSection("DTC CLEAR OPERATIONS (1 TOKEN EACH)", W, 95);
            sec1.Location = new Point(0, y);

            int bx = 24;
            var b1 = MakeButton("Clear P160A", 130, 38, ACCENT); b1.Location = new Point(bx, 48); b1.Click += BtnP160A_Click; bx += 145;
            var b2 = MakeButton("Clear B10A2", 130, 38, ACCENT); b2.Location = new Point(bx, 48); b2.Click += BtnB10A2_Click; bx += 145;
            var b3 = MakeButton("Clear Crush Event", 150, 38, ACCENT); b3.Location = new Point(bx, 48); b3.Click += BtnCrush_Click; bx += 165;
            var b4 = MakeButton("Unlock Gateway", 140, 38, ACCENT); b4.Location = new Point(bx, 48); b4.Click += BtnGateway_Click;

            sec1.Controls.AddRange(new Control[] { b1, b2, b3, b4 });
            _diagTab.Controls.Add(sec1);
            y += 110;

            // Keypad Operations
            var sec2 = MakeSection("KEYPAD CODE OPERATIONS", W, 95);
            sec2.Location = new Point(0, y);

            var k1 = MakeButton("Read Keypad Code", 160, 38, BTN_BG); k1.Location = new Point(24, 48); k1.Click += BtnKeypad_Click;
            var k2 = MakeButton("Write Keypad Code", 165, 38, BTN_BG); k2.Location = new Point(200, 48); k2.Click += BtnKeypad_Click;
            var kNote = new Label { Text = "For vehicles with door keypad entry", Font = new Font("Segoe UI", 9), ForeColor = TEXT_MUTED, Location = new Point(390, 55), AutoSize = true };

            sec2.Controls.AddRange(new Control[] { k1, k2, kNote });
            _diagTab.Controls.Add(sec2);
            y += 110;

            // BCM Operations
            var sec3 = MakeSection("BCM ADVANCED OPERATIONS", W, 95);
            sec3.Location = new Point(0, y);

            var bcm = MakeButton("BCM Factory Reset", 170, 38, DANGER); bcm.Location = new Point(24, 48); bcm.Click += BtnBcm_Click;
            var warn = new Label { Text = "⚠ WARNING: Requires As-Built reprogramming with scan tool after reset!", Font = new Font("Segoe UI", 9), ForeColor = DANGER, Location = new Point(210, 55), AutoSize = true };

            sec3.Controls.AddRange(new Control[] { bcm, warn });
            _diagTab.Controls.Add(sec3);
            y += 110;

            // Module Info
            var sec4 = MakeSection("MODULE INFORMATION", W, 95);
            sec4.Location = new Point(0, y);

            var mod = MakeButton("Read All Module Info", 180, 38, BTN_BG); mod.Location = new Point(24, 48); mod.Click += BtnModInfo_Click;

            sec4.Controls.Add(mod);
            _diagTab.Controls.Add(sec4);

            _diagTab.Size = new Size(W + 20, y + 110);
            _content.Controls.Add(_diagTab);
        }

        private void BuildFreeTab()
        {
            _freeTab = new Panel { Location = new Point(24, 16), BackColor = BG, Visible = false };
            int y = 0;
            int W = 1220;

            // Free banner
            var banner = new Panel { Location = new Point(0, y), Size = new Size(W, 50), BackColor = Color.FromArgb(20, 34, 197, 94) };
            banner.Paint += (s, e) => { using var p = new Pen(SUCCESS, 2); e.Graphics.DrawRectangle(p, 1, 1, W - 3, 47); };
            var bannerLbl = new Label { Text = "✓ All operations on this tab are FREE - No token cost!", Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = SUCCESS, Location = new Point(24, 14), AutoSize = true };
            banner.Controls.Add(bannerLbl);
            _freeTab.Controls.Add(banner);
            y += 65;

            // Basic Operations
            var sec1 = MakeSection("BASIC VEHICLE OPERATIONS", W, 95);
            sec1.Location = new Point(0, y);

            var f1 = MakeButton("Clear All DTCs", 140, 38, BTN_BG); f1.Location = new Point(24, 48); f1.Click += BtnDtc_Click;
            var f2 = MakeButton("Clear KAM", 110, 38, BTN_BG); f2.Location = new Point(180, 48); f2.Click += BtnKam_Click;
            var f3 = MakeButton("Vehicle Reset", 130, 38, BTN_BG); f3.Location = new Point(305, 48); f3.Click += BtnReset_Click;

            sec1.Controls.AddRange(new Control[] { f1, f2, f3 });
            _freeTab.Controls.Add(sec1);
            y += 110;

            // Read Operations
            var sec2 = MakeSection("READ OPERATIONS", W, 95);
            sec2.Location = new Point(0, y);

            var r1 = MakeButton("Read Keys Count", 150, 38, BTN_BG); r1.Location = new Point(24, 48); r1.Click += BtnReadKeys_Click;
            var r2 = MakeButton("Read Module Info", 155, 38, BTN_BG); r2.Location = new Point(190, 48); r2.Click += BtnModInfo_Click;

            sec2.Controls.AddRange(new Control[] { r1, r2 });
            _freeTab.Controls.Add(sec2);
            y += 110;

            // Resources
            var sec3 = MakeSection("RESOURCES & SUPPORT", W, 95);
            sec3.Location = new Point(0, y);

            var u1 = MakeButton("User Guide", 120, 38, ACCENT); u1.Location = new Point(24, 48); u1.Click += (s, e) => OpenUrl("https://patskiller.com/faqs");
            var u2 = MakeButton("Buy Tokens", 120, 38, SUCCESS); u2.Location = new Point(160, 48); u2.Click += (s, e) => OpenUrl("https://patskiller.com/buy-tokens");
            var u3 = MakeButton("Contact Support", 150, 38, BTN_BG); u3.Location = new Point(295, 48); u3.Click += (s, e) => OpenUrl("https://patskiller.com/contact");

            sec3.Controls.AddRange(new Control[] { u1, u2, u3 });
            _freeTab.Controls.Add(sec3);

            _freeTab.Size = new Size(W + 20, y + 110);
            _content.Controls.Add(_freeTab);
        }

        private void BuildLoginPanel()
        {
            _loginPanel = new Panel { Dock = DockStyle.Fill, BackColor = BG };

            var card = new Panel { Size = new Size(420, 480), BackColor = CARD };
            card.Paint += (s, e) => { using var p = new Pen(BORDER, 2); e.Graphics.DrawRectangle(p, 1, 1, 417, 477); };

            int cy = 35;

            var lblWelcome = new Label
            {
                Text = "Welcome to PatsKiller Pro",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = TEXT,
                Size = new Size(400, 35),
                Location = new Point(10, cy),
                TextAlign = ContentAlignment.MiddleCenter
            };
            cy += 40;

            var lblSub = new Label
            {
                Text = "Sign in to access your tokens",
                Font = new Font("Segoe UI", 10),
                ForeColor = TEXT_MUTED,
                Size = new Size(400, 22),
                Location = new Point(10, cy),
                TextAlign = ContentAlignment.MiddleCenter
            };
            cy += 45;

            var btnGoogle = MakeButton("Continue with Google", 340, 48, Color.White);
            btnGoogle.ForeColor = Color.FromArgb(50, 50, 50);
            btnGoogle.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            btnGoogle.Location = new Point(40, cy);
            btnGoogle.Click += BtnGoogle_Click;
            cy += 65;

            var lblOr = new Label
            {
                Text = "───────  or sign in with email  ───────",
                Font = new Font("Segoe UI", 9),
                ForeColor = TEXT_MUTED,
                Size = new Size(340, 20),
                Location = new Point(40, cy),
                TextAlign = ContentAlignment.MiddleCenter
            };
            cy += 35;

            var lblEmail = new Label { Text = "Email", Font = new Font("Segoe UI", 9), ForeColor = TEXT_DIM, Location = new Point(40, cy), AutoSize = true };
            cy += 22;

            _txtEmail = MakeTextBox(340);
            _txtEmail.Location = new Point(40, cy);
            cy += 48;

            var lblPass = new Label { Text = "Password", Font = new Font("Segoe UI", 9), ForeColor = TEXT_DIM, Location = new Point(40, cy), AutoSize = true };
            cy += 22;

            _txtPassword = MakeTextBox(340);
            _txtPassword.Location = new Point(40, cy);
            _txtPassword.UseSystemPasswordChar = true;
            _txtPassword.KeyPress += (s, e) => { if (e.KeyChar == 13) DoLogin(); };
            cy += 55;

            var btnLogin = MakeButton("Sign In", 340, 48, ACCENT);
            btnLogin.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            btnLogin.Location = new Point(40, cy);
            btnLogin.Click += (s, e) => DoLogin();

            card.Controls.AddRange(new Control[] { lblWelcome, lblSub, btnGoogle, lblOr, lblEmail, _txtEmail, lblPass, _txtPassword, btnLogin });
            _loginPanel.Controls.Add(card);
            _loginPanel.Resize += (s, e) => { card.Location = new Point((_loginPanel.Width - 420) / 2, (_loginPanel.Height - 480) / 2 - 30); };

            Controls.Add(_loginPanel);
        }

        #region UI Helpers
        private Panel MakeSection(string title, int w, int h)
        {
            var p = new Panel { Size = new Size(w, h), BackColor = CARD };
            p.Paint += (s, e) =>
            {
                using var pen = new Pen(BORDER);
                e.Graphics.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                using var font = new Font("Segoe UI", 10F, FontStyle.Bold);
                using var brush = new SolidBrush(TEXT_DIM);
                e.Graphics.DrawString(title, font, brush, 20, 12);
                e.Graphics.DrawLine(pen, 16, 38, w - 16, 38);
            };
            return p;
        }

        private Button MakeButton(string text, int w, int h, Color bg)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = BORDER;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        private Button MakeTabButton(string text, bool active)
        {
            int w = TextRenderer.MeasureText(text, new Font("Segoe UI", 10, FontStyle.Bold)).Width + 40;
            var b = new Button
            {
                Text = text,
                Size = new Size(w, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = active ? ACCENT : BTN_BG,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private TextBox MakeTextBox(int w) => new TextBox
        {
            Size = new Size(w, 38),
            BackColor = SURFACE,
            ForeColor = TEXT,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 11),
            TextAlign = HorizontalAlignment.Center
        };

        private ComboBox MakeComboBox(int w) => new ComboBox
        {
            Size = new Size(w, 36),
            BackColor = SURFACE,
            ForeColor = TEXT,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        private void Log(string t, string m)
        {
            if (_txtLog == null || _txtLog.IsDisposed) return;
            if (_txtLog.InvokeRequired) { _txtLog.Invoke(() => Log(t, m)); return; }
            var c = t == "success" ? SUCCESS : t == "error" ? DANGER : t == "warning" ? WARNING : TEXT_DIM;
            _txtLog.SelectionColor = TEXT_MUTED;
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
            _txtLog.SelectionColor = c;
            _txtLog.AppendText($"[{(t == "success" ? "OK" : t == "error" ? "ERR" : t == "warning" ? "WARN" : "INFO")}] {m}\n");
            _txtLog.ScrollToCaret();
        }

        private void OpenUrl(string u) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = u, UseShellExecute = true }); } catch { } }
        private void ShowError(string t, string m, Exception? ex = null) { MessageBox.Show(ex != null ? $"{m}\n\n{ex.Message}" : m, t, MessageBoxButtons.OK, MessageBoxIcon.Error); Log("error", m); }
        private bool Confirm(int cost, string op) { if (cost == 0) return true; if (_tokenBalance < cost) { MessageBox.Show($"Need {cost} tokens", "Insufficient", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; } return MessageBox.Show($"{op}\nCost: {cost} token(s)\n\nProceed?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes; }
        #endregion

        #region Navigation
        private void ShowLogin() { _loginPanel.Visible = true; _tabBar.Visible = _content.Visible = _logPanel.Visible = _btnLogout.Visible = false; }
        private void ShowMain() { _loginPanel.Visible = false; _tabBar.Visible = _content.Visible = _logPanel.Visible = _btnLogout.Visible = true; LayoutHeader(); SwitchTab(0); }
        private void SwitchTab(int i)
        {
            _activeTab = i;
            _btnTab1.BackColor = i == 0 ? ACCENT : BTN_BG;
            _btnTab2.BackColor = i == 1 ? ACCENT : BTN_BG;
            _btnTab3.BackColor = i == 2 ? ACCENT : BTN_BG;
            _patsTab.Visible = i == 0;
            _diagTab.Visible = i == 1;
            _freeTab.Visible = i == 2;
        }
        #endregion

        #region Auth
        private void LoadSession()
        {
            var e = Settings.GetString("email", "");
            var t = Settings.GetString("auth_token", "");
            if (!string.IsNullOrEmpty(e) && !string.IsNullOrEmpty(t))
            {
                _userEmail = e; _authToken = t; _tokenBalance = 10;
                _lblTokens.Text = $"Tokens: {_tokenBalance}";
                _lblUser.Text = _userEmail;
                ShowMain();
                Log("info", $"Logged in as {_userEmail}");
            }
        }

        private async void DoLogin()
        {
            var e = _txtEmail.Text.Trim();
            var p = _txtPassword.Text;
            if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p)) { MessageBox.Show("Enter email and password"); return; }
            await Task.Delay(200);
            _userEmail = e; _authToken = "t_" + DateTime.Now.Ticks; _tokenBalance = 10;
            Settings.SetString("email", e); Settings.SetString("auth_token", _authToken); Settings.Save();
            _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail;
            ShowMain();
            Log("success", $"Logged in as {e}");
        }

        private void BtnGoogle_Click(object? s, EventArgs e)
        {
            try
            {
                using var f = new GoogleLoginForm();
                if (f.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(f.AuthToken))
                {
                    _authToken = f.AuthToken; _userEmail = f.UserEmail ?? "Google User"; _tokenBalance = f.TokenCount;
                    Settings.SetString("auth_token", _authToken); Settings.SetString("email", _userEmail); Settings.Save();
                    _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail;
                    ShowMain();
                    Log("success", $"Logged in as {_userEmail}");
                }
            }
            catch (Exception ex) { ShowError("Login Failed", ex.Message, ex); }
        }

        private void Logout()
        {
            _userEmail = _authToken = ""; _tokenBalance = 0;
            Settings.Remove("auth_token"); Settings.Save();
            _txtPassword.Text = ""; _lblTokens.Text = "Tokens: --"; _lblUser.Text = "";
            ShowLogin();
        }
        #endregion

        #region Device Operations
        private void BtnScan_Click(object? s, EventArgs e)
        {
            try
            {
                _cmbDevices.Items.Clear();
                _deviceManager?.Dispose();
                _deviceManager = new J2534DeviceManager();
                _deviceManager.ScanForDevices();
                var names = _deviceManager.GetDeviceNames();
                if (names.Count == 0) { _cmbDevices.Items.Add("No devices found"); Log("warning", "No devices found"); }
                else { foreach (var n in names) _cmbDevices.Items.Add(n); Log("success", $"Found {names.Count} device(s)"); }
                _cmbDevices.SelectedIndex = 0;
            }
            catch (Exception ex) { ShowError("Scan Error", "Failed to scan", ex); }
        }

        private void BtnConnect_Click(object? s, EventArgs e)
        {
            if (_cmbDevices.SelectedItem == null || _deviceManager == null) return;
            var name = _cmbDevices.SelectedItem.ToString()!;
            if (name.Contains("No") || name.Contains("Select")) { MessageBox.Show("Select a device first"); return; }
            try
            {
                _device = _deviceManager.ConnectToDevice(name);
                _channel = _device.OpenChannel(Protocol.ISO15765, BaudRates.HS_CAN_500K, ConnectFlags.NONE);
                _lblStatus.Text = "Status: Connected";
                _lblStatus.ForeColor = SUCCESS;
                Log("success", $"Connected to {name}");
            }
            catch (Exception ex) { ShowError("Connect Error", "Failed to connect", ex); }
        }

        private async void BtnReadVin_Click(object? s, EventArgs e)
        {
            if (_channel == null) { MessageBox.Show("Connect to device first"); return; }
            try
            {
                Log("info", "Reading VIN...");
                var uds = new UdsService(_channel);
                var vin = await Task.Run(() => uds.ReadVIN());
                if (!string.IsNullOrEmpty(vin))
                {
                    _lblVin.Text = $"VIN: {vin}";
                    _lblVin.ForeColor = SUCCESS;
                    _txtOutcode.Text = await Task.Run(() => uds.ReadOutcode());
                    Log("success", $"VIN: {vin}");
                }
                else
                {
                    _lblVin.Text = "VIN: Could not read";
                    _lblVin.ForeColor = DANGER;
                    Log("warning", "Could not read VIN");
                }
            }
            catch (Exception ex) { ShowError("Read Error", "Failed to read VIN", ex); }
        }
        #endregion

        #region PATS Operations
        private async void BtnProgram_Click(object? s, EventArgs e)
        {
            if (_channel == null) { MessageBox.Show("Connect to device first"); return; }
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode first"); return; }
            try
            {
                Log("info", "Programming key...");
                var pats = new PatsOperations(new UdsService(_channel));
                if (await Task.Run(() => pats.ProgramKeys(incode)))
                {
                    MessageBox.Show("Key programmed successfully!\n\nRemove key, insert next key, click Program again.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Log("success", "Key programmed");
                }
            }
            catch (Exception ex) { ShowError("Program Error", "Failed to program key", ex); }
        }

        private async void BtnErase_Click(object? s, EventArgs e)
        {
            if (_channel == null) { MessageBox.Show("Connect to device first"); return; }
            if (!Confirm(PatsOperations.TOKEN_COST_KEY_ERASE, "Erase All Keys")) return;
            if (MessageBox.Show("Are you sure you want to ERASE ALL KEYS?\n\nThis cannot be undone!", "Confirm Erase", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode first"); return; }
            try
            {
                Log("warning", "Erasing all keys...");
                var pats = new PatsOperations(new UdsService(_channel));
                await Task.Run(() => pats.EraseAllKeys(incode));
                MessageBox.Show("All keys erased!\n\nYou must program at least 2 new keys.", "Erased", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("success", "All keys erased");
            }
            catch (Exception ex) { ShowError("Erase Error", "Failed to erase keys", ex); }
        }

        private async void BtnParam_Click(object? s, EventArgs e)
        {
            if (_channel == null) { MessageBox.Show("Connect to device first"); return; }
            try
            {
                Log("info", "Parameter reset...");
                var pats = new PatsOperations(new UdsService(_channel));
                await Task.Run(() => pats.ParameterReset());
                MessageBox.Show("Parameter reset complete!\n\nTurn ignition OFF for 15 seconds, then ON.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "Parameter reset complete");
            }
            catch (Exception ex) { ShowError("Reset Error", "Failed to reset", ex); }
        }

        private async void BtnEscl_Click(object? s, EventArgs e)
        {
            if (_channel == null) { MessageBox.Show("Connect to device first"); return; }
            if (!Confirm(PatsOperations.TOKEN_COST_ESCL_INIT, "Initialize ESCL")) return;
            try
            {
                Log("info", "Initializing ESCL...");
                var pats = new PatsOperations(new UdsService(_channel));
                await Task.Run(() => pats.InitializeESCL());
                MessageBox.Show("ESCL initialized successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "ESCL initialized");
            }
            catch (Exception ex) { ShowError("ESCL Error", "Failed to initialize ESCL", ex); }
        }

        private async void BtnDisable_Click(object? s, EventArgs e)
        {
            if (_channel == null) { MessageBox.Show("Connect to device first"); return; }
            try
            {
                Log("info", "Disabling BCM security...");
                var pats = new PatsOperations(new UdsService(_channel));
                await Task.Run(() => pats.DisableBcmSecurity());
                MessageBox.Show("BCM security disabled.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "BCM security disabled");
            }
            catch (Exception ex) { ShowError("BCM Error", "Failed to disable BCM", ex); }
        }
        #endregion

        #region Diagnostics
        private async void BtnP160A_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_CLEAR_P160A, "Clear P160A")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ClearP160A()); MessageBox.Show("P160A cleared!"); Log("success", "P160A cleared"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnB10A2_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_CLEAR_B10A2, "Clear B10A2")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ClearB10A2()); MessageBox.Show("B10A2 cleared!"); Log("success", "B10A2 cleared"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnCrush_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_CLEAR_CRUSH, "Clear Crush")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ClearCrushEvent()); MessageBox.Show("Crush event cleared!"); Log("success", "Crush cleared"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnGateway_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var pats = new PatsOperations(new UdsService(_channel)); if (!await Task.Run(() => pats.DetectGateway())) { MessageBox.Show("No gateway detected (pre-2020 vehicle)"); return; } if (!Confirm(PatsOperations.TOKEN_COST_GATEWAY_UNLOCK, "Unlock Gateway")) return; var ic = _txtIncode.Text.Trim(); if (string.IsNullOrEmpty(ic)) { MessageBox.Show("Enter incode"); return; } await Task.Run(() => pats.UnlockGateway(ic)); MessageBox.Show("Gateway unlocked!"); Log("success", "Gateway unlocked"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnKeypad_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } var r = MessageBox.Show("YES = Read Code\nNO = Write Code", "Keypad", MessageBoxButtons.YesNoCancel); if (r == DialogResult.Cancel) return; if (r == DialogResult.Yes) { if (!Confirm(PatsOperations.TOKEN_COST_KEYPAD_READ, "Read Keypad")) return; try { var code = await Task.Run(() => new PatsOperations(new UdsService(_channel)).ReadKeypadCode()); MessageBox.Show($"Keypad Code: {code}"); Log("success", $"Keypad: {code}"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } } else { var nc = Microsoft.VisualBasic.Interaction.InputBox("Enter 5-digit code:", "Write Keypad", ""); if (nc.Length != 5) return; if (!Confirm(PatsOperations.TOKEN_COST_KEYPAD_WRITE, "Write Keypad")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).WriteKeypadCode(nc)); MessageBox.Show($"Keypad set to: {nc}"); Log("success", "Keypad written"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } } }
        private async void BtnBcm_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (MessageBox.Show("This will reset ALL BCM settings!\nYou will need a scan tool to reprogram As-Built data!\n\nContinue?", "WARNING", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; if (!Confirm(PatsOperations.TOKEN_COST_BCM_FACTORY, "BCM Factory Reset")) return; var i1 = Microsoft.VisualBasic.Interaction.InputBox("Incode 1:", "BCM Reset", _txtIncode.Text); if (string.IsNullOrEmpty(i1)) return; var i2 = Microsoft.VisualBasic.Interaction.InputBox("Incode 2:", "BCM Reset", ""); if (string.IsNullOrEmpty(i2)) return; var i3 = Microsoft.VisualBasic.Interaction.InputBox("Incode 3 (optional):", "BCM Reset", ""); var codes = string.IsNullOrEmpty(i3) ? new[] { i1, i2 } : new[] { i1, i2, i3 }; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).BcmFactoryDefaults(codes)); MessageBox.Show("BCM reset complete!\nUse scan tool to reprogram As-Built data."); Log("success", "BCM reset"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnModInfo_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var info = await Task.Run(() => new UdsService(_channel).ReadAllModuleInfo()); MessageBox.Show(info, "Module Information"); Log("success", "Module info read"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        #endregion

        #region Free Functions
        private async void BtnDtc_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new UdsService(_channel).ClearDTCs()); MessageBox.Show("All DTCs cleared!"); Log("success", "DTCs cleared"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnKam_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ClearKAM()); MessageBox.Show("KAM cleared!"); Log("success", "KAM cleared"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnReset_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).VehicleReset()); MessageBox.Show("Vehicle reset complete!"); Log("success", "Vehicle reset"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnReadKeys_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var count = await Task.Run(() => new UdsService(_channel).ReadKeysCount()); _lblKeys.Text = count.ToString(); MessageBox.Show($"Keys programmed: {count}"); Log("success", $"Keys: {count}"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _channel?.Dispose(); _device?.Dispose(); _deviceManager?.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}