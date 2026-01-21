using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PatsKillerPro.J2534;
using PatsKillerPro.Vehicle;
using PatsKillerPro.Communication;
using PatsKillerPro.Utils;

namespace PatsKillerPro
{
    public partial class MainForm : Form
    {
        // State
        private J2534DeviceManager? _deviceManager;
        private J2534Device? _connectedDevice;
        private J2534Channel? _hsCanChannel;
        private J2534Channel? _msCanChannel;
        private VehicleInfo? _detectedVehicle;
        private bool _isConnectedToDevice = false;
        private bool _isConnectedToVehicle = false;
        private string _currentOutcode = "";
        
        // Settings
        private bool _autoDisableAlarm = true;
        
        // UI Colors
        private readonly Color _colorBackground = Color.FromArgb(240, 240, 240);
        private readonly Color _colorPanelBg = Color.White;
        private readonly Color _colorSuccess = Color.FromArgb(34, 197, 94);
        private readonly Color _colorWarning = Color.FromArgb(245, 158, 11);
        private readonly Color _colorError = Color.FromArgb(239, 68, 68);
        private readonly Color _colorPrimary = Color.FromArgb(59, 130, 246);
        private readonly Color _colorOrange = Color.FromArgb(249, 115, 22);

        // Tooltip manager
        private ToolTip _toolTip = new ToolTip();

        public MainForm()
        {
            InitializeComponent();
            _deviceManager = new J2534DeviceManager();
            LoadSettings();
            SetupTooltips();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            
            // Form settings
            this.Text = "PatsKiller Pro 2026 ( Ford & Lincoln PATS Solution )";
            this.Size = new Size(850, 750);
            this.MinimumSize = new Size(800, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = _colorBackground;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            this.FormClosing += MainForm_FormClosing;
            this.Load += MainForm_Load;

            // Create menu strip
            CreateMenuStrip();
            
            // Create main layout
            CreateMainLayout();
            
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

        private void CreateMenuStrip()
        {
            var menuStrip = new MenuStrip();
            menuStrip.BackColor = Color.FromArgb(255, 214, 0); // Yellow title bar
            
            // File menu
            var fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.Add("Exit", null, (s, e) => this.Close());
            menuStrip.Items.Add(fileMenu);
            
            // Help menu (Log folder hidden but still writes to file)
            var helpMenu = new ToolStripMenuItem("Help");
            helpMenu.DropDownItems.Add("Software Tutorial", null, (s, e) => OpenUrl("https://patskiller.com/faqs"));
            helpMenu.DropDownItems.Add("Visit patskiller.com", null, (s, e) => OpenUrl("https://patskiller.com"));
            helpMenu.DropDownItems.Add(new ToolStripSeparator());
            helpMenu.DropDownItems.Add("About PatsKiller Pro", null, ShowAbout_Click);
            menuStrip.Items.Add(helpMenu);
            
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);
        }

        private TabControl? _tabControl;
        private ComboBox? _cmbDevices;
        private Button? _btnScan;
        private Button? _btnConnect;
        private Label? _lblDeviceStatus;
        private ComboBox? _cmbVehicles;
        private Button? _btnReadVehicle;
        private CheckBox? _chkKeyless;
        private CheckBox? _chkRfaOnly;
        private CheckBox? _chkAutoDisableAlarm;
        private Label? _lblVehicleInfo;
        private TextBox? _txtOutcode;
        private TextBox? _txtIncode;
        private Button? _btnCopyOutcode;
        private Button? _btnGetIncode;
        private Button? _btnProgramKeys;
        private Button? _btnEraseKeys;
        private Button? _btnParameterReset;
        private Button? _btnInitEscl;
        private Button? _btnDisableBcm;
        private Panel? _vehicleInfoPanel;
        private Label? _lblStatusBar;

        private void CreateMainLayout()
        {
            int yPos = 30; // Below menu
            int padding = 10;
            int panelWidth = this.ClientSize.Width - (padding * 2);

            // Tab Control
            _tabControl = new TabControl();
            _tabControl.Location = new Point(padding, yPos);
            _tabControl.Size = new Size(panelWidth, 630);
            _tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            
            // PATS Functions Tab
            var patsTab = new TabPage("PATS Functions");
            patsTab.BackColor = _colorPanelBg;
            patsTab.Padding = new Padding(10);
            CreatePatsTabContent(patsTab);
            _tabControl.TabPages.Add(patsTab);
            
            // Other Functions Tab
            var otherTab = new TabPage("Other Functions");
            otherTab.BackColor = _colorPanelBg;
            otherTab.Padding = new Padding(10);
            otherTab.AutoScroll = true;
            CreateOtherTabContent(otherTab);
            _tabControl.TabPages.Add(otherTab);
            
            this.Controls.Add(_tabControl);

            // Status Bar
            _lblStatusBar = new Label();
            _lblStatusBar.Text = "Status: Ready";
            _lblStatusBar.Location = new Point(0, this.ClientSize.Height - 25);
            _lblStatusBar.Size = new Size(this.ClientSize.Width, 25);
            _lblStatusBar.BackColor = Color.FromArgb(209, 213, 219);
            _lblStatusBar.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatusBar.Padding = new Padding(10, 0, 0, 0);
            _lblStatusBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(_lblStatusBar);
            
            _toolTip.SetToolTip(_lblStatusBar, "Shows the current operation status and any errors");
        }

        private void CreatePatsTabContent(TabPage tab)
        {
            int yPos = 10;
            int xPos = 10;
            int fullWidth = tab.ClientSize.Width - 40;

            // Session ID Row
            var lblSession = new Label();
            lblSession.Text = "Session: Connected to patskiller.com";
            lblSession.Location = new Point(xPos, yPos);
            lblSession.AutoSize = true;
            lblSession.ForeColor = _colorSuccess;
            tab.Controls.Add(lblSession);
            _toolTip.SetToolTip(lblSession, "Your patskiller.com session status - tokens are charged from your account");

            var lblJ2534Only = new Label();
            lblJ2534Only.Text = "● J2534 v2 Only";
            lblJ2534Only.Location = new Point(fullWidth - 100, yPos);
            lblJ2534Only.AutoSize = true;
            lblJ2534Only.ForeColor = _colorPrimary;
            lblJ2534Only.Font = new Font(this.Font, FontStyle.Bold);
            tab.Controls.Add(lblJ2534Only);
            _toolTip.SetToolTip(lblJ2534Only, "This application only supports J2534 v2 compliant pass-thru devices");

            yPos += 30;

            // J2534 Device Section
            var devicePanel = CreateGroupPanel("J2534 Device", xPos, yPos, fullWidth, 110);
            tab.Controls.Add(devicePanel);

            int innerY = 25;
            _cmbDevices = new ComboBox();
            _cmbDevices.Location = new Point(10, innerY);
            _cmbDevices.Size = new Size(fullWidth - 120, 25);
            _cmbDevices.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbDevices.Items.Add("-- Click Scan to detect J2534 devices --");
            _cmbDevices.SelectedIndex = 0;
            devicePanel.Controls.Add(_cmbDevices);
            _toolTip.SetToolTip(_cmbDevices, "Select your J2534 pass-thru device from the list after scanning");

            _btnScan = new Button();
            _btnScan.Text = "Scan";
            _btnScan.Location = new Point(fullWidth - 100, innerY);
            _btnScan.Size = new Size(80, 25);
            _btnScan.Click += BtnScan_Click;
            devicePanel.Controls.Add(_btnScan);
            _toolTip.SetToolTip(_btnScan, "Scan Windows registry for installed J2534 devices");

            innerY += 35;
            _btnConnect = new Button();
            _btnConnect.Text = "Connect to Device";
            _btnConnect.Location = new Point(10, innerY);
            _btnConnect.Size = new Size(fullWidth - 20, 35);
            _btnConnect.BackColor = _colorOrange;
            _btnConnect.ForeColor = Color.White;
            _btnConnect.FlatStyle = FlatStyle.Flat;
            _btnConnect.Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold);
            _btnConnect.Click += BtnConnect_Click;
            _btnConnect.Enabled = false;
            devicePanel.Controls.Add(_btnConnect);
            _toolTip.SetToolTip(_btnConnect, "Connect to the selected J2534 device via USB");

            yPos += 120;

            // Vehicle Detection Section
            var vehiclePanel = CreateGroupPanel("Vehicle Detection", xPos, yPos, fullWidth, 150);
            tab.Controls.Add(vehiclePanel);

            innerY = 25;
            _btnReadVehicle = new Button();
            _btnReadVehicle.Text = "🔍 Read Vehicle (Auto-Detect from VIN)";
            _btnReadVehicle.Location = new Point(10, innerY);
            _btnReadVehicle.Size = new Size(fullWidth - 20, 35);
            _btnReadVehicle.BackColor = _colorPrimary;
            _btnReadVehicle.ForeColor = Color.White;
            _btnReadVehicle.FlatStyle = FlatStyle.Flat;
            _btnReadVehicle.Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold);
            _btnReadVehicle.Click += BtnReadVehicle_Click;
            _btnReadVehicle.Enabled = false;
            vehiclePanel.Controls.Add(_btnReadVehicle);
            _toolTip.SetToolTip(_btnReadVehicle, "Automatically reads VIN from vehicle CAN bus and identifies the vehicle model - FREE");

