using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.J2534;
using PatsKillerPro.Vehicle;
using PatsKillerPro.Communication;
using PatsKillerPro.Utils;
using PatsKillerPro.Services;
using PatsKillerPro.Models;

namespace PatsKillerPro
{
    public partial class MainForm : Form
    {
        #region Fields
        
        // J2534 Components
        private J2534DeviceManager? _deviceManager;
        private J2534Device? _connectedDevice;
        private J2534Channel? _hsCanChannel;
        private J2534Channel? _msCanChannel;
        
        // State
        private VehicleInfo? _detectedVehicle;
        private bool _isConnectedToDevice = false;
        private bool _isConnectedToVehicle = false;
        private bool _incodeVerified = false;
        private bool _sessionActive = false;
        private int _sessionSecondsRemaining = 0;
        private bool _is2020Plus = false;
        private string _currentOutcode = "";
        private string _currentIncode = "";
        private string _currentVin = "";
        
        // Parameter Reset State
        private bool _paramResetActive = false;
        private int _currentParamResetStep = 0;
        private bool _skipAbs = false;
        private bool _absOnCan2 = false;
        
        // Services (for future API integration)
        private AuthService? _authService;
        private ApiClient? _apiClient;
        
        // Settings
        private bool _autoDisableAlarm = true;
        
        // Timers
        private System.Windows.Forms.Timer? _sessionTimer;
        
        // Tooltip manager
        private ToolTip _toolTip = new ToolTip();

        #endregion

        #region UI Colors

        private static class AppColors
        {
            // Header
            public static Color HeaderBackground = Color.FromArgb(30, 64, 175);     // Blue-800
            public static Color HeaderText = Color.White;
            public static Color BadgeBlue = Color.FromArgb(59, 130, 246);           // Blue-500
            
            // Session Banner
            public static Color SessionBannerBg = Color.FromArgb(34, 197, 94);      // Green-500
            public static Color SessionBannerText = Color.White;
            
            // Tokens
            public static Color PurchaseTokenBg = Color.FromArgb(34, 197, 94);      // Green-500
            public static Color PromoTokenBg = Color.FromArgb(134, 239, 172);       // Green-300 (fluorescent)
            public static Color PromoTokenText = Color.Black;
            
            // Panels
            public static Color PanelBackground = Color.White;
            public static Color FormBackground = Color.FromArgb(229, 231, 235);     // Gray-200
            
            // Input backgrounds
            public static Color OutcodeBg = Color.FromArgb(254, 249, 195);          // Yellow-100
            public static Color IncodeBg = Color.FromArgb(220, 252, 231);           // Green-100
            public static Color ParamResetBg = Color.FromArgb(219, 234, 254);       // Blue-100
            public static Color GatewayBg = Color.FromArgb(254, 243, 199);          // Amber-100
            public static Color VehicleInfoBg = Color.FromArgb(220, 252, 231);      // Green-100
            
            // Buttons
            public static Color ButtonPrimary = Color.FromArgb(59, 130, 246);       // Blue-500
            public static Color ButtonSuccess = Color.FromArgb(34, 197, 94);        // Green-500
            public static Color ButtonDanger = Color.FromArgb(239, 68, 68);         // Red-500
            public static Color ButtonWarning = Color.FromArgb(245, 158, 11);       // Amber-500
            public static Color ButtonDisabled = Color.FromArgb(209, 213, 219);     // Gray-300
            
            // Status
            public static Color Success = Color.FromArgb(22, 163, 74);              // Green-600
            public static Color Warning = Color.FromArgb(202, 138, 4);              // Yellow-600
            public static Color Error = Color.FromArgb(220, 38, 38);                // Red-600
            public static Color Info = Color.FromArgb(107, 114, 128);               // Gray-500
            
            // Activity Log
            public static Color LogBackground = Color.Black;
            public static Color LogSuccess = Color.FromArgb(74, 222, 128);          // Green-400
            public static Color LogWarning = Color.FromArgb(250, 204, 21);          // Yellow-400
            public static Color LogError = Color.FromArgb(248, 113, 113);           // Red-400
            public static Color LogInfo = Color.FromArgb(156, 163, 175);            // Gray-400
        }

        #endregion

        #region UI Components

        // Header
        private Panel? _headerPanel;
        private Label? _lblTitle;
        private Label? _lblBadge;
        private Label? _lblUserEmail;
        private Label? _lblPurchaseTokens;
        private Label? _lblPromoTokens;
        private Button? _btnAccount;
        
        // Session Banner
        private Panel? _sessionBanner;
        private Label? _lblSessionStatus;
        private Label? _lblSessionTimer;
        
        // Tab Control
        private TabControl? _tabControl;
        private TabPage? _tabPats;
        private TabPage? _tabUtility;
        private TabPage? _tabFree;
        
        // PATS Tab - Device Connection
        private ComboBox? _cmbDevices;
        private Button? _btnScan;
        private Button? _btnConnect;
        
        // PATS Tab - Vehicle
        private Button? _btnReadVehicle;
        private Panel? _vehicleInfoPanel;
        private Label? _lblVehicleInfo;
        private CheckBox? _chkKeyless;
        private CheckBox? _chkRfaOnly;
        
        // PATS Tab - Outcode/Incode
        private Panel? _outcodePanel;
        private TextBox? _txtOutcode;
        private Panel? _incodePanel;
        private TextBox? _txtIncode;
        private Button? _btnSubmitIncode;
        private Label? _lblIncodeStatus;
        
        // PATS Tab - Key Operations
        private Panel? _keyOperationsPanel;
        private Button? _btnEraseKeys;
        private Button? _btnProgramKeys;
        private Label? _lblKeyOpsFree;
        
        // PATS Tab - Parameter Reset
        private Panel? _paramResetPanel;
        private CheckBox? _chkAbsOnCan2;
        private CheckBox? _chkSkipAbs;
        private Button? _btnStartParamReset;
        private Panel? _paramResetProgressPanel;
        private Label? _lblParamResetProgress;
        private TextBox? _txtParamResetOutcode;
        private TextBox? _txtParamResetIncode;
        private Button? _btnParamResetSubmit;
        private Button? _btnParamResetCancel;
        
        // PATS Tab - Gateway
        private Panel? _gatewayPanel;
        private Button? _btnGatewayUnlock;
        
        // PATS Tab - Other Actions
        private Button? _btnInitEscl;
        private Button? _btnDisableBcm;
        
        // Activity Log
        private RichTextBox? _rtbLog;
        
        // Status Bar
        private StatusStrip? _statusBar;
        private ToolStripStatusLabel? _lblStatus;
        private ToolStripStatusLabel? _lblVersion;

        #endregion

        #region Constructor

        public MainForm()
        {
            _deviceManager = new J2534DeviceManager();
            _authService = new AuthService();
            _apiClient = new ApiClient();
            
            InitializeComponent();
            SetupTooltips();
            LoadSettings();
            
            AddLog("info", "PatsKiller Pro v2.0 started");
            AddLog("info", "Click Scan to detect J2534 devices");
        }

        #endregion

        #region Initialization

