using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Utils;
using PatsKillerPro.Services;

namespace PatsKillerPro
{
    /// <summary>
    /// Main application form with integrated TokenBalanceService and ProActivityLogger.
    /// 
    /// TOKEN COSTS:
    /// - Key Session: 1 token (unlimited keys while same outcode)
    /// - Parameter Reset: 1 token per module (auto-detected: BCM, ABS, PCM = 3 typically)
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
            public static Color ButtonDisabled = Color.FromArgb(209, 213, 219);
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
        private bool _deviceConnected;
        private bool _vehicleConnected;
        private bool _incodeVerified;
        private bool _gatewaySessionActive;
        private int _gatewaySessionSecondsRemaining;
        private bool _is2020Plus;
        private string _currentVin = "";
        private string _currentOutcode = "";
        private string _currentIncode = "";
        private string? _currentVehicleYear;
        private string? _currentVehicleModel;

        // Parameter Reset State
        private bool _paramResetActive;
        private int _paramResetCurrentStep;
        private ParamResetModule[]? _paramResetModules;
        private bool _skipAbsModule;

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
        private TextBox _txtIncode = null!;
        private Button _btnSubmitIncode = null!;
        private Label _lblIncodeStatus = null!;
        
        private Panel _keyOperationsPanel = null!;
        private Button _btnEraseKeys = null!;
        private Button _btnProgramKeys = null!;
        
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

        // ============ CONSTRUCTOR ============
        public MainForm()
        {
            InitializeComponent();
            BuildUI();
            WireEvents();
            
            // Subscribe to token balance changes
            TokenBalanceService.Instance.BalanceChanged += OnTokenBalanceChanged;
            
            LogInfo("PatsKiller Pro v2.0 started");
            LogInfo("Click Scan to detect J2534 devices");
            
            // Log app start
            ProActivityLogger.Instance.LogAppStart();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(700, 850);
            this.MinimumSize = new Size(650, 750);
            this.Name = "MainForm";
            this.Text = "PatsKiller Pro v2.0";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = AppColors.FormBackground;
            this.Font = new Font("Segoe UI", 9F);
            this.FormClosing += MainForm_FormClosing;
            this.ResumeLayout(false);
        }

