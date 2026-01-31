using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
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
        private string _userEmail = "", _authToken = "";
        private int _tokenBalance = 0;
        private List<J2534DeviceInfo> _devices = new();
        private bool _isConnected = false;
        private int _activeTab = 0;
        private bool _didAutoStart = false;

        // Assets
        private Image? _logoImage;

        // Controls
        private Panel _header = null!, _tabBar = null!, _content = null!, _logPanel = null!, _loginPanel = null!;
        private Panel _patsTab = null!, _diagTab = null!, _freeTab = null!;
        private Button _btnTab1 = null!, _btnTab2 = null!, _btnTab3 = null!, _btnLogout = null!;
        private Label _lblTokens = null!, _lblUser = null!, _lblStatus = null!, _lblVin = null!, _lblKeys = null!;
        private ComboBox _cmbDevices = null!, _cmbVehicles = null!;
        private TextBox _txtOutcode = null!, _txtIncode = null!, _txtEmail = null!, _txtPassword = null!;
        private RichTextBox _txtLog = null!;

        // DPI helpers (keeps runtime-created controls scaling-friendly)
        private int Dpi(int px) => (int)Math.Round(px * (DeviceDpi / 96f));
        private Padding DpiPad(int l, int t, int r, int b) => new Padding(Dpi(l), Dpi(t), Dpi(r), Dpi(b));


        public MainForm()
        {
            InitializeComponent();
            ApplyDarkTitleBar();
            BuildUI();
            LoadSession();

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

            sec1.Controls.Add(row1);
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

            var btnCopy = AutoBtn("Copy", BTN_BG);
            btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtOutcode.Text)) Clipboard.SetText(_txtOutcode.Text); };
            row3.Controls.Add(btnCopy);

            row3.Controls.Add(new Label { Text = "INCODE:", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = TEXT, AutoSize = true, Margin = DpiPad(30, 12, 10, 0) });
            
            _txtIncode = MakeTextBox(160);
            _txtIncode.Margin = DpiPad(0, 6, 15, 0);
            row3.Controls.Add(_txtIncode);

            var btnGetIncode = AutoBtn("Get Incode", ACCENT);
            btnGetIncode.Click += BtnGetIncode_Click;
            row3.Controls.Add(btnGetIncode);

            sec3.Controls.Add(row3);
            layout.Controls.Add(sec3, 0, 2);
            layout.SetColumnSpan(sec3, 2);

            // === SECTION 4: KEY PROGRAMMING ===
            var sec4 = Section("KEY PROGRAMMING");
            var row4 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
            
            var btnProg = AutoBtn("Program Key", SUCCESS);
            btnProg.Click += BtnProgram_Click;
            row4.Controls.Add(btnProg);

            var btnErase = AutoBtn("Erase All Keys", DANGER);
            btnErase.Click += BtnErase_Click;
            row4.Controls.Add(btnErase);

            var btnParam = AutoBtn("Parameter Reset", WARNING);
            btnParam.Click += BtnParam_Click;
            row4.Controls.Add(btnParam);

            var btnEscl = AutoBtn("ESCL Initialize", BTN_BG);
            btnEscl.Click += BtnEscl_Click;
            row4.Controls.Add(btnEscl);

            var btnDisable = AutoBtn("Disable BCM Security", BTN_BG);
            btnDisable.Click += BtnDisable_Click;
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
            var d3 = AutoBtn("Clear Crush Event", BTN_BG); d3.Click += BtnCrush_Click; r1.Controls.Add(d3);
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
        private bool Confirm(int cost, string op) { if (cost == 0) return true; if (_tokenBalance < cost) { MessageBox.Show($"Need {cost} tokens"); return false; } return MessageBox.Show($"{op}\nCost: {cost} token(s)\n\nProceed?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes; }
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

            try
            {
                Log("info", "Programming key...");
                var result = await J2534Service.Instance.SubmitIncodeAsync("PCM", ic);
                if (result.Success)
                {
                    MessageBox.Show("Key programmed successfully!\n\nRemove key, insert next key, and click Program again.");
                    Log("success", "Key programmed");
                }
                else
                {
                    Log("error", result.Error ?? "Programming failed");
                }
            }
            catch (Exception ex)
            {
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

            if (!Confirm(1, "Erase All Keys"))
                return;

            if (MessageBox.Show("WARNING: This will ERASE ALL KEYS!\n\nAre you absolutely sure?", "Confirm Erase", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var ic = _txtIncode.Text.Trim();
            if (string.IsNullOrEmpty(ic))
            {
                MessageBox.Show("Enter incode first");
                return;
            }

            try
            {
                Log("info", "Erasing all keys...");
                var result = await J2534Service.Instance.SubmitIncodeAsync("PCM", ic);
                if (result.Success)
                {
                    MessageBox.Show("All keys erased!");
                    Log("success", "Keys erased");
                }
                else
                {
                    Log("error", result.Error ?? "Erase failed");
                }
            }
            catch (Exception ex)
            {
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
            
            try
            {
                var gw = await J2534Service.Instance.CheckGatewayAsync();
                if (!gw.Success || !gw.HasGateway)
                {
                    MessageBox.Show("No gateway detected");
                    return;
                }

                if (!Confirm(1, "Unlock Gateway")) return;

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
            if (!_isConnected) { MessageBox.Show("Connect first"); return; }
            
            try
            {
                var result = await J2534Service.Instance.ReadVehicleInfoAsync();
                if (result.Success && result.VehicleInfo != null)
                {
                    const string NA = "N/A";
                    var info = $"VIN: {result.Vin ?? NA}\n" +
                               $"Year: {(result.VehicleInfo?.Year.ToString() ?? NA)}\n" +
                               $"Model: {result.VehicleInfo?.Model ?? NA}\n" +
                               $"Battery: {result.BatteryVoltage:F1}V";
                    MessageBox.Show(info, "Module Info");
                    Log("success", "Module info read");
                }
                else
                {
                    Log("error", result.Error ?? "Failed");
                }
            }
            catch (Exception ex) { ShowError("Error", "Failed", ex); }
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
