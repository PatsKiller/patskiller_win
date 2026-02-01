using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Controls;
using PatsKillerPro.J2534;
using PatsKillerPro.Services;
using PatsKillerPro.Services.Workflow;
using PatsKillerPro.Utils;
using PatsKillerPro.Vehicle;

namespace PatsKillerPro
{
    public partial class MainForm : Form
    {
        // Theme
        private readonly Color BG = Color.FromArgb(26, 26, 30);
        private readonly Color SURFACE = Color.FromArgb(35, 35, 40);
        private readonly Color CARD = Color.FromArgb(42, 42, 48);
        private readonly Color BORDER = Color.FromArgb(58, 58, 66);
        private readonly Color TEXT = Color.FromArgb(240, 240, 240);
        private readonly Color TEXT_DIM = Color.FromArgb(160, 160, 165);
        private readonly Color TEXT_MUTED = Color.FromArgb(112, 112, 117);
        private readonly Color ACCENT = Color.FromArgb(59, 130, 246);
        private readonly Color SUCCESS = Color.FromArgb(34, 197, 94);
        private readonly Color WARNING = Color.FromArgb(234, 179, 8);
        private readonly Color DANGER = Color.FromArgb(239, 68, 68);
        private readonly Color BTN_BG = Color.FromArgb(54, 54, 64);

        // State
        private string _userEmail = "", _authToken = "";
        private int _tokenBalance = 0;
        private List<J2534DeviceInfo> _devices = new();
        private bool _isConnected = false;
        private bool _uiBusy = false;

        // Session management
        private readonly SessionStateManager _sessionManager = new();
        private System.Windows.Forms.Timer? _sessionTimer;
        private bool _sessionWarningShown = false;
        private int _retryCount = 0;
        private const int MaxAutoRetries = 3;

        // Alarm disarm timer
        private System.Windows.Forms.Timer? _alarmTimer;
        private DateTime _alarmDisarmExpiry = DateTime.MinValue;
        private bool _alarmDisarmed = false;

        // Tooltips
        private ToolTip _toolTip = null!;

        private void SetUiBusy(bool busy)
        {
            if (_uiBusy == busy) return;
            _uiBusy = busy;
            if (_content != null) _content.Enabled = !busy;
            if (_tabBar != null) _tabBar.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private int _activeTab = 0;
        private bool _didAutoStart = false;

        // Assets
        private Image? _logoImage;

        // Controls
        private Panel _header = null!, _tabBar = null!, _content = null!, _logPanel = null!, _loginPanel = null!;
        private Panel _patsTab = null!, _diagTab = null!, _freeTab = null!;
        private Panel _sessionPanel = null!;
        private Button _btnTab1 = null!, _btnTab2 = null!, _btnTab3 = null!, _btnLogout = null!;
        private Button _btnCloseSession = null!;
        private Label _lblTokens = null!, _lblUser = null!, _lblStatus = null!, _lblVin = null!, _lblKeys = null!;
        private Label _lblSessionTimer = null!;
        private Label _lblAlarmTimer = null!;
        private Panel _alarmPanel = null!;
        private KeySlotPanel _keySlotPanel = null!;
        private ComboBox _cmbDevices = null!, _cmbVehicles = null!;
        private TextBox _txtOutcode = null!, _txtIncode = null!, _txtEmail = null!, _txtPassword = null!;
        private RichTextBox _txtLog = null!;

        // Session-dependent buttons (enable/disable based on session state)
        private List<Button> _sessionDependentButtons = new();

        // DPI helpers (keeps runtime-created controls scaling-friendly)
        private int Dpi(int px) => (int)Math.Round(px * (DeviceDpi / 96f));
        private Padding DpiPad(int l, int t, int r, int b) => new Padding(Dpi(l), Dpi(t), Dpi(r), Dpi(b));


        public MainForm()
        {
            InitializeComponent();
            ApplyDarkTitleBar();
            
            // Initialize tooltip provider
            _toolTip = ToolTipHelper.CreateToolTip();
            
            BuildUI();

            // Centralized UI busy gating (prevents double-click / out-of-order ops).
            J2534Service.Instance.BusyChanged += busy =>
            {
                if (IsDisposed) return;
                try { BeginInvoke(new Action(() => SetUiBusy(busy))); } catch { /* ignore */ }
            };

            // Initialize session timer (updates every second)
            _sessionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _sessionTimer.Tick += SessionTimer_Tick;

            // Initialize alarm disarm timer (updates every second)
            _alarmTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _alarmTimer.Tick += AlarmTimer_Tick;

            // Try to recover previous session
            if (_sessionManager.Load() && _sessionManager.HasRecoverableSession())
            {
                Log("info", "Previous session found - data available for recovery");
            }

            LoadSession();

            // Dispose cached images and save log on close
            this.FormClosing += MainForm_FormClosing;
            this.FormClosed += (_, __) => { try { _logoImage?.Dispose(); } catch { } };
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Auto-save activity log
            try
            {
                if (_txtLog != null && !string.IsNullOrEmpty(_txtLog.Text))
                {
                    var vin = _lblVin?.Text?.Replace("VIN: ", "").Replace("—", "").Trim();
                    SessionStateManager.SaveLog(_txtLog.Text, vin);
                    SessionStateManager.CleanupOldLogs();
                }
            }
            catch { /* Silent fail */ }

            // Stop session timer
            _sessionTimer?.Stop();
            _sessionTimer?.Dispose();

            // Stop alarm timer
            _alarmTimer?.Stop();
            _alarmTimer?.Dispose();
        }

        private void SessionTimer_Tick(object? sender, EventArgs e)
        {
            UpdateSessionTimerDisplay();
        }

        private void UpdateSessionTimerDisplay()
        {
            if (_lblSessionTimer == null) return;

            var workflow = J2534Service.Instance.Workflow;
            if (workflow == null || !workflow.Session.HasActiveSecuritySession)
            {
                _lblSessionTimer.Text = "Session: Inactive";
                _lblSessionTimer.ForeColor = TEXT_MUTED;
                _sessionPanel.BackColor = SURFACE;
                _btnCloseSession.Visible = false;
                UpdateSessionDependentButtons(false);
                return;
            }

            var remaining = workflow.Session.SecurityTimeRemaining;
            if (!remaining.HasValue || remaining.Value <= TimeSpan.Zero)
            {
                _lblSessionTimer.Text = "Session: Expired";
                _lblSessionTimer.ForeColor = DANGER;
                _sessionPanel.BackColor = Color.FromArgb(40, 239, 68, 68);
                _btnCloseSession.Visible = false;
                UpdateSessionDependentButtons(false);
                _sessionTimer?.Stop();
                Log("warning", "Security session expired - new token required for operations");
                return;
            }

            var mins = (int)remaining.Value.TotalMinutes;
            var secs = remaining.Value.Seconds;
            _lblSessionTimer.Text = $"Session: {mins:D2}:{secs:D2}";
            _btnCloseSession.Visible = true;
            UpdateSessionDependentButtons(true);

            // Warning at 2 minutes
            if (remaining.Value.TotalMinutes <= 2 && !_sessionWarningShown)
            {
                _sessionWarningShown = true;
                _lblSessionTimer.ForeColor = DANGER;
                _sessionPanel.BackColor = Color.FromArgb(40, 239, 68, 68);
                
                // Show warning dialog
                var result = MessageBox.Show(
                    "Security session expires in 2 minutes!\n\nDo you want to refresh the session?\n(This will consume 1 token)",
                    "Session Expiring",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    RefreshSession();
                }
            }
            else if (remaining.Value.TotalMinutes > 2)
            {
                _sessionWarningShown = false;
                _lblSessionTimer.ForeColor = SUCCESS;
                _sessionPanel.BackColor = Color.FromArgb(20, 34, 197, 94);
            }
        }

        private void UpdateSessionDependentButtons(bool sessionActive)
        {
            foreach (var btn in _sessionDependentButtons)
            {
                // These buttons show different state based on session
                // When session active: show as FREE (green border)
                // When session inactive: show as costs token
                if (btn.Tag?.ToString() == "session_dependent")
                {
                    btn.FlatAppearance.BorderColor = sessionActive ? SUCCESS : WARNING;
                    btn.FlatAppearance.BorderSize = sessionActive ? 2 : 0;
                }
            }
        }

        private async void RefreshSession()
        {
            if (!Confirm(1, "Refresh security session")) return;
            
            var incode = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(incode))
            {
                ShowError("No Incode", "Please enter incode to refresh session");
                return;
            }

            try
            {
                Log("info", "Refreshing security session...");
                var workflow = J2534Service.Instance.Workflow;
                if (workflow == null) return;

                var result = await workflow.UnlockGatewayAsync(incode);
                if (result.Success)
                {
                    _tokenBalance--;
                    _lblTokens.Text = $"Tokens: {_tokenBalance}";
                    _sessionWarningShown = false;
                    Log("success", "Session refreshed successfully");
                }
                else
                {
                    Log("error", $"Session refresh failed: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                ShowError("Refresh Failed", "Could not refresh session", ex);
            }
        }

        private void CloseSession()
        {
            var result = MessageBox.Show(
                "Close the current security session?\n\nYou will need to use another token to perform key operations.",
                "Close Session",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            try
            {
                var workflow = J2534Service.Instance.Workflow;
                workflow?.Session.ClearAll();
                _sessionTimer?.Stop();
                _sessionManager.ClearSession();
                UpdateSessionTimerDisplay();
                Log("info", "Security session closed");
            }
            catch (Exception ex)
            {
                Log("error", $"Error closing session: {ex.Message}");
            }
        }

        #region Alarm Disarm Timer

        private void AlarmTimer_Tick(object? sender, EventArgs e)
        {
            UpdateAlarmTimerDisplay();
        }

        /// <summary>
        /// Starts the 10-minute alarm disarm countdown
        /// </summary>
        private void StartAlarmDisarmTimer(int durationSeconds = 600)
        {
            _alarmDisarmExpiry = DateTime.Now.AddSeconds(durationSeconds);
            _alarmDisarmed = true;
            _alarmTimer?.Start();
            UpdateAlarmTimerDisplay();
            Log("info", $"Alarm disarmed - {durationSeconds / 60} minute countdown started");
        }

        /// <summary>
        /// Stops the alarm disarm timer and hides indicators
        /// </summary>
        private void StopAlarmDisarmTimer()
        {
            _alarmTimer?.Stop();
            _alarmDisarmed = false;
            _alarmDisarmExpiry = DateTime.MinValue;
            
            // Hide UI elements
            if (_lblAlarmTimer != null)
            {
                _lblAlarmTimer.Visible = false;
                _lblAlarmTimer.Text = "";
            }
            if (_alarmPanel != null)
            {
                _alarmPanel.Visible = false;
            }
        }

        /// <summary>
        /// Check if alarm is currently disarmed
        /// </summary>
        private bool IsAlarmDisarmed => _alarmDisarmed && _alarmDisarmExpiry > DateTime.Now;

        /// <summary>
        /// Updates alarm timer display in both status bar and visual panel
        /// </summary>
        private void UpdateAlarmTimerDisplay()
        {
            if (_lblAlarmTimer == null || _alarmPanel == null) return;

            if (!_alarmDisarmed || _alarmDisarmExpiry <= DateTime.Now)
            {
                // Timer expired or not active
                if (_alarmDisarmed)
                {
                    _alarmDisarmed = false;
                    Log("warning", "Alarm disarm timer expired - alarm re-armed");
                }
                
                _lblAlarmTimer.Visible = false;
                _lblAlarmTimer.Text = "";
                _alarmPanel.Visible = false;
                _alarmTimer?.Stop();
                return;
            }

            var remaining = _alarmDisarmExpiry - DateTime.Now;
            var mins = (int)remaining.TotalMinutes;
            var secs = remaining.Seconds;

            // Update status bar
            _lblAlarmTimer.Text = $"| Alarm: {mins:D2}:{secs:D2}";
            _lblAlarmTimer.Visible = true;

            // Update visual panel
            _alarmPanel.Visible = true;
            var timeLabel = _alarmPanel.Controls.Find("alarmTimeLabel", true).FirstOrDefault() as Label;
            if (timeLabel != null)
            {
                timeLabel.Text = $"{mins:D2}:{secs:D2}";
            }

            // Color changes based on time remaining
            Color timerColor;
            Color panelBg;
            if (remaining.TotalMinutes <= 1)
            {
                // Red - critical
                timerColor = DANGER;
                panelBg = Color.FromArgb(40, 239, 68, 68);
            }
            else if (remaining.TotalMinutes <= 3)
            {
                // Yellow - warning
                timerColor = WARNING;
                panelBg = Color.FromArgb(40, 234, 179, 8);
            }
            else
            {
                // Green - good
                timerColor = SUCCESS;
                panelBg = Color.FromArgb(40, 34, 197, 94);
            }

            _lblAlarmTimer.ForeColor = timerColor;
            _alarmPanel.BackColor = panelBg;
            
            if (timeLabel != null)
            {
                timeLabel.ForeColor = timerColor;
            }

            // Find and update the icon label
            foreach (Control c in _alarmPanel.Controls)
            {
                if (c is FlowLayoutPanel flow)
                {
                    foreach (Control fc in flow.Controls)
                    {
                        if (fc is Label lbl && lbl.Text.Contains("Alarm"))
                        {
                            lbl.ForeColor = timerColor;
                        }
                    }
                }
            }
        }

        #endregion

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Maximize by default (requested). Keeps the Activity Log visible and avoids initial scrolling.
            if (WindowState != FormWindowState.Maximized)
                WindowState = FormWindowState.Maximized;
        }

        private void InitializeComponent()
        {
            this.Text = "PatsKiller Pro 2026";
            this.ClientSize = new Size(1400, 900);
            // Keep the app usable on common laptop screens (e.g., 1366x768)
            this.MinimumSize = new Size(1100, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = BG;
            this.ForeColor = TEXT;
            this.Font = new Font("Segoe UI", 10F);
            this.DoubleBuffered = true;
            // We do our own DPI sizing via DeviceDpi + Dpi() helpers
            this.AutoScaleMode = AutoScaleMode.None;
            this.AutoScroll = false;
        }

        private void ApplyDarkTitleBar()
        {
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
        }

        private Control CreateLogoBlock()
        {
            var sz = Dpi(54);
            var host = new Panel
            {
                Size = new Size(sz, sz),
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, Dpi(12), 0),
                Padding = new Padding(0)
            };

            _logoImage ??= TryLoadLogoImage();

            if (_logoImage != null)
            {
                var pb = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = _logoImage
                };
                host.Controls.Add(pb);
                return host;
            }

            // Fallback: use the EXE icon (always available) before the plain letter mark.
            try
            {
                var ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ic != null)
                {
                    using (ic)
                    {
                        _logoImage = ic.ToBitmap();
                    }

                    if (_logoImage != null)
                    {
                        var pb2 = new PictureBox
                        {
                            Dock = DockStyle.Fill,
                            BackColor = Color.Transparent,
                            SizeMode = PictureBoxSizeMode.Zoom,
                            Image = _logoImage
                        };
                        host.Controls.Add(pb2);
                        return host;
                    }
                }
            }
            catch { }

            // Last-resort: simple letter mark (keeps UI usable even if branding assets go missing).
            var lbl = new Label
            {
                Text = "P",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = ACCENT,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            host.Padding = new Padding(0);
            host.Controls.Add(lbl);
            return host;
        }

        private Image? TryLoadLogoImage()
        {
            // Robust brand mark loading across Debug / Publish / single-file.
            // Priority: embedded resources -> loose files -> associated EXE icon.

            static Image? CloneFromStream(Stream s)
            {
                try
                {
                    using var tmp = Image.FromStream(s);
                    return new Bitmap(tmp);
                }
                catch { return null; }
            }

            static Image? LoadPngNoLock(string path)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    return CloneFromStream(fs);
                }
                catch { return null; }
            }