            innerY += 45;
            var lblManual = new Label();
            lblManual.Text = "Or select manually:";
            lblManual.Location = new Point(10, innerY);
            lblManual.AutoSize = true;
            vehiclePanel.Controls.Add(lblManual);

            innerY += 20;
            _cmbVehicles = new ComboBox();
            _cmbVehicles.Location = new Point(10, innerY);
            _cmbVehicles.Size = new Size(fullWidth - 20, 25);
            _cmbVehicles.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbVehicles.Enabled = false;
            PopulateVehicleList();
            vehiclePanel.Controls.Add(_cmbVehicles);
            _toolTip.SetToolTip(_cmbVehicles, "Manually select your vehicle if auto-detection fails");

            innerY += 35;
            _chkKeyless = new CheckBox();
            _chkKeyless.Text = "Keyless";
            _chkKeyless.Location = new Point(10, innerY);
            _chkKeyless.AutoSize = true;
            _chkKeyless.Enabled = false;
            vehiclePanel.Controls.Add(_chkKeyless);
            _toolTip.SetToolTip(_chkKeyless, "Check if vehicle has keyless push-button start");

            _chkRfaOnly = new CheckBox();
            _chkRfaOnly.Text = "RFA only";
            _chkRfaOnly.Location = new Point(100, innerY);
            _chkRfaOnly.AutoSize = true;
            _chkRfaOnly.Enabled = false;
            vehiclePanel.Controls.Add(_chkRfaOnly);
            _toolTip.SetToolTip(_chkRfaOnly, "Check if vehicle uses RFA (Remote Function Actuator) only mode");

            _chkAutoDisableAlarm = new CheckBox();
            _chkAutoDisableAlarm.Text = "Auto-disable alarm before programming (recommended)";
            _chkAutoDisableAlarm.Location = new Point(200, innerY);
            _chkAutoDisableAlarm.AutoSize = true;
            _chkAutoDisableAlarm.Checked = _autoDisableAlarm;
            _chkAutoDisableAlarm.CheckedChanged += (s, e) => _autoDisableAlarm = _chkAutoDisableAlarm.Checked;
            vehiclePanel.Controls.Add(_chkAutoDisableAlarm);
            _toolTip.SetToolTip(_chkAutoDisableAlarm, "Automatically disables the vehicle alarm before key programming to prevent false triggers - FREE");

            yPos += 160;

            // Vehicle Info Panel (hidden until connected)
            _vehicleInfoPanel = CreateGroupPanel("Connected Vehicle", xPos, yPos, fullWidth, 80);
            _vehicleInfoPanel.Visible = false;
            tab.Controls.Add(_vehicleInfoPanel);

            _lblVehicleInfo = new Label();
            _lblVehicleInfo.Location = new Point(10, 20);
            _lblVehicleInfo.Size = new Size(fullWidth - 20, 50);
            _lblVehicleInfo.Font = new Font("Consolas", 9);
            _vehicleInfoPanel.Controls.Add(_lblVehicleInfo);
            _toolTip.SetToolTip(_lblVehicleInfo, "Shows connected vehicle information including VIN, BCM part number, battery voltage, and programmed key count");

            // Outcode/Incode Section
            var codePanel = CreateGroupPanel("PATS Codes", xPos, yPos, fullWidth, 130);
            codePanel.Visible = false;
            codePanel.Name = "codePanel";
            tab.Controls.Add(codePanel);

            innerY = 20;
            var lblOutcode = new Label();
            lblOutcode.Text = "OUTCODE:";
            lblOutcode.Location = new Point(10, innerY);
            lblOutcode.AutoSize = true;
            lblOutcode.Font = new Font(this.Font, FontStyle.Bold);
            codePanel.Controls.Add(lblOutcode);
            _toolTip.SetToolTip(lblOutcode, "The 12-character security outcode read from the vehicle's BCM");

            _btnCopyOutcode = new Button();
            _btnCopyOutcode.Text = "Copy";
            _btnCopyOutcode.Location = new Point(fullWidth - 60, innerY - 3);
            _btnCopyOutcode.Size = new Size(50, 23);
            _btnCopyOutcode.Click += BtnCopyOutcode_Click;
            codePanel.Controls.Add(_btnCopyOutcode);
            _toolTip.SetToolTip(_btnCopyOutcode, "Copy outcode to clipboard");

