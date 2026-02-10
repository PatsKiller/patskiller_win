using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Communication;
using PatsKillerPro.Forms;
using PatsKillerPro.J2534;
using PatsKillerPro.Services;
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
        private string _userEmail = "", _userDisplayName = "", _authToken = "", _refreshToken = "";
        private int _tokenBalance = 0;
        private List<J2534DeviceInfo> _devices = new();
        private bool _isConnected = false;
        private bool _uiBusy = false;

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
        private Button _btnTab1 = null!, _btnTab2 = null!, _btnTab3 = null!, _btnLogout = null!;
        private Label _lblTokensTotal = null!, _lblTokensPromo = null!, _lblPromoExpiry = null!, _lblLicense = null!, _lblUser = null!, _lblUserEmail = null!, _lblStatus = null!, _lblDeviceBanner = null!, _lblVin = null!, _lblKeys = null!;
        private ComboBox _cmbDevices = null!, _cmbVehicles = null!;
        private TextBox _txtOutcode = null!, _txtIncode = null!, _txtEmail = null!, _txtPassword = null!;
        private RichTextBox _txtLog = null!;
        private ToolTip _toolTip = null!;

        // BCM Session Panel
        private Panel _bcmSessionPanel = null!;
        private Label _lblBcmStatus = null!, _lblSessionTimer = null!, _lblKeepAlive = null!;
        private Button _btnProgram = null!, _btnErase = null!, _btnKeyCounters = null!;
        private System.Windows.Forms.Timer _sessionTimerUpdate = null!;

        // DPI helpers (keeps runtime-created controls scaling-friendly)
        private int Dpi(int px) => (int)Math.Round(px * (DeviceDpi / 96f));
        private Padding DpiPad(int l, int t, int r, int b) => new Padding(Dpi(l), Dpi(t), Dpi(r), Dpi(b));


        public MainForm()
        {
            InitializeComponent();
            ApplyDarkTitleBar();
            BuildUI();

            // Centralized UI busy gating (prevents double-click / out-of-order ops).
            J2534Service.Instance.BusyChanged += busy =>
            {
                if (IsDisposed) return;
                try { BeginInvoke(new Action(() => SetUiBusy(busy))); } catch { /* ignore */ }
            };

            // Session restore handled in MainForm_Shown.
            
            // Wire up ProActivityLogger to show messages in UI log panel
            ProActivityLogger.Instance.OnLogMessage += (type, msg) =>
            {
                if (IsDisposed) return;
                try { BeginInvoke(new Action(() => Log(type, msg))); } catch { /* ignore */ }
            };

            // License status: keep header in sync
            try
            {
                LicenseService.Instance.OnLicenseChanged += res =>
                {
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            ApplyAuthHeader();
                            if (res.IsValid)
                                Log("success", $"{res.Message}");
                            else if (res.HasLicense)
                                Log("warning", $"{res.Message}");
                            else
                                Log("info", $"{res.Message}");
                        }));
                    }
                    catch { /* ignore */ }
                };
            }
            catch { /* ignore */ }

            // Dispose cached images cleanly
            this.FormClosed += (_, __) => { try { _logoImage?.Dispose(); } catch { } };
        }

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
            // Initialize ToolTip for all controls
            _toolTip = new ToolTip
            {
                AutoPopDelay = 10000,
                InitialDelay = 500,
                ReshowDelay = 200,
                ShowAlways = true
            };

            // HEADER (table-based layout so it doesn't overlap at high DPI / long email)
            _header = new Panel
            {
                Dock = DockStyle.Top,
                // Increased height so the two-line identity (Name + Email) is always visible
                // alongside the token/license stack at common DPI scaling settings.
                Height = Dpi(112),
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
                RowCount = 3,
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
                Margin = new Padding(0, Dpi(2), 0, 0),
                Visible = false};
            textStack.Controls.Add(title, 0, 0);
            textStack.Controls.Add(subtitle, 0, 1);

            left.Controls.Add(logo, 0, 0);
            left.Controls.Add(textStack, 1, 0);

            
                        // Right block (tokens + promo + license + identity) + logout button
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };

            var meta = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 6,
                Margin = new Padding(0, 0, 10, 0)
            };
            meta.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // total
            meta.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // promo
            meta.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // promo expiry
            meta.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // license
            meta.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // user name
            meta.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // user email

            _lblTokensTotal = new Label
            {
                AutoSize = true,
                ForeColor = SUCCESS,
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 0, 0, 0)
            };

            _lblTokensPromo = new Label
            {
                AutoSize = true,
                ForeColor = WARNING,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 2, 0, 0)
            };

            _lblPromoExpiry = new Label
            {
                AutoSize = true,
                ForeColor = TEXT_DIM,
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 0, 0, 0),
                Visible = false
            };

            _lblLicense = new Label
            {
                AutoSize = true,
                ForeColor = TEXT_DIM,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 4, 0, 0),
                Cursor = Cursors.Hand
            };

            // Identity (two-line: Name + Email)
            _lblUser = new Label
            {
                AutoSize = true,
                ForeColor = TEXT,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 6, 0, 0),
                AutoEllipsis = true,
                MaximumSize = new Size(Dpi(420), 0)
            };

            _lblUserEmail = new Label
            {
                AutoSize = true,
                ForeColor = TEXT_DIM,
                // Slightly stronger emphasis improves readability on dark header backgrounds.
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 0, 0, 0),
                AutoEllipsis = true,
                // Allow longer emails before ellipsis.
                MaximumSize = new Size(Dpi(420), 0)
            };

            _lblLicense.Click += (_, __) =>
            {
                if (!IsLoggedIn) return;
                using var lic = new PatsKillerPro.Forms.LicenseActivationForm();
                lic.ShowDialog(this);
                try { _ = LicenseService.Instance.ValidateAsync(); } catch { }
                try { _ = LicenseService.Instance.RefreshAccountLicensesAsync(); } catch { }
                ApplyAuthHeader();
            };

            meta.Controls.Add(_lblTokensTotal, 0, 0);
            meta.Controls.Add(_lblTokensPromo, 0, 1);
            meta.Controls.Add(_lblPromoExpiry, 0, 2);
            meta.Controls.Add(_lblLicense, 0, 3);
            meta.Controls.Add(_lblUser, 0, 4);
            meta.Controls.Add(_lblUserEmail, 0, 5);
            right.Controls.Add(meta);

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
            // Start locked-down; login is modal and will enable the app on success.
            ShowLogin();
            this.Shown += MainForm_Shown;

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
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            row1.Controls.Add(btnScan);

            var btnConn = AutoBtn("Connect", SUCCESS);
            btnConn.Click += BtnConnect_Click;
            row1.Controls.Add(btnConn);

            _lblStatus = new Label { Text = "Status: Not Connected", Font = new Font("Segoe UI", 11), ForeColor = WARNING, AutoSize = true, Margin = DpiPad(30, 12, 0, 0) };
            row1.Controls.Add(_lblStatus);