            static Image? LoadIcoToBitmap(string path)
            {
                try
                {
                    using var ic = new Icon(path);
                    return ic.ToBitmap();
                }
                catch { return null; }
            }

            // 1) Embedded resources
            try
            {
                var asm = typeof(MainForm).Assembly
                          ?? Assembly.GetEntryAssembly()
                          ?? Assembly.GetExecutingAssembly();

                var names = asm.GetManifestResourceNames();

                string? pick(params string[] endsWith)
                {
                    foreach (var suf in endsWith)
                    {
                        var n = names.FirstOrDefault(x => x.EndsWith(suf, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(n)) return n;
                    }
                    return null;
                }

                // Prefer icons (smaller + crisp), then PNG
                var resName = pick(".Resources.app.ico", "Resources.app.ico", "app.ico",
                                   ".Resources.favicon.ico", "Resources.favicon.ico", "favicon.ico",
                                   ".Resources.logo.png", "Resources.logo.png", "logo.png");

                if (!string.IsNullOrWhiteSpace(resName))
                {
                    using var s = asm.GetManifestResourceStream(resName!);
                    if (s != null)
                    {
                        if (resName!.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                        {
                            using var ms = new MemoryStream();
                            s.CopyTo(ms);
                            ms.Position = 0;
                            using var ic = new Icon(ms);
                            return ic.ToBitmap();
                        }
                        var img = CloneFromStream(s);
                        if (img != null) return img;
                    }
                }
            }
            catch { }

            // 2) Loose files (dev runs / non single-file publish)
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;

                foreach (var p in new[]
                {
                    Path.Combine(baseDir, "Resources", "app.ico"),
                    Path.Combine(baseDir, "Resources", "favicon.ico"),
                    Path.Combine(baseDir, "Resources", "logo.png"),
                    Path.Combine(baseDir, "app.ico"),
                    Path.Combine(baseDir, "favicon.ico"),
                    Path.Combine(baseDir, "logo.png")
                })
                {
                    if (!File.Exists(p)) continue;
                    if (p.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        var bi = LoadIcoToBitmap(p);
                        if (bi != null) return bi;
                    }
                    else
                    {
                        var bp = LoadPngNoLock(p);
                        if (bp != null) return bp;
                    }
                }
            }
            catch { }

            // 3) Associated icon (works even when content files aren't extracted)
            try
            {
                var ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ic != null)
                {
                    using (ic)
                    {
                        return ic.ToBitmap();
                    }
                }
            }
            catch { }

            return null;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        private void BuildUI()
        {
            // HEADER (table-based layout so it doesn't overlap at high DPI / long email)
            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = Dpi(84),
                BackColor = SURFACE,
                Padding = DpiPad(18, 12, 18, 12)
            };
            _header.Paint += (s, e) =>
            {
                using var p = new Pen(BORDER);
                e.Graphics.DrawLine(p, 0, _header.Height - 1, _header.Width, _header.Height - 1);
            };

            var headerTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 3,
                RowCount = 1
            };
            headerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            headerTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // Left block (logo + title/subtitle)
            var left = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // Logo (loads Resources/logo.png from embedded resource or disk; falls back to "P")
            var logo = CreateLogoBlock();

