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
            this.ClientSize = new Size(1150, 820);
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
            // Header - 80px
            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = ColorSurface };
            _headerPanel.Paint += (s, e) => { using var p = new Pen(ColorBorder); e.Graphics.DrawLine(p, 0, 79, Width, 79); };
            
            var logo = new Label { Text = "PK", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.White, BackColor = ColorAccent, Size = new Size(40, 40), Location = new Point(20, 20), TextAlign = ContentAlignment.MiddleCenter };
            _headerPanel.Controls.Add(logo);
            
            var title = new Label { Text = "PatsKiller Pro", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = ColorText, AutoSize = true, Location = new Point(70, 18) };
            _headerPanel.Controls.Add(title);
            
            var subtitle = new Label { Text = "Ford & Lincoln PATS Key Programming", Font = new Font("Segoe UI", 9), ForeColor = ColorTextMuted, AutoSize = true, Location = new Point(72, 48) };
            _headerPanel.Controls.Add(subtitle);
            
            _lblTokens = new Label { Text = "Tokens: --", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = ColorSuccess, AutoSize = true };
            _headerPanel.Controls.Add(_lblTokens);
            
            _lblUser = new Label { Font = new Font("Segoe UI", 9), ForeColor = ColorTextDim, AutoSize = true };
            _headerPanel.Controls.Add(_lblUser);
            
            _btnLogout = MakeButton("Logout", 80, 32);
            _btnLogout.Click += (s, e) => DoLogout();
            _btnLogout.Visible = false;
            _headerPanel.Controls.Add(_btnLogout);
            
            _headerPanel.Resize += (s, e) => LayoutHeader();
            Controls.Add(_headerPanel);

            // Tab Bar - 50px
            _tabBar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = ColorSurface, Visible = false };
            _tabBar.Paint += (s, e) => { using var p = new Pen(ColorBorder); e.Graphics.DrawLine(p, 0, 49, Width, 49); };
            
            _tabPats = MakeTabButton("PATS Key Programming", true);
            _tabPats.Location = new Point(20, 8);
            _tabPats.Click += (s, e) => SwitchTab(0);
            _tabBar.Controls.Add(_tabPats);
            
            _tabDiag = MakeTabButton("Diagnostics", false);
            _tabDiag.Location = new Point(240, 8);
            _tabDiag.Click += (s, e) => SwitchTab(1);
            _tabBar.Controls.Add(_tabDiag);
            
            _tabFree = MakeTabButton("Free Functions", false);
            _tabFree.Location = new Point(400, 8);
            _tabFree.Click += (s, e) => SwitchTab(2);
            _tabBar.Controls.Add(_tabFree);
            
            Controls.Add(_tabBar);

            // Log Panel - 110px
            _logPanel = new Panel { Dock = DockStyle.Bottom, Height = 110, BackColor = ColorSurface, Visible = false };
            _logPanel.Paint += (s, e) => { using var p = new Pen(ColorBorder); e.Graphics.DrawLine(p, 0, 0, Width, 0); };
            
            var logTitle = new Label { Text = "ACTIVITY LOG", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = ColorTextDim, Location = new Point(20, 8), AutoSize = true };
            _logPanel.Controls.Add(logTitle);
            
            _txtLog = new RichTextBox { Location = new Point(20, 28), BackColor = ColorBg, ForeColor = ColorText, Font = new Font("Consolas", 9.5F), BorderStyle = BorderStyle.None, ReadOnly = true };
            _logPanel.Controls.Add(_txtLog);
            _logPanel.Resize += (s, e) => { _txtLog.Width = _logPanel.Width - 40; _txtLog.Height = _logPanel.Height - 38; };
            Controls.Add(_logPanel);

            // Content Panel
            _contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = ColorBg, Visible = false, AutoScroll = true };
            Controls.Add(_contentPanel);

            // Build tab contents
            BuildPatsPanel();
            BuildDiagPanel();
            BuildFreePanel();
            BuildLoginPanel();

            ShowLogin();
        }

        private void LayoutHeader()
        {
            int right = _headerPanel.Width - 20;
            _btnLogout.Location = new Point(right - 80, 24);
            _lblTokens.Location = new Point(right - 80 - 20 - _lblTokens.Width, 18);
            _lblUser.Location = new Point(right - 80 - 20 - _lblUser.Width, 48);
        }

        private void BuildPatsPanel()
        {
            _patsPanel = new Panel { Location = new Point(20, 15), Size = new Size(1090, 560), BackColor = ColorBg };
            
            int y = 0;
            int W = 1050; // Section width

            // === SECTION 1: J2534 Device Connection ===
            var sec1 = MakeSection("J2534 DEVICE CONNECTION", W, 95);
            sec1.Location = new Point(0, y);
            
            _cmbDevices = MakeComboBox(300);
            _cmbDevices.Location = new Point(25, 48);
            _cmbDevices.Items.Add("Select J2534 Device...");
            _cmbDevices.SelectedIndex = 0;
            sec1.Controls.Add(_cmbDevices);
            
            var btnScan = MakeButton("Scan Devices", 130, 36);
            btnScan.Location = new Point(340, 47);
            btnScan.Click += BtnScan_Click;
            sec1.Controls.Add(btnScan);
            
            var btnConnect = MakeButton("Connect", 100, 36);
            btnConnect.Location = new Point(485, 47);
            btnConnect.BackColor = ColorSuccess;
            btnConnect.Click += BtnConnect_Click;
            sec1.Controls.Add(btnConnect);
            
            _lblStatus = new Label { Text = "● Not Connected", Font = new Font("Segoe UI", 10), ForeColor = ColorWarning, AutoSize = true, Location = new Point(610, 53) };
            sec1.Controls.Add(_lblStatus);
            
            _patsPanel.Controls.Add(sec1);
            y += 110;

            // === SECTION 2: Vehicle Information ===
            var sec2 = MakeSection("VEHICLE INFORMATION", W, 95);
            sec2.Location = new Point(0, y);
            
            var btnReadVin = MakeButton("Read VIN", 100, 36);
            btnReadVin.Location = new Point(25, 48);
            btnReadVin.BackColor = ColorAccent;
            btnReadVin.Click += BtnReadVin_Click;
            sec2.Controls.Add(btnReadVin);
            
            _lblVin = new Label { Text = "VIN: -----------------", Font = new Font("Consolas", 11), ForeColor = ColorTextDim, AutoSize = true, Location = new Point(140, 54) };
            sec2.Controls.Add(_lblVin);
            
            var lblSelect = new Label { Text = "Or select vehicle:", Font = new Font("Segoe UI", 9), ForeColor = ColorTextDim, AutoSize = true, Location = new Point(400, 54) };
            sec2.Controls.Add(lblSelect);
            
            // INCREASED WIDTH to 350 so "Bronco" text fits
            _cmbVehicles = MakeComboBox(350);
            _cmbVehicles.Location = new Point(520, 48);
            foreach (var v in VehiclePlatforms.GetAllVehicles()) _cmbVehicles.Items.Add(v.DisplayName);
            if (_cmbVehicles.Items.Count > 0) _cmbVehicles.SelectedIndex = 0;
            sec2.Controls.Add(_cmbVehicles);
            
            // Keys badge
            var keysBg = new Panel { Size = new Size(90, 55), Location = new Point