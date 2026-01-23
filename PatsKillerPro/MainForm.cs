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
        private readonly Color ColorBg = Color.FromArgb(26, 26, 30);
        private readonly Color ColorSurface = Color.FromArgb(35, 35, 40);
        private readonly Color ColorCard = Color.FromArgb(42, 42, 48);
        private readonly Color ColorBorder = Color.FromArgb(58, 58, 66);
        private readonly Color ColorText = Color.FromArgb(240, 240, 240);
        private readonly Color ColorTextDim = Color.FromArgb(160, 160, 165);
        private readonly Color ColorTextMuted = Color.FromArgb(112, 112, 117);
        private readonly Color ColorAccent = Color.FromArgb(59, 130, 246);
        private readonly Color ColorSuccess = Color.FromArgb(34, 197, 94);
        private readonly Color ColorWarning = Color.FromArgb(234, 179, 8);
        private readonly Color ColorDanger = Color.FromArgb(239, 68, 68);
        private readonly Color ColorButtonBg = Color.FromArgb(54, 54, 64);
        private readonly Color ColorButtonHover = Color.FromArgb(69, 69, 80);
        private readonly Color ColorTabActive = Color.FromArgb(59, 130, 246);
        private readonly Color ColorTabInactive = Color.FromArgb(50, 50, 58);
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
        private Panel _contentArea = null!;
        private Panel _logPanel = null!;
        private Panel _loginPanel = null!;
        
        private Panel _patsContent = null!;
        private Panel _diagContent = null!;
        private Panel _freeContent = null!;
        
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
        private Button _btnLogin = null!;
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
            this.Size = new Size(1100, 800);
            this.MinimumSize = new Size(950, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColorBg;
            this.ForeColor = ColorText;
            this.Font = new Font("Segoe UI", 9F);
            this.DoubleBuffered = true;
            this.ResumeLayout(false);
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                int value = 1;
                DwmSetWindowAttribute(this.Handle, 20, ref value, sizeof(int));
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        #region Build UI
        private void BuildUI()
        {
            BuildHeader();
            BuildTabBar();
            BuildLogPanel();
            BuildContentArea();
            BuildLoginPanel();
            
            BuildPatsContent();
            BuildDiagContent();
            BuildFreeContent();
            
            ShowLogin();
        }

        private void BuildHeader()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = ColorSurface
            };

            // Logo placeholder
            var logo = new Panel
            {
                Size = new Size(50, 50),
                Location = new Point(20, 15),
                BackColor = ColorAccent
            };
            logo.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var font = new Font("Segoe UI", 20F, FontStyle.Bold);
                using var brush = new SolidBrush(Color.White);
                e.Graphics.DrawString("PK", font, brush, 5, 8);
            };
            _headerPanel.Controls.Add(logo);

            // Title
            var lblTitle = new Label
            {
                Text = "PatsKiller Pro",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = ColorText,
                AutoSize = true,
                Location = new Point(80, 15)
            };
            _headerPanel.Controls.Add(lblTitle);

            var lblSub = new Label
            {
                Text = "Ford & Lincoln PATS Key Programming",
                Font = new Font("Segoe UI", 10F),
                ForeColor = ColorTextMuted,
                AutoSize = true,
                Location = new Point(82, 48)
            };
            _headerPanel.Controls.Add(lblSub);

            // Tokens
            _lblTokens = new Label
            {
                Text = "Tokens: --",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = ColorSuccess,
                AutoSize = true
            };
            _headerPanel.Controls.Add(_lblTokens);

            // User
            _lblUser = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = ColorTextDim,
                AutoSize = true
            };
            _headerPanel.Controls.Add(_lblUser);

            // Logout
            _btnLogout = CreateButton("Logout", 80, 32);
            _btnLogout.Click += (s, e) => DoLogout();
            _btnLogout.Visible = false;
            _headerPanel.Controls.Add(_btnLogout);

            _headerPanel.Resize += (s, e) => LayoutHeader();
            _headerPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(ColorBorder);
                e.Graphics.DrawLine(pen, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
            };

            this.Controls.Add(_headerPanel);
        }

        private void LayoutHeader()
        {
            int rightEdge = _headerPanel.Width - 20;
            _btnLogout.Location = new Point(rightEdge - _btnLogout.Width, 24);
            _lblTokens.Location = new Point(rightEdge - _btnLogout.Width - _lblTokens.Width - 30, 18);
            _lblUser.Location = new Point(rightEdge - _btnLogout.Width - _lblUser.Width - 30, 45);
        }

        private void BuildTabBar()
        {
            _tabBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = ColorSurface
            };

            _tabPats = CreateTabButton("PATS Key Programming", 0);
            _tabPats.Location = new Point(20, 8);
            _tabPats.Click += (s, e) => SwitchTab(0);
            _tabBar.Controls.Add(_tabPats);

            _tabDiag = CreateTabButton("Diagnostics", 1);
            _tabDiag.Location = new Point(220, 8);
            _tabDiag.Click += (s, e) => SwitchTab(1);
            _tabBar.Controls.Add(_tabDiag);

            _tabFree = CreateTabButton("Free Functions", 2);
            _tabFree.Location = new Point(370, 8);
            _tabFree.Click += (s, e) => SwitchTab(2);
            _tabBar.Controls.Add(_tabFree);

            _tabBar.Paint += (s, e) =>
            {
                using var pen = new Pen(ColorBorder);
                e.Graphics.DrawLine(pen, 0, _tabBar.Height - 1, _tabBar.Width, _tabBar.Height - 1);
            };

            _tabBar.Visible = false;
            this.Controls.Add(_tabBar);
        }

        private void BuildLogPanel()
        {
            _logPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 130,
                BackColor = ColorSurface,
                Padding = new Padding(20, 10, 20, 10)
            };

            var lblLog = new Label
            {
                Text = "ACTIVITY LOG",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = ColorTextDim,
                Location = new Point(20, 8),
                AutoSize = true
            };
            _logPanel.Controls.Add(lblLog);

            _txtLog = new RichTextBox
            {
                Location = new Point(20, 30),
                Size = new Size(1040, 85),
                BackColor = ColorBg,
                ForeColor = ColorText,
                Font = new Font("Consolas", 9.5F),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            _logPanel.Controls.Add(_txtLog);

            _logPanel.Resize += (s, e) => _txtLog.Width = _logPanel.Width - 40;
            _logPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(ColorBorder);
                e.Graphics.DrawLine(pen, 0, 0, _logPanel.Width, 0);
            };

            _logPanel.Visible = false;
            this.Controls.Add(_logPanel);
        }

        private void BuildContentArea()
        {
            _contentArea = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBg,
                Padding = new Padding(20)
            };
            _contentArea.Visible = false;
            this.Controls.Add(_contentArea);
        }

        private void BuildLoginPanel()
        {
            _loginPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBg
            };

            var card = new Panel
            {
                Size = new Size(420, 480),
                BackColor = ColorCard
            };
            card.Paint += (s, e) => DrawRoundedBorder(e.Graphics, card.ClientRectangle, 12, ColorBorder);

            int y = 30;

            var lblWelcome = new Label
            {
                Text = "Welcome to PatsKiller Pro",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = ColorText,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(400, 35),
                Location = new Point(10, y)
            };
            card.Controls.Add(lblWelcome);
            y += 40;

            var lblSubtitle = new Label
            {
                Text = "Sign in to access your tokens",
                Font = new Font("Segoe UI", 10F),
                ForeColor = ColorTextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(400, 25),
                Location = new Point(10, y)
            };
            card.Controls.Add(lblSubtitle);
            y += 45;

            var btnGoogle = CreateButton("Continue with Google", 340, 50);
            btnGoogle.Location = new Point(40, y);
            btnGoogle.BackColor = Color.White;
            btnGoogle.ForeColor = Color.FromArgb(60, 60, 60);
            btnGoogle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnGoogle.FlatAppearance.BorderSize = 0;
            btnGoogle.Click += BtnGoogleLogin_Click;
            card.Controls.Add(btnGoogle);
            y += 70;

            var lblOr = new Label
            {
                Text = "------- or sign in with email -------",
                Font = new Font("Segoe UI", 9F),
                ForeColor = ColorTextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(340, 25),
                Location = new Point(40, y)
            };
            card.Controls.Add(lblOr);
            y += 40;

            var lblEmail = new Label { Text = "Email", ForeColor = ColorTextDim, Location = new Point(40, y), AutoSize = true };
            card.Controls.Add(lblEmail);
            y += 22;

            _txtEmail = CreateTextBox(340, 40);
            _txtEmail.Location = new Point(40, y);
            card.Controls.Add(_txtEmail);
            y += 55;

            var lblPass = new Label { Text = "Password", ForeColor = ColorTextDim, Location = new Point(40, y), AutoSize = true };
            card.Controls.Add(lblPass);
            y += 22;

            _txtPassword = CreateTextBox(340, 40);
            _txtPassword.Location = new Point(40, y);
            _txtPassword.UseSystemPasswordChar = true;
            _txtPassword.KeyPress += (s, e) => { if (e.KeyChar == (char)Keys.Enter) DoLogin(); };
            card.Controls.Add(_txtPassword);
            y += 60;

            _btnLogin = CreateButton("Sign In", 340, 48);
            _btnLogin.Location = new Point(40, y);
            _btnLogin.BackColor = ColorAccent;
            _btnLogin.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            _btnLogin.Click += (s, e) => DoLogin();
            card.Controls.Add(_btnLogin);
            y += 65;

            var lblRegister = new Label
            {
                Text = "Don't have an account? Register at patskiller.com",
                Font = new Font("Segoe UI", 9F),
                ForeColor = ColorTextDim,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(340, 25),
                Location = new Point(40, y),
                Cursor = Cursors.Hand
            };
            lblRegister.Click += (s, e) => OpenUrl("https://patskiller.com/register");
            lblRegister.MouseEnter += (s, e) => lblRegister.ForeColor = ColorAccent;
            lblRegister.MouseLeave += (s, e) => lblRegister.ForeColor = ColorTextDim;
            card.Controls.Add(lblRegister);

            _loginPanel.Controls.Add(card);
            _loginPanel.Resize += (s, e) =>
            {
                card.Location = new Point(
                    (_loginPanel.Width - card.Width) / 2,
                    (_loginPanel.Height - card.Height) / 2 - 40
                );
            };

            this.Controls.Add(_loginPanel);
        }
        #endregion

        #region Tab Contents
        private void BuildPatsContent()
        {
            _patsContent = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = ColorBg
            };

            var inner = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(1040, 600),
                BackColor = ColorBg
            };

            int y = 0;

            // Device Connection Section
            var secDevice = CreateSection("J2534 DEVICE CONNECTION", 0, y, 1020, 90);
            
            _cmbDevices = CreateComboBox(350);
            _cmbDevices.Location = new Point(20, 40);
            _cmbDevices.Items.Add("Select J2534 Device...");
            _cmbDevices.SelectedIndex = 0;
            secDevice.Controls.Add(_cmbDevices);

            var btnScan = CreateButton("Scan Devices", 120, 36);
            btnScan.Location = new Point(385, 38);
            btnScan.Click += BtnScan_Click;
            secDevice.Controls.Add(btnScan);

            var btnConnect = CreateButton("Connect", 100, 36);
            btnConnect.Location = new Point(515, 38);
            btnConnect.BackColor = ColorSuccess;
            btnConnect.Click += BtnConnect_Click;
            secDevice.Controls.Add(btnConnect);

            _lblStatus = new Label
            {
                Text = "Status: Not Connected",
                Font = new Font("Segoe UI", 9F),
                ForeColor = ColorWarning,
                Location = new Point(780, 45),
                AutoSize = true
            };
            secDevice.Controls.Add(_lblStatus);

            inner.Controls.Add(secDevice);
            y += 105;

            // Vehicle Section
            var secVehicle = CreateSection("VEHICLE INFORMATION", 0, y, 1020, 110);

            var btnReadVin = CreateButton("Read VIN", 110, 38);
            btnReadVin.Location = new Point(20, 40);
            btnReadVin.BackColor = ColorAccent;
            btnReadVin.Click += BtnReadVin_Click;
            secVehicle.Controls.Add(btnReadVin);

            _lblVin = new Label
            {
                Text = "VIN: -----------------",
                Font = new Font("Consolas", 11F),
                ForeColor = ColorTextDim,
                Location = new Point(145, 45),
                AutoSize = true
            };
            secVehicle.Controls.Add(_lblVin);

            var lblOrSelect = new Label
            {
                Text = "Or select vehicle:",
                Font = new Font("Segoe UI", 9F),
                ForeColor = ColorTextDim,
                Location = new Point(20, 78),
                AutoSize = true
            };
            secVehicle.Controls.Add(lblOrSelect);

            _cmbVehicles = CreateComboBox(300);
            _cmbVehicles.Location = new Point(135, 74);
            foreach (var v in VehiclePlatforms.GetAllVehicles())
                _cmbVehicles.Items.Add(v.DisplayName);
            if (_cmbVehicles.Items.Count > 0) _cmbVehicles.SelectedIndex = 0;
            secVehicle.Controls.Add(_cmbVehicles);

            // Keys Count Badge
            var keysPanel = new Panel
            {
                Size = new Size(130, 70),
                Location = new Point(870, 30),
                BackColor = ColorSurface
            };
            keysPanel.Paint += (s, e) => DrawRoundedBorder(e.Graphics, keysPanel.ClientRectangle, 8, ColorBorder);

            var lblKeysLabel = new Label
            {
                Text = "KEYS",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = ColorTextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(130, 20),
                Location = new Point(0, 8)
            };
            keysPanel.Controls.Add(lblKeysLabel);

            _lblKeysCount = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 26F, FontStyle.Bold),
                ForeColor = ColorSuccess,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(130, 40),
                Location = new Point(0, 25)
            };
            keysPanel.Controls.Add(_lblKeysCount);

            secVehicle.Controls.Add(keysPanel);
            inner.Controls.Add(secVehicle);
            y += 125;

            // PATS Codes Section
            var secCodes = CreateSection("PATS SECURITY CODES", 0, y, 1020, 85);

            var lblOutcode = new Label
            {
                Text = "OUTCODE:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = ColorText,
                Location = new Point(20, 45),
                AutoSize = true
            };
            secCodes.Controls.Add(lblOutcode);

            _txtOutcode = CreateTextBox(150, 36);
            _txtOutcode.Location = new Point(100, 40);
            _txtOutcode.ReadOnly = true;
            _txtOutcode.Font = new Font("Consolas", 11F, FontStyle.Bold);
            _txtOutcode.TextAlign = HorizontalAlignment.Center;
            secCodes.Controls.Add(_txtOutcode);

            var btnCopy = CreateButton("Copy", 65, 36);
            btnCopy.Location = new Point(260, 40);
            btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) { Clipboard.SetText(_txtOutcode.Text); Log("info", "Outcode copied to clipboard"); } };
            secCodes.Controls.Add(btnCopy);

            var sep1 = new Panel { Size = new Size(1, 30), Location = new Point(345, 43), BackColor = ColorBorder };
            secCodes.Controls.Add(sep1);

            var btnGetIncode = CreateButton("Get Incode Online", 140, 36);
            btnGetIncode.Location = new Point(365, 40);
            btnGetIncode.BackColor = ColorWarning;
            btnGetIncode.ForeColor = Color.Black;
            btnGetIncode.Click += (s, e) => OpenUrl("https://patskiller.com/calculator");
            secCodes.Controls.Add(btnGetIncode);

            var sep2 = new Panel { Size = new Size(1, 30), Location = new Point(525, 43), BackColor = ColorBorder };
            secCodes.Controls.Add(sep2);

            var lblIncode = new Label
            {
                Text = "INCODE:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = ColorText,
                Location = new Point(545, 45),
                AutoSize = true
            };
            secCodes.Controls.Add(lblIncode);

            _txtIncode = CreateTextBox(150, 36);
            _txtIncode.Location = new Point(615, 40);
            _txtIncode.Font = new Font("Consolas", 11F, FontStyle.Bold);
            _txtIncode.TextAlign = HorizontalAlignment.Center;
            secCodes.Controls.Add(_txtIncode);

            inner.Controls.Add(secCodes);
            y += 100;

            // Key Operations Section
            var secOps = CreateSection("KEY PROGRAMMING OPERATIONS", 0, y, 1020, 100);

            var btnProgram = CreateButton("Program Key", 130, 42);
            btnProgram.Location = new Point(20, 40);
            btnProgram.BackColor = ColorSuccess;
            btnProgram.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnProgram.Click += BtnProgramKeys_Click;
            secOps.Controls.Add(btnProgram);

            var btnErase = CreateButton("Erase All Keys", 120, 42);
            btnErase.Location = new Point(165, 40);
            btnErase.BackColor = ColorDanger;
            btnErase.Click += BtnEraseKeys_Click;
            secOps.Controls.Add(btnErase);

            var btnParam = CreateButton("Parameter Reset", 130, 42);
            btnParam.Location = new Point(300, 40);
            btnParam.Click += BtnParamReset_Click;
            secOps.Controls.Add(btnParam);

            var btnEscl = CreateButton("Initialize ESCL", 120, 42);
            btnEscl.Location = new Point(445, 40);
            btnEscl.Click += BtnEscl_Click;
            secOps.Controls.Add(btnEscl);

            var btnDisable = CreateButton("Disable BCM", 115, 42);
            btnDisable.Location = new Point(580, 40);
            btnDisable.Click += BtnDisableBcm_Click;
            secOps.Controls.Add(btnDisable);

            inner.Controls.Add(secOps);
            y += 115;

            // Tip box
            var tipPanel = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(1020, 50),
                BackColor = ColorSurface
            };
            tipPanel.Paint += (s, e) => DrawRoundedBorder(e.Graphics, tipPanel.ClientRectangle, 8, ColorBorder);

            var lblTip = new Label
            {
                Text = "TIP: Program Key costs 1 token per session (unlimited keys). Insert key, click Program, repeat for additional keys.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = ColorTextDim,
                Location = new Point(20, 15),
                AutoSize = true
            };
            tipPanel.Controls.Add(lblTip);
            inner.Controls.Add(tipPanel);

            inner.Height = y + 70;
            _patsContent.Controls.Add(inner);
            _contentArea.Controls.Add(_patsContent);
        }

        private void BuildDiagContent()
        {
            _diagContent = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = ColorBg,
                Visible = false
            };

            var inner = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(1040, 450),
                BackColor = ColorBg
            };

            int y = 0;

            // DTC Operations
            var secDtc = CreateSection("DTC CLEAR OPERATIONS (1 TOKEN EACH)", 0, y, 1020, 90);

            var btnP160A = CreateButton("Clear P160A", 120, 38);
            btnP160A.Location = new Point(20, 40);
            btnP160A.BackColor = ColorAccent;
            btnP160A.Click += BtnClearP160A_Click;
            secDtc.Controls.Add(btnP160A);

            var btnB10A2 = CreateButton("Clear B10A2", 120, 38);
            btnB10A2.Location = new Point(155, 40);
            btnB10A2.BackColor = ColorAccent;
            btnB10A2.Click += BtnClearB10A2_Click;
            secDtc.Controls.Add(btnB10A2);

            var btnCrush = CreateButton("Clear Crush", 115, 38);
            btnCrush.Location = new Point(290, 40);
            btnCrush.BackColor = ColorAccent;
            btnCrush.Click += BtnClearCrush_Click;
            secDtc.Controls.Add(btnCrush);

            var btnGateway = CreateButton("Unlock Gateway", 130, 38);
            btnGateway.Location = new Point(420, 40);
            btnGateway.BackColor = ColorAccent;
            btnGateway.Click += BtnGatewayUnlock_Click;
            secDtc.Controls.Add(btnGateway);

            inner.Controls.Add(secDtc);
            y += 105;

            // Keypad
            var secKeypad = CreateSection("KEYPAD CODE OPERATIONS", 0, y, 1020, 90);

            var btnReadKeypad = CreateButton("Read Keypad Code", 150, 38);
            btnReadKeypad.Location = new Point(20, 40);
            btnReadKeypad.Click += BtnKeypadCode_Click;
            secKeypad.Controls.Add(btnReadKeypad);

            var btnWriteKeypad = CreateButton("Write Keypad Code", 150, 38);
            btnWriteKeypad.Location = new Point(185, 40);
            btnWriteKeypad.Click += BtnKeypadCode_Click;
            secKeypad.Controls.Add(btnWriteKeypad);

            var lblKeypadNote = new Label
            {
                Text = "For vehicles with door keypad entry",
                Font = new Font("Segoe UI", 9F),
                ForeColor = ColorTextMuted,
                Location = new Point(360, 47),
                AutoSize = true
            };
            secKeypad.Controls.Add(lblKeypadNote);

            inner.Controls.Add(secKeypad);
            y += 105;

            // BCM Advanced
            var secBcm = CreateSection("BCM ADVANCED OPERATIONS", 0, y, 1020, 90);

            var btnBcmFactory = CreateButton("BCM Factory Reset", 150, 38);
            btnBcmFactory.Location = new Point(20, 40);
            btnBcmFactory.BackColor = ColorDanger;
            btnBcmFactory.Click += BtnBcmFactory_Click;
            secBcm.Controls.Add(btnBcmFactory);

            var lblBcmWarn = new Label
            {
                Text = "WARNING: Requires As-Built reprogramming with scan tool after reset!",
                Font = new Font("Segoe UI", 9F),
                ForeColor = ColorDanger,
                Location = new Point(190, 47),
                AutoSize = true
            };
            secBcm.Controls.Add(lblBcmWarn);

            inner.Controls.Add(secBcm);
            y += 105;

            // Module Info
            var secModules = CreateSection("MODULE INFORMATION", 0, y, 1020, 90);

            var btnModuleInfo = CreateButton("Read All Module Info", 160, 38);
            btnModuleInfo.Location = new Point(20, 40);
            btnModuleInfo.Click += BtnReadModuleInfo_Click;
            secModules.Controls.Add(btnModuleInfo);

            inner.Controls.Add(secModules);

            inner.Height = y + 105;
            _diagContent.Controls.Add(inner);
            _contentArea.Controls.Add(_diagContent);
        }

        private void BuildFreeContent()
        {
            _freeContent = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = ColorBg,
                Visible = false
            };

            var inner = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(1040, 450),
                BackColor = ColorBg
            };

            int y = 0;

            // Free banner
            var banner = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(1020, 50),
                BackColor = Color.FromArgb(34, 197, 94, 30)
            };
            banner.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(100, ColorSuccess));
                e.Graphics.DrawRectangle(pen, 0, 0, banner.Width - 1, banner.Height - 1);
            };

            var lblFree = new Label
            {
                Text = "All operations on this tab are FREE - No token cost!",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = ColorSuccess,
                Location = new Point(50, 13),
                AutoSize = true
            };
            banner.Controls.Add(lblFree);
            inner.Controls.Add(banner);
            y += 65;

            // Basic Operations
            var secBasic = CreateSection("BASIC VEHICLE OPERATIONS", 0, y, 1020, 90);

            var btnClearDtc = CreateButton("Clear All DTCs", 130, 38);
            btnClearDtc.Location = new Point(20, 40);
            btnClearDtc.Click += BtnClearDtc_Click;
            secBasic.Controls.Add(btnClearDtc);

            var btnClearKam = CreateButton("Clear KAM", 110, 38);
            btnClearKam.Location = new Point(165, 40);
            btnClearKam.Click += BtnClearKam_Click;
            secBasic.Controls.Add(btnClearKam);

            var btnReset = CreateButton("Vehicle Reset", 120, 38);
            btnReset.Location = new Point(290, 40);
            btnReset.Click += BtnVehicleReset_Click;
            secBasic.Controls.Add(btnReset);

            inner.Controls.Add(secBasic);
            y += 105;

            // Read Operations
            var secRead = CreateSection("READ OPERATIONS", 0, y, 1020, 90);

            var btnReadKeys = CreateButton("Read Keys Count", 140, 38);
            btnReadKeys.Location = new Point(20, 40);
            btnReadKeys.Click += BtnReadKeysCount_Click;
            secRead.Controls.Add(btnReadKeys);

            var btnReadModules = CreateButton("Read Module Info", 140, 38);
            btnReadModules.Location = new Point(175, 40);
            btnReadModules.Click += BtnReadModuleInfo_Click;
            secRead.Controls.Add(btnReadModules);

            inner.Controls.Add(secRead);
            y += 105;

            // Resources
            var secRes = CreateSection("RESOURCES & SUPPORT", 0, y, 1020, 90);

            var btnGuide = CreateButton("User Guide", 110, 38);
            btnGuide.Location = new Point(20, 40);
            btnGuide.BackColor = ColorAccent;
            btnGuide.Click += (s, e) => OpenUrl("https://patskiller.com/faqs");
            secRes.Controls.Add(btnGuide);

            var btnBuy = CreateButton("Buy Tokens", 110, 38);
            btnBuy.Location = new Point(145, 40);
            btnBuy.BackColor = ColorSuccess;
            btnBuy.Click += (s, e) => OpenUrl("https://patskiller.com/buy-tokens");
            secRes.Controls.Add(btnBuy);

            var btnSupport = CreateButton("Contact Support", 130, 38);
            btnSupport.Location = new Point(270, 40);
            btnSupport.Click += (s, e) => OpenUrl("https://patskiller.com/contact");
            secRes.Controls.Add(btnSupport);

            inner.Controls.Add(secRes);

            inner.Height = y + 105;
            _freeContent.Controls.Add(inner);
            _contentArea.Controls.Add(_freeContent);
        }
        #endregion

        #region UI Helpers
        private Panel CreateSection(string title, int x, int y, int width, int height)
        {
            var panel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = ColorCard
            };

            panel.Paint += (s, e) =>
            {
                DrawRoundedBorder(e.Graphics, panel.ClientRectangle, 10, ColorBorder);
                using var font = new Font("Segoe UI", 9F, FontStyle.Bold);
                using var brush = new SolidBrush(ColorTextDim);
                e.Graphics.DrawString(title, font, brush, 20, 12);
                using var pen = new Pen(ColorBorder);
                e.Graphics.DrawLine(pen, 15, 32, panel.Width - 15, 32);
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
                BackColor = ColorButtonBg,
                ForeColor = ColorText,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = ColorBorder;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = ColorButtonHover;
            return btn;
        }

        private Button CreateTabButton(string text, int index)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(185, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = index == 0 ? ColorTabActive : ColorTabInactive,
                ForeColor = ColorText,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ColorAccent;
            return btn;
        }

        private TextBox CreateTextBox(int width, int height)
        {
            return new TextBox
            {
                Size = new Size(width, height),
                BackColor = ColorSurface,
                ForeColor = ColorText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F)
            };
        }

        private ComboBox CreateComboBox(int width)
        {
            return new ComboBox
            {
                Size = new Size(width, 36),
                BackColor = ColorSurface,
                ForeColor = ColorText,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
        }

        private void DrawRoundedBorder(Graphics g, Rectangle rect, int radius, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(color);
            using var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d - 1, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d - 1, rect.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);
        }

        private void Log(string type, string msg)
        {
            if (_txtLog == null) return;
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

        private void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { }
        }

        private bool ConfirmToken(int cost, string op)
        {
            if (cost == 0) return true;
            if (_tokenBalance < cost)
            {
                MessageBox.Show($"Insufficient tokens.\n\nRequired: {cost}\nAvailable: {_tokenBalance}", "Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return MessageBox.Show($"Operation: {op}\nCost: {cost} token(s)\nBalance: {_tokenBalance}\n\nProceed?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void ShowError(string title, string msg, Exception? ex = null)
        {
            var full = ex != null ? $"{msg}\n\n{ex.Message}" : msg;
            MessageBox.Show(full, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log("error", msg);
        }
        #endregion

        #region Navigation
        private void ShowLogin()
        {
            _loginPanel.Visible = true;
            _tabBar.Visible = false;
            _contentArea.Visible = false;
            _logPanel.Visible = false;
            _btnLogout.Visible = false;
        }

        private void ShowMain()
        {
            _loginPanel.Visible = false;
            _tabBar.Visible = true;
            _contentArea.Visible = true;
            _logPanel.Visible = true;
            _btnLogout.Visible = true;
            LayoutHeader();
            SwitchTab(0);
        }

        private void SwitchTab(int index)
        {
            _activeTab = index;
            _tabPats.BackColor = index == 0 ? ColorTabActive : ColorTabInactive;
            _tabDiag.BackColor = index == 1 ? ColorTabActive : ColorTabInactive;
            _tabFree.BackColor = index == 2 ? ColorTabActive : ColorTabInactive;

            _patsContent.Visible = index == 0;
            _diagContent.Visible = index == 1;
            _freeContent.Visible = index == 2;
        }
        #endregion

        #region Auth
        private void LoadSavedSession()
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
                ShowMain();
                Log("info", $"Logged in as {_userEmail}");
            }
        }

        private async void DoLogin()
        {
            var email = _txtEmail.Text.Trim();
            var pass = _txtPassword.Text;
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass))
            {
                MessageBox.Show("Enter email and password.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                ShowMain();
                Log("success", $"Logged in as {email}");
            }
            catch (Exception ex) { ShowError("Login Failed", "Could not connect", ex); }
            finally { _btnLogin.Enabled = true; _btnLogin.Text = "Sign In"; }
        }

        private void BtnGoogleLogin_Click(object? sender, EventArgs e)
        {
            try
            {
                Log("info", "Opening Google login...");
                using var form = new GoogleLoginForm();
                var result = form.ShowDialog(this);
                if (result == DialogResult.OK && !string.IsNullOrEmpty(form.AuthToken))
                {
                    _authToken = form.AuthToken;
                    _userEmail = form.UserEmail ?? "Google User";
                    _tokenBalance = form.TokenCount;
                    Settings.SetString("auth_token", _authToken);
                    Settings.SetString("email", _userEmail);
                    Settings.Save();
                    _lblTokens.Text = $"Tokens: {_tokenBalance}";
                    _lblUser.Text = _userEmail;
                    ShowMain();
                    Log("success", $"Logged in as {_userEmail}");
                }
            }
            catch (Exception ex) { ShowError("Login Failed", ex.Message, ex); }
        }

        private void DoLogout()
        {
            _userEmail = "";
            _authToken = "";
            _tokenBalance = 0;
            Settings.Remove("auth_token");
            Settings.Save();
            _txtPassword.Text = "";
            _lblTokens.Text = "Tokens: --";
            _lblUser.Text = "";
            ShowLogin();
            Log("info", "Logged out");
        }
        #endregion

        #region Device Operations
        private void BtnScan_Click(object? sender, EventArgs e)
        {
            try
            {
                Log("info", "Scanning for J2534 devices...");
                _cmbDevices.Items.Clear();
                _deviceManager?.Dispose();
                _deviceManager = new J2534DeviceManager();
                _deviceManager.ScanForDevices();
                var names = _deviceManager.GetDeviceNames();
                if (names.Count == 0)
                {
                    _cmbDevices.Items.Add("No devices found");
                    Log("warning", "No J2534 devices found");
                }
                else
                {
                    foreach (var n in names) _cmbDevices.Items.Add(n);
                    Log("success", $"Found {names.Count} device(s)");
                }
                _cmbDevices.SelectedIndex = 0;
            }
            catch (Exception ex) { ShowError("Scan Error", "Failed to scan", ex); }
        }

        private void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_cmbDevices.SelectedItem == null || _cmbDevices.SelectedItem.ToString()!.Contains("No devices") || _deviceManager == null)
            {
                MessageBox.Show("Select a device first.", "Connect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                Log("info", "Connecting...");
                var name = _cmbDevices.SelectedItem.ToString()!;
                _device = _deviceManager.ConnectToDevice(name);
                _hsCanChannel = _device.OpenChannel(Protocol.ISO15765, BaudRates.HS_CAN_500K, ConnectFlags.NONE);
                _lblStatus.Text = "Status: Connected";
                _lblStatus.ForeColor = ColorSuccess;
                Log("success", $"Connected to {name}");
            }
            catch (Exception ex) { ShowError("Connection Failed", "Could not connect", ex); }
        }

        private async void BtnReadVin_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect to device first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Reading VIN...");
                var uds = new UdsService(_hsCanChannel);
                _currentVin = await Task.Run(() => uds.ReadVIN()) ?? "";
                if (!string.IsNullOrEmpty(_currentVin))
                {
                    _lblVin.Text = $"VIN: {_currentVin}";
                    _lblVin.ForeColor = ColorSuccess;
                    Log("info", "Reading outcode...");
                    var outcode = await Task.Run(() => uds.ReadOutcode());
                    _txtOutcode.Text = outcode;
                    Log("success", $"VIN: {_currentVin}");
                }
                else
                {
                    _lblVin.Text = "VIN: Could not read";
                    _lblVin.ForeColor = ColorDanger;
                    Log("warning", "Could not read VIN - select vehicle manually");
                }
            }
            catch (Exception ex) { ShowError("Read Error", "Failed to read VIN", ex); }
        }
        #endregion

        #region PATS Operations
        private async void BtnProgramKeys_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Programming key...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                var result = await Task.Run(() => pats.ProgramKeys(incode));
                if (result)
                {
                    MessageBox.Show("Key programmed!\n\nRemove key, insert next, click Program again.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Log("success", "Key programmed successfully");
                }
            }
            catch (Exception ex) { ShowError("Programming Failed", "Failed to program key", ex); }
        }

        private async void BtnEraseKeys_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmToken(PatsOperations.TOKEN_COST_KEY_ERASE, "Erase All Keys")) return;
            if (MessageBox.Show("This will ERASE ALL programmed keys!\n\nAre you sure?", "Confirm Erase", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("warning", "Erasing all keys...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.EraseAllKeys(incode));
                MessageBox.Show("All keys erased!\n\nProgram at least 2 new keys now.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("success", "All keys erased");
            }
            catch (Exception ex) { ShowError("Erase Failed", "Failed to erase keys", ex); }
        }

        private async void BtnParamReset_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Parameter reset...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.ParameterReset());
                MessageBox.Show("Parameter reset complete!\n\nTurn ignition OFF for 15 seconds, then ON.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "Parameter reset complete");
            }
            catch (Exception ex) { ShowError("Reset Failed", "Failed to reset parameters", ex); }
        }

        private async void BtnEscl_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmToken(PatsOperations.TOKEN_COST_ESCL_INIT, "Initialize ESCL")) return;
            try
            {
                Log("info", "Initializing ESCL...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.InitializeESCL());
                MessageBox.Show("ESCL initialized!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "ESCL initialized");
            }
            catch (Exception ex) { ShowError("ESCL Failed", "Failed to initialize ESCL", ex); }
        }

        private async void BtnDisableBcm_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Disabling BCM security...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.DisableBcmSecurity());
                MessageBox.Show("BCM security disabled.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "BCM security disabled");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to disable BCM", ex); }
        }
        #endregion

        #region Diagnostics
        private async void BtnClearP160A_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmToken(PatsOperations.TOKEN_COST_CLEAR_P160A, "Clear P160A")) return;
            try
            {
                Log("info", "Clearing P160A...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.ClearP160A());
                MessageBox.Show("P160A cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "P160A cleared");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to clear P160A", ex); }
        }

        private async void BtnClearB10A2_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmToken(PatsOperations.TOKEN_COST_CLEAR_B10A2, "Clear B10A2")) return;
            try
            {
                Log("info", "Clearing B10A2...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.ClearB10A2());
                MessageBox.Show("B10A2 cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "B10A2 cleared");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to clear B10A2", ex); }
        }

        private async void BtnClearCrush_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmToken(PatsOperations.TOKEN_COST_CLEAR_CRUSH, "Clear Crush Event")) return;
            try
            {
                Log("info", "Clearing crush event...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.ClearCrushEvent());
                MessageBox.Show("Crush event cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "Crush event cleared");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to clear crush event", ex); }
        }

        private async void BtnGatewayUnlock_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                var hasGateway = await Task.Run(() => pats.DetectGateway());
                if (!hasGateway)
                {
                    MessageBox.Show("No gateway module detected (pre-2020 vehicle).", "Gateway", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!ConfirmToken(PatsOperations.TOKEN_COST_GATEWAY_UNLOCK, "Unlock Gateway")) return;
                var incode = _txtIncode.Text.Trim();
                if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                await Task.Run(() => pats.UnlockGateway(incode));
                MessageBox.Show("Gateway unlocked!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "Gateway unlocked");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to unlock gateway", ex); }
        }

        private async void BtnKeypadCode_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var choice = MessageBox.Show("YES = Read Code\nNO = Write Code", "Keypad Code", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel) return;

            if (choice == DialogResult.Yes)
            {
                if (!ConfirmToken(PatsOperations.TOKEN_COST_KEYPAD_READ, "Read Keypad Code")) return;
                try
                {
                    var uds = new UdsService(_hsCanChannel);
                    var pats = new PatsOperations(uds);
                    var code = await Task.Run(() => pats.ReadKeypadCode());
                    MessageBox.Show($"Keypad Code: {code}", "Keypad", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Log("success", $"Keypad code: {code}");
                }
                catch (Exception ex) { ShowError("Failed", "Failed to read keypad code", ex); }
            }
            else
            {
                var newCode = Microsoft.VisualBasic.Interaction.InputBox("Enter 5-digit code (1-9 only):", "Write Keypad Code", "");
                if (string.IsNullOrEmpty(newCode) || newCode.Length != 5) return;
                if (!ConfirmToken(PatsOperations.TOKEN_COST_KEYPAD_WRITE, "Write Keypad Code")) return;
                try
                {
                    var uds = new UdsService(_hsCanChannel);
                    var pats = new PatsOperations(uds);
                    await Task.Run(() => pats.WriteKeypadCode(newCode));
                    MessageBox.Show($"Keypad code set to: {newCode}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Log("success", $"Keypad code set: {newCode}");
                }
                catch (Exception ex) { ShowError("Failed", "Failed to write keypad code", ex); }
            }
        }

        private async void BtnBcmFactory_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show("WARNING: This will reset ALL BCM settings!\n\nYou will need a scan tool with As-Built data to restore vehicle configuration.\n\nContinue?", "BCM Factory Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!ConfirmToken(PatsOperations.TOKEN_COST_BCM_FACTORY, "BCM Factory Reset")) return;

            var incode1 = Microsoft.VisualBasic.Interaction.InputBox("Enter Incode 1:", "BCM Factory Reset", _txtIncode.Text);
            if (string.IsNullOrEmpty(incode1)) return;
            var incode2 = Microsoft.VisualBasic.Interaction.InputBox("Enter Incode 2:", "BCM Factory Reset", "");
            if (string.IsNullOrEmpty(incode2)) return;
            var incode3 = Microsoft.VisualBasic.Interaction.InputBox("Enter Incode 3 (optional, leave empty if none):", "BCM Factory Reset", "");
            var incodes = string.IsNullOrEmpty(incode3) ? new[] { incode1, incode2 } : new[] { incode1, incode2, incode3 };

            try
            {
                Log("warning", "BCM Factory Reset in progress...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.BcmFactoryDefaults(incodes));
                MessageBox.Show("BCM reset complete!\n\nUse scan tool to restore As-Built configuration.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("success", "BCM factory reset complete");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to reset BCM", ex); }
        }
        #endregion

        #region Free Functions
        private async void BtnClearDtc_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Clearing all DTCs...");
                var uds = new UdsService(_hsCanChannel);
                await Task.Run(() => uds.ClearDTCs());
                MessageBox.Show("All DTCs cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "All DTCs cleared");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to clear DTCs", ex); }
        }

        private async void BtnClearKam_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Clearing KAM...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.ClearKAM());
                MessageBox.Show("KAM cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "KAM cleared");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to clear KAM", ex); }
        }

        private async void BtnVehicleReset_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Resetting vehicle modules...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.VehicleReset());
                MessageBox.Show("Vehicle reset complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "Vehicle reset complete");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to reset vehicle", ex); }
        }

        private async void BtnReadKeysCount_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Reading keys count...");
                var uds = new UdsService(_hsCanChannel);
                var count = await Task.Run(() => uds.ReadKeysCount());
                _lblKeysCount.Text = count.ToString();
                MessageBox.Show($"Keys programmed: {count}", "Keys Count", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", $"Keys count: {count}");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to read keys count", ex); }
        }

        private async void BtnReadModuleInfo_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                Log("info", "Reading module information...");
                var uds = new UdsService(_hsCanChannel);
                var info = await Task.Run(() => uds.ReadAllModuleInfo());
                MessageBox.Show(info, "Module Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("success", "Module info read complete");
            }
            catch (Exception ex) { ShowError("Failed", "Failed to read module info", ex); }
        }
        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                _hsCanChannel?.Dispose();
                _device?.Dispose();
                _deviceManager?.Dispose();
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}