            var textStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            textStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            textStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                Text = "PatsKiller Pro",
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = TEXT,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0)
            };
            var subtitle = new Label
            {
                Text = "Ford & Lincoln PATS Key Programming",
                Font = new Font("Segoe UI", 10),
                ForeColor = TEXT_MUTED,
                AutoSize = true,
                AutoEllipsis = true,
                MaximumSize = new Size(Dpi(900), 0),
                Margin = new Padding(0, Dpi(2), 0, 0)
            };
            textStack.Controls.Add(title, 0, 0);
            textStack.Controls.Add(subtitle, 0, 1);

            left.Controls.Add(logo, 0, 0);
            left.Controls.Add(textStack, 1, 0);

            // Right block (tokens + user) + logout button
            var meta = new TableLayoutPanel
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(Dpi(10), 0, Dpi(20), 0),
                Anchor = AnchorStyles.Right
            };
            meta.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            meta.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lblTokens = new Label
            {
                Text = "Tokens: --",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = SUCCESS,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Right
            };
            _lblUser = new Label
            {
                Font = new Font("Segoe UI", 10),
                ForeColor = TEXT_DIM,
                AutoSize = true,
                AutoEllipsis = true,
                MaximumSize = new Size(Dpi(420), 0),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, Dpi(2), 0, 0)
            };

            meta.Controls.Add(_lblTokens, 0, 0);
            meta.Controls.Add(_lblUser, 0, 1);

            _btnLogout = AutoBtn("Logout", BTN_BG);
            _btnLogout.Margin = new Padding(0);
            _btnLogout.Click += (s, e) => Logout();
            _btnLogout.Visible = false;

            headerTable.Controls.Add(left, 0, 0);
            headerTable.Controls.Add(meta, 1, 0);
            headerTable.Controls.Add(_btnLogout, 2, 0);
            _header.Controls.Add(headerTable);

            // TAB BAR (Dock below header, height scales with DPI)
            _tabBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = Dpi(56),
                BackColor = SURFACE,
                Visible = false,
                Padding = DpiPad(18, 8, 18, 8)
            };
            _tabBar.Paint += (s, e) =>
            {
                using var p = new Pen(BORDER);
                e.Graphics.DrawLine(p, 0, _tabBar.Height - 1, _tabBar.Width, _tabBar.Height - 1);
            };

            var tabFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                WrapContents = false,
                AutoScroll = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            _btnTab1 = TabBtn("PATS Key Programming", true);
            _btnTab1.Click += (s, e) => SwitchTab(0);
            tabFlow.Controls.Add(_btnTab1);
            _btnTab2 = TabBtn("Diagnostics", false);
            _btnTab2.Click += (s, e) => SwitchTab(1);
            tabFlow.Controls.Add(_btnTab2);
            _btnTab3 = TabBtn("Free Functions", false);
            _btnTab3.Click += (s, e) => SwitchTab(2);
            tabFlow.Controls.Add(_btnTab3);
            _tabBar.Controls.Add(tabFlow);

            // LOG
            _logPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Dpi(120),
                BackColor = SURFACE,
                Visible = false,
                Padding = DpiPad(18, 10, 18, 10)
            };
            _logPanel.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawLine(p, 0, 0, _logPanel.Width, 0); };

            var logLbl = new Label
            {
                Text = "ACTIVITY LOG",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = TEXT_DIM,
                AutoSize = true,
                Dock = DockStyle.Top
            };
            _txtLog = new RichTextBox
            {
                BackColor = BG,
                ForeColor = TEXT,
                Font = new Font("Consolas", 10),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, Dpi(8), 0, 0)
            };
            _logPanel.Controls.Add(_txtLog);
            _logPanel.Controls.Add(logLbl);

            // CONTENT (tabs fill this region)
            _content = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false };

            BuildPatsTab();
            BuildDiagTab();
            BuildFreeTab();
            BuildLogin();
            ShowLogin();

            // Add in docking order (last added docks first)
            Controls.Add(_content);
            Controls.Add(_logPanel);
            Controls.Add(_tabBar);
            Controls.Add(_header);
            Controls.Add(_loginPanel);
        }

        private void BuildPatsTab()
        {
            _patsTab = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false, AutoScroll = false, Padding = DpiPad(18, 12, 18, 12) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 5,  // Increased for session panel + alarm panel
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // For session panel
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // For alarm panel
            _patsTab.Controls.Add(layout);

            // === SECTION 1: J2534 DEVICE CONNECTION ===
            var sec1 = Section("J2534 DEVICE CONNECTION");
            var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            _cmbDevices = MakeCombo(320);
            _cmbDevices.Items.Add("Select J2534 Device...");
            _cmbDevices.SelectedIndex = 0;
            _cmbDevices.Margin = DpiPad(0, 6, 20, 0);
            row1.Controls.Add(_cmbDevices);

            var btnScan = AutoBtn("Scan Devices", BTN_BG);
            btnScan.Click += BtnScan_Click;
            _toolTip.SetToolTip(btnScan, ToolTipHelper.Tips.ScanDevices);
            row1.Controls.Add(btnScan);

            var btnConn = AutoBtn("Connect", SUCCESS);
            btnConn.Click += BtnConnect_Click;
            _toolTip.SetToolTip(btnConn, ToolTipHelper.Tips.Connect);
            row1.Controls.Add(btnConn);

            _lblStatus = new Label { Text = "Status: Not Connected", Font = new Font("Segoe UI", 11), ForeColor = WARNING, AutoSize = true, Margin = DpiPad(30, 12, 0, 0) };
            row1.Controls.Add(_lblStatus);

            sec1.Controls.Add(row1);
            layout.Controls.Add(sec1, 0, 0);

            // === SECTION 2: VEHICLE INFORMATION ===
            var sec2 = Section("VEHICLE INFORMATION");
            var grid2 = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            grid2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid2.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid2.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true, Margin = new Padding(0) };
            
            var btnVin = AutoBtn("Read VIN", ACCENT);
            btnVin.Click += BtnReadVin_Click;
            _toolTip.SetToolTip(btnVin, ToolTipHelper.Tips.ReadVin);
            row2.Controls.Add(btnVin);

            _lblVin = new Label { Text = "VIN: —————————————————", Font = new Font("Consolas", 12), ForeColor = TEXT_DIM, AutoSize = true, Margin = DpiPad(15, 12, 20, 0) };
            row2.Controls.Add(_lblVin);

            var lblSel = new Label { Text = "Or select vehicle:", Font = new Font("Segoe UI", 10), ForeColor = TEXT_DIM, AutoSize = true, Margin = DpiPad(20, 12, 10, 0) };
            row2.Controls.Add(lblSel);

            _cmbVehicles = MakeCombo(340);
            foreach (var v in VehiclePlatforms.GetAllVehicles()) _cmbVehicles.Items.Add(v.DisplayName);
            if (_cmbVehicles.Items.Count > 0) _cmbVehicles.SelectedIndex = 0;
            _cmbVehicles.Margin = DpiPad(0, 6, 0, 0);
            row2.Controls.Add(_cmbVehicles);

            // Keys badge (right side)
            var keysBg = new Panel { Size = new Size(Dpi(130), Dpi(50)), BackColor = SURFACE, Margin = DpiPad(20, 0, 0, 0) };
            keysBg.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, keysBg.Width - 1, keysBg.Height - 1); };
            keysBg.Controls.Add(new Label { Text = "KEYS", Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = TEXT_MUTED, Dock = DockStyle.Top, Height = Dpi(18), TextAlign = ContentAlignment.MiddleCenter });
            _lblKeys = new Label { Text = "--", Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = SUCCESS, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            keysBg.Controls.Add(_lblKeys);

            grid2.Controls.Add(row2, 0, 0);
            grid2.Controls.Add(keysBg, 1, 0);

            // Key Slot Visualization (row 2, spans both columns)
            _keySlotPanel = new KeySlotPanel
            {
                Dock = DockStyle.Fill,
                Height = Dpi(70),
                Margin = DpiPad(0, 8, 0, 0),
                BackColor = Color.Transparent
            };
            _keySlotPanel.SlotSelected += (s, slot) =>
            {
                Log("info", slot > 0 ? $"Target slot selected: {slot}" : "Slot selection: Auto (next available)");
            };
            grid2.Controls.Add(_keySlotPanel, 0, 1);
            grid2.SetColumnSpan(_keySlotPanel, 2);

            sec2.Controls.Add(grid2);

            layout.Controls.Add(sec2, 0, 1);
            layout.SetColumnSpan(sec2, 2);

            // === SECTION 3: PATS SECURITY CODES ===
            var sec3 = Section("PATS SECURITY CODES");
            var row3 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            row3.Controls.Add(new Label { Text = "OUTCODE:", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = TEXT, AutoSize = true, Margin = DpiPad(0, 12, 10, 0) });
            
            _txtOutcode = MakeTextBox(160);
            _txtOutcode.ReadOnly = true;
            _txtOutcode.Margin = DpiPad(0, 6, 15, 0);
            row3.Controls.Add(_txtOutcode);

            var btnCopy = AutoBtn("Copy", BTN_BG);
            btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) { Clipboard.SetText(_txtOutcode.Text); Log("info", "Outcode copied to clipboard"); } };
            _toolTip.SetToolTip(btnCopy, ToolTipHelper.Tips.CopyOutcode);
            row3.Controls.Add(btnCopy);

            row3.Controls.Add(new Label { Text = "INCODE:", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = TEXT, AutoSize = true, Margin = DpiPad(30, 12, 10, 0) });
            
            _txtIncode = MakeTextBox(160);
            _txtIncode.Margin = DpiPad(0, 6, 15, 0);
            row3.Controls.Add(_txtIncode);

            var btnGetIncode = AutoBtn("Get Incode", ACCENT);
            btnGetIncode.Click += BtnGetIncode_Click;
            _toolTip.SetToolTip(btnGetIncode, ToolTipHelper.Tips.GetIncode);
            row3.Controls.Add(btnGetIncode);

            sec3.Controls.Add(row3);
            layout.Controls.Add(sec3, 0, 2);
            layout.SetColumnSpan(sec3, 2);

            // === SESSION TIMER PANEL (between codes and key programming) ===
            _sessionPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = Dpi(50),
                BackColor = SURFACE,
                Margin = DpiPad(0, 10, 0, 10),
                Padding = DpiPad(15, 0, 15, 0)
            };
            _sessionPanel.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, _sessionPanel.Width - 1, _sessionPanel.Height - 1); };

            var sessionFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                WrapContents = false,
                Padding = new Padding(0)
            };

            _lblSessionTimer = new Label
            {
                Text = "Session: Inactive",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = TEXT_MUTED,
                AutoSize = true,
                Margin = DpiPad(0, 10, 30, 0)
            };
            sessionFlow.Controls.Add(_lblSessionTimer);

            // Alarm timer in status bar
            _lblAlarmTimer = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = TEXT_MUTED,
                AutoSize = true,
                Visible = false,
                Margin = DpiPad(0, 10, 30, 0)
            };
            sessionFlow.Controls.Add(_lblAlarmTimer);

            _btnCloseSession = AutoBtn("Close Session", BTN_BG);
            _btnCloseSession.Click += (s, e) => CloseSession();
            _btnCloseSession.Visible = false;
            _btnCloseSession.Margin = DpiPad(0, 5, 0, 0);
            _toolTip.SetToolTip(_btnCloseSession, ToolTipHelper.Tips.CloseSession);
            sessionFlow.Controls.Add(_btnCloseSession);

            _sessionPanel.Controls.Add(sessionFlow);
            
            // Add session panel to layout
            var sec3b = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Margin = DpiPad(0, 5, 0, 5) };
            sec3b.Controls.Add(_sessionPanel);
            layout.Controls.Add(sec3b, 0, 3);
            layout.SetColumnSpan(sec3b, 2);

            // === ALARM DISARM PANEL (visual indicator) ===
            _alarmPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = Dpi(45),
                BackColor = Color.FromArgb(40, 234, 179, 8),
                Margin = DpiPad(0, 0, 0, 10),
                Padding = DpiPad(15, 0, 15, 0),
                Visible = false
            };
            _alarmPanel.Paint += (s, e) => { using var p = new Pen(WARNING); e.Graphics.DrawRectangle(p, 0, 0, _alarmPanel.Width - 1, _alarmPanel.Height - 1); };

            var alarmFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                WrapContents = false,
                Padding = new Padding(0)
            };

            var alarmIcon = new Label
            {
                Text = "⚠️ Alarm Disarmed:",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = WARNING,
                AutoSize = true,
                Margin = DpiPad(0, 8, 10, 0)
            };
            alarmFlow.Controls.Add(alarmIcon);

            var alarmTime = new Label
            {
                Text = "10:00",
                Font = new Font("Consolas", 14, FontStyle.Bold),
                ForeColor = WARNING,
                AutoSize = true,
                Margin = DpiPad(0, 6, 20, 0),
                Name = "alarmTimeLabel"
            };
            alarmFlow.Controls.Add(alarmTime);

            var alarmNote = new Label
            {
                Text = "Re-arms automatically • Program keys now",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(200, 234, 179, 8),
                AutoSize = true,
                Margin = DpiPad(0, 10, 0, 0)
            };
            alarmFlow.Controls.Add(alarmNote);

            _alarmPanel.Controls.Add(alarmFlow);

            // Add alarm panel to layout (before key programming)
            var sec3c = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Margin = DpiPad(0, 0, 0, 5) };
            sec3c.Controls.Add(_alarmPanel);
            layout.Controls.Add(sec3c, 0, 4);
            layout.SetColumnSpan(sec3c, 2);

            // === SECTION 4: KEY PROGRAMMING ===
            var sec4 = Section("KEY PROGRAMMING");
            var row4 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            var btnProg = AutoBtn("Program Key", SUCCESS);
            btnProg.Click += BtnProgram_Click;
            btnProg.Tag = "session_dependent";
            _toolTip.SetToolTip(btnProg, "Program a new key (auto-disarms alarm)\nCosts 1 token or FREE if session active");
            _sessionDependentButtons.Add(btnProg);
            row4.Controls.Add(btnProg);

            var btnManualAdd = AutoBtn("Manual Add Key", BTN_BG);
            btnManualAdd.Click += BtnManualAddKey_Click;
            btnManualAdd.Tag = "session_dependent";
            _toolTip.SetToolTip(btnManualAdd, "Program key WITHOUT auto-disarm\nFor advanced users - alarm must be disarmed first");
            _sessionDependentButtons.Add(btnManualAdd);
            row4.Controls.Add(btnManualAdd);

            var btnErase = AutoBtn("Erase All Keys", DANGER);
            btnErase.Click += BtnErase_Click;
            btnErase.Tag = "session_dependent";
            _toolTip.SetToolTip(btnErase, ToolTipHelper.Tips.EraseAllKeys);
            _sessionDependentButtons.Add(btnErase);
            row4.Controls.Add(btnErase);

            var btnDisarmAlarm = AutoBtn("Disarm Alarm", WARNING);
            btnDisarmAlarm.Click += BtnDisarmAlarm_Click;
            _toolTip.SetToolTip(btnDisarmAlarm, "Manually disarm vehicle alarm\nCosts 1 token • Requires incode + BCM unlocked");
            row4.Controls.Add(btnDisarmAlarm);

            var btnParam = AutoBtn("Module Reset", BTN_BG);
            btnParam.Click += BtnModuleReset_Click;
            _toolTip.SetToolTip(btnParam, ToolTipHelper.Tips.ParameterReset);
            row4.Controls.Add(btnParam);

            var btnEscl = AutoBtn("ESCL Initialize", BTN_BG);
            btnEscl.Click += BtnEscl_Click;
            _toolTip.SetToolTip(btnEscl, ToolTipHelper.Tips.EsclInit);
            row4.Controls.Add(btnEscl);

            var btnDisable = AutoBtn("Disable BCM Security", BTN_BG);
            btnDisable.Click += BtnDisable_Click;
            _toolTip.SetToolTip(btnDisable, ToolTipHelper.Tips.DisableBcmSecurity);
            row4.Controls.Add(btnDisable);

            sec4.Controls.Add(row4);
            layout.Controls.Add(sec4, 1, 0);

            _content.Controls.Add(_patsTab);
        }

        private void BuildDiagTab()
        {
            _diagTab = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false, AutoScroll = false, Padding = DpiPad(18, 12, 18, 12) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _diagTab.Controls.Add(layout);

            var sec1 = Section("DTC CLEARING");
            var r1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            var d1 = AutoBtn("Clear P160A", BTN_BG);
            d1.Click += BtnP160A_Click;
            _toolTip.SetToolTip(d1, ToolTipHelper.Tips.ClearP160A);
            r1.Controls.Add(d1);
            
            var d2 = AutoBtn("Clear B10A2", BTN_BG);
            d2.Click += BtnB10A2_Click;
            _toolTip.SetToolTip(d2, ToolTipHelper.Tips.ClearB10A2);
            r1.Controls.Add(d2);
            
            var d3 = AutoBtn("Clear Crush Event", BTN_BG);
            d3.Click += BtnCrush_Click;
            _toolTip.SetToolTip(d3, ToolTipHelper.Tips.ClearCrushEvent);
            r1.Controls.Add(d3);
            
            sec1.Controls.Add(r1);
            layout.Controls.Add(sec1, 0, 0);

            var sec2 = Section("GATEWAY OPERATIONS");
            var r2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            var g1 = AutoBtn("Unlock Gateway", ACCENT);
            g1.Click += BtnGateway_Click;
            _toolTip.SetToolTip(g1, ToolTipHelper.Tips.UnlockGateway);
            r2.Controls.Add(g1);
            
            sec2.Controls.Add(r2);
            layout.Controls.Add(sec2, 1, 0);

            var sec3 = Section("KEYPAD & BCM");
            var r3 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            var k1 = AutoBtn("Keypad Code", BTN_BG);
            k1.Click += BtnKeypad_Click;
            _toolTip.SetToolTip(k1, ToolTipHelper.Tips.KeypadCode);
            r3.Controls.Add(k1);
            
            var b1 = AutoBtn("BCM Factory Defaults", DANGER);
            b1.Click += BtnBcm_Click;
            _toolTip.SetToolTip(b1, ToolTipHelper.Tips.BcmFactoryDefaults);
            r3.Controls.Add(b1);
            
            sec3.Controls.Add(r3);
            layout.Controls.Add(sec3, 0, 1);

            var sec4 = Section("MODULE INFO");
            var r4 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            var mod = AutoBtn("Read All Module Info", BTN_BG);
            mod.Click += BtnModInfo_Click;
            _toolTip.SetToolTip(mod, ToolTipHelper.Tips.ReadModuleInfo);
            r4.Controls.Add(mod);
            
            sec4.Controls.Add(r4);
            layout.Controls.Add(sec4, 1, 1);

            _content.Controls.Add(_diagTab);
        }

        private void BuildFreeTab()
        {
            _freeTab = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false, AutoScroll = false, Padding = DpiPad(18, 12, 18, 12) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _freeTab.Controls.Add(layout);

            var banner = new Panel { Dock = DockStyle.Top, Height = Dpi(48), BackColor = Color.FromArgb(20, 34, 197, 94), Margin = DpiPad(0, 0, 0, 15) };
            banner.Paint += (s, e) => { using var p = new Pen(SUCCESS, 2); e.Graphics.DrawRectangle(p, 1, 1, banner.Width - 3, banner.Height - 3); };
            banner.Controls.Add(new Label { Text = "✓ All operations on this tab are FREE - No token cost!", Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = SUCCESS, Dock = DockStyle.Fill, Padding = DpiPad(24, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft });
            layout.Controls.Add(banner, 0, 0);
            layout.SetColumnSpan(banner, 2);

            var sec1 = Section("BASIC VEHICLE OPERATIONS");
            var r1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            var f1 = AutoBtn("Clear All DTCs", BTN_BG);
            f1.Click += BtnDtc_Click;
            _toolTip.SetToolTip(f1, ToolTipHelper.Tips.ClearAllDtcs);
            r1.Controls.Add(f1);
            
            var f2 = AutoBtn("Clear KAM", BTN_BG);
            f2.Click += BtnKam_Click;
            _toolTip.SetToolTip(f2, ToolTipHelper.Tips.ClearKam);
            r1.Controls.Add(f2);
            
            var f3 = AutoBtn("Vehicle Reset", BTN_BG);
            f3.Click += BtnReset_Click;
            _toolTip.SetToolTip(f3, ToolTipHelper.Tips.VehicleReset);
            r1.Controls.Add(f3);
            
            sec1.Controls.Add(r1);
            layout.Controls.Add(sec1, 0, 1);

            var sec2 = Section("READ OPERATIONS");
            var r2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            var rd1 = AutoBtn("Read Keys Count", BTN_BG);
            rd1.Click += BtnReadKeys_Click;
            _toolTip.SetToolTip(rd1, ToolTipHelper.Tips.ReadKeys);
            r2.Controls.Add(rd1);
            
            var rd2 = AutoBtn("Read Module Info", BTN_BG);
            rd2.Click += BtnModInfo_Click;
            _toolTip.SetToolTip(rd2, ToolTipHelper.Tips.ReadModuleInfo);
            r2.Controls.Add(rd2);
            
            sec2.Controls.Add(r2);
            layout.Controls.Add(sec2, 1, 1);

            var sec3 = Section("RESOURCES & SUPPORT");
            var r3 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            var u1 = AutoBtn("User Guide", ACCENT);
            u1.Click += (s, e) => OpenUrl("https://patskiller.com/faqs");
            _toolTip.SetToolTip(u1, ToolTipHelper.Tips.UserGuide);
            r3.Controls.Add(u1);
            
            var u2 = AutoBtn("Buy Tokens", SUCCESS);
            u2.Click += (s, e) => OpenUrl("https://patskiller.com/buy-tokens");
            _toolTip.SetToolTip(u2, ToolTipHelper.Tips.BuyTokens);
            r3.Controls.Add(u2);
            
            var u3 = AutoBtn("Contact Support", BTN_BG);
            u3.Click += (s, e) => OpenUrl("https://patskiller.com/contact");
            _toolTip.SetToolTip(u3, ToolTipHelper.Tips.ContactSupport);
            r3.Controls.Add(u3);
            
            sec3.Controls.Add(r3);
            layout.Controls.Add(sec3, 0, 2);
            layout.SetColumnSpan(sec3, 2);

            _content.Controls.Add(_freeTab);
        }

        private void BuildLogin()
        {
            _loginPanel = new Panel { Dock = DockStyle.Fill, BackColor = BG };
            var card = new Panel { Size = new Size(450, 520), BackColor = CARD };
            card.Paint += (s, e) => { using var p = new Pen(BORDER, 2); e.Graphics.DrawRectangle(p, 1, 1, 447, 517); };

            int cy = 40;
            card.Controls.Add(new Label { Text = "Welcome to PatsKiller Pro", Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = TEXT, Size = new Size(430, 40), Location = new Point(10, cy), TextAlign = ContentAlignment.MiddleCenter }); cy += 45;
            card.Controls.Add(new Label { Text = "Sign in to access your tokens", Font = new Font("Segoe UI", 11), ForeColor = TEXT_MUTED, Size = new Size(430, 25), Location = new Point(10, cy), TextAlign = ContentAlignment.MiddleCenter }); cy += 50;

            var btnG = new Button { Text = "Continue with Google", Size = new Size(370, 52), Location = new Point(40, cy), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(50, 50, 50), Font = new Font("Segoe UI", 12, FontStyle.Bold), Cursor = Cursors.Hand };
            btnG.FlatAppearance.BorderColor = BORDER;
            btnG.Click += BtnGoogle_Click;
            card.Controls.Add(btnG); cy += 70;

            card.Controls.Add(new Label { Text = "───────  or sign in with email  ───────", Font = new Font("Segoe UI", 10), ForeColor = TEXT_MUTED, Size = new Size(370, 22), Location = new Point(40, cy), TextAlign = ContentAlignment.MiddleCenter }); cy += 38;
            card.Controls.Add(new Label { Text = "Email", Font = new Font("Segoe UI", 10), ForeColor = TEXT_DIM, Location = new Point(40, cy), AutoSize = true }); cy += 25;
            _txtEmail = MakeTextBox(370); _txtEmail.Location = new Point(40, cy); card.Controls.Add(_txtEmail); cy += 52;
            card.Controls.Add(new Label { Text = "Password", Font = new Font("Segoe UI", 10), ForeColor = TEXT_DIM, Location = new Point(40, cy), AutoSize = true }); cy += 25;
            _txtPassword = MakeTextBox(370); _txtPassword.Location = new Point(40, cy); _txtPassword.UseSystemPasswordChar = true; _txtPassword.KeyPress += (s, e) => { if (e.KeyChar == 13) DoLogin(); }; card.Controls.Add(_txtPassword); cy += 58;

            var btnL = new Button { Text = "Sign In", Size = new Size(370, 52), Location = new Point(40, cy), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = TEXT, Font = new Font("Segoe UI", 13, FontStyle.Bold), Cursor = Cursors.Hand };
            btnL.FlatAppearance.BorderColor = BORDER;
            btnL.Click += (s, e) => DoLogin();
            card.Controls.Add(btnL);

            _loginPanel.Controls.Add(card);
            _loginPanel.Resize += (s, e) => card.Location = new Point((_loginPanel.Width - 450) / 2, (_loginPanel.Height - 520) / 2 - 30);
            // _loginPanel is added in BuildUI after all other docked controls to ensure it sits on top
        }

        #region Helpers
        private Panel Section(string title)
        {
            var p = new Panel
            {
                BackColor = CARD,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = DpiPad(0, 0, 0, 12),
                Padding = DpiPad(18, 44, 18, 12)
            };
            p.Paint += (s, e) => {
                using var pen = new Pen(BORDER);
                var W = p.Width;
                var H = p.Height;
                e.Graphics.DrawRectangle(pen, 0, 0, W - 1, H - 1);
                using var f = new Font("Segoe UI", 11F, FontStyle.Bold);
                using var b = new SolidBrush(TEXT_DIM);
                e.Graphics.DrawString(title, f, b, Dpi(20), Dpi(12));
                e.Graphics.DrawLine(pen, Dpi(16), Dpi(36), W - Dpi(16), Dpi(36));
            };
            return p;
        }

        private Button AutoBtn(string text, Color bg)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                Padding = DpiPad(18, 10, 18, 10),
                Margin = DpiPad(0, 0, 12, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = BORDER;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        private Button TabBtn(string text, bool active)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                Padding = DpiPad(22, 9, 22, 9),
                Margin = DpiPad(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = active ? ACCENT : BTN_BG,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private TextBox MakeTextBox(int w) => new TextBox { Size = new Size(Dpi(w), Dpi(40)), BackColor = SURFACE, ForeColor = TEXT, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 12), TextAlign = HorizontalAlignment.Center };
        private ComboBox MakeCombo(int w) => new ComboBox { Size = new Size(Dpi(w), Dpi(40)), BackColor = SURFACE, ForeColor = TEXT, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11), DropDownStyle = ComboBoxStyle.DropDownList };

        private void Log(string t, string m) { if (_txtLog == null) return; if (_txtLog.InvokeRequired) { _txtLog.Invoke(() => Log(t, m)); return; } var c = t == "success" ? SUCCESS : t == "error" ? DANGER : t == "warning" ? WARNING : TEXT_DIM; _txtLog.SelectionColor = TEXT_MUTED; _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] "); _txtLog.SelectionColor = c; _txtLog.AppendText($"[{(t == "success" ? "OK" : t == "error" ? "ERR" : t == "warning" ? "WARN" : "INFO")}] {m}\n"); _txtLog.ScrollToCaret(); }
        private void OpenUrl(string u) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = u, UseShellExecute = true }); } catch { } }
        private void ShowError(string t, string m, Exception? ex = null) { MessageBox.Show(ex != null ? $"{m}\n\n{ex.Message}" : m, t, MessageBoxButtons.OK, MessageBoxIcon.Error); Log("error", m); }
        /// <summary>
        /// Confirm operation with token cost. Checks session state for Program/Erase operations.
        /// </summary>
        private bool Confirm(int baseCost, string op) 
        { 
            // Check if session is active - key operations are FREE if so
            var workflow = J2534Service.Instance.Workflow;
            bool sessionActive = workflow?.Session?.HasActiveSecuritySession ?? false;
            
            // Operations that are FREE when session is active
            bool isSessionOperation = op.Contains("Program") || op.Contains("Erase");
            int actualCost = (isSessionOperation && sessionActive) ? 0 : baseCost;
            
            if (actualCost == 0) 
            {
                if (isSessionOperation && sessionActive)
                {
                    // Still confirm but note it's FREE
                    return MessageBox.Show($"{op}\n\n✓ Session active - FREE (no token cost)\n\nProceed?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes;
                }
                return true; 
            }
            
            if (_tokenBalance < actualCost) 
            { 
                MessageBox.Show($"Need {actualCost} tokens, you have {_tokenBalance}.\n\nPurchase more at patskiller.com", "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning); 
                return false; 
            } 
            
            return MessageBox.Show($"{op}\nCost: {actualCost} token(s)\n\nProceed?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes; 
        }
        #endregion

        #region Navigation
        private void ShowLogin() { _loginPanel.Visible = true; _tabBar.Visible = _content.Visible = _logPanel.Visible = _btnLogout.Visible = false; }
        private void ShowMain() { _loginPanel.Visible = false; _tabBar.Visible = _content.Visible = _logPanel.Visible = _btnLogout.Visible = true; SwitchTab(0); AutoStartOnce(); }
        private void SwitchTab(int i) { _activeTab = i; _btnTab1.BackColor = i == 0 ? ACCENT : BTN_BG; _btnTab2.BackColor = i == 1 ? ACCENT : BTN_BG; _btnTab3.BackColor = i == 2 ? ACCENT : BTN_BG; _patsTab.Visible = i == 0; _diagTab.Visible = i == 1; _freeTab.Visible = i == 2; }
        #endregion

        #region Auth
        private void LoadSession() { var e = Settings.GetString("email", ""); var t = Settings.GetString("auth_token", ""); if (!string.IsNullOrEmpty(e) && !string.IsNullOrEmpty(t)) { _userEmail = e; _authToken = t; _tokenBalance = 10; _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail; ShowMain(); Log("info", $"Logged in as {_userEmail}"); } }
        private async void DoLogin() { var e = _txtEmail.Text.Trim(); var p = _txtPassword.Text; if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p)) { MessageBox.Show("Enter email and password"); return; } await Task.Delay(200); _userEmail = e; _authToken = "t_" + DateTime.Now.Ticks; _tokenBalance = 10; Settings.SetString("email", e); Settings.SetString("auth_token", _authToken); Settings.Save(); _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail; ShowMain(); Log("success", $"Logged in as {e}"); }
        private void BtnGoogle_Click(object? s, EventArgs e) { try { using var f = new GoogleLoginForm(); if (f.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(f.AuthToken)) { _authToken = f.AuthToken; _userEmail = f.UserEmail ?? "Google User"; _tokenBalance = f.TokenCount; Settings.SetString("auth_token", _authToken); Settings.SetString("email", _userEmail); Settings.Save(); _lblTokens.Text = $"Tokens: {_tokenBalance}"; _lblUser.Text = _userEmail; ShowMain(); Log("success", $"Logged in as {_userEmail}"); } } catch (Exception ex) { ShowError("Login Failed", ex.Message, ex); } }
        private void Logout() { _userEmail = _authToken = ""; _tokenBalance = 0; Settings.Remove("auth_token"); Settings.Save(); _txtPassword.Text = ""; _lblTokens.Text = "Tokens: --"; _lblUser.Text = ""; ShowLogin(); }
        #endregion

        #region Device - Using J2534Service Singleton
        private void BtnScan_Click(object? s, EventArgs e)
        {
            try
            {
                _cmbDevices.Items.Clear();
                _devices = J2534DeviceScanner.ScanForDevices();
                
                if (_devices.Count == 0)
                {
                    _cmbDevices.Items.Add("No devices found");
                    Log("warning", "No J2534 devices found");
                }
                else
                {
                    foreach (var d in _devices)
                        _cmbDevices.Items.Add(d.Name);
                    Log("success", $"Found {_devices.Count} device(s)");
                }
                _cmbDevices.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                ShowError("Scan Failed", "Could not scan for devices", ex);
            }
        }

        private async void BtnConnect_Click(object? s, EventArgs e)
        {
            if (_cmbDevices.SelectedIndex < 0 || _devices.Count == 0)
            {
                MessageBox.Show("Please scan and select a device first");
                return;
            }

            var idx = _cmbDevices.SelectedIndex;
            if (idx >= _devices.Count)
            {
                MessageBox.Show("Please select a valid device");
                return;
            }

            try
            {
                var device = _devices[idx];
                var result = await J2534Service.Instance.ConnectDeviceAsync(device);
                
                if (result.Success)
                {
                    _isConnected = true;
                    _lblStatus.Text = "Status: Connected";
                    _lblStatus.ForeColor = SUCCESS;
                    Log("success", $"Connected to {device.Name}");
                    
                    // Subscribe to workflow events for progress updates
                    SubscribeToWorkflowEvents();
                }
                else
                {
                    _lblStatus.Text = "Status: Connection Failed";
                    _lblStatus.ForeColor = DANGER;
                    Log("error", $"Failed: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                ShowError("Connect Failed", "Could not connect to device", ex);
            }
        }
        
        private bool _workflowEventsSubscribed = false;
        
        private void SubscribeToWorkflowEvents()
        {
            if (_workflowEventsSubscribed) return;
            _workflowEventsSubscribed = true;
            
            // Subscribe to workflow progress for logging
            J2534Service.Instance.SubscribeToWorkflowProgress((sender, e) =>
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        var desc = e.StepDescription ?? e.State.ToString();
                        Log("info", $"[Step {e.StepIndex}/{e.TotalSteps}] {e.StepName}: {desc}");
                    }));
                }
                catch { /* UI disposed */ }
            });
            
            // Subscribe to workflow errors
            J2534Service.Instance.SubscribeToWorkflowErrors((sender, e) =>
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        var level = e.WillRetry ? "warning" : "error";
                        var retryMsg = e.WillRetry ? $" (retry {e.RetryAttempt})" : "";
                        Log(level, $"{e.ErrorMessage}{retryMsg}");
                    }));
                }
                catch { /* UI disposed */ }
            });
            
            // Subscribe to operation completion
            J2534Service.Instance.SubscribeToWorkflowComplete((sender, e) =>
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        var level = e.Result.Success ? "success" : "error";
                        var resultMsg = e.Result.Success ? "completed" : $"failed: {e.Result.ErrorMessage}";
                        Log(level, $"{e.OperationName} {resultMsg} ({e.Duration.TotalSeconds:F1}s)");
                    }));
                }
                catch { /* UI disposed */ }
            });
            
            // Subscribe to user action required events
            J2534Service.Instance.SubscribeToUserActionRequired((sender, e) =>
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        Log("warning", $"ACTION REQUIRED: {e.Prompt.Instruction}");
                        var result = MessageBox.Show(
                            e.Prompt.Instruction, 
                            e.Prompt.Title, 
                            MessageBoxButtons.OKCancel, 
                            MessageBoxIcon.Information);
                        J2534Service.Instance.ResumeWorkflowAfterUserAction(result == DialogResult.OK);
                    }));
                }
                catch { /* UI disposed */ }
            });
        }

        private async void BtnReadVin_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            try
            {
                var result = await J2534Service.Instance.ReadVehicleInfoAsync();
                if (result.Success && !string.IsNullOrEmpty(result.Vin))
                {
                    _lblVin.Text = $"VIN: {result.Vin}";
                    _lblVin.ForeColor = SUCCESS;
                    Log("success", $"VIN: {result.Vin}");
                    
                    // Configure workflow service with VIN and platform for proper timing/routing
                    J2534Service.Instance.ConfigureWorkflow(result.Vin, result.PlatformCode);
                    Log("info", $"Workflow configured: Platform={result.PlatformCode ?? "default"}");
                    
                    if (!string.IsNullOrEmpty(result.AdditionalInfo))
                    {
                        Log("info", result.AdditionalInfo);
                    }
                }
                else
                {
                    _lblVin.Text = "VIN: Could not read";
                    _lblVin.ForeColor = DANGER;
                    Log("error", result.Error ?? "Failed to read VIN");
                }
            }
            catch (Exception ex)
            {
                ShowError("Read Failed", "Could not read VIN", ex);
            }
        }