        private void InitializeComponent()
        {
            this.SuspendLayout();
            
            // Form settings
            this.Text = "PatsKiller Pro v2.0";
            this.Size = new Size(700, 900);
            this.MinimumSize = new Size(650, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = AppColors.FormBackground;
            this.Font = new Font("Segoe UI", 9F);
            this.FormClosing += MainForm_FormClosing;
            
            // Build UI components
            BuildHeader();
            BuildSessionBanner();
            BuildTabControl();
            BuildActivityLog();
            BuildStatusBar();
            
            // Initialize timer
            _sessionTimer = new System.Windows.Forms.Timer();
            _sessionTimer.Interval = 1000;
            _sessionTimer.Tick += SessionTimer_Tick;
            
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void SetupTooltips()
        {
            _toolTip.AutoPopDelay = 10000;
            _toolTip.InitialDelay = 500;
            _toolTip.ReshowDelay = 200;
            _toolTip.ShowAlways = true;
        }

        #endregion

        #region UI Building - Header

        private void BuildHeader()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 45,
                BackColor = AppColors.HeaderBackground
            };

            // Title with key icon
            _lblTitle = new Label
            {
                Text = "🔑 PatsKiller Pro v2.0",
                ForeColor = AppColors.HeaderText,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 12)
            };

            // FORD PATS badge
            _lblBadge = new Label
            {
                Text = "FORD PATS",
                ForeColor = AppColors.HeaderText,
                BackColor = AppColors.BadgeBlue,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Padding = new Padding(8, 3, 8, 3),
                AutoSize = true,
                Location = new Point(200, 14)
            };

            // User email (right side)
            _lblUserEmail = new Label
            {
                Text = "👤 john@bestratemotors.com",
                ForeColor = AppColors.HeaderText,
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(340, 14)
            };

            // Purchase tokens badge (green)
            _lblPurchaseTokens = new Label
            {
                Text = "47",
                ForeColor = AppColors.HeaderText,
                BackColor = AppColors.PurchaseTokenBg,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Padding = new Padding(10, 4, 10, 4),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(540, 10)
            };
            _toolTip.SetToolTip(_lblPurchaseTokens, "Purchased tokens");

            // Promo tokens badge (fluorescent green)
            _lblPromoTokens = new Label
            {
                Text = "12 promo",
                ForeColor = AppColors.PromoTokenText,
                BackColor = AppColors.PromoTokenBg,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Padding = new Padding(8, 4, 8, 4),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(595, 10),
                Visible = true
            };
            _toolTip.SetToolTip(_lblPromoTokens, "Promo tokens - expires 3/31/2026");

            _headerPanel.Controls.AddRange(new Control[] {
                _lblTitle, _lblBadge, _lblUserEmail, _lblPurchaseTokens, _lblPromoTokens
            });
            
            this.Controls.Add(_headerPanel);
        }

        #endregion

        #region UI Building - Session Banner

        private void BuildSessionBanner()
        {
            _sessionBanner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 35,
                BackColor = AppColors.SessionBannerBg,
                Visible = false
            };

            _lblSessionStatus = new Label
            {
                Text = "🔓 Gateway Session Active - Key programming is FREE!",
                ForeColor = AppColors.SessionBannerText,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 7)
            };

            _lblSessionTimer = new Label
            {
                Text = "10:00",
                ForeColor = AppColors.SessionBannerText,
                Font = new Font("Consolas", 12, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(600, 6)
            };

            _sessionBanner.Controls.AddRange(new Control[] {
                _lblSessionStatus, _lblSessionTimer
            });
            
            this.Controls.Add(_sessionBanner);
            _sessionBanner.BringToFront();
        }

        #endregion

        #region UI Building - Tab Control

        private void BuildTabControl()
        {
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Padding = new Point(12, 4),
                Margin = new Padding(10)
            };

            // PATS Operations Tab
            _tabPats = new TabPage
            {
                Text = "🔑 PATS Operations",
                BackColor = AppColors.PanelBackground,
                Padding = new Padding(10),
                AutoScroll = true
            };
            BuildPatsTab();
            
            // Utility Tab
            _tabUtility = new TabPage
            {
                Text = "🔧 Utility (1 token)",
                BackColor = AppColors.PanelBackground,
                Padding = new Padding(10),
                AutoScroll = true
            };
            BuildUtilityTab();
            
            // Free Functions Tab
            _tabFree = new TabPage
            {
                Text = "⚙️ Free Functions",
                BackColor = AppColors.PanelBackground,
                Padding = new Padding(10),
                AutoScroll = true
            };
            BuildFreeTab();

            _tabControl.TabPages.AddRange(new TabPage[] {
                _tabPats, _tabUtility, _tabFree
            });

            // Wrap in panel with padding
            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 5, 10, 5)
            };
            contentPanel.Controls.Add(_tabControl);
            
            this.Controls.Add(contentPanel);
        }

        private void BuildPatsTab()
        {
            int yPos = 10;
            int fullWidth = 640;

            // 1. Device Connection Section
            var devicePanel = CreateGroupPanel("J2534 Device Connection", 10, yPos, fullWidth, 80);
            
            _cmbDevices = new ComboBox
            {
                Location = new Point(10, 25),
                Size = new Size(430, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9)
            };
            _cmbDevices.Items.Add("-- Click Scan to detect J2534 devices --");
            _cmbDevices.SelectedIndex = 0;
            _cmbDevices.SelectedIndexChanged += CmbDevices_SelectedIndexChanged;
            devicePanel.Controls.Add(_cmbDevices);
            _toolTip.SetToolTip(_cmbDevices, "Select your J2534 pass-thru device");

            _btnScan = CreateButton("Scan", AppColors.ButtonPrimary, new Size(70, 28));
            _btnScan.Location = new Point(450, 25);
            _btnScan.Click += BtnScan_Click;
            devicePanel.Controls.Add(_btnScan);
            _toolTip.SetToolTip(_btnScan, "Scan for installed J2534 devices");

            _btnConnect = CreateButton("Connect", AppColors.ButtonWarning, new Size(100, 28));
            _btnConnect.Location = new Point(530, 25);
            _btnConnect.Enabled = false;
            _btnConnect.Click += BtnConnect_Click;
            devicePanel.Controls.Add(_btnConnect);
            _toolTip.SetToolTip(_btnConnect, "Connect to selected device");

            _tabPats.Controls.Add(devicePanel);
            yPos += 90;

            // 2. Read Vehicle Button
            _btnReadVehicle = CreateButton("🔍 Read Vehicle (Auto-Detect from VIN)", AppColors.ButtonPrimary, new Size(fullWidth, 40));
            _btnReadVehicle.Location = new Point(10, yPos);
            _btnReadVehicle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            _btnReadVehicle.Enabled = false;
            _btnReadVehicle.Click += BtnReadVehicle_Click;
            _tabPats.Controls.Add(_btnReadVehicle);
            _toolTip.SetToolTip(_btnReadVehicle, "Read VIN and auto-detect vehicle - FREE");
            yPos += 50;

            // 3. Vehicle Info Panel (hidden initially)
            _vehicleInfoPanel = CreateGroupPanel("✅ Connected Vehicle", 10, yPos, fullWidth, 90);
            _vehicleInfoPanel.BackColor = AppColors.VehicleInfoBg;
            _vehicleInfoPanel.Visible = false;
            
            _lblVehicleInfo = new Label
            {
                Location = new Point(10, 22),
                Size = new Size(620, 60),
                Font = new Font("Consolas", 9)
            };
            _vehicleInfoPanel.Controls.Add(_lblVehicleInfo);
            
            // Checkboxes
            _chkKeyless = new CheckBox
            {
                Text = "Keyless",
                Location = new Point(450, 22),
                AutoSize = true
            };
            _vehicleInfoPanel.Controls.Add(_chkKeyless);
            
            _chkRfaOnly = new CheckBox
            {
                Text = "RFA Only",
                Location = new Point(530, 22),
                AutoSize = true
            };
            _vehicleInfoPanel.Controls.Add(_chkRfaOnly);
            
            _tabPats.Controls.Add(_vehicleInfoPanel);
            yPos += 100;

            // 4. Outcode Panel (hidden initially)
            _outcodePanel = CreateGroupPanel("OUTCODE", 10, yPos, fullWidth, 90);
            _outcodePanel.BackColor = AppColors.OutcodeBg;
            _outcodePanel.Visible = false;
            
            var btnCopyOutcode = new LinkLabel
            {
                Text = "📋 Copy",
                Location = new Point(580, 5),
                AutoSize = true
            };
            btnCopyOutcode.Click += BtnCopyOutcode_Click;
            _outcodePanel.Controls.Add(btnCopyOutcode);
            
            _txtOutcode = new TextBox
            {
                Location = new Point(10, 28),
                Size = new Size(620, 32),
                Font = new Font("Consolas", 14, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                ReadOnly = true,
                BackColor = Color.White
            };
            _outcodePanel.Controls.Add(_txtOutcode);
            
            var lnkCalculator = new LinkLabel
            {
                Text = "🔗 Get Incode at patskiller.com/calculator",
                Location = new Point(200, 65),
                AutoSize = true
            };
            lnkCalculator.Click += (s, e) => OpenUrl("https://patskiller.com/calculator");
            _outcodePanel.Controls.Add(lnkCalculator);
            
            _tabPats.Controls.Add(_outcodePanel);
            yPos += 100;

            // 5. Incode Panel with SUBMIT button (hidden initially)
            _incodePanel = CreateGroupPanel("INCODE", 10, yPos, fullWidth, 75);
            _incodePanel.BackColor = AppColors.IncodeBg;
            _incodePanel.Visible = false;
            
            _txtIncode = new TextBox
            {
                Location = new Point(10, 28),
                Size = new Size(510, 32),
                Font = new Font("Consolas", 14, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                CharacterCasing = CharacterCasing.Upper,
                BackColor = Color.White
            };
            _txtIncode.TextChanged += TxtIncode_TextChanged;
            _incodePanel.Controls.Add(_txtIncode);
            
            _btnSubmitIncode = CreateButton("Submit", AppColors.ButtonPrimary, new Size(100, 32));
            _btnSubmitIncode.Location = new Point(530, 28);
            _btnSubmitIncode.Enabled = false;
            _btnSubmitIncode.Click += BtnSubmitIncode_Click;
            _incodePanel.Controls.Add(_btnSubmitIncode);
            _toolTip.SetToolTip(_btnSubmitIncode, "Verify incode to unlock operations");
            
            _lblIncodeStatus = new Label
            {
                Text = "",
                ForeColor = AppColors.Success,
                Font = new Font("Segoe UI", 8),
                Location = new Point(10, 62),
                AutoSize = true,
                Visible = false
            };
            _incodePanel.Controls.Add(_lblIncodeStatus);
            
            _tabPats.Controls.Add(_incodePanel);
            yPos += 85;

            // 6. Key Operations Panel (hidden initially)
            _keyOperationsPanel = new Panel
            {
                Location = new Point(10, yPos),
                Size = new Size(fullWidth, 100),
                Visible = false
            };
            
            var lblKeyOps = new Label
            {
                Text = "🔑 Key Operations",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(0, 0),
                AutoSize = true
            };
            _keyOperationsPanel.Controls.Add(lblKeyOps);
            
            _lblKeyOpsFree = new Label
            {
                Text = "FREE during session!",
                ForeColor = AppColors.Success,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Location = new Point(140, 2),
                AutoSize = true,
                Visible = false
            };
            _keyOperationsPanel.Controls.Add(_lblKeyOpsFree);
            
            _btnEraseKeys = CreateButton("🗑️ Erase All Keys\n1 token", AppColors.ButtonDanger, new Size(310, 60));
            _btnEraseKeys.Location = new Point(0, 30);
            _btnEraseKeys.Enabled = false;
            _btnEraseKeys.Click += BtnEraseKeys_Click;
            _keyOperationsPanel.Controls.Add(_btnEraseKeys);
            _toolTip.SetToolTip(_btnEraseKeys, "⚠️ Erases ALL programmed keys from BCM");
            
            _btnProgramKeys = CreateButton("🔑 Program New Keys\n1 token", AppColors.ButtonSuccess, new Size(310, 60));
            _btnProgramKeys.Location = new Point(320, 30);
            _btnProgramKeys.Enabled = false;
            _btnProgramKeys.Click += BtnProgramKeys_Click;
            _keyOperationsPanel.Controls.Add(_btnProgramKeys);
            _toolTip.SetToolTip(_btnProgramKeys, "Program new transponder keys to vehicle");
            
            _tabPats.Controls.Add(_keyOperationsPanel);
            yPos += 110;

            // 7. Parameter Reset Panel (hidden initially)
            _paramResetPanel = CreateGroupPanel("🔄 Parameter Reset", 10, yPos, fullWidth, 150);
            _paramResetPanel.BackColor = AppColors.ParamResetBg;
            _paramResetPanel.Visible = false;
            
            // Description
            var lblParamDesc = new Label
            {
                Text = "Iterative process: Each module (BCM, ABS, PCM) requires its own outcode/incode cycle.",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(30, 64, 175),
                Location = new Point(10, 22),
                Size = new Size(620, 18)
            };
            _paramResetPanel.Controls.Add(lblParamDesc);
            
            // Troubleshooting options
            var optionsPanel = new Panel
            {
                Location = new Point(10, 45),
                Size = new Size(620, 55),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            
            var lblOptions = new Label
            {
                Text = "Troubleshooting Options:",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = AppColors.Info,
                Location = new Point(5, 5),
                AutoSize = true
            };
            optionsPanel.Controls.Add(lblOptions);
            
            _chkAbsOnCan2 = new CheckBox
            {
                Text = "ABS on CAN 2",
                Location = new Point(5, 28),
                AutoSize = true
            };
            _chkAbsOnCan2.CheckedChanged += ChkAbsOnCan2_CheckedChanged;
            optionsPanel.Controls.Add(_chkAbsOnCan2);
            _toolTip.SetToolTip(_chkAbsOnCan2, "Use if ABS communication fails - routes to MS-CAN");
            
            _chkSkipAbs = new CheckBox
            {
                Text = "Skip ABS (2 modules only)",
                Location = new Point(130, 28),
                AutoSize = true
            };
            _chkSkipAbs.CheckedChanged += ChkSkipAbs_CheckedChanged;
            optionsPanel.Controls.Add(_chkSkipAbs);
            _toolTip.SetToolTip(_chkSkipAbs, "USA vehicles where ABS doesn't participate in PATS");
            
            var lblSaves = new Label
            {
                Text = "Saves tokens!",
                ForeColor = AppColors.Success,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Location = new Point(310, 30),
                AutoSize = true
            };
            optionsPanel.Controls.Add(lblSaves);
            
            _paramResetPanel.Controls.Add(optionsPanel);
            
            _btnStartParamReset = CreateButton("🔄 Start Parameter Reset (3-4 tokens)", AppColors.ButtonPrimary, new Size(620, 40));
            _btnStartParamReset.Location = new Point(10, 105);
            _btnStartParamReset.Enabled = false;
            _btnStartParamReset.Click += BtnStartParamReset_Click;
            _paramResetPanel.Controls.Add(_btnStartParamReset);
            
            _tabPats.Controls.Add(_paramResetPanel);
            yPos += 160;

            // 8. Parameter Reset Progress Panel (hidden, shown during reset)
            _paramResetProgressPanel = CreateGroupPanel("🔄 PARAMETER RESET IN PROGRESS", 10, yPos - 160, fullWidth, 200);
            _paramResetProgressPanel.BackColor = Color.FromArgb(254, 243, 199);
            _paramResetProgressPanel.Visible = false;
            
            // Progress indicator
            _lblParamResetProgress = new Label
            {
                Text = "[1 BCM] ──→ [2 ABS] ──→ [3 PCM]\n    ●              ○              ○",
                Font = new Font("Consolas", 11, FontStyle.Bold),
                ForeColor = AppColors.ButtonPrimary,
                Location = new Point(150, 25),
                Size = new Size(400, 40),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _paramResetProgressPanel.Controls.Add(_lblParamResetProgress);
            
            // Current module outcode
            var lblModuleOutcode = new Label
            {
                Text = "BCM OUTCODE:",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(10, 75),
                AutoSize = true
            };
            _paramResetProgressPanel.Controls.Add(lblModuleOutcode);
            
            _txtParamResetOutcode = new TextBox
            {
                Location = new Point(10, 95),
                Size = new Size(620, 28),
                Font = new Font("Consolas", 12, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                ReadOnly = true,
                BackColor = Color.White
            };
            _paramResetProgressPanel.Controls.Add(_txtParamResetOutcode);
            
            // Current module incode input
            var lblModuleIncode = new Label
            {
                Text = "BCM INCODE:",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(10, 128),
                AutoSize = true
            };
            _paramResetProgressPanel.Controls.Add(lblModuleIncode);
            
            _txtParamResetIncode = new TextBox
            {
                Location = new Point(10, 148),
                Size = new Size(510, 28),
                Font = new Font("Consolas", 12, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                CharacterCasing = CharacterCasing.Upper
            };
            _paramResetProgressPanel.Controls.Add(_txtParamResetIncode);
            
            _btnParamResetSubmit = CreateButton("Submit", AppColors.ButtonSuccess, new Size(100, 28));
            _btnParamResetSubmit.Location = new Point(530, 148);
            _btnParamResetSubmit.Click += BtnParamResetSubmit_Click;
            _paramResetProgressPanel.Controls.Add(_btnParamResetSubmit);
            
            _btnParamResetCancel = CreateButton("Cancel", AppColors.ButtonDanger, new Size(80, 25));
            _btnParamResetCancel.Location = new Point(550, 5);
            _btnParamResetCancel.Click += BtnParamResetCancel_Click;
            _paramResetProgressPanel.Controls.Add(_btnParamResetCancel);
            
            _tabPats.Controls.Add(_paramResetProgressPanel);

            // 9. Other PATS Buttons
            var otherPatsPanel = new Panel
            {
                Location = new Point(10, yPos),
                Size = new Size(fullWidth, 45),
                Visible = false
            };
            otherPatsPanel.Name = "otherPatsPanel";
            
            _btnInitEscl = CreateButton("Initialize ESCL (1 token)", Color.White, new Size(200, 35));
            _btnInitEscl.Location = new Point(0, 5);
            _btnInitEscl.ForeColor = Color.Black;
            _btnInitEscl.FlatStyle = FlatStyle.Flat;
            _btnInitEscl.FlatAppearance.BorderColor = AppColors.Info;
            _btnInitEscl.Enabled = false;
            _btnInitEscl.Click += BtnInitEscl_Click;
            otherPatsPanel.Controls.Add(_btnInitEscl);
            _toolTip.SetToolTip(_btnInitEscl, "Initialize Electronic Steering Column Lock");
            
            _btnDisableBcm = CreateButton("Disable BCM Security", Color.White, new Size(200, 35));
            _btnDisableBcm.Location = new Point(210, 5);
            _btnDisableBcm.ForeColor = Color.Black;
            _btnDisableBcm.FlatStyle = FlatStyle.Flat;
            _btnDisableBcm.FlatAppearance.BorderColor = AppColors.Info;
            _btnDisableBcm.Enabled = false;
            _btnDisableBcm.Click += BtnDisableBcm_Click;
            otherPatsPanel.Controls.Add(_btnDisableBcm);
            _toolTip.SetToolTip(_btnDisableBcm, "For ALL KEYS LOST situations");
            
            _tabPats.Controls.Add(otherPatsPanel);
            yPos += 55;

            // 10. Gateway Unlock Panel (hidden, shown for 2020+ vehicles)
            _gatewayPanel = CreateGroupPanel("🔐 Gateway Unlock 2020+", 10, yPos, fullWidth, 100);
            _gatewayPanel.BackColor = AppColors.GatewayBg;
            _gatewayPanel.Visible = false;
            
            var lblGatewayDesc = new Label
            {
                Text = "✨ Unlock Security Gateway to get FREE key programming for 10 minutes!",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(146, 64, 14),
                Location = new Point(10, 25),
                Size = new Size(620, 20)
            };
            _gatewayPanel.Controls.Add(lblGatewayDesc);
            
            _btnGatewayUnlock = CreateButton("🔓 Gateway Unlock (1 token) - Get FREE Key Ops!", AppColors.ButtonWarning, new Size(620, 45));
            _btnGatewayUnlock.Location = new Point(10, 50);
            _btnGatewayUnlock.Enabled = false;
            _btnGatewayUnlock.Click += BtnGatewayUnlock_Click;
            _gatewayPanel.Controls.Add(_btnGatewayUnlock);
            _toolTip.SetToolTip(_btnGatewayUnlock, "Unlock security gateway for 10-minute FREE key programming session");
            
            _tabPats.Controls.Add(_gatewayPanel);
        }

        private void BuildUtilityTab()
        {
            int yPos = 15;
            int fullWidth = 620;
            
            var lblHeader = new Label
            {
                Text = "🔧 Utility Operations (1 token each)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Location = new Point(15, yPos),
                AutoSize = true
            };
            _tabUtility.Controls.Add(lblHeader);
            yPos += 25;
            
            var lblDesc = new Label
            {
                Text = "Each operation uses the same flow: Connect → Outcode → Incode → Submit",
                Font = new Font("Segoe UI", 8),
                ForeColor = AppColors.Info,
                Location = new Point(15, yPos),
                AutoSize = true
            };
            _tabUtility.Controls.Add(lblDesc);
            yPos += 30;

            // Create 2x2 grid of utility buttons
            var btnClearP160A = CreateUtilityButton(
                "🔧 Clear P160A - PCM",
                "Theft Detected - Vehicle Immobilized",
                "1 token",
                BtnClearP160A_Click);
            btnClearP160A.Location = new Point(15, yPos);
            _tabUtility.Controls.Add(btnClearP160A);
            _toolTip.SetToolTip(btnClearP160A, "Clears P160A theft code from PCM - use after key programming or BCM replacement");
            
            var btnClearB10A2 = CreateUtilityButton(
                "🚗 Clear B10A2 - BCM",
                "Crash Input Failure",
                "1 token",
                BtnClearB10A2_Click);
            btnClearB10A2.Location = new Point(335, yPos);
            _tabUtility.Controls.Add(btnClearB10A2);
            _toolTip.SetToolTip(btnClearB10A2, "Clears B10A2 configuration incompatible code from BCM");
            yPos += 110;
            
            var btnClearCrash = CreateUtilityButton(
                "⚠️ Clear Crash Event - BCM",
                "Collision/Accident Flag (DID 5B17)",
                "1 token",
                BtnClearCrashEvent_Click);
            btnClearCrash.Location = new Point(15, yPos);
            _tabUtility.Controls.Add(btnClearCrash);
            _toolTip.SetToolTip(btnClearCrash, "Clears crash/collision event flag from BCM");
            
            var btnBcmDefaults = CreateUtilityButton(
                "🔄 BCM Factory Defaults",
                "Restore BCM config (NOT PATS)",
                "1 token",
                BtnBcmFactoryDefaults_Click);
            btnBcmDefaults.Location = new Point(335, yPos);
            _tabUtility.Controls.Add(btnBcmDefaults);
            _toolTip.SetToolTip(btnBcmDefaults, "Restores BCM configuration to factory defaults - does NOT reset PATS");
        }

        private void BuildFreeTab()
        {
            int yPos = 15;
            int fullWidth = 620;
            
            var lblHeader = new Label
            {
                Text = "⚙️ Free Functions (No tokens required)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Location = new Point(15, yPos),
                AutoSize = true
            };
            _tabFree.Controls.Add(lblHeader);
            yPos += 35;

            string[][] functions = new string[][]
            {
                new[] { "❌ Clear All DTCs", "FREE", "BtnClearDtc" },
                new[] { "🔄 Vehicle Reset", "FREE", "BtnVehicleReset" },
                new[] { "🔑 Read Keys Count", "FREE", "BtnReadKeysCount" },
                new[] { "🔢 Read/Write Keypad Code", "FREE", "BtnKeypadCode" },
                new[] { "📋 Read All Module Info", "FREE", "BtnReadModuleInfo" },
                new[] { "🔕 Disarm Alarm", "FREE", "BtnDisarmAlarm" }
            };

            foreach (var func in functions)
            {
                var btn = CreateButton($"{func[0]}    {func[1]}", Color.White, new Size(fullWidth, 45));
                btn.Location = new Point(15, yPos);
                btn.ForeColor = Color.Black;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = AppColors.Info;
                btn.TextAlign = ContentAlignment.MiddleLeft;
                btn.Padding = new Padding(15, 0, 0, 0);
                btn.Enabled = false;
                btn.Name = func[2];
                btn.Click += FreeFunctionButton_Click;
                _tabFree.Controls.Add(btn);
                yPos += 55;
            }
        }

        #endregion

        #region UI Building - Activity Log & Status Bar

        private void BuildActivityLog()
        {
            var logPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 130,
                Padding = new Padding(10, 5, 10, 5)
            };

            var lblLog = new Label
            {
                Text = "Activity Log",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 20
            };
            logPanel.Controls.Add(lblLog);

            _rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = AppColors.LogBackground,
                ForeColor = AppColors.LogInfo,
                Font = new Font("Consolas", 9),
                ReadOnly = true,
                BorderStyle = BorderStyle.None
            };
            logPanel.Controls.Add(_rtbLog);
            
            this.Controls.Add(logPanel);
        }

        private void BuildStatusBar()
        {
            _statusBar = new StatusStrip
            {
                BackColor = Color.FromArgb(209, 213, 219)
            };

            _lblStatus = new ToolStripStatusLabel
            {
                Text = "Status: Ready",
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblVersion = new ToolStripStatusLabel
            {
                Text = "v2.0.0"
            };

            _statusBar.Items.AddRange(new ToolStripItem[] { _lblStatus, _lblVersion });
            this.Controls.Add(_statusBar);
        }

        #endregion

        #region UI Helper Methods

        private Panel CreateGroupPanel(string title, int x, int y, int width, int height)
        {
            var panel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(10, 3),
                AutoSize = true
            };
            panel.Controls.Add(lblTitle);

            return panel;
        }

        private Button CreateButton(string text, Color backColor, Size size)
        {
            var btn = new Button
            {
                Text = text,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = size,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private Button CreateUtilityButton(string title, string desc, string cost, EventHandler onClick)
        {
            var btn = new Button
            {
                Size = new Size(305, 100),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(10),
                Enabled = false
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            btn.Text = $"{title}\n\n{desc}\n{cost}";
            btn.Font = new Font("Segoe UI", 9);
            btn.Click += onClick;
            return btn;
        }

        #endregion

        #region Logging

        private void AddLog(string level, string message)
        {
            if (_rtbLog == null) return;
            
            if (_rtbLog.InvokeRequired)
            {
                _rtbLog.Invoke(() => AddLog(level, message));
                return;
            }

            Color color = level switch
            {
                "success" => AppColors.LogSuccess,
                "warning" => AppColors.LogWarning,
                "error" => AppColors.LogError,
                _ => AppColors.LogInfo
            };

            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            _rtbLog.ScrollToCaret();
            
            // Also log to file
            Logger.Info(message);
        }

        private void UpdateStatus(string message)
        {
            if (_lblStatus != null)
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(() => _lblStatus.Text = $"Status: {message}");
                }
                else
                {
                    _lblStatus.Text = $"Status: {message}";
                }
            }
        }

        #endregion

        #region Event Handlers - Device Connection

        private void BtnScan_Click(object? sender, EventArgs e)
        {
            AddLog("info", "Scanning for J2534 devices...");
            UpdateStatus("Scanning...");
            
            try
            {
                if (_cmbDevices == null || _deviceManager == null) return;
                
                _cmbDevices.Items.Clear();
                _cmbDevices.Items.Add("-- Scanning... --");
                _cmbDevices.SelectedIndex = 0;
                _cmbDevices.Enabled = false;
                
                var devices = _deviceManager.GetAvailableDevices();
                
                _cmbDevices.Items.Clear();
                
                if (devices.Count == 0)
                {
                    _cmbDevices.Items.Add("-- No J2534 devices found --");
                    AddLog("warning", "No J2534 devices found");
                }
                else
                {
                    foreach (var device in devices)
                    {
                        _cmbDevices.Items.Add(device);
                    }
                    AddLog("success", $"Found {devices.Count} device(s)");
                }
                
                _cmbDevices.SelectedIndex = 0;
                _cmbDevices.Enabled = true;
                UpdateStatus("Ready");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Scan failed: {ex.Message}");
                _cmbDevices?.Items.Clear();
                _cmbDevices?.Items.Add("-- Scan failed --");
                if (_cmbDevices != null)
                {
                    _cmbDevices.SelectedIndex = 0;
                    _cmbDevices.Enabled = true;
                }
            }
        }

        private void CmbDevices_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_btnConnect != null && _cmbDevices != null)
            {
                var selectedText = _cmbDevices.SelectedItem?.ToString() ?? "";
                _btnConnect.Enabled = !selectedText.StartsWith("--");
            }
        }

        private async void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_isConnectedToDevice)
            {
                DisconnectDevice();
                return;
            }

            var deviceName = _cmbDevices?.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(deviceName) || deviceName.StartsWith("--")) return;

            AddLog("info", $"Connecting to {deviceName}...");
            UpdateStatus("Connecting...");
            
            if (_btnConnect != null) _btnConnect.Enabled = false;
            if (_cmbDevices != null) _cmbDevices.Enabled = false;
            if (_btnScan != null) _btnScan.Enabled = false;

            try
            {
                _connectedDevice = _deviceManager?.OpenDevice(deviceName);
                
                if (_connectedDevice == null)
                {
                    throw new Exception("Failed to open device");
                }

                // Open HS-CAN channel
                _hsCanChannel = _connectedDevice.OpenChannel(
                    J2534Definitions.ProtocolId.CAN,
                    J2534Definitions.BaudRate.CAN_500K,
                    J2534Definitions.ConnectFlags.CAN_29BIT_ID);

                _isConnectedToDevice = true;
                
                if (_btnConnect != null)
                {
                    _btnConnect.Text = "✓ Connected";
                    _btnConnect.BackColor = AppColors.Success;
                    _btnConnect.Enabled = true;
                }
                if (_btnReadVehicle != null) _btnReadVehicle.Enabled = true;

                AddLog("success", $"Connected to {deviceName}");
                UpdateStatus("Device connected - Read vehicle to continue");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Connection failed: {ex.Message}");
                DisconnectDevice();
                
                if (_btnConnect != null) _btnConnect.Enabled = true;
                if (_cmbDevices != null) _cmbDevices.Enabled = true;
                if (_btnScan != null) _btnScan.Enabled = true;
                
                UpdateStatus("Connection failed");
            }
        }

        private void DisconnectDevice()
        {
            try
            {
                _hsCanChannel?.Dispose();
                _msCanChannel?.Dispose();
                _connectedDevice?.Dispose();
            }
            catch { }

            _hsCanChannel = null;
            _msCanChannel = null;
            _connectedDevice = null;
            _isConnectedToDevice = false;
            _isConnectedToVehicle = false;
            _incodeVerified = false;

            if (_btnConnect != null)
            {
                _btnConnect.Text = "Connect";
                _btnConnect.BackColor = AppColors.ButtonWarning;
                _btnConnect.Enabled = true;
            }
            if (_btnReadVehicle != null) _btnReadVehicle.Enabled = false;
            if (_cmbDevices != null) _cmbDevices.Enabled = true;
            if (_btnScan != null) _btnScan.Enabled = true;

            HideAllPanels();
            
            AddLog("info", "Device disconnected");
            UpdateStatus("Disconnected");
        }

        #endregion

        #region Event Handlers - Vehicle Operations

        private async void BtnReadVehicle_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            AddLog("info", "Reading vehicle VIN from CAN bus...");
            UpdateStatus("Reading vehicle...");
            
            if (_btnReadVehicle != null) _btnReadVehicle.Enabled = false;

            try
            {
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                
                // Read VIN
                var vin = await Task.Run(() => pats.ReadVin());
                AddLog("info", $"VIN: {vin}");
                
                // Decode VIN
                _detectedVehicle = VinDecoder.Decode(vin);
                _currentVin = vin;
                _is2020Plus = _detectedVehicle?.Year >= 2020;
                
                AddLog("success", $"Auto-detected: {_detectedVehicle?.Year} Ford {_detectedVehicle?.Model}");
                
                if (_is2020Plus)
                {
                    AddLog("warning", "2020+ vehicle - Gateway Unlock recommended for free key operations");
                }
                
                // Read PATS status
                var status = await Task.Run(() => pats.ReadPatsStatus());
                _currentOutcode = status.Outcode;
                
                // Display vehicle info
                ShowVehicleInfo(vin, _detectedVehicle, status);
                ShowOutcode(status.Outcode);
                ShowIncode();
                
                _isConnectedToVehicle = true;
                UpdateStatus("Enter incode and click Submit");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Failed to read vehicle: {ex.Message}");
                UpdateStatus("Failed to read vehicle");
                if (_btnReadVehicle != null) _btnReadVehicle.Enabled = true;
            }
        }

        private void ShowVehicleInfo(string vin, VehicleInfo? vehicle, PatsStatus status)
        {
            if (_vehicleInfoPanel == null || _lblVehicleInfo == null) return;

            var info = $"VIN: {vin}    |    Year: {vehicle?.Year ?? 0}    |    Model: {vehicle?.Model ?? "Unknown"}\n";
            info += $"BCM: {status.BcmPartNumber}    |    Battery: {status.BatteryVoltage:F1}V    |    Keys: {status.KeyCount}";
            
            _lblVehicleInfo.Text = info;
            _vehicleInfoPanel.Visible = true;
            
            if (_is2020Plus)
            {
                // Add 2020+ badge
                var badge = new Label
                {
                    Text = "2020+",
                    BackColor = AppColors.GatewayBg,
                    ForeColor = Color.FromArgb(146, 64, 14),
                    Font = new Font("Segoe UI", 8, FontStyle.Bold),
                    Padding = new Padding(5, 2, 5, 2),
                    AutoSize = true,
                    Location = new Point(400, 22),
                    Name = "badge2020"
                };
                _vehicleInfoPanel.Controls.Add(badge);
            }
        }

        private void ShowOutcode(string outcode)
        {
            if (_outcodePanel == null || _txtOutcode == null) return;
            
            _txtOutcode.Text = outcode;
            _outcodePanel.Visible = true;
        }

        private void ShowIncode()
        {
            if (_incodePanel == null) return;
            
            _incodePanel.Visible = true;
            _txtIncode?.Focus();
        }

        private void HideAllPanels()
        {
            if (_vehicleInfoPanel != null) _vehicleInfoPanel.Visible = false;
            if (_outcodePanel != null) _outcodePanel.Visible = false;
            if (_incodePanel != null) _incodePanel.Visible = false;
            if (_keyOperationsPanel != null) _keyOperationsPanel.Visible = false;
            if (_paramResetPanel != null) _paramResetPanel.Visible = false;
            if (_gatewayPanel != null) _gatewayPanel.Visible = false;
            
            var otherPanel = _tabPats?.Controls.Find("otherPatsPanel", false).FirstOrDefault();
            if (otherPanel != null) otherPanel.Visible = false;
        }

        #endregion

        #region Event Handlers - Incode Submit

        private void TxtIncode_TextChanged(object? sender, EventArgs e)
        {
            if (_btnSubmitIncode == null || _txtIncode == null) return;
            
            _btnSubmitIncode.Enabled = _txtIncode.Text.Length >= 4;
            
            // Reset verification if incode changed
            if (_incodeVerified)
            {
                _incodeVerified = false;
                _btnSubmitIncode.Text = "Submit";
                _btnSubmitIncode.BackColor = AppColors.ButtonPrimary;
                _btnSubmitIncode.Enabled = true;
                _txtIncode.Enabled = true;
                if (_lblIncodeStatus != null) _lblIncodeStatus.Visible = false;
                
                DisableOperations();
            }
        }

        private async void BtnSubmitIncode_Click(object? sender, EventArgs e)
        {
            if (_txtIncode == null || _btnSubmitIncode == null) return;
            
            _currentIncode = _txtIncode.Text.Trim().ToUpper();
            
            AddLog("info", "Validating incode...");
            _btnSubmitIncode.Enabled = false;
            _btnSubmitIncode.Text = "Validating...";
            
            // Simulate validation (in Phase 2, this will call API)
            await Task.Delay(500);
            
            _incodeVerified = true;
            _btnSubmitIncode.Text = "✓ Verified";
            _btnSubmitIncode.BackColor = AppColors.Success;
            _txtIncode.Enabled = false;
            
            if (_lblIncodeStatus != null)
            {
                _lblIncodeStatus.Text = "✓ Verified - Operations unlocked!";
                _lblIncodeStatus.Visible = true;
            }
            
            AddLog("success", "Incode verified! Operations unlocked.");
            
            if (_is2020Plus)
            {
                AddLog("warning", "2020+ vehicle - Gateway Unlock recommended for FREE key operations");
            }
            
            EnableOperations();
            UpdateStatus("Ready for operations");
        }

        private void EnableOperations()
        {
            // Key operations
            if (_keyOperationsPanel != null) _keyOperationsPanel.Visible = true;
            if (_btnEraseKeys != null) _btnEraseKeys.Enabled = true;
            if (_btnProgramKeys != null) _btnProgramKeys.Enabled = true;
            
            // Parameter reset
            if (_paramResetPanel != null) _paramResetPanel.Visible = true;
            if (_btnStartParamReset != null) _btnStartParamReset.Enabled = true;
            
            // Gateway (only for 2020+)
            if (_is2020Plus)
            {
                if (_gatewayPanel != null) _gatewayPanel.Visible = true;
                if (_btnGatewayUnlock != null) _btnGatewayUnlock.Enabled = true;
            }
            
            // Other PATS buttons
            var otherPanel = _tabPats?.Controls.Find("otherPatsPanel", false).FirstOrDefault();
            if (otherPanel != null) otherPanel.Visible = true;
            if (_btnInitEscl != null) _btnInitEscl.Enabled = true;
            if (_btnDisableBcm != null) _btnDisableBcm.Enabled = true;
            
            // Utility tab buttons
            foreach (Control ctrl in _tabUtility?.Controls ?? new Control.ControlCollection(this))
            {
                if (ctrl is Button btn) btn.Enabled = true;
            }
            
            // Free tab buttons
            foreach (Control ctrl in _tabFree?.Controls ?? new Control.ControlCollection(this))
            {
                if (ctrl is Button btn) btn.Enabled = true;
            }
        }

        private void DisableOperations()
        {
            if (_keyOperationsPanel != null) _keyOperationsPanel.Visible = false;
            if (_paramResetPanel != null) _paramResetPanel.Visible = false;
            if (_gatewayPanel != null) _gatewayPanel.Visible = false;
            
            var otherPanel = _tabPats?.Controls.Find("otherPatsPanel", false).FirstOrDefault();
            if (otherPanel != null) otherPanel.Visible = false;
            
            // Disable utility buttons
            foreach (Control ctrl in _tabUtility?.Controls ?? new Control.ControlCollection(this))
            {
                if (ctrl is Button btn) btn.Enabled = false;
            }
            
            // Disable free buttons
            foreach (Control ctrl in _tabFree?.Controls ?? new Control.ControlCollection(this))
            {
                if (ctrl is Button btn) btn.Enabled = false;
            }
        }

        #endregion

        #region Event Handlers - Key Operations

        private async void BtnEraseKeys_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null || string.IsNullOrEmpty(_currentIncode)) return;

            var result = MessageBox.Show(
                "⚠️ WARNING: This will ERASE ALL programmed keys from the BCM!\n\n" +
                "You must program at least 2 new keys immediately after.\n\n" +
                "Are you sure you want to continue?",
                "Erase All Keys",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            int tokenCost = _sessionActive ? 0 : 1;
            AddLog("info", $"Erasing all keys... ({(tokenCost == 0 ? "FREE" : "1 token")})");
            UpdateStatus("Erasing keys...");

            try
            {
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);

                await Task.Run(() => pats.EraseAllKeys(_currentIncode));

                AddLog("success", "All keys erased successfully!");
                MessageBox.Show(
                    "All keys have been erased.\n\nYou must now program at least 2 new keys.",
                    "Keys Erased",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                
                UpdateStatus("Keys erased - program new keys");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Erase failed: {ex.Message}");
                MessageBox.Show($"Failed to erase keys:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void BtnProgramKeys_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null || string.IsNullOrEmpty(_currentIncode)) return;

            int tokenCost = _sessionActive ? 0 : 1;
            AddLog("info", $"Starting key programming... ({(tokenCost == 0 ? "FREE" : "1 token")})");
            UpdateStatus("Programming keys...");

            MessageBox.Show(
                "Key Programming Instructions:\n\n" +
                "1. Insert the new key into the ignition\n" +
                "2. Turn to ON position (do not start)\n" +
                "3. Click OK to begin programming\n" +
                "4. Wait for the security light to go out",
                "Program Keys",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            try
            {
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);

                await Task.Run(() => pats.ProgramKey(_currentIncode));

                AddLog("success", "Key programmed successfully!");
                MessageBox.Show("Key programmed successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("Key programmed");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Programming failed: {ex.Message}");
                MessageBox.Show($"Failed to program key:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Event Handlers - Parameter Reset

        private void ChkAbsOnCan2_CheckedChanged(object? sender, EventArgs e)
        {
            if (_chkAbsOnCan2?.Checked == true && _chkSkipAbs != null)
            {
                _chkSkipAbs.Checked = false;
            }
            _absOnCan2 = _chkAbsOnCan2?.Checked ?? false;
        }

        private void ChkSkipAbs_CheckedChanged(object? sender, EventArgs e)
        {
            if (_chkSkipAbs?.Checked == true && _chkAbsOnCan2 != null)
            {
                _chkAbsOnCan2.Checked = false;
            }
            _skipAbs = _chkSkipAbs?.Checked ?? false;
            
            // Update button text
            if (_btnStartParamReset != null)
            {
                _btnStartParamReset.Text = _skipAbs 
                    ? "🔄 Start Parameter Reset (2 tokens)" 
                    : "🔄 Start Parameter Reset (3-4 tokens)";
            }
        }

        private void BtnStartParamReset_Click(object? sender, EventArgs e)
        {
            // Hide normal panel, show progress panel
            if (_paramResetPanel != null) _paramResetPanel.Visible = false;
            if (_paramResetProgressPanel != null)
            {
                _paramResetProgressPanel.Visible = true;
                _paramResetProgressPanel.BringToFront();
            }
            
            _paramResetActive = true;
            _currentParamResetStep = 0;
            
            int moduleCount = _skipAbs ? 2 : 3;
            AddLog("info", $"Starting Parameter Reset - {moduleCount} modules");
            
            ProcessNextParamResetStep();
        }

        private void ProcessNextParamResetStep()
        {
            string[] modules = _skipAbs 
                ? new[] { "BCM", "PCM" } 
                : new[] { "BCM", "ABS", "PCM" };

            if (_currentParamResetStep >= modules.Length)
            {
                // Complete!
                CompleteParameterReset();
                return;
            }

            string currentModule = modules[_currentParamResetStep];
            UpdateParamResetProgress(modules, _currentParamResetStep);
            
            AddLog("info", $"Reading {currentModule} outcode...");
            
            // Simulate reading outcode (in real implementation, call J2534)
            Task.Delay(1000).ContinueWith(t => {
                this.Invoke(() => {
                    string outcode = $"{currentModule}-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
                    if (_txtParamResetOutcode != null)
                    {
                        _txtParamResetOutcode.Text = outcode;
                    }
                    AddLog("success", $"{currentModule} Outcode: {outcode}");
                    UpdateStatus($"Enter {currentModule} incode and click Submit");
                });
            });
        }

        private void UpdateParamResetProgress(string[] modules, int currentStep)
        {
            if (_lblParamResetProgress == null) return;

            var progressLine1 = "";
            var progressLine2 = "";
            
            for (int i = 0; i < modules.Length; i++)
            {
                string arrow = i < modules.Length - 1 ? " ──→ " : "";
                progressLine1 += $"[{i + 1} {modules[i]}]{arrow}";
                
                string status = i < currentStep ? "✓" : (i == currentStep ? "●" : "○");
                progressLine2 += $"    {status}    " + (i < modules.Length - 1 ? "         " : "");
            }
            
            _lblParamResetProgress.Text = progressLine1 + "\n" + progressLine2;
        }

        private void BtnParamResetSubmit_Click(object? sender, EventArgs e)
        {
            string incode = _txtParamResetIncode?.Text?.Trim().ToUpper() ?? "";
            
            if (string.IsNullOrEmpty(incode))
            {
                MessageBox.Show("Please enter the incode", "Incode Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string[] modules = _skipAbs 
                ? new[] { "BCM", "PCM" } 
                : new[] { "BCM", "ABS", "PCM" };
            string currentModule = modules[_currentParamResetStep];
            
            AddLog("info", $"Applying {currentModule} incode...");
            UpdateStatus($"Resetting {currentModule}...");
            
            // Simulate applying incode
            Task.Delay(1500).ContinueWith(t => {
                this.Invoke(() => {
                    AddLog("success", $"{currentModule} reset complete!");
                    
                    _currentParamResetStep++;
                    if (_txtParamResetIncode != null) _txtParamResetIncode.Text = "";
                    
                    ProcessNextParamResetStep();
                });
            });
        }

        private void CompleteParameterReset()
        {
            _paramResetActive = false;
            
            int moduleCount = _skipAbs ? 2 : 3;
            AddLog("success", $"✅ Parameter Reset COMPLETE - All {moduleCount} modules synchronized!");
            
            MessageBox.Show(
                $"Parameter Reset Complete!\n\n{moduleCount} modules synchronized successfully.",
                "Success",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            
            // Hide progress panel, show normal panel
            if (_paramResetProgressPanel != null) _paramResetProgressPanel.Visible = false;
            if (_paramResetPanel != null) _paramResetPanel.Visible = true;
            
            UpdateStatus("Parameter Reset complete");
        }

        private void BtnParamResetCancel_Click(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Cancel Parameter Reset?\n\nPartially completed modules may need to be reset again.",
                "Cancel Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            
            if (result == DialogResult.Yes)
            {
                _paramResetActive = false;
                AddLog("warning", "Parameter Reset cancelled by user");
                
                if (_paramResetProgressPanel != null) _paramResetProgressPanel.Visible = false;
                if (_paramResetPanel != null) _paramResetPanel.Visible = true;
                
                UpdateStatus("Parameter Reset cancelled");
            }
        }

        #endregion

        #region Event Handlers - Gateway Unlock

        private async void BtnGatewayUnlock_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null || string.IsNullOrEmpty(_currentIncode)) return;

            AddLog("info", "Unlocking Security Gateway...");
            UpdateStatus("Unlocking gateway...");
            
            if (_btnGatewayUnlock != null) _btnGatewayUnlock.Enabled = false;

            try
            {
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);

                await Task.Run(() => pats.UnlockGateway(_currentIncode));

                // Start session
                _sessionActive = true;
                _sessionSecondsRemaining = 600; // 10 minutes
                
                if (_sessionBanner != null) _sessionBanner.Visible = true;
                if (_sessionTimer != null) _sessionTimer.Start();
                
                UpdateKeyOperationCosts(true);
                
                AddLog("success", "Security Gateway unlocked!");
                AddLog("info", "BCM session opened - key operations FREE for 10 minutes");
                
                if (_btnGatewayUnlock != null)
                {
                    _btnGatewayUnlock.Text = "✓ Gateway Unlocked";
                    _btnGatewayUnlock.BackColor = AppColors.Success;
                }
                
                UpdateStatus("Gateway unlocked - FREE key programming for 10 minutes");
            }
            catch (Exception ex)
            {
                AddLog("error", $"Gateway unlock failed: {ex.Message}");
                if (_btnGatewayUnlock != null) _btnGatewayUnlock.Enabled = true;
                UpdateStatus("Gateway unlock failed");
            }
        }

        private void SessionTimer_Tick(object? sender, EventArgs e)
        {
            _sessionSecondsRemaining--;
            
            int mins = _sessionSecondsRemaining / 60;
            int secs = _sessionSecondsRemaining % 60;
            
            if (_lblSessionTimer != null)
            {
                _lblSessionTimer.Text = $"{mins}:{secs:D2}";
                
                if (_sessionSecondsRemaining <= 60)
                {
                    _lblSessionTimer.ForeColor = Color.Yellow;
                }
            }
            
            if (_sessionSecondsRemaining == 60)
            {
                AddLog("warning", "Gateway session expires in 1 minute!");
            }
            
            if (_sessionSecondsRemaining <= 0)
            {
                _sessionTimer?.Stop();
                _sessionActive = false;
                
                if (_sessionBanner != null) _sessionBanner.Visible = false;
                
                UpdateKeyOperationCosts(false);
                
                AddLog("warning", "Gateway session expired - key operations now cost tokens");
                
                if (_btnGatewayUnlock != null)
                {
                    _btnGatewayUnlock.Text = "🔓 Gateway Unlock (1 token) - Get FREE Key Ops!";
                    _btnGatewayUnlock.BackColor = AppColors.ButtonWarning;
                    _btnGatewayUnlock.Enabled = true;
                }
                
                if (_lblSessionTimer != null)
                {
                    _lblSessionTimer.ForeColor = Color.White;
                }
                
                UpdateStatus("Gateway session expired");
            }
        }

        private void UpdateKeyOperationCosts(bool isFree)
        {
            if (_btnEraseKeys != null)
            {
                _btnEraseKeys.Text = isFree 
                    ? "🗑️ Erase All Keys\nFREE" 
                    : "🗑️ Erase All Keys\n1 token";
            }
            
            if (_btnProgramKeys != null)
            {
                _btnProgramKeys.Text = isFree 
                    ? "🔑 Program New Keys\nFREE" 
                    : "🔑 Program New Keys\n1 token";
            }
            
            if (_lblKeyOpsFree != null)
            {
                _lblKeyOpsFree.Visible = isFree;
            }
        }

        #endregion

        #region Event Handlers - Other PATS Functions

        private async void BtnInitEscl_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null || string.IsNullOrEmpty(_currentIncode)) return;
            
            AddLog("info", "Initializing ESCL...");
            // Implementation similar to v2
            MessageBox.Show("ESCL initialization would run here", "ESCL Init", MessageBoxButtons.OK);
        }

        private void BtnDisableBcm_Click(object? sender, EventArgs e)
        {
            MessageBox.Show(
                "BCM Security Disable is for ALL KEYS LOST situations.\n\n" +
                "This function will be available in a future update.",
                "Disable BCM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        #endregion

        #region Event Handlers - Utility Tab

        private void BtnClearP160A_Click(object? sender, EventArgs e)
        {
            AddLog("info", "Clearing P160A from PCM...");
            MessageBox.Show("Clear P160A operation would run here", "Clear P160A", MessageBoxButtons.OK);
        }

        private void BtnClearB10A2_Click(object? sender, EventArgs e)
        {
            AddLog("info", "Clearing B10A2 from BCM...");
            MessageBox.Show("Clear B10A2 operation would run here", "Clear B10A2", MessageBoxButtons.OK);
        }

        private void BtnClearCrashEvent_Click(object? sender, EventArgs e)
        {
            AddLog("info", "Clearing Crash Event from BCM...");
            MessageBox.Show("Clear Crash Event operation would run here", "Clear Crash Event", MessageBoxButtons.OK);
        }

        private void BtnBcmFactoryDefaults_Click(object? sender, EventArgs e)
        {
            AddLog("info", "Performing BCM Factory Defaults...");
            MessageBox.Show("BCM Factory Defaults operation would run here", "BCM Defaults", MessageBoxButtons.OK);
        }

        #endregion

        #region Event Handlers - Free Functions Tab

        private void FreeFunctionButton_Click(object? sender, EventArgs e)
        {
            if (sender is Button btn)
            {
                AddLog("info", $"Executing: {btn.Text.Split("    ")[0]}");
                MessageBox.Show($"{btn.Text.Split("    ")[0]} would run here", "Free Function", MessageBoxButtons.OK);
            }
        }

        #endregion

        #region Event Handlers - Miscellaneous

        private void BtnCopyOutcode_Click(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentOutcode))
            {
                Clipboard.SetText(_currentOutcode);
                AddLog("info", "Outcode copied to clipboard");
            }
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            SaveSettings();
            DisconnectDevice();
        }

        #endregion

        #region Helper Methods

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddLog("error", $"Failed to open URL: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            _autoDisableAlarm = Settings.GetBool("AutoDisableAlarm", true);
        }

        private void SaveSettings()
        {
            Settings.SetBool("AutoDisableAlarm", _autoDisableAlarm);
            Settings.Save();
        }

        #endregion
    }
}
