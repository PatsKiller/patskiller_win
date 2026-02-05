using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Forms;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;

namespace PatsKillerPro
{
    /// <summary>
    /// Main application form – Phase 2 Licensing Integration.
    ///
    /// HYBRID AUTH MODEL:
    ///   IsLicensed   → valid license key bound to this machine
    ///   HasSSO       → valid Google SSO session (auth token)
    ///   IsAuthorized → IsLicensed OR HasSSO   (grants app access)
    ///   CanUseTokens → HasSSO                 (tokens need server tracking)
    ///
    /// TOKEN COSTS:
    ///   Key Session:      1 token (unlimited keys while same outcode)
    ///   Parameter Reset:  1 token per module (BCM, ABS, PCM)
    ///   Utility:          1 token each
    ///   Gateway Unlock:   1 token (then key ops FREE for 10 min)
    ///   Diagnostics:      FREE
    /// </summary>
    public partial class MainForm : Form
    {
        // ═════════════ COLORS ═════════════
        private static class C
        {
            public static readonly Color HeaderBg        = Color.FromArgb(30, 64, 175);
            public static readonly Color HeaderText      = Color.White;
            public static readonly Color SessionBg       = Color.FromArgb(34, 197, 94);
            public static readonly Color PurchaseBg      = Color.FromArgb(34, 197, 94);
            public static readonly Color PromoBg         = Color.FromArgb(134, 239, 172);
            public static readonly Color PromoText       = Color.Black;
            public static readonly Color Panel           = Color.White;
            public static readonly Color Form            = Color.FromArgb(229, 231, 235);
            public static readonly Color BtnPrimary      = Color.FromArgb(59, 130, 246);
            public static readonly Color BtnSuccess      = Color.FromArgb(34, 197, 94);
            public static readonly Color BtnDanger       = Color.FromArgb(239, 68, 68);
            public static readonly Color BtnWarning      = Color.FromArgb(245, 158, 11);
            public static readonly Color BtnDisabled     = Color.FromArgb(209, 213, 219);
            public static readonly Color Success         = Color.FromArgb(22, 163, 74);
            public static readonly Color Warning         = Color.FromArgb(202, 138, 4);
            public static readonly Color Error           = Color.FromArgb(220, 38, 38);
            public static readonly Color Info            = Color.FromArgb(107, 114, 128);
            public static readonly Color LogBg           = Color.Black;
            public static readonly Color LogOk           = Color.FromArgb(74, 222, 128);
            public static readonly Color LogWarn         = Color.FromArgb(250, 204, 21);
            public static readonly Color LogErr          = Color.FromArgb(248, 113, 113);
            public static readonly Color LogInfo         = Color.FromArgb(156, 163, 175);
            public static readonly Color LicBadgeBg      = Color.FromArgb(59, 130, 246);
            public static readonly Color GraceBg         = Color.FromArgb(245, 158, 11);
            public static readonly Color SsoPromptBg     = Color.FromArgb(59, 130, 246);
        }

        // ═════════════ APP STATE ═════════════
        private bool _deviceConnected;
        private bool _vehicleConnected;
        private bool _incodeVerified;
        private bool _gatewaySessionActive;
        private int _gatewayCountdown;
        private bool _is2020Plus;
        private string _currentVin = "";
        private string _currentOutcode = "";
        private string _currentIncode = "";
        private string? _currentYear;
        private string? _currentModel;

        // Param reset
        private bool _paramResetActive;
        private int _paramResetStep;
        private ParamResetModule[]? _paramResetModules;
        private bool _skipAbs;

        // ═════════════ AUTH STATE (Phase 2) ═════════════
        private string? _authToken;

        private bool IsLicensed   => LicenseService.Instance.IsLicensed;
        private bool HasSSO       => !string.IsNullOrWhiteSpace(_authToken);
        private bool IsAuthorized => IsLicensed || HasSSO;
        private bool CanUseTokens => HasSSO;   // tokens always need SSO

        // ═════════════ UI CONTROLS ═════════════
        private Panel _headerPanel = null!;
        private Label _lblTitle = null!;
        private Label _lblUserEmail = null!;
        private Label _lblPurchaseTokens = null!;
        private Label _lblPromoTokens = null!;
        private Label _lblLicBadge = null!;      // "PROFESSIONAL"
        private Label _lblLicStatus = null!;     // "🔑 John Doe"
        private Label _lblGrace = null!;         // "⚠ 2d grace"
        private Button _btnAccount = null!;

        private Panel _sessionBanner = null!;
        private Label _lblSessionText = null!;
        private Label _lblSessionTimer = null!;

        private Panel _ssoPromptBanner = null!;  // "Sign in for tokens"
        private Label _lblSsoPrompt = null!;
        private Button _btnSsoPromptLogin = null!;

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

        private Panel _keyOpsPanel = null!;
        private Button _btnEraseKeys = null!;
        private Button _btnProgramKeys = null!;

        private Panel _paramResetPanel = null!;
        private CheckBox _chkSkipAbs = null!;
        private Button _btnParamReset = null!;
        private Label _lblParamStatus = null!;
        private ProgressBar _paramProgress = null!;

        private Panel _gatewayPanel = null!;
        private Button _btnGateway = null!;

        private RichTextBox _rtbLog = null!;
        private StatusStrip _statusBar = null!;
        private ToolStripStatusLabel _tsStatus = null!;

        private System.Windows.Forms.Timer _gatewayTimer = null!;
        // Heartbeat uses System.Threading.Timer inside LicenseService (v18)

