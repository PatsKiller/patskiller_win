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
            this.MinimumSize = new Size(1050, 750);
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
            // Header - 90px (Increased slightly for better spacing)
            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = ColorSurface };
            _headerPanel.Paint += (s, e) => { using var p = new Pen(ColorBorder); e.Graphics.DrawLine(p, 0, 89, Width, 89); };
            
            var logo = new Label { Text = "PK", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.White, BackColor = ColorAccent, Size = new Size(44, 44), Location = new Point(20, 23), TextAlign = ContentAlignment.MiddleCenter };
            _headerPanel.Controls.Add(logo);
            
            var title = new Label { Text = "PatsKiller Pro", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = ColorText, AutoSize = true, Location = new Point(75, 20) };
            _headerPanel.Controls.Add(title);
            
            var subtitle = new Label { Text = "Ford & Lincoln PATS Key Programming", Font = new Font("Segoe UI", 9), ForeColor = ColorTextMuted, AutoSize = true, Location = new Point(77, 52) };
            _headerPanel.Controls.Add(subtitle);
            
            _lblTokens = new Label { Text = "Tokens: --", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = ColorSuccess, AutoSize = true };
            _headerPanel.Controls.Add(_lblTokens);
            
            _lblUser = new Label { Font = new Font("Segoe UI", 9), ForeColor = ColorTextDim, AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
            _headerPanel.Controls.Add(_lblUser);
            
            _btnLogout = new Button { Text = "Logout", AutoSize = true, Padding = new Padding(12, 6, 12, 6), FlatStyle = FlatStyle.Flat, BackColor = ColorButtonBg, ForeColor = ColorText, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand };
            _btnLogout.FlatAppearance.BorderColor = ColorBorder;
            _btnLogout.Click += (s, e) => DoLogout();
            _btnLogout.Visible = false;
            _headerPanel.Controls.Add(_btnLogout);
            
            _headerPanel.Resize += (s, e) => LayoutHeader();
            Controls.Add(_headerPanel);

            // Tab Bar - 55px
            _tabBar = new Panel { Dock = DockStyle.Top, Height = 55, BackColor = ColorSurface, Visible = false };
            _tabBar.Paint += (s, e) => { using var p = new Pen(ColorBorder); e.Graphics.DrawLine(p, 0, 54, Width, 54); };
            
            var tabFlow = new FlowLayoutPanel