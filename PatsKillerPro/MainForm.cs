using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Services;
using PatsKillerPro.J2534;

namespace PatsKillerPro
{
    /// <summary>
    /// Main application form with full service integration.
    /// All operations implemented using:
    /// - J2534Service: Device and vehicle communication
    /// - IncodeService: Provider API for incode calculation
    /// - TokenBalanceService: Token management
    /// - ProActivityLogger: Activity logging
    /// 
    /// TOKEN COSTS:
    /// - Key Session: 1 token (unlimited keys while same outcode)
    /// - Parameter Reset: 1 token per module (BCM, ABS, PCM)
    /// - Utility Operations: 1 token each
    /// - Gateway Unlock: 1 token (then key ops FREE for 10 min)
    /// - Diagnostics: FREE
    /// </summary>
    public partial class MainForm : Form
    {
        // ============ COLORS ============
        private static class AppColors
        {
            public static Color HeaderBackground = Color.FromArgb(30, 64, 175);
            public static Color HeaderText = Color.White;
            public static Color SessionBannerBg = Color.FromArgb(34, 197, 94);
            public static Color SessionBannerText = Color.White;
            public static Color PurchaseTokenBg = Color.FromArgb(34, 197, 94);
            public static Color PromoTokenBg = Color.FromArgb(134, 239, 172);
            public static Color PromoTokenText = Color.Black;
            public static Color PanelBackground = Color.White;
            public static Color FormBackground = Color.FromArgb(229, 231, 235);
            public static Color ButtonPrimary = Color.FromArgb(59, 130, 246);
            public static Color ButtonSuccess = Color.FromArgb(34, 197, 94);
            public static Color ButtonDanger = Color.FromArgb(239, 68, 68);
            public static Color ButtonWarning = Color.FromArgb(245, 158, 11);
            public static Color Success = Color.FromArgb(22, 163, 74);
            public static Color Warning = Color.FromArgb(202, 138, 4);
            public static Color Error = Color.FromArgb(220, 38, 38);
            public static Color Info = Color.FromArgb(107, 114, 128);
            public static Color LogBackground = Color.Black;
            public static Color LogSuccess = Color.FromArgb(74, 222, 128);
            public static Color LogWarning = Color.FromArgb(250, 204, 21);
            public static Color LogError = Color.FromArgb(248, 113, 113);
            public static Color LogInfo = Color.FromArgb(156, 163, 175);
        }

        // ============ STATE ============
        private bool _gatewaySessionActive;
        private int _gatewaySessionSecondsRemaining;
        private string _currentIncode = "";
        private List<J2534DeviceInfo> _availableDevices = new();

        // ============ UI CONTROLS ============
        private Panel _headerPanel = null!;
        private Label _lblTitle = null!;
        private Label _lblUserEmail = null!;
        private Label _lblPurchaseTokens = null!;
        private Label _lblPromoTokens = null!;
        private Button _btnAccount = null!;
        private Panel _sessionBanner = null!;
        private Label _lblSessionStatus = null!;
        private Label _lblSessionTimer = null!;
        private TabControl _tabControl = null!;
        private TabPage _tabPats = null!;
        private TabPage _tabUtility = null!;
        private TabPage _tabFree = null!;
        private ComboBox _cmbDevice = null!;
        private Button _btnScan = null!;
        private Button _btnConnect = null!;
        private Button _btnDisconnect = null!;
        private Button _btnReadVehicle = null!;
        private Panel _vehicleInfoPanel = null!;
        private Label _lblVin = null!;
        private Label _lblVehicleDesc = null!;
        private Label _lblOutcode = null!;
        private Label _lblBattery = null!;
        private TextBox _txtIncode = null!;
        private Button _btnGetIncode = null!;
        private Button _btnSubmitIncode = null!;
        private Label _lblIncodeStatus = null!;
        private Panel _keyOperationsPanel = null!;
        private Button _btnEraseKeys = null!;
        private Button _btnProgramKeys = null!;
        private Label _lblKeyCount = null!;
        private Panel _paramResetPanel = null!;
        private CheckBox _chkSkipAbs = null!;
        private Button _btnStartParamReset = null!;
        private Label _lblParamResetStatus = null!;
        private ProgressBar _paramResetProgress = null!;
        private Panel _gatewayPanel = null!;
        private Button _btnGatewayUnlock = null!;
        private RichTextBox _rtbLog = null!;
        private StatusStrip _statusBar = null!;
        private ToolStripStatusLabel _lblStatus = null!;
        private System.Windows.Forms.Timer _gatewayTimer = null!;

        public MainForm()
        {
            InitializeComponent();
            BuildUI();
            WireEvents();
            TokenBalanceService.Instance.BalanceChanged += OnTokenBalanceChanged;
            LogInfo("PatsKiller Pro v2.0 started");
            LogInfo("Click Scan to detect J2534 devices");
            ProActivityLogger.Instance.LogAppStart();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(720, 900);
            this.MinimumSize = new Size(700, 800);
            this.Name = "MainForm";
            this.Text = "PatsKiller Pro v2.0";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = AppColors.FormBackground;
            this.Font = new Font("Segoe UI", 9F);
            this.FormClosing += MainForm_FormClosing;
            this.ResumeLayout(false);
        }