            innerY += 22;
            _txtOutcode = new TextBox();
            _txtOutcode.Location = new Point(10, innerY);
            _txtOutcode.Size = new Size(fullWidth - 20, 28);
            _txtOutcode.Font = new Font("Consolas", 14, FontStyle.Bold);
            _txtOutcode.TextAlign = HorizontalAlignment.Center;
            _txtOutcode.ReadOnly = true;
            _txtOutcode.BackColor = Color.FromArgb(254, 249, 195);
            codePanel.Controls.Add(_txtOutcode);
            _toolTip.SetToolTip(_txtOutcode, "Vehicle outcode - Copy this and enter at patskiller.com/calculator to get the incode");

            innerY += 32;
            _btnGetIncode = new Button();
            _btnGetIncode.Text = "🔗 Get Incode at patskiller.com/calculator (1 token)";
            _btnGetIncode.Location = new Point(10, innerY);
            _btnGetIncode.Size = new Size(fullWidth - 20, 25);
            _btnGetIncode.ForeColor = _colorPrimary;
            _btnGetIncode.FlatStyle = FlatStyle.Flat;
            _btnGetIncode.Click += BtnGetIncode_Click;
            codePanel.Controls.Add(_btnGetIncode);
            _toolTip.SetToolTip(_btnGetIncode, "Opens patskiller.com/calculator to convert outcode to incode - Costs 1 token per calculation");

            innerY += 30;
            var lblIncode = new Label();
            lblIncode.Text = "INCODE:";
            lblIncode.Location = new Point(10, innerY);
            lblIncode.AutoSize = true;
            lblIncode.Font = new Font(this.Font, FontStyle.Bold);
            codePanel.Controls.Add(lblIncode);
            _toolTip.SetToolTip(lblIncode, "Enter the incode received from patskiller.com/calculator");

            innerY += 22;
            _txtIncode = new TextBox();
            _txtIncode.Location = new Point(10, innerY);
            _txtIncode.Size = new Size(fullWidth - 20, 28);
            _txtIncode.Font = new Font("Consolas", 14, FontStyle.Bold);
            _txtIncode.TextAlign = HorizontalAlignment.Center;
            _txtIncode.BackColor = Color.FromArgb(220, 252, 231);
            _txtIncode.CharacterCasing = CharacterCasing.Upper;
            codePanel.Controls.Add(_txtIncode);
            _toolTip.SetToolTip(_txtIncode, "Paste the incode from patskiller.com here - Same incode allows unlimited key programming until outcode changes");

            // Action Buttons Panel
            var actionPanel = CreateGroupPanel("Actions", xPos, yPos + 140, fullWidth, 120);
            actionPanel.Visible = false;
            actionPanel.Name = "actionPanel";
            tab.Controls.Add(actionPanel);

            int btnWidth = (fullWidth - 50) / 2;
            _btnEraseKeys = new Button();
            _btnEraseKeys.Text = "Erase all keys (1 token)";
            _btnEraseKeys.Location = new Point(10, 25);
            _btnEraseKeys.Size = new Size(btnWidth, 40);
            _btnEraseKeys.BackColor = _colorError;
            _btnEraseKeys.ForeColor = Color.White;
            _btnEraseKeys.FlatStyle = FlatStyle.Flat;
            _btnEraseKeys.Font = new Font(this.Font, FontStyle.Bold);
            _btnEraseKeys.Click += BtnEraseKeys_Click;
            actionPanel.Controls.Add(_btnEraseKeys);
            _toolTip.SetToolTip(_btnEraseKeys, "⚠️ WARNING: Erases ALL programmed keys from BCM. You must program at least 2 new keys after. Costs 1 token.");

            _btnProgramKeys = new Button();
            _btnProgramKeys.Text = "Program new keys (FREE*)";
            _btnProgramKeys.Location = new Point(10, 70);
            _btnProgramKeys.Size = new Size(btnWidth, 40);
            _btnProgramKeys.BackColor = _colorSuccess;
            _btnProgramKeys.ForeColor = Color.White;
            _btnProgramKeys.FlatStyle = FlatStyle.Flat;
            _btnProgramKeys.Font = new Font(this.Font, FontStyle.Bold);
            _btnProgramKeys.Click += BtnProgramKeys_Click;
            actionPanel.Controls.Add(_btnProgramKeys);
            _toolTip.SetToolTip(_btnProgramKeys, "Program new transponder keys - FREE after incode obtained (same incode = unlimited keys until outcode changes)");

            _btnParameterReset = new Button();
            _btnParameterReset.Text = "Parameter reset (FREE)";
            _btnParameterReset.Location = new Point(btnWidth + 20, 25);
            _btnParameterReset.Size = new Size(btnWidth, 35);
            _btnParameterReset.Click += BtnParameterReset_Click;
            actionPanel.Controls.Add(_btnParameterReset);
            _toolTip.SetToolTip(_btnParameterReset, "Syncs PATS parameters between BCM and PCM, then clears DTCs - FREE");

            _btnInitEscl = new Button();
            _btnInitEscl.Text = "Initialize ESCL (1 token)";
            _btnInitEscl.Location = new Point(btnWidth + 20, 65);
            _btnInitEscl.Size = new Size(btnWidth / 2 - 5, 35);
            _btnInitEscl.Click += BtnInitEscl_Click;
            actionPanel.Controls.Add(_btnInitEscl);
            _toolTip.SetToolTip(_btnInitEscl, "Initialize Electronic Steering Column Lock (CEI) - Required after steering lock module replacement. Costs 1 token.");

            _btnDisableBcm = new Button();
            _btnDisableBcm.Text = "Disable BCM";
            _btnDisableBcm.Location = new Point(btnWidth + btnWidth / 2 + 20, 65);
            _btnDisableBcm.Size = new Size(btnWidth / 2, 35);
            _btnDisableBcm.Click += BtnDisableBcm_Click;
            actionPanel.Controls.Add(_btnDisableBcm);
            _toolTip.SetToolTip(_btnDisableBcm, "Disables BCM security for ALL KEYS LOST situations - Allows key programming without existing keys");

            // Tutorial Button at bottom
            var btnTutorial = new Button();
            btnTutorial.Text = "📖 Software tutorial";
            btnTutorial.Location = new Point(xPos + fullWidth / 2 - 100, yPos + 270);
            btnTutorial.Size = new Size(200, 30);
            btnTutorial.Click += (s, e) => OpenUrl("https://patskiller.com/faqs");
            btnTutorial.Name = "btnTutorial";
            btnTutorial.Anchor = AnchorStyles.Bottom;
            tab.Controls.Add(btnTutorial);
            _toolTip.SetToolTip(btnTutorial, "Opens the online software tutorial and FAQ page");
        }

        private void CreateOtherTabContent(TabPage tab)
        {
            int yPos = 15;
            int xPos = 15;
            int btnWidth = tab.ClientSize.Width - 50;
            int btnHeight = 38;
            int spacing = 45;

            // Section: DTC Operations (FREE)
            var lblDtcSection = new Label();
            lblDtcSection.Text = "DTC Operations (FREE)";
            lblDtcSection.Font = new Font(this.Font, FontStyle.Bold);
            lblDtcSection.ForeColor = _colorSuccess;
            lblDtcSection.Location = new Point(xPos, yPos);
            lblDtcSection.AutoSize = true;
            tab.Controls.Add(lblDtcSection);
            yPos += 25;

            var btnClearDtc = CreateOtherButton("Clear All DTCs", BtnClearDtc_Click, 
                "Clears all diagnostic trouble codes from BCM, PCM, TCM, and ABS modules - FREE");
            btnClearDtc.Location = new Point(xPos, yPos);
            btnClearDtc.Size = new Size(btnWidth / 2 - 5, btnHeight);
            tab.Controls.Add(btnClearDtc);

            var btnClearKam = CreateOtherButton("Clear KAM (FREE)", BtnClearKam_Click,
                "Clears Keep Alive Memory - Resets PCM adaptive learning parameters like idle speed and fuel trim - FREE");
            btnClearKam.Location = new Point(xPos + btnWidth / 2 + 5, yPos);
            btnClearKam.Size = new Size(btnWidth / 2 - 5, btnHeight);
            tab.Controls.Add(btnClearKam);
            yPos += spacing;

            // Section: Vehicle Operations (FREE)
            var lblVehicleSection = new Label();
            lblVehicleSection.Text = "Vehicle Operations (FREE)";
            lblVehicleSection.Font = new Font(this.Font, FontStyle.Bold);
            lblVehicleSection.ForeColor = _colorSuccess;
            lblVehicleSection.Location = new Point(xPos, yPos);
            lblVehicleSection.AutoSize = true;
            tab.Controls.Add(lblVehicleSection);
            yPos += 25;

            var btnVehicleReset = CreateOtherButton("Vehicle Reset (BCM+PCM+ABS)", BtnVehicleReset_Click,
                "Soft reset BCM + PCM + ABS together - Does NOT erase keys, DTCs, or configuration - FREE");
            btnVehicleReset.Location = new Point(xPos, yPos);
            btnVehicleReset.Size = new Size(btnWidth / 2 - 5, btnHeight);
            tab.Controls.Add(btnVehicleReset);

            var btnReadKeysCount = CreateOtherButton("Read Keys Count", BtnReadKeysCount_Click,
                "Reads the number of transponder keys currently programmed to the vehicle (0-8) - FREE");
            btnReadKeysCount.Location = new Point(xPos + btnWidth / 2 + 5, yPos);
            btnReadKeysCount.Size = new Size(btnWidth / 2 - 5, btnHeight);
            tab.Controls.Add(btnReadKeysCount);
            yPos += spacing;

            var btnReadModuleInfo = CreateOtherButton("Read All Module Info", BtnReadModuleInfo_Click,
                "Reads part numbers and software versions from BCM, PCM, IPC, and ABS modules - FREE");
            btnReadModuleInfo.Location = new Point(xPos, yPos);
            btnReadModuleInfo.Size = new Size(btnWidth, btnHeight);
            tab.Controls.Add(btnReadModuleInfo);
            yPos += spacing + 10;

            // Section: Token Operations (Costs Tokens)
            var lblTokenSection = new Label();
            lblTokenSection.Text = "Token Operations (Costs Tokens from patskiller.com)";
            lblTokenSection.Font = new Font(this.Font, FontStyle.Bold);
            lblTokenSection.ForeColor = _colorWarning;
            lblTokenSection.Location = new Point(xPos, yPos);
            lblTokenSection.AutoSize = true;
            tab.Controls.Add(lblTokenSection);
            yPos += 25;

            var btnKeypadCode = CreateOtherButton("Read/Write Keypad Code (1 token each)", BtnKeypadCode_Click,
                "Read or write the 5-digit door keypad entry code - Each operation costs 1 token");
            btnKeypadCode.Location = new Point(xPos, yPos);
            btnKeypadCode.Size = new Size(btnWidth, btnHeight);
            tab.Controls.Add(btnKeypadCode);
            yPos += spacing;

            var btnClearP160A = CreateOtherButton("Clear P160A from PCM (1 token)", BtnClearP160A_Click,
                "Clears 'Calibration Parameter Reset Required' - Use if vehicle won't start after key programming");
            btnClearP160A.Location = new Point(xPos, yPos);
            btnClearP160A.Size = new Size(btnWidth / 2 - 5, btnHeight);
            tab.Controls.Add(btnClearP160A);

            var btnClearB10A2 = CreateOtherButton("Clear B10A2 from BCM (1 token)", BtnClearB10A2_Click,
                "Clears 'Configuration Incompatible' DTC from BCM - Fixes BCM/module mismatch errors");
            btnClearB10A2.Location = new Point(xPos + btnWidth / 2 + 5, yPos);
            btnClearB10A2.Size = new Size(btnWidth / 2 - 5, btnHeight);
            tab.Controls.Add(btnClearB10A2);
            yPos += spacing;

            var btnClearCrush = CreateOtherButton("Clear Crush Event (1 token)", BtnClearCrush_Click,
                "Clears crash/collision flag from BCM - Required after collision repairs");
            btnClearCrush.Location = new Point(xPos, yPos);
            btnClearCrush.Size = new Size(btnWidth / 2 - 5, btnHeight);
            tab.Controls.Add(btnClearCrush);

            var btnGateway = CreateOtherButton("Gateway Unlock 2020+ (1 token)", BtnGatewayUnlock_Click,
                "Unlocks security gateway on 2020+ vehicles - Required before any diagnostic operations on newer vehicles");
            btnGateway.Location = new Point(xPos + btnWidth / 2 + 5, yPos);
            btnGateway.Size = new Size(btnWidth / 2 - 5, btnHeight);
            tab.Controls.Add(btnGateway);
            yPos += spacing + 10;

            // Section: BCM Factory Operations (High Cost)
            var lblBcmSection = new Label();
            lblBcmSection.Text = "⚠️ BCM Factory Operations (2-3 Tokens - Requires Scanner After)";
            lblBcmSection.Font = new Font(this.Font, FontStyle.Bold);
            lblBcmSection.ForeColor = _colorError;
            lblBcmSection.Location = new Point(xPos, yPos);
            lblBcmSection.AutoSize = true;
            tab.Controls.Add(lblBcmSection);
            yPos += 25;

            var btnBcmFactory = CreateOtherButton("BCM Factory Defaults (2-3 tokens)", BtnBcmFactoryDefaults_Click,
                "⚠️ WARNING: Resets ALL BCM settings! Requires 2-3 incodes. Vehicle MUST be adapted with scanner (IDS/FDRS) after!");
            btnBcmFactory.BackColor = Color.FromArgb(254, 226, 226);
            btnBcmFactory.Location = new Point(xPos, yPos);
            btnBcmFactory.Size = new Size(btnWidth, btnHeight);
            tab.Controls.Add(btnBcmFactory);
        }

        private Button CreateOtherButton(string text, EventHandler handler, string tooltip)
        {
            var btn = new Button();
            btn.Text = text;
            btn.Click += handler;
            btn.Enabled = false;
            btn.Tag = "otherFunction";
            _toolTip.SetToolTip(btn, tooltip);
            return btn;
        }

        private Panel CreateGroupPanel(string title, int x, int y, int width, int height)
        {
            var panel = new Panel();
            panel.Location = new Point(x, y);
            panel.Size = new Size(width, height);
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.BackColor = Color.FromArgb(249, 250, 251);

            var lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Location = new Point(5, 3);
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font(this.Font, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(75, 85, 99);
            panel.Controls.Add(lblTitle);

            return panel;
        }

        private void PopulateVehicleList()
        {
            if (_cmbVehicles == null) return;
            
            _cmbVehicles.Items.Clear();
            _cmbVehicles.Items.Add("-- Select Vehicle Manually --");
            
            foreach (var vehicle in VehiclePlatforms.GetAllVehicles())
            {
                _cmbVehicles.Items.Add(vehicle.DisplayName);
            }
            
            _cmbVehicles.SelectedIndex = 0;
        }

        #region Token Cost Helpers

        private bool ConfirmTokenCost(int tokenCost, string operation, string details = "")
        {
            if (tokenCost == 0) return true;

            var costText = tokenCost == 1 ? "1 token" : $"{tokenCost} tokens";
            var message = $"This operation will cost {costText} from your patskiller.com account.\n\n" +
                         $"Operation: {operation}\n" +
                         (string.IsNullOrEmpty(details) ? "" : $"\n{details}\n") +
                         $"\nDo you want to continue?";

            var result = MessageBox.Show(message, $"Token Cost: {costText}",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            return result == DialogResult.Yes;
        }

        private void ShowError(string title, string message, Exception? ex = null)
        {
            var fullMessage = message;
            if (ex != null)
            {
                fullMessage += $"\n\nError details: {ex.Message}";
                
                // Add helpful tips based on error type
                if (ex.Message.Contains("Security access denied"))
                {
                    fullMessage += "\n\n💡 Tip: Wait 10 minutes for anti-scan lockout to expire, then try again.";
                }
                else if (ex.Message.Contains("No response"))
                {
                    fullMessage += "\n\n💡 Tip: Check that ignition is ON and OBD cable is connected securely.";
                }
                else if (ex.Message.Contains("incode"))
                {
                    fullMessage += "\n\n💡 Tip: Verify the incode was entered correctly from patskiller.com/calculator.";
                }
            }
            
            MessageBox.Show(fullMessage, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Logger.Error($"{title}: {message}", ex);
        }

        #endregion

        #region Event Handlers

        private void MainForm_Load(object? sender, EventArgs e)
        {
            Logger.Info("MainForm loaded");
            UpdateStatus("Ready - Click Scan to detect J2534 devices");
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            DisconnectDevice();
            SaveSettings();
        }

        private async void BtnScan_Click(object? sender, EventArgs e)
        {
            if (_cmbDevices == null || _btnScan == null || _btnConnect == null) return;
            
            _btnScan.Enabled = false;
            _btnScan.Text = "Scanning...";
            UpdateStatus("Scanning Windows registry for J2534 devices...");
            Logger.Info("Scanning for J2534 devices");

            try
            {
                await Task.Run(() => _deviceManager?.ScanForDevices());
                
                _cmbDevices.Items.Clear();
                var devices = _deviceManager?.GetDeviceNames() ?? new List<string>();
                
                if (devices.Count == 0)
                {
                    _cmbDevices.Items.Add("-- No J2534 devices found --");
                    UpdateStatus("No J2534 devices found. Please install device drivers.");
                    Logger.Warning("No J2534 devices found");
                }
                else
                {
                    _cmbDevices.Items.Add($"-- Select from {devices.Count} device(s) --");
                    foreach (var device in devices)
                    {
                        _cmbDevices.Items.Add(device);
                    }
                    UpdateStatus($"Found {devices.Count} J2534 device(s)");
                    Logger.Info($"Found {devices.Count} J2534 devices");
                }
                
                _cmbDevices.SelectedIndex = 0;
                _cmbDevices.SelectedIndexChanged += CmbDevices_SelectedIndexChanged;
            }
            catch (Exception ex)
            {
                ShowError("Scan Error", "Error scanning for devices", ex);
            }
            finally
            {
                _btnScan.Text = "Scan";
                _btnScan.Enabled = true;
            }
        }

        private void CmbDevices_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_cmbDevices == null || _btnConnect == null) return;
            _btnConnect.Enabled = _cmbDevices.SelectedIndex > 0;
        }

        private async void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_isConnectedToDevice)
            {
                DisconnectDevice();
                return;
            }

            if (_cmbDevices == null || _btnConnect == null || _btnReadVehicle == null) return;
            if (_cmbDevices.SelectedIndex <= 0) return;