        // ============ UI BUILDING ============
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
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 45,
                BackColor = AppColors.HeaderBackground,
                Padding = new Padding(10, 0, 10, 0)
            };

            _lblTitle = new Label
            {
                Text = "🔑 PatsKiller Pro v2.0",
                ForeColor = AppColors.HeaderText,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 12)
            };

            var lblBadge = new Label
            {
                Text = "FORD PATS",
                ForeColor = AppColors.HeaderText,
                BackColor = AppColors.ButtonPrimary,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Padding = new Padding(6, 2, 6, 2),
                AutoSize = true,
                Location = new Point(200, 14)
            };

            // Right side - tokens and user
            _lblPromoTokens = new Label
            {
                Text = "",
                ForeColor = AppColors.PromoTokenText,
                BackColor = AppColors.PromoTokenBg,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Padding = new Padding(8, 4, 8, 4),
                AutoSize = true,
                Visible = false
            };

            _lblPurchaseTokens = new Label
            {
                Text = "",
                ForeColor = AppColors.HeaderText,
                BackColor = AppColors.PurchaseTokenBg,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Padding = new Padding(8, 4, 8, 4),
                AutoSize = true,
                Visible = false
            };

            _lblUserEmail = new Label
            {
                Text = "",
                ForeColor = AppColors.HeaderText,
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                Visible = false
            };

            _btnAccount = new Button
            {
                Text = "▼",
                ForeColor = AppColors.HeaderText,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(30, 30)
            };
            _btnAccount.FlatAppearance.BorderSize = 0;

            _headerPanel.Controls.AddRange(new Control[] { _lblTitle, lblBadge });
            PositionHeaderRightControls();

            this.Controls.Add(_headerPanel);
        }

        private void PositionHeaderRightControls()
        {
            int rightX = _headerPanel.Width - 15;
            
            _btnAccount.Location = new Point(rightX - 30, 8);
            _headerPanel.Controls.Add(_btnAccount);
            rightX -= 40;

            if (_lblPromoTokens.Visible)
            {
                _lblPromoTokens.Location = new Point(rightX - _lblPromoTokens.PreferredWidth, 10);
                _headerPanel.Controls.Add(_lblPromoTokens);
                rightX -= _lblPromoTokens.PreferredWidth + 10;
            }

            _lblPurchaseTokens.Location = new Point(rightX - _lblPurchaseTokens.PreferredWidth, 10);
            _headerPanel.Controls.Add(_lblPurchaseTokens);
            rightX -= _lblPurchaseTokens.PreferredWidth + 10;

            _lblUserEmail.Location = new Point(rightX - _lblUserEmail.PreferredWidth, 14);
            _headerPanel.Controls.Add(_lblUserEmail);
        }

        private void BuildSessionBanner()
        {
            _sessionBanner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 35,
                BackColor = AppColors.SessionBannerBg,
                Visible = false,
                Padding = new Padding(10, 5, 10, 5)
            };

            _lblSessionStatus = new Label
            {
                Text = "🔓 Gateway Session Active - Key programming is FREE!",
                ForeColor = AppColors.SessionBannerText,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 7)
            };

            _lblSessionTimer = new Label
            {
                Text = "10:00",
                ForeColor = AppColors.SessionBannerText,
                Font = new Font("Consolas", 12, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Location = new Point(580, 5)
            };

            _sessionBanner.Controls.AddRange(new Control[] { _lblSessionStatus, _lblSessionTimer });
            this.Controls.Add(_sessionBanner);
            _sessionBanner.BringToFront();

            _gatewayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _gatewayTimer.Tick += GatewayTimer_Tick;
        }

        private void BuildTabControl()
        {
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Padding = new Point(15, 5)
            };

            _tabPats = new TabPage
            {
                Text = "🔑 PATS Operations",
                BackColor = AppColors.PanelBackground,
                Padding = new Padding(10)
            };
            BuildPatsTab();

            _tabUtility = new TabPage
            {
                Text = "🔧 Utility (1 token)",
                BackColor = AppColors.PanelBackground,
                Padding = new Padding(10)
            };
            BuildUtilityTab();

            _tabFree = new TabPage
            {
                Text = "⚙️ Free Functions",
                BackColor = AppColors.PanelBackground,
                Padding = new Padding(10)
            };
            BuildFreeTab();

            _tabControl.TabPages.AddRange(new[] { _tabPats, _tabUtility, _tabFree });
            this.Controls.Add(_tabControl);
        }

        private void BuildPatsTab()
        {
            var container = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            int y = 10;

            // Device Selection Panel
            var devicePanel = CreateGroupPanel("🔌 J2534 Device", 120);
            devicePanel.Location = new Point(10, y);

            _cmbDevice = new ComboBox
            {
                Location = new Point(15, 30),
                Size = new Size(350, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbDevice.Items.Add("Select J2534 Device...");

            _btnScan = CreateButton("🔍 Scan", AppColors.ButtonPrimary, new Size(80, 30));
            _btnScan.Location = new Point(375, 28);

            _btnConnect = CreateButton("Connect", AppColors.ButtonSuccess, new Size(90, 30));
            _btnConnect.Location = new Point(15, 70);
            _btnConnect.Enabled = false;

            _btnDisconnect = CreateButton("Disconnect", AppColors.ButtonDanger, new Size(90, 30));
            _btnDisconnect.Location = new Point(115, 70);
            _btnDisconnect.Enabled = false;

            _btnReadVehicle = CreateButton("📖 Read Vehicle", AppColors.ButtonPrimary, new Size(130, 30));
            _btnReadVehicle.Location = new Point(215, 70);
            _btnReadVehicle.Enabled = false;

            devicePanel.Controls.AddRange(new Control[] { _cmbDevice, _btnScan, _btnConnect, _btnDisconnect, _btnReadVehicle });
            container.Controls.Add(devicePanel);
            y += 135;

            // Vehicle Info Panel
            _vehicleInfoPanel = CreateGroupPanel("🚗 Vehicle Information", 100);
            _vehicleInfoPanel.Location = new Point(10, y);
            _vehicleInfoPanel.Visible = false;

            _lblVin = new Label { Location = new Point(15, 30), AutoSize = true, Font = new Font("Consolas", 11, FontStyle.Bold) };
            _lblVehicleDesc = new Label { Location = new Point(15, 55), AutoSize = true };
            _lblOutcode = new Label { Location = new Point(15, 75), AutoSize = true, Font = new Font("Consolas", 11, FontStyle.Bold), ForeColor = AppColors.ButtonPrimary };

            _vehicleInfoPanel.Controls.AddRange(new Control[] { _lblVin, _lblVehicleDesc, _lblOutcode });
            container.Controls.Add(_vehicleInfoPanel);
            y += 115;

            // Incode Panel
            var incodePanel = CreateGroupPanel("🔐 Security Access", 80);
            incodePanel.Location = new Point(10, y);
            incodePanel.Name = "incodePanel";
            incodePanel.Visible = false;

            var lblIncodePrompt = new Label { Text = "Enter Incode:", Location = new Point(15, 32), AutoSize = true };
            _txtIncode = new TextBox { Location = new Point(110, 28), Size = new Size(150, 25), Font = new Font("Consolas", 11), CharacterCasing = CharacterCasing.Upper };
            _btnSubmitIncode = CreateButton("✓ Submit", AppColors.ButtonSuccess, new Size(90, 28));
            _btnSubmitIncode.Location = new Point(270, 27);
            _btnSubmitIncode.Enabled = false;
            _lblIncodeStatus = new Label { Location = new Point(370, 32), AutoSize = true };

            incodePanel.Controls.AddRange(new Control[] { lblIncodePrompt, _txtIncode, _btnSubmitIncode, _lblIncodeStatus });
            container.Controls.Add(incodePanel);
            y += 95;

            // Key Operations Panel
            _keyOperationsPanel = CreateGroupPanel("🔑 Key Operations (1 token per session)", 90);
            _keyOperationsPanel.Location = new Point(10, y);
            _keyOperationsPanel.Visible = false;

            _btnEraseKeys = CreateButton("🗑️ Erase All Keys", AppColors.ButtonDanger, new Size(150, 35));
            _btnEraseKeys.Location = new Point(15, 35);
            _btnEraseKeys.Enabled = false;

            _btnProgramKeys = CreateButton("🔑 Program Keys", AppColors.ButtonSuccess, new Size(150, 35));
            _btnProgramKeys.Location = new Point(180, 35);
            _btnProgramKeys.Enabled = false;

            var lblKeyInfo = new Label
            {
                Text = "💡 1 token = unlimited keys in session (same outcode)",
                Location = new Point(350, 42),
                AutoSize = true,
                ForeColor = AppColors.Info
            };

            _keyOperationsPanel.Controls.AddRange(new Control[] { _btnEraseKeys, _btnProgramKeys, lblKeyInfo });
            container.Controls.Add(_keyOperationsPanel);
            y += 105;

            // Parameter Reset Panel
            _paramResetPanel = CreateGroupPanel("🔄 Parameter Reset (1 token per module)", 130);
            _paramResetPanel.Location = new Point(10, y);
            _paramResetPanel.Visible = false;

            var lblParamDesc = new Label
            {
                Text = "Auto-detects modules: BCM + ABS + PCM (3-4 tokens total)",
                Location = new Point(15, 28),
                AutoSize = true,
                ForeColor = AppColors.Info
            };

            _chkSkipAbs = new CheckBox
            {
                Text = "Skip ABS (2 modules only) - Saves 1 token!",
                Location = new Point(15, 50),
                AutoSize = true,
                ForeColor = AppColors.Success
            };

            _btnStartParamReset = CreateButton("🔄 Start Parameter Reset", AppColors.ButtonPrimary, new Size(200, 35));
            _btnStartParamReset.Location = new Point(15, 80);
            _btnStartParamReset.Enabled = false;

            _lblParamResetStatus = new Label { Location = new Point(230, 88), AutoSize = true };

            _paramResetProgress = new ProgressBar
            {
                Location = new Point(15, 115),
                Size = new Size(430, 10),
                Visible = false
            };

            _paramResetPanel.Controls.AddRange(new Control[] { lblParamDesc, _chkSkipAbs, _btnStartParamReset, _lblParamResetStatus, _paramResetProgress });
            container.Controls.Add(_paramResetPanel);
            y += 145;

            // Gateway Panel (2020+)
            _gatewayPanel = CreateGroupPanel("🔓 Gateway Unlock (2020+)", 80);
            _gatewayPanel.Location = new Point(10, y);
            _gatewayPanel.BackColor = Color.FromArgb(254, 243, 199);
            _gatewayPanel.Visible = false;

            var lblGatewayDesc = new Label
            {
                Text = "✨ Unlock Gateway = FREE key programming for 10 minutes!",
                Location = new Point(15, 28),
                AutoSize = true,
                ForeColor = Color.FromArgb(146, 64, 14)
            };

            _btnGatewayUnlock = CreateButton("🔓 Gateway Unlock (1 token)", AppColors.ButtonWarning, new Size(220, 35));
            _btnGatewayUnlock.Location = new Point(15, 50);
            _btnGatewayUnlock.Enabled = false;

            _gatewayPanel.Controls.AddRange(new Control[] { lblGatewayDesc, _btnGatewayUnlock });
            container.Controls.Add(_gatewayPanel);

            _tabPats.Controls.Add(container);
        }

        private void BuildUtilityTab()
        {
            var container = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(10)
            };

            var lblHeader = new Label
            {
                Text = "🔧 Utility Operations (1 token each)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            container.Controls.Add(lblHeader);

            // Utility buttons
            var utilities = new[]
            {
                ("Clear Theft Flag", "Theft Detected - Vehicle Immobilized"),
                ("Clear Crash Flag", "Collision/Accident Flag (DID 5B17)"),
                ("Clear Crash Input", "Crash Input Failure"),
                ("BCM Factory Defaults", "Restore BCM config (NOT PATS)")
            };

            foreach (var (title, desc) in utilities)
            {
                var btn = CreateUtilityButton(title, desc);
                btn.Click += async (s, e) => await ExecuteUtilityOperationAsync(title);
                container.Controls.Add(btn);
            }

            _tabUtility.Controls.Add(container);
        }

        private void BuildFreeTab()
        {
            var container = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(10)
            };

            var lblHeader = new Label
            {
                Text = "⚙️ Free Functions (No tokens required)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            container.Controls.Add(lblHeader);

            var freeOps = new[]
            {
                ("Read VIN", "Read VIN from vehicle modules"),
                ("Read Key Count", "Count programmed keys"),
                ("Read DTCs", "Read diagnostic trouble codes"),
                ("Clear DTCs", "Clear all DTCs from modules"),
                ("Read Battery", "Check vehicle battery voltage")
            };

            foreach (var (title, desc) in freeOps)
            {
                var btn = CreateUtilityButton(title, desc, true);
                container.Controls.Add(btn);
            }

            _tabFree.Controls.Add(container);
        }

        private void BuildActivityLog()
        {
            var logPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 150,
                Padding = new Padding(5)
            };

            var lblLog = new Label
            {
                Text = "📋 Activity Log",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(5, 3)
            };

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
            logPanel.Controls.Add(lblLog);
            lblLog.BringToFront();

            this.Controls.Add(logPanel);
        }

        private void BuildStatusBar()
        {
            _statusBar = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel { Text = "Ready", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            var lblVersion = new ToolStripStatusLabel { Text = "v2.0.0", Alignment = ToolStripItemAlignment.Right };
            _statusBar.Items.AddRange(new ToolStripItem[] { _lblStatus, lblVersion });
            this.Controls.Add(_statusBar);
        }

        // ============ UI HELPERS ============
        private Panel CreateGroupPanel(string title, int height)
        {
            var panel = new Panel
            {
                Size = new Size(560, height),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lbl = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(10, 5),
                AutoSize = true
            };
            panel.Controls.Add(lbl);

            return panel;
        }

        private Button CreateButton(string text, Color backColor, Size size)
        {
            return new Button
            {
                Text = text,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = size,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
        }

        private Button CreateUtilityButton(string title, string desc, bool isFree = false)
        {
            var btn = new Button
            {
                Size = new Size(530, 50),
                BackColor = isFree ? AppColors.ButtonPrimary : AppColors.ButtonWarning,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10),
                Margin = new Padding(0, 0, 0, 5),
                Text = $"{title}\n{desc}" + (isFree ? "" : " (1 token)")
            };
            return btn;
        }

        // ============ EVENT WIRING ============
        private void WireEvents()
        {
            _btnScan.Click += BtnScan_Click;
            _btnConnect.Click += BtnConnect_Click;
            _btnDisconnect.Click += BtnDisconnect_Click;
            _btnReadVehicle.Click += BtnReadVehicle_Click;
            _cmbDevice.SelectedIndexChanged += CmbDevice_SelectedIndexChanged;
            _txtIncode.TextChanged += TxtIncode_TextChanged;
            _btnSubmitIncode.Click += BtnSubmitIncode_Click;
            _btnEraseKeys.Click += BtnEraseKeys_Click;
            _btnProgramKeys.Click += BtnProgramKeys_Click;
            _btnStartParamReset.Click += BtnStartParamReset_Click;
            _btnGatewayUnlock.Click += BtnGatewayUnlock_Click;
            _btnAccount.Click += BtnAccount_Click;
        }

        // ============ TOKEN BALANCE HANDLING ============
        private void OnTokenBalanceChanged(object? sender, TokenBalanceChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnTokenBalanceChanged(sender, e)));
                return;
            }

            UpdateTokenDisplay();
        }

        private void UpdateTokenDisplay()
        {
            var service = TokenBalanceService.Instance;
            
            _lblPurchaseTokens.Text = service.RegularTokens.ToString();
            _lblPurchaseTokens.Visible = true;

            if (service.PromoTokens > 0)
            {
                _lblPromoTokens.Text = $"{service.PromoTokens} promo";
                _lblPromoTokens.Visible = true;
            }
            else
            {
                _lblPromoTokens.Visible = false;
            }

            PositionHeaderRightControls();
        }

        // ============ DEVICE EVENTS ============
        private void BtnScan_Click(object? sender, EventArgs e)
        {
            LogInfo("Scanning for J2534 devices...");
            _cmbDevice.Items.Clear();
            _cmbDevice.Items.Add("Scanning...");
            _cmbDevice.SelectedIndex = 0;
            _btnScan.Enabled = false;

            // TODO: Replace with actual J2534 device scanning
            Task.Delay(1000).ContinueWith(t =>
            {
                this.Invoke(() =>
                {
                    _cmbDevice.Items.Clear();
                    _cmbDevice.Items.Add("Select J2534 Device...");
                    _cmbDevice.Items.Add("VCM II (Ford)");
                    _cmbDevice.Items.Add("VXDIAG VCX");
                    _cmbDevice.SelectedIndex = 0;
                    _btnScan.Enabled = true;
                    LogSuccess("Found 2 devices");
                });
            });
        }

        private void CmbDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            _btnConnect.Enabled = _cmbDevice.SelectedIndex > 0;
        }

        private void BtnConnect_Click(object? sender, EventArgs e)
        {
            var deviceName = _cmbDevice.SelectedItem?.ToString() ?? "";
            LogInfo($"Connecting to {deviceName}...");

            // TODO: Replace with actual J2534 connection
            Task.Delay(800).ContinueWith(t =>
            {
                this.Invoke(() =>
                {
                    _deviceConnected = true;
                    _btnConnect.Text = "✓ Connected";
                    _btnConnect.BackColor = AppColors.Success;
                    _btnConnect.Enabled = false;
                    _btnDisconnect.Enabled = true;
                    _cmbDevice.Enabled = false;
                    _btnReadVehicle.Enabled = true;
                    LogSuccess($"Connected to {deviceName}");
                    
                    ProActivityLogger.Instance.LogJ2534Connection(deviceName, true);
                    UpdateStatus("Device ready - Read vehicle to continue");
                });
            });
        }

        private void BtnDisconnect_Click(object? sender, EventArgs e)
        {
            var deviceName = _cmbDevice.SelectedItem?.ToString() ?? "device";
            
            // End any active key session
            TokenBalanceService.Instance.EndKeySession();
            
            _deviceConnected = false;
            _vehicleConnected = false;
            _incodeVerified = false;
            _currentVin = "";
            _currentOutcode = "";

            _btnConnect.Text = "Connect";
            _btnConnect.BackColor = AppColors.ButtonSuccess;
            _btnConnect.Enabled = true;
            _btnDisconnect.Enabled = false;
            _cmbDevice.Enabled = true;
            _btnReadVehicle.Enabled = false;

            _vehicleInfoPanel.Visible = false;
            FindControl<Panel>("incodePanel")!.Visible = false;
            _keyOperationsPanel.Visible = false;
            _paramResetPanel.Visible = false;
            _gatewayPanel.Visible = false;

            LogInfo($"Disconnected from {deviceName}");
            ProActivityLogger.Instance.LogJ2534Disconnect(deviceName);
            UpdateStatus("Disconnected");
        }

        // ============ VEHICLE EVENTS ============
        private async void BtnReadVehicle_Click(object? sender, EventArgs e)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            LogInfo("Reading vehicle VIN from CAN bus...");
            _btnReadVehicle.Enabled = false;

            // TODO: Replace with actual vehicle reading
            await Task.Delay(1500);

            _currentVin = "1FA6P8CF5L5123456";
            _currentVehicleYear = "2020";
            _currentVehicleModel = "Ford Mustang";
            _currentOutcode = "BCM-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            _is2020Plus = int.Parse(_currentVehicleYear) >= 2020;

            LogInfo($"VIN: {_currentVin}");
            LogInfo($"Vehicle: {_currentVehicleYear} {_currentVehicleModel}");
            LogSuccess($"Outcode: {_currentOutcode}");

            _vehicleConnected = true;
            ShowVehicleInfo();

            ProActivityLogger.Instance.LogVehicleDetection(_currentVin, _currentVehicleYear, _currentVehicleModel, true, (int)stopwatch.ElapsedMilliseconds);
            UpdateStatus("Vehicle detected - Enter incode to continue");
        }

        private void ShowVehicleInfo()
        {
            _lblVin.Text = $"VIN: {_currentVin}";
            _lblVehicleDesc.Text = $"{_currentVehicleYear} {_currentVehicleModel}";
            _lblOutcode.Text = $"Outcode: {_currentOutcode}";

            _vehicleInfoPanel.Visible = true;
            FindControl<Panel>("incodePanel")!.Visible = true;

            if (_is2020Plus)
            {
                _gatewayPanel.Visible = true;
                _btnGatewayUnlock.Enabled = true;
            }
        }

        // ============ INCODE HANDLING ============
        private void TxtIncode_TextChanged(object? sender, EventArgs e)
        {
            _btnSubmitIncode.Enabled = _txtIncode.Text.Length >= 4;
        }

        private async void BtnSubmitIncode_Click(object? sender, EventArgs e)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _currentIncode = _txtIncode.Text.ToUpper();
            LogInfo($"Validating incode: {_currentIncode}...");
            _btnSubmitIncode.Enabled = false;
            _lblIncodeStatus.Text = "Validating...";
            _lblIncodeStatus.ForeColor = AppColors.Info;

            // TODO: Replace with actual incode validation
            await Task.Delay(1000);

            _incodeVerified = true;
            _lblIncodeStatus.Text = "✓ Verified";
            _lblIncodeStatus.ForeColor = AppColors.Success;
            LogSuccess("Incode verified - Security access granted");

            // Show key operations
            _keyOperationsPanel.Visible = true;
            _paramResetPanel.Visible = true;
            EnableKeyOperations();

            UpdateStatus("Ready for operations");
        }

        private void EnableKeyOperations()
        {
            bool canOperate = _incodeVerified || _gatewaySessionActive;
            _btnEraseKeys.Enabled = canOperate;
            _btnProgramKeys.Enabled = canOperate;
            _btnStartParamReset.Enabled = canOperate;
        }

        // ============ KEY PROGRAMMING (1 token per session) ============
        private async void BtnEraseKeys_Click(object? sender, EventArgs e)
        {
            await ExecuteKeyOperationAsync("erase");
        }

        private async void BtnProgramKeys_Click(object? sender, EventArgs e)
        {
            await ExecuteKeyOperationAsync("program");
        }

        private async Task ExecuteKeyOperationAsync(string operation)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Check/start key session (1 token for entire session)
            var sessionResult = await TokenBalanceService.Instance.StartKeySessionAsync(_currentVin, _currentOutcode);
            
            if (!sessionResult.Success)
            {
                LogError($"Cannot start key session: {sessionResult.Error}");
                MessageBox.Show(sessionResult.Error, "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!sessionResult.SessionAlreadyActive)
            {
                LogInfo("Key session started (1 token)");
                ProActivityLogger.Instance.LogKeySessionStart(_currentVin, _currentVehicleYear, _currentVehicleModel, _currentOutcode, true, -1, (int)stopwatch.ElapsedMilliseconds);
            }
            else
            {
                LogInfo("Continuing key session (no charge)");
            }

            // Perform operation (FREE within session)
            if (operation == "erase")
            {
                LogInfo("Erasing all keys...");
                await Task.Delay(2000); // TODO: Actual operation
                LogSuccess("All keys erased successfully");
                ProActivityLogger.Instance.LogEraseAllKeys(_currentVin, _currentVehicleYear, _currentVehicleModel, 3, true, 0, (int)stopwatch.ElapsedMilliseconds);
            }
            else if (operation == "program")
            {
                // Program multiple keys (all FREE within session)
                for (int i = 1; i <= 2; i++)
                {
                    LogInfo($"Programming key #{i}...");
                    await Task.Delay(1500); // TODO: Actual operation
                    LogSuccess($"Key #{i} programmed successfully");
                    ProActivityLogger.Instance.LogKeyProgrammed(_currentVin, _currentVehicleYear, _currentVehicleModel, i, true, (int)stopwatch.ElapsedMilliseconds);
                }
            }

            // Check if BCM gave new outcode (session ends)
            // TODO: Actually read outcode from BCM
            var newOutcode = _currentOutcode; // Simulated - same outcode means session continues

            if (newOutcode != _currentOutcode)
            {
                LogWarning("BCM locked - session ended (new outcode required)");
                TokenBalanceService.Instance.EndKeySession();
                _currentOutcode = newOutcode;
                _lblOutcode.Text = $"Outcode: {_currentOutcode}";
            }

            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        // ============ PARAMETER RESET (1 token per module) ============
        private async void BtnStartParamReset_Click(object? sender, EventArgs e)
        {
            // Auto-detect modules (like EZimmo)
            _paramResetModules = _chkSkipAbs.Checked
                ? new[] { new ParamResetModule("BCM", "0x726"), new ParamResetModule("PCM", "0x7E0") }
                : new[] { new ParamResetModule("BCM", "0x726"), new ParamResetModule("ABS", "0x760"), new ParamResetModule("PCM", "0x7E0") };

            int totalModules = _paramResetModules.Length;

            // Check if enough tokens
            if (!TokenBalanceService.Instance.HasEnoughTokens(totalModules))
            {
                LogError($"Need {totalModules} tokens for parameter reset");
                MessageBox.Show($"Parameter reset requires {totalModules} tokens.\nYou have {TokenBalanceService.Instance.TotalTokens} tokens.", 
                    "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _paramResetActive = true;
            _paramResetCurrentStep = 0;
            _btnStartParamReset.Enabled = false;
            _paramResetProgress.Visible = true;
            _paramResetProgress.Maximum = totalModules;
            _paramResetProgress.Value = 0;

            LogInfo($"Starting Parameter Reset - {totalModules} modules to reset");

            var totalTokensUsed = 0;
            var modulesReset = new List<string>();
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

            foreach (var module in _paramResetModules)
            {
                var moduleStopwatch = System.Diagnostics.Stopwatch.StartNew();
                _lblParamResetStatus.Text = $"Processing {module.Name}...";
                LogInfo($"Reading {module.Name} outcode...");

                // Deduct 1 token for this module
                var deductResult = await TokenBalanceService.Instance.DeductForParamResetAsync(module.Name, _currentVin);
                if (!deductResult.Success)
                {
                    LogError($"Failed to deduct for {module.Name}: {deductResult.Error}");
                    break;
                }
                totalTokensUsed++;

                // Simulate reading outcode from module
                await Task.Delay(800);
                module.Outcode = $"{module.Name}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
                LogSuccess($"{module.Name} Outcode: {module.Outcode}");

                // Calculate incode (would normally call API)
                await Task.Delay(500);
                module.Incode = $"IN{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
                LogInfo($"{module.Name} Incode: {module.Incode}");

                // Submit incode to module
                LogInfo($"Submitting incode to {module.Name}...");
                await Task.Delay(1000);
                module.Status = "complete";
                LogSuccess($"{module.Name} reset complete!");

                ProActivityLogger.Instance.LogParameterResetModule(
                    _currentVin, _currentVehicleYear, _currentVehicleModel, module.Name,
                    module.Outcode, module.Incode, true, -1, (int)moduleStopwatch.ElapsedMilliseconds);

                modulesReset.Add(module.Name);
                _paramResetProgress.Value++;
                _paramResetCurrentStep++;
            }

            // Complete
            _paramResetActive = false;
            _btnStartParamReset.Enabled = true;
            _paramResetProgress.Visible = false;
            _lblParamResetStatus.Text = "Complete!";
            _lblParamResetStatus.ForeColor = AppColors.Success;

            LogSuccess($"✅ Parameter Reset COMPLETE - {modulesReset.Count} modules, {totalTokensUsed} tokens");

            ProActivityLogger.Instance.LogParameterResetComplete(
                _currentVin, _currentVehicleYear, _currentVehicleModel,
                modulesReset.Count, totalTokensUsed, (int)totalStopwatch.ElapsedMilliseconds, modulesReset.ToArray());

            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        // ============ GATEWAY UNLOCK (1 token) ============
        private async void BtnGatewayUnlock_Click(object? sender, EventArgs e)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Deduct 1 token
            var result = await TokenBalanceService.Instance.DeductTokensAsync(1, "gateway_unlock", _currentVin);
            if (!result.Success)
            {
                LogError($"Gateway unlock failed: {result.Error}");
                MessageBox.Show(result.Error, "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LogInfo("Unlocking Security Gateway Module...");
            _btnGatewayUnlock.Enabled = false;

            // TODO: Actual gateway unlock
            await Task.Delay(2000);

            _gatewaySessionActive = true;
            _gatewaySessionSecondsRemaining = 600; // 10 minutes

            LogSuccess("Security Gateway Module unlocked!");
            LogSuccess("🎉 Key programming is FREE for 10 minutes!");

            // Show session banner
            _sessionBanner.Visible = true;
            _lblSessionTimer.Text = FormatTime(_gatewaySessionSecondsRemaining);
            _gatewayTimer.Start();

            // Enable key operations (now FREE)
            EnableKeyOperations();
            UpdateKeyOperationLabels(true);

            ProActivityLogger.Instance.LogUtilityOperation("Gateway Unlock", _currentVin, _currentVehicleYear, _currentVehicleModel, true, -1, (int)stopwatch.ElapsedMilliseconds);
            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        private void GatewayTimer_Tick(object? sender, EventArgs e)
        {
            _gatewaySessionSecondsRemaining--;
            _lblSessionTimer.Text = FormatTime(_gatewaySessionSecondsRemaining);

            if (_gatewaySessionSecondsRemaining <= 0)
            {
                _gatewayTimer.Stop();
                _gatewaySessionActive = false;
                _sessionBanner.Visible = false;
                _btnGatewayUnlock.Enabled = true;

                LogWarning("Gateway session expired - key operations now cost tokens");
                UpdateKeyOperationLabels(false);
            }
        }

        private void UpdateKeyOperationLabels(bool isFree)
        {
            if (isFree)
            {
                _btnEraseKeys.Text = "🗑️ Erase All Keys (FREE)";
                _btnProgramKeys.Text = "🔑 Program Keys (FREE)";
            }
            else
            {
                _btnEraseKeys.Text = "🗑️ Erase All Keys";
                _btnProgramKeys.Text = "🔑 Program Keys";
            }
        }

        // ============ UTILITY OPERATIONS (1 token each) ============
        private async Task ExecuteUtilityOperationAsync(string operation)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Deduct 1 token
            var result = await TokenBalanceService.Instance.DeductForUtilityAsync(operation, _currentVin);
            if (!result.Success)
            {
                LogError($"{operation} failed: {result.Error}");
                MessageBox.Show(result.Error, "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LogInfo($"Executing {operation}...");

            // TODO: Actual operation
            await Task.Delay(1500);

            LogSuccess($"{operation} complete!");

            ProActivityLogger.Instance.LogUtilityOperation(operation, _currentVin, _currentVehicleYear, _currentVehicleModel, true, -1, (int)stopwatch.ElapsedMilliseconds);
            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        // ============ ACCOUNT MENU ============
        private void BtnAccount_Click(object? sender, EventArgs e)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Buy Tokens", null, (s, ev) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://patskiller.com/buy", UseShellExecute = true }));
            menu.Items.Add("-");
            menu.Items.Add("Logout", null, (s, ev) => Logout());
            menu.Show(_btnAccount, new Point(0, _btnAccount.Height));
        }

        private void Logout()
        {
            ProActivityLogger.Instance.LogLogout(TokenBalanceService.Instance.UserEmail ?? "");
            TokenBalanceService.Instance.ClearAuthContext();
            ProActivityLogger.Instance.ClearAuthContext();
            LogInfo("Logged out");
            // TODO: Show login form
        }

        // ============ FORM CLOSING ============
        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            TokenBalanceService.Instance.BalanceChanged -= OnTokenBalanceChanged;
            TokenBalanceService.Instance.EndKeySession();
            ProActivityLogger.Instance.LogAppClose();
        }

        // ============ HELPERS ============
        private string FormatTime(int seconds)
        {
            var mins = seconds / 60;
            var secs = seconds % 60;
            return $"{mins}:{secs:D2}";
        }

        private T? FindControl<T>(string name) where T : Control
        {
            var controls = this.Controls.Find(name, true);
            return controls.Length > 0 ? controls[0] as T : null;
        }

        private void UpdateStatus(string text)
        {
            _lblStatus.Text = text;
        }

        // ============ LOGGING ============
        private void LogInfo(string message) => AppendLog(message, AppColors.LogInfo);
        private void LogSuccess(string message) => AppendLog(message, AppColors.LogSuccess);
        private void LogWarning(string message) => AppendLog(message, AppColors.LogWarning);
        private void LogError(string message) => AppendLog(message, AppColors.LogError);

        private void AppendLog(string message, Color color)
        {
            if (_rtbLog.InvokeRequired)
            {
                _rtbLog.Invoke(() => AppendLog(message, color));
                return;
            }

            var time = DateTime.Now.ToString("HH:mm:ss");
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{time}] {message}\n");
            _rtbLog.ScrollToCaret();
        }
    }

    // ============ HELPER CLASSES ============
    internal class ParamResetModule
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string Status { get; set; } = "pending";
        public string Outcode { get; set; } = "";
        public string Incode { get; set; } = "";

        public ParamResetModule(string name, string address)
        {
            Name = name;
            Address = address;
        }
    }
}