        // ═══════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════
        public MainForm()
        {
            InitializeComponent();
            BuildUI();
            WireEvents();

            TokenBalanceService.Instance.BalanceChanged += OnTokenBalanceChanged;
            LicenseService.Instance.OnLicenseChanged += OnLicenseChanged;
            LicenseService.Instance.OnLogMessage += OnLicenseLog;

            LogI("PatsKiller Pro v2.0 started");
            this.Shown += MainForm_Shown;
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(700, 850);
            MinimumSize = new Size(650, 750);
            Name = "MainForm";
            Text = "PatsKiller Pro v2.0";
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C.Form;
            Font = new Font("Segoe UI", 9F);
            FormClosing += MainForm_FormClosing;
            ResumeLayout(false);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PHASE 2: STARTUP AUTH FLOW
        // ═══════════════════════════════════════════════════════════════
        private async void MainForm_Shown(object? sender, EventArgs e)
        {
            // 1. Check cached license
            LogI("Checking license...");
            var licResult = await LicenseService.Instance.ValidateAsync();

            if (licResult.IsValid)
            {
                LogS($"Licensed to {licResult.LicensedTo}");
                UpdateLicenseHeader(licResult);
            }
            else if (licResult.IsGracePeriod)
            {
                LogW($"License grace period: {licResult.GraceDaysRemaining}d remaining — connect to internet soon");
                UpdateLicenseHeader(licResult);
            }
            else
            {
                LogI($"License check: {licResult.Message}");
            }

            // 2. Check cached SSO session
            LoadSession();

            // 3. Authorized?
            if (IsAuthorized)
            {
                if (HasSSO)
                {
                    await RefreshTokensAsync();
                    UpdateTokenDisplay();
                }
                ShowMainUI();

                if (IsLicensed && !HasSSO) _ssoPromptBanner.Visible = true;

                LogI("Click Scan to detect J2534 devices");
                ProActivityLogger.Instance.LogAppStart();
                return;
            }

            // 4. Need auth
            LogI("Authentication required");
            await PromptHybridLoginAsync();
        }

        /// <summary>
        /// Loop: show Google SSO dialog → on Retry show license dialog → repeat until authorized.
        /// </summary>
        private async Task PromptHybridLoginAsync()
        {
            while (!IsAuthorized)
            {
                using var loginForm = new GoogleLoginForm();
                var dlg = loginForm.ShowDialog(this);

                if (dlg == DialogResult.OK && !string.IsNullOrWhiteSpace(loginForm.AuthToken))
                {
                    // SSO success
                    _authToken = loginForm.AuthToken;
                    SaveSession(loginForm.AuthToken, loginForm.RefreshToken, loginForm.UserEmail);
                    TokenBalanceService.Instance.SetAuthContext(loginForm.AuthToken, loginForm.UserEmail ?? "");
                    ProActivityLogger.Instance.SetAuthContext(loginForm.AuthToken, loginForm.UserEmail ?? "");
                    _lblUserEmail.Text = loginForm.UserEmail ?? "";
                    _lblUserEmail.Visible = true;
                    LogS($"Signed in as {loginForm.UserEmail}");
                    await RefreshTokensAsync();
                    UpdateTokenDisplay();
                }
                else if (dlg == DialogResult.Retry)
                {
                    // "Use License Key Instead"
                    await ShowLicenseDialogAsync();
                }
                else
                {
                    // Cancel → offer license key fallback
                    var ask = MessageBox.Show(
                        "You can also activate with a license key.\nWould you like to enter one?",
                        "PatsKiller Pro", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (ask == DialogResult.Yes)
                        await ShowLicenseDialogAsync();
                    else
                    {
                        Close();
                        return;
                    }
                }
            }

            if (IsAuthorized)
            {
                ShowMainUI();
                if (IsLicensed && !HasSSO) _ssoPromptBanner.Visible = true;
                LogI("Click Scan to detect J2534 devices");
                ProActivityLogger.Instance.LogAppStart();
            }
        }

        private async Task<bool> ShowLicenseDialogAsync()
        {
            using var form = new LicenseActivationForm();
            var dlg = form.ShowDialog(this);
            if (dlg == DialogResult.OK && form.Activated && form.ActivationResult != null)
            {
                LogS($"License activated: {form.ActivationResult.LicensedTo}");
                UpdateLicenseHeader(form.ActivationResult);
                return true;
            }
            return false;
        }

        private void ShowMainUI()
        {
            _tabControl.Visible = true;
            SetStatus("Ready");
        }

        // ═══════════════════════════════════════════════════════════════
        //  PHASE 2: LICENSE HEADER UI
        // ═══════════════════════════════════════════════════════════════
        private void UpdateLicenseHeader(LicenseValidationResult r)
        {
            if (InvokeRequired) { BeginInvoke(() => UpdateLicenseHeader(r)); return; }

            if (r.IsValid || r.IsGracePeriod)
            {
                _lblLicBadge.Text = (r.LicenseType ?? "standard").ToUpperInvariant();
                _lblLicBadge.Visible = true;

                _lblLicStatus.Text = $"🔑 {r.LicensedTo}";
                _lblLicStatus.Visible = true;

                if (r.IsGracePeriod)
                {
                    _lblGrace.Text = $"⚠ {r.GraceDaysRemaining}d grace";
                    _lblGrace.Visible = true;
                }
                else
                {
                    _lblGrace.Visible = false;
                }
            }
            else
            {
                _lblLicBadge.Visible = false;
                _lblLicStatus.Visible = false;
                _lblGrace.Visible = false;
            }
            LayoutHeaderRight();
        }

        private void OnLicenseChanged(LicenseValidationResult r)
        {
            UpdateLicenseHeader(r);
        }

        private void OnLicenseLog(string type, string msg)
        {
            if (InvokeRequired) { BeginInvoke(() => OnLicenseLog(type, msg)); return; }
            switch (type)
            {
                case "success": LogS(msg); break;
                case "warning": LogW(msg); break;
                case "error":   LogE(msg); break;
                default:        LogI(msg); break;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SESSION PERSISTENCE
        // ═══════════════════════════════════════════════════════════════
        private static readonly string SessionFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PatsKillerPro", "session.json");

        private void LoadSession()
        {
            try
            {
                if (!File.Exists(SessionFile)) return;
                var s = JsonSerializer.Deserialize<SessionData>(File.ReadAllText(SessionFile));
                if (s == null || string.IsNullOrWhiteSpace(s.AuthToken)) return;

                _authToken = s.AuthToken;
                _lblUserEmail.Text = s.UserEmail ?? "";
                _lblUserEmail.Visible = true;
                TokenBalanceService.Instance.SetAuthContext(s.AuthToken, s.UserEmail ?? "");
                ProActivityLogger.Instance.SetAuthContext(s.AuthToken, s.UserEmail ?? "");
                LogI($"Session restored: {s.UserEmail}");
            }
            catch (Exception ex)
            {
                LogW($"Could not restore session: {ex.Message}");
            }
        }

        private static void SaveSession(string? token, string? refresh, string? email)
        {
            try
            {
                var dir = Path.GetDirectoryName(SessionFile)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(new SessionData
                {
                    AuthToken = token, RefreshToken = refresh,
                    UserEmail = email, SavedAt = DateTime.UtcNow
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SessionFile, json);
            }
            catch { }
        }

        private void ClearSession()
        {
            _authToken = null;
            try { if (File.Exists(SessionFile)) File.Delete(SessionFile); } catch { }
        }

        private async Task RefreshTokensAsync()
        {
            if (!HasSSO) return;
            try { await TokenBalanceService.Instance.RefreshBalanceAsync(); } catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        //  BUILD UI
        // ═══════════════════════════════════════════════════════════════
        private void BuildUI()
        {
            BuildHeader();
            BuildSessionBanner();
            BuildSsoPromptBanner();
            BuildTabs();
            BuildLog();
            BuildStatusBar();
            UpdateTokenDisplay();
        }

        private void BuildHeader()
        {
            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = C.HeaderBg, Padding = new Padding(10, 0, 10, 0) };

            _lblTitle = new Label
            {
                Text = "🔒 PatsKiller Pro v2.0", ForeColor = C.HeaderText,
                Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true, Location = new Point(10, 12)
            };

            var lblBadge = new Label
            {
                Text = "FORD PATS", ForeColor = C.HeaderText, BackColor = C.BtnPrimary,
                Font = new Font("Segoe UI", 8, FontStyle.Bold), Padding = new Padding(6, 2, 6, 2),
                AutoSize = true, Location = new Point(200, 14)
            };

            _lblLicBadge = new Label
            {
                ForeColor = C.HeaderText, BackColor = C.LicBadgeBg,
                Font = new Font("Segoe UI", 7, FontStyle.Bold), Padding = new Padding(5, 2, 5, 2),
                AutoSize = true, Location = new Point(280, 15), Visible = false
            };

            _lblGrace = new Label
            {
                ForeColor = Color.White, BackColor = C.GraceBg,
                Font = new Font("Segoe UI", 7, FontStyle.Bold), Padding = new Padding(5, 2, 5, 2),
                AutoSize = true, Visible = false
            };

            _lblPromoTokens = new Label
            {
                ForeColor = C.PromoText, BackColor = C.PromoBg,
                Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(8, 4, 8, 4),
                AutoSize = true, Visible = false
            };

            _lblPurchaseTokens = new Label
            {
                ForeColor = C.HeaderText, BackColor = C.PurchaseBg,
                Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(8, 4, 8, 4),
                AutoSize = true, Visible = false
            };

            _lblLicStatus = new Label
            {
                ForeColor = Color.FromArgb(186, 230, 253),
                Font = new Font("Segoe UI", 8), AutoSize = true, Visible = false
            };

            _lblUserEmail = new Label
            {
                ForeColor = C.HeaderText, Font = new Font("Segoe UI", 9), AutoSize = true, Visible = false
            };

            _btnAccount = new Button
            {
                Text = "▼", ForeColor = C.HeaderText, BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat, Size = new Size(30, 30)
            };
            _btnAccount.FlatAppearance.BorderSize = 0;

            _headerPanel.Controls.AddRange(new Control[] { _lblTitle, lblBadge, _lblLicBadge, _lblGrace });
            LayoutHeaderRight();
            Controls.Add(_headerPanel);
        }

        private void LayoutHeaderRight()
        {
            int x = _headerPanel.Width - 15;

            _btnAccount.Location = new Point(x - 30, 8);
            if (!_headerPanel.Controls.Contains(_btnAccount)) _headerPanel.Controls.Add(_btnAccount);
            x -= 40;

            void Place(Label lbl, int topY)
            {
                if (!lbl.Visible) return;
                lbl.Location = new Point(x - lbl.PreferredWidth, topY);
                if (!_headerPanel.Controls.Contains(lbl)) _headerPanel.Controls.Add(lbl);
                x -= lbl.PreferredWidth + 8;
            }

            Place(_lblPromoTokens, 10);
            Place(_lblPurchaseTokens, 10);
            Place(_lblGrace, 12);
            Place(_lblLicStatus, 14);
            Place(_lblUserEmail, 14);
        }

        private void BuildSessionBanner()
        {
            _sessionBanner = new Panel { Dock = DockStyle.Top, Height = 35, BackColor = C.SessionBg, Visible = false, Padding = new Padding(10, 5, 10, 5) };
            _lblSessionText = new Label
            {
                Text = "🔓 Gateway Session Active - Key programming is FREE!",
                ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Location = new Point(10, 7)
            };
            _lblSessionTimer = new Label
            {
                Text = "10:00", ForeColor = Color.White, Font = new Font("Consolas", 12, FontStyle.Bold),
                AutoSize = true, Anchor = AnchorStyles.Right, Location = new Point(580, 5)
            };
            _sessionBanner.Controls.AddRange(new Control[] { _lblSessionText, _lblSessionTimer });
            Controls.Add(_sessionBanner);
            _sessionBanner.BringToFront();

            _gatewayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _gatewayTimer.Tick += GatewayTimer_Tick;
        }

        /// <summary>Blue banner shown when licensed-only (no SSO) to encourage token access.</summary>
        private void BuildSsoPromptBanner()
        {
            _ssoPromptBanner = new Panel { Dock = DockStyle.Top, Height = 35, BackColor = C.SsoPromptBg, Visible = false, Padding = new Padding(10, 5, 10, 5) };

            _lblSsoPrompt = new Label
            {
                Text = "🌐 Sign in with Google to use InCode tokens and cloud sync",
                ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), AutoSize = true, Location = new Point(10, 8)
            };

            _btnSsoPromptLogin = new Button
            {
                Text = "Sign In", ForeColor = C.BtnPrimary, BackColor = Color.White,
                FlatStyle = FlatStyle.Flat, Size = new Size(70, 25),
                Font = new Font("Segoe UI", 8, FontStyle.Bold), Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(560, 4)
            };
            _btnSsoPromptLogin.FlatAppearance.BorderSize = 0;
            _btnSsoPromptLogin.Click += BtnSsoPromptLogin_Click;

            _ssoPromptBanner.Controls.AddRange(new Control[] { _lblSsoPrompt, _btnSsoPromptLogin });
            Controls.Add(_ssoPromptBanner);
            _ssoPromptBanner.BringToFront();
        }

        private async void BtnSsoPromptLogin_Click(object? sender, EventArgs e)
        {
            using var loginForm = new GoogleLoginForm();
            var dlg = loginForm.ShowDialog(this);
            if (dlg == DialogResult.OK && !string.IsNullOrWhiteSpace(loginForm.AuthToken))
            {
                _authToken = loginForm.AuthToken;
                SaveSession(loginForm.AuthToken, loginForm.RefreshToken, loginForm.UserEmail);
                TokenBalanceService.Instance.SetAuthContext(loginForm.AuthToken, loginForm.UserEmail ?? "");
                ProActivityLogger.Instance.SetAuthContext(loginForm.AuthToken, loginForm.UserEmail ?? "");
                _lblUserEmail.Text = loginForm.UserEmail ?? "";
                _lblUserEmail.Visible = true;
                _ssoPromptBanner.Visible = false;
                await RefreshTokensAsync();
                UpdateTokenDisplay();
                LogS($"Signed in as {loginForm.UserEmail}");
            }
        }

        // ── Tabs ──
        private void BuildTabs()
        {
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Padding = new Point(15, 5), Visible = false
            };

            _tabPats    = new TabPage { Text = "🔑 PATS Operations",  BackColor = C.Panel, Padding = new Padding(10) };
            _tabUtility = new TabPage { Text = "🔧 Utility (1 token)", BackColor = C.Panel, Padding = new Padding(10) };
            _tabFree    = new TabPage { Text = "⚙️ Free Functions",    BackColor = C.Panel, Padding = new Padding(10) };

            BuildPatsTab();
            BuildUtilityTab();
            BuildFreeTab();

            _tabControl.TabPages.AddRange(new[] { _tabPats, _tabUtility, _tabFree });
            Controls.Add(_tabControl);
        }

        private void BuildPatsTab()
        {
            var c = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            int y = 10;

            // Device
            var dp = GroupPanel("🔌 J2534 Device", 120); dp.Location = new Point(10, y);
            _cmbDevice = new ComboBox { Location = new Point(15, 30), Size = new Size(350, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbDevice.Items.Add("Select J2534 Device...");
            _btnScan       = Btn("🔍 Scan",       C.BtnPrimary, new Size(80, 30));  _btnScan.Location       = new Point(375, 28);
            _btnConnect    = Btn("Connect",        C.BtnSuccess, new Size(90, 30));  _btnConnect.Location    = new Point(15, 70);  _btnConnect.Enabled = false;
            _btnDisconnect = Btn("Disconnect",     C.BtnDanger,  new Size(90, 30));  _btnDisconnect.Location = new Point(115, 70); _btnDisconnect.Enabled = false;
            _btnReadVehicle= Btn("📖 Read Vehicle",C.BtnPrimary, new Size(130, 30)); _btnReadVehicle.Location= new Point(215, 70); _btnReadVehicle.Enabled = false;
            dp.Controls.AddRange(new Control[] { _cmbDevice, _btnScan, _btnConnect, _btnDisconnect, _btnReadVehicle });
            c.Controls.Add(dp); y += 135;

            // Vehicle Info
            _vehicleInfoPanel = GroupPanel("🚗 Vehicle Information", 100); _vehicleInfoPanel.Location = new Point(10, y); _vehicleInfoPanel.Visible = false;
            _lblVin         = new Label { Location = new Point(15, 30), AutoSize = true, Font = new Font("Consolas", 11, FontStyle.Bold) };
            _lblVehicleDesc = new Label { Location = new Point(15, 55), AutoSize = true };
            _lblOutcode     = new Label { Location = new Point(15, 75), AutoSize = true, Font = new Font("Consolas", 11, FontStyle.Bold), ForeColor = C.BtnPrimary };
            _vehicleInfoPanel.Controls.AddRange(new Control[] { _lblVin, _lblVehicleDesc, _lblOutcode });
            c.Controls.Add(_vehicleInfoPanel); y += 115;

            // Incode
            var ip = GroupPanel("🔐 Security Access", 80); ip.Location = new Point(10, y); ip.Name = "incodePanel"; ip.Visible = false;
            var lblP = new Label { Text = "Enter Incode:", Location = new Point(15, 32), AutoSize = true };
            _txtIncode = new TextBox { Location = new Point(110, 28), Size = new Size(150, 25), Font = new Font("Consolas", 11), CharacterCasing = CharacterCasing.Upper };
            _btnSubmitIncode = Btn("✔ Submit", C.BtnSuccess, new Size(90, 28)); _btnSubmitIncode.Location = new Point(270, 27); _btnSubmitIncode.Enabled = false;
            _lblIncodeStatus = new Label { Location = new Point(370, 32), AutoSize = true };
            ip.Controls.AddRange(new Control[] { lblP, _txtIncode, _btnSubmitIncode, _lblIncodeStatus });
            c.Controls.Add(ip); y += 95;

            // Key Ops
            _keyOpsPanel = GroupPanel("🔑 Key Operations (1 token per session)", 90); _keyOpsPanel.Location = new Point(10, y); _keyOpsPanel.Visible = false;
            _btnEraseKeys   = Btn("🗑️ Erase All Keys", C.BtnDanger,  new Size(150, 35)); _btnEraseKeys.Location   = new Point(15, 35);  _btnEraseKeys.Enabled = false;
            _btnProgramKeys = Btn("🔑 Program Keys",   C.BtnSuccess, new Size(150, 35)); _btnProgramKeys.Location = new Point(180, 35); _btnProgramKeys.Enabled = false;
            var lblKeyTip = new Label { Text = "💡 1 token = unlimited keys in session (same outcode)", Location = new Point(350, 42), AutoSize = true, ForeColor = C.Info };
            _keyOpsPanel.Controls.AddRange(new Control[] { _btnEraseKeys, _btnProgramKeys, lblKeyTip });
            c.Controls.Add(_keyOpsPanel); y += 105;

            // Param Reset
            _paramResetPanel = GroupPanel("🔄 Parameter Reset (1 token per module)", 130); _paramResetPanel.Location = new Point(10, y); _paramResetPanel.Visible = false;
            var lblPD = new Label { Text = "Auto-detects modules: BCM + ABS + PCM (3-4 tokens total)", Location = new Point(15, 28), AutoSize = true, ForeColor = C.Info };
            _chkSkipAbs = new CheckBox { Text = "Skip ABS (2 modules only) - Saves 1 token!", Location = new Point(15, 50), AutoSize = true, ForeColor = C.Success };
            _btnParamReset = Btn("🔄 Start Parameter Reset", C.BtnPrimary, new Size(200, 35)); _btnParamReset.Location = new Point(15, 80); _btnParamReset.Enabled = false;
            _lblParamStatus = new Label { Location = new Point(230, 88), AutoSize = true };
            _paramProgress = new ProgressBar { Location = new Point(15, 115), Size = new Size(430, 10), Visible = false };
            _paramResetPanel.Controls.AddRange(new Control[] { lblPD, _chkSkipAbs, _btnParamReset, _lblParamStatus, _paramProgress });
            c.Controls.Add(_paramResetPanel); y += 145;

            // Gateway
            _gatewayPanel = GroupPanel("🔓 Gateway Unlock (2020+)", 80); _gatewayPanel.Location = new Point(10, y);
            _gatewayPanel.BackColor = Color.FromArgb(254, 243, 199); _gatewayPanel.Visible = false;
            var lblGD = new Label { Text = "✨ Unlock Gateway = FREE key programming for 10 minutes!", Location = new Point(15, 28), AutoSize = true, ForeColor = Color.FromArgb(146, 64, 14) };
            _btnGateway = Btn("🔓 Gateway Unlock (1 token)", C.BtnWarning, new Size(220, 35)); _btnGateway.Location = new Point(15, 50); _btnGateway.Enabled = false;
            _gatewayPanel.Controls.AddRange(new Control[] { lblGD, _btnGateway });
            c.Controls.Add(_gatewayPanel);

            _tabPats.Controls.Add(c);
        }

        private void BuildUtilityTab()
        {
            var c = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10) };
            c.Controls.Add(new Label { Text = "🔧 Utility Operations (1 token each)", Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 10) });

            foreach (var (t, d) in new[] {
                ("Clear Theft Flag", "Theft Detected - Vehicle Immobilized"),
                ("Clear Crash Flag", "Collision/Accident Flag (DID 5B17)"),
                ("Clear Crash Input", "Crash Input Failure"),
                ("BCM Factory Defaults", "Restore BCM config (NOT PATS)")
            })
            {
                var b = UtilBtn(t, d); b.Click += async (_, _) => await ExecUtilityAsync(t); c.Controls.Add(b);
            }
            _tabUtility.Controls.Add(c);
        }

        private void BuildFreeTab()
        {
            var c = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10) };
            c.Controls.Add(new Label { Text = "⚙️ Free Functions (No tokens required)", Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 10) });

