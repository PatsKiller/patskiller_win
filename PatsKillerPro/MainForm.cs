using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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
        #region Theme Colors
        private readonly Color ColorBg = Color.FromArgb(22, 22, 26);
        private readonly Color ColorSurface = Color.FromArgb(32, 32, 38);
        private readonly Color ColorCard = Color.FromArgb(40, 40, 48);
        private readonly Color ColorBorder = Color.FromArgb(60, 60, 70);
        private readonly Color ColorText = Color.FromArgb(245, 245, 245);
        private readonly Color ColorTextDim = Color.FromArgb(180, 180, 185);
        private readonly Color ColorTextMuted = Color.FromArgb(120, 120, 128);
        private readonly Color ColorAccent = Color.FromArgb(59, 130, 246);
        private readonly Color ColorSuccess = Color.FromArgb(34, 197, 94);
        private readonly Color ColorWarning = Color.FromArgb(234, 179, 8);
        private readonly Color ColorDanger = Color.FromArgb(239, 68, 68);
        private readonly Color ColorButtonBg = Color.FromArgb(55, 55, 65);
        private readonly Color ColorTabActive = Color.FromArgb(59, 130, 246);
        private readonly Color ColorTabInactive = Color.FromArgb(48, 48, 56);
        #endregion

        #region State
        private string _userEmail = "";
        private string _authToken = "";
        private int _tokenBalance = 0;
        private J2534DeviceManager? _deviceManager;
        private J2534Device? _device;
        private J2534Channel? _hsCanChannel;
        private string _currentVin = "";
        private int _activeTab = 0;
        #endregion

        #region UI Controls
        private Panel _headerPanel = null!;
        private Panel _tabBar = null!;
        private Panel _contentPanel = null!;
        private Panel _logPanel = null!;
        private Panel _loginPanel = null!;
        
        private Panel _patsPanel = null!;
        private Panel _diagPanel = null!;
        private Panel _freePanel = null!;
        
        private Button _tabPats = null!;
        private Button _tabDiag = null!;
        private Button _tabFree = null!;
        
        private RichTextBox _txtLog = null!;
        private Label _lblTokens = null!;
        private Label _lblUser = null!;
        private Label _lblStatus = null!;
        private Label _lblVin = null!;
        private Label _lblKeysCount = null!;
        private Button _btnLogout = null!;
        
        private ComboBox _cmbDevices = null!;
        private ComboBox _cmbVehicles = null!;
        private TextBox _txtOutcode = null!;
        private TextBox _txtIncode = null!;
        private TextBox _txtEmail = null!;
        private TextBox _txtPassword = null!;
        #endregion

        public MainForm()
        {
            InitializeComponent();
            ApplyDarkTitleBar();
            BuildUI();
            LoadSavedSession();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.Text = "PatsKiller Pro 2026";
            this.ClientSize = new Size(1200, 850);
            this.MinimumSize = new Size(1000, 750);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColorBg;
            this.ForeColor = ColorText;
            this.Font = new Font("Segoe UI", 9.5F);
            this.DoubleBuffered = true;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.ResumeLayout(false);
        }

        private void ApplyDarkTitleBar()
        {
            try { int v = 1; DwmSetWindowAttribute(this.Handle, 20, ref v, 4); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        #region Build UI
        private void BuildUI()
        {
            // Header
            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = ColorSurface };
            _headerPanel.Paint += (s, e) => { using var p = new Pen(ColorBorder); e.Graphics.DrawLine(p, 0, 69, Width, 69); };
            
            var logo = new Label { Text = "PK", Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.White, BackColor = ColorAccent, Size = new Size(44, 44), Location = new Point(20, 13), TextAlign = ContentAlignment.MiddleCenter };
            _headerPanel.Controls.Add(logo);
            
            var title = new Label { Text = "PatsKiller Pro", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = ColorText, AutoSize = true, Location = new Point(75, 12) };
            _headerPanel.Controls.Add(title);
            
            var subtitle = new Label { Text = "Ford & Lincoln PATS Key Programming", Font = new Font("Segoe UI", 9), ForeColor = ColorTextMuted, AutoSize = true, Location = new Point(77, 42) };
            _headerPanel.Controls.Add(subtitle);
            
            _lblTokens = new Label { Text = "Tokens: --", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = ColorSuccess, AutoSize = true };
            _headerPanel.Controls.Add(_lblTokens);
            
            _lblUser = new Label { Font = new Font("Segoe UI", 9), ForeColor = ColorTextDim, AutoSize = true };
            _headerPanel.Controls.Add(_lblUser);
            
            _btnLogout = MakeButton("Logout", 80, 32);
            _btnLogout.Click += (s, e) => DoLogout();
            _btnLogout.Visible = false;
            _headerPanel.Controls.Add(_btnLogout);
            
            _headerPanel.Resize += (s, e) => {
                _btnLogout.Location = new Point(_headerPanel.Width - 100, 19);
                _lblTokens.Location = new Point(_headerPanel.Width - 220, 15);
                _lblUser.Location = new Point(_headerPanel.Width - 220, 42);
            };
            Controls.Add(_headerPanel);

            // Tab Bar
            _tabBar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = ColorSurface, Visible = false };
            _tabBar.Paint += (s, e) => { using var p = new Pen(ColorBorder); e.Graphics.DrawLine(p, 0, 49, Width, 49); };
            
            _tabPats = MakeTabButton("PATS Key Programming", true);
            _tabPats.Location = new Point(20, 8);
            _tabPats.Click += (s, e) => SwitchTab(0);
            _tabBar.Controls.Add(_tabPats);
            
            _tabDiag = MakeTabButton("Diagnostics", false);
            _tabDiag.Location = new Point(230, 8);
            _tabDiag.Click += (s, e) => SwitchTab(1);
            _tabBar.Controls.Add(_tabDiag);
            
            _tabFree = MakeTabButton("Free Functions", false);
            _tabFree.Location = new Point(390, 8);
            _tabFree.Click += (s, e) => SwitchTab(2);
            _tabBar.Controls.Add(_tabFree);
            
            Controls.Add(_tabBar);

            // Log Panel
            _logPanel = new Panel { Dock = DockStyle.Bottom, Height = 120, BackColor = ColorSurface, Visible = false };
            _logPanel.Paint += (s, e) => { using var p = new Pen(ColorBorder); e.Graphics.DrawLine(p, 0, 0, Width, 0); };
            
            var logTitle = new Label { Text = "ACTIVITY LOG", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = ColorTextDim, Location = new Point(20, 8), AutoSize = true };
            _logPanel.Controls.Add(logTitle);
            
            _txtLog = new RichTextBox { Location = new Point(20, 30), BackColor = ColorBg, ForeColor = ColorText, Font = new Font("Consolas", 9.5F), BorderStyle = BorderStyle.None, ReadOnly = true };
            _logPanel.Controls.Add(_txtLog);
            _logPanel.Resize += (s, e) => { _txtLog.Width = _logPanel.Width - 40; _txtLog.Height = _logPanel.Height - 40; };
            Controls.Add(_logPanel);

            // Content Panel
            _contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = ColorBg, Visible = false, AutoScroll = true, Padding = new Padding(20) };
            Controls.Add(_contentPanel);

            // Build tab contents
            BuildPatsPanel();
            BuildDiagPanel();
            BuildFreePanel();
            BuildLoginPanel();

            ShowLogin();
        }

        private void BuildPatsPanel()
        {
            _patsPanel = new Panel { Location = new Point(20, 20), BackColor = ColorBg, AutoSize = true };
            
            int y = 0;
            int sectionWidth = 1100;

            // === SECTION 1: Device Connection ===
            var sec1 = MakeSection("J2534 DEVICE CONNECTION", sectionWidth, 100);
            sec1.Location = new Point(0, y);
            
            _cmbDevices = MakeComboBox(320);
            _cmbDevices.Location = new Point(25, 50);
            _cmbDevices.Items.Add("Select J2534 Device...");
            _cmbDevices.SelectedIndex = 0;
            sec1.Controls.Add(_cmbDevices);
            
            var btnScan = MakeButton("Scan Devices", 130, 38);
            btnScan.Location = new Point(360, 48);
            btnScan.Click += BtnScan_Click;
            sec1.Controls.Add(btnScan);
            
            var btnConnect = MakeButton("Connect", 110, 38);
            btnConnect.Location = new Point(505, 48);
            btnConnect.BackColor = ColorSuccess;
            btnConnect.Click += BtnConnect_Click;
            sec1.Controls.Add(btnConnect);
            
            _lblStatus = new Label { Text = "● Not Connected", Font = new Font("Segoe UI", 10), ForeColor = ColorWarning, AutoSize = true, Location = new Point(640, 55) };
            sec1.Controls.Add(_lblStatus);
            
            _patsPanel.Controls.Add(sec1);
            y += 120;

            // === SECTION 2: Vehicle Information ===
            var sec2 = MakeSection("VEHICLE INFORMATION", sectionWidth, 120);
            sec2.Location = new Point(0, y);
            
            var btnReadVin = MakeButton("Read VIN", 120, 40);
            btnReadVin.Location = new Point(25, 50);
            btnReadVin.BackColor = ColorAccent;
            btnReadVin.Click += BtnReadVin_Click;
            sec2.Controls.Add(btnReadVin);
            
            _lblVin = new Label { Text = "VIN: -----------------", Font = new Font("Consolas", 11), ForeColor = ColorTextDim, AutoSize = true, Location = new Point(160, 58) };
            sec2.Controls.Add(_lblVin);
            
            var lblSelect = new Label { Text = "Or select:", Font = new Font("Segoe UI", 9), ForeColor = ColorTextDim, AutoSize = true, Location = new Point(450, 58) };
            sec2.Controls.Add(lblSelect);
            
            _cmbVehicles = MakeComboBox(280);
            _cmbVehicles.Location = new Point(520, 52);
            foreach (var v in VehiclePlatforms.GetAllVehicles()) _cmbVehicles.Items.Add(v.DisplayName);
            if (_cmbVehicles.Items.Count > 0) _cmbVehicles.SelectedIndex = 0;
            sec2.Controls.Add(_cmbVehicles);
            
            // Keys badge
            var keysBg = new Panel { Size = new Size(100, 70), Location = new Point(980, 40), BackColor = ColorSurface };
            keysBg.Paint += (s, e) => { using var p = new Pen(ColorBorder); e.Graphics.DrawRectangle(p, 0, 0, 99, 69); };
            var keysLbl = new Label { Text = "KEYS", Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = ColorTextMuted, Size = new Size(100, 18), Location = new Point(0, 8), TextAlign = ContentAlignment.MiddleCenter };
            keysBg.Controls.Add(keysLbl);
            _lblKeysCount = new Label { Text = "--", Font = new Font("Segoe UI", 24, FontStyle.Bold), ForeColor = ColorSuccess, Size = new Size(100, 40), Location = new Point(0, 26), TextAlign = ContentAlignment.MiddleCenter };
            keysBg.Controls.Add(_lblKeysCount);
            sec2.Controls.Add(keysBg);
            
            _patsPanel.Controls.Add(sec2);
            y += 140;

            // === SECTION 3: Security Codes ===
            var sec3 = MakeSection("PATS SECURITY CODES", sectionWidth, 100);
            sec3.Location = new Point(0, y);
            
            var lblOut = new Label { Text = "OUTCODE:", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = ColorText, AutoSize = true, Location = new Point(25, 55) };
            sec3.Controls.Add(lblOut);
            
            _txtOutcode = MakeTextBox(140, 38);
            _txtOutcode.Location = new Point(110, 50);
            _txtOutcode.ReadOnly = true;
            _txtOutcode.Font = new Font("Consolas", 12, FontStyle.Bold);
            _txtOutcode.TextAlign = HorizontalAlignment.Center;
            sec3.Controls.Add(_txtOutcode);
            
            var btnCopy = MakeButton("Copy", 70, 38);
            btnCopy.Location = new Point(265, 50);
            btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) { Clipboard.SetText(_txtOutcode.Text); Log("info", "Copied"); } };
            sec3.Controls.Add(btnCopy);
            
            var btnGet = MakeButton("Get Incode Online", 160, 38);
            btnGet.Location = new Point(370, 50);
            btnGet.BackColor = ColorWarning;
            btnGet.ForeColor = Color.Black;
            btnGet.Click += (s, e) => OpenUrl("https://patskiller.com/calculator");
            sec3.Controls.Add(btnGet);
            
            var lblIn = new Label { Text = "INCODE:", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = ColorText, AutoSize = true, Location = new Point(565, 55) };
            sec3.Controls.Add(lblIn);
            
            _txtIncode = MakeTextBox(140, 38);
            _txtIncode.Location = new Point(640, 50);
            _txtIncode.Font = new Font("Consolas", 12, FontStyle.Bold);
            _txtIncode.TextAlign = HorizontalAlignment.Center;
            sec3.Controls.Add(_txtIncode);
            
            _patsPanel.Controls.Add(sec3);
            y += 120;

            // === SECTION 4: Key Operations ===
            var sec4 = MakeSection("KEY PROGRAMMING OPERATIONS", sectionWidth, 130);
            sec4.Location = new Point(0, y);
            
            var btnProg = MakeButton("Program Key", 150, 48);
            btnProg.Location = new Point(25, 50);
            btnProg.BackColor = ColorSuccess;
            btnProg.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            btnProg.Click += BtnProgramKeys_Click;
            sec4.Controls.Add(btnProg);
            
            var btnErase = MakeButton("Erase All Keys", 150, 48);
            btnErase.Location = new Point(195, 50);
            btnErase.BackColor = ColorDanger;
            btnErase.Click += BtnEraseKeys_Click;
            sec4.Controls.Add(btnErase);
            
            var btnParam = MakeButton("Parameter Reset", 155, 48);
            btnParam.Location = new Point(365, 50);
            btnParam.Click += BtnParamReset_Click;
            sec4.Controls.Add(btnParam);
            
            var btnEscl = MakeButton("Initialize ESCL", 145, 48);
            btnEscl.Location = new Point(540, 50);
            btnEscl.Click += BtnEscl_Click;
            sec4.Controls.Add(btnEscl);
            
            var btnDisable = MakeButton("Disable BCM", 135, 48);
            btnDisable.Location = new Point(705, 50);
            btnDisable.Click += BtnDisableBcm_Click;
            sec4.Controls.Add(btnDisable);
            
            var tip = new Label { Text = "TIP: Program Key = 1 token per session (unlimited keys). Insert key → Program → Remove → Repeat.", Font = new Font("Segoe UI", 9), ForeColor = ColorTextMuted, AutoSize = true, Location = new Point(25, 105) };
            sec4.Controls.Add(tip);
            
            _patsPanel.Controls.Add(sec4);

            _patsPanel.Size = new Size(sectionWidth + 40, y + 150);
            _contentPanel.Controls.Add(_patsPanel);
        }

        private void BuildDiagPanel()
        {
            _diagPanel = new Panel { Location = new Point(20, 20), BackColor = ColorBg, AutoSize = true, Visible = false };
            
            int y = 0;
            int sectionWidth = 1100;

            // DTC Operations
            var sec1 = MakeSection("DTC CLEAR OPERATIONS (1 TOKEN EACH)", sectionWidth, 100);
            sec1.Location = new Point(0, y);
            
            var btnP160A = MakeButton("Clear P160A", 140, 42);
            btnP160A.Location = new Point(25, 50);
            btnP160A.BackColor = ColorAccent;
            btnP160A.Click += BtnClearP160A_Click;
            sec1.Controls.Add(btnP160A);
            
            var btnB10A2 = MakeButton("Clear B10A2", 140, 42);
            btnB10A2.Location = new Point(185, 50);
            btnB10A2.BackColor = ColorAccent;
            btnB10A2.Click += BtnClearB10A2_Click;
            sec1.Controls.Add(btnB10A2);
            
            var btnCrush = MakeButton("Clear Crush", 130, 42);
            btnCrush.Location = new Point(345, 50);
            btnCrush.BackColor = ColorAccent;
            btnCrush.Click += BtnClearCrush_Click;
            sec1.Controls.Add(btnCrush);
            
            var btnGateway = MakeButton("Unlock Gateway", 150, 42);
            btnGateway.Location = new Point(495, 50);
            btnGateway.BackColor = ColorAccent;
            btnGateway.Click += BtnGatewayUnlock_Click;
            sec1.Controls.Add(btnGateway);
            
            _diagPanel.Controls.Add(sec1);
            y += 120;

            // Keypad
            var sec2 = MakeSection("KEYPAD CODE OPERATIONS", sectionWidth, 100);
            sec2.Location = new Point(0, y);
            
            var btnReadKp = MakeButton("Read Keypad Code", 170, 42);
            btnReadKp.Location = new Point(25, 50);
            btnReadKp.Click += BtnKeypadCode_Click;
            sec2.Controls.Add(btnReadKp);
            
            var btnWriteKp = MakeButton("Write Keypad Code", 175, 42);
            btnWriteKp.Location = new Point(215, 50);
            btnWriteKp.Click += BtnKeypadCode_Click;
            sec2.Controls.Add(btnWriteKp);
            
            var kpNote = new Label { Text = "For vehicles with door keypad entry", Font = new Font("Segoe UI", 9), ForeColor = ColorTextMuted, AutoSize = true, Location = new Point(420, 58) };
            sec2.Controls.Add(kpNote);
            
            _diagPanel.Controls.Add(sec2);
            y += 120;

            // BCM
            var sec3 = MakeSection("BCM ADVANCED OPERATIONS", sectionWidth, 100);
            sec3.Location = new Point(0, y);
            
            var btnBcm = MakeButton("BCM Factory Reset", 175, 42);
            btnBcm.Location = new Point(25, 50);
            btnBcm.BackColor = ColorDanger;
            btnBcm.Click += BtnBcmFactory_Click;
            sec3.Controls.Add(btnBcm);
            
            var bcmWarn = new Label { Text = "⚠ WARNING: Requires As-Built reprogramming with scan tool after reset!", Font = new Font("Segoe UI", 9), ForeColor = ColorDanger, AutoSize = true, Location = new Point(220, 58) };
            sec3.Controls.Add(bcmWarn);
            
            _diagPanel.Controls.Add(sec3);
            y += 120;

            // Module Info
            var sec4 = MakeSection("MODULE INFORMATION", sectionWidth, 100);
            sec4.Location = new Point(0, y);
            
            var btnMod = MakeButton("Read All Module Info", 185, 42);
            btnMod.Location = new Point(25, 50);
            btnMod.Click += BtnReadModuleInfo_Click;
            sec4.Controls.Add(btnMod);
            
            _diagPanel.Controls.Add(sec4);

            _diagPanel.Size = new Size(sectionWidth + 40, y + 120);
            _contentPanel.Controls.Add(_diagPanel);
        }

        private void BuildFreePanel()
        {
            _freePanel = new Panel { Location = new Point(20, 20), BackColor = ColorBg, AutoSize = true, Visible = false };
            
            int y = 0;
            int sectionWidth = 1100;

            // Banner
            var banner = new Panel { Size = new Size(sectionWidth, 50), Location = new Point(0, y), BackColor = Color.FromArgb(20, 34, 197, 94) };
            banner.Paint += (s, e) => { using var p = new Pen(ColorSuccess, 2); e.Graphics.DrawRectangle(p, 1, 1, sectionWidth - 3, 47); };
            var bannerLbl = new Label { Text = "✓ All operations on this tab are FREE - No token cost!", Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = ColorSuccess, AutoSize = true, Location = new Point(25, 14) };
            banner.Controls.Add(bannerLbl);
            _freePanel.Controls.Add(banner);
            y += 70;

            // Basic Ops
            var sec1 = MakeSection("BASIC VEHICLE OPERATIONS", sectionWidth, 100);
            sec1.Location = new Point(0, y);
            
            var btnDtc = MakeButton("Clear All DTCs", 150, 42);
            btnDtc.Location = new Point(25, 50);
            btnDtc.Click += BtnClearDtc_Click;
            sec1.Controls.Add(btnDtc);
            
            var btnKam = MakeButton("Clear KAM", 130, 42);
            btnKam.Location = new Point(195, 50);
            btnKam.Click += BtnClearKam_Click;
            sec1.Controls.Add(btnKam);
            
            var btnReset = MakeButton("Vehicle Reset", 140, 42);
            btnReset.Location = new Point(345, 50);
            btnReset.Click += BtnVehicleReset_Click;
            sec1.Controls.Add(btnReset);
            
            _freePanel.Controls.Add(sec1);
            y += 120;

            // Read Ops
            var sec2 = MakeSection("READ OPERATIONS", sectionWidth, 100);
            sec2.Location = new Point(0, y);
            
            var btnReadKeys = MakeButton("Read Keys Count", 160, 42);
            btnReadKeys.Location = new Point(25, 50);
            btnReadKeys.Click += BtnReadKeysCount_Click;
            sec2.Controls.Add(btnReadKeys);
            
            var btnReadMod = MakeButton("Read Module Info", 165, 42);
            btnReadMod.Location = new Point(205, 50);
            btnReadMod.Click += BtnReadModuleInfo_Click;
            sec2.Controls.Add(btnReadMod);
            
            _freePanel.Controls.Add(sec2);
            y += 120;

            // Resources
            var sec3 = MakeSection("RESOURCES & SUPPORT", sectionWidth, 100);
            sec3.Location = new Point(0, y);
            
            var btnGuide = MakeButton("User Guide", 130, 42);
            btnGuide.Location = new Point(25, 50);
            btnGuide.BackColor = ColorAccent;
            btnGuide.Click += (s, e) => OpenUrl("https://patskiller.com/faqs");
            sec3.Controls.Add(btnGuide);
            
            var btnBuy = MakeButton("Buy Tokens", 130, 42);
            btnBuy.Location = new Point(175, 50);
            btnBuy.BackColor = ColorSuccess;
            btnBuy.Click += (s, e) => OpenUrl("https://patskiller.com/buy-tokens");
            sec3.Controls.Add(btnBuy);
            
            var btnSupport = MakeButton("Contact Support", 155, 42);
            btnSupport.Location = new Point(325, 50);
            btnSupport.Click += (s, e) => OpenUrl("https://patskiller.com/contact");
            sec3.Controls.Add(btnSupport);
            
            _freePanel.Controls.Add(sec3);

            _freePanel.Size = new Size(sectionWidth + 40, y + 120);
            _contentPanel.Controls.Add(_freePanel);
        }

        private void BuildLoginPanel()
        {
            _loginPanel = new Panel { Dock = DockStyle.Fill, BackColor = ColorBg };
            
            var card = new Panel { Size = new Size(420, 480), BackColor = ColorCard };
            card.Paint += (s, e) => { using var p = new Pen(ColorBorder, 2); e.Graphics.DrawRectangle(p, 1, 1, 417, 477); };
            
            int y = 35;
            
            var lblWelcome = new Label { Text = "Welcome to PatsKiller Pro", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = ColorText, Size = new Size(400, 35), Location = new Point(10, y), TextAlign = ContentAlignment.MiddleCenter };
            card.Controls.Add(lblWelcome);
            y += 45;
            
            var lblSub = new Label { Text = "Sign in to access your tokens", Font = new Font("Segoe UI", 10), ForeColor = ColorTextMuted, Size = new Size(400, 25), Location = new Point(10, y), TextAlign = ContentAlignment.MiddleCenter };
            card.Controls.Add(lblSub);
            y += 50;
            
            var btnGoogle = MakeButton("Continue with Google", 340, 50);
            btnGoogle.Location = new Point(40, y);
            btnGoogle.BackColor = Color.White;
            btnGoogle.ForeColor = Color.FromArgb(50, 50, 50);
            btnGoogle.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            btnGoogle.Click += BtnGoogleLogin_Click;
            card.Controls.Add(btnGoogle);
            y += 70;
            
            var lblOr = new Label { Text = "─────── or sign in with email ───────", Font = new Font("Segoe UI", 9), ForeColor = ColorTextMuted, Size = new Size(340, 25), Location = new Point(40, y), TextAlign = ContentAlignment.MiddleCenter };
            card.Controls.Add(lblOr);
            y += 40;
            
            var lblEmail = new Label { Text = "Email", Font = new Font("Segoe UI", 9), ForeColor = ColorTextDim, Location = new Point(40, y), AutoSize = true };
            card.Controls.Add(lblEmail);
            y += 25;
            
            _txtEmail = MakeTextBox(340, 42);
            _txtEmail.Location = new Point(40, y);
            card.Controls.Add(_txtEmail);
            y += 55;
            
            var lblPass = new Label { Text = "Password", Font = new Font("Segoe UI", 9), ForeColor = ColorTextDim, Location = new Point(40, y), AutoSize = true };
            card.Controls.Add(lblPass);
            y += 25;
            
            _txtPassword = MakeTextBox(340, 42);
            _txtPassword.Location = new Point(40, y);
            _txtPassword.UseSystemPasswordChar = true;
            _txtPassword.KeyPress += (s, e) => { if (e.KeyChar == 13) DoLogin(); };
            card.Controls.Add(_txtPassword);
            y += 60;
            
            var btnLogin = MakeButton("Sign In", 340, 48);
            btnLogin.Location = new Point(40, y);
            btnLogin.BackColor = ColorAccent;
            btnLogin.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            btnLogin.Click += (s, e) => DoLogin();
            card.Controls.Add(btnLogin);
            
            _loginPanel.Controls.Add(card);
            _loginPanel.Resize += (s, e) => { card.Location = new Point((_loginPanel.Width - 420) / 2, (_loginPanel.Height - 480) / 2 - 20); };
            
            Controls.Add(_loginPanel);
        }
        #endregion

        #region UI Helpers
        private Panel MakeSection(string title, int w, int h)
        {
            var p = new Panel { Size = new Size(w, h), BackColor = ColorCard };
            p.Paint += (s, e) => {
                using var pen = new Pen(ColorBorder);
                e.Graphics.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                using var font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
                using var brush = new SolidBrush(ColorTextDim);
                e.Graphics.DrawString(title, font, brush, 20, 15);
                e.Graphics.DrawLine(pen, 15, 38, w - 15, 38);
            };
            return p;
        }

        private Button MakeButton(string text, int w, int h)
        {
            var b = new Button { Text = text, Size = new Size(w, h), FlatStyle = FlatStyle.Flat, BackColor = ColorButtonBg, ForeColor = ColorText, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = ColorBorder;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        private Button MakeTabButton(string text, bool active)
        {
            var b = new Button { Text = text, Size = new Size(195, 34), FlatStyle = FlatStyle.Flat, BackColor = active ? ColorTabActive : ColorTabInactive, ForeColor = ColorText, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private TextBox MakeTextBox(int w, int h)
        {
            return new TextBox { Size = new Size(w, h), BackColor = ColorSurface, ForeColor = ColorText, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
        }

        private ComboBox MakeComboBox(int w)
        {
            return new ComboBox { Size = new Size(w, 36), BackColor = ColorSurface, ForeColor = ColorText, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10), DropDownStyle = ComboBoxStyle.DropDownList };
        }

        private void Log(string type, string msg)
        {
            if (_txtLog == null || _txtLog.IsDisposed) return;
            if (_txtLog.InvokeRequired) { _txtLog.Invoke(new Action(() => Log(type, msg))); return; }
            var time = DateTime.Now.ToString("HH:mm:ss");
            var tag = type switch { "success" => "[OK]", "error" => "[ERR]", "warning" => "[WARN]", _ => "[INFO]" };
            var color = type switch { "success" => ColorSuccess, "error" => ColorDanger, "warning" => ColorWarning, _ => ColorTextDim };
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.SelectionColor = ColorTextMuted;
            _txtLog.AppendText($"[{time}] ");
            _txtLog.SelectionColor = color;
            _txtLog.AppendText($"{tag} {msg}\n");
            _txtLog.ScrollToCaret();
        }

        private void OpenUrl(string url) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { } }

        private bool ConfirmToken(int cost, string op)
        {
            if (cost == 0) return true;
            if (_tokenBalance < cost) { MessageBox.Show($"Need {cost} tokens, have {_tokenBalance}", "Insufficient", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            return MessageBox.Show($"{op}\nCost: {cost} token(s)\nBalance: {_tokenBalance}\n\nProceed?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void ShowError(string t, string m, Exception? ex = null) { MessageBox.Show(ex != null ? $"{m}\n\n{ex.Message}" : m, t, MessageBoxButtons.OK, MessageBoxIcon.Error); Log("error", m); }
        #endregion

        #region Navigation
        private void ShowLogin() { _loginPanel.Visible = true; _tabBar.Visible = false; _contentPanel.Visible = false; _logPanel.Visible = false; _btnLogout.Visible = false; }

        private void ShowMain() { _loginPanel.Visible = false; _tabBar.Visible = true; _contentPanel.Visible = true; _logPanel.Visible = true; _btnLogout.Visible = true; SwitchTab(0); }

        private void SwitchTab(int i)
        {
            _activeTab = i;
            _tabPats.BackColor = i == 0 ? ColorTabActive : ColorTabInactive;
            _tabDiag.BackColor = i == 1 ? ColorTabActive : ColorTabInactive;
            _tabFree.BackColor = i == 2 ? ColorTabActive : ColorTabInactive;
            _patsPanel.Visible = i == 0;
            _diagPanel.Visible = i == 1;
            _freePanel.Visible = i == 2;
        }
        #endregion

        #region Auth
        private void LoadSavedSession()
        {
            var email = Settings.GetString("email", "");
            var token = Settings.GetString("auth_token", "");
            if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(token))
            {
                _userEmail = email; _authToken = token; _tokenBalance = 10;
                _lblTokens.Text = $"Tokens: {_tokenBalance}";
                _lblUser.Text = _userEmail;
                ShowMain();
                Log("info", $"Logged in as {_userEmail}");
            }
        }

        private async void DoLogin()
        {
            var email = _txtEmail.Text.Trim();
            var pass = _txtPassword.Text;
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass)) { MessageBox.Show("Enter email and password.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                await Task.Delay(300);
                _userEmail = email; _authToken = "token_" + DateTime.Now.Ticks; _tokenBalance = 10;
                Settings.SetString("email", email); Settings.SetString("auth_token", _authToken); Settings.Save();
                _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail;
                ShowMain(); Log("success", $"Logged in as {email}");
            }
            catch (Exception ex) { ShowError("Login Failed", "Error", ex); }
        }

        private void BtnGoogleLogin_Click(object? sender, EventArgs e)
        {
            try
            {
                Log("info", "Opening Google login...");
                using var form = new GoogleLoginForm();
                if (form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(form.AuthToken))
                {
                    _authToken = form.AuthToken; _userEmail = form.UserEmail ?? "Google User"; _tokenBalance = form.TokenCount;
                    Settings.SetString("auth_token", _authToken); Settings.SetString("email", _userEmail); Settings.Save();
                    _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail;
                    ShowMain(); Log("success", $"Logged in as {_userEmail}");
                }
            }
            catch (Exception ex) { ShowError("Login Failed", ex.Message, ex); }
        }

        private void DoLogout()
        {
            _userEmail = ""; _authToken = ""; _tokenBalance = 0;
            Settings.Remove("auth_token"); Settings.Save();
            _txtPassword.Text = ""; _lblTokens.Text = "Tokens: --"; _lblUser.Text = "";
            ShowLogin(); Log("info", "Logged out");
        }
        #endregion

        #region Device
        private void BtnScan_Click(object? sender, EventArgs e)
        {
            try
            {
                Log("info", "Scanning..."); _cmbDevices.Items.Clear();
                _deviceManager?.Dispose(); _deviceManager = new J2534DeviceManager();
                _deviceManager.ScanForDevices();
                var names = _deviceManager.GetDeviceNames();
                if (names.Count == 0) { _cmbDevices.Items.Add("No devices found"); Log("warning", "No devices"); }
                else { foreach (var n in names) _cmbDevices.Items.Add(n); Log("success", $"Found {names.Count}"); }
                _cmbDevices.SelectedIndex = 0;
            }
            catch (Exception ex) { ShowError("Scan Error", "Failed", ex); }
        }

        private void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_cmbDevices.SelectedItem == null || _cmbDevices.SelectedItem.ToString()!.Contains("No") || _cmbDevices.SelectedItem.ToString()!.Contains("Select") || _deviceManager == null)
            { MessageBox.Show("Select device first.", "Connect", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Connecting...");
                var name = _cmbDevices.SelectedItem.ToString()!;
                _device = _deviceManager.ConnectToDevice(name);
                _hsCanChannel = _device.OpenChannel(Protocol.ISO15765, BaudRates.HS_CAN_500K, ConnectFlags.NONE);
                _lblStatus.Text = "● Connected"; _lblStatus.ForeColor = ColorSuccess;
                Log("success", $"Connected to {name}");
            }
            catch (Exception ex) { ShowError("Connect Failed", "Error", ex); }
        }

        private async void BtnReadVin_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Reading VIN...");
                var uds = new UdsService(_hsCanChannel);
                _currentVin = await Task.Run(() => uds.ReadVIN()) ?? "";
                if (!string.IsNullOrEmpty(_currentVin))
                {
                    _lblVin.Text = $"VIN: {_currentVin}"; _lblVin.ForeColor = ColorSuccess;
                    var outcode = await Task.Run(() => uds.ReadOutcode());
                    _txtOutcode.Text = outcode;
                    Log("success", $"VIN: {_currentVin}");
                }
                else { _lblVin.Text = "VIN: Could not read"; _lblVin.ForeColor = ColorDanger; Log("warning", "Could not read VIN"); }
            }
            catch (Exception ex) { ShowError("Read Error", "Failed", ex); }
        }
        #endregion

        #region PATS Operations
        private async void BtnProgramKeys_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { Log("info", "Programming..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); var r = await Task.Run(() => pats.ProgramKeys(incode)); if (r) { MessageBox.Show("Key programmed!\n\nRemove key, insert next, click Program again.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Key programmed"); } }
            catch (Exception ex) { ShowError("Programming Failed", "Failed", ex); }
        }

        private async void BtnEraseKeys_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmToken(PatsOperations.TOKEN_COST_KEY_ERASE, "Erase All Keys")) return;
            if (MessageBox.Show("ERASE ALL KEYS?\n\nThis cannot be undone!", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { Log("warning", "Erasing..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.EraseAllKeys(incode)); MessageBox.Show("All keys erased!\n\nProgram 2+ new keys now.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Warning); Log("success", "Erased"); }
            catch (Exception ex) { ShowError("Erase Failed", "Failed", ex); }
        }

        private async void BtnParamReset_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { Log("info", "Resetting..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ParameterReset()); MessageBox.Show("Done!\n\nIgnition OFF 15s then ON.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Reset complete"); }
            catch (Exception ex) { ShowError("Reset Failed", "Failed", ex); }
        }

        private async void BtnEscl_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmToken(PatsOperations.TOKEN_COST_ESCL_INIT, "Initialize ESCL")) return;
            try { Log("info", "Initializing ESCL..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.InitializeESCL()); MessageBox.Show("ESCL initialized!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "ESCL done"); }
            catch (Exception ex) { ShowError("ESCL Failed", "Failed", ex); }
        }

        private async void BtnDisableBcm_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { Log("info", "Disabling BCM..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.DisableBcmSecurity()); MessageBox.Show("BCM disabled.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "BCM disabled"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }
        #endregion

        #region Diagnostics
        private async void BtnClearP160A_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } if (!ConfirmToken(PatsOperations.TOKEN_COST_CLEAR_P160A, "Clear P160A")) return; try { Log("info", "Clearing P160A..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearP160A()); MessageBox.Show("P160A cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Cleared"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        private async void BtnClearB10A2_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } if (!ConfirmToken(PatsOperations.TOKEN_COST_CLEAR_B10A2, "Clear B10A2")) return; try { Log("info", "Clearing B10A2..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearB10A2()); MessageBox.Show("B10A2 cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Cleared"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        private async void BtnClearCrush_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } if (!ConfirmToken(PatsOperations.TOKEN_COST_CLEAR_CRUSH, "Clear Crush")) return; try { Log("info", "Clearing crush..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearCrushEvent()); MessageBox.Show("Crush cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Cleared"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        private async void BtnGatewayUnlock_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } try { var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); if (!await Task.Run(() => pats.DetectGateway())) { MessageBox.Show("No gateway (pre-2020).", "Gateway", MessageBoxButtons.OK, MessageBoxIcon.Information); return; } if (!ConfirmToken(PatsOperations.TOKEN_COST_GATEWAY_UNLOCK, "Unlock Gateway")) return; var incode = _txtIncode.Text.Trim(); if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } await Task.Run(() => pats.UnlockGateway(incode)); MessageBox.Show("Gateway unlocked!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Unlocked"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        private async void BtnKeypadCode_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } var c = MessageBox.Show("YES = Read\nNO = Write", "Keypad", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question); if (c == DialogResult.Cancel) return; if (c == DialogResult.Yes) { if (!ConfirmToken(PatsOperations.TOKEN_COST_KEYPAD_READ, "Read Keypad")) return; try { var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); var code = await Task.Run(() => pats.ReadKeypadCode()); MessageBox.Show($"Code: {code}", "Keypad", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", $"Keypad: {code}"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } } else { var nc = Microsoft.VisualBasic.Interaction.InputBox("Enter 5-digit code:", "Write Keypad", ""); if (string.IsNullOrEmpty(nc) || nc.Length != 5) return; if (!ConfirmToken(PatsOperations.TOKEN_COST_KEYPAD_WRITE, "Write Keypad")) return; try { var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.WriteKeypadCode(nc)); MessageBox.Show($"Set: {nc}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Keypad set"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } } }
        private async void BtnBcmFactory_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } if (MessageBox.Show("This resets ALL BCM settings!\nScanner required after!\n\nContinue?", "WARNING", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; if (!ConfirmToken(PatsOperations.TOKEN_COST_BCM_FACTORY, "BCM Factory Reset")) return; var i1 = Microsoft.VisualBasic.Interaction.InputBox("Incode 1:", "BCM Factory", _txtIncode.Text); if (string.IsNullOrEmpty(i1)) return; var i2 = Microsoft.VisualBasic.Interaction.InputBox("Incode 2:", "BCM Factory", ""); if (string.IsNullOrEmpty(i2)) return; var i3 = Microsoft.VisualBasic.Interaction.InputBox("Incode 3 (optional):", "BCM Factory", ""); var incodes = string.IsNullOrEmpty(i3) ? new[] { i1, i2 } : new[] { i1, i2, i3 }; try { Log("warning", "BCM Factory Reset..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.BcmFactoryDefaults(incodes)); MessageBox.Show("BCM reset!\nScanner required!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Warning); Log("success", "BCM reset"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        #endregion

        #region Free Functions
        private async void BtnClearDtc_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } try { Log("info", "Clearing DTCs..."); var uds = new UdsService(_hsCanChannel); await Task.Run(() => uds.ClearDTCs()); MessageBox.Show("DTCs cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Cleared"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        private async void BtnClearKam_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } try { Log("info", "Clearing KAM..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearKAM()); MessageBox.Show("KAM cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Cleared"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        private async void BtnVehicleReset_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } try { Log("info", "Resetting..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.VehicleReset()); MessageBox.Show("Reset complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Reset done"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        private async void BtnReadKeysCount_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } try { Log("info", "Reading keys..."); var uds = new UdsService(_hsCanChannel); var count = await Task.Run(() => uds.ReadKeysCount()); _lblKeysCount.Text = count.ToString(); MessageBox.Show($"Keys: {count}", "Count", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", $"Keys: {count}"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        private async void BtnReadModuleInfo_Click(object? sender, EventArgs e) { if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } try { Log("info", "Reading modules..."); var uds = new UdsService(_hsCanChannel); var info = await Task.Run(() => uds.ReadAllModuleInfo()); MessageBox.Show(info, "Module Info", MessageBoxButtons.OK, MessageBoxIcon.Information); Log("success", "Done"); } catch (Exception ex) { ShowError("Failed", "Failed", ex); } }
        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e) { try { _hsCanChannel?.Dispose(); _device?.Dispose(); _deviceManager?.Dispose(); } catch { } base.OnFormClosing(e); }
    }
}