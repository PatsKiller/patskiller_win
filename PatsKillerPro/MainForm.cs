using System;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        // Dark Theme Colors
        private readonly Color _colorBackground = Color.FromArgb(30, 30, 30);
        private readonly Color _colorPanel = Color.FromArgb(45, 45, 48);
        private readonly Color _colorBorder = Color.FromArgb(60, 60, 65);
        private readonly Color _colorText = Color.FromArgb(220, 220, 220);
        private readonly Color _colorTextDim = Color.FromArgb(150, 150, 150);
        private readonly Color _colorAccent = Color.FromArgb(0, 122, 204);
        private readonly Color _colorSuccess = Color.FromArgb(46, 204, 113);
        private readonly Color _colorWarning = Color.FromArgb(241, 196, 15);
        private readonly Color _colorDanger = Color.FromArgb(231, 76, 60);
        private readonly Color _colorButtonBg = Color.FromArgb(60, 60, 65);
        private readonly Color _colorButtonHover = Color.FromArgb(80, 80, 85);

        // State
        private bool _isLoggedIn = false;
        private string _userEmail = "";
        private string _authToken = "";
        private int _tokenBalance = 0;

        private J2534Device? _device;
        private J2534Channel? _hsCanChannel;
        private string _currentVin = "";
        private string _currentOutcode = "";

        // Controls
        private Panel _headerPanel = null!;
        private Panel _loginPanel = null!;
        private Panel _mainPanel = null!;
        private TabControl _tabControl = null!;
        private Label _lblStatus = null!;
        private Label _lblTokens = null!;
        private Label _lblUser = null!;
        private TextBox _txtEmail = null!;
        private TextBox _txtPassword = null!;
        private Button _btnLogin = null!;
        private Button _btnLogout = null!;
        private ComboBox _cmbDevices = null!;
        private ComboBox _cmbVehicles = null!;
        private TextBox _txtOutcode = null!;
        private TextBox _txtIncode = null!;
        private Label _lblVin = null!;
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
            this.Size = new Size(900, 700);
            this.MinimumSize = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = _colorBackground;
            this.ForeColor = _colorText;
            this.Font = new Font("Segoe UI", 9F);
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

        private void CreateHeader()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = _colorPanel
            };

            var lblTitle = new Label
            {
                Text = "🔑 PatsKiller Pro",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true,
                Location = new Point(15, 15)
            };
            _headerPanel.Controls.Add(lblTitle);

            _lblTokens = new Label
            {
                Text = "Tokens: --",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorSuccess,
                AutoSize = true,
                Location = new Point(650, 10)
            };
            _headerPanel.Controls.Add(_lblTokens);

            _lblUser = new Label
            {
                Text = "Not logged in",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Location = new Point(650, 32)
            };
            _headerPanel.Controls.Add(_lblUser);

            _btnLogout = CreateButton("Logout", 70, 28);
            _btnLogout.Location = new Point(800, 15);
            _btnLogout.Click += BtnLogout_Click;
            _btnLogout.Visible = false;
            _headerPanel.Controls.Add(_btnLogout);

            this.Controls.Add(_headerPanel);

            this.Resize += (s, e) =>
            {
                _lblTokens.Location = new Point(this.ClientSize.Width - 250, 10);
                _lblUser.Location = new Point(this.ClientSize.Width - 250, 32);
                _btnLogout.Location = new Point(this.ClientSize.Width - 90, 15);
            };
        }

        private void CreateLoginPanel()
        {
            _loginPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _colorBackground,
                Visible = false
            };

            var centerPanel = new Panel
            {
                Size = new Size(400, 350),
                BackColor = _colorPanel
            };
            centerPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(_colorBorder, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, centerPanel.Width - 1, centerPanel.Height - 1);
            };

            var lblLoginTitle = new Label
            {
                Text = "Login to PatsKiller.com",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = _colorText,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(380, 40),
                Location = new Point(10, 20)
            };
            centerPanel.Controls.Add(lblLoginTitle);

            var lblSubtitle = new Label
            {
                Text = "Connect your account to access token balance",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(380, 25),
                Location = new Point(10, 55)
            };
            centerPanel.Controls.Add(lblSubtitle);

            var lblEmail = new Label
            {
                Text = "Email:",
                ForeColor = _colorText,
                Location = new Point(30, 100),
                AutoSize = true
            };
            centerPanel.Controls.Add(lblEmail);

            _txtEmail = CreateTextBox();
            _txtEmail.Size = new Size(340, 30);
            _txtEmail.Location = new Point(30, 125);
            centerPanel.Controls.Add(_txtEmail);

            var lblPassword = new Label
            {
                Text = "Password:",
                ForeColor = _colorText,
                Location = new Point(30, 165),
                AutoSize = true
            };
            centerPanel.Controls.Add(lblPassword);

            _txtPassword = CreateTextBox();
            _txtPassword.Size = new Size(340, 30);
            _txtPassword.Location = new Point(30, 190);
            _txtPassword.UseSystemPasswordChar = true;
            _txtPassword.KeyPress += (s, e) => { if (e.KeyChar == (char)Keys.Enter) BtnLogin_Click(s, e); };
            centerPanel.Controls.Add(_txtPassword);

            _btnLogin = CreateButton("Login", 340, 40);
            _btnLogin.Location = new Point(30, 240);
            _btnLogin.BackColor = _colorAccent;
            _btnLogin.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _btnLogin.Click += BtnLogin_Click;
            centerPanel.Controls.Add(_btnLogin);

            var lblRegister = new Label
            {
                Text = "Don't have an account? Register at patskiller.com",
                ForeColor = _colorAccent,
                Font = new Font("Segoe UI", 9F, FontStyle.Underline),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(380, 25),
                Location = new Point(10, 295)
            };
            lblRegister.Click += (s, e) => OpenUrl("https://patskiller.com/register");
            centerPanel.Controls.Add(lblRegister);

            _loginPanel.Controls.Add(centerPanel);

            _loginPanel.Resize += (s, e) =>
            {
                centerPanel.Location = new Point(
                    (_loginPanel.ClientSize.Width - centerPanel.Width) / 2,
                    (_loginPanel.ClientSize.Height - centerPanel.Height) / 2 - 30
                );
            };

            this.Controls.Add(_loginPanel);
        }

        private void CreateMainPanel()
        {
            _mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _colorBackground,
                Padding = new Padding(10),
                Visible = false
            };

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F)
            };
            ApplyDarkThemeToTabControl(_tabControl);

            var tabPats = new TabPage("PATS Functions");
            var tabOther = new TabPage("Other Functions");
            ApplyDarkThemeToTab(tabPats);
            ApplyDarkThemeToTab(tabOther);

            CreatePatsTab(tabPats);
            CreateOtherTab(tabOther);

            _tabControl.TabPages.Add(tabPats);
            _tabControl.TabPages.Add(tabOther);

            _mainPanel.Controls.Add(_tabControl);

            _lblStatus = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = _colorPanel,
                ForeColor = _colorTextDim,
                Text = "  Ready - Click Scan to detect J2534 devices",
                TextAlign = ContentAlignment.MiddleLeft
            };
            _mainPanel.Controls.Add(_lblStatus);

            this.Controls.Add(_mainPanel);
        }

        private void CreatePatsTab(TabPage tab)
        {
            tab.AutoScroll = true;
            int y = 20;
            int margin = 20;

            // Device Section
            var grpDevice = CreateGroupBox("J2534 Device", margin, y, 820, 80);
            
            var lblDevice = new Label { Text = "Device:", ForeColor = _colorText, Location = new Point(15, 35), AutoSize = true };
            grpDevice.Controls.Add(lblDevice);

            _cmbDevices = CreateComboBox();
            _cmbDevices.Location = new Point(80, 32);
            _cmbDevices.Size = new Size(400, 28);
            _toolTip.SetToolTip(_cmbDevices, "Select your J2534 pass-thru device");
            grpDevice.Controls.Add(_cmbDevices);

            var btnScan = CreateButton("Scan", 80, 30);
            btnScan.Location = new Point(500, 30);
            btnScan.Click += BtnScan_Click;
            _toolTip.SetToolTip(btnScan, "Scan for installed J2534 devices");
            grpDevice.Controls.Add(btnScan);

            var btnConnect = CreateButton("Connect", 100, 30);
            btnConnect.Location = new Point(590, 30);
            btnConnect.BackColor = _colorSuccess;
            btnConnect.Click += BtnConnect_Click;
            _toolTip.SetToolTip(btnConnect, "Connect to selected J2534 device");
            grpDevice.Controls.Add(btnConnect);

            tab.Controls.Add(grpDevice);
            y += 100;

            // Vehicle Section
            var grpVehicle = CreateGroupBox("Vehicle", margin, y, 820, 110);

            var btnReadVin = CreateButton("🚗 Read Vehicle", 140, 35);
            btnReadVin.Location = new Point(15, 30);
            btnReadVin.BackColor = _colorAccent;
            btnReadVin.Click += BtnReadVin_Click;
            _toolTip.SetToolTip(btnReadVin, "Auto-detect vehicle from VIN (FREE)");
            grpVehicle.Controls.Add(btnReadVin);

            _lblVin = new Label
            {
                Text = "VIN: Not read",
                ForeColor = _colorTextDim,
                Location = new Point(170, 38),
                AutoSize = true
            };
            grpVehicle.Controls.Add(_lblVin);

            var lblManual = new Label { Text = "Or select manually:", ForeColor = _colorText, Location = new Point(15, 75), AutoSize = true };
            grpVehicle.Controls.Add(lblManual);

            _cmbVehicles = CreateComboBox();
            _cmbVehicles.Location = new Point(145, 72);
            _cmbVehicles.Size = new Size(350, 28);
            _cmbVehicles.Items.AddRange(new[] { "-- Select Vehicle --", "2014-2020 Ford F-150", "2015-2020 Ford Mustang", "2017-2023 Ford Super Duty", "2013-2019 Ford Escape", "2011-2019 Ford Explorer", "2013-2020 Ford Fusion", "2015-2022 Ford Edge", "2014-2020 Lincoln MKZ", "2015-2020 Lincoln MKC", "2018-2023 Lincoln Navigator" });
            _cmbVehicles.SelectedIndex = 0;
            grpVehicle.Controls.Add(_cmbVehicles);

            var chkKeyless = new CheckBox { Text = "Keyless (Push Start)", ForeColor = _colorText, Location = new Point(520, 74), AutoSize = true };
            grpVehicle.Controls.Add(chkKeyless);

            tab.Controls.Add(grpVehicle);
            y += 130;

            // PATS Codes Section
            var grpCodes = CreateGroupBox("PATS Codes", margin, y, 820, 100);

            var lblOutcode = new Label { Text = "OUTCODE:", ForeColor = _colorText, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new Point(15, 40), AutoSize = true };
            grpCodes.Controls.Add(lblOutcode);

            _txtOutcode = CreateTextBox();
            _txtOutcode.Location = new Point(100, 37);
            _txtOutcode.Size = new Size(150, 28);
            _txtOutcode.ReadOnly = true;
            _txtOutcode.Font = new Font("Consolas", 12F, FontStyle.Bold);
            _txtOutcode.TextAlign = HorizontalAlignment.Center;
            grpCodes.Controls.Add(_txtOutcode);

            var btnCopy = CreateButton("📋 Copy", 70, 28);
            btnCopy.Location = new Point(260, 37);
            btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) { Clipboard.SetText(_txtOutcode.Text); UpdateStatus("Outcode copied to clipboard"); } };
            grpCodes.Controls.Add(btnCopy);

            var btnGetIncode = CreateButton("🌐 Get Incode at patskiller.com", 200, 28);
            btnGetIncode.Location = new Point(340, 37);
            btnGetIncode.BackColor = _colorWarning;
            btnGetIncode.ForeColor = Color.Black;
            btnGetIncode.Click += (s, e) => OpenUrl("https://patskiller.com/calculator");
            grpCodes.Controls.Add(btnGetIncode);

            var lblIncode = new Label { Text = "INCODE:", ForeColor = _colorText, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new Point(560, 40), AutoSize = true };
            grpCodes.Controls.Add(lblIncode);

            _txtIncode = CreateTextBox();
            _txtIncode.Location = new Point(640, 37);
            _txtIncode.Size = new Size(150, 28);
            _txtIncode.Font = new Font("Consolas", 12F, FontStyle.Bold);
            _txtIncode.TextAlign = HorizontalAlignment.Center;
            grpCodes.Controls.Add(_txtIncode);

            tab.Controls.Add(grpCodes);
            y += 120;

            // Key Operations Section
            var grpKeys = CreateGroupBox("Key Operations", margin, y, 820, 90);

            var btnProgram = CreateButton("🔑 Program Keys", 150, 40);
            btnProgram.Location = new Point(20, 35);
            btnProgram.BackColor = _colorSuccess;
            btnProgram.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnProgram.Click += BtnProgramKeys_Click;
            _toolTip.SetToolTip(btnProgram, "Program new keys (FREE after incode)");
            grpKeys.Controls.Add(btnProgram);

            var btnErase = CreateButton("⚠️ Erase All Keys", 150, 40);
            btnErase.Location = new Point(180, 35);
            btnErase.BackColor = _colorDanger;
            btnErase.Click += BtnEraseKeys_Click;
            _toolTip.SetToolTip(btnErase, "WARNING: Erases ALL keys! (1 token)");
            grpKeys.Controls.Add(btnErase);

            var btnParamReset = CreateButton("🔄 Parameter Reset", 150, 40);
            btnParamReset.Location = new Point(340, 35);
            btnParamReset.Click += BtnParamReset_Click;
            _toolTip.SetToolTip(btnParamReset, "Sync BCM/PCM parameters (FREE)");
            grpKeys.Controls.Add(btnParamReset);

            var btnEscl = CreateButton("🔒 Initialize ESCL", 150, 40);
            btnEscl.Location = new Point(500, 35);
            btnEscl.Click += BtnEscl_Click;
            _toolTip.SetToolTip(btnEscl, "Initialize steering lock (1 token)");
            grpKeys.Controls.Add(btnEscl);

            var btnDisableBcm = CreateButton("🔓 Disable BCM", 150, 40);
            btnDisableBcm.Location = new Point(660, 35);
            btnDisableBcm.Click += BtnDisableBcm_Click;
            _toolTip.SetToolTip(btnDisableBcm, "All Keys Lost mode");
            grpKeys.Controls.Add(btnDisableBcm);

            tab.Controls.Add(grpKeys);
        }

        private void CreateOtherTab(TabPage tab)
        {
            tab.AutoScroll = true;
            int y = 20;
            int margin = 20;

            // FREE Operations
            var grpFree = CreateGroupBox("FREE Operations (No Token Cost)", margin, y, 820, 110);
            grpFree.ForeColor = _colorSuccess;

            var btnClearDtc = CreateButton("Clear All DTCs", 150, 35);
            btnClearDtc.Location = new Point(20, 30);
            btnClearDtc.Click += BtnClearDtc_Click;
            grpFree.Controls.Add(btnClearDtc);

            var btnClearKam = CreateButton("Clear KAM", 150, 35);
            btnClearKam.Location = new Point(180, 30);
            btnClearKam.Click += BtnClearKam_Click;
            grpFree.Controls.Add(btnClearKam);

            var btnReset = CreateButton("Vehicle Reset", 150, 35);
            btnReset.Location = new Point(340, 30);
            btnReset.Click += BtnVehicleReset_Click;
            grpFree.Controls.Add(btnReset);

            var btnReadKeys = CreateButton("Read Keys Count", 150, 35);
            btnReadKeys.Location = new Point(500, 30);
            btnReadKeys.Click += BtnReadKeysCount_Click;
            grpFree.Controls.Add(btnReadKeys);

            var btnModuleInfo = CreateButton("Read Module Info", 310, 35);
            btnModuleInfo.Location = new Point(20, 70);
            btnModuleInfo.Click += BtnReadModuleInfo_Click;
            grpFree.Controls.Add(btnModuleInfo);

            tab.Controls.Add(grpFree);
            y += 130;

            // Token Operations
            var grpToken = CreateGroupBox("Token Operations (1 Token Each)", margin, y, 820, 160);
            grpToken.ForeColor = _colorWarning;

            var btnKeypad = CreateButton("Read/Write Keypad Code", 200, 35);
            btnKeypad.Location = new Point(20, 30);
            btnKeypad.Click += BtnKeypadCode_Click;
            grpToken.Controls.Add(btnKeypad);

            var btnGateway = CreateButton("Gateway Unlock (2020+)", 200, 35);
            btnGateway.Location = new Point(230, 30);
            btnGateway.Click += BtnGatewayUnlock_Click;
            grpToken.Controls.Add(btnGateway);

            var btnP160A = CreateButton("Clear P160A (PCM)", 150, 35);
            btnP160A.Location = new Point(20, 75);
            btnP160A.Click += BtnClearP160A_Click;
            grpToken.Controls.Add(btnP160A);

            var btnB10A2 = CreateButton("Clear B10A2 (BCM)", 150, 35);
            btnB10A2.Location = new Point(180, 75);
            btnB10A2.Click += BtnClearB10A2_Click;
            grpToken.Controls.Add(btnB10A2);

            var btnCrush = CreateButton("Clear Crush Event", 150, 35);
            btnCrush.Location = new Point(340, 75);
            btnCrush.Click += BtnClearCrush_Click;
            grpToken.Controls.Add(btnCrush);

            var btnBcmFactory = CreateButton("⚠️ BCM Factory Defaults (2-3 tokens)", 300, 35);
            btnBcmFactory.Location = new Point(20, 120);
            btnBcmFactory.BackColor = _colorDanger;
            btnBcmFactory.Click += BtnBcmFactory_Click;
            grpToken.Controls.Add(btnBcmFactory);

            tab.Controls.Add(grpToken);
            y += 180;

            // Links
            var grpLinks = CreateGroupBox("Resources", margin, y, 820, 80);

            var btnTutorial = CreateButton("📖 Software Tutorial", 180, 35);
            btnTutorial.Location = new Point(20, 30);
            btnTutorial.Click += (s, e) => OpenUrl("https://patskiller.com/faqs");
            grpLinks.Controls.Add(btnTutorial);

            var btnBuyTokens = CreateButton("💳 Buy Tokens", 150, 35);
            btnBuyTokens.Location = new Point(210, 30);
            btnBuyTokens.BackColor = _colorAccent;
            btnBuyTokens.Click += (s, e) => OpenUrl("https://patskiller.com/buy-tokens");
            grpLinks.Controls.Add(btnBuyTokens);

            var btnSupport = CreateButton("📧 Contact Support", 160, 35);
            btnSupport.Location = new Point(370, 30);
            btnSupport.Click += (s, e) => OpenUrl("https://patskiller.com/contact");
            grpLinks.Controls.Add(btnSupport);

            tab.Controls.Add(grpLinks);
        }

        #region UI Helpers

        private Button CreateButton(string text, int width, int height)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorButtonBg,
                ForeColor = _colorText,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9F)
            };
            btn.FlatAppearance.BorderColor = _colorBorder;
            btn.FlatAppearance.BorderSize = 1;
            btn.MouseEnter += (s, e) => { if (btn.BackColor == _colorButtonBg) btn.BackColor = _colorButtonHover; };
            btn.MouseLeave += (s, e) => { if (btn.BackColor == _colorButtonHover) btn.BackColor = _colorButtonBg; };
            return btn;
        }

        private TextBox CreateTextBox()
        {
            return new TextBox
            {
                BackColor = Color.FromArgb(50, 50, 55),
                ForeColor = _colorText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F)
            };
        }

        private ComboBox CreateComboBox()
        {
            return new ComboBox
            {
                BackColor = Color.FromArgb(50, 50, 55),
                ForeColor = _colorText,
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10F)
            };
        }

        private GroupBox CreateGroupBox(string title, int x, int y, int width, int height)
        {
            var grp = new GroupBox
            {
                Text = title,
                Location = new Point(x, y),
                Size = new Size(width, height),
                ForeColor = _colorText,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            return grp;
        }

        private void ApplyDarkThemeToTabControl(TabControl tc)
        {
            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
            tc.DrawItem += (s, e) =>
            {
                var tab = tc.TabPages[e.Index];
                var bounds = tc.GetTabRect(e.Index);
                var isSelected = tc.SelectedIndex == e.Index;
                
                using var bgBrush = new SolidBrush(isSelected ? _colorPanel : _colorBackground);
                e.Graphics.FillRectangle(bgBrush, bounds);
                
                using var textBrush = new SolidBrush(isSelected ? _colorText : _colorTextDim);
                var textSize = e.Graphics.MeasureString(tab.Text, tc.Font);
                var textX = bounds.X + (bounds.Width - textSize.Width) / 2;
                var textY = bounds.Y + (bounds.Height - textSize.Height) / 2;
                e.Graphics.DrawString(tab.Text, tc.Font, textBrush, textX, textY);
            };
        }

        private void ApplyDarkThemeToTab(TabPage tab)
        {
            tab.BackColor = _colorBackground;
            tab.ForeColor = _colorText;
        }

        private void UpdateStatus(string message)
        {
            if (_lblStatus != null) _lblStatus.Text = "  " + message;
            Logger.Info(message);
        }

        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex) { Logger.Error($"Failed to open URL: {url}", ex); }
        }

        private bool ConfirmTokenCost(int cost, string operation, string details = "")
        {
            if (cost == 0) return true;
            var message = $"This operation will cost {cost} token(s).\n\nOperation: {operation}\n" +
                         (string.IsNullOrEmpty(details) ? "" : $"\n{details}\n") +
                         $"\nYour balance: {_tokenBalance} tokens\n\nContinue?";
            return MessageBox.Show(message, "Confirm Token Cost", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void ShowError(string title, string message, Exception? ex = null)
        {
            var fullMsg = message;
            if (ex != null) fullMsg += $"\n\nDetails: {ex.Message}";
            if (message.Contains("security") || message.Contains("denied")) fullMsg += "\n\n💡 Tip: Wait 10 minutes for timeout.";
            else if (message.Contains("response") || message.Contains("timeout")) fullMsg += "\n\n💡 Tip: Check ignition is ON.";
            MessageBox.Show(fullMsg, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Logger.Error($"{title}: {message}", ex);
        }

        #endregion

        #region Login/Logout

        private void ShowLoginPanel()
        {
            _loginPanel.Visible = true;
            _mainPanel.Visible = false;
            _btnLogout.Visible = false;
            _lblTokens.Text = "Tokens: --";
            _lblUser.Text = "Not logged in";
        }

        private void ShowMainPanel()
        {
            _loginPanel.Visible = false;
            _mainPanel.Visible = true;
            _btnLogout.Visible = true;
        }

        private void LoadSavedCredentials()
        {
            var email = Settings.GetString("email", "");
            var token = Settings.GetString("auth_token", "");
            if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(token))
            {
                _txtEmail.Text = email;
                _userEmail = email;
                _authToken = token;
                _ = ValidateAndShowMain();
            }
        }

        private async Task ValidateAndShowMain()
        {
            try
            {
                var balance = await FetchTokenBalance();
                if (balance >= 0)
                {
                    _isLoggedIn = true;
                    _tokenBalance = balance;
                    _lblTokens.Text = $"Tokens: {_tokenBalance}";
                    _lblUser.Text = _userEmail;
                    ShowMainPanel();
                }
                else ShowLoginPanel();
            }
            catch { ShowLoginPanel(); }
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
            _btnLogin.Text = "Logging in...";

            try
            {
                await Task.Delay(500);
                _isLoggedIn = true;
                _userEmail = email;
                _authToken = "demo_token_" + DateTime.Now.Ticks;
                _tokenBalance = 10;
                Settings.SetString("email", email);
                Settings.SetString("auth_token", _authToken);
                Settings.Save();
                _lblTokens.Text = $"Tokens: {_tokenBalance}";
                _lblUser.Text = _userEmail;
                ShowMainPanel();
                UpdateStatus($"Logged in as {email}");
            }
            catch (Exception ex) { ShowError("Login Failed", "Could not connect", ex); }
            finally
            {
                _btnLogin.Enabled = true;
                _btnLogin.Text = "Login";
            }
        }

        private void BtnLogout_Click(object? sender, EventArgs e)
        {
            _isLoggedIn = false;
            _userEmail = "";
            _authToken = "";
            _tokenBalance = 0;
            Settings.Remove("auth_token");
            Settings.Save();
            _txtPassword.Text = "";
            ShowLoginPanel();
            UpdateStatus("Logged out");
        }

        private async Task<int> FetchTokenBalance()
        {
            await Task.Delay(100);
            return _tokenBalance;
        }

        #endregion

        #region Device Operations

        private void BtnScan_Click(object? sender, EventArgs e)
        {
            try
            {
                UpdateStatus("Scanning for J2534 devices...");
                _cmbDevices.Items.Clear();
                var devices = J2534DeviceManager.GetAvailableDevices();
                if (devices.Count == 0)
                {
                    _cmbDevices.Items.Add("No devices found");
                    UpdateStatus("No J2534 devices found");
                }
                else
                {
                    foreach (var device in devices) _cmbDevices.Items.Add(device);
                    _cmbDevices.SelectedIndex = 0;
                    UpdateStatus($"Found {devices.Count} device(s)");
                }
            }
            catch (Exception ex) { ShowError("Scan Error", "Failed to scan", ex); }
        }

        private void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_cmbDevices.SelectedItem == null || _cmbDevices.SelectedItem.ToString() == "No devices found")
            {
                MessageBox.Show("Select a device first.", "Connect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                UpdateStatus("Connecting...");
                var deviceInfo = _cmbDevices.SelectedItem as J2534DeviceInfo;
                if (deviceInfo == null) { ShowError("Error", "Invalid device"); return; }
                _device = new J2534Device(deviceInfo);
                _device.Open();
                _hsCanChannel = _device.OpenChannel(J2534Protocol.ISO15765, J2534Baud.CAN_500K, J2534ConnectFlags.NONE);
                UpdateStatus($"Connected to {deviceInfo.Name}");
                MessageBox.Show($"Connected to {deviceInfo.Name}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ShowError("Connection Failed", "Could not connect", ex); }
        }

        #endregion

        #region Vehicle Operations

        private async void BtnReadVin_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect to device first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            try
            {
                UpdateStatus("Reading VIN...");
                var uds = new UdsService(_hsCanChannel);
                _currentVin = await Task.Run(() => uds.ReadVin(ModuleAddresses.BCM_TX));
                if (string.IsNullOrEmpty(_currentVin)) _currentVin = await Task.Run(() => uds.ReadVin(ModuleAddresses.PCM_TX));

                if (!string.IsNullOrEmpty(_currentVin))
                {
                    _lblVin.Text = $"VIN: {_currentVin}";
                    _lblVin.ForeColor = _colorSuccess;
                    UpdateStatus("Reading outcode...");
                    _currentOutcode = await Task.Run(() => uds.ReadOutcode(ModuleAddresses.BCM_TX));
                    _txtOutcode.Text = _currentOutcode;
                    UpdateStatus($"Vehicle: {_currentVin}");
                }
                else
                {
                    _lblVin.Text = "VIN: Could not read";
                    _lblVin.ForeColor = _colorDanger;
                    UpdateStatus("Select vehicle manually");
                }
            }
            catch (Exception ex) { ShowError("Read Error", "Failed to read", ex); }
        }

        #endregion

        #region Key Operations

        private async void BtnProgramKeys_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            try
            {
                UpdateStatus("Programming key...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                var result = await Task.Run(() => pats.ProgramKeys(incode));
                if (result)
                {
                    MessageBox.Show("Key programmed!\n\nRemove key, insert next, click Program again.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    UpdateStatus("Key programmed");
                }
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
                UpdateStatus("Erasing keys...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.EraseAllKeys(incode));
                MessageBox.Show("Keys erased! Program 2+ new keys now.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateStatus("Keys erased");
            }
            catch (Exception ex) { ShowError("Erase Failed", "Failed", ex); }
        }

        private async void BtnParamReset_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            try
            {
                UpdateStatus("Parameter reset...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.ParameterReset());
                MessageBox.Show("Done!\n\nIgnition OFF 15s, then ON.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("Parameter reset complete");
            }
            catch (Exception ex) { ShowError("Reset Failed", "Failed", ex); }
        }

        private async void BtnEscl_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_ESCL_INIT, "Initialize ESCL")) return;
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            try
            {
                UpdateStatus("Initializing ESCL...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.InitializeEscl(incode));
                MessageBox.Show("ESCL initialized!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("ESCL done");
            }
            catch (Exception ex) { ShowError("ESCL Failed", "Failed", ex); }
        }

        private async void BtnDisableBcm_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode)) { MessageBox.Show("Enter incode.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            try
            {
                UpdateStatus("Disabling BCM security...");
                var uds = new UdsService(_hsCanChannel);
                var pats = new PatsOperations(uds);
                await Task.Run(() => pats.DisableBcmSecurity(incode));
                MessageBox.Show("BCM security disabled for All Keys Lost programming.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateStatus("BCM disabled");
            }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        #endregion

        #region Other Operations

        private async void BtnClearDtc_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { UpdateStatus("Clearing DTCs..."); var uds = new UdsService(_hsCanChannel); await Task.Run(() => uds.ClearAllDtcs()); MessageBox.Show("DTCs cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); UpdateStatus("DTCs cleared"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnClearKam_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { UpdateStatus("Clearing KAM..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearKam()); MessageBox.Show("KAM cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); UpdateStatus("KAM cleared"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnVehicleReset_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { UpdateStatus("Resetting..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.VehicleReset()); MessageBox.Show("Reset complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); UpdateStatus("Reset done"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnReadKeysCount_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { UpdateStatus("Reading keys..."); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); var count = await Task.Run(() => pats.ReadKeysCount()); MessageBox.Show($"Keys: {count}", "Count", MessageBoxButtons.OK, MessageBoxIcon.Information); UpdateStatus($"Keys: {count}"); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnReadModuleInfo_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { UpdateStatus("Reading modules..."); var uds = new UdsService(_hsCanChannel); var info = await Task.Run(() => uds.ReadAllModuleInfo()); MessageBox.Show(info, "Module Info", MessageBoxButtons.OK, MessageBoxIcon.Information); UpdateStatus("Done"); }
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
                try { var incode = _txtIncode.Text.Trim(); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); var code = await Task.Run(() => pats.ReadKeypadCode(incode)); MessageBox.Show($"Code: {code}", "Keypad", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { ShowError("Failed", "Failed", ex); }
            }
            else
            {
                var newCode = Microsoft.VisualBasic.Interaction.InputBox("Enter 5-digit code (1-9):", "Write Keypad", "");
                if (string.IsNullOrEmpty(newCode) || newCode.Length != 5) return;
                if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_KEYPAD_WRITE, "Write Keypad")) return;
                try { var incode = _txtIncode.Text.Trim(); var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.WriteKeypadCode(incode, newCode)); MessageBox.Show($"Code set: {newCode}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { ShowError("Failed", "Failed", ex); }
            }
        }

        private async void BtnGatewayUnlock_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds);
                var hasGateway = await Task.Run(() => pats.DetectGateway());
                if (!hasGateway) { MessageBox.Show("No gateway (pre-2020 vehicle).", "Gateway", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_GATEWAY_UNLOCK, "Unlock Gateway")) return;
                var incode = _txtIncode.Text.Trim();
                await Task.Run(() => pats.UnlockGateway(incode));
                MessageBox.Show("Gateway unlocked!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnClearP160A_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_CLEAR_P160A, "Clear P160A")) return;
            try { var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearP160A()); MessageBox.Show("P160A cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnClearB10A2_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_CLEAR_B10A2, "Clear B10A2")) return;
            try { var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearB10A2()); MessageBox.Show("B10A2 cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        private async void BtnClearCrush_Click(object? sender, EventArgs e)
        {
            if (_hsCanChannel == null) { MessageBox.Show("Connect first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ConfirmTokenCost(PatsOperations.TOKEN_COST_CLEAR_CRUSH, "Clear Crush Event")) return;
            try { var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.ClearCrushEvent()); MessageBox.Show("Crush cleared!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
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

            try { var uds = new UdsService(_hsCanChannel); var pats = new PatsOperations(uds); await Task.Run(() => pats.BcmFactoryDefaults(incodes)); MessageBox.Show("BCM reset!\nScanner adaptation required!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (Exception ex) { ShowError("Failed", "Failed", ex); }
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _hsCanChannel?.Dispose(); _device?.Close(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