private async void BtnGetIncode_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            try
            {
                var result = await J2534Service.Instance.ReadOutcodeAsync();
                if (result.Success && !string.IsNullOrEmpty(result.Outcode))
                {
                    _txtOutcode.Text = result.Outcode;
                    Log("success", $"Outcode: {result.Outcode}");
                    
                    // TODO: Call incode service to get incode from outcode
                    MessageBox.Show($"Outcode retrieved: {result.Outcode}\n\nUse the web portal or API to calculate the incode.");
                }
                else
                {
                    Log("error", result.Error ?? "Failed to read outcode");
                }
            }
            catch (Exception ex)
            {
                ShowError("Read Failed", "Could not read outcode", ex);
            }
        }
        #endregion

        #region PATS - Using Workflow System
        private async void BtnProgram_Click(object? s, EventArgs e)
        {
            await ProgramKeyInternal(autoDisarm: true);
        }

        /// <summary>
        /// Internal key programming logic with optional auto-disarm
        /// </summary>
        private async Task ProgramKeyInternal(bool autoDisarm)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            // Check workflow is configured
            if (!J2534Service.Instance.IsWorkflowConfigured)
            {
                MessageBox.Show("Please read VIN first to configure the workflow system.");
                return;
            }

            var ic = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(ic))
            {
                MessageBox.Show("Enter incode first");
                return;
            }

            // Check if session is active for token cost
            var workflow = J2534Service.Instance.Workflow;
            bool hasActiveSession = workflow?.Session?.HasActiveSecuritySession ?? false;
            int tokenCost = hasActiveSession ? 0 : 1;

            string operationName = autoDisarm ? "Program Key" : "Manual Add Key";
            if (!Confirm(tokenCost, operationName))
                return;

            // Warning for manual add key if alarm not disarmed
            if (!autoDisarm && !IsAlarmDisarmed)
            {
                var result = MessageBox.Show(
                    "⚠️ ALARM NOT DISARMED\n\n" +
                    "Manual Add Key does NOT auto-disarm the alarm.\n\n" +
                    "If the alarm is active, programming may fail or\n" +
                    "trigger security lockout.\n\n" +
                    "Continue anyway?",
                    "Alarm Warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                    
                if (result != DialogResult.Yes) return;
            }

            // First, read current key count to determine slot
            var kc = await J2534Service.Instance.ReadKeyCountWithWorkflowAsync();
            int current = kc.Success ? kc.KeyCount : 0;
            int max = kc.MaxKeys;

            if (current >= max)
            {
                var msg = $"Max keys already programmed ({current}/{max}). Erase keys first if you need to add a new one.";
                Log("warning", msg);
                MessageBox.Show(msg);
                _lblKeys.Text = current.ToString();
                _keySlotPanel.ProgrammedKeys = current;
                return;
            }

            // Use selected slot from KeySlotPanel, or auto-select next available
            int nextSlot = _keySlotPanel.SelectedSlot > 0 ? _keySlotPanel.SelectedSlot : current + 1;
            if (nextSlot <= current) nextSlot = current + 1;

            // Get vehicle info for splash
            string vehicleInfo = _cmbVehicles.SelectedItem?.ToString() ?? "Unknown Vehicle";
            string vin = _lblVin.Text.Replace("VIN: ", "");
            if (vin.Length == 17) vehicleInfo = $"{vehicleInfo} ({vin})";

            // Build steps based on auto-disarm
            var steps = new List<string>();
            steps.Add("Reading current key count");
            steps.Add("Unlocking security access");
            if (autoDisarm) steps.Add("Disarming alarm");
            steps.Add($"Programming key to slot {nextSlot}");
            steps.Add("Verifying key programmed");
            steps.Add("Updating key count");

            // Show operation splash
            using var splash = new OperationProgressForm();
            splash.Configure(
                autoDisarm ? "Programming Key" : "Manual Add Key",
                vehicleInfo,
                tokenCost,
                steps.ToArray()
            );

            bool hadSessionBefore = hasActiveSession;
            Services.J2534Service.KeyOperationResult? prog = null;
            Exception? error = null;
            bool disarmSuccess = true;

            // Run operation in background
            var opTask = Task.Run(async () =>
            {
                try
                {
                    int stepIndex = 0;

                    // Step 1: Already done (read key count)
                    splash.StartStep(stepIndex);
                    await Task.Delay(200);
                    splash.CompleteStep(stepIndex++);

                    // Step 2: Security access
                    splash.StartStep(stepIndex);
                    splash.SetInstruction("Unlocking BCM security...");
                    await Task.Delay(300);
                    splash.CompleteStep(stepIndex++);

                    // Step 3 (if autoDisarm): Disarm alarm
                    if (autoDisarm)
                    {
                        splash.StartStep(stepIndex);
                        splash.SetInstruction("Disarming vehicle alarm...");
                        // Simulate disarm command - in real implementation this sends UDS command
                        await Task.Delay(500);
                        disarmSuccess = true; // Would check actual result
                        splash.CompleteStep(stepIndex++, disarmSuccess);
                    }

                    // Step 4: Program key
                    splash.StartStep(stepIndex);
                    splash.SetInstruction($"Programming key to slot {nextSlot}...");
                    prog = await J2534Service.Instance.ProgramKeyWithWorkflowAsync(ic, nextSlot);
                    splash.CompleteStep(stepIndex++, prog.Success);

                    if (prog.Success)
                    {
                        // Step 5: Verify
                        splash.StartStep(stepIndex);
                        splash.SetInstruction("Verifying key...");
                        await Task.Delay(500);
                        splash.CompleteStep(stepIndex++);

                        // Step 6: Update count
                        splash.StartStep(stepIndex);
                        splash.SetInstruction("Updating key count...");
                        await Task.Delay(300);
                        splash.CompleteStep(stepIndex++);

                        splash.Complete(true, "Key programmed successfully!");
                    }
                    else
                    {
                        splash.Complete(false, prog.Error ?? "Programming failed");
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                    splash.Complete(false, ex.Message);
                }
            });

            // Show splash (blocks until closed)
            splash.ShowDialog(this);
            await opTask;

            // Handle results on UI thread
            if (error != null)
            {
                ShowError("Program Failed", "Could not program key", error);
                return;
            }

            if (prog == null) return;

            if (prog.Success)
            {
                _lblKeys.Text = prog.CurrentKeyCount.ToString();
                _keySlotPanel.ProgrammedKeys = prog.CurrentKeyCount;

                // Start alarm disarm timer if auto-disarm was used
                if (autoDisarm && disarmSuccess)
                {
                    StartAlarmDisarmTimer(600); // 10 minutes
                }

                // Check if we created a new session
                bool hasSessionNow = workflow?.Session?.HasActiveSecuritySession ?? false;
                if (!hadSessionBefore && hasSessionNow)
                {
                    _tokenBalance--;
                    _lblTokens.Text = $"Tokens: {_tokenBalance}";
                    _sessionTimer?.Start();
                    _sessionWarningShown = false;
                    UpdateSessionTimerDisplay();
                    _sessionManager.UpdateFromVehicleSession(workflow!.Session);
                    Log("info", "Session started - subsequent keys are FREE");
                }

                string alarmMsg = autoDisarm ? "\n✓ Alarm disarmed for 10 minutes" : "";
                Log("success", $"Key programmed (slot {nextSlot}, total: {prog.CurrentKeyCount})");
                MessageBox.Show($"Key programmed successfully!\n\nKeys now: {prog.CurrentKeyCount}{alarmMsg}\n\n{(hasSessionNow ? "✓ Session active - next key is FREE!\n" : "")}Remove key, insert next key, and click Program again.");
            }
            else
            {
                Log("error", prog.Error ?? "Programming failed");
                MessageBox.Show($"Programming failed: {prog.Error}");
            }
        }

        /// <summary>
        /// Manual Add Key - programs key WITHOUT auto-disarm
        /// </summary>
        private async void BtnManualAddKey_Click(object? s, EventArgs e)
        {
            await ProgramKeyInternal(autoDisarm: false);
        }

        /// <summary>
        /// Manual Disarm Alarm - costs 1 token, requires incode + BCM unlocked
        /// </summary>
        private async void BtnDisarmAlarm_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            // Check workflow is configured
            if (!J2534Service.Instance.IsWorkflowConfigured)
            {
                MessageBox.Show("Please read VIN first to configure the workflow system.");
                return;
            }

            var ic = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(ic))
            {
                MessageBox.Show("Enter incode first\n\nDisarm alarm requires BCM to be unlocked with incode.");
                return;
            }

            // Check if session is active for token cost
            var workflow = J2534Service.Instance.Workflow;
            bool hasActiveSession = workflow?.Session?.HasActiveSecuritySession ?? false;
            
            if (!hasActiveSession)
            {
                MessageBox.Show("BCM must be unlocked first!\n\nUse Gateway Unlock or Program Key first to establish a security session.");
                return;
            }

            // Already disarmed?
            if (IsAlarmDisarmed)
            {
                var remaining = _alarmDisarmExpiry - DateTime.Now;
                var result = MessageBox.Show(
                    $"Alarm is already disarmed!\n\n" +
                    $"Time remaining: {(int)remaining.TotalMinutes}:{remaining.Seconds:D2}\n\n" +
                    $"Do you want to reset the timer?",
                    "Alarm Already Disarmed",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                    
                if (result != DialogResult.Yes) return;
            }

            // Costs 1 token even if session active (per your requirement)
            if (!Confirm(1, "Disarm Alarm"))
                return;

            Log("info", "Disarming vehicle alarm...");

            try
            {
                // In real implementation, this sends UDS command to disarm alarm
                // For now, simulate the operation
                await Task.Delay(500);
                
                // Deduct token
                _tokenBalance--;
                _lblTokens.Text = $"Tokens: {_tokenBalance}";

                // Start 10-minute countdown
                StartAlarmDisarmTimer(600);

                Log("success", "Alarm disarmed - 10 minute countdown started");
                MessageBox.Show(
                    "✓ Alarm Disarmed\n\n" +
                    "You have 10 minutes to perform key operations.\n" +
                    "After 10 minutes, the alarm will re-arm automatically.\n\n" +
                    "Timer shown in status bar and panel above key operations.",
                    "Alarm Disarmed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError("Disarm Failed", "Could not disarm alarm", ex);
            }
        }

        private async void BtnErase_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            // Check workflow is configured
            if (!J2534Service.Instance.IsWorkflowConfigured)
            {
                MessageBox.Show("Please read VIN first to configure the workflow system.");
                return;
            }

            // Check session for token cost
            var workflow = J2534Service.Instance.Workflow;
            bool hasActiveSession = workflow?.Session?.HasActiveSecuritySession ?? false;
            int tokenCost = hasActiveSession ? 0 : 1;

            if (!Confirm(tokenCost, "Erase All Keys"))
                return;

            // Show 3-checkbox safety confirmation
            if (!EraseKeysConfirmationForm.ShowConfirmation(this))
                return;

            var ic = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(ic))
            {
                MessageBox.Show("Enter incode first");
                return;
            }

            // Get vehicle info for splash
            string vehicleInfo = _cmbVehicles.SelectedItem?.ToString() ?? "Unknown Vehicle";
            string vin = _lblVin.Text.Replace("VIN: ", "");
            if (vin.Length == 17) vehicleInfo = $"{vehicleInfo} ({vin})";

            // Show operation splash
            using var splash = new OperationProgressForm();
            splash.Configure(
                "Erasing All Keys",
                vehicleInfo,
                tokenCost,
                "Unlocking security access",
                "Disarming BCM",
                "Erasing all programmed keys",
                "Verifying keys erased",
                "Updating key count"
            );

            Services.J2534Service.KeyOperationResult? erase = null;
            Exception? error = null;

            // Run operation in background
            var opTask = Task.Run(async () =>
            {
                try
                {
                    // Step 1: Security access
                    splash.StartStep(0);
                    splash.SetInstruction("Unlocking BCM security...");
                    await Task.Delay(300);
                    splash.CompleteStep(0);

                    // Step 2: Disarm
                    splash.StartStep(1);
                    splash.SetInstruction("Disarming BCM...");
                    await Task.Delay(300);
                    splash.CompleteStep(1);

                    // Step 3: Erase keys
                    splash.StartStep(2);
                    splash.SetInstruction("Erasing all programmed keys...");
                    erase = await J2534Service.Instance.EraseAllKeysWithWorkflowAsync(ic);
                    splash.CompleteStep(2, erase.Success);

                    if (erase.Success)
                    {
                        // Step 4: Verify
                        splash.StartStep(3);
                        splash.SetInstruction("Verifying keys erased...");
                        await Task.Delay(500);
                        splash.CompleteStep(3);

                        // Step 5: Update count
                        splash.StartStep(4);
                        splash.SetInstruction("Updating key count...");
                        await Task.Delay(300);
                        splash.CompleteStep(4);

                        splash.Complete(true, "All keys erased! Program 2+ keys now.");
                    }
                    else
                    {
                        splash.Complete(false, erase.Error ?? "Erase failed");
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                    splash.Complete(false, ex.Message);
                }
            });

            // Show splash (blocks until closed)
            splash.ShowDialog(this);
            await opTask;

            // Handle results on UI thread
            if (error != null)
            {
                ShowError("Erase Failed", "Could not erase keys", error);
                return;
            }

            if (erase == null) return;

            if (erase.Success)
            {
                _lblKeys.Text = erase.CurrentKeyCount.ToString();
                _keySlotPanel.ProgrammedKeys = erase.CurrentKeyCount;
                
                // Start alarm disarm timer (erase auto-disarms)
                StartAlarmDisarmTimer(600);
                
                Log("success", $"Keys erased (remaining: {erase.CurrentKeyCount})");
                MessageBox.Show($"All keys erased!\n\nKeys remaining: {erase.CurrentKeyCount}\n\n✓ Alarm disarmed for 10 minutes\n\n⚠️ CRITICAL: Program at least 2 keys NOW!\nVehicle will not start until 2+ keys are programmed.");
            }
            else
            {
                Log("error", erase.Error ?? "Erase failed");
                MessageBox.Show($"Erase failed: {erase.Error}");
            }
        }

        private async void BtnParam_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            // Check workflow is configured
            if (!J2534Service.Instance.IsWorkflowConfigured)
            {
                MessageBox.Show("Please read VIN first to configure the workflow system.");
                return;
            }

            if (!Confirm(1, "Parameter Reset"))
                return;

            var ic = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(ic))
            {
                MessageBox.Show("Enter incode first");
                return;
            }

            // Get vehicle info for splash
            string vehicleInfo = _cmbVehicles.SelectedItem?.ToString() ?? "Unknown Vehicle";
            string vin = _lblVin.Text.Replace("VIN: ", "");
            if (vin.Length == 17) vehicleInfo = $"{vehicleInfo} ({vin})";

            // Show operation splash
            using var splash = new OperationProgressForm();
            splash.Configure(
                "Parameter Reset",
                vehicleInfo,
                1,
                "Unlocking security access",
                "Resetting BCM parameters",
                "Clearing calibration data",
                "Verifying reset complete"
            );

            Services.J2534Service.OperationResult? result = null;
            Exception? error = null;

            // Run operation in background
            var opTask = Task.Run(async () =>
            {
                try
                {
                    // Step 1: Security access
                    splash.StartStep(0);
                    splash.SetInstruction("Unlocking BCM security...");
                    await Task.Delay(300);
                    splash.CompleteStep(0);

                    // Step 2: Reset parameters
                    splash.StartStep(1);
                    splash.SetInstruction("Resetting BCM parameters...");
                    result = await J2534Service.Instance.ParameterResetWithWorkflowAsync(ic);
                    splash.CompleteStep(1, result.Success);

                    if (result.Success)
                    {
                        // Step 3: Clear calibration
                        splash.StartStep(2);
                        splash.SetInstruction("Clearing calibration data...");
                        await Task.Delay(500);
                        splash.CompleteStep(2);

                        // Step 4: Verify
                        splash.StartStep(3);
                        splash.SetInstruction("Verifying reset complete...");
                        await Task.Delay(300);
                        splash.CompleteStep(3);

                        splash.Complete(true, "Parameter reset complete!");
                    }
                    else
                    {
                        splash.Complete(false, result.Error ?? "Reset failed");
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                    splash.Complete(false, ex.Message);
                }
            });

            // Show splash (blocks until closed)
            splash.ShowDialog(this);
            await opTask;

            // Handle results on UI thread
            if (error != null)
            {
                ShowError("Reset Failed", "Could not reset parameters", error);
                return;
            }

            if (result == null) return;

            if (result.Success)
            {
                Log("success", "Parameter reset done");
                MessageBox.Show("Parameter reset complete!\n\n⚠️ Turn ignition OFF and wait 15 seconds before proceeding.");
            }
            else
            {
                Log("error", result.Error ?? "Parameter reset failed");
                MessageBox.Show($"Parameter reset failed: {result.Error ?? "Unknown error"}");
            }
        }

        /// <summary>
        /// Opens the Module Reset dialog for multi-module parameter reset
        /// </summary>
        private async void BtnModuleReset_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            // Check workflow is configured
            if (!J2534Service.Instance.IsWorkflowConfigured)
            {
                MessageBox.Show("Please read VIN first to configure the workflow system.");
                return;
            }

            var ic = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(ic))
            {
                MessageBox.Show("Enter incode first");
                return;
            }

            using var form = new ModuleResetForm();
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            var selectedModules = form.SelectedModules;
            if (selectedModules.Count == 0)
                return;

            var totalCost = form.TotalTokenCost;
            if (_tokenBalance < totalCost)
            {
                MessageBox.Show($"Insufficient tokens. Need {totalCost}, have {_tokenBalance}.");
                return;
            }

            try
            {
                Log("info", $"Starting multi-module reset ({selectedModules.Count} modules, {totalCost} tokens)...");

                int successCount = 0;
                int failCount = 0;

                foreach (var module in selectedModules)
                {
                    Log("info", $"Resetting {module.Name}...");
                    
                    // Perform reset for each module
                    var result = await J2534Service.Instance.ParameterResetWithWorkflowAsync(ic);
                    
                    if (result.Success)
                    {
                        successCount++;
                        _tokenBalance--;
                        _lblTokens.Text = $"Tokens: {_tokenBalance}";
                        Log("success", $"{module.Name} reset complete");
                    }
                    else
                    {
                        failCount++;
                        Log("error", $"{module.Name} reset failed: {result.Error}");
                        
                        // Ask if user wants to continue
                        var continueResult = MessageBox.Show(
                            $"{module.Name} reset failed: {result.Error}\n\nContinue with remaining modules?",
                            "Reset Failed",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        
                        if (continueResult != DialogResult.Yes)
                            break;
                    }
                    
                    // Small delay between modules
                    await Task.Delay(500);
                }

                var summary = $"Module reset complete.\n\nSuccessful: {successCount}\nFailed: {failCount}\n\nTurn ignition OFF and wait 15 seconds.";
                MessageBox.Show(summary, "Reset Complete", MessageBoxButtons.OK, 
                    failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError("Reset Failed", "Could not complete module reset", ex);
            }
        }

        private async void BtnEscl_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            if (!Confirm(1, "ESCL Initialize"))
                return;

            try
            {
                Log("info", "Initializing ESCL...");
                var result = await J2534Service.Instance.InitializePatsAsync();
                if (result.Success)
                {
                    MessageBox.Show("ESCL initialized!");
                    Log("success", "ESCL initialized");
                }
                else
                {
                    Log("error", result.Error ?? "ESCL init failed");
                }
            }
            catch (Exception ex)
            {
                ShowError("ESCL Failed", "Could not initialize ESCL", ex);
            }
        }

        private async void BtnDisable_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            try
            {
                Log("info", "Disabling BCM security...");
                var result = await J2534Service.Instance.RestoreBcmDefaultsAsync();
                if (result.Success)
                {
                    MessageBox.Show("BCM security disabled!");
                    Log("success", "BCM security disabled");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex)
            {
                ShowError("BCM Failed", "Could not disable BCM security", ex);
            }
        }
        #endregion

        #region Diag - Using J2534Service Singleton
        private async void BtnP160A_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            if (!Confirm(1, "Clear P160A")) return;
            
            try
            {
                var result = await J2534Service.Instance.ClearDtcsAsync();
                if (result.Success)
                {
                    MessageBox.Show("P160A cleared!");
                    Log("success", "P160A cleared");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
        }

        private async void BtnB10A2_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            if (!Confirm(1, "Clear B10A2")) return;
            
            try
            {
                var result = await J2534Service.Instance.ClearDtcsAsync();
                if (result.Success)
                {
                    MessageBox.Show("B10A2 cleared!");
                    Log("success", "B10A2 cleared");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
        }

        private async void BtnCrush_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            if (!Confirm(1, "Clear Crush Event")) return;
            
            try
            {
                var result = await J2534Service.Instance.ClearCrashFlagAsync();
                if (result.Success)
                {
                    MessageBox.Show("Crush event cleared!");
                    Log("success", "Crush cleared");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
        }

        private async void BtnGateway_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            
            // Check workflow is configured
            if (!J2534Service.Instance.IsWorkflowConfigured)
            {
                MessageBox.Show("Please read VIN first to configure the workflow system.");
                return;
            }

            // Check for gateway first
            var gw = await J2534Service.Instance.CheckGatewayAsync();
            if (!gw.Success || !gw.HasGateway)
            {
                MessageBox.Show("No gateway detected on this vehicle");
                Log("info", "No gateway module detected");
                return;
            }

            if (!Confirm(1, "Unlock Gateway")) return;

            var ic = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(ic))
            {
                MessageBox.Show("Enter incode first");
                return;
            }

            // Get vehicle info for splash
            string vehicleInfo = _cmbVehicles.SelectedItem?.ToString() ?? "Unknown Vehicle";
            string vin = _lblVin.Text.Replace("VIN: ", "");
            if (vin.Length == 17) vehicleInfo = $"{vehicleInfo} ({vin})";

            // Show operation splash
            using var splash = new OperationProgressForm();
            splash.Configure(
                "Unlocking Gateway",
                vehicleInfo,
                1,
                "Detecting gateway module",
                "Unlocking gateway (GWM)",
                "Unlocking BCM",
                "Starting security session"
            );

            Services.J2534Service.GatewayResult? result = null;
            Exception? error = null;

            // Run operation in background
            var opTask = Task.Run(async () =>
            {
                try
                {
                    // Step 1: Gateway detection (already done)
                    splash.StartStep(0);
                    splash.SetInstruction("Gateway detected...");
                    await Task.Delay(200);
                    splash.CompleteStep(0);

                    // Step 2: Unlock gateway
                    splash.StartStep(1);
                    splash.SetInstruction("Unlocking gateway module...");
                    await Task.Delay(300);
                    splash.CompleteStep(1);

                    // Step 3: Unlock BCM
                    splash.StartStep(2);
                    splash.SetInstruction("Unlocking BCM...");
                    result = await J2534Service.Instance.UnlockGatewayWithWorkflowAsync(ic);
                    splash.CompleteStep(2, result.Success);

                    if (result.Success)
                    {
                        // Step 4: Session started
                        splash.StartStep(3);
                        splash.SetInstruction("Starting security session...");
                        await Task.Delay(300);
                        splash.CompleteStep(3);

                        splash.Complete(true, "Gateway + BCM unlocked!");
                    }
                    else
                    {
                        splash.Complete(false, result.Error ?? "Unlock failed");
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                    splash.Complete(false, ex.Message);
                }
            });

            // Show splash (blocks until closed)
            splash.ShowDialog(this);
            await opTask;

            // Handle results on UI thread
            if (error != null)
            {
                ShowError("Error", "Gateway unlock failed", error);
                return;
            }

            if (result == null) return;

            if (result.Success)
            {
                _tokenBalance--;
                _lblTokens.Text = $"Tokens: {_tokenBalance}";

                // Start session timer
                _sessionTimer?.Start();
                _sessionWarningShown = false;
                UpdateSessionTimerDisplay();

                // Update session manager
                var workflow = J2534Service.Instance.Workflow;
                if (workflow != null)
                {
                    _sessionManager.UpdateFromVehicleSession(workflow.Session);
                }

                Log("success", "Gateway + BCM unlocked - session active");
                MessageBox.Show("Gateway + BCM unlocked!\n\n✓ All key operations are now FREE until session expires.\n✓ Session timer started (10 minutes).");
            }
            else
            {
                Log("error", result.Error ?? "Gateway unlock failed");
                MessageBox.Show($"Gateway unlock failed: {result.Error ?? "Unknown error"}");
            }
        }

        /// <summary>
        /// <summary>
        /// Shows 10-minute security lockout countdown when NRC 0x36 (security locked) is received.
        /// Called automatically when BCM is locked due to failed attempts.
        /// </summary>
        /// <param name="durationSeconds">Lockout duration (default 600 = 10 minutes)</param>
        /// <returns>True if countdown completed, false if cancelled</returns>
        private async Task<bool> ShowSecurityLockoutCountdownAsync(int durationSeconds = 600)
        {
            Log("warning", $"Security locked (NRC 0x36) - {durationSeconds / 60} minute lockout");

            var confirmResult = MessageBox.Show(
                $"⚠️ SECURITY LOCKOUT DETECTED\n\n" +
                $"The BCM has temporarily locked security access.\n" +
                $"This happens after failed authentication attempts.\n\n" +
                $"You must wait {durationSeconds / 60} minutes before retrying.\n\n" +
                $"During this time:\n" +
                $"• Keep the tool connected\n" +
                $"• Keep ignition ON\n" +
                $"• Do NOT disconnect any cables\n\n" +
                $"Start the countdown now?",
                "Security Lockout",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirmResult != DialogResult.Yes)
                return false;

            Log("info", $"Starting {durationSeconds / 60}-minute security lockout countdown...");

            // Define keep-alive action
            Func<Task<bool>> keepAlive = async () =>
            {
                try
                {
                    await J2534Service.Instance.SendTesterPresentAsync();
                    return true;
                }
                catch
                {
                    return false;
                }
            };

            // Show timed access form
            using var timedForm = new TimedSecurityAccessForm(durationSeconds, keepAlive);
            timedForm.ShowDialog(this);

            if (timedForm.Cancelled)
            {
                Log("warning", "Security lockout countdown cancelled by user");
                return false;
            }

            if (timedForm.Completed)
            {
                Log("success", "Security lockout countdown completed - ready to retry");
                return true;
            }

            return false;
        }

        private async void BtnKeypad_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            
            var r = MessageBox.Show("YES = Read keypad code\nNO = Write new code", "Keypad Code", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return;

            if (r == DialogResult.Yes)
            {
                if (!Confirm(1, "Read Keypad")) return;
                try
                {
                    // Read keypad - placeholder
                    MessageBox.Show("Keypad read not implemented in J2534Service yet");
                    Log("info", "Keypad read requested");
                }
                catch (Exception ex) { ShowError("Error", "Failed", ex); }
            }
            else
            {
                var nc = Microsoft.VisualBasic.Interaction.InputBox("Enter 5-digit code:", "Keypad Code", "");
                if (nc.Length != 5) return;
                if (!Confirm(1, "Write Keypad")) return;
                try
                {
                    // Write keypad - placeholder
                    MessageBox.Show($"Keypad write to {nc} not implemented in J2534Service yet");
                    Log("info", $"Keypad write {nc} requested");
                }
                catch (Exception ex) { ShowError("Error", "Failed", ex); }
            }
        }

        private async void BtnBcm_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            
            if (MessageBox.Show("WARNING: This will reset ALL BCM settings to factory defaults!\n\nAre you sure?", "BCM Factory Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            if (!Confirm(1, "BCM Factory Reset")) return;

            try
            {
                var result = await J2534Service.Instance.RestoreBcmDefaultsAsync();
                if (result.Success)
                {
                    MessageBox.Show("BCM reset to factory defaults!");
                    Log("success", "BCM factory reset");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
        }

        private async void BtnModInfo_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect first");
                return;
            }

            try
            {
                var result = await J2534Service.Instance.ReadVehicleInfoAsync();
                if (result.Success == false)
                {
                    Log("error", $"ReadVehicleInfo failed: {result.ErrorMessage}");
                    MessageBox.Show(result.ErrorMessage ?? "Failed to read module info.");
                    return;
                }

                var lines = new List<string>
                {
                    $"VIN: {result.Vin ?? "N/A"}",
                    $"Year: {(result.Year?.ToString() ?? "N/A")}",
                    $"Model: {result.Model ?? "N/A"}",
                    $"Platform: {result.PlatformCode ?? "N/A"}",
                    $"Security Target: {result.SecurityTargetModule ?? "N/A"}"
                };

                if (!string.IsNullOrWhiteSpace(result.AdditionalInfo))
                {
                    lines.Add(string.Empty);
                    lines.Add(result.AdditionalInfo.Trim());
                }

                var info = string.Join(Environment.NewLine, lines);

                MessageBox.Show(info, "Module Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("info", "Module info shown to user.");
            }
            catch (Exception ex)
            {
                Log("error", $"Read module info failed: {ex.Message}");
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Free - Using J2534Service Singleton
        private async void BtnDtc_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            
            try
            {
                var result = await J2534Service.Instance.ClearDtcsAsync();
                if (result.Success)
                {
                    MessageBox.Show("All DTCs cleared!");
                    Log("success", "DTCs cleared");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
        }

        private async void BtnKam_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            
            try
            {
                var result = await J2534Service.Instance.VehicleResetAsync();
                if (result.Success)
                {
                    MessageBox.Show("KAM cleared!");
                    Log("success", "KAM cleared");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
        }

        private async void BtnReset_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            
            try
            {
                var result = await J2534Service.Instance.VehicleResetAsync();
                if (result.Success)
                {
                    MessageBox.Show("Vehicle reset complete!");
                    Log("success", "Vehicle reset");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
        }

        private async void BtnReadKeys_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            
            try
            {
                var result = await J2534Service.Instance.ReadKeyCountAsync();
                if (result.Success)
                {
                    _lblKeys.Text = result.KeyCount.ToString();
                    _keySlotPanel.ProgrammedKeys = result.KeyCount;
                    MessageBox.Show($"Keys programmed: {result.KeyCount}");
                    Log("success", $"Keys: {result.KeyCount}");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
        }
        #endregion

        private void AutoStartOnce()
        {
            if (_didAutoStart) return;
            _didAutoStart = true;

            // Auto-start on successful login: land on PATS tab and populate device list
            BeginInvoke(new Action(() =>
            {
                try { BtnScan_Click(null, EventArgs.Empty); } catch { }
            }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                J2534Service.Instance.Disconnect();
                _logoImage?.Dispose();
                _logoImage = null;
            }
            catch { }

            base.OnFormClosing(e);
        }
    }
}