// Device banner (operator-friendly)
            var row1b = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true, Margin = new Padding(0, Dpi(6), 0, 0) };
            _lblDeviceBanner = new Label
            {
                Text = "Selected Device: —",
                Font = new Font("Segoe UI", 10),
                ForeColor = TEXT_DIM,
                AutoSize = true,
                Margin = DpiPad(0, 0, 0, 0)
            };
            row1b.Controls.Add(_lblDeviceBanner);

            sec1.Controls.Add(row1);
            sec1.Controls.Add(row1b);
            layout.Controls.Add(sec1, 0, 0);

            // === SECTION 2: VEHICLE INFORMATION ===
            var sec2 = Section("VEHICLE INFORMATION");
            var grid2 = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            grid2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true, Margin = new Padding(0) };
            
            var btnVin = AutoBtn("Read VIN", ACCENT);
            btnVin.Click += BtnReadVin_Click;
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

            // Keys badge (right side, no manual coordinates)
            var keysBg = new Panel { Size = new Size(Dpi(130), Dpi(50)), BackColor = SURFACE, Margin = DpiPad(20, 0, 0, 0) };
            keysBg.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, keysBg.Width - 1, keysBg.Height - 1); };
            keysBg.Controls.Add(new Label { Text = "KEYS", Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = TEXT_MUTED, Dock = DockStyle.Top, Height = Dpi(18), TextAlign = ContentAlignment.MiddleCenter });
            _lblKeys = new Label { Text = "--", Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = SUCCESS, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            keysBg.Controls.Add(_lblKeys);

            grid2.Controls.Add(row2, 0, 0);
            grid2.Controls.Add(keysBg, 1, 0);
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

            row3.Controls.Add(new Label { Text = "INCODE:", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = TEXT, AutoSize = true, Margin = DpiPad(30, 12, 10, 0) });
            
            _txtIncode = MakeTextBox(160);
            _txtIncode.ReadOnly = true;
            _txtIncode.Margin = DpiPad(0, 6, 15, 0);
            row3.Controls.Add(_txtIncode);

            var btnGetIncode = AutoBtn("Get Incode", ACCENT);
            btnGetIncode.Click += BtnGetIncode_Click;
            row3.Controls.Add(btnGetIncode);

            sec3.Controls.Add(row3);

            // === BCM SESSION PANEL (Professional Design) ===
            _bcmSessionPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = Dpi(56),
                BackColor = Color.FromArgb(25, 32, 42),
                Margin = new Padding(0, Dpi(10), 0, 0)
            };
            _bcmSessionPanel.Paint += (s, e) =>
            {
                var state = BcmSessionManager.Instance.GetState();
                var borderColor = state.IsUnlocked ? SUCCESS : Color.FromArgb(70, 75, 85);
                using var pen = new Pen(borderColor, state.IsUnlocked ? 2 : 1);
                var rect = new Rectangle(0, 0, _bcmSessionPanel.Width - 1, _bcmSessionPanel.Height - 1);
                e.Graphics.DrawRectangle(pen, rect);
            };

            // Use TableLayoutPanel for reliable left-right layout
            var sessionTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(Dpi(12), Dpi(10), Dpi(12), Dpi(10))
            };
            sessionTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Left expands
            sessionTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // Right auto-sizes

            // LEFT: Status + Session info
            var leftFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0)
            };

            _lblBcmStatus = new Label
            {
                Text = "🔒 BCM LOCKED",
                Font = new Font("Segoe UI Semibold", 11),
                ForeColor = DANGER,
                AutoSize = true,
                Margin = new Padding(0, Dpi(3), Dpi(20), 0)
            };
            leftFlow.Controls.Add(_lblBcmStatus);

            _lblSessionTimer = new Label
            {
                Text = "Session: 00:00",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(140, 200, 140),
                AutoSize = true,
                Margin = new Padding(0, Dpi(4), Dpi(8), 0),
                Visible = false
            };
            leftFlow.Controls.Add(_lblSessionTimer);

            _lblKeepAlive = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 12),
                ForeColor = SUCCESS,
                AutoSize = true,
                Margin = new Padding(0, Dpi(2), 0, 0),
                Visible = false
            };
            _toolTip.SetToolTip(_lblKeepAlive, "Keep-alive active\nBCM session maintained");
            leftFlow.Controls.Add(_lblKeepAlive);

            sessionTable.Controls.Add(leftFlow, 0, 0);

            // RIGHT: Operation buttons in a FlowLayoutPanel
            var rightFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0)
            };

            // Helper for session buttons
            Button CreateSessionBtn(string text, int width)
            {
                var btn = new Button
                {
                    Text = text,
                    Size = new Size(Dpi(width), Dpi(32)),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(45, 55, 70),
                    ForeColor = Color.FromArgb(140, 145, 155),
                    Font = new Font("Segoe UI", 9),
                    Enabled = false,
                    Cursor = Cursors.Arrow,
                    Margin = new Padding(Dpi(6), 0, 0, 0)
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(60, 70, 85);
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 65, 80);
                return btn;
            }

            _btnProgram = CreateSessionBtn("🔑 Program", 95);
            _btnProgram.Margin = new Padding(0, 0, 0, 0); // First button no left margin
            _btnProgram.Click += BtnProgram_Click;
            _toolTip.SetToolTip(_btnProgram, "Program new PATS keys\nRequires BCM unlock\n[FREE - included with incode]");
            rightFlow.Controls.Add(_btnProgram);

            _btnErase = CreateSessionBtn("🗑️ Erase", 85);
            _btnErase.Click += BtnErase_Click;
            _toolTip.SetToolTip(_btnErase, "Erase all programmed keys\n⚠️ This cannot be undone!\n[FREE - included with incode]");
            rightFlow.Controls.Add(_btnErase);

            _btnKeyCounters = CreateSessionBtn("📊 Counters", 95);
            _btnKeyCounters.Click += BtnKeyCounters_Click;
            _toolTip.SetToolTip(_btnKeyCounters, "View/Edit key counters (Min/Max)\n[FREE - included with incode]");
            rightFlow.Controls.Add(_btnKeyCounters);

            sessionTable.Controls.Add(rightFlow, 1, 0);
            _bcmSessionPanel.Controls.Add(sessionTable);
            sec3.Controls.Add(_bcmSessionPanel);

            // Session timer update (every 1 second)
            _sessionTimerUpdate = new System.Windows.Forms.Timer { Interval = 1000 };
            _sessionTimerUpdate.Tick += (s, e) => UpdateSessionTimerDisplay();
            _sessionTimerUpdate.Start();

            // Wire up BcmSessionManager events
            BcmSessionManager.Instance.SessionStateChanged += OnBcmSessionStateChanged;
            BcmSessionManager.Instance.LogMessage += msg => Log("info", msg);
            BcmSessionManager.Instance.UnlockOperationsEnabled += OnUnlockOperationsEnabled;

            layout.Controls.Add(sec3, 0, 2);
            layout.SetColumnSpan(sec3, 2);

            // === SECTION 4: ADDITIONAL OPERATIONS ===
            var sec4 = Section("ADDITIONAL OPERATIONS");
            var row4 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            // Parameter Reset - does not require BCM session (uses its own incode flow)
            var btnParam = AutoBtn("⚙️ Parameter Reset", WARNING);
            btnParam.Click += BtnParam_Click;
            _toolTip.SetToolTip(btnParam, "Reset PATS parameters across modules\nRequires separate incode\n[1 TOKEN per module]");
            row4.Controls.Add(btnParam);

            // ESCL Initialize - standalone operation
            var btnEscl = AutoBtn("🔧 ESCL Initialize", BTN_BG);
            btnEscl.Click += BtnEscl_Click;
            _toolTip.SetToolTip(btnEscl, "Initialize Electronic Steering Column Lock\n[1 TOKEN]");
            row4.Controls.Add(btnEscl);

            // Disable BCM Security - manual unlock (alternative to Get Incode auto-unlock)
            var btnDisable = AutoBtn("🔓 Manual BCM Unlock", BTN_BG);
            btnDisable.Click += BtnDisable_Click;
            _toolTip.SetToolTip(btnDisable, "Manually unlock BCM with existing incode\nUse if auto-unlock failed");
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
            var d1 = AutoBtn("Clear P160A", BTN_BG); d1.Click += BtnP160A_Click; r1.Controls.Add(d1);
            var d2 = AutoBtn("Clear B10A2", BTN_BG); d2.Click += BtnB10A2_Click; r1.Controls.Add(d2);
            var d3 = AutoBtn("Clear Crash Event", BTN_BG); d3.Click += BtnCrush_Click; r1.Controls.Add(d3);
            sec1.Controls.Add(r1);
            layout.Controls.Add(sec1, 0, 0);

            var sec2 = Section("GATEWAY OPERATIONS");
            var r2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            var g1 = AutoBtn("Unlock Gateway", BTN_BG); g1.Click += BtnGateway_Click; r2.Controls.Add(g1);
            sec2.Controls.Add(r2);
            layout.Controls.Add(sec2, 1, 0);

            var sec3 = Section("KEYPAD & BCM");
            var r3 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            var k1 = AutoBtn("Keypad Code", BTN_BG); k1.Click += BtnKeypad_Click; r3.Controls.Add(k1);
            var b1 = AutoBtn("BCM Factory Defaults", DANGER); b1.Click += BtnBcm_Click; r3.Controls.Add(b1);
            sec3.Controls.Add(r3);
            layout.Controls.Add(sec3, 0, 1);

            var sec4 = Section("MODULE INFO");
            var r4 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            var mod = AutoBtn("Read All Module Info", BTN_BG); mod.Click += BtnModInfo_Click; r4.Controls.Add(mod);
            sec4.Controls.Add(r4);
            layout.Controls.Add(sec4, 1, 1);

            // === PHASE 2: ADVANCED TOOLS ===
            var sec5 = Section("ADVANCED TOOLS");
            var r5 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            var btnTargets = AutoBtn("🎯 Target Blocks", ACCENT); btnTargets.Click += BtnTargets_Click; r5.Controls.Add(btnTargets);
            var btnKeyCounters = AutoBtn("🔢 Key Counters", BTN_BG); btnKeyCounters.Click += BtnKeyCounters_Click; r5.Controls.Add(btnKeyCounters);
            var btnEngineering = AutoBtn("🔧 Engineering Mode", WARNING); btnEngineering.Click += BtnEngineering_Click; r5.Controls.Add(btnEngineering);
            sec5.Controls.Add(r5);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(sec5, 0, 2);
            layout.SetColumnSpan(sec5, 2);

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
            var f1 = AutoBtn("Clear All DTCs", BTN_BG); f1.Click += BtnDtc_Click; r1.Controls.Add(f1);
            var f2 = AutoBtn("Clear KAM", BTN_BG); f2.Click += BtnKam_Click; r1.Controls.Add(f2);
            var f3 = AutoBtn("Vehicle Reset", BTN_BG); f3.Click += BtnReset_Click; r1.Controls.Add(f3);
            sec1.Controls.Add(r1);
            layout.Controls.Add(sec1, 0, 1);

            var sec2 = Section("READ OPERATIONS");
            var r2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            var rd1 = AutoBtn("Read Keys Count", BTN_BG); rd1.Click += BtnReadKeys_Click; r2.Controls.Add(rd1);
            var rd2 = AutoBtn("Read Module Info", BTN_BG); rd2.Click += BtnModInfo_Click; r2.Controls.Add(rd2);
            sec2.Controls.Add(r2);
            layout.Controls.Add(sec2, 1, 1);

            var sec3 = Section("RESOURCES & SUPPORT");
            var r3 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            var u1 = AutoBtn("User Guide", ACCENT); u1.Click += (s, e) => OpenUrl("https://patskiller.com/faqs"); r3.Controls.Add(u1);
            var u2 = AutoBtn("Buy Tokens", SUCCESS); u2.Click += (s, e) => OpenUrl("https://patskiller.com/buy-tokens"); r3.Controls.Add(u2);
            var u3 = AutoBtn("Contact Support", BTN_BG); u3.Click += (s, e) => OpenUrl("https://patskiller.com/contact"); r3.Controls.Add(u3);
            sec3.Controls.Add(r3);
            layout.Controls.Add(sec3, 0, 2);
            layout.SetColumnSpan(sec3, 2);

            _content.Controls.Add(_freeTab);
        }

        private void BuildLogin()
        {
            _loginPanel = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false };
            
            // DPI-scaled card dimensions
            int cardW = Dpi(480);
            int cardH = Dpi(560);
            
            var card = new Panel 
            { 
                Size = new Size(cardW, cardH), 
                BackColor = CARD,
                Padding = DpiPad(20, 20, 20, 20)
            };
            card.Paint += (s, e) => { using var p = new Pen(BORDER, 2); e.Graphics.DrawRectangle(p, 1, 1, card.Width - 3, card.Height - 3); };

            int cy = Dpi(35);
            int btnW = Dpi(400);
            int padL = (cardW - btnW) / 2;
            
            // Title
            var lblTitle = new Label 
            { 
                Text = "Welcome to PatsKiller Pro", 
                Font = new Font("Segoe UI", 22, FontStyle.Bold), 
                ForeColor = TEXT, 
                Size = new Size(cardW - Dpi(20), Dpi(45)), 
                Location = new Point(Dpi(10), cy), 
                TextAlign = ContentAlignment.MiddleCenter 
            };
            card.Controls.Add(lblTitle); 
            cy += Dpi(50);
            
            // Subtitle
            var lblSub = new Label 
            { 
                Text = "Sign in to access your tokens", 
                Font = new Font("Segoe UI", 12), 
                ForeColor = TEXT_MUTED, 
                Size = new Size(cardW - Dpi(20), Dpi(28)), 
                Location = new Point(Dpi(10), cy), 
                TextAlign = ContentAlignment.MiddleCenter 
            };
            card.Controls.Add(lblSub); 
            cy += Dpi(55);

            // Google button
            var btnG = new Button 
            { 
                Text = "Continue with Google", 
                Size = new Size(btnW, Dpi(56)), 
                Location = new Point(padL, cy), 
                FlatStyle = FlatStyle.Flat, 
                BackColor = Color.White, 
                ForeColor = Color.FromArgb(50, 50, 50), 
                Font = new Font("Segoe UI", 13, FontStyle.Bold), 
                Cursor = Cursors.Hand 
            };
            btnG.FlatAppearance.BorderColor = BORDER;
            btnG.Click += BtnGoogle_Click;
            card.Controls.Add(btnG); 
            cy += Dpi(75);

            // Divider
            var lblDiv = new Label 
            { 
                Text = "───────  or sign in with email  ───────", 
                Font = new Font("Segoe UI", 10), 
                ForeColor = TEXT_MUTED, 
                Size = new Size(btnW, Dpi(25)), 
                Location = new Point(padL, cy), 
                TextAlign = ContentAlignment.MiddleCenter 
            };
            card.Controls.Add(lblDiv); 
            cy += Dpi(40);
            
            // Email label
            var lblEmail = new Label 
            { 
                Text = "Email", 
                Font = new Font("Segoe UI", 11), 
                ForeColor = TEXT_DIM, 
                Location = new Point(padL, cy), 
                AutoSize = true 
            };
            card.Controls.Add(lblEmail); 
            cy += Dpi(28);
            
            // Email textbox
            _txtEmail = MakeTextBox(btnW); 
            _txtEmail.Location = new Point(padL, cy); 
            _txtEmail.Font = new Font("Segoe UI", 12);
            _txtEmail.Height = Dpi(36);
            card.Controls.Add(_txtEmail); 
            cy += Dpi(55);
            
            // Password label
            var lblPass = new Label 
            { 
                Text = "Password", 
                Font = new Font("Segoe UI", 11), 
                ForeColor = TEXT_DIM, 
                Location = new Point(padL, cy), 
                AutoSize = true 
            };
            card.Controls.Add(lblPass); 
            cy += Dpi(28);
            
            // Password textbox
            _txtPassword = MakeTextBox(btnW); 
            _txtPassword.Location = new Point(padL, cy); 
            _txtPassword.Font = new Font("Segoe UI", 12);
            _txtPassword.Height = Dpi(36);
            _txtPassword.UseSystemPasswordChar = true; 
            _txtPassword.KeyPress += (s, e) => { if (e.KeyChar == 13) DoLogin(); }; 
            card.Controls.Add(_txtPassword); 
            cy += Dpi(62);

            // Sign In button
            var btnL = new Button 
            { 
                Text = "Sign In", 
                Size = new Size(btnW, Dpi(56)), 
                Location = new Point(padL, cy), 
                FlatStyle = FlatStyle.Flat, 
                BackColor = ACCENT, 
                ForeColor = Color.White, 
                Font = new Font("Segoe UI", 14, FontStyle.Bold), 
                Cursor = Cursors.Hand 
            };
            btnL.FlatAppearance.BorderColor = ACCENT;
            btnL.FlatAppearance.BorderSize = 0;
            btnL.Click += (s, e) => DoLogin();
            card.Controls.Add(btnL);

            _loginPanel.Controls.Add(card);
            _loginPanel.Resize += (s, e) => CenterLoginPanel();
        }

        /// <summary>
        /// Centers the login card inside the login panel.
        /// </summary>
        private void CenterLoginPanel()
        {
            if (_loginPanel == null || _loginPanel.Controls.Count == 0) return;
            var card = _loginPanel.Controls[0];
            if (card == null) return;
            card.Location = new Point(
                Math.Max(0, (_loginPanel.Width - card.Width) / 2),
                Math.Max(0, (_loginPanel.Height - card.Height) / 2 - Dpi(20))
            );
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
        
        private async Task<bool> ConfirmAsync(int cost, string op)
        {
            if (cost <= 0) return true;

            // Strict policy: token operations require BOTH SSO + valid license tied to the same email.
            if (!IsLoggedIn)
            {
                MessageBox.Show("Please sign in with Google to continue.", "Sign-in Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                await PromptAuthModalAsync();
                if (!IsLoggedIn) return false;
            }

            // Ensure license is valid for this user
            var lic = await LicenseService.Instance.ValidateAsync();
            ApplyAuthHeader();

            if (!LicenseService.Instance.IsLicensed)
            {
                var msg = "An active license is required to use paid features." + Environment.NewLine + Environment.NewLine +
                          "Click the License status in the header to activate your key.";
                if (!string.IsNullOrWhiteSpace(lic.Message))
                    msg = $"{lic.Message}{Environment.NewLine}{Environment.NewLine}{msg}";

                MessageBox.Show(msg, "License Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // Best-effort heartbeat before token spend (keeps server-side state fresh)
            try { await LicenseService.Instance.HeartbeatAsync(); } catch { }

            // Refresh balance to avoid stale UI / false negatives
            try
            {
                await RefreshTokenBalanceAsync();
            }
            catch { /* ignore */ }

            if (_tokenBalance < cost)
            {
                MessageBox.Show($"Need {cost} token(s). Current balance: {_tokenBalance}.", "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var confirmMsg = $"{op}{Environment.NewLine}Cost: {cost} token(s){Environment.NewLine}{Environment.NewLine}Proceed?";
            return MessageBox.Show(confirmMsg, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
        #endregion

        #region Navigation
        private void ShowLogin()
        {
            // Modal login is handled via PromptLoginModalAsync(). This panel is a
            // non-modal fallback surface and also prevents the user from operating
            // features while logged out.

            _tabBar.Visible = false;
            _content.Visible = false;
            _logPanel.Visible = false;

            // Keep embedded login panel hidden; we use a modal login dialog.
            _loginPanel.Visible = false;

            // Hide token/user UI while logged out.
            _lblTokensTotal.Visible = false;
            _lblTokensTotal.Text = string.Empty;
            _lblTokensPromo.Visible = false;
            _lblTokensPromo.Text = string.Empty;
            _lblPromoExpiry.Visible = false;
            _lblPromoExpiry.Text = string.Empty;
            _lblLicense.Visible = false;
            _lblLicense.Text = string.Empty;
            _lblUser.Visible = false;
            _lblUserEmail.Visible = false;
            _btnLogout.Visible = false;
        }
        private void ShowMain() { _loginPanel.Visible = false; _tabBar.Visible = _content.Visible = _logPanel.Visible = _btnLogout.Visible = true; SwitchTab(0); AutoStartOnce(); }
        
        private void SwitchTab(int i) { _activeTab = i; _btnTab1.BackColor = i == 0 ? ACCENT : BTN_BG; _btnTab2.BackColor = i == 1 ? ACCENT : BTN_BG; _btnTab3.BackColor = i == 2 ? ACCENT : BTN_BG; _patsTab.Visible = i == 0; _diagTab.Visible = i == 1; _freeTab.Visible = i == 2; }
        #endregion

        #region Auth

        private bool IsLoggedIn => !string.IsNullOrWhiteSpace(_authToken);

        // Licensing (V21)
        private bool IsLicensed => LicenseService.Instance.IsLicensed;
        private bool IsAuthorized => IsLoggedIn;

        
private async void MainForm_Shown(object? sender, EventArgs e)
{
    // Restore cached session first so services have the Bearer token for strict licensing.
    LoadSession();

    if (IsLoggedIn)
    {
        // Validate license (strict: requires SSO identity), but do not block app startup if missing.
        try { await LicenseService.Instance.ValidateAsync(); } catch { /* best effort */ }

        // Fetch account licenses (masked) so the header can show availability.
        try { await LicenseService.Instance.RefreshAccountLicensesAsync(); } catch { /* best effort */ }

        await RefreshTokenBalanceAsync();
        ShowMain();
        ApplyAuthHeader();
        return;
    }

    // No SSO session — strict licensing cannot validate. Force sign-in.
    await PromptAuthModalAsync();
    ApplyAuthHeader();
}

        private void LoadSession()
        {
            _authToken = Settings.GetString("auth_token", "") ?? "";
            _refreshToken = Settings.GetString("refresh_token", "") ?? "";
            _userEmail = Settings.GetString("user_email", "") ?? "";
            
            
            _userDisplayName = Settings.GetString("user_display_name", "") ?? "";

            if (string.IsNullOrWhiteSpace(_userDisplayName) && !string.IsNullOrWhiteSpace(_authToken) && !string.IsNullOrWhiteSpace(_userEmail))
            {
                _userDisplayName = DeriveDisplayName(_authToken, _userEmail);
                try { SaveSession(); } catch { }
            }
            
// CRITICAL: Set auth context for downstream services when restoring session
if (!string.IsNullOrEmpty(_authToken) && !string.IsNullOrEmpty(_userEmail))
{
    ProActivityLogger.Instance.SetAuthContext(_authToken, _userEmail);
    TokenBalanceService.Instance.SetAuthContext(_authToken, _userEmail);
    IncodeService.Instance.SetAuthContext(_authToken, _userEmail);
    LicenseService.Instance.SetAuthContext(_authToken, _userEmail, _userDisplayName);
    Logger.Info($"[MainForm] Session restored for: {_userEmail}");
}
        }

        private void SaveSession()
        {
            Settings.SetString("auth_token", _authToken ?? "");
            Settings.SetString("refresh_token", _refreshToken ?? "");
            Settings.SetString("user_email", _userEmail ?? "");
            Settings.SetString("user_display_name", _userDisplayName ?? "");
            Settings.Save();
        }

        private void ClearSession()
        {
            _authToken = "";
            _refreshToken = "";
            _userEmail = "";
            _tokenBalance = 0;
            _userDisplayName = "";

            Settings.SetString("auth_token", "");
            Settings.SetString("refresh_token", "");
            Settings.SetString("user_email", "");
            Settings.SetString("user_display_name", "");
            Settings.Save();
            
            // Clear logger auth context too
            ProActivityLogger.Instance.ClearAuthContext();
            TokenBalanceService.Instance.ClearAuthContext();
            IncodeService.Instance.ClearAuthContext();
            LicenseService.Instance.ClearAuthContext();
        }

        

        private static string DeriveDisplayName(string authToken, string email)
        {
            var raw = (string?)null;

            try
            {
                var parts = (authToken ?? "").Split('.');
                if (parts.Length >= 2)
                {
                    var payload = parts[1].Replace('-', '+').Replace('_', '/');
                    payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Supabase typically stores Google profile data under user_metadata.
                    if (root.TryGetProperty("user_metadata", out var um) && um.ValueKind == JsonValueKind.Object)
                    {
                        if (um.TryGetProperty("full_name", out var fn) && fn.ValueKind == JsonValueKind.String)
                            raw = fn.GetString();
                        else if (um.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                            raw = n.GetString();
                        else if (um.TryGetProperty("display_name", out var dn) && dn.ValueKind == JsonValueKind.String)
                            raw = dn.GetString();
                    }

                    if (string.IsNullOrWhiteSpace(raw) && root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                        raw = name.GetString();
                }
            }
            catch
            {
                // best effort
            }

            // Normalize to "First L." (clean + readable)
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var parts = raw!.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) return parts[0];
                var first = parts[0];
                var last = parts[parts.Length - 1];
                return $"{first} {char.ToUpperInvariant(last[0])}.";
            }

            // Fallback: derive from email local-part
            var local = (email ?? "").Split('@')[0].Replace('.', ' ').Replace('_', ' ').Trim();
            if (string.IsNullOrWhiteSpace(local)) return email ?? "";
            var eparts = local.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (eparts.Length == 1)
            {
                var s = eparts[0];
                return char.ToUpperInvariant(s[0]) + s.Substring(1);
            }
            var f = eparts[0];
            var l = eparts[eparts.Length - 1];
            return $"{char.ToUpperInvariant(f[0]) + f.Substring(1)} {char.ToUpperInvariant(l[0])}.";
        }

private void ApplyAuthHeader()
{
    // Top-right meta stack:
    //   • Total tokens (green)
    //   • Promo tokens (yellow)
    //   • Promo expiry (separate line; hidden if null)
    //   • License (clickable)
    //   • User name
    //   • User email

    if (IsLoggedIn)
    {
        // Identity
        var email = _userEmail ?? "";
        if (string.IsNullOrWhiteSpace(_userDisplayName))
            _userDisplayName = DeriveDisplayName(_authToken, email);

        _lblUser.Text = _userDisplayName ?? "";
        _lblUser.Visible = !string.IsNullOrWhiteSpace(_lblUser.Text);

        _lblUserEmail.Text = email;
        _lblUserEmail.Visible = !string.IsNullOrWhiteSpace(_lblUserEmail.Text);

        // Tokens
        var tbs = TokenBalanceService.Instance;
        _lblTokensTotal.Text = $"Tokens: {tbs.TotalTokens}";
        _lblTokensTotal.Visible = true;

        // Promo token line is always shown for clarity ("Promo: 0" when none).
        _lblTokensPromo.Text = $"Promo: {tbs.PromoTokens}";
        _lblTokensPromo.Visible = true;
        _lblTokensPromo.ForeColor = tbs.PromoTokens > 0 ? WARNING : TEXT_DIM;

        // Promo expiry (separate line; date-only). If promo tokens exist but there's no expiry,
        // display "No expiry" so operators aren't guessing.
        var promoExp = tbs.PromoExpiresAt;
        if (tbs.PromoTokens > 0)
        {
            _lblPromoExpiry.Visible = true;

            if (promoExp.HasValue)
            {
                _lblPromoExpiry.Text = $"Promo exp: {promoExp.Value:yyyy-MM-dd}";

                // If expiring within 7 days, escalate to red.
                var daysLeft = (promoExp.Value.ToUniversalTime() - DateTime.UtcNow).TotalDays;
                _lblPromoExpiry.ForeColor = daysLeft <= 7 ? DANGER : TEXT_DIM;
            }
            else
            {
                _lblPromoExpiry.Text = "Promo exp: No expiry";
                _lblPromoExpiry.ForeColor = TEXT_DIM;
            }
        }
        else
        {
            _lblPromoExpiry.Visible = false;
            _lblPromoExpiry.Text = "";
        }

        // License line
        if (LicenseService.Instance.IsLicensed)
        {
            var typ = LicenseService.Instance.LicenseType ?? "active";
            _lblLicense.Text = $"License: {typ}";
            _lblLicense.ForeColor = SUCCESS;
        }
        else if (!string.IsNullOrWhiteSpace(LicenseService.Instance.LicenseKey))
        {
            _lblLicense.Text = "License: Attention needed";
            _lblLicense.ForeColor = WARNING;
        }
        else
        {
            var available = LicenseService.Instance.AccountLicenseCount;
            _lblLicense.Text = available > 0
                ? $"License: Not activated ({available} available)"
                : "License: Not activated";
            _lblLicense.ForeColor = TEXT_DIM;
        }
        _lblLicense.Visible = true;

        _btnLogout.Visible = true;
        return;
    }

    // Logged out
    _lblTokensTotal.Visible = false;
    _lblTokensPromo.Visible = false;
    _lblPromoExpiry.Visible = false;
    _lblLicense.Text = "";
    _lblLicense.Visible = false;
    _lblUser.Visible = false;
    _lblUserEmail.Visible = false;
    _btnLogout.Visible = false;
}

        private async System.Threading.Tasks.Task<bool> PromptAuthModalAsync()
        {
            // Modal auth flow:
            // 1) Google SSO (token mode)
            // 2) License key activation (license-only mode)

            while (true)
            {
                using var login = new GoogleLoginForm();
                var dr = login.ShowDialog(this);

                if (dr == DialogResult.OK && !string.IsNullOrWhiteSpace(login.AuthToken))
                {
                    await CompleteLoginAsync(login.AuthToken ?? "", login.RefreshToken ?? "", login.UserEmail ?? "");
                    ShowMain();
                    ApplyAuthHeader();
                    return true;
                }

                if (dr == DialogResult.Retry)
                {
                    using var lic = new LicenseActivationForm();
                    var lr = lic.ShowDialog(this);

                    if (lr == DialogResult.OK)
                    {
                        try { await LicenseService.Instance.ValidateAsync(); } catch { /* best effort */ }
                        ShowMain();
                        ApplyAuthHeader();
                        return true;
                    }

                    if (lr == DialogResult.Retry)
                    {
                        // Back to Google sign-in
                        continue;
                    }
                }

                // Cancelled / closed
                ClearSession();
                ApplyAuthHeader();
                ShowLogin();

                // Fallback surface: show embedded login panel so they can re-try without restarting.
                _loginPanel.Visible = true;
                CenterLoginPanel();
                return false;
            }
        }

        // Backward-compatible wrapper (some legacy buttons call this).
        private async System.Threading.Tasks.Task PromptLoginModalAsync()
        {
            await PromptAuthModalAsync();
        }

        private async System.Threading.Tasks.Task CompleteLoginAsync(string authToken, string refreshToken, string email)
        {
            var startTime = DateTime.Now;
            
            _authToken = authToken ?? "";
            _refreshToken = refreshToken ?? "";
            _userEmail = email ?? "";

            _userDisplayName = string.IsNullOrWhiteSpace(_userDisplayName)
                ? DeriveDisplayName(_authToken, _userEmail)
                : _userDisplayName;

            SaveSession();

            // Set auth context for downstream services
            ProActivityLogger.Instance.SetAuthContext(_authToken, _userEmail);
            TokenBalanceService.Instance.SetAuthContext(_authToken, _userEmail);
            IncodeService.Instance.SetAuthContext(_authToken, _userEmail);
            LicenseService.Instance.SetAuthContext(_authToken, _userEmail, _userDisplayName);

            // Strict license validation now has the Bearer token
            try { await LicenseService.Instance.ValidateAsync(); } catch { /* best effort */ }

            // Fetch account licenses (masked keys only) to drive UI selection without leaking full keys.
            try
            {
                var lr = await LicenseService.Instance.RefreshAccountLicensesAsync();
                if (lr.Success)
                {
                    if (!LicenseService.Instance.IsLicensed && string.IsNullOrWhiteSpace(LicenseService.Instance.LicenseKey) && lr.Count > 0)
                        Log("info", $"{lr.Count} license(s) found in your account. Click 'License' to activate on this machine.");
                }
            }
            catch { /* best effort */ }

            await RefreshTokenBalanceAsync();
            
            // Log successful login
            ProActivityLogger.Instance.LogLogin(email ?? "unknown", true, null, (int)(DateTime.Now - startTime).TotalMilliseconds);
            Log("success", $"Logged in as {email}");
        }

        private async System.Threading.Tasks.Task RefreshTokenBalanceAsync()
        {
            if (!IsLoggedIn) return;

            try
            {
                TokenBalanceService.Instance.SetAuthContext(_authToken, _userEmail);
                await TokenBalanceService.Instance.RefreshBalanceAsync();
                _tokenBalance = TokenBalanceService.Instance.TotalTokens;
            }
            catch (Exception ex)
            {
                Log("warning", $"Token balance refresh failed: {ex.Message}");
                // Keep current value (0). UI still hides tokens when logged out.
            }
        }

        private async void DoLogin()
        {
            // Email/password sign-in is centralized in the modal login dialog for now.
            await PromptLoginModalAsync();
            ApplyAuthHeader();
        }

        private async void BtnGoogle_Click(object? sender, EventArgs e)
        {
            await PromptLoginModalAsync();
            ApplyAuthHeader();
        }

        private async void Logout()
        {
            // Log logout before clearing session
            if (!string.IsNullOrEmpty(_userEmail))
            {
                ProActivityLogger.Instance.LogLogout(_userEmail);
            }
            
            // ClearSession already clears auth context across services.
            ClearSession();
            ApplyAuthHeader();

            // Optional: immediately present the modal login again (switch user).
            await PromptLoginModalAsync();
            ApplyAuthHeader();
        }

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
            // If no cached scan results, run a quick scan.
            if (_devices.Count == 0)
            {
                try { _devices = J2534DeviceScanner.ScanForDevices(); } catch { /* ignore */ }
            }

            if (_devices.Count == 0)
            {
                MessageBox.Show("No J2534 devices detected. Click 'Scan Devices' and ensure your interface driver is installed.");
                return;
            }

            // --- Device selection ---
            J2534DeviceInfo device;
            string? probeVin = null;
            double probeVoltage = 0;
            string probeStatus = "";

            if (_devices.Count == 1)
            {
                device = _devices[0];
            }
            else
            {
                using var dlg = new DeviceSelectForm(_devices, TimeSpan.FromSeconds(20));
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Selection == null)
                    return;

                device = dlg.Selection.Device;
                probeVin = dlg.Selection.Vin;
                probeVoltage = dlg.Selection.Voltage;
                probeStatus = dlg.Selection.Status;
            }

            // Sync dropdown selection (visual consistency)
            try
            {
                var matchIdx = _devices.FindIndex(d => d.FunctionLibrary == device.FunctionLibrary);
                if (matchIdx >= 0)
                    _cmbDevices.SelectedIndex = matchIdx;
            }
            catch { /* ignore */ }

            // Pre-connect banner (what we are about to use)
            if (_lblDeviceBanner != null)
            {
                _lblDeviceBanner.Text = _devices.Count > 1
                    ? $"Selected Device: {device.Name} ({device.Vendor}) — {probeStatus}".Trim()
                    : $"Selected Device: {device.Name} ({device.Vendor})";
            }

var startTime = DateTime.Now;

            try
            {
                var result = await J2534Service.Instance.ConnectDeviceAsync(device);
                
                if (result.Success)
                {
                    _isConnected = true;
                    _lblStatus.Text = "Status: Connected";
                    _lblStatus.ForeColor = SUCCESS;
                    if (_lblDeviceBanner != null) _lblDeviceBanner.Text = $"Selected Device: {device.Name} ({device.Vendor}) — Connected";
                    Log("success", $"Connected to {device.Name}");

                    // If the probe provided VIN/VBATT, surface it immediately (VIN may be unavailable).
                    if (!string.IsNullOrWhiteSpace(probeVin))
                    {
                        _lblVin.Text = $"VIN: {probeVin}";
                        _lblVin.ForeColor = SUCCESS;
                    }
                    else
                    {
                        _lblVin.Text = "VIN: —————————————————";
                        _lblVin.ForeColor = TEXT_DIM;
                    }
                    
                    ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
                    {
                        Action = "device_connect",
                        ActionCategory = "diagnostics",
                        Success = true,
                        TokenChange = 0, // FREE
                        Details = $"Connected to {device.Name}",
                        ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds,
                        Metadata = new { deviceName = device.Name, vendor = device.Vendor, probeVin = probeVin, probeVoltage = probeVoltage }
                    });
                }
                else
                {
                    _lblStatus.Text = "Status: Connection Failed";
                    _lblStatus.ForeColor = DANGER;
                    if (_lblDeviceBanner != null) _lblDeviceBanner.Text = $"Selected Device: {device.Name} ({device.Vendor}) — Connection Failed";
                    Log("error", $"Failed: {result.Error}");
                    
                    ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
                    {
                        Action = "device_connect",
                        ActionCategory = "diagnostics",
                        Success = false,
                        TokenChange = 0,
                        ErrorMessage = result.Error,
                        Details = $"Failed to connect to {device.Name}",
                        ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds
                    });
                }
            }
            catch (Exception ex)
            {
                ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
                {
                    Action = "device_connect",
                    ActionCategory = "diagnostics",
                    Success = false,
                    ErrorMessage = ex.Message,
                    ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds
                });
                
                ShowError("Connect Failed", "Could not connect to device", ex);
            }
        }

        private async void BtnReadVin_Click(object? s, EventArgs e)
{
    if (!_isConnected)
    {
        MessageBox.Show("Connect to a device first");
        return;
    }

    var startTime = DateTime.Now;

    try
    {
        var result = await J2534Service.Instance.ReadVehicleInfoAsync();
        if (result.Success && !string.IsNullOrEmpty(result.Vin))
        {
            _lblVin.Text = $"VIN: {result.Vin}";
            _lblVin.ForeColor = SUCCESS;
            Log("success", $"VIN: {result.Vin}");
            
            ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
            {
                Action = "read_vin",
                ActionCategory = "diagnostics",
                Vin = result.Vin,
                Success = true,
                TokenChange = 0, // FREE
                ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds
            });
        }
        else
        {
            _lblVin.Text = "VIN: Could not read";
            _lblVin.ForeColor = DANGER;
            Log("error", result.Error ?? "Failed to read VIN");
            
            ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
            {
                Action = "read_vin",
                ActionCategory = "diagnostics",
                Success = false,
                TokenChange = 0,
                ErrorMessage = result.Error ?? "Failed to read VIN",
                ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds
            });
        }
    }
    catch (Exception ex)
    {
        ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
        {
            Action = "read_vin",
            ActionCategory = "diagnostics",
            Success = false,
            ErrorMessage = ex.Message,
            ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds
        });
        
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

            var startTime = DateTime.Now;
            string? vin = null;
            string? vehicleYear = null;
            string? vehicleModel = null;

            try
            {
                // Extract vehicle info from UI
                vin = _lblVin.Text.Replace("VIN:", "").Replace("—", "").Trim();
                if (string.IsNullOrWhiteSpace(vin)) vin = null;
                vehicleModel = _cmbVehicles.SelectedItem?.ToString();

                // Step 1: Read outcode from vehicle if not already present
                if (string.IsNullOrEmpty(_txtOutcode.Text))
                {
                    Log("info", "Reading outcode from vehicle...");
                    var outcodeResult = await J2534Service.Instance.ReadOutcodeAsync();
                    if (!outcodeResult.Success || string.IsNullOrEmpty(outcodeResult.Outcode))
                    {
                        Log("error", outcodeResult.Error ?? "Failed to read outcode");
                        return;
                    }
                    _txtOutcode.Text = outcodeResult.Outcode;
                    Log("success", $"Outcode read: {Utils.SecretRedactor.MaskOutcode(outcodeResult.Outcode)}");
                }

                // Step 2: Call provider-router to get incode (this charges 1 token)
                Log("info", "Calculating incode via provider-router [1 TOKEN]...");
                
                var incodeResult = await Services.IncodeService.Instance.CalculateIncodeAsync(
                    _txtOutcode.Text, 
                    vin,
                    "BCM" // Default to BCM for main form
                );

                if (!incodeResult.Success || string.IsNullOrEmpty(incodeResult.Incode))
                {
                    Log("error", incodeResult.Error ?? "Failed to calculate incode");
                    
                    // Log failure
                    ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
                    {
                        Action = "get_incode",
                        ActionCategory = "key_programming",
                        Vin = vin,
                        VehicleYear = vehicleYear,
                        VehicleModel = vehicleModel,
                        Success = false,
                        TokenChange = 0,
                        ErrorMessage = incodeResult.Error ?? "Failed to calculate incode",
                        ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds
                    });
                    
                    MessageBox.Show(
                        $"Failed to calculate incode:\n\n{incodeResult.Error}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                // Store in UI field ONLY (never to disk/settings)
                _txtIncode.Text = incodeResult.Incode;
                Log("success", $"Incode received (Provider: {incodeResult.ProviderUsed}, Tokens: {incodeResult.TokensCharged})");

                // Log success
                ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
                {
                    Action = "get_incode",
                    ActionCategory = "key_programming",
                    Vin = vin,
                    VehicleYear = vehicleYear,
                    VehicleModel = vehicleModel,
                    Success = true,
                    TokenChange = -incodeResult.TokensCharged,
                    ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds,
                    Details = $"Provider: {incodeResult.ProviderUsed}",
                    Metadata = new { provider = incodeResult.ProviderUsed, tokensRemaining = incodeResult.TokensRemaining }
                });

                // Step 3: Initialize BcmSessionManager with UDS service
                var uds = J2534Service.Instance.GetUdsService();
                if (uds != null)
                {
                    BcmSessionManager.Instance.Initialize(uds);
                }

                // Step 4: AUTO-UNLOCK SEQUENCE
                Log("info", "Starting BCM auto-unlock sequence...");
                var unlockResult = await BcmSessionManager.Instance.UnlockBcmAsync(
                    _txtOutcode.Text,
                    incodeResult.Incode
                );

                if (unlockResult.IsSuccess)
                {
                    Log("success", "✓ BCM unlocked - Key functions enabled");
                    
                    // Log BCM unlock success
                    ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
                    {
                        Action = "bcm_unlock",
                        ActionCategory = "key_programming",
                        Vin = vin,
                        VehicleYear = vehicleYear,
                        VehicleModel = vehicleModel,
                        Success = true,
                        TokenChange = 0, // FREE - included with incode
                        Details = "BCM unlocked, key functions enabled"
                    });
                    
                    MessageBox.Show(
                        $"BCM Unlocked Successfully!\n\n" +
                        $"Provider: {incodeResult.ProviderUsed}\n" +
                        $"Tokens charged: {incodeResult.TokensCharged}\n\n" +
                        $"Key functions are now enabled:\n" +
                        $"• Program Keys\n" +
                        $"• Erase Keys\n" +
                        $"• Key Counters",
                        "BCM Unlocked",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                else
                {
                    Log("warning", $"Incode received but BCM unlock failed: {unlockResult.Error}");
                    
                    // Log BCM unlock failure
                    ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
                    {
                        Action = "bcm_unlock",
                        ActionCategory = "key_programming",
                        Vin = vin,
                        VehicleYear = vehicleYear,
                        VehicleModel = vehicleModel,
                        Success = false,
                        TokenChange = 0,
                        ErrorMessage = unlockResult.Error
                    });
                    
                    MessageBox.Show(
                        $"Incode calculated but BCM unlock failed:\n\n{unlockResult.Error}\n\n" +
                        $"The incode is saved - you can retry the unlock manually.",
                        "Unlock Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }
            catch (Exception ex)
            {
                // Log exception
                ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
                {
                    Action = "get_incode",
                    ActionCategory = "key_programming",
                    Vin = vin,
                    Success = false,
                    TokenChange = 0,
                    ErrorMessage = ex.Message,
                    ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds
                });
                
                ShowError("Incode Failed", "Could not calculate incode", ex);
            }
        }
        #endregion

        #region PATS - Using J2534Service Singleton
private async void BtnProgram_Click(object? s, EventArgs e)
{
    if (!_isConnected)
    {
        MessageBox.Show("Connect to a device first");
        return;
    }

    var ic = _txtIncode.Text.Trim();
    if (string.IsNullOrEmpty(ic))
    {
        MessageBox.Show("Enter incode first");
        return;
    }

    // Extract vehicle info
    var vin = _lblVin.Text.Replace("VIN:", "").Replace("—", "").Trim();
    if (string.IsNullOrWhiteSpace(vin)) vin = null;
    var vehicleModel = _cmbVehicles.SelectedItem?.ToString();

    if (!await ConfirmAsync(1, "Program Key"))
        return;

    try
    {
        Log("info", "Programming key...");

        // 1) Unlock PATS security using the provided incode
        var unlock = await J2534Service.Instance.SubmitIncodeAsync("PCM", ic);
        if (!unlock.Success)
        {
            var msg = unlock.Error ?? "Incode rejected";
            Log("error", msg);
            
            ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
            {
                Action = "program_key",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleModel = vehicleModel,
                Success = false,
                TokenChange = 0,
                ErrorMessage = msg
            });
            
            MessageBox.Show($"Incode rejected: {msg}");
            return;
        }

        // 2) Determine next slot based on current key count
        var kc = await J2534Service.Instance.ReadKeyCountAsync();
        int current = kc.Success ? kc.KeyCount : 0;
        int max = kc.Success ? kc.MaxKeys : 8;

        if (current >= max)
        {
            var msg = $"Max keys already programmed ({current}/{max}). Erase keys first if you need to add a new one.";
            Log("warning", msg);
            MessageBox.Show(msg);
            _lblKeys.Text = current.ToString();
            return;
        }

        int nextSlot = current + 1;

        // 3) Perform the actual key programming operation
        var prog = await J2534Service.Instance.ProgramKeyAsync(ic, nextSlot);
        if (prog.Success)
        {
            _lblKeys.Text = prog.CurrentKeyCount.ToString();
            MessageBox.Show($"Key programmed successfully!\n\nKeys now: {prog.CurrentKeyCount}\n\nRemove key, insert next key, and click Program again.");
            Log("success", $"Key programmed (slot {nextSlot}, total: {prog.CurrentKeyCount})");
            
            ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
            {
                Action = "program_key",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleModel = vehicleModel,
                Success = true,
                TokenChange = 0, // FREE - session already charged
                Details = $"Key programmed to slot {nextSlot}, total keys: {prog.CurrentKeyCount}",
                Metadata = new { slot = nextSlot, totalKeys = prog.CurrentKeyCount }
            });
        }
        else
        {
            var msg = prog.Error ?? "Programming failed";
            Log("error", msg);
            
            ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
            {
                Action = "program_key",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleModel = vehicleModel,
                Success = false,
                TokenChange = 0,
                ErrorMessage = msg
            });
            
            MessageBox.Show($"Programming failed: {msg}");
        }
    }
    catch (Exception ex)
    {
        ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
        {
            Action = "program_key",
            ActionCategory = "key_programming",
            Vin = vin,
            Success = false,
            ErrorMessage = ex.Message
        });
        
        ShowError("Program Failed", "Could not program key", ex);
    }
}
private async void BtnErase_Click(object? s, EventArgs e)
{
    if (!_isConnected)
    {
        MessageBox.Show("Connect to a device first");
        return;
    }

    if (!await ConfirmAsync(1, "Erase All Keys"))
        return;

    if (MessageBox.Show("WARNING: This will ERASE ALL KEYS!\n\nAre you absolutely sure?", "Confirm Erase", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        return;

    var ic = _txtIncode.Text.Trim();
    if (string.IsNullOrEmpty(ic))
    {
        MessageBox.Show("Enter incode first");
        return;
    }

    // Extract vehicle info
    var vin = _lblVin.Text.Replace("VIN:", "").Replace("—", "").Trim();
    if (string.IsNullOrWhiteSpace(vin)) vin = null;
    var vehicleModel = _cmbVehicles.SelectedItem?.ToString();

    try
    {
        Log("info", "Erasing all keys...");

        // 1) Unlock PATS security using the provided incode
        var unlock = await J2534Service.Instance.SubmitIncodeAsync("PCM", ic);
        if (!unlock.Success)
        {
            var msg = unlock.Error ?? "Incode rejected";
            Log("error", msg);
            
            ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
            {
                Action = "erase_keys",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleModel = vehicleModel,
                Success = false,
                TokenChange = 0,
                ErrorMessage = msg
            });
            
            MessageBox.Show($"Incode rejected: {msg}");
            return;
        }

        // 2) Perform the actual erase operation
        var erase = await J2534Service.Instance.EraseAllKeysAsync(ic);
        if (erase.Success)
        {
            _lblKeys.Text = erase.CurrentKeyCount.ToString();
            MessageBox.Show($"All keys erased!\n\nKeys remaining: {erase.CurrentKeyCount}");
            Log("success", $"Keys erased (remaining: {erase.CurrentKeyCount})");
            
            ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
            {
                Action = "erase_keys",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleModel = vehicleModel,
                Success = true,
                TokenChange = 0, // FREE - session already charged
                Details = $"All keys erased, remaining: {erase.CurrentKeyCount}",
                Metadata = new { keysRemaining = erase.CurrentKeyCount }
            });
        }
        else
        {
            var msg = erase.Error ?? "Erase failed";
            Log("error", msg);
            
            ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
            {
                Action = "erase_keys",
                ActionCategory = "key_programming",
                Vin = vin,
                VehicleModel = vehicleModel,
                Success = false,
                TokenChange = 0,
                ErrorMessage = msg
            });
            
            MessageBox.Show($"Erase failed: {msg}");
        }
    }
    catch (Exception ex)
    {
        ProActivityLogger.Instance.LogActivity(new ActivityLogEntry
        {
            Action = "erase_keys",
            ActionCategory = "key_programming",
            Vin = vin,
            Success = false,
            ErrorMessage = ex.Message
        });
        
        ShowError("Erase Failed", "Could not erase keys", ex);
    }
}

        private async void BtnParam_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            try
            {
                Log("info", "Parameter reset...");
                var result = await J2534Service.Instance.RestoreBcmDefaultsAsync();
                if (result.Success)
                {
                    MessageBox.Show("Parameter reset complete!\n\nTurn ignition OFF and wait 15 seconds.");
                    Log("success", "Parameter reset done");
                }
                else
                {
                    Log("error", result.Error ?? "Reset failed");
                }
            }
            catch (Exception ex)
            {
                ShowError("Reset Failed", "Could not reset parameters", ex);
            }
        }

        private async void BtnEscl_Click(object? s, EventArgs e)
        {
            if (!_isConnected)
            {
                MessageBox.Show("Connect to a device first");
                return;
            }

            if (!await ConfirmAsync(1, "ESCL Initialize"))
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
            if (!await ConfirmAsync(1, "Clear P160A")) return;
            
            try
            {
                var result = await J2534Service.Instance.ClearP160AAsync();
                if (result.Success)
                {
                    MessageBox.Show("P160A cleared (targeted)");
                    Log("success", "P160A cleared (targeted)");
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
            if (!await ConfirmAsync(1, "Clear B10A2")) return;
            
            try
            {
                var result = await J2534Service.Instance.ClearB10A2Async();
                if (result.Success)
                {
                    MessageBox.Show("B10A2 cleared (targeted)");
                    Log("success", "B10A2 cleared (targeted)");
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
            if (!await ConfirmAsync(1, "Clear Crash Event")) return;
            
            try
            {
                var result = await J2534Service.Instance.ClearCrashFlagAsync();
                if (result.Success)
                {
                    MessageBox.Show("Crash event cleared!");
                    Log("success", "Crash event cleared");
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
            
            try
            {
                var gw = await J2534Service.Instance.CheckGatewayAsync();
                if (!gw.Success || !gw.HasGateway)
                {
                    MessageBox.Show("No gateway detected");
                    return;
                }

                if (!await ConfirmAsync(1, "Unlock Gateway")) return;

                var ic = _txtIncode.Text.Trim();
                if (string.IsNullOrEmpty(ic))
                {
                    MessageBox.Show("Enter incode first");
                    return;
                }

                var result = await J2534Service.Instance.SubmitIncodeAsync("GWM", ic);
                if (result.Success)
                {
                    MessageBox.Show("Gateway unlocked!");
                    Log("success", "Gateway unlocked");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
        }

        private async void BtnKeypad_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }

            var r = MessageBox.Show(
                "YES = Read keypad code\nNO = Write a new 5-digit code (digits 1-9 only)",
                "Keypad Code",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (r == DialogResult.Cancel) return;

            if (r == DialogResult.Yes)
            {
                if (!await ConfirmAsync(1, "Read Keypad")) return;
                try
                {
                    var result = await J2534Service.Instance.ReadKeypadCodeAsync();
                    if (result.Success)
                    {
                        MessageBox.Show($"Keypad Code: {result.Code}\n\nTip: write it down before you close this window.", "Keypad Code", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Log("success", $"Keypad read: {result.Code}");
                    }
                    else
                    {
                        Log("error", result.Error ?? "Keypad read failed");
                    }
                }
                catch (Exception ex) { ShowError("Error", "Failed", ex); }
            }
            else
            {
                // Writing keypad is a BCM write operation — require a safety checklist.
                if (!SafetyChecklistForm.Show(
                    this,
                    "Write Keypad Code",
                    "Writes a new 5-digit keypad/door code to the BCM.",
                    new[]
                    {
                        "Battery support/charger connected (stable voltage)",
                        "Ignition ON, engine OFF",
                        "No other diagnostic tools connected",
                        "I understand an incorrect code may lock me out"
                    }))
                    return;

                using var prompt = new KeypadCodeInputForm();
                if (prompt.ShowDialog(this) != DialogResult.OK) return;

                var nc = prompt.Code;
                if (string.IsNullOrWhiteSpace(nc)) return;

                if (!await ConfirmAsync(1, "Write Keypad")) return;
                try
                {
                    var result = await J2534Service.Instance.WriteKeypadCodeAsync(nc);
                    if (result.Success)
                    {
                        MessageBox.Show($"Keypad code written: {nc}", "Keypad Code", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Log("success", $"Keypad write: {nc}");
                    }
                    else
                    {
                        Log("error", result.Error ?? "Keypad write failed");
                    }
                }
                catch (Exception ex) { ShowError("Error", "Failed", ex); }
            }
        }

        private async void BtnBcm_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            
            if (MessageBox.Show("WARNING: This will reset ALL BCM settings to factory defaults!\n\nAre you sure?", "BCM Factory Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            if (!await ConfirmAsync(1, "BCM Factory Reset")) return;

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

            // KAM clear is a write/routine operation. Require an explicit safety checklist.
            if (!SafetyChecklistForm.Show(
                this,
                "Clear KAM",
                "Clears PCM Keep-Alive Memory (adaptive learned values).",
                new[]
                {
                    "Battery support/charger connected (stable voltage)",
                    "Ignition ON, engine OFF",
                    "No other diagnostic tools connected",
                    "I understand this changes PCM learned values"
                }))
                return;
            
            try
            {
                var result = await J2534Service.Instance.ClearKAMAsync();
                if (result.Success)
                {
                    MessageBox.Show("KAM cleared (proper routine)");
                    Log("success", "KAM cleared (proper routine)");
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

        // === PHASE 2: ADVANCED TOOLS HANDLERS ===
        private void BtnTargets_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect to vehicle first"); return; }
            try
            {
                var uds = J2534Service.Instance.GetUdsService();
                var vin = _lblVin.Text.Replace("VIN: ", "").Trim();
                if (uds == null) { MessageBox.Show("UDS service not available"); return; }
                using var form = new Forms.TargetsForm(uds, vin);
                form.ShowDialog(this);
            }
            catch (Exception ex) { ShowError("Error", "Failed to open Targets form", ex); }
        }

        private void BtnKeyCounters_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect to vehicle first"); return; }
            try
            {
                var uds = J2534Service.Instance.GetUdsService();
                var vin = _lblVin.Text.Replace("VIN: ", "").Trim();
                if (uds == null) { MessageBox.Show("UDS service not available"); return; }
                using var form = new Forms.KeyCountersForm(uds, vin);
                form.ShowDialog(this);
            }
            catch (Exception ex) { ShowError("Error", "Failed to open Key Counters form", ex); }
        }

        private void BtnEngineering_Click(object? s, EventArgs e)
        {
            if (!_isConnected) { MessageBox.Show("Connect to vehicle first"); return; }
            var result = MessageBox.Show(
                "⚠️ ENGINEERING MODE WARNING ⚠️\n\n" +
                "This mode provides direct access to vehicle modules.\n" +
                "Incorrect usage can permanently damage modules.\n\n" +
                "Only use if you know exactly what you are doing.\n\n" +
                "Do you want to continue?",
                "Engineering Mode",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
            try
            {
                var uds = J2534Service.Instance.GetUdsService();
                var vin = _lblVin.Text.Replace("VIN: ", "").Trim();
                if (uds == null) { MessageBox.Show("UDS service not available"); return; }
                using var form = new Forms.EngineeringForm(uds, vin);
                form.ShowDialog(this);
            }
            catch (Exception ex) { ShowError("Error", "Failed to open Engineering form", ex); }
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
                // End BCM session
                BcmSessionManager.Instance.EndSession();
                _sessionTimerUpdate?.Stop();
                _sessionTimerUpdate?.Dispose();

                J2534Service.Instance.Disconnect();
                _logoImage?.Dispose();
                _logoImage = null;
            }
            catch { }

            base.OnFormClosing(e);
        }

        #region BCM Session UI Handlers

        /// <summary>
        /// Update BCM session panel UI when state changes
        /// </summary>
        private void OnBcmSessionStateChanged(BcmSessionState state)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnBcmSessionStateChanged(state)));
                return;
            }

            if (state.IsUnlocked)
            {
                _lblBcmStatus.Text = "🔓 BCM UNLOCKED";
                _lblBcmStatus.ForeColor = SUCCESS;
                _lblSessionTimer.Text = $"Session: {state.DurationDisplay}";
                _lblSessionTimer.ForeColor = Color.FromArgb(140, 200, 140);
                _lblSessionTimer.Visible = true;
                _lblKeepAlive.Visible = true;
                _lblKeepAlive.ForeColor = state.KeepAliveActive ? SUCCESS : WARNING;
            }
            else
            {
                _lblBcmStatus.Text = "🔒 BCM LOCKED";
                _lblBcmStatus.ForeColor = DANGER;
                _lblSessionTimer.Visible = false;
                _lblKeepAlive.Visible = false;
            }

            // Refresh panel border color
            _bcmSessionPanel.Invalidate();
        }

        /// <summary>
        /// Enable/disable unlock-dependent operations
        /// </summary>
        private void OnUnlockOperationsEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnUnlockOperationsEnabled(enabled)));
                return;
            }

            _btnProgram.Enabled = enabled;
            _btnErase.Enabled = enabled;
            _btnKeyCounters.Enabled = enabled;

            // Update button appearance based on enabled state
            if (enabled)
            {
                // Enabled: Vibrant colors with white text
                _btnProgram.BackColor = SUCCESS;
                _btnProgram.ForeColor = Color.White;
                _btnProgram.FlatAppearance.BorderColor = SUCCESS;
                _btnProgram.Cursor = Cursors.Hand;

                _btnErase.BackColor = Color.FromArgb(220, 60, 60);
                _btnErase.ForeColor = Color.White;
                _btnErase.FlatAppearance.BorderColor = Color.FromArgb(220, 60, 60);
                _btnErase.Cursor = Cursors.Hand;

                _btnKeyCounters.BackColor = ACCENT;
                _btnKeyCounters.ForeColor = Color.White;
                _btnKeyCounters.FlatAppearance.BorderColor = ACCENT;
                _btnKeyCounters.Cursor = Cursors.Hand;
            }
            else
            {
                // Disabled: Muted appearance
                var disabledBg = Color.FromArgb(45, 55, 70);
                var disabledFg = Color.FromArgb(100, 110, 125);
                var disabledBorder = Color.FromArgb(60, 70, 85);

                _btnProgram.BackColor = disabledBg;
                _btnProgram.ForeColor = disabledFg;
                _btnProgram.FlatAppearance.BorderColor = disabledBorder;
                _btnProgram.Cursor = Cursors.Arrow;

                _btnErase.BackColor = disabledBg;
                _btnErase.ForeColor = disabledFg;
                _btnErase.FlatAppearance.BorderColor = disabledBorder;
                _btnErase.Cursor = Cursors.Arrow;

                _btnKeyCounters.BackColor = disabledBg;
                _btnKeyCounters.ForeColor = disabledFg;
                _btnKeyCounters.FlatAppearance.BorderColor = disabledBorder;
                _btnKeyCounters.Cursor = Cursors.Arrow;
            }
        }

        /// <summary>
        /// Update session timer display every second
        /// </summary>
        private void UpdateSessionTimerDisplay()
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(UpdateSessionTimerDisplay)); } catch { }
                return;
            }

            var state = BcmSessionManager.Instance.GetState();
            if (state.IsUnlocked)
            {
                _lblSessionTimer.Text = $"Session: {state.DurationDisplay}";
            }
        }

        #endregion
    }
}
