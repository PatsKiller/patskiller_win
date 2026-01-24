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
            // === HEADER ===
            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = ColorSurface };
            _headerPanel.Paint += (s, e) => { using (var p = new Pen(ColorBorder)) { e.Graphics.DrawLine(p, 0, 79, Width, 79); } };

            // Left Header (Title)
            var headerLeft = new Panel { Dock = DockStyle.Left, Width = 500, BackColor = Color.Transparent };
            var logo = new Label { Text = "PK", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.White, BackColor = ColorAccent, Size = new Size(40, 40), Location = new Point(20, 20), TextAlign = ContentAlignment.MiddleCenter };
            var title = new Label { Text = "PatsKiller Pro", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = ColorText, AutoSize = true, Location = new Point(70, 18) };
            var subtitle = new Label { Text = "Ford & Lincoln PATS Key Programming", Font = new Font("Segoe UI", 9), ForeColor = ColorTextMuted, AutoSize = true, Location = new Point(72, 48) };
            headerLeft.Controls.Add(logo);
            headerLeft.Controls.Add(title);
            headerLeft.Controls.Add(subtitle);
            _headerPanel.Controls.Add(headerLeft);

            // Right Header (User Info)
            var headerRight = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Right, 
                Width = 400, 
                FlowDirection = FlowDirection.TopDown, 
                WrapContents = false,
                Padding = new Padding(0, 15, 20, 0)
            };
            
            _lblTokens = new Label { Text = "Tokens: --", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = ColorSuccess, AutoSize = true, Anchor = AnchorStyles.Right };
            _lblUser = new Label { Text = "Not logged in", Font = new Font("Segoe UI", 9), ForeColor = ColorTextDim, AutoSize = true, Anchor = AnchorStyles.Right };
            _btnLogout = MakeButton("Logout", 80, 28);
            _btnLogout.Margin = new Padding(0, 5, 0, 0);
            _btnLogout.Anchor = AnchorStyles.Right;
            _btnLogout.Click += (s, e) => DoLogout();
            _btnLogout.Visible = false;

            headerRight.Controls.Add(_lblTokens);
            headerRight.Controls.Add(_lblUser);
            headerRight.Controls.Add(_btnLogout);
            _headerPanel.Controls.Add(headerRight);
            
            Controls.Add(_headerPanel);

            // === TAB BAR ===
            _tabBar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = ColorSurface, Visible = false };
            _tabBar.Paint += (s, e) => { using (var p = new Pen(ColorBorder)) { e.Graphics.DrawLine(p, 0, 49, Width, 49); } };
            
            var tabFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 8, 0, 0) };
            _tabPats = MakeTabButton("PATS Key Programming", true);
            _tabPats.Click += (s, e) => SwitchTab(0);
            _tabDiag = MakeTabButton("Diagnostics", false);
            _tabDiag.Click += (s, e) => SwitchTab(1);
            _tabFree = MakeTabButton("Free Functions", false);
            _tabFree.Click += (s, e) => SwitchTab(2);
            
            tabFlow.Controls.Add(_tabPats);
            tabFlow.Controls.Add(_tabDiag);
            tabFlow.Controls.Add(_tabFree);
            _tabBar.Controls.Add(tabFlow);
            
            Controls.Add(_tabBar);

            // === LOG PANEL ===
            _logPanel = new Panel { Dock = DockStyle.Bottom, Height = 110, BackColor = ColorSurface, Visible = false };
            _logPanel.Paint += (s, e) => { using (var p = new Pen(ColorBorder)) { e.Graphics.DrawLine(p, 0, 0, Width, 0); } };
            var logTitle = new Label { Text = "ACTIVITY LOG", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = ColorTextDim, Location = new Point(20, 8), AutoSize = true };
            _txtLog = new RichTextBox { Location = new Point(20, 28), BackColor = ColorBg, ForeColor = ColorText, Font = new Font("Consolas", 9.5F), BorderStyle = BorderStyle.None, ReadOnly = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, Height = 70, Width = 1100 };
            _logPanel.Controls.Add(logTitle);
            _logPanel.Controls.Add(_txtLog);
            Controls.Add(_logPanel);

            // === CONTENT PANEL ===
            _contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = ColorBg, Visible = false, AutoScroll = true };
            Controls.Add(_contentPanel);

            BuildPatsPanel();
            BuildDiagPanel();
            BuildFreePanel();
            BuildLoginPanel();

            ShowLogin();
        }

        private void BuildPatsPanel()
        {
            _patsPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20) };
            
            int W = 1050; // Max width guide

            // 1. Device Connection
            var sec1 = MakeSection("J2534 DEVICE CONNECTION");
            var flow1 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 45, 0, 20), AutoSize = true };
            
            _cmbDevices = MakeComboBox(300);
            _cmbDevices.Items.Add("Select J2534 Device...");
            _cmbDevices.SelectedIndex = 0;
            flow1.Controls.Add(_cmbDevices);
            
            var btnScan = MakeButton("Scan Devices", 130, 36);
            btnScan.Click += BtnScan_Click;
            btnScan.Margin = new Padding(10, 0, 0, 0);
            flow1.Controls.Add(btnScan);
            
            var btnConnect = MakeButton("Connect", 100, 36);
            btnConnect.BackColor = ColorSuccess;
            btnConnect.Click += BtnConnect_Click;
            btnConnect.Margin = new Padding(10, 0, 0, 0);
            flow1.Controls.Add(btnConnect);
            
            _lblStatus = new Label { Text = "● Not Connected", Font = new Font("Segoe UI", 10), ForeColor = ColorWarning, AutoSize = true, Margin = new Padding(15, 8, 0, 0) };
            flow1.Controls.Add(_lblStatus);
            sec1.Controls.Add(flow1);
            _patsPanel.Controls.Add(sec1);

            // 2. Vehicle Info
            var sec2 = MakeSection("VEHICLE INFORMATION");
            sec2.Margin = new Padding(0, 20, 0, 0); // Space above
            var flow2 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 45, 0, 20), AutoSize = true };

            var btnReadVin = MakeButton("Read VIN", 100, 36);
            btnReadVin.BackColor = ColorAccent;
            btnReadVin.Click += BtnReadVin_Click;
            flow2.Controls.Add(btnReadVin);
            
            _lblVin = new Label { Text = "VIN: -----------------", Font = new Font("Consolas", 11), ForeColor = ColorTextDim, AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
            flow2.Controls.Add(_lblVin);
            
            // Break to new line for dropdown to prevent cutoff
            flow2.SetFlowBreak(_lblVin, true);
            
            var lblSelect = new Label { Text = "Or select vehicle:", Font = new Font("Segoe UI", 9), ForeColor = ColorTextDim, AutoSize = true, Margin = new Padding(0, 15, 0, 0) };
            flow2.Controls.Add(lblSelect);
            
            _cmbVehicles = MakeComboBox(400);
            _cmbVehicles.Margin = new Padding(10, 10, 0, 0);
            foreach (var v in VehiclePlatforms.GetAllVehicles()) _cmbVehicles.Items.Add(v.DisplayName);
            if (_cmbVehicles.Items.Count > 0) _cmbVehicles.SelectedIndex = 0;
            flow2.Controls.Add(_cmbVehicles);

            sec2.Controls.Add(flow2);
            _patsPanel.Controls.Add(sec2);

            // 3. Security Codes (TableLayout to fix overlaps)
            var sec3 = MakeSection("PATS SECURITY CODES");
            sec3.Margin = new Padding(0, 20, 0, 0);
            
            var table3 = new TableLayoutPanel 
            { 
                Dock = DockStyle.Fill, 
                ColumnCount = 6, 
                RowCount = 1, 
                Padding = new Padding(20, 45, 20, 20),
                AutoSize = true
            };
            // Define columns: Auto, Fixed, Auto, Auto, Auto, Fixed
            table3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Label
            table3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); // Box
            table3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); // Copy
            table3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); // Get
            table3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Label
            table3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); // Box

            var lblOut = new Label { Text = "OUTCODE:", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = ColorText, AutoSize = true, Anchor = AnchorStyles.Left };
            _txtOutcode = MakeTextBox(130, 36); _txtOutcode.ReadOnly = true; _txtOutcode.TextAlign = HorizontalAlignment.Center;
            
            var btnCopy = MakeButton("Copy", 90, 36);
            btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) { Clipboard.SetText(_txtOutcode.Text); Log("info", "Copied to clipboard"); } };
            
            var btnGet = MakeButton("Get Incode", 160, 36);
            btnGet.BackColor = ColorWarning; btnGet.ForeColor = Color.Black;
            btnGet.Click += (s, e) => OpenUrl("https://patskiller.com/calculator");
            
            var lblIn = new Label { Text = "INCODE:", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = ColorText, AutoSize = true, Anchor = AnchorStyles.Right };
            _txtIncode = MakeTextBox(130, 36); _txtIncode.TextAlign = HorizontalAlignment.Center;

            table3.Controls.Add(lblOut, 0, 0);
            table3.Controls.Add(_txtOutcode, 1, 0);
            table3.Controls.Add(btnCopy, 2, 0);
            table3.Controls.Add(btnGet, 3, 0);
            table3.Controls.Add(lblIn, 4, 0);
            table3.Controls.Add(_txtIncode, 5, 0);
            
            sec3.Controls.Add(table3);
            _patsPanel.Controls.Add(sec3);

            // 4. Operations
            var sec4 = MakeSection("KEY PROGRAMMING OPERATIONS");
            sec4.Margin = new Padding(0, 20, 0, 0);
            var flow4 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 45, 0, 20), AutoSize = true, WrapContents = true };
            
            // Buttons with explicitly enforced heights to prevent squashing
            var btnProg = MakeAutoSizedButton("Program Key", ColorSuccess);
            btnProg.Click += BtnProgramKeys_Click;
            flow4.Controls.Add(btnProg);
            
            var btnErase = MakeAutoSizedButton("Erase All Keys", ColorDanger);
            btnErase.Click += BtnEraseKeys_Click;
            flow4.Controls.Add(btnErase);
            
            var btnParam = MakeAutoSizedButton("Parameter Reset", ColorButtonBg);
            btnParam.Click += BtnParamReset_Click;
            flow4.Controls.Add(btnParam);
            
            var btnEscl = MakeAutoSizedButton("Initialize ESCL", ColorButtonBg);
            btnEscl.Click += BtnEscl_Click;
            flow4.Controls.Add(btnEscl);
            
            var btnDisable = MakeAutoSizedButton("Disable BCM", ColorButtonBg);
            btnDisable.Click += BtnDisableBcm_Click;
            flow4.Controls.Add(btnDisable);
            
            var tip = new Label { Text = "TIP: 1 token per session. Insert key → Program → Remove → Repeat.", Font = new Font("Segoe UI", 9), ForeColor = ColorTextMuted, AutoSize = true, Margin = new Padding(5, 15, 0, 0) };
            flow4.SetFlowBreak(btnDisable, true);
            flow4.Controls.Add(tip);

            sec4.Controls.Add(flow4);
            _patsPanel.Controls.Add(sec4);

            _contentPanel.Controls.Add(_patsPanel);
        }

        private void BuildDiagPanel()
        {
            _diagPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20), Visible = false };
            
            var sec1 = MakeSection("DTC CLEAR OPERATIONS");
            var f1 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 45, 0, 20), AutoSize = true };
            
            var btnP160A = MakeAutoSizedButton("Clear P160A", ColorAccent); btnP160A.Click += BtnClearP160A_Click; f1.Controls.Add(btnP160A);
            var btnB10A2 = MakeAutoSizedButton("Clear B10A2", ColorAccent); btnB10A2.Click += BtnClearB10A2_Click; f1.Controls.Add(btnB10A2);
            var btnCrush = MakeAutoSizedButton("Clear Crush", ColorAccent); btnCrush.Click += BtnClearCrush_Click; f1.Controls.Add(btnCrush);
            var btnGate = MakeAutoSizedButton("Unlock Gateway", ColorAccent); btnGate.Click += BtnGatewayUnlock_Click; f1.Controls.Add(btnGate);
            sec1.Controls.Add(f1);
            _diagPanel.Controls.Add(sec1);

            _contentPanel.Controls.Add(_diagPanel);
        }

        private void BuildFreePanel()
        {
            _freePanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20), Visible = false };
            
            var banner = new Panel { Height = 45, Dock = DockStyle.Top, BackColor = Color.FromArgb(20, 34, 197, 94), Margin = new Padding(0,0,0,20) };
            var bl = new Label { Text = "✓ All operations here are FREE", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = ColorSuccess, Location = new Point(20, 12), AutoSize = true };
            banner.Controls.Add(bl);
            _freePanel.Controls.Add(banner);

            var sec1 = MakeSection("BASIC OPERATIONS");
            sec1.Top = 60; // Offset below banner
            var f1 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 45, 0, 20), AutoSize = true };
            
            var btnDtc = MakeAutoSizedButton("Clear All DTCs", ColorButtonBg); btnDtc.Click += BtnClearDtc_Click; f1.Controls.Add(btnDtc);
            var btnKam = MakeAutoSizedButton("Clear KAM", ColorButtonBg); btnKam.Click += BtnClearKam_Click; f1.Controls.Add(btnKam);
            var btnRes = MakeAutoSizedButton("Vehicle Reset", ColorButtonBg); btnRes.Click += BtnVehicleReset_Click; f1.Controls.Add(btnRes);
            sec1.Controls.Add(f1);
            _freePanel.Controls.Add(sec1);

            _contentPanel.Controls.Add(_freePanel);
        }

        private void BuildLoginPanel()
        {
            _loginPanel = new Panel { Dock = DockStyle.Fill, BackColor = ColorBg };
            var card = new Panel { Size = new Size(400, 460), BackColor = ColorCard };
            card.Paint += (s, e) => { using (var p = new Pen(ColorBorder, 2)) { e.Graphics.DrawRectangle(p, 1, 1, 397, 457); } };
            
            // Simple center logic
            card.Location = new Point((this.ClientSize.Width - 400)/2, (this.ClientSize.Height - 460)/2);
            _loginPanel.Resize += (s,e) => card.Location = new Point((_loginPanel.Width - 400)/2, (_loginPanel.Height - 460)/2);

            var lblTitle = new Label { Text = "Welcome", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = ColorText, AutoSize = true, Location = new Point(140, 40) };
            card.Controls.Add(lblTitle);

            var lblE = new Label { Text = "Email", ForeColor = ColorTextDim, Location = new Point(40, 120), AutoSize = true };
            card.Controls.Add(lblE);
            _txtEmail = MakeTextBox(320, 40); _txtEmail.Location = new Point(40, 145);
            card.Controls.Add(_txtEmail);

            var lblP = new Label { Text = "Password", ForeColor = ColorTextDim, Location = new Point(40, 200), AutoSize = true };
            card.Controls.Add(lblP);
            _txtPassword = MakeTextBox(320, 40); _txtPassword.Location = new Point(40, 225); _txtPassword.UseSystemPasswordChar = true;
            card.Controls.Add(_txtPassword);

            var btnL = MakeButton("Sign In", 320, 45); btnL.Location = new Point(40, 290); btnL.BackColor = ColorAccent;
            btnL.Click += (s, e) => DoLogin();
            card.Controls.Add(btnL);

            var btnG = MakeButton("Google Login", 320, 45); btnG.Location = new Point(40, 350); btnG.BackColor = Color.White; btnG.ForeColor = Color.Black;
            btnG.Click += BtnGoogleLogin_Click;
            card.Controls.Add(btnG);

            _loginPanel.Controls.Add(card);
            Controls.Add(_loginPanel);
        }

        // === HELPERS ===

        // Creates a GroupBox-like panel that auto-sizes
        private Panel MakeSection(string title)
        {
            var p = new Panel { Width = 1000, AutoSize = true, BackColor = ColorCard, Margin = new Padding(0, 0, 0, 20) };
            p.Paint += (s, e) => {
                using (var pen = new Pen(ColorBorder)) { e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); }
                using (var font = new Font("Segoe UI", 10F, FontStyle.Bold))
                using (var brush = new SolidBrush(ColorTextDim)) { e.Graphics.DrawString(title, font, brush, 20, 12); }
                using (var pen = new Pen(ColorBorder)) { e.Graphics.DrawLine(pen, 15, 36, p.Width - 15, 36); }
            };
            return p;
        }

        private Button MakeButton(string text, int w, int h)
        {
            return new Button { Text = text, Size = new Size(w, h), FlatStyle = FlatStyle.Flat, BackColor = ColorButtonBg, ForeColor = ColorText, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Cursor = Cursors.Hand, FlatAppearance = { BorderColor = ColorBorder, BorderSize = 1 } };
        }

        private Button MakeAutoSizedButton(string text, Color bg)
        {
            var b = MakeButton(text, 0, 0); // Size ignored initially
            b.BackColor = bg;
            b.AutoSize = true;
            // IMPORTANT: Enforce minimum height so they don't squash
            b.MinimumSize = new Size(120, 45); 
            b.Padding = new Padding(10, 0, 10, 0);
            b.Margin = new Padding(0, 0, 10, 10);
            return b;
        }

        private Button MakeTabButton(string text, bool active)
        {
            return new Button { Text = text, Size = new Size(200, 34), FlatStyle = FlatStyle.Flat, BackColor = active ? ColorTabActive : ColorTabInactive, ForeColor = ColorText, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, FlatAppearance = { BorderSize = 0 }, Margin = new Padding(0, 0, 5, 0) };
        }

        private TextBox MakeTextBox(int w, int h) => new TextBox { Size = new Size(w, h), BackColor = ColorSurface, ForeColor = ColorText, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
        private ComboBox MakeComboBox(int w) => new ComboBox { Size = new Size(w, 36), BackColor = ColorSurface, ForeColor = ColorText, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10), DropDownStyle = ComboBoxStyle.DropDownList };
        
        // === LOGIC STUBS ===
        private void Log(string t, string m) { if(!_txtLog.IsDisposed) _txtLog.AppendText($"[{DateTime.Now:HH:mm}] {m}\n"); }
        private void OpenUrl(string u) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = u, UseShellExecute = true }); } catch { } }
        private bool ConfirmToken(int c, string o) => MessageBox.Show($"Cost: {c}. Continue?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes;
        private void ShowError(string t, string m, Exception ex = null) => MessageBox.Show(m, t);
        
        // Navigation
        private void ShowLogin() { _loginPanel.Visible = true; _tabBar.Visible = false; _contentPanel.Visible = false; _logPanel.Visible = false; _headerPanel.Visible = false; }
        private void ShowMain() { _loginPanel.Visible = false; _tabBar.Visible = true; _contentPanel.Visible = true; _logPanel.Visible = true; _headerPanel.Visible = true; SwitchTab(0); }
        private void SwitchTab(int i) {
            _activeTab = i;
            _tabPats.BackColor = i == 0 ? ColorTabActive : ColorTabInactive;
            _tabDiag.BackColor = i == 1 ? ColorTabActive : ColorTabInactive;
            _tabFree.BackColor = i == 2 ? ColorTabActive : ColorTabInactive;
            _patsPanel.Visible = i == 0; _diagPanel.Visible = i == 1; _freePanel.Visible = i == 2;
        }

        // Handlers
        private void LoadSavedSession() { /* Load logic */ }
        private void DoLogin() { _userEmail = "user@example.com"; ShowMain(); }
        private void DoLogout() { ShowLogin(); }
        private void BtnGoogleLogin_Click(object s, EventArgs e) { DoLogin(); }
        
        private void BtnScan_Click(object s, EventArgs e) { }
        private void BtnConnect_Click(object s, EventArgs e) { }
        private void BtnReadVin_Click(object s, EventArgs e) { }
        private void BtnProgramKeys_Click(object s, EventArgs e) { }
        private void BtnEraseKeys_Click(object s, EventArgs e) { }
        private void BtnParamReset_Click(object s, EventArgs e) { }
        private void BtnEscl_Click(object s, EventArgs e) { }
        private void BtnDisableBcm_Click(object s, EventArgs e) { }
        private void BtnClearP160A_Click(object s, EventArgs e) { }
        private void BtnClearB10A2_Click(object s, EventArgs e) { }
        private void BtnClearCrush_Click(object s, EventArgs e) { }
        private void BtnGatewayUnlock_Click(object s, EventArgs e) { }
        private void BtnKeypadCode_Click(object s, EventArgs e) { }
        private void BtnBcmFactory_Click(object s, EventArgs e) { }
        private void BtnClearDtc_Click(object s, EventArgs e) { }
        private void BtnClearKam_Click(object s, EventArgs e) { }
        private void BtnVehicleReset_Click(object s, EventArgs e) { }
        private void BtnReadKeysCount_Click(object s, EventArgs e) { }
        private void BtnReadModuleInfo_Click(object s, EventArgs e) { }

        protected override void OnFormClosing(FormClosingEventArgs e) { try { _hsCanChannel?.Dispose(); _device?.Dispose(); _deviceManager?.Dispose(); } catch { } base.OnFormClosing(e); }
    }
}