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
        // Theme
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
        private string _userEmail = "", _authToken = "";
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
            this.Text = "PatsKiller Pro 2026";
            this.ClientSize = new Size(1400, 950);
            this.MinimumSize = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = BG;
            this.ForeColor = TEXT;
            this.Font = new Font("Segoe UI", 10F);
            this.DoubleBuffered = true;
            this.AutoScaleMode = AutoScaleMode.Dpi;
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
            _header = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = SURFACE };
            _header.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawLine(p, 0, 89, Width, 89); };

            var logo = new Label { Text = "PK", Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.White, BackColor = ACCENT, Size = new Size(54, 54), Location = new Point(24, 18), TextAlign = ContentAlignment.MiddleCenter };
            var title = new Label { Text = "PatsKiller Pro", Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = TEXT, Location = new Point(90, 16), AutoSize = true };
            var subtitle = new Label { Text = "Ford & Lincoln PATS Key Programming", Font = new Font("Segoe UI", 10), ForeColor = TEXT_MUTED, Location = new Point(92, 52), AutoSize = true };
            _lblTokens = new Label { Text = "Tokens: --", Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = SUCCESS, AutoSize = true };
            _lblUser = new Label { Font = new Font("Segoe UI", 10), ForeColor = TEXT_DIM, AutoSize = true };
            _btnLogout = AutoBtn("Logout", BTN_BG); _btnLogout.Click += (s, e) => Logout(); _btnLogout.Visible = false;

            _header.Controls.AddRange(new Control[] { logo, title, subtitle, _lblTokens, _lblUser, _btnLogout });
            _header.Resize += (s, e) => LayoutHeader();
            Controls.Add(_header);

            // TAB BAR
            _tabBar = new Panel { Dock = DockStyle.Top, Height = 55, BackColor = SURFACE, Visible = false };
            _tabBar.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawLine(p, 0, 54, Width, 54); };

            var tabFlow = new FlowLayoutPanel { Location = new Point(20, 10), Size = new Size(800, 42), BackColor = Color.Transparent };
            _btnTab1 = TabBtn("PATS Key Programming", true); _btnTab1.Click += (s, e) => SwitchTab(0); tabFlow.Controls.Add(_btnTab1);
            _btnTab2 = TabBtn("Diagnostics", false); _btnTab2.Click += (s, e) => SwitchTab(1); tabFlow.Controls.Add(_btnTab2);
            _btnTab3 = TabBtn("Free Functions", false); _btnTab3.Click += (s, e) => SwitchTab(2); tabFlow.Controls.Add(_btnTab3);
            _tabBar.Controls.Add(tabFlow);
            Controls.Add(_tabBar);

            // LOG
            _logPanel = new Panel { Dock = DockStyle.Bottom, Height = 130, BackColor = SURFACE, Visible = false };
            _logPanel.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawLine(p, 0, 0, Width, 0); };
            var logLbl = new Label { Text = "ACTIVITY LOG", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = TEXT_DIM, Location = new Point(24, 12), AutoSize = true };
            _txtLog = new RichTextBox { Location = new Point(24, 38), BackColor = BG, ForeColor = TEXT, Font = new Font("Consolas", 10), BorderStyle = BorderStyle.None, ReadOnly = true };
            _logPanel.Controls.AddRange(new Control[] { logLbl, _txtLog });
            _logPanel.Resize += (s, e) => _txtLog.Size = new Size(_logPanel.Width - 48, _logPanel.Height - 50);
            Controls.Add(_logPanel);

            // CONTENT
            _content = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false, AutoScroll = true };
            Controls.Add(_content);

            BuildPatsTab();
            BuildDiagTab();
            BuildFreeTab();
            BuildLogin();
            ShowLogin();
        }

        private void LayoutHeader()
        {
            int r = _header.Width - 30;
            _btnLogout.Location = new Point(r - _btnLogout.Width, 28);
            _lblTokens.Location = new Point(_btnLogout.Left - _lblTokens.Width - 25, 22);
            _lblUser.Location = new Point(_btnLogout.Left - _lblUser.Width - 25, 52);
        }

        private void BuildPatsTab()
        {
            _patsTab = new Panel { Location = new Point(20, 15), BackColor = BG, AutoSize = true };
            int y = 0, W = 1320;

            // === J2534 DEVICE CONNECTION ===
            var sec1 = Section("J2534 DEVICE CONNECTION", W, 100);
            sec1.Location = new Point(0, y);

            var row1 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 48), BackColor = Color.Transparent };
            _cmbDevices = MakeCombo(420); _cmbDevices.Items.Add("Select J2534 Device..."); _cmbDevices.SelectedIndex = 0;
            _cmbDevices.Margin = new Padding(0, 4, 20, 0);
            row1.Controls.Add(_cmbDevices);

            var btnScan = AutoBtn("Scan Devices", BTN_BG); btnScan.Click += BtnScan_Click; row1.Controls.Add(btnScan);
            var btnConn = AutoBtn("Connect", SUCCESS); btnConn.Click += BtnConnect_Click; row1.Controls.Add(btnConn);
            _lblStatus = new Label { Text = "Status: Not Connected", Font = new Font("Segoe UI", 11), ForeColor = WARNING, AutoSize = true, Margin = new Padding(25, 10, 0, 0) };
            row1.Controls.Add(_lblStatus);

            sec1.Controls.Add(row1);
            _patsTab.Controls.Add(sec1);
            y += 115;

            // === VEHICLE INFORMATION ===
            var sec2 = Section("VEHICLE INFORMATION", W, 100);
            sec2.Location = new Point(0, y);

            var row2 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 180, 48), BackColor = Color.Transparent };
            var btnVin = AutoBtn("Read VIN", ACCENT); btnVin.Click += BtnReadVin_Click; row2.Controls.Add(btnVin);
            _lblVin = new Label { Text = "VIN: —————————————————", Font = new Font("Consolas", 12), ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(15, 10, 20, 0) };
            row2.Controls.Add(_lblVin);
            var lblSel = new Label { Text = "Or select vehicle:", Font = new Font("Segoe UI", 10), ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(10, 10, 10, 0) };
            row2.Controls.Add(lblSel);
            _cmbVehicles = MakeCombo(360);
            foreach (var v in VehiclePlatforms.GetAllVehicles()) _cmbVehicles.Items.Add(v.DisplayName);
            if (_cmbVehicles.Items.Count > 0) _cmbVehicles.SelectedIndex = 0;
            _cmbVehicles.Margin = new Padding(0, 4, 0, 0);
            row2.Controls.Add(_cmbVehicles);

            sec2.Controls.Add(row2);

            // Keys badge
            var keysBg = new Panel { Location = new Point(W - 150, 42), Size = new Size(130, 52), BackColor = SURFACE };
            keysBg.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, 129, 51); };
            keysBg.Controls.Add(new Label { Text = "KEYS", Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = TEXT_MUTED, Location = new Point(0, 6), Size = new Size(130, 16), TextAlign = ContentAlignment.MiddleCenter });
            _lblKeys = new Label { Text = "--", Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = SUCCESS, Location = new Point(0, 22), Size = new Size(130, 28), TextAlign = ContentAlignment.MiddleCenter };
            keysBg.Controls.Add(_lblKeys);
            sec2.Controls.Add(keysBg);

            _patsTab.Controls.Add(sec2);
            y += 115;

            // === PATS SECURITY CODES ===
            var sec3 = Section("PATS SECURITY CODES", W, 100);
            sec3.Location = new Point(0, y);

            var row3 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 48), BackColor = Color.Transparent };
            row3.Controls.Add(new Label { Text = "OUTCODE:", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 10, 8, 0) });
            _txtOutcode = MakeTextBox(160); _txtOutcode.ReadOnly = true; _txtOutcode.Margin = new Padding(0, 4, 15, 0); row3.Controls.Add(_txtOutcode);
            var btnCopy = AutoBtn("Copy", BTN_BG); btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) { Clipboard.SetText(_txtOutcode.Text); Log("info", "Copied"); } }; row3.Controls.Add(btnCopy);
            var btnGet = AutoBtn("Get Incode Online", WARNING); btnGet.ForeColor = Color.Black; btnGet.Click += (s, e) => OpenUrl("https://patskiller.com/calculator"); row3.Controls.Add(btnGet);
            row3.Controls.Add(new Label { Text = "INCODE:", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = TEXT, AutoSize = true, Margin = new Padding(30, 10, 8, 0) });
            _txtIncode = MakeTextBox(160); _txtIncode.Margin = new Padding(0, 4, 0, 0); row3.Controls.Add(_txtIncode);

            sec3.Controls.Add(row3);
            _patsTab.Controls.Add(sec3);
            y += 115;

            // === KEY PROGRAMMING OPERATIONS ===
            var sec4 = Section("KEY PROGRAMMING OPERATIONS", W, 130);
            sec4.Location = new Point(0, y);

            var row4 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 50), BackColor = Color.Transparent };
            var btnProg = AutoBtn("Program Key", SUCCESS); btnProg.Font = new Font("Segoe UI", 11, FontStyle.Bold); btnProg.Click += BtnProgram_Click; row4.Controls.Add(btnProg);
            var btnErase = AutoBtn("Erase All Keys", DANGER); btnErase.Click += BtnErase_Click; row4.Controls.Add(btnErase);
            var btnParam = AutoBtn("Parameter Reset", BTN_BG); btnParam.Click += BtnParam_Click; row4.Controls.Add(btnParam);
            var btnEscl = AutoBtn("Initialize ESCL", BTN_BG); btnEscl.Click += BtnEscl_Click; row4.Controls.Add(btnEscl);
            var btnDis = AutoBtn("Disable BCM Security", BTN_BG); btnDis.Click += BtnDisable_Click; row4.Controls.Add(btnDis);
            sec4.Controls.Add(row4);

            var tip = new Label { Text = "💡 Tip: Program Key costs 1 token per session (unlimited keys). Insert key, click Program, repeat for additional keys.", Font = new Font("Segoe UI", 10), ForeColor = TEXT_MUTED, Location = new Point(24, 102), AutoSize = true };
            sec4.Controls.Add(tip);

            _patsTab.Controls.Add(sec4);
            _patsTab.Size = new Size(W + 30, y + 145);
            _content.Controls.Add(_patsTab);
        }

        private void BuildDiagTab()
        {
            _diagTab = new Panel { Location = new Point(20, 15), BackColor = BG, Visible = false, AutoSize = true };
            int y = 0, W = 1320;

            var sec1 = Section("DTC CLEAR OPERATIONS (1 TOKEN EACH)", W, 100);
            sec1.Location = new Point(0, y);
            var r1 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 48), BackColor = Color.Transparent };
            var b1 = AutoBtn("Clear P160A", ACCENT); b1.Click += BtnP160A_Click; r1.Controls.Add(b1);
            var b2 = AutoBtn("Clear B10A2", ACCENT); b2.Click += BtnB10A2_Click; r1.Controls.Add(b2);
            var b3 = AutoBtn("Clear Crush Event", ACCENT); b3.Click += BtnCrush_Click; r1.Controls.Add(b3);
            var b4 = AutoBtn("Unlock Gateway", ACCENT); b4.Click += BtnGateway_Click; r1.Controls.Add(b4);
            sec1.Controls.Add(r1);
            _diagTab.Controls.Add(sec1);
            y += 115;

            var sec2 = Section("KEYPAD CODE OPERATIONS", W, 100);
            sec2.Location = new Point(0, y);
            var r2 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 48), BackColor = Color.Transparent };
            var k1 = AutoBtn("Read Keypad Code", BTN_BG); k1.Click += BtnKeypad_Click; r2.Controls.Add(k1);
            var k2 = AutoBtn("Write Keypad Code", BTN_BG); k2.Click += BtnKeypad_Click; r2.Controls.Add(k2);
            r2.Controls.Add(new Label { Text = "For vehicles with door keypad entry", Font = new Font("Segoe UI", 10), ForeColor = TEXT_MUTED, AutoSize = true, Margin = new Padding(20, 12, 0, 0) });
            sec2.Controls.Add(r2);
            _diagTab.Controls.Add(sec2);
            y += 115;

            var sec3 = Section("BCM ADVANCED OPERATIONS", W, 100);
            sec3.Location = new Point(0, y);
            var r3 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 48), BackColor = Color.Transparent };
            var bcm = AutoBtn("BCM Factory Reset", DANGER); bcm.Click += BtnBcm_Click; r3.Controls.Add(bcm);
            r3.Controls.Add(new Label { Text = "⚠ WARNING: Requires As-Built reprogramming after reset!", Font = new Font("Segoe UI", 10), ForeColor = DANGER, AutoSize = true, Margin = new Padding(20, 12, 0, 0) });
            sec3.Controls.Add(r3);
            _diagTab.Controls.Add(sec3);
            y += 115;

            var sec4 = Section("MODULE INFORMATION", W, 100);
            sec4.Location = new Point(0, y);
            var r4 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 48), BackColor = Color.Transparent };
            var mod = AutoBtn("Read All Module Info", BTN_BG); mod.Click += BtnModInfo_Click; r4.Controls.Add(mod);
            sec4.Controls.Add(r4);
            _diagTab.Controls.Add(sec4);

            _diagTab.Size = new Size(W + 30, y + 115);
            _content.Controls.Add(_diagTab);
        }

        private void BuildFreeTab()
        {
            _freeTab = new Panel { Location = new Point(20, 15), BackColor = BG, Visible = false, AutoSize = true };
            int y = 0, W = 1320;

            var banner = new Panel { Location = new Point(0, y), Size = new Size(W, 55), BackColor = Color.FromArgb(20, 34, 197, 94) };
            banner.Paint += (s, e) => { using var p = new Pen(SUCCESS, 2); e.Graphics.DrawRectangle(p, 1, 1, W - 3, 52); };
            banner.Controls.Add(new Label { Text = "✓ All operations on this tab are FREE - No token cost!", Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = SUCCESS, Location = new Point(24, 15), AutoSize = true });
            _freeTab.Controls.Add(banner);
            y += 70;

            var sec1 = Section("BASIC VEHICLE OPERATIONS", W, 100);
            sec1.Location = new Point(0, y);
            var r1 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 48), BackColor = Color.Transparent };
            var f1 = AutoBtn("Clear All DTCs", BTN_BG); f1.Click += BtnDtc_Click; r1.Controls.Add(f1);
            var f2 = AutoBtn("Clear KAM", BTN_BG); f2.Click += BtnKam_Click; r1.Controls.Add(f2);
            var f3 = AutoBtn("Vehicle Reset", BTN_BG); f3.Click += BtnReset_Click; r1.Controls.Add(f3);
            sec1.Controls.Add(r1);
            _freeTab.Controls.Add(sec1);
            y += 115;

            var sec2 = Section("READ OPERATIONS", W, 100);
            sec2.Location = new Point(0, y);
            var r2 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 48), BackColor = Color.Transparent };
            var rd1 = AutoBtn("Read Keys Count", BTN_BG); rd1.Click += BtnReadKeys_Click; r2.Controls.Add(rd1);
            var rd2 = AutoBtn("Read Module Info", BTN_BG); rd2.Click += BtnModInfo_Click; r2.Controls.Add(rd2);
            sec2.Controls.Add(r2);
            _freeTab.Controls.Add(sec2);
            y += 115;

            var sec3 = Section("RESOURCES & SUPPORT", W, 100);
            sec3.Location = new Point(0, y);
            var r3 = new FlowLayoutPanel { Location = new Point(20, 48), Size = new Size(W - 40, 48), BackColor = Color.Transparent };
            var u1 = AutoBtn("User Guide", ACCENT); u1.Click += (s, e) => OpenUrl("https://patskiller.com/faqs"); r3.Controls.Add(u1);
            var u2 = AutoBtn("Buy Tokens", SUCCESS); u2.Click += (s, e) => OpenUrl("https://patskiller.com/buy-tokens"); r3.Controls.Add(u2);
            var u3 = AutoBtn("Contact Support", BTN_BG); u3.Click += (s, e) => OpenUrl("https://patskiller.com/contact"); r3.Controls.Add(u3);
            sec3.Controls.Add(r3);
            _freeTab.Controls.Add(sec3);

            _freeTab.Size = new Size(W + 30, y + 115);
            _content.Controls.Add(_freeTab);
        }

        private void BuildLogin()
        {
            _loginPanel = new Panel { Dock = DockStyle.Fill, BackColor = BG };
            var card = new Panel { Size = new Size(450, 520), BackColor = CARD };
            card.Paint += (s, e) => { using var p = new Pen(BORDER, 2); e.Graphics.DrawRectangle(p, 1, 1, 447, 517); };

            int cy = 40;
            card.Controls.Add(new Label { Text = "Welcome to PatsKiller Pro", Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = TEXT, Size = new Size(430, 40), Location = new Point(10, cy), TextAlign = ContentAlignment.MiddleCenter }); cy += 45;
            card.Controls.Add(new Label { Text = "Sign in to access your tokens", Font = new Font("Segoe UI", 11), ForeColor = TEXT_MUTED, Size = new Size(430, 25), Location = new Point(10, cy), TextAlign = ContentAlignment.MiddleCenter }); cy += 50;

            var btnG = new Button { Text = "Continue with Google", Size = new Size(370, 52), Location = new Point(40, cy), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(50, 50, 50), Font = new Font("Segoe UI", 12, FontStyle.Bold), Cursor = Cursors.Hand };
            btnG.FlatAppearance.BorderColor = BORDER;
            btnG.Click += BtnGoogle_Click;
            card.Controls.Add(btnG); cy += 70;

            card.Controls.Add(new Label { Text = "───────  or sign in with email  ───────", Font = new Font("Segoe UI", 10), ForeColor = TEXT_MUTED, Size = new Size(370, 22), Location = new Point(40, cy), TextAlign = ContentAlignment.MiddleCenter }); cy += 38;
            card.Controls.Add(new Label { Text = "Email", Font = new Font("Segoe UI", 10), ForeColor = TEXT_DIM, Location = new Point(40, cy), AutoSize = true }); cy += 25;
            _txtEmail = MakeTextBox(370); _txtEmail.Location = new Point(40, cy); card.Controls.Add(_txtEmail); cy += 52;
            card.Controls.Add(new Label { Text = "Password", Font = new Font("Segoe UI", 10), ForeColor = TEXT_DIM, Location = new Point(40, cy), AutoSize = true }); cy += 25;
            _txtPassword = MakeTextBox(370); _txtPassword.Location = new Point(40, cy); _txtPassword.UseSystemPasswordChar = true; _txtPassword.KeyPress += (s, e) => { if (e.KeyChar == 13) DoLogin(); }; card.Controls.Add(_txtPassword); cy += 58;

            var btnL = new Button { Text = "Sign In", Size = new Size(370, 52), Location = new Point(40, cy), FlatStyle = FlatStyle.Flat, BackColor = ACCENT, ForeColor = TEXT, Font = new Font("Segoe UI", 13, FontStyle.Bold), Cursor = Cursors.Hand };
            btnL.FlatAppearance.BorderColor = BORDER;
            btnL.Click += (s, e) => DoLogin();
            card.Controls.Add(btnL);

            _loginPanel.Controls.Add(card);
            _loginPanel.Resize += (s, e) => card.Location = new Point((_loginPanel.Width - 450) / 2, (_loginPanel.Height - 520) / 2 - 30);
            Controls.Add(_loginPanel);
        }

        #region Helpers
        private Panel Section(string title, int w, int h)
        {
            var p = new Panel { Size = new Size(w, h), BackColor = CARD };
            p.Paint += (s, e) => {
                using var pen = new Pen(BORDER);
                e.Graphics.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                using var f = new Font("Segoe UI", 11F, FontStyle.Bold);
                using var b = new SolidBrush(TEXT_DIM);
                e.Graphics.DrawString(title, f, b, 22, 14);
                e.Graphics.DrawLine(pen, 18, 42, w - 18, 42);
            };
            return p;
        }

        // AUTO-SIZED BUTTON - Windows calculates width!
        private Button AutoBtn(string text, Color bg)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                Padding = new Padding(20, 12, 20, 12),  // Generous padding
                Margin = new Padding(0, 0, 15, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = BORDER;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        private Button TabBtn(string text, bool active)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                Padding = new Padding(22, 8, 22, 8),
                Margin = new Padding(0, 0, 10, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = active ? ACCENT : BTN_BG,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private TextBox MakeTextBox(int w) => new TextBox { Size = new Size(w, 40), BackColor = SURFACE, ForeColor = TEXT, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 12), TextAlign = HorizontalAlignment.Center };
        private ComboBox MakeCombo(int w) => new ComboBox { Size = new Size(w, 40), BackColor = SURFACE, ForeColor = TEXT, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11), DropDownStyle = ComboBoxStyle.DropDownList };

        private void Log(string t, string m) { if (_txtLog == null) return; if (_txtLog.InvokeRequired) { _txtLog.Invoke(() => Log(t, m)); return; } var c = t == "success" ? SUCCESS : t == "error" ? DANGER : t == "warning" ? WARNING : TEXT_DIM; _txtLog.SelectionColor = TEXT_MUTED; _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] "); _txtLog.SelectionColor = c; _txtLog.AppendText($"[{(t == "success" ? "OK" : t == "error" ? "ERR" : t == "warning" ? "WARN" : "INFO")}] {m}\n"); _txtLog.ScrollToCaret(); }
        private void OpenUrl(string u) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = u, UseShellExecute = true }); } catch { } }
        private void ShowError(string t, string m, Exception? ex = null) { MessageBox.Show(ex != null ? $"{m}\n\n{ex.Message}" : m, t, MessageBoxButtons.OK, MessageBoxIcon.Error); Log("error", m); }
        private bool Confirm(int cost, string op) { if (cost == 0) return true; if (_tokenBalance < cost) { MessageBox.Show($"Need {cost} tokens"); return false; } return MessageBox.Show($"{op}\nCost: {cost} token(s)\n\nProceed?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes; }
        #endregion

        #region Navigation
        private void ShowLogin() { _loginPanel.Visible = true; _tabBar.Visible = _content.Visible = _logPanel.Visible = _btnLogout.Visible = false; }
        private void ShowMain() { _loginPanel.Visible = false; _tabBar.Visible = _content.Visible = _logPanel.Visible = _btnLogout.Visible = true; LayoutHeader(); SwitchTab(0); }
        private void SwitchTab(int i) { _activeTab = i; _btnTab1.BackColor = i == 0 ? ACCENT : BTN_BG; _btnTab2.BackColor = i == 1 ? ACCENT : BTN_BG; _btnTab3.BackColor = i == 2 ? ACCENT : BTN_BG; _patsTab.Visible = i == 0; _diagTab.Visible = i == 1; _freeTab.Visible = i == 2; }
        #endregion

        #region Auth
        private void LoadSession() { var e = Settings.GetString("email", ""); var t = Settings.GetString("auth_token", ""); if (!string.IsNullOrEmpty(e) && !string.IsNullOrEmpty(t)) { _userEmail = e; _authToken = t; _tokenBalance = 10; _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail; ShowMain(); Log("info", $"Logged in as {_userEmail}"); } }
        private async void DoLogin() { var e = _txtEmail.Text.Trim(); var p = _txtPassword.Text; if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p)) { MessageBox.Show("Enter email and password"); return; } await Task.Delay(200); _userEmail = e; _authToken = "t_" + DateTime.Now.Ticks; _tokenBalance = 10; Settings.SetString("email", e); Settings.SetString("auth_token", _authToken); Settings.Save(); _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail; ShowMain(); Log("success", $"Logged in as {e}"); }
        private void BtnGoogle_Click(object? s, EventArgs e) { try { using var f = new GoogleLoginForm(); if (f.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(f.AuthToken)) { _authToken = f.AuthToken; _userEmail = f.UserEmail ?? "Google User"; _tokenBalance = f.TokenCount; Settings.SetString("auth_token", _authToken); Settings.SetString("email", _userEmail); Settings.Save(); _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail; ShowMain(); Log("success", $"Logged in as {_userEmail}"); } } catch (Exception ex) { ShowError("Login Failed", ex.Message, ex); } }
        private void Logout() { _userEmail = _authToken = ""; _tokenBalance = 0; Settings.Remove("auth_token"); Settings.Save(); _txtPassword.Text = ""; _lblTokens.Text = "Tokens: --"; _lblUser.Text = ""; ShowLogin(); }
        #endregion

        #region Device
        private void BtnScan_Click(object? s, EventArgs e) { try { _cmbDevices.Items.Clear(); _deviceManager?.Dispose(); _deviceManager = new J2534DeviceManager(); _deviceManager.ScanForDevices(); var n = _deviceManager.GetDeviceNames(); if (n.Count == 0) { _cmbDevices.Items.Add("No devices found"); Log("warning", "No devices"); } else { foreach (var x in n) _cmbDevices.Items.Add(x); Log("success", $"Found {n.Count}"); } _cmbDevices.SelectedIndex = 0; } catch (Exception ex) { ShowError("Scan", "Failed", ex); } }
        private void BtnConnect_Click(object? s, EventArgs e) { if (_cmbDevices.SelectedItem == null || _deviceManager == null) return; var nm = _cmbDevices.SelectedItem.ToString()!; if (nm.Contains("No") || nm.Contains("Select")) { MessageBox.Show("Select device"); return; } try { _device = _deviceManager.ConnectToDevice(nm); _channel = _device.OpenChannel(Protocol.ISO15765, BaudRates.HS_CAN_500K, ConnectFlags.NONE); _lblStatus.Text = "Status: Connected"; _lblStatus.ForeColor = SUCCESS; Log("success", $"Connected to {nm}"); } catch (Exception ex) { ShowError("Connect", "Failed", ex); } }
        private async void BtnReadVin_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var uds = new UdsService(_channel); var vin = await Task.Run(() => uds.ReadVIN()); if (!string.IsNullOrEmpty(vin)) { _lblVin.Text = $"VIN: {vin}"; _lblVin.ForeColor = SUCCESS; _txtOutcode.Text = await Task.Run(() => uds.ReadOutcode()); Log("success", $"VIN: {vin}"); } else { _lblVin.Text = "VIN: Could not read"; _lblVin.ForeColor = DANGER; } } catch (Exception ex) { ShowError("Read", "Failed", ex); } }
        #endregion

        #region PATS
        private async void BtnProgram_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } var ic = _txtIncode.Text.Trim(); if (string.IsNullOrEmpty(ic)) { MessageBox.Show("Enter incode"); return; } try { Log("info", "Programming..."); if (await Task.Run(() => new PatsOperations(new UdsService(_channel)).ProgramKeys(ic))) { MessageBox.Show("Key programmed!\n\nRemove, insert next, click Program."); Log("success", "Programmed"); } } catch (Exception ex) { ShowError("Program", "Failed", ex); } }
        private async void BtnErase_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_KEY_ERASE, "Erase All Keys")) return; if (MessageBox.Show("ERASE ALL KEYS?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; var ic = _txtIncode.Text.Trim(); if (string.IsNullOrEmpty(ic)) { MessageBox.Show("Enter incode"); return; } try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).EraseAllKeys(ic)); MessageBox.Show("Erased!"); Log("success", "Erased"); } catch (Exception ex) { ShowError("Erase", "Failed", ex); } }
        private async void BtnParam_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ParameterReset()); MessageBox.Show("Reset done! Turn ignition OFF 15s."); Log("success", "Reset"); } catch (Exception ex) { ShowError("Reset", "Failed", ex); } }
        private async void BtnEscl_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_ESCL_INIT, "ESCL")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).InitializeESCL()); MessageBox.Show("ESCL initialized!"); Log("success", "ESCL"); } catch (Exception ex) { ShowError("ESCL", "Failed", ex); } }
        private async void BtnDisable_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).DisableBcmSecurity()); MessageBox.Show("BCM disabled"); Log("success", "BCM disabled"); } catch (Exception ex) { ShowError("BCM", "Failed", ex); } }
        #endregion

        #region Diag
        private async void BtnP160A_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_CLEAR_P160A, "P160A")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ClearP160A()); MessageBox.Show("P160A cleared!"); Log("success", "P160A"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnB10A2_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_CLEAR_B10A2, "B10A2")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ClearB10A2()); MessageBox.Show("B10A2 cleared!"); Log("success", "B10A2"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnCrush_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (!Confirm(PatsOperations.TOKEN_COST_CLEAR_CRUSH, "Crush")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ClearCrushEvent()); MessageBox.Show("Crush cleared!"); Log("success", "Crush"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnGateway_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var pats = new PatsOperations(new UdsService(_channel)); if (!await Task.Run(() => pats.DetectGateway())) { MessageBox.Show("No gateway"); return; } if (!Confirm(PatsOperations.TOKEN_COST_GATEWAY_UNLOCK, "Gateway")) return; var ic = _txtIncode.Text.Trim(); if (string.IsNullOrEmpty(ic)) { MessageBox.Show("Enter incode"); return; } await Task.Run(() => pats.UnlockGateway(ic)); MessageBox.Show("Gateway unlocked!"); Log("success", "Gateway"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnKeypad_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } var r = MessageBox.Show("YES=Read, NO=Write", "Keypad", MessageBoxButtons.YesNoCancel); if (r == DialogResult.Cancel) return; if (r == DialogResult.Yes) { if (!Confirm(PatsOperations.TOKEN_COST_KEYPAD_READ, "Read")) return; try { var c = await Task.Run(() => new PatsOperations(new UdsService(_channel)).ReadKeypadCode()); MessageBox.Show($"Code: {c}"); Log("success", $"Keypad: {c}"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } } else { var nc = Microsoft.VisualBasic.Interaction.InputBox("5-digit code:", "Keypad", ""); if (nc.Length != 5) return; if (!Confirm(PatsOperations.TOKEN_COST_KEYPAD_WRITE, "Write")) return; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).WriteKeypadCode(nc)); MessageBox.Show($"Set: {nc}"); Log("success", "Keypad set"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } } }
        private async void BtnBcm_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } if (MessageBox.Show("Reset ALL BCM settings?", "WARNING", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; if (!Confirm(PatsOperations.TOKEN_COST_BCM_FACTORY, "BCM Reset")) return; var i1 = Microsoft.VisualBasic.Interaction.InputBox("Incode 1:", "BCM", _txtIncode.Text); if (string.IsNullOrEmpty(i1)) return; var i2 = Microsoft.VisualBasic.Interaction.InputBox("Incode 2:", "BCM", ""); if (string.IsNullOrEmpty(i2)) return; var i3 = Microsoft.VisualBasic.Interaction.InputBox("Incode 3 (opt):", "BCM", ""); var codes = string.IsNullOrEmpty(i3) ? new[] { i1, i2 } : new[] { i1, i2, i3 }; try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).BcmFactoryDefaults(codes)); MessageBox.Show("BCM reset!"); Log("success", "BCM reset"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnModInfo_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var info = await Task.Run(() => new UdsService(_channel).ReadAllModuleInfo()); MessageBox.Show(info, "Module Info"); Log("success", "Module info"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        #endregion

        #region Free
        private async void BtnDtc_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new UdsService(_channel).ClearDTCs()); MessageBox.Show("DTCs cleared!"); Log("success", "DTCs"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnKam_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).ClearKAM()); MessageBox.Show("KAM cleared!"); Log("success", "KAM"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnReset_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { await Task.Run(() => new PatsOperations(new UdsService(_channel)).VehicleReset()); MessageBox.Show("Reset!"); Log("success", "Reset"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        private async void BtnReadKeys_Click(object? s, EventArgs e) { if (_channel == null) { MessageBox.Show("Connect first"); return; } try { var c = await Task.Run(() => new UdsService(_channel).ReadKeysCount()); _lblKeys.Text = c.ToString(); MessageBox.Show($"Keys: {c}"); Log("success", $"Keys: {c}"); } catch (Exception ex) { ShowError("Error", "Failed", ex); } }
        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e) { try { _channel?.Dispose(); _device?.Dispose(); _deviceManager?.Dispose(); } catch { } base.OnFormClosing(e); }
    }
}