        private void BuildUI()
        {
            BuildHeader();
            BuildSessionBanner();
            BuildTabControl();
            BuildActivityLog();
            BuildStatusBar();
            UpdateTokenDisplay();
        }

        private void BuildHeader()
        {
            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = AppColors.HeaderBackground };
            _lblTitle = new Label { Text = "🔑 PatsKiller Pro v2.0", ForeColor = AppColors.HeaderText, Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true, Location = new Point(10, 12) };
            var lblBadge = new Label { Text = "FORD PATS", ForeColor = AppColors.HeaderText, BackColor = AppColors.ButtonPrimary, Font = new Font("Segoe UI", 8, FontStyle.Bold), Padding = new Padding(6, 2, 6, 2), AutoSize = true, Location = new Point(200, 14) };
            _lblPromoTokens = new Label { Text = "", ForeColor = AppColors.PromoTokenText, BackColor = AppColors.PromoTokenBg, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(8, 4, 8, 4), AutoSize = true, Visible = false };
            _lblPurchaseTokens = new Label { Text = "", ForeColor = AppColors.HeaderText, BackColor = AppColors.PurchaseTokenBg, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(8, 4, 8, 4), AutoSize = true, Visible = false };
            _lblUserEmail = new Label { Text = "", ForeColor = AppColors.HeaderText, Font = new Font("Segoe UI", 9), AutoSize = true, Visible = false };
            _btnAccount = new Button { Text = "▼", ForeColor = AppColors.HeaderText, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(30, 30) };
            _btnAccount.FlatAppearance.BorderSize = 0;
            _headerPanel.Controls.AddRange(new Control[] { _lblTitle, lblBadge });
            PositionHeaderRightControls();
            this.Controls.Add(_headerPanel);
        }

        private void PositionHeaderRightControls()
        {
            int rightX = 680;
            _btnAccount.Location = new Point(rightX - 30, 8);
            _headerPanel.Controls.Add(_btnAccount);
            rightX -= 40;
            if (_lblPromoTokens.Visible) { _lblPromoTokens.Location = new Point(rightX - _lblPromoTokens.PreferredWidth, 10); _headerPanel.Controls.Add(_lblPromoTokens); rightX -= _lblPromoTokens.PreferredWidth + 10; }
            _lblPurchaseTokens.Location = new Point(rightX - 60, 10);
            _headerPanel.Controls.Add(_lblPurchaseTokens);
        }

        private void BuildSessionBanner()
        {
            _sessionBanner = new Panel { Dock = DockStyle.Top, Height = 35, BackColor = AppColors.SessionBannerBg, Visible = false };
            _lblSessionStatus = new Label { Text = "🔓 Gateway Session Active - Key programming is FREE!", ForeColor = AppColors.SessionBannerText, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Location = new Point(10, 7) };
            _lblSessionTimer = new Label { Text = "10:00", ForeColor = AppColors.SessionBannerText, Font = new Font("Consolas", 12, FontStyle.Bold), AutoSize = true, Location = new Point(600, 5) };
            _sessionBanner.Controls.AddRange(new Control[] { _lblSessionStatus, _lblSessionTimer });
            this.Controls.Add(_sessionBanner);
            _sessionBanner.BringToFront();
            _gatewayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _gatewayTimer.Tick += GatewayTimer_Tick;
        }

        private void BuildTabControl()
        {
            _tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            _tabPats = new TabPage { Text = "🔑 PATS Operations", BackColor = AppColors.PanelBackground, Padding = new Padding(10) };
            BuildPatsTab();
            _tabUtility = new TabPage { Text = "🔧 Utility (1 token)", BackColor = AppColors.PanelBackground, Padding = new Padding(10) };
            BuildUtilityTab();
            _tabFree = new TabPage { Text = "⚙️ Free Functions", BackColor = AppColors.PanelBackground, Padding = new Padding(10) };
            BuildFreeTab();
            _tabControl.TabPages.AddRange(new[] { _tabPats, _tabUtility, _tabFree });
            this.Controls.Add(_tabControl);
        }