            foreach (var (t, d) in new[] {
                ("Read VIN", "Read VIN from vehicle modules"),
                ("Read Key Count", "Count programmed keys"),
                ("Read DTCs", "Read diagnostic trouble codes"),
                ("Clear DTCs", "Clear all DTCs from modules"),
                ("Read Battery", "Check vehicle battery voltage")
            })
                c.Controls.Add(UtilBtn(t, d, true));

            _tabFree.Controls.Add(c);
        }

        private void BuildLog()
        {
            var p = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(5) };
            var lbl = new Label { Text = "📋 Activity Log", Font = new Font("Segoe UI", 9, FontStyle.Bold), AutoSize = true, Location = new Point(5, 3) };
            _rtbLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = C.LogBg, ForeColor = C.LogInfo, Font = new Font("Consolas", 9), ReadOnly = true, BorderStyle = BorderStyle.None };
            p.Controls.Add(_rtbLog);
            p.Controls.Add(lbl);
            lbl.BringToFront();
            Controls.Add(p);
        }

        private void BuildStatusBar()
        {
            _statusBar = new StatusStrip();
            _tsStatus = new ToolStripStatusLabel { Text = "Ready", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusBar.Items.Add(_tsStatus);
            _statusBar.Items.Add(new ToolStripStatusLabel { Text = "v2.0.0", Alignment = ToolStripItemAlignment.Right });
            Controls.Add(_statusBar);
        }

        // ═══════════════════════════════════════════════════════════════
        //  EVENTS
        // ═══════════════════════════════════════════════════════════════
        private void WireEvents()
        {
            _btnScan.Click          += BtnScan_Click;
            _btnConnect.Click       += BtnConnect_Click;
            _btnDisconnect.Click    += BtnDisconnect_Click;
            _btnReadVehicle.Click   += BtnReadVehicle_Click;
            _cmbDevice.SelectedIndexChanged += (_, _) => _btnConnect.Enabled = _cmbDevice.SelectedIndex > 0;
            _txtIncode.TextChanged  += (_, _) => _btnSubmitIncode.Enabled = _txtIncode.Text.Length >= 4;
            _btnSubmitIncode.Click  += BtnSubmitIncode_Click;
            _btnEraseKeys.Click     += BtnEraseKeys_Click;
            _btnProgramKeys.Click   += BtnProgramKeys_Click;
            _btnParamReset.Click    += BtnParamReset_Click;
            _btnGateway.Click       += BtnGateway_Click;
            _btnAccount.Click       += BtnAccount_Click;
        }

        // ── Token display ──
        private void OnTokenBalanceChanged(object? sender, TokenBalanceChangedEventArgs e)
        {
            if (InvokeRequired) { BeginInvoke(() => OnTokenBalanceChanged(sender, e)); return; }
            UpdateTokenDisplay();
        }

        private void UpdateTokenDisplay()
        {
            if (HasSSO)
            {
                var svc = TokenBalanceService.Instance;
                _lblPurchaseTokens.Text = svc.RegularTokens.ToString();
                _lblPurchaseTokens.Visible = true;
                _lblPromoTokens.Text = svc.PromoTokens > 0 ? $"{svc.PromoTokens} promo" : "";
                _lblPromoTokens.Visible = svc.PromoTokens > 0;
            }
            else
            {
                _lblPurchaseTokens.Visible = false;
                _lblPromoTokens.Visible = false;
            }
            LayoutHeaderRight();
        }

        // ── Device ──
        private void BtnScan_Click(object? s, EventArgs e)
        {
            LogI("Scanning for J2534 devices...");
            _cmbDevice.Items.Clear(); _cmbDevice.Items.Add("Scanning..."); _cmbDevice.SelectedIndex = 0; _btnScan.Enabled = false;
            Task.Delay(1000).ContinueWith(_ => Invoke(() =>
            {
                _cmbDevice.Items.Clear(); _cmbDevice.Items.Add("Select J2534 Device..."); _cmbDevice.Items.Add("VCM II (Ford)"); _cmbDevice.Items.Add("VXDIAG VCX");
                _cmbDevice.SelectedIndex = 0; _btnScan.Enabled = true; LogS("Found 2 devices");
            }));
        }

        private void BtnConnect_Click(object? s, EventArgs e)
        {
            var dev = _cmbDevice.SelectedItem?.ToString() ?? ""; LogI($"Connecting to {dev}...");
            Task.Delay(800).ContinueWith(_ => Invoke(() =>
            {
                _deviceConnected = true;
                _btnConnect.Text = "✔ Connected"; _btnConnect.BackColor = C.Success; _btnConnect.Enabled = false;
                _btnDisconnect.Enabled = true; _cmbDevice.Enabled = false; _btnReadVehicle.Enabled = true;
                LogS($"Connected to {dev}");
                ProActivityLogger.Instance.LogJ2534Connection(dev, true);
                SetStatus("Device ready - Read vehicle to continue");
            }));
        }

        private void BtnDisconnect_Click(object? s, EventArgs e)
        {
            var dev = _cmbDevice.SelectedItem?.ToString() ?? "device";
            TokenBalanceService.Instance.EndKeySession();
            _deviceConnected = false; _vehicleConnected = false; _incodeVerified = false;
            _currentVin = ""; _currentOutcode = "";
            _btnConnect.Text = "Connect"; _btnConnect.BackColor = C.BtnSuccess; _btnConnect.Enabled = true;
            _btnDisconnect.Enabled = false; _cmbDevice.Enabled = true; _btnReadVehicle.Enabled = false;
            _vehicleInfoPanel.Visible = false; FindCtl<Panel>("incodePanel")!.Visible = false;
            _keyOpsPanel.Visible = false; _paramResetPanel.Visible = false; _gatewayPanel.Visible = false;
            LogI($"Disconnected from {dev}");
            ProActivityLogger.Instance.LogJ2534Disconnect(dev);
            SetStatus("Disconnected");
        }

        // ── Vehicle ──
        private async void BtnReadVehicle_Click(object? s, EventArgs e)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LogI("Reading vehicle VIN from CAN bus..."); _btnReadVehicle.Enabled = false;
            await Task.Delay(1500); // TODO: actual read
            _currentVin = "1FA6P8CF5L5123456"; _currentYear = "2020"; _currentModel = "Ford Mustang";
            _currentOutcode = "BCM-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            _is2020Plus = int.Parse(_currentYear) >= 2020;
            LogI($"VIN: {_currentVin}"); LogI($"Vehicle: {_currentYear} {_currentModel}"); LogS($"Outcode: {_currentOutcode}");
            _vehicleConnected = true;
            _lblVin.Text = $"VIN: {_currentVin}"; _lblVehicleDesc.Text = $"{_currentYear} {_currentModel}"; _lblOutcode.Text = $"Outcode: {_currentOutcode}";
            _vehicleInfoPanel.Visible = true; FindCtl<Panel>("incodePanel")!.Visible = true;
            if (_is2020Plus) { _gatewayPanel.Visible = true; _btnGateway.Enabled = true; }
            ProActivityLogger.Instance.LogVehicleDetection(_currentVin, _currentYear, _currentModel, true, (int)sw.ElapsedMilliseconds);
            SetStatus("Vehicle detected - Enter incode to continue");
        }

        // ── Incode ──
        private async void BtnSubmitIncode_Click(object? s, EventArgs e)
        {
            if (!RequireTokens()) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _currentIncode = _txtIncode.Text.Trim().ToUpper();
            LogI($"Verifying incode: {_currentIncode}..."); _btnSubmitIncode.Enabled = false;
            var r = await TokenBalanceService.Instance.StartKeySessionAsync(_currentOutcode, _currentVin);
            if (!r.Success) { LogE($"Token deduction failed: {r.Error}"); _btnSubmitIncode.Enabled = true; MessageBox.Show(r.Error, "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            await Task.Delay(800);
            _incodeVerified = true; _lblIncodeStatus.Text = "✅ Valid"; _lblIncodeStatus.ForeColor = C.Success;
            LogS("Incode verified - Key operations unlocked!");
            _keyOpsPanel.Visible = true; _paramResetPanel.Visible = true;
            _btnEraseKeys.Enabled = true; _btnProgramKeys.Enabled = true; _btnParamReset.Enabled = true;
            ProActivityLogger.Instance.LogIncodeVerification(_currentVin, _currentYear, _currentModel, _currentOutcode, _currentIncode, true, (int)sw.ElapsedMilliseconds);
            SetStatus("Security access granted - Ready for operations");
        }

        // ── Key Ops ──
        private async void BtnEraseKeys_Click(object? s, EventArgs e)
        {
            if (!RequireTokens()) return;
            if (MessageBox.Show("This will ERASE ALL programmed keys.\nAre you sure?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var sw = System.Diagnostics.Stopwatch.StartNew(); LogI("Erasing all keys..."); _btnEraseKeys.Enabled = false;
            await Task.Delay(2000); LogS("All keys erased!"); _btnEraseKeys.Enabled = true;
            ProActivityLogger.Instance.LogEraseAllKeys(_currentVin, _currentYear, _currentModel, 0, true, 0, (int)sw.ElapsedMilliseconds);
        }

        private async void BtnProgramKeys_Click(object? s, EventArgs e)
        {
            if (!RequireTokens()) return;
            var sw = System.Diagnostics.Stopwatch.StartNew(); LogI("Programming new key..."); _btnProgramKeys.Enabled = false;
            await Task.Delay(2000); LogS("Key programmed!"); _btnProgramKeys.Enabled = true;
            ProActivityLogger.Instance.LogKeyProgrammed(_currentVin, _currentYear, _currentModel, 1, true, (int)sw.ElapsedMilliseconds);
        }

        // ── Param Reset ──
        private async void BtnParamReset_Click(object? s, EventArgs e)
        {
            if (!RequireTokens()) return;
            _skipAbs = _chkSkipAbs.Checked;
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            _paramResetActive = true; _btnParamReset.Enabled = false; _paramResetStep = 0;
            _paramResetModules = _skipAbs
                ? new[] { new ParamResetModule("BCM", "0x726"), new ParamResetModule("PCM", "0x7E0") }
                : new[] { new ParamResetModule("BCM", "0x726"), new ParamResetModule("ABS", "0x760"), new ParamResetModule("PCM", "0x7E0") };
            _paramProgress.Maximum = _paramResetModules.Length; _paramProgress.Value = 0; _paramProgress.Visible = true;

            int tokens = 0; var done = new List<string>();
            foreach (var m in _paramResetModules)
            {
                var mSw = System.Diagnostics.Stopwatch.StartNew();
                _lblParamStatus.Text = $"Resetting {m.Name}..."; _lblParamStatus.ForeColor = C.Info;
                var r = await TokenBalanceService.Instance.DeductTokensAsync(1, "param_reset", _currentVin);
                if (!r.Success) { LogE($"Token fail for {m.Name}: {r.Error}"); _lblParamStatus.Text = $"Failed: {r.Error}"; _lblParamStatus.ForeColor = C.Error; break; }
                tokens++; LogI($"Resetting {m.Name} at {m.Address}..."); await Task.Delay(1500);
                m.Outcode = _currentOutcode; m.Incode = _currentIncode; m.Status = "complete";
                LogS($"✅ {m.Name} reset complete");
                ProActivityLogger.Instance.LogParameterResetModule(_currentVin, _currentYear, _currentModel, m.Name, m.Outcode ?? "", m.Incode ?? "", true, -1, (int)mSw.ElapsedMilliseconds);
                done.Add(m.Name); _paramProgress.Value++; _paramResetStep++;
            }
            _paramResetActive = false; _btnParamReset.Enabled = true; _paramProgress.Visible = false;
            _lblParamStatus.Text = "Complete!"; _lblParamStatus.ForeColor = C.Success;
            LogS($"✅ Parameter Reset COMPLETE - {done.Count} modules, {tokens} tokens");
            ProActivityLogger.Instance.LogParameterResetComplete(_currentVin, _currentYear, _currentModel, done.Count, tokens, (int)totalSw.ElapsedMilliseconds, done.ToArray());
            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        // ── Gateway ──
        private async void BtnGateway_Click(object? s, EventArgs e)
        {
            if (!RequireTokens()) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await TokenBalanceService.Instance.DeductTokensAsync(1, "gateway_unlock", _currentVin);
            if (!r.Success) { LogE($"Gateway failed: {r.Error}"); MessageBox.Show(r.Error, "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            LogI("Unlocking Security Gateway Module..."); _btnGateway.Enabled = false;
            await Task.Delay(2000);
            _gatewaySessionActive = true; _gatewayCountdown = 600;
            LogS("Security Gateway Module unlocked!"); LogS("🎉 Key programming is FREE for 10 minutes!");
            _sessionBanner.Visible = true; _lblSessionTimer.Text = FmtTime(_gatewayCountdown); _gatewayTimer.Start();
            _btnEraseKeys.Enabled = true; _btnProgramKeys.Enabled = true;
            _btnEraseKeys.Text = "🗑️ Erase All Keys (FREE)"; _btnProgramKeys.Text = "🔑 Program Keys (FREE)";
            ProActivityLogger.Instance.LogUtilityOperation("Gateway Unlock", _currentVin, _currentYear, _currentModel, true, -1, (int)sw.ElapsedMilliseconds);
            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        private void GatewayTimer_Tick(object? s, EventArgs e)
        {
            _gatewayCountdown--;
            _lblSessionTimer.Text = FmtTime(_gatewayCountdown);
            if (_gatewayCountdown <= 0)
            {
                _gatewayTimer.Stop(); _gatewaySessionActive = false; _sessionBanner.Visible = false; _btnGateway.Enabled = true;
                LogW("Gateway session expired - key operations now cost tokens");
                _btnEraseKeys.Text = "🗑️ Erase All Keys"; _btnProgramKeys.Text = "🔑 Program Keys";
            }
        }

        // ── Utility ──
        private async Task ExecUtilityAsync(string op)
        {
            if (!RequireTokens()) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await TokenBalanceService.Instance.DeductForUtilityAsync(op, _currentVin);
            if (!r.Success) { LogE($"{op} failed: {r.Error}"); MessageBox.Show(r.Error, "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            LogI($"Executing {op}..."); await Task.Delay(1500); LogS($"{op} complete!");
            ProActivityLogger.Instance.LogUtilityOperation(op, _currentVin, _currentYear, _currentModel, true, -1, (int)sw.ElapsedMilliseconds);
            TokenBalanceService.Instance.RefreshAfterOperation();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ACCOUNT MENU (Phase 2: license + SSO options)
        // ═══════════════════════════════════════════════════════════════
        private void BtnAccount_Click(object? s, EventArgs e)
        {
            var menu = new ContextMenuStrip();

            if (IsLicensed)
            {
                menu.Items.Add(new ToolStripMenuItem($"🔑 Licensed: {LicenseService.Instance.LicensedTo}") { Enabled = false });
                if (LicenseService.Instance.ExpiresAt.HasValue)
                    menu.Items.Add(new ToolStripMenuItem($"   Expires: {LicenseService.Instance.ExpiresAt:yyyy-MM-dd}") { Enabled = false });
            }
            if (HasSSO)
                menu.Items.Add(new ToolStripMenuItem($"🌐 SSO: {TokenBalanceService.Instance.UserEmail}") { Enabled = false });

            menu.Items.Add("-");

            if (!HasSSO)
                menu.Items.Add("Sign in with Google (for tokens)", null, (_, _) => _btnSsoPromptLogin?.PerformClick());

            if (!IsLicensed)
                menu.Items.Add("Enter License Key", null, async (_, _) => await ShowLicenseDialogAsync());

            if (IsLicensed)
                menu.Items.Add("Deactivate License", null, async (_, _) =>
                {
                    if (MessageBox.Show("Deactivate this license?\nYou can re-activate on this or another machine.", "Deactivate", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    var ok = await LicenseService.Instance.DeactivateAsync();
                    if (ok)
                    {
                        LogI("License deactivated");
                        _lblLicBadge.Visible = false; _lblLicStatus.Visible = false; _lblGrace.Visible = false; LayoutHeaderRight();
                        if (!IsAuthorized) { _tabControl.Visible = false; await PromptHybridLoginAsync(); }
                    }
                    else LogE("Failed to deactivate license");
                });

            menu.Items.Add("Buy Tokens", null, (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://patskiller.com/buy", UseShellExecute = true }));
            menu.Items.Add("-");
            menu.Items.Add("Logout", null, (_, _) => Logout());
            menu.Show(_btnAccount, new Point(0, _btnAccount.Height));
        }

        private void Logout()
        {
            ProActivityLogger.Instance.LogLogout(TokenBalanceService.Instance.UserEmail ?? "");
            TokenBalanceService.Instance.ClearAuthContext();
            ProActivityLogger.Instance.ClearAuthContext();
            ClearSession();
            _lblUserEmail.Text = ""; _lblUserEmail.Visible = false;
            _lblPurchaseTokens.Visible = false; _lblPromoTokens.Visible = false;
            LogI("Logged out of SSO");
            if (!IsAuthorized) { _tabControl.Visible = false; _ = PromptHybridLoginAsync(); }
            else { _ssoPromptBanner.Visible = true; LayoutHeaderRight(); }
        }

        /// <summary>Check CanUseTokens; if not, prompt SSO and return false.</summary>
        private bool RequireTokens()
        {
            if (CanUseTokens) return true;
            if (MessageBox.Show("This requires tokens (Google sign-in needed).\nSign in now?", "Sign In Required", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                _btnSsoPromptLogin?.PerformClick();
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        //  FORM CLOSING
        // ═══════════════════════════════════════════════════════════════
        private void MainForm_FormClosing(object? s, FormClosingEventArgs e)
        {
            TokenBalanceService.Instance.BalanceChanged -= OnTokenBalanceChanged;
            LicenseService.Instance.OnLicenseChanged -= OnLicenseChanged;
            LicenseService.Instance.OnLogMessage -= OnLicenseLog;
            TokenBalanceService.Instance.EndKeySession();
            ProActivityLogger.Instance.LogAppClose();
            LicenseService.Instance.Dispose();
            _gatewayTimer?.Stop(); _gatewayTimer?.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════
        private static string FmtTime(int sec) => $"{sec / 60}:{sec % 60:D2}";
        private T? FindCtl<T>(string name) where T : Control { var c = Controls.Find(name, true); return c.Length > 0 ? c[0] as T : null; }
        private void SetStatus(string text) => _tsStatus.Text = text;

        private static Panel GroupPanel(string title, int h)
        {
            var p = new Panel { Size = new Size(560, h), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            p.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 10, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });
            return p;
        }

        private static Button Btn(string text, Color bg, Size sz) => new()
        {
            Text = text, BackColor = bg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Size = sz, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand
        };

        private static Button UtilBtn(string title, string desc, bool free = false) => new()
        {
            Size = new Size(530, 50), BackColor = free ? Color.FromArgb(59, 130, 246) : Color.FromArgb(245, 158, 11),
            ForeColor = Color.White, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10), Margin = new Padding(0, 0, 0, 5),
            Text = $"{title}\n{desc}" + (free ? "" : " (1 token)")
        };

        // ── Logging ──
        private void LogI(string m) => AppendLog(m, C.LogInfo);
        private void LogS(string m) => AppendLog(m, C.LogOk);
        private void LogW(string m) => AppendLog(m, C.LogWarn);
        private void LogE(string m) => AppendLog(m, C.LogErr);

        private void AppendLog(string msg, Color color)
        {
            if (_rtbLog.InvokeRequired) { _rtbLog.Invoke(() => AppendLog(msg, color)); return; }
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            _rtbLog.ScrollToCaret();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HELPER MODELS
    // ═══════════════════════════════════════════════════════════════════
    internal class ParamResetModule
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string Status { get; set; } = "pending";
        public string Outcode { get; set; } = "";
        public string Incode { get; set; } = "";
        public ParamResetModule(string name, string address) { Name = name; Address = address; }
    }

    internal class SessionData
    {
        public string? AuthToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? UserEmail { get; set; }
        public DateTime SavedAt { get; set; }
    }
}