            var deviceName = _cmbDevices.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(deviceName)) return;

            _btnConnect.Enabled = false;
            _btnConnect.Text = "Connecting...";
            UpdateStatus($"Connecting to {deviceName}...");

            try
            {
                await Task.Run(() =>
                {
                    _connectedDevice = _deviceManager?.ConnectToDevice(deviceName);
                });

                if (_connectedDevice != null)
                {
                    _isConnectedToDevice = true;
                    _btnConnect.Text = "✓ Connected - Click to Disconnect";
                    _btnConnect.BackColor = _colorSuccess;
                    _btnReadVehicle.Enabled = true;
                    _cmbVehicles!.Enabled = true;
                    _cmbDevices.Enabled = false;
                    _btnScan!.Enabled = false;
                    UpdateStatus($"Connected to {deviceName} - Click Read Vehicle to continue");
                    Logger.Info($"Connected to device: {deviceName}");
                }
            }
            catch (Exception ex)
            {
                ShowError("Connection Error", "Error connecting to device", ex);
                _btnConnect.Text = "Connect to Device";
                _btnConnect.BackColor = _colorOrange;
            }
            finally
            {
                _btnConnect.Enabled = true;
            }
        }

        private async void BtnReadVehicle_Click(object? sender, EventArgs e)
        {
            if (_connectedDevice == null || _btnReadVehicle == null) return;

            _btnReadVehicle.Enabled = false;
            _btnReadVehicle.Text = "Reading vehicle...";
            UpdateStatus("Reading vehicle VIN from CAN bus...");
            Logger.Info("Reading vehicle VIN");

            try
            {
                _hsCanChannel = await Task.Run(() => 
                    _connectedDevice.OpenChannel(Protocol.ISO15765, 500000));

                if (_hsCanChannel == null)
                {
                    throw new Exception("Failed to open CAN channel");
                }

                var uds = new UdsService(_hsCanChannel);
                var vin = await Task.Run(() => uds.ReadVIN());

                if (string.IsNullOrEmpty(vin))
                {
                    UpdateStatus("Could not read VIN - Please select vehicle manually");
                    Logger.Warning("Could not read VIN from vehicle");
                    _cmbVehicles!.Focus();
                    return;
                }

                Logger.Info($"VIN Read: {vin}");

                var decoded = VinDecoder.Decode(vin);
                
                if (decoded != null)
                {
                    _detectedVehicle = decoded;
                    await ConnectToVehicle(decoded);
                }
                else
                {
                    UpdateStatus($"VIN: {vin} - Unknown vehicle, please select manually");
                    Logger.Warning($"Could not decode VIN: {vin}");
                }
            }
            catch (Exception ex)
            {
                ShowError("Read Error", "Error reading vehicle", ex);
                UpdateStatus("Error reading vehicle - Please select manually");
            }
            finally
            {
                _btnReadVehicle.Text = "🔍 Read Vehicle (Auto-Detect from VIN)";
                _btnReadVehicle.Enabled = true;
            }
        }

        private async Task ConnectToVehicle(VehicleInfo vehicle)
        {
            UpdateStatus($"Detected: {vehicle.DisplayName}");
            Logger.Info($"Auto-detected vehicle: {vehicle.DisplayName}");

            _chkKeyless!.Checked = vehicle.SupportsKeyless;
            
            UpdateStatus("Reading BCM module data...");
            
            try
            {
                var uds = new UdsService(_hsCanChannel!);
                
                var bcmPart = await Task.Run(() => uds.ReadPartNumber(ModuleAddresses.BCM_TX));
                var voltage = await Task.Run(() => _connectedDevice!.ReadBatteryVoltage());
                var keysCount = await Task.Run(() => uds.ReadKeysCount());
                
                UpdateStatus("Reading PATS outcode...");
                _currentOutcode = await Task.Run(() => uds.ReadOutcode());

                _isConnectedToVehicle = true;
                ShowVehicleConnectedUI(vehicle, vehicle.VIN, bcmPart, voltage, keysCount);
                
                Logger.Info($"Vehicle connected - Outcode: {_currentOutcode}");
                UpdateStatus($"Ready - Outcode: {_currentOutcode}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error reading vehicle data: {ex.Message}", ex);
                throw;
            }
        }

        private void ShowVehicleConnectedUI(VehicleInfo vehicle, string vin, string bcmPart, 
            double voltage, int keysCount)
        {
            _vehicleInfoPanel!.Visible = true;
            _lblVehicleInfo!.Text = $"Vehicle: {vehicle.DisplayName}\n" +
                                   $"VIN: {vin}  |  BCM: {bcmPart}  |  " +
                                   $"Battery: {voltage:F1}V  |  Keys: {keysCount}";

            var codePanel = _tabControl?.TabPages[0].Controls["codePanel"] as Panel;
            var actionPanel = _tabControl?.TabPages[0].Controls["actionPanel"] as Panel;
            
            if (codePanel != null)
            {
                codePanel.Visible = true;
                codePanel.Location = new Point(codePanel.Location.X, 
                    _vehicleInfoPanel.Location.Y + _vehicleInfoPanel.Height + 10);
            }
            
            if (actionPanel != null)
            {
                actionPanel.Visible = true;
                actionPanel.Location = new Point(actionPanel.Location.X,
                    codePanel!.Location.Y + codePanel.Height + 10);
            }

            _txtOutcode!.Text = _currentOutcode;
            
            // Enable other functions
            foreach (Control ctrl in _tabControl!.TabPages[1].Controls)
            {
                if (ctrl.Tag?.ToString() == "otherFunction")
                {
                    ctrl.Enabled = true;
                }
            }

            var btnTutorial = _tabControl.TabPages[0].Controls["btnTutorial"] as Button;
            if (btnTutorial != null && actionPanel != null)
            {
                btnTutorial.Location = new Point(btnTutorial.Location.X,
                    actionPanel.Location.Y + actionPanel.Height + 10);
            }
        }

        private void BtnCopyOutcode_Click(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentOutcode))
            {
                Clipboard.SetText(_currentOutcode);
                UpdateStatus("Outcode copied to clipboard");
                Logger.Info("Outcode copied to clipboard");
            }
        }

        private void BtnGetIncode_Click(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentOutcode))
            {
                Clipboard.SetText(_currentOutcode);
            }
            OpenUrl("https://patskiller.com/calculator");
            Logger.Info("Opened patskiller.com/calculator");
        }

        private async void BtnProgramKeys_Click(object? sender, EventArgs e)
        {
            var incode = _txtIncode?.Text?.Trim();
            if (string.IsNullOrEmpty(incode))
            {
                MessageBox.Show("Please enter the incode from patskiller.com/calculator",
                    "Incode Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_hsCanChannel == null)
            {
                MessageBox.Show("Vehicle not connected", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var result = MessageBox.Show(
                "Ready to program new keys.\n\n" +
                "✅ Key programming is FREE after incode obtained\n" +
                "(Same incode = unlimited keys until outcode changes)\n\n" +
                "IMPORTANT:\n" +
                "• Turn ignition ON\n" +
                "• Insert the key to be programmed\n" +
                "• Keep key inserted until programming completes\n\n" +
                "Continue?",
                "Program New Keys",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            try
            {
                _btnProgramKeys!.Enabled = false;
                UpdateStatus("Programming keys...");
                Logger.Info($"Starting key programming with incode: {incode}");

                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);

                if (_autoDisableAlarm)
                {
                    UpdateStatus("Disabling alarm...");
                    await Task.Run(() => pats.DisableAlarm());
                }

                UpdateStatus("Writing key data...");
                var success = await Task.Run(() => pats.ProgramKeys(incode));

                if (success)
                {
                    Logger.Info("Key programming successful");
                    MessageBox.Show(
                        "✅ Key programming successful!\n\n" +
                        "You can program additional keys by repeating this process.\n" +
                        "(Same incode works until outcode changes)",
                        "Success",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    UpdateStatus("Key programming completed successfully");
                }
                else
                {
                    throw new Exception("Key programming failed - vehicle did not confirm");
                }
            }
            catch (Exception ex)
            {
                ShowError("Programming Error", "Key programming failed", ex);
                UpdateStatus("Key programming failed");
            }
            finally
            {
                _btnProgramKeys!.Enabled = true;
            }
        }

        private async void BtnEraseKeys_Click(object? sender, EventArgs e)
        {
            var incode = _txtIncode?.Text?.Trim();
            if (string.IsNullOrEmpty(incode))
            {
                MessageBox.Show("Please enter the incode from patskiller.com/calculator",
                    "Incode Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_KEY_ERASE, "Erase All Keys",
                "⚠️ WARNING: This will ERASE ALL KEYS!\nAfter erasing, you must program at least 2 new keys."))
                return;

            try
            {
                _btnEraseKeys!.Enabled = false;
                UpdateStatus("Erasing all keys...");
                Logger.Info("Starting key erase operation");

                var uds = new UdsService(_hsCanChannel!);
                var pats = new PatsOperations(uds);

                if (_autoDisableAlarm)
                {
                    UpdateStatus("Disabling alarm...");
                    await Task.Run(() => pats.DisableAlarm());
                }

                var success = await Task.Run(() => pats.EraseAllKeys(incode));

                if (success)
                {
                    Logger.Info("Keys erased successfully");
                    MessageBox.Show(
                        "All keys have been erased.\n\n" +
                        "You must now program at least 2 new keys to start the vehicle.",
                        "Keys Erased",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    UpdateStatus("Keys erased - Program new keys to continue");
                }
            }
            catch (Exception ex)
            {
                ShowError("Erase Error", "Key erase failed", ex);
            }
            finally
            {
                _btnEraseKeys!.Enabled = true;
            }
        }

        private async void BtnParameterReset_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            try
            {
                UpdateStatus("Performing parameter reset...");
                Logger.Info("Starting parameter reset");

                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);

                await Task.Run(() => pats.ParameterReset());

                Logger.Info("Parameter reset completed");
                MessageBox.Show("Parameter reset completed successfully.",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("Parameter reset completed");
            }
            catch (Exception ex)
            {
                ShowError("Parameter Reset Error", "Parameter reset failed", ex);
            }
        }

        private async void BtnInitEscl_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_ESCL_INIT, "Initialize ESCL",
                "Initializes the Electronic Steering Column Lock (CEI)\nRequired after steering lock module replacement"))
                return;

            try
            {
                UpdateStatus("Initializing ESCL...");
                Logger.Info("Starting ESCL initialization");

                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);

                await Task.Run(() => pats.InitializeESCL());

                Logger.Info("ESCL initialization completed");
                MessageBox.Show("ESCL initialized successfully.",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("ESCL initialization completed");
            }
            catch (Exception ex)
            {
                ShowError("ESCL Error", "ESCL initialization failed", ex);
            }
        }

        private async void BtnDisableBcm_Click(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "⚠️ DISABLE BCM SECURITY\n\n" +
                "This function is for ALL KEYS LOST situations only.\n" +
                "It will disable the BCM security to allow key programming.\n\n" +
                "Continue?",
                "Disable BCM Security",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            try
            {
                UpdateStatus("Disabling BCM security...");
                Logger.Info("Starting BCM security disable");

                var uds = new UdsService(_hsCanChannel!);
                var pats = new PatsOperations(uds);

                await Task.Run(() => pats.DisableBcmSecurity());

                Logger.Info("BCM security disabled");
                MessageBox.Show("BCM security disabled.\nYou can now program new keys.",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("BCM security disabled - Ready to program keys");
            }
            catch (Exception ex)
            {
                ShowError("BCM Disable Error", "Failed to disable BCM security", ex);
            }
        }

        private async void BtnClearDtc_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            try
            {
                UpdateStatus("Clearing DTCs...");
                var uds = new UdsService(_hsCanChannel);
                await Task.Run(() => uds.ClearDTCs());
                MessageBox.Show("DTCs cleared successfully from all modules.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("DTCs cleared");
                Logger.Info("DTCs cleared");
            }
            catch (Exception ex)
            {
                ShowError("Clear DTC Error", "Failed to clear DTCs", ex);
            }
        }

        private async void BtnVehicleReset_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            try
            {
                UpdateStatus("Performing vehicle reset (BCM + PCM + ABS)...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                
                await Task.Run(() => pats.VehicleReset());
                
                MessageBox.Show("Vehicle reset completed.\nBCM, PCM, and ABS have been soft reset.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("Vehicle reset completed");
                Logger.Info("Vehicle reset completed");
            }
            catch (Exception ex)
            {
                ShowError("Vehicle Reset Error", "Vehicle reset failed", ex);
            }
        }

        private async void BtnReadKeysCount_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            try
            {
                var uds = new UdsService(_hsCanChannel);
                var count = await Task.Run(() => uds.ReadKeysCount());
                MessageBox.Show($"Keys programmed: {count}", "Keys Count",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Logger.Info($"Keys count: {count}");
            }
            catch (Exception ex)
            {
                ShowError("Read Keys Error", "Failed to read keys count", ex);
            }
        }

        private async void BtnKeypadCode_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            var choice = MessageBox.Show(
                "Door Keypad Code Operation\n\n" +
                "Click YES to READ the current code (1 token)\n" +
                "Click NO to WRITE a new code (1 token)\n" +
                "Click Cancel to abort",
                "Keypad Code",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel) return;

            if (choice == DialogResult.Yes)
            {
                // READ keypad code
                if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_KEYPAD_READ, "Read Keypad Code"))
                    return;

                try
                {
                    UpdateStatus("Reading keypad code...");
                    var uds = new UdsService(_hsCanChannel);
                    var pats = new PatsOperations(uds);
                    
                    var code = await Task.Run(() => pats.ReadKeypadCode());
                    
                    MessageBox.Show($"Door Keypad Code: {code}\n\n(Digits 1-9 only)", "Keypad Code",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    UpdateStatus($"Keypad code: {code}");
                    Logger.Info($"Keypad code read: {code}");
                }
                catch (Exception ex)
                {
                    ShowError("Read Keypad Error", "Failed to read keypad code", ex);
                }
            }
            else
            {
                // WRITE keypad code
                var inputCode = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter new 5-digit keypad code (digits 1-9 only):",
                    "Write Keypad Code", "");

                if (string.IsNullOrEmpty(inputCode)) return;

                if (inputCode.Length != 5)
                {
                    MessageBox.Show("Keypad code must be exactly 5 digits", "Invalid Code",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                foreach (char c in inputCode)
                {
                    if (c < '1' || c > '9')
                    {
                        MessageBox.Show("Keypad code digits must be 1-9 only (no zeros)", "Invalid Code",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_KEYPAD_WRITE, "Write Keypad Code",
                    $"New code: {inputCode}"))
                    return;

                try
                {
                    UpdateStatus("Writing keypad code...");
                    var uds = new UdsService(_hsCanChannel);
                    var pats = new PatsOperations(uds);
                    
                    await Task.Run(() => pats.WriteKeypadCode(inputCode));
                    
                    MessageBox.Show($"Keypad code set to: {inputCode}", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    UpdateStatus("Keypad code written");
                    Logger.Info($"Keypad code written: {inputCode}");
                }
                catch (Exception ex)
                {
                    ShowError("Write Keypad Error", "Failed to write keypad code", ex);
                }
            }
        }

        private async void BtnReadModuleInfo_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            try
            {
                UpdateStatus("Reading module info...");
                var uds = new UdsService(_hsCanChannel);
                
                var info = await Task.Run(() => uds.ReadAllModuleInfo());
                
                var msg = "MODULE INFORMATION\n" +
                         "==================\n\n" + info;
                
                MessageBox.Show(msg, "Module Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Logger.Info("Module info read");
            }
            catch (Exception ex)
            {
                ShowError("Read Module Error", "Failed to read module info", ex);
            }
        }

        private async void BtnClearP160A_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_CLEAR_P160A, "Clear P160A from PCM",
                "Clears 'Calibration Parameter Reset Required' DTC\nUse this if vehicle won't start after key programming"))
                return;

            try
            {
                UpdateStatus("Clearing P160A from PCM...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                
                await Task.Run(() => pats.ClearP160A());
                
                MessageBox.Show("P160A cleared from PCM.\n\nPerform ignition cycle:\n• ON 5 sec → OFF 15 sec → ON", 
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("P160A cleared");
                Logger.Info("P160A cleared");
            }
            catch (Exception ex)
            {
                ShowError("Clear P160A Error", "Failed to clear P160A", ex);
            }
        }

        private async void BtnClearB10A2_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_CLEAR_B10A2, "Clear B10A2 from BCM",
                "Clears 'Configuration Incompatible' DTC from BCM"))
                return;

            try
            {
                UpdateStatus("Clearing B10A2 from BCM...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                
                await Task.Run(() => pats.ClearB10A2());
                
                MessageBox.Show("B10A2 cleared from BCM.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("B10A2 cleared");
                Logger.Info("B10A2 cleared");
            }
            catch (Exception ex)
            {
                ShowError("Clear B10A2 Error", "Failed to clear B10A2", ex);
            }
        }

        private async void BtnClearCrush_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_CLEAR_CRUSH, "Clear Crush Event",
                "Clears crash/collision flag from BCM\nRequired after collision repairs"))
                return;

            try
            {
                UpdateStatus("Clearing crush event...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                
                await Task.Run(() => pats.ClearCrushEvent());
                
                MessageBox.Show("Crush event cleared from BCM.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("Crush event cleared");
                Logger.Info("Crush event cleared");
            }
            catch (Exception ex)
            {
                ShowError("Clear Crush Error", "Failed to clear crush event", ex);
            }
        }

        private async void BtnClearKam_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            try
            {
                UpdateStatus("Clearing KAM...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                
                await Task.Run(() => pats.ClearKAM());
                
                MessageBox.Show("Keep Alive Memory cleared.\n\nPCM will re-learn adaptive parameters.", 
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("KAM cleared");
                Logger.Info("KAM cleared");
            }
            catch (Exception ex)
            {
                ShowError("Clear KAM Error", "Failed to clear KAM", ex);
            }
        }

        private async void BtnGatewayUnlock_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            // First check if gateway exists
            UpdateStatus("Checking for security gateway...");
            var uds = new UdsService(_hsCanChannel);
            var pats = new PatsOperations(uds);
            
            var hasGateway = await Task.Run(() => pats.DetectGateway());
            
            if (!hasGateway)
            {
                MessageBox.Show("No security gateway detected.\n\nThis vehicle does not require gateway unlock.",
                    "No Gateway", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("No gateway detected");
                return;
            }

            var incode = _txtIncode?.Text?.Trim();
            if (string.IsNullOrEmpty(incode))
            {
                MessageBox.Show("Please enter the incode from patskiller.com/calculator",
                    "Incode Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_GATEWAY_UNLOCK, "Unlock Security Gateway",
                "Unlocks 2020+ security gateway for diagnostic access"))
                return;

            try
            {
                UpdateStatus("Unlocking gateway...");
                
                await Task.Run(() => pats.UnlockGateway(incode));
                
                MessageBox.Show("Security gateway unlocked.\n\nYou can now perform diagnostic operations.",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("Gateway unlocked");
                Logger.Info("Gateway unlocked");
            }
            catch (Exception ex)
            {
                ShowError("Gateway Unlock Error", "Failed to unlock gateway", ex);
            }
        }

        private async void BtnBcmFactoryDefaults_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) return;

            // Big warning
            var warning = MessageBox.Show(
                "⚠️ BCM FACTORY DEFAULTS - CRITICAL WARNING ⚠️\n\n" +
                "This operation will RESET ALL BCM CONFIGURATION including:\n" +
                "• Window positions and settings\n" +
                "• Door lock settings\n" +
                "• Lighting configurations\n" +
                "• Remote start settings\n" +
                "• All personalization options\n\n" +
                "⚡ REQUIRES 2-3 INCODES (2-3 tokens)\n" +
                "⚡ Vehicle MUST be adapted with scanner (IDS/FDRS) after!\n\n" +
                "Are you ABSOLUTELY SURE you want to continue?",
                "BCM Factory Defaults - WARNING",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (warning != DialogResult.Yes) return;

            // Get incodes
            var incode1 = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter FIRST incode (Level 1):", "BCM Factory Defaults", "");
            if (string.IsNullOrEmpty(incode1)) return;

            var incode2 = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter SECOND incode (Level 2):", "BCM Factory Defaults", "");
            if (string.IsNullOrEmpty(incode2)) return;

            var incode3 = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter THIRD incode (Level 3) or leave blank if not needed:", 
                "BCM Factory Defaults", "");

            var incodes = string.IsNullOrEmpty(incode3) 
                ? new[] { incode1, incode2 } 
                : new[] { incode1, incode2, incode3 };

            if (!ConfirmTokenCost(incodes.Length, "BCM Factory Defaults",
                "⚠️ This resets ALL BCM settings!\nVehicle requires scanner adaptation after!"))
                return;

            try
            {
                UpdateStatus("Performing BCM Factory Defaults...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                
                await Task.Run(() => pats.BcmFactoryDefaults(incodes));
                
                MessageBox.Show(
                    "BCM Factory Defaults completed!\n\n" +
                    "⚠️ IMPORTANT: Vehicle MUST be adapted with\n" +
                    "a factory scanner (IDS/FDRS) to restore\n" +
                    "proper BCM functionality.",
                    "BCM Reset Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                UpdateStatus("BCM Factory Defaults completed - SCANNER ADAPTATION REQUIRED");
                Logger.Info("BCM Factory Defaults completed");
            }
            catch (Exception ex)
            {
                ShowError("BCM Factory Defaults Error", "BCM Factory Defaults failed", ex);
            }
        }

        private void ShowAbout_Click(object? sender, EventArgs e)
        {
            MessageBox.Show(
                "PatsKiller Pro v2.0.0\n\n" +
                "Ford & Lincoln PATS Key Programming Solution\n\n" +
                "© 2026 PatsKiller. All rights reserved.\n\n" +
                "Website: https://patskiller.com\n" +
                "Support: support@patskiller.com",
                "About PatsKiller Pro",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        #endregion

        #region Helper Methods

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

            if (_btnConnect != null)
            {
                _btnConnect.Text = "Connect to Device";
                _btnConnect.BackColor = _colorOrange;
            }
            if (_btnReadVehicle != null) _btnReadVehicle.Enabled = false;
            if (_cmbDevices != null) _cmbDevices.Enabled = true;
            if (_btnScan != null) _btnScan.Enabled = true;
            if (_cmbVehicles != null) _cmbVehicles.Enabled = false;

            UpdateStatus("Disconnected");
            Logger.Info("Device disconnected");
        }

        private void UpdateStatus(string message)
        {
            if (_lblStatusBar != null && !_lblStatusBar.IsDisposed)
            {
                if (_lblStatusBar.InvokeRequired)
                {
                    _lblStatusBar.Invoke(() => _lblStatusBar.Text = $"Status: {message}");
                }
                else
                {
                    _lblStatusBar.Text = $"Status: {message}";
                }
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to open URL: {ex.Message}", ex);
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