        private void BuildPatsTab()
        {
            var container = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            int y = 10;

            // Device Panel
            var devicePanel = new Panel { Size = new Size(580, 120), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Location = new Point(10, y) };
            devicePanel.Controls.Add(new Label { Text = "🔌 J2534 Device", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });
            _cmbDevice = new ComboBox { Location = new Point(15, 30), Size = new Size(350, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbDevice.Items.Add("Click Scan to detect devices...");
            _cmbDevice.SelectedIndex = 0;
            _btnScan = new Button { Text = "🔍 Scan", BackColor = AppColors.ButtonPrimary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(80, 30), Location = new Point(375, 28) };
            _btnConnect = new Button { Text = "Connect", BackColor = AppColors.ButtonSuccess, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(90, 30), Location = new Point(15, 70), Enabled = false };
            _btnDisconnect = new Button { Text = "Disconnect", BackColor = AppColors.ButtonDanger, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(90, 30), Location = new Point(115, 70), Enabled = false };
            _btnReadVehicle = new Button { Text = "📖 Read Vehicle", BackColor = AppColors.ButtonPrimary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(130, 30), Location = new Point(215, 70), Enabled = false };
            devicePanel.Controls.AddRange(new Control[] { _cmbDevice, _btnScan, _btnConnect, _btnDisconnect, _btnReadVehicle });
            container.Controls.Add(devicePanel);
            y += 135;

            // Vehicle Info Panel
            _vehicleInfoPanel = new Panel { Size = new Size(580, 110), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Location = new Point(10, y), Visible = false };
            _vehicleInfoPanel.Controls.Add(new Label { Text = "🚗 Vehicle Information", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });
            _lblVin = new Label { Location = new Point(15, 28), AutoSize = true, Font = new Font("Consolas", 11, FontStyle.Bold) };
            _lblVehicleDesc = new Label { Location = new Point(15, 50), AutoSize = true };
            _lblOutcode = new Label { Location = new Point(15, 72), AutoSize = true, Font = new Font("Consolas", 11, FontStyle.Bold), ForeColor = AppColors.ButtonPrimary };
            _lblBattery = new Label { Location = new Point(400, 72), AutoSize = true, ForeColor = AppColors.Info };
            _vehicleInfoPanel.Controls.AddRange(new Control[] { _lblVin, _lblVehicleDesc, _lblOutcode, _lblBattery });
            container.Controls.Add(_vehicleInfoPanel);
            y += 125;

            // Incode Panel
            var incodePanel = new Panel { Size = new Size(580, 90), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Location = new Point(10, y), Visible = false, Name = "incodePanel" };
            incodePanel.Controls.Add(new Label { Text = "🔐 Security Access", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });
            incodePanel.Controls.Add(new Label { Text = "Incode:", Location = new Point(15, 38), AutoSize = true });
            _txtIncode = new TextBox { Location = new Point(70, 34), Size = new Size(120, 25), Font = new Font("Consolas", 11), CharacterCasing = CharacterCasing.Upper };
            _btnGetIncode = new Button { Text = "Calculate", BackColor = AppColors.ButtonPrimary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(85, 28), Location = new Point(200, 33) };
            _btnSubmitIncode = new Button { Text = "✓ Submit", BackColor = AppColors.ButtonSuccess, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(80, 28), Location = new Point(295, 33), Enabled = false };
            _lblIncodeStatus = new Label { Location = new Point(385, 38), AutoSize = true };
            incodePanel.Controls.AddRange(new Control[] { _txtIncode, _btnGetIncode, _btnSubmitIncode, _lblIncodeStatus });
            container.Controls.Add(incodePanel);
            y += 105;

            // Key Operations Panel
            _keyOperationsPanel = new Panel { Size = new Size(580, 100), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Location = new Point(10, y), Visible = false };
            _keyOperationsPanel.Controls.Add(new Label { Text = "🔑 Key Operations (1 token per session)", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });
            _btnEraseKeys = new Button { Text = "🗑️ Erase All Keys", BackColor = AppColors.ButtonDanger, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(150, 35), Location = new Point(15, 35), Enabled = false };
            _btnProgramKeys = new Button { Text = "🔑 Program Keys", BackColor = AppColors.ButtonSuccess, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(150, 35), Location = new Point(180, 35), Enabled = false };
            _lblKeyCount = new Label { Text = "", Location = new Point(350, 42), AutoSize = true, ForeColor = AppColors.Info };
            _keyOperationsPanel.Controls.Add(new Label { Text = "💡 1 token = unlimited keys in session", Location = new Point(15, 75), AutoSize = true, ForeColor = AppColors.Info });
            _keyOperationsPanel.Controls.AddRange(new Control[] { _btnEraseKeys, _btnProgramKeys, _lblKeyCount });
            container.Controls.Add(_keyOperationsPanel);
            y += 115;

            // Parameter Reset Panel
            _paramResetPanel = new Panel { Size = new Size(580, 140), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Location = new Point(10, y), Visible = false };
            _paramResetPanel.Controls.Add(new Label { Text = "🔄 Parameter Reset (1 token per module)", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });
            _paramResetPanel.Controls.Add(new Label { Text = "Auto-detects: BCM + ABS + PCM (3 tokens typical)", Location = new Point(15, 28), AutoSize = true, ForeColor = AppColors.Info });
            _chkSkipAbs = new CheckBox { Text = "Skip ABS (2 modules only) - Saves 1 token!", Location = new Point(15, 50), AutoSize = true, ForeColor = AppColors.Success };
            _btnStartParamReset = new Button { Text = "🔄 Start Parameter Reset", BackColor = AppColors.ButtonPrimary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(200, 35), Location = new Point(15, 80), Enabled = false };
            _lblParamResetStatus = new Label { Location = new Point(230, 88), AutoSize = true };
            _paramResetProgress = new ProgressBar { Location = new Point(15, 120), Size = new Size(430, 12), Visible = false };
            _paramResetPanel.Controls.AddRange(new Control[] { _chkSkipAbs, _btnStartParamReset, _lblParamResetStatus, _paramResetProgress });
            container.Controls.Add(_paramResetPanel);
            y += 155;

            // Gateway Panel
            _gatewayPanel = new Panel { Size = new Size(580, 85), BackColor = Color.FromArgb(254, 243, 199), BorderStyle = BorderStyle.FixedSingle, Location = new Point(10, y), Visible = false };
            _gatewayPanel.Controls.Add(new Label { Text = "🔓 Gateway Unlock (2020+)", Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true, ForeColor = Color.FromArgb(146, 64, 14) });
            _gatewayPanel.Controls.Add(new Label { Text = "✨ Unlock Gateway = FREE key programming for 10 minutes!", Location = new Point(15, 28), AutoSize = true, ForeColor = Color.FromArgb(146, 64, 14) });
            _btnGatewayUnlock = new Button { Text = "🔓 Gateway Unlock (1 token)", BackColor = AppColors.ButtonWarning, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(220, 35), Location = new Point(15, 52), Enabled = false };
            _gatewayPanel.Controls.Add(_btnGatewayUnlock);
            container.Controls.Add(_gatewayPanel);

            _tabPats.Controls.Add(container);
        }

        private void BuildUtilityTab()
        {
            var container = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10) };
            container.Controls.Add(new Label { Text = "🔧 Utility Operations (1 token each)", Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 10) });
            var utilities = new[] { ("Clear Theft Flag", "clear_theft", "Theft Detected - Vehicle Immobilized"), ("Clear Crash Flag", "clear_crash", "Collision/Accident Flag"), ("Clear Crash Input", "clear_crash_input", "Crash Input Failure"), ("BCM Factory Defaults", "bcm_defaults", "Restore BCM config") };
            foreach (var (title, op, desc) in utilities)
            {
                var btn = new Button { Size = new Size(550, 50), BackColor = AppColors.ButtonWarning, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 5), Text = $"{title}\n{desc} (1 token)", Tag = op };
                btn.Click += BtnUtility_Click;
                container.Controls.Add(btn);
            }
            _tabUtility.Controls.Add(container);
        }

        private void BuildFreeTab()
        {
            var container = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10) };
            container.Controls.Add(new Label { Text = "⚙️ Free Functions (No tokens required)", Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 10) });
            var freeOps = new[] { ("Read VIN", "read_vin", "Read VIN from vehicle"), ("Read Key Count", "read_keys", "Count programmed keys"), ("Read DTCs", "read_dtc", "Read diagnostic codes"), ("Clear DTCs", "clear_dtc", "Clear all DTCs"), ("Read Battery", "read_battery", "Check battery voltage") };
            foreach (var (title, op, desc) in freeOps)
            {
                var btn = new Button { Size = new Size(550, 50), BackColor = AppColors.ButtonPrimary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 5), Text = $"{title}\n{desc}", Tag = op };
                btn.Click += BtnFreeOperation_Click;
                container.Controls.Add(btn);
            }
            _tabFree.Controls.Add(container);
        }

        private void BuildActivityLog()
        {
            var logPanel = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(5) };
            logPanel.Controls.Add(new Label { Text = "📋 Activity Log", Font = new Font("Segoe UI", 9, FontStyle.Bold), AutoSize = true, Location = new Point(5, 3) });
            _rtbLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = AppColors.LogBackground, ForeColor = AppColors.LogInfo, Font = new Font("Consolas", 9), ReadOnly = true, BorderStyle = BorderStyle.None };
            logPanel.Controls.Add(_rtbLog);
            this.Controls.Add(logPanel);
        }

        private void BuildStatusBar()
        {
            _statusBar = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel { Text = "Ready", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusBar.Items.AddRange(new ToolStripItem[] { _lblStatus, new ToolStripStatusLabel { Text = "v2.0.0" } });
            this.Controls.Add(_statusBar);
        }

        private void WireEvents()
        {
            _btnScan.Click += BtnScan_Click;
            _btnConnect.Click += BtnConnect_Click;
            _btnDisconnect.Click += BtnDisconnect_Click;
            _btnReadVehicle.Click += BtnReadVehicle_Click;
            _cmbDevice.SelectedIndexChanged += (s, e) => _btnConnect.Enabled = _cmbDevice.SelectedIndex > 0 && _cmbDevice.SelectedIndex <= _availableDevices.Count;
            _txtIncode.TextChanged += (s, e) => _btnSubmitIncode.Enabled = _txtIncode.Text.Length >= 4;
            _btnGetIncode.Click += BtnGetIncode_Click;
            _btnSubmitIncode.Click += BtnSubmitIncode_Click;
            _btnEraseKeys.Click += async (s, e) => await ExecuteKeyOperationAsync("erase");
            _btnProgramKeys.Click += async (s, e) => await ExecuteKeyOperationAsync("program");
            _btnStartParamReset.Click += BtnStartParamReset_Click;
            _btnGatewayUnlock.Click += BtnGatewayUnlock_Click;
            _btnAccount.Click += BtnAccount_Click;
        }

        private void OnTokenBalanceChanged(object? sender, TokenBalanceChangedEventArgs e) { if (InvokeRequired) { BeginInvoke(new Action(() => OnTokenBalanceChanged(sender, e))); return; } UpdateTokenDisplay(); }

        private void UpdateTokenDisplay()
        {
            _lblPurchaseTokens.Text = TokenBalanceService.Instance.RegularTokens.ToString();
            _lblPurchaseTokens.Visible = true;
            _lblPromoTokens.Visible = TokenBalanceService.Instance.PromoTokens > 0;
            if (_lblPromoTokens.Visible) _lblPromoTokens.Text = $"{TokenBalanceService.Instance.PromoTokens} promo";
        }

        private async void BtnScan_Click(object? sender, EventArgs e)
        {
            LogInfo("Scanning for J2534 devices...");
            _btnScan.Enabled = false;
            _cmbDevice.Items.Clear();
            _cmbDevice.Items.Add("Scanning...");
            _availableDevices = await J2534Service.Instance.ScanForDevicesAsync();
            _cmbDevice.Items.Clear();
            _cmbDevice.Items.Add("Select J2534 Device...");
            foreach (var d in _availableDevices) _cmbDevice.Items.Add(d.ToString());
            _cmbDevice.SelectedIndex = 0;
            _btnScan.Enabled = true;
            LogSuccess($"Found {_availableDevices.Count} device(s)");
        }

        private async void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_cmbDevice.SelectedIndex <= 0) return;
            var device = _availableDevices[_cmbDevice.SelectedIndex - 1];
            LogInfo($"Connecting to {device.Name}...");
            _btnConnect.Enabled = false;
            var result = await J2534Service.Instance.ConnectDeviceAsync(device);
            if (result.Success)
            {
                _btnConnect.Text = "✓ Connected"; _btnConnect.BackColor = AppColors.Success; _btnDisconnect.Enabled = true; _cmbDevice.Enabled = false; _btnReadVehicle.Enabled = true;
                LogSuccess($"Connected to {device.Name}");
                ProActivityLogger.Instance.LogJ2534Connection(device.Name, true);
                _lblStatus.Text = "Device ready - Read vehicle";
            }
            else { LogError($"Connection failed: {result.Error}"); _btnConnect.Enabled = true; }
        }

        private async void BtnDisconnect_Click(object? sender, EventArgs e)
        {
            var name = J2534Service.Instance.ConnectedDeviceName ?? "device";
            TokenBalanceService.Instance.EndKeySession();
            await J2534Service.Instance.DisconnectDeviceAsync();
            _btnConnect.Text = "Connect"; _btnConnect.BackColor = AppColors.ButtonSuccess; _btnConnect.Enabled = true; _btnDisconnect.Enabled = false; _cmbDevice.Enabled = true; _btnReadVehicle.Enabled = false;
            _vehicleInfoPanel.Visible = false; FindControl<Panel>("incodePanel")!.Visible = false; _keyOperationsPanel.Visible = false; _paramResetPanel.Visible = false; _gatewayPanel.Visible = false; _sessionBanner.Visible = false;
            LogInfo($"Disconnected from {name}");
            ProActivityLogger.Instance.LogJ2534Disconnect(name);
        }

        private async void BtnReadVehicle_Click(object? sender, EventArgs e)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LogInfo("Reading vehicle from CAN bus...");
            _btnReadVehicle.Enabled = false;
            var result = await J2534Service.Instance.ReadVehicleAsync();
            if (!result.Success) { LogError($"Failed: {result.Error}"); _btnReadVehicle.Enabled = true; return; }
            LogInfo($"VIN: {result.Vin}"); LogInfo($"Vehicle: {result.VehicleInfo}"); LogSuccess($"Outcode: {result.Outcode}"); LogInfo($"Battery: {result.BatteryVoltage:F1}V");
            _lblVin.Text = $"VIN: {result.Vin}"; _lblVehicleDesc.Text = result.VehicleInfo?.ToString() ?? ""; _lblOutcode.Text = $"Outcode: {result.Outcode}"; _lblBattery.Text = $"🔋 {result.BatteryVoltage:F1}V";
            _vehicleInfoPanel.Visible = true; FindControl<Panel>("incodePanel")!.Visible = true;
            if (result.VehicleInfo?.Is2020Plus == true) { _gatewayPanel.Visible = true; _btnGatewayUnlock.Enabled = true; }
            ProActivityLogger.Instance.LogVehicleDetection(result.Vin!, result.VehicleInfo?.Year?.ToString(), result.VehicleInfo?.Model, true, (int)sw.ElapsedMilliseconds);
            _lblStatus.Text = "Vehicle detected - Get incode";
        }

        private async void BtnGetIncode_Click(object? sender, EventArgs e)
        {
            var outcode = J2534Service.Instance.CurrentOutcode;
            if (string.IsNullOrEmpty(outcode)) { LogError("No outcode - read vehicle first"); return; }
            LogInfo($"Calculating incode for: {outcode}...");
            _btnGetIncode.Enabled = false; _lblIncodeStatus.Text = "Calculating..."; _lblIncodeStatus.ForeColor = AppColors.Info;
            var result = await IncodeService.Instance.CalculateIncodeAsync(outcode, J2534Service.Instance.CurrentVin, "BCM");
            if (result.Success && !string.IsNullOrEmpty(result.Incode))
            {
                _txtIncode.Text = result.Incode; _lblIncodeStatus.Text = $"✓ via {result.ProviderUsed}"; _lblIncodeStatus.ForeColor = AppColors.Success;
                LogSuccess($"Incode: {result.Incode} (via {result.ProviderUsed}, {result.ResponseTimeMs}ms)");
            }
            else { _lblIncodeStatus.Text = "✗ Failed"; _lblIncodeStatus.ForeColor = AppColors.Error; LogError($"Failed: {result.Error}"); }
            _btnGetIncode.Enabled = true;
        }

        private async void BtnSubmitIncode_Click(object? sender, EventArgs e)
        {
            _currentIncode = _txtIncode.Text.ToUpper();
            LogInfo($"Submitting incode: {_currentIncode}...");
            _btnSubmitIncode.Enabled = false; _lblIncodeStatus.Text = "Validating...";
            var result = await J2534Service.Instance.SubmitIncodeAsync("BCM", _currentIncode);
            if (result.Success)
            {
                _lblIncodeStatus.Text = "✓ Verified"; _lblIncodeStatus.ForeColor = AppColors.Success;
                LogSuccess("Security access granted");
                _keyOperationsPanel.Visible = true; _paramResetPanel.Visible = true;
                _btnEraseKeys.Enabled = true; _btnProgramKeys.Enabled = true; _btnStartParamReset.Enabled = true;
                var kc = await J2534Service.Instance.ReadKeyCountAsync();
                _lblKeyCount.Text = $"Keys: {kc.KeyCount}/{kc.MaxKeys}";
            }
            else { _lblIncodeStatus.Text = "✗ Invalid"; _lblIncodeStatus.ForeColor = AppColors.Error; LogError($"Rejected: {result.Error}"); _btnSubmitIncode.Enabled = true; }
        }

        private async Task ExecuteKeyOperationAsync(string operation)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var vin = J2534Service.Instance.CurrentVin ?? ""; var outcode = J2534Service.Instance.CurrentOutcode ?? ""; var v = J2534Service.Instance.CurrentVehicle;
            var sess = await TokenBalanceService.Instance.StartKeySessionAsync(vin, outcode);
            if (!sess.Success) { LogError(sess.Error ?? "Token error"); MessageBox.Show(sess.Error, "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!sess.SessionAlreadyActive) { LogInfo("Key session started (1 token)"); ProActivityLogger.Instance.LogKeySessionStart(vin, v?.Year?.ToString(), v?.Model, outcode, true, -1, (int)sw.ElapsedMilliseconds); }
            else LogInfo("Continuing session (no charge)");

            if (operation == "erase")
            {
                LogInfo("Erasing all keys...");
                var r = await J2534Service.Instance.EraseAllKeysAsync(_currentIncode);
                if (r.Success) { LogSuccess($"Erased {r.KeysAffected} keys"); _lblKeyCount.Text = $"Keys: {r.CurrentKeyCount}/8"; ProActivityLogger.Instance.LogEraseAllKeys(vin, v?.Year?.ToString(), v?.Model, r.KeysAffected, true, 0, (int)sw.ElapsedMilliseconds); }
                else LogError($"Erase failed: {r.Error}");
            }
            else if (operation == "program")
            {
                for (int slot = 1; slot <= 2; slot++)
                {
                    LogInfo($"Programming key #{slot}...");
                    var r = await J2534Service.Instance.ProgramKeyAsync(_currentIncode, slot);
                    if (r.Success) { LogSuccess($"Key #{slot} done"); _lblKeyCount.Text = $"Keys: {r.CurrentKeyCount}/8"; ProActivityLogger.Instance.LogKeyProgrammed(vin, v?.Year?.ToString(), v?.Model, slot, true, (int)sw.ElapsedMilliseconds); }
                    else { LogError($"Key #{slot} failed: {r.Error}"); break; }
                }
            }
            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        private async void BtnStartParamReset_Click(object? sender, EventArgs e)
        {
            var vin = J2534Service.Instance.CurrentVin ?? ""; var v = J2534Service.Instance.CurrentVehicle;
            var modules = _chkSkipAbs.Checked ? new[] { ("BCM", "0x726"), ("PCM", "0x7E0") } : new[] { ("BCM", "0x726"), ("ABS", "0x760"), ("PCM", "0x7E0") };
            if (!TokenBalanceService.Instance.HasEnoughTokens(modules.Length)) { LogError($"Need {modules.Length} tokens"); MessageBox.Show($"Need {modules.Length} tokens", "Insufficient", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _btnStartParamReset.Enabled = false; _paramResetProgress.Visible = true; _paramResetProgress.Maximum = modules.Length; _paramResetProgress.Value = 0;
            LogInfo($"Parameter Reset - {modules.Length} modules");
            var totalSw = System.Diagnostics.Stopwatch.StartNew(); var done = new List<string>(); int tokens = 0;
            foreach (var (mod, addr) in modules)
            {
                var modSw = System.Diagnostics.Stopwatch.StartNew();
                _lblParamResetStatus.Text = $"{mod}...";
                var d = await TokenBalanceService.Instance.DeductForParamResetAsync(mod, vin);
                if (!d.Success) { LogError($"Deduct failed for {mod}"); break; }
                tokens++;
                LogInfo($"Reading {mod} outcode...");
                var oc = await J2534Service.Instance.ReadModuleOutcodeAsync(mod);
                if (!oc.Success) { LogError($"{mod} outcode failed"); break; }
                LogSuccess($"{mod} Outcode: {oc.Outcode}");
                LogInfo($"Calculating {mod} incode...");
                var ic = await IncodeService.Instance.CalculateParamResetIncodeAsync(oc.Outcode!, mod, vin);
                if (!ic.Success) { LogError($"{mod} incode failed: {ic.Error}"); break; }
                LogSuccess($"{mod} Incode: {ic.Incode} (via {ic.ProviderUsed})");
                LogInfo($"Submitting to {mod}...");
                var sub = await J2534Service.Instance.SubmitIncodeAsync(mod, ic.Incode!);
                if (!sub.Success) { LogError($"{mod} submit failed"); break; }
                LogSuccess($"{mod} complete!");
                ProActivityLogger.Instance.LogParameterResetModule(vin, v?.Year?.ToString(), v?.Model, mod, oc.Outcode!, ic.Incode!, true, -1, (int)modSw.ElapsedMilliseconds);
                done.Add(mod); _paramResetProgress.Value++;
            }
            _btnStartParamReset.Enabled = true; _paramResetProgress.Visible = false;
            _lblParamResetStatus.Text = done.Count == modules.Length ? "✓ Complete!" : "⚠ Partial"; _lblParamResetStatus.ForeColor = done.Count == modules.Length ? AppColors.Success : AppColors.Warning;
            LogSuccess($"Reset: {done.Count}/{modules.Length} modules, {tokens} tokens");
            ProActivityLogger.Instance.LogParameterResetComplete(vin, v?.Year?.ToString(), v?.Model, done.Count, tokens, (int)totalSw.ElapsedMilliseconds, done.ToArray());
            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        private async void BtnGatewayUnlock_Click(object? sender, EventArgs e)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); var vin = J2534Service.Instance.CurrentVin ?? ""; var v = J2534Service.Instance.CurrentVehicle;
            var d = await TokenBalanceService.Instance.DeductTokensAsync(1, "gateway_unlock", vin);
            if (!d.Success) { LogError(d.Error ?? "Token error"); MessageBox.Show(d.Error, "Insufficient", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            LogInfo("Unlocking Gateway..."); _btnGatewayUnlock.Enabled = false;
            var r = await J2534Service.Instance.UnlockGatewayAsync(_currentIncode);
            if (r.Success)
            {
                _gatewaySessionActive = true; _gatewaySessionSecondsRemaining = r.SessionDurationSeconds;
                LogSuccess("Gateway unlocked!"); LogSuccess("🎉 FREE key ops for 10 min!");
                _sessionBanner.Visible = true; _lblSessionTimer.Text = $"{_gatewaySessionSecondsRemaining / 60}:{_gatewaySessionSecondsRemaining % 60:D2}"; _gatewayTimer.Start();
                _btnEraseKeys.Text = "🗑️ Erase (FREE)"; _btnProgramKeys.Text = "🔑 Program (FREE)";
                ProActivityLogger.Instance.LogUtilityOperation("Gateway Unlock", vin, v?.Year?.ToString(), v?.Model, true, -1, (int)sw.ElapsedMilliseconds);
            }
            else { LogError($"Gateway failed: {r.Error}"); _btnGatewayUnlock.Enabled = true; }
            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        private void GatewayTimer_Tick(object? sender, EventArgs e)
        {
            _gatewaySessionSecondsRemaining--;
            _lblSessionTimer.Text = $"{_gatewaySessionSecondsRemaining / 60}:{_gatewaySessionSecondsRemaining % 60:D2}";
            if (_gatewaySessionSecondsRemaining <= 0) { _gatewayTimer.Stop(); _gatewaySessionActive = false; _sessionBanner.Visible = false; _btnGatewayUnlock.Enabled = true; LogWarning("Gateway expired"); _btnEraseKeys.Text = "🗑️ Erase All Keys"; _btnProgramKeys.Text = "🔑 Program Keys"; }
        }

        private async void BtnUtility_Click(object? sender, EventArgs e)
        {
            var op = (sender as Button)?.Tag?.ToString() ?? ""; var sw = System.Diagnostics.Stopwatch.StartNew(); var vin = J2534Service.Instance.CurrentVin ?? ""; var v = J2534Service.Instance.CurrentVehicle;
            var d = await TokenBalanceService.Instance.DeductForUtilityAsync(op, vin);
            if (!d.Success) { LogError(d.Error ?? "Token error"); MessageBox.Show(d.Error, "Insufficient", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            LogInfo($"Executing {op}...");
            J2534Result r = op switch { "clear_theft" => await J2534Service.Instance.ClearTheftFlagAsync(), "clear_crash" or "clear_crash_input" => await J2534Service.Instance.ClearCrashFlagAsync(), "bcm_defaults" => await J2534Service.Instance.RestoreBcmDefaultsAsync(), _ => new J2534Result { Success = false, Error = "Unknown" } };
            if (r.Success) { LogSuccess($"{op} complete!"); ProActivityLogger.Instance.LogUtilityOperation(op, vin, v?.Year?.ToString(), v?.Model, true, -1, (int)sw.ElapsedMilliseconds); } else LogError($"{op} failed: {r.Error}");
            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        private async void BtnFreeOperation_Click(object? sender, EventArgs e)
        {
            var op = (sender as Button)?.Tag?.ToString() ?? "";
            LogInfo($"Executing {op}...");
            switch (op)
            {
                case "read_vin": LogSuccess($"VIN: {J2534Service.Instance.CurrentVin}"); LogInfo($"Vehicle: {J2534Service.Instance.CurrentVehicle}"); break;
                case "read_keys": var kc = await J2534Service.Instance.ReadKeyCountAsync(); if (kc.Success) { LogSuccess($"Keys: {kc.KeyCount}/{kc.MaxKeys}"); _lblKeyCount.Text = $"Keys: {kc.KeyCount}/{kc.MaxKeys}"; } break;
                case "read_dtc": var dtc = await J2534Service.Instance.ReadDtcsAsync(); if (dtc.Success) { LogSuccess($"Found {dtc.DtcCount} DTCs"); foreach (var c in dtc.Dtcs) LogInfo($"  {c}"); } break;
                case "clear_dtc": var clr = await J2534Service.Instance.ClearDtcsAsync(); if (clr.Success) LogSuccess("DTCs cleared"); else LogError($"Clear failed: {clr.Error}"); break;
                case "read_battery": var volt = await J2534Service.Instance.ReadBatteryVoltageAsync(); LogSuccess($"Battery: {volt:F1}V"); _lblBattery.Text = $"🔋 {volt:F1}V"; break;
            }
        }

        private void BtnAccount_Click(object? sender, EventArgs e)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Buy Tokens", null, (s, ev) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://patskiller.com/buy", UseShellExecute = true }));
            menu.Items.Add("-");
            menu.Items.Add("Logout", null, (s, ev) => { ProActivityLogger.Instance.LogLogout(TokenBalanceService.Instance.UserEmail ?? ""); TokenBalanceService.Instance.ClearAuthContext(); ProActivityLogger.Instance.ClearAuthContext(); IncodeService.Instance.ClearAuthContext(); LogInfo("Logged out"); this.Hide(); var lf = new GoogleLoginForm(); lf.ShowDialog(); if (lf.IsLoggedIn) { UpdateTokenDisplay(); this.Show(); } else Application.Exit(); });
            menu.Show(_btnAccount, new Point(0, _btnAccount.Height));
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e) { TokenBalanceService.Instance.BalanceChanged -= OnTokenBalanceChanged; TokenBalanceService.Instance.EndKeySession(); ProActivityLogger.Instance.LogAppClose(); }

        private T? FindControl<T>(string name) where T : Control { var c = this.Controls.Find(name, true); return c.Length > 0 ? c[0] as T : null; }

        private void LogInfo(string msg) => AppendLog(msg, AppColors.LogInfo);
        private void LogSuccess(string msg) => AppendLog(msg, AppColors.LogSuccess);
        private void LogWarning(string msg) => AppendLog(msg, AppColors.LogWarning);
        private void LogError(string msg) => AppendLog(msg, AppColors.LogError);
        private void AppendLog(string msg, Color c) { if (_rtbLog.InvokeRequired) { _rtbLog.Invoke(() => AppendLog(msg, c)); return; } _rtbLog.SelectionStart = _rtbLog.TextLength; _rtbLog.SelectionColor = c; _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n"); _rtbLog.ScrollToCaret(); }
    }
}
