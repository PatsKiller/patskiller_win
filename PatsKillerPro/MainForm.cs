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
    /// <summary>
    /// Main application form - V4 Professional Design (Fixed Tabs)
    /// Clean dark theme with tabbed interface and activity log
    /// </summary>
    public partial class MainForm : Form
    {
        // ============ THEME COLORS ============
        private readonly Color _colorBackground = Color.FromArgb(24, 24, 28);
        private readonly Color _colorPanel = Color.FromArgb(32, 32, 36);
        private readonly Color _colorSurface = Color.FromArgb(40, 40, 44);
        private readonly Color _colorBorder = Color.FromArgb(55, 55, 60);
        private readonly Color _colorText = Color.FromArgb(240, 240, 240);
        private readonly Color _colorTextDim = Color.FromArgb(160, 160, 165);
        private readonly Color _colorTextMuted = Color.FromArgb(100, 100, 105);
        private readonly Color _colorAccent = Color.FromArgb(59, 130, 246);
        private readonly Color _colorSuccess = Color.FromArgb(34, 197, 94);
        private readonly Color _colorWarning = Color.FromArgb(234, 179, 8);
        private readonly Color _colorDanger = Color.FromArgb(239, 68, 68);
        private readonly Color _colorButton = Color.FromArgb(50, 50, 55);
        private readonly Color _colorButtonHover = Color.FromArgb(65, 65, 70);

        // ============ STATE ============
        private string _userEmail = "";
        private string _authToken = "";
        private int _tokenBalance = 0;
        private J2534DeviceManager? _deviceManager;
        private J2534Device? _device;
        private J2534Channel? _hsCanChannel;
        private string _currentVin = "";

        // ============ UI CONTROLS ============
        private Panel _headerPanel = null!;
        private Panel _loginPanel = null!;
        private Panel _mainPanel = null!;
        private TabControl _tabControl = null!;
        private RichTextBox _txtLog = null!;
        private Label _lblTokens = null!;
        private Label _lblUser = null!;
        private Button _btnLogout = null!;
        private ComboBox _cmbDevices = null!;
        private ComboBox _cmbVehicles = null!;
        private TextBox _txtOutcode = null!;
        private TextBox _txtIncode = null!;
        private TextBox _txtEmail = null!;
        private TextBox _txtPassword = null!;
        private Button _btnLogin = null!;
        private Label _lblVin = null!;
        private Label _lblKeysCount = null!;
        private ToolTip _toolTip = null!;

        public MainForm()
        {
            InitializeComponent();
            SetupDarkTheme();
            CreateUI();
            LoadSavedCredentials();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.Text = "PatsKiller Pro 2026 (Ford & Lincoln PATS Solution)";
            this.Size = new Size(950, 750);
            this.MinimumSize = new Size(900, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = _colorBackground;
            this.ForeColor = _colorText;
            this.Font = new Font("Segoe UI", 9F);
            this.DoubleBuffered = true;
            this.ResumeLayout(false);
        }

        private void SetupDarkTheme()
        {
            try
            {
                var attribute = 20;
                var value = 1;
                DwmSetWindowAttribute(this.Handle, attribute, ref value, sizeof(int));
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private void CreateUI()
        {
            _toolTip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 500, ReshowDelay = 200, ShowAlways = true };
            CreateHeader();
            CreateLoginPanel();
            CreateMainPanel();
            ShowLoginPanel();
        }

        // ============ HEADER ============
        private void CreateHeader()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = _colorPanel
            };

            var picLogo = new PictureBox
            {
                Size = new Size(48, 48),
                Location = new Point(15, 11),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            LoadLogo(picLogo);
            _headerPanel.Controls.Add(picLogo);

            var lblTitle = new Label
            {
                Text = "PatsKiller Pro",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true,
                Location = new Point(70, 12),
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(lblTitle);

            var lblSubtitle = new Label
            {
                Text = "Ford & Lincoln PATS Solution",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextMuted,
                AutoSize = true,
                Location = new Point(72, 42),
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(lblSubtitle);

            _lblTokens = new Label
            {
                Text = "Tokens: --",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = _colorSuccess,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblTokens);

            _lblUser = new Label
            {
                Text = "Not logged in",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblUser);

            _btnLogout = CreateButton("Logout", 75, 30);
            _btnLogout.Click += BtnLogout_Click;
            _btnLogout.Visible = false;
            _headerPanel.Controls.Add(_btnLogout);

            _headerPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(_colorBorder, 1);
                e.Graphics.DrawLine(pen, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
            };

            _headerPanel.Resize += (s, e) => PositionHeaderElements();
            this.Controls.Add(_headerPanel);
        }

        private void PositionHeaderElements()
        {
            if (_lblTokens == null || _lblUser == null || _btnLogout == null) return;
            _lblTokens.Location = new Point(_headerPanel.Width - _lblTokens.Width - 100, 12);
            _lblUser.Location = new Point(_headerPanel.Width - _lblUser.Width - 100, 38);
            _btnLogout.Location = new Point(_headerPanel.Width - 90, 20);
        }

        private void LoadLogo(PictureBox pic)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceStream = assembly.GetManifestResourceStream("PatsKillerPro.Resources.logo.png");
                if (resourceStream != null) { pic.Image = Image.FromStream(resourceStream); return; }
                var paths = new[] {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo.png"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png")
                };
                foreach (var path in paths)
                    if (File.Exists(path)) { pic.Image = Image.FromFile(path); return; }
            }
            catch { }
        }

        // ============ LOGIN PANEL ============
        private void CreateLoginPanel()
        {
            _loginPanel = new Panel { Dock = DockStyle.Fill, BackColor = _colorBackground, Visible = false };

            var centerPanel = new Panel { Size = new Size(400, 420), BackColor = _colorPanel };
            centerPanel.Paint += (s, e) => {
                using var pen = new Pen(_colorBorder, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, centerPanel.Width - 1, centerPanel.Height - 1);
            };

            var lblTitle = new Label {
                Text = "Welcome to PatsKiller Pro", Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = _colorText, TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(380, 40), Location = new Point(10, 15)
            };
            centerPanel.Controls.Add(lblTitle);

            var lblSubtitle = new Label {
                Text = "Sign in to access your tokens", Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim, TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(380, 25), Location = new Point(10, 48)
            };
            centerPanel.Controls.Add(lblSubtitle);

            var btnGoogle = CreateButton("     Continue with Google", 340, 50);
            btnGoogle.Location = new Point(30, 85);
            btnGoogle.BackColor = Color.White;
            btnGoogle.ForeColor = Color.FromArgb(60, 60, 60);
            btnGoogle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnGoogle.FlatAppearance.BorderSize = 0;
            btnGoogle.Click += BtnGoogleLogin_Click;
            centerPanel.Controls.Add(btnGoogle);

            var lblOr = new Label {
                Text = "─────────  or sign in with email  ─────────",
                ForeColor = _colorTextDim, Font = new Font("Segoe UI", 8F),
                TextAlign = ContentAlignment.MiddleCenter, Size = new Size(340, 25), Location = new Point(30, 145)
            };
            centerPanel.Controls.Add(lblOr);

            centerPanel.Controls.Add(new Label { Text = "Email", ForeColor = _colorTextDim, Location = new Point(30, 180), AutoSize = true });
            _txtEmail = CreateTextBox();
            _txtEmail.Size = new Size(340, 35);
            _txtEmail.Location = new Point(30, 200);
            centerPanel.Controls.Add(_txtEmail);

            centerPanel.Controls.Add(new Label { Text = "Password", ForeColor = _colorTextDim, Location = new Point(30, 240), AutoSize = true });
            _txtPassword = CreateTextBox();
            _txtPassword.Size = new Size(340, 35);
            _txtPassword.Location = new Point(30, 260);
            _txtPassword.UseSystemPasswordChar = true;
            _txtPassword.KeyPress += (s, e) => { if (e.KeyChar == (char)Keys.Enter) BtnLogin_Click(s, e); };
            centerPanel.Controls.Add(_txtPassword);

            _btnLogin = CreateButton("Sign In", 340, 45);
            _btnLogin.Location = new Point(30, 310);
            _btnLogin.BackColor = _colorAccent;
            _btnLogin.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            _btnLogin.Click += BtnLogin_Click;
            centerPanel.Controls.Add(_btnLogin);

            var lblRegister = new Label {
                Text = "Don't have an account? Register at patskiller.com",
                ForeColor = _colorTextDim, Font = new Font("Segoe UI", 9F), Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter, Size = new Size(380, 25), Location = new Point(10, 370)
            };
            lblRegister.Click += (s, e) => OpenUrl("https://patskiller.com/register");
            lblRegister.MouseEnter += (s, e) => lblRegister.ForeColor = _colorAccent;
            lblRegister.MouseLeave += (s, e) => lblRegister.ForeColor = _colorTextDim;
            centerPanel.Controls.Add(lblRegister);

            _loginPanel.Controls.Add(centerPanel);
            _loginPanel.Resize += (s, e) => {
                centerPanel.Location = new Point(
                    (_loginPanel.ClientSize.Width - centerPanel.Width) / 2,
                    (_loginPanel.ClientSize.Height - centerPanel.Height) / 2 - 30);
            };

            this.Controls.Add(_loginPanel);
        }

        // ============ MAIN PANEL WITH TABS ============
        private void CreateMainPanel()
        {
            _mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _colorBackground,
                Padding = new Padding(10),
                Visible = false
            };

            // === LOG PANEL (add FIRST for proper docking) ===
            var logPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 140,
                BackColor = _colorSurface,
                Padding = new Padding(10)
            };

            var lblLog = new Label
            {
                Text = "📋 Activity Log",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = _colorTextDim,
                Location = new Point(10, 5),
                AutoSize = true
            };
            logPanel.Controls.Add(lblLog);

            _txtLog = new RichTextBox
            {
                Dock = DockStyle.Bottom,
                Height = 110,
                BackColor = _colorPanel,
                ForeColor = _colorText,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            logPanel.Controls.Add(_txtLog);

            logPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(_colorBorder, 1);
                e.Graphics.DrawLine(pen, 0, 0, logPanel.Width, 0);
            };

            // === TAB CONTROL (standard rendering) ===
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Padding = new Point(20, 8),
                ItemSize = new Size(150, 35),
                SizeMode = TabSizeMode.Fixed
            };

            // Create tab pages
            var tabPats = new TabPage("🔑 PATS Keys")
            {
                BackColor = _colorBackground,
                ForeColor = _colorText,
                Padding = new Padding(5)
            };

            var tabDiag = new TabPage("⚙️ Diagnostics")
            {
                BackColor = _colorBackground,
                ForeColor = _colorText,
                Padding = new Padding(5)
            };

            var tabFree = new TabPage("⚡ Free Functions")
            {
                BackColor = _colorBackground,
                ForeColor = _colorText,
                Padding = new Padding(5)
            };

            // Build tab content
            CreatePatsTab(tabPats);
            CreateDiagnosticsTab(tabDiag);
            CreateFreeTab(tabFree);

            // Add tabs to control
            _tabControl.TabPages.Add(tabPats);
            _tabControl.TabPages.Add(tabDiag);
            _tabControl.TabPages.Add(tabFree);

            // ADD CONTROLS IN CORRECT ORDER FOR DOCKING
            _mainPanel.Controls.Add(_tabControl);
            _mainPanel.Controls.Add(logPanel);

            this.Controls.Add(_mainPanel);
        }

        // ============ PATS TAB ============
        private void CreatePatsTab(TabPage tab)
        {
            var container = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, AutoScroll = true };
            int y = 10;

            // Device Section
            var grpDevice = CreateSection("J2534 Device", 10, y, 870, 70);
            grpDevice.Controls.Add(new Label { Text = "Device:", ForeColor = _colorText, Location = new Point(15, 30), AutoSize = true });

            _cmbDevices = CreateComboBox();
            _cmbDevices.Location = new Point(75, 26);
            _cmbDevices.Size = new Size(400, 28);
            grpDevice.Controls.Add(_cmbDevices);

            var btnScan = CreateButton("Scan", 80, 32);
            btnScan.Location = new Point(490, 25);
            btnScan.Click += BtnScan_Click;
            grpDevice.Controls.Add(btnScan);

            var btnConnect = CreateButton("Connect", 100, 32);
            btnConnect.Location = new Point(580, 25);
            btnConnect.BackColor = _colorSuccess;
            btnConnect.Click += BtnConnect_Click;
            grpDevice.Controls.Add(btnConnect);

            container.Controls.Add(grpDevice);
            y += 85;

            // Vehicle Section
            var grpVehicle = CreateSection("Vehicle", 10, y, 870, 95);

            var btnRead = CreateButton("🚗 Read VIN", 120, 36);
            btnRead.Location = new Point(15, 28);
            btnRead.BackColor = _colorAccent;
            btnRead.Click += BtnReadVin_Click;
            grpVehicle.Controls.Add(btnRead);

            _lblVin = new Label { Text = "VIN: Not read", ForeColor = _colorTextDim, Font = new Font("Consolas", 10F), Location = new Point(150, 35), AutoSize = true };
            grpVehicle.Controls.Add(_lblVin);

            _lblKeysCount = new Label { Text = "Keys: --", ForeColor = _colorText, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Location = new Point(700, 28), AutoSize = true };
            grpVehicle.Controls.Add(_lblKeysCount);

            grpVehicle.Controls.Add(new Label { Text = "Or select:", ForeColor = _colorTextMuted, Location = new Point(15, 68), AutoSize = true });
            _cmbVehicles = CreateComboBox();
            _cmbVehicles.Location = new Point(85, 64);
            _cmbVehicles.Size = new Size(400, 28);
            foreach (var v in VehiclePlatforms.GetAllVehicles()) _cmbVehicles.Items.Add(v.DisplayName);
            if (_cmbVehicles.Items.Count > 0) _cmbVehicles.SelectedIndex = 0;
            grpVehicle.Controls.Add(_cmbVehicles);

            container.Controls.Add(grpVehicle);
            y += 110;

            // PATS Codes Section
            var grpCodes = CreateSection("PATS Codes", 10, y, 870, 75);

            grpCodes.Controls.Add(new Label { Text = "OUTCODE:", ForeColor = _colorText, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new Point(15, 33), AutoSize = true });
            _txtOutcode = CreateTextBox();
            _txtOutcode.Location = new Point(100, 28);
            _txtOutcode.Size = new Size(180, 30);
            _txtOutcode.ReadOnly = true;
            _txtOutcode.Font = new Font("Consolas", 11F, FontStyle.Bold);
            _txtOutcode.TextAlign = HorizontalAlignment.Center;
            grpCodes.Controls.Add(_txtOutcode);

            var btnCopy = CreateButton("📋", 40, 30);
            btnCopy.Location = new Point(290, 28);
            _toolTip.SetToolTip(btnCopy, "Copy outcode");
            btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) { Clipboard.SetText(_txtOutcode.Text); AddLog("info", "Outcode copied"); } };
            grpCodes.Controls.Add(btnCopy);

            var btnGetIncode = CreateButton("🌐 Get Incode", 140, 32);
            btnGetIncode.Location = new Point(350, 27);
            btnGetIncode.BackColor = _colorWarning;
            btnGetIncode.ForeColor = Color.Black;
            btnGetIncode.Click += (s, e) => OpenUrl("https://patskiller.com/calculator");
            grpCodes.Controls.Add(btnGetIncode);

            grpCodes.Controls.Add(new Label { Text = "INCODE:", ForeColor = _colorText, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new Point(520, 33), AutoSize = true });
            _txtIncode = CreateTextBox();
            _txtIncode.Location = new Point(595, 28);
            _txtIncode.Size = new Size(180, 30);
            _txtIncode.Font = new Font("Consolas", 11F, FontStyle.Bold);
            _txtIncode.TextAlign = HorizontalAlignment.Center;
            grpCodes.Controls.Add(_txtIncode);

            container.Controls.Add(grpCodes);
            y += 90;

            // Key Operations Section
            var grpKeys = CreateSection("Key Operations", 10, y, 870, 75);

            var btnProgram = CreateButton("🔑 Program Keys", 140, 38);
            btnProgram.Location = new Point(20, 26);
            btnProgram.BackColor = _colorSuccess;
            btnProgram.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnProgram.Click += BtnProgramKeys_Click;
            grpKeys.Controls.Add(btnProgram);

            var btnErase = CreateButton("⚠ Erase All", 110, 38);
            btnErase.Location = new Point(170, 26);
            btnErase.BackColor = _colorDanger;
            btnErase.Click += BtnEraseKeys_Click;
            grpKeys.Controls.Add(btnErase);

            var btnParamReset = CreateButton("🔄 Param Reset", 130, 38);
            btnParamReset.Location = new Point(290, 26);
            btnParamReset.Click += BtnParamReset_Click;
            grpKeys.Controls.Add(btnParamReset);

            var btnEscl = CreateButton("🔒 Init ESCL", 110, 38);
            btnEscl.Location = new Point(430, 26);
            btnEscl.Click += BtnEscl_Click;
            grpKeys.Controls.Add(btnEscl);

            var btnDisable = CreateButton("🔓 Disable BCM", 120, 38);
            btnDisable.Location = new Point(550, 26);
            btnDisable.Click += BtnDisableBcm_Click;
            grpKeys.Controls.Add(btnDisable);

            container.Controls.Add(grpKeys);
            tab.Controls.Add(container);
        }

        // ============ DIAGNOSTICS TAB ============
        private void CreateDiagnosticsTab(TabPage tab)
        {
            var container = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, AutoScroll = true };
            int y = 10;

            var grpDtc = CreateSection("DTC Operations (1 Token Each)", 10, y, 870, 75);

            var btnClearP160A = CreateButton("Clear P160A", 120, 36);
            btnClearP160A.Location = new Point(20, 26);
            btnClearP160A.Click += BtnClearP160A_Click;
            grpDtc.Controls.Add(btnClearP160A);

            var btnClearB10A2 = CreateButton("Clear B10A2", 120, 36);
            btnClearB10A2.Location = new Point(150, 26);
            btnClearB10A2.Click += BtnClearB10A2_Click;
            grpDtc.Controls.Add(btnClearB10A2);

            var btnClearCrush = CreateButton("Clear Crush", 120, 36);
            btnClearCrush.Location = new Point(280, 26);
            btnClearCrush.Click += BtnClearCrush_Click;
            grpDtc.Controls.Add(btnClearCrush);

            var btnGateway = CreateButton("Unlock Gateway", 130, 36);
            btnGateway.Location = new Point(410, 26);
            btnGateway.BackColor = _colorAccent;
            btnGateway.Click += BtnGatewayUnlock_Click;
            grpDtc.Controls.Add(btnGateway);

            container.Controls.Add(grpDtc);
            y += 90;

            var grpKeypad = CreateSection("Keypad Code Operations", 10, y, 870, 75);

            var btnKeypad = CreateButton("Read/Write Keypad Code", 180, 36);
            btnKeypad.Location = new Point(20, 26);
            btnKeypad.Click += BtnKeypadCode_Click;
            grpKeypad.Controls.Add(btnKeypad);

            container.Controls.Add(grpKeypad);
            y += 90;

            var grpBcm = CreateSection("BCM Operations (Advanced)", 10, y, 870, 75);

            var btnBcmFactory = CreateButton("⚠ BCM Factory Reset", 170, 36);
            btnBcmFactory.Location = new Point(20, 26);
            btnBcmFactory.BackColor = _colorDanger;
            btnBcmFactory.Click += BtnBcmFactory_Click;
            _toolTip.SetToolTip(btnBcmFactory, "WARNING: Resets ALL BCM settings!");
            grpBcm.Controls.Add(btnBcmFactory);

            container.Controls.Add(grpBcm);
            tab.Controls.Add(container);
        }

        // ============ FREE FUNCTIONS TAB ============
        private void CreateFreeTab(TabPage tab)
        {
            var container = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, AutoScroll = true };
            int y = 10;

            var lblFree = new Label
            {
                Text = "✓ These operations are FREE - No token cost",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = _colorSuccess,
                Location = new Point(15, y),
                AutoSize = true
            };
            container.Controls.Add(lblFree);
            y += 40;

            var grpBasic = CreateSection("Basic Operations", 10, y, 870, 75);

            var btnClearDtc = CreateButton("Clear All DTCs", 140, 36);
            btnClearDtc.Location = new Point(20, 26);
            btnClearDtc.Click += BtnClearDtc_Click;
            grpBasic.Controls.Add(btnClearDtc);

            var btnClearKam = CreateButton("Clear KAM", 120, 36);
            btnClearKam.Location = new Point(170, 26);
            btnClearKam.Click += BtnClearKam_Click;
            grpBasic.Controls.Add(btnClearKam);

            var btnVehicleReset = CreateButton("Vehicle Reset", 130, 36);
            btnVehicleReset.Location = new Point(300, 26);
            btnVehicleReset.Click += BtnVehicleReset_Click;
            grpBasic.Controls.Add(btnVehicleReset);

            container.Controls.Add(grpBasic);
            y += 90;

            var grpRead = CreateSection("Read Operations", 10, y, 870, 75);

            var btnReadKeys = CreateButton("Read Keys Count", 140, 36);
            btnReadKeys.Location = new Point(20, 26);
            btnReadKeys.Click += BtnReadKeysCount_Click;
            grpRead.Controls.Add(btnReadKeys);

            var btnReadModules = CreateButton("Read Module Info", 140, 36);
            btnReadModules.Location = new Point(170, 26);
            btnReadModules.Click += BtnReadModuleInfo_Click;
            grpRead.Controls.Add(btnReadModules);

            container.Controls.Add(grpRead);
            y += 90;

            var grpResources = CreateSection("Resources & Support", 10, y, 870, 75);

            var btnTutorial = CreateButton("📖 Tutorial", 120, 36);
            btnTutorial.Location = new Point(20, 26);
            btnTutorial.Click += (s, e) => OpenUrl("https://patskiller.com/faqs");
            grpResources.Controls.Add(btnTutorial);

            var btnBuyTokens = CreateButton("💳 Buy Tokens", 130, 36);
            btnBuyTokens.Location = new Point(150, 26);
            btnBuyTokens.BackColor = _colorAccent;
            btnBuyTokens.Click += (s, e) => OpenUrl("https://patskiller.com/buy-tokens");
            grpResources.Controls.Add(btnBuyTokens);

            var btnSupport = CreateButton("📧 Support", 120, 36);
            btnSupport.Location = new Point(290, 26);
            btnSupport.Click += (s, e) => OpenUrl("https://patskiller.com/contact");
            grpResources.Controls.Add(btnSupport);

            container.Controls.Add(grpResources);
            tab.Controls.Add(container);
        }

        // ============ UI HELPERS ============
        private Panel CreateSection(string title, int x, int y, int width, int height)
        {
            var panel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = _colorSurface
            };

            panel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(_colorBorder, 1);
                using var path = GetRoundedPath(panel.ClientRectangle, 8);
                g.DrawPath(pen, path);
                using var titleBrush = new SolidBrush(_colorTextDim);
                using var titleFont = new Font("Segoe UI", 9F, FontStyle.Bold);
                g.DrawString(title, titleFont, titleBrush, 12, 6);
            };

            return panel;
        }

        private Button CreateButton(string text, int width, int height)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorButton,
                ForeColor = _colorText,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = _colorBorder;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = _colorButtonHover;
            return btn;
        }

        private TextBox CreateTextBox()
        {
            return new TextBox
            {
                BackColor = _colorPanel,
                ForeColor = _colorText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F)
            };
        }

        private ComboBox CreateComboBox()
        {
            return new ComboBox
            {
                BackColor = _colorPanel,
                ForeColor = _colorText,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
        }

        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d - 1, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d - 1, rect.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void AddLog(string type, string message)
        {
            if (_txtLog == null) return;
            if (_txtLog.InvokeRequired) { _txtLog.Invoke(new Action(() => AddLog(type, message))); return; }

            var time = DateTime.Now.ToString("HH:mm:ss");
            var prefix = type switch { "success" => "✓", "error" => "✗", "warning" => "⚠", _ => "•" };
            var color = type switch { "success" => _colorSuccess, "error" => _colorDanger, "warning" => _colorWarning, _ => _colorTextDim };

            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.SelectionColor = _colorTextMuted;
            _txtLog.AppendText($"[{time}] ");
            _txtLog.SelectionColor = color;
            _txtLog.AppendText($"{prefix} {message}\n");
            _txtLog.ScrollToCaret();
        }

        private void UpdateStatus(string message) { AddLog("info", message); Logger.Info(message); }
        private void OpenUrl(string url) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { } }

        private bool ConfirmTokenCost(int cost, string operation, string details = "")
        {
            if (cost == 0) return true;
            if (_tokenBalance < cost)
            {
                MessageBox.Show($"Not enough tokens!\n\nRequired: {cost}\nAvailable: {_tokenBalance}", "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var msg = $"Operation: {operation}\nToken cost: {cost}\nBalance: {_tokenBalance}";
            if (!string.IsNullOrEmpty(details)) msg += $"\n\n{details}";
            return MessageBox.Show(msg + "\n\nProceed?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void ShowError(string title, string message, Exception? ex = null)
        {
            var fullMsg = ex != null ? $"{message}\n\n{ex.Message}" : message;
            MessageBox.Show(fullMsg, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            AddLog("error", message);
        }

        // ============ LOGIN/LOGOUT ============
        private void ShowLoginPanel() { _loginPanel.Visible = true; _mainPanel.Visible = false; _btnLogout.Visible = false; }
        private void ShowMainPanel() { _loginPanel.Visible = false; _mainPanel.Visible = true; _btnLogout.Visible = true; PositionHeaderElements(); }

        private void LoadSavedCredentials()
        {
            var email = Settings.GetString("email", "");
            var token = Settings.GetString("auth_token", "");
            if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(token))
            {
                _userEmail = email;
                _authToken = token;
                _tokenBalance = 10;
                _lblTokens.Text = $"Tokens: {_tokenBalance}";
                _lblUser.Text = _userEmail;
                ShowMainPanel();
                AddLog("info", $"Logged in as {_userEmail}");
            }
        }

        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            var email = _txtEmail.Text.Trim();
            var password = _txtPassword.Text;
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter email and password.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnLogin.Enabled = false;
            _btnLogin.Text = "Signing in...";

            try
            {
                await Task.Delay(500);
                _userEmail = email;
                _authToken = "token_" + DateTime.Now.Ticks;
                _tokenBalance = 10;
                Settings.SetString("email", email);
                Settings.SetString("auth_token", _authToken);
                Settings.Save();
                _lblTokens.Text = $"Tokens: {_tokenBalance}";
                _lblUser.Text = _userEmail;
                ShowMainPanel();
                AddLog("success", $"Logged in as {email}");
            }
            catch (Exception ex) { ShowError("Login Failed", "Could not connect", ex); }
            finally { _btnLogin.Enabled = true; _btnLogin.Text = "Sign In"; }
        }

        private void BtnGoogleLogin_Click(object? sender, EventArgs e)
        {
            try
            {
                AddLog("info", "Opening Google login...");
                using var loginForm = new GoogleLoginForm();
                var result = loginForm.ShowDialog(this);

                if (result == DialogResult.OK && !string.IsNullOrEmpty(loginForm.AuthToken))
                {
                    _authToken = loginForm.AuthToken;
                    _userEmail = loginForm.UserEmail ?? "Google User";
                    _tokenBalance = loginForm.TokenCount;

                    Settings.SetString("auth_token", _authToken);
                    Settings.SetString("email", _userEmail);
                    Settings.Save();

                    _lblTokens.Text = $"Tokens: {_tokenBalance}";
                    _lblUser.Text = _userEmail;
                    ShowMainPanel();
                    AddLog("success", $"Logged in as {_userEmail}");
                }
            }
            catch (Exception ex) { ShowError("Login Failed", ex.Message, ex); }
        }

        private void BtnLogout_Click(object? sender, EventArgs e)
        {
            _userEmail = "";
            _authToken = "";
            _tokenBalance = 0;
            Settings.Remove("auth_token");
            Settings.Save();
            _txtPassword.Text = "";
            _lblTokens.Text = "Tokens: --";
            _lblUser.Text = "Not logged in";
            ShowLoginPanel();
            AddLog("info", "Logged out");
        }

        // ============ J2534 OPERATIONS ============
        private void BtnScan_Click(object? sender, EventArgs e)
        {
            try
            {
                AddLog("info", "Scanning for J2534 devices...");
                _cmbDevices.Items.Clear();
                _deviceManager?.Dispose();
                _deviceManager = new J2534DeviceManager();
                _deviceManager.ScanForDevices();
                var deviceNames = _deviceManager.GetDeviceNames();
                if (deviceNames.Count == 0) { _cmbDevices.Items.Add("No devices found"); AddLog("warning", "No J2534 devices found"); }
                else { foreach (var name in deviceNames) _cmbDevices.Items.Add(name); _cmbDevices.SelectedIndex = 0; AddLog("success", $"Found {deviceNames.Count} device(s)"); }
            }
            catch (Exception ex) { ShowError("Scan Error", "Failed to scan", ex); }
        }

        private void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_cmbDevices.SelectedItem == null || _cmbDevices.SelectedItem.ToString() == "No devices found" || _deviceManager == null)
            {
                MessageBox.Show("Select a device first.", "Connect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                AddLog("info", "Connecting...");
                var deviceName = _cmbDevices.SelectedItem.ToString()!;
                _device = _deviceManager.ConnectToDevice(deviceName);
                _hsCanChannel = _device.OpenChannel(Protocol.ISO15765, BaudRates.HS_CAN_500K, ConnectFlags.NONE);
                AddLog("success", $"Connected to {deviceName}");
                MessageBox.Show($"Connected to {deviceName}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ShowError("Connection Failed", "Could not connect", ex); }
        }

        private async void BtnReadVin_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect to device first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                AddLog("info", "Reading VIN...");
                var uds = new UdsService(_hsCanChannel);
                _currentVin = await Task.Run(() => uds.ReadVIN()) ?? "";
                if (!string.IsNullOrEmpty(_currentVin))
                {
                    _lblVin.Text = $"VIN: {_currentVin}";
                    _lblVin.ForeColor = _colorSuccess;
                    AddLog("info", "Reading outcode...");
                    var outcode = await Task.Run(() => uds.ReadOutcode());
                    _txtOutcode.Text = outcode;
                    AddLog("success", $"VIN: {_currentVin}");
                }
                else { _lblVin.Text = "VIN: Could not read"; _lblVin.ForeColor = _colorDanger; AddLog("warning", "Select vehicle manually"); }
            }
            catch (Exception ex) { ShowError("Read Error", "Failed to read", ex); }
        }

        // ============ PATS OPERATIONS ============
        private async void BtnProgramKeys_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                AddLog("info", "Programming key...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                var result = await Task.Run(() => pats.ProgramKeys(incode));
                if (result) { MessageBox.Show("Key programmed!\n\nRemove key, insert next, click Program again.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", "Key programmed"); }
            }
            catch (Exception ex) { ShowError("Programming Failed", "Failed", ex); }
        }

        private async void BtnEraseKeys_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_KEY_ERASE, "Erase All Keys", "⚠️ WARNING: Erases ALL keys!")) return;
            if (MessageBox.Show("Are you SURE?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                AddLog("warning", "Erasing keys...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.EraseAllKeys(incode));
                MessageBox.Show("Keys erased! Program 2+ new keys now.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                AddLog("success", "Keys erased");
            }
            catch (Exception ex) { ShowError("Erase Failed", "Failed", ex); }
        }

        private async void BtnParamReset_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                AddLog("info", "Parameter reset...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.ParameterReset());
                MessageBox.Show("Done!\n\nIgnition OFF 15s, then ON.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                AddLog("success", "Parameter reset complete");
            }
            catch (Exception ex) { ShowError("Reset Failed", "Failed", ex); }
        }

        private async void BtnEscl_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_ESCL_INIT, "Initialize ESCL")) return;
            try
            {
                AddLog("info", "Initializing ESCL...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.InitializeESCL());
                MessageBox.Show("ESCL initialized!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                AddLog("success", "ESCL done");
            }
            catch (Exception ex) { ShowError("ESCL Failed", "Failed", ex); }
        }

        private async void BtnDisableBcm_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                AddLog("info", "Disabling BCM security...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.DisableBcmSecurity());
                MessageBox.Show("BCM security disabled.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                AddLog("success", "BCM disabled");
            }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnClearDtc_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { AddLog("info", "Clearing DTCs..."); var uds = new UdsService(_hsCanChannel); await Task.Run(() => uds.ClearDTCs()); MessageBox.Show("DTCs cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", "DTCs cleared"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnClearKam_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { AddLog("info", "Clearing KAM..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearKAM()); MessageBox.Show("KAM cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", "KAM cleared"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnVehicleReset_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { AddLog("info", "Resetting vehicle..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.VehicleReset()); MessageBox.Show("Reset complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", "Reset done"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnReadKeysCount_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { AddLog("info", "Reading keys..."); var uds = new UdsService(_hsCanChannel); var count = await Task.Run(() => uds.ReadKeysCount()); _lblKeysCount.Text = $"Keys: {count}"; MessageBox.Show($"Keys programmed: {count}", "Count", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", $"Keys: {count}"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnReadModuleInfo_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { AddLog("info", "Reading modules..."); var uds = new UdsService(_hsCanChannel); var info = await Task.Run(() => uds.ReadAllModuleInfo()); MessageBox.Show(info, "Module Info", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", "Done"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnClearP160A_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_CLEAR_P160A, "Clear P160A")) return;
            try { AddLog("info", "Clearing P160A..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearP160A()); MessageBox.Show("P160A cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", "P160A cleared"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnClearB10A2_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_CLEAR_B10A2, "Clear B10A2")) return;
            try { AddLog("info", "Clearing B10A2..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearB10A2()); MessageBox.Show("B10A2 cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", "B10A2 cleared"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnClearCrush_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_CLEAR_CRUSH, "Clear Crush Event")) return;
            try { AddLog("info", "Clearing crush event..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearCrushEvent()); MessageBox.Show("Crush cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", "Crush cleared"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnGatewayUnlock_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                var hasGateway = await Task.Run(() => pats.DetectGateway());
                if (!hasGateway) { MessageBox.Show("No gateway (pre-2020 vehicle).", "Gateway", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_GATEWAY_UNLOCK, "Unlock Gateway")) return;
                var incode = _txtIncode.Text.Trim();
                if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                await Task.Run(() => pats.UnlockGateway(incode));
                MessageBox.Show("Gateway unlocked!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                AddLog("success", "Gateway unlocked");
            }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnKeypadCode_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var choice = MessageBox.Show("YES = Read, NO = Write", "Keypad Code", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel) return;
            if (choice == DialogResult.Yes)
            {
                if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_KEYPAD_READ, "Read Keypad")) return;
                try { var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); var code = await Task.Run(() => pats.ReadKeypadCode()); MessageBox.Show($"Code: {code}", "Keypad", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", $"Keypad: {code}"); }
                catch (Exception ex) { ShowError("Failed", "Failed", ex); }
            }
            else
            {
                var newCode = Microsoft.VisualBasic.Interaction.InputBox("Enter 5-digit code (1-9):", "Write Keypad", "");
                if (string.IsNullOrEmpty(newCode) || newCode.Length != 5) return;
                if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_KEYPAD_WRITE, "Write Keypad")) return;
                try { var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.WriteKeypadCode(newCode)); MessageBox.Show($"Code set: {newCode}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); AddLog("success", $"Keypad set: {newCode}"); }
                catch (Exception ex) { ShowError("Failed", "Failed", ex); }
            }
        }

        private async void BtnBcmFactory_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show("⚠️ This resets ALL BCM settings!\nScanner required after!\n\nContinue?", "⚠️ DANGER", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_BCM_FACTORY, "BCM Factory Defaults")) return;
            var incode1 = Microsoft.VisualBasic.Interaction.InputBox("Incode 1:", "BCM Factory", _txtIncode.Text);
            if (string.IsNullOrEmpty(incode1)) return;
            var incode2 = Microsoft.VisualBasic.Interaction.InputBox("Incode 2:", "BCM Factory", "");
            if (string.IsNullOrEmpty(incode2)) return;
            var incode3 = Microsoft.VisualBasic.Interaction.InputBox("Incode 3 (optional):", "BCM Factory", "");
            var incodes = string.IsNullOrEmpty(incode3) ? new[] { incode1, incode2 } : new[] { incode1, incode2, incode3 };
            try { AddLog("warning", "BCM Factory Reset..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.BcmFactoryDefaults(incodes)); MessageBox.Show("BCM reset!\nScanner adaptation required!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Warning); AddLog("success", "BCM reset complete"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _hsCanChannel?.Dispose(); _device?.Dispose(); _deviceManager?.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}