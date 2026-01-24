using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Utils;

namespace PatsKillerPro
{
    /// <summary>
    /// Professional OAuth login form matching PatsKiller Pro mockup v2
    /// Features: Google OAuth primary, Email/password secondary, Token fetching after login
    /// VERSION: 2.1 - Added detailed API logging for debugging
    /// </summary>
    public class GoogleLoginForm : Form
    {
        // ============ UI CONTROLS ============
        private Panel _headerPanel = null!;
        private Panel _contentPanel = null!;
        private PictureBox _logoBox = null!;
        private Label _lblTitle = null!;
        private Label _lblSubtitle = null!;
        private Label _lblTokens = null!;
        private Label _lblStatus = null!;
        
        private Panel _loginPanel = null!;
        private Panel _waitingPanel = null!;
        private Panel _successPanel = null!;
        private Panel _errorPanel = null!;
        private Label? _lblWaitingStatus = null;

        // ============ DARK THEME COLORS ============
        private readonly Color _colorBackground = Color.FromArgb(30, 30, 30);
        private readonly Color _colorHeader = Color.FromArgb(37, 37, 38);
        private readonly Color _colorPanel = Color.FromArgb(45, 45, 48);
        private readonly Color _colorInput = Color.FromArgb(60, 60, 60);
        private readonly Color _colorBorder = Color.FromArgb(70, 70, 70);
        private readonly Color _colorText = Color.FromArgb(255, 255, 255);
        private readonly Color _colorTextDim = Color.FromArgb(150, 150, 150);
        private readonly Color _colorRed = Color.FromArgb(233, 69, 96);
        private readonly Color _colorGreen = Color.FromArgb(76, 175, 80);
        private readonly Color _colorGoogleBtn = Color.FromArgb(255, 255, 255);

        // ============ RESULTS ============
        public string? AuthToken { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? UserEmail { get; private set; }
        public int TokenCount { get; private set; }

        // ============ API CONFIGURATION ============
        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjacx.supabase.co";
        private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0.iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";
        private const string AUTH_PAGE_URL = "https://patskiller.com/desktop-auth?session=";

        // ============ STATE ============
        private CancellationTokenSource? _cts;
        private string? _currentSessionCode;
        private readonly HttpClient _httpClient;
        private readonly string _machineId;
        private int _pollFailureCount = 0;
        private const int MAX_POLL_FAILURES = 5;
        private const int POLL_INTERVAL_MS = 2000;
        private const int HTTP_TIMEOUT_SECONDS = 15;

        public GoogleLoginForm()
        {
            _machineId = GetMachineId();
            
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(HTTP_TIMEOUT_SECONDS)
            };
            
            InitializeComponent();
            ShowLoginState();
            
            Logger.Info($"[GoogleLoginForm] Initialized. MachineId: {_machineId}");
        }

        private static string GetMachineId()
        {
            try
            {
                var data = Environment.MachineName + Environment.UserName;
                using var sha = System.Security.Cryptography.SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
                return Convert.ToBase64String(hash)[..16];
            }
            catch
            {
                return Environment.MachineName;
            }
        }

        private void InitializeComponent()
        {
            this.Text = "PatsKiller Pro 2026 (Ford & Lincoln PATS Solution)";
            this.Size = new Size(900, 700);
            this.MinimumSize = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.BackColor = _colorBackground;
            this.Font = new Font("Segoe UI", 9F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.DoubleBuffered = true;

            try
            {
                var attribute = 20;
                var value = 1;
                DwmSetWindowAttribute(this.Handle, attribute, ref value, sizeof(int));
            }
            catch { }

            CreateHeaderPanel();
            CreateContentPanel();
            CreateLoginPanel();
            CreateWaitingPanel();
            CreateSuccessPanel();
            CreateErrorPanel();

            this.FormClosing += (s, e) => _cts?.Cancel();
            this.Load += (s, e) => PositionHeaderLabels();
            this.Resize += (s, e) => { PositionHeaderLabels(); CenterActivePanel(); };
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private void CreateHeaderPanel()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = _colorHeader,
                Padding = new Padding(20, 15, 20, 15)
            };

            _logoBox = new PictureBox
            {
                Size = new Size(50, 50),
                Location = new Point(20, 15),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            LoadLogo();
            _headerPanel.Controls.Add(_logoBox);

            _lblTitle = new Label
            {
                Text = "PatsKiller Pro",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true,
                Location = new Point(80, 15),
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblTitle);

            _lblSubtitle = new Label
            {
                Text = "Ford & Lincoln PATS Solution",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Location = new Point(82, 45),
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblSubtitle);

            _lblTokens = new Label
            {
                Text = "Tokens: --",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = _colorGreen,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblTokens);

            _lblStatus = new Label
            {
                Text = "Not logged in",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblStatus);

            this.Controls.Add(_headerPanel);
        }

        private void PositionHeaderLabels()
        {
            if (_lblTokens != null && _lblStatus != null && _headerPanel != null)
            {
                _lblTokens.Location = new Point(_headerPanel.Width - _lblTokens.Width - 20, 18);
                _lblStatus.Location = new Point(_headerPanel.Width - _lblStatus.Width - 20, 45);
            }
        }

        private void CreateContentPanel()
        {
            _contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = _colorBackground, Padding = new Padding(40) };
            this.Controls.Add(_contentPanel);
        }

        private void LoadLogo()
        {
            try
            {
                var asm = typeof(GoogleLoginForm).Assembly;
                var resourceCandidates = new[]
                {
                    (asm.GetName().Name ?? "PatsKillerPro") + ".Resources.logo.png",
                    "PatsKillerPro.Resources.logo.png",
                };

                foreach (var res in resourceCandidates)
                {
                    using var s = asm.GetManifestResourceStream(res);
                    if (s != null)
                    {
                        using var img = Image.FromStream(s);
                        _logoBox.Image = new Bitmap(img);
                        return;
                    }
                }

                var paths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo.png"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png"),
                    "Resources/logo.png",
                    "logo.png"
                };

                foreach (var p in paths)
                {
                    if (!File.Exists(p)) continue;
                    using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var img = Image.FromStream(fs);
                    _logoBox.Image = new Bitmap(img);
                    return;
                }

                _logoBox.Image = CreatePatsKillerLogo(50);
            }
            catch
            {
                _logoBox.Image = CreatePatsKillerLogo(50);
            }
        }

        private Image CreatePatsKillerLogo(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bgBrush = new LinearGradientBrush(new Point(0, 0), new Point(size, size), Color.FromArgb(26, 26, 46), Color.FromArgb(22, 33, 62)))
                    g.FillRectangle(bgBrush, 0, 0, size, size);
                using (var pen = new Pen(_colorRed, 2))
                    g.DrawRectangle(pen, 1, 1, size - 3, size - 3);
                var keySize = size * 0.6f;
                var offsetX = (size - keySize) / 2;
                var offsetY = (size - keySize) / 2;
                using (var redBrush = new SolidBrush(_colorRed))
                {
                    g.FillEllipse(redBrush, offsetX, offsetY + 2, keySize * 0.5f, keySize * 0.5f);
                    g.FillRectangle(redBrush, offsetX + keySize * 0.35f, offsetY + keySize * 0.2f, keySize * 0.6f, keySize * 0.15f);
                    g.FillRectangle(redBrush, offsetX + keySize * 0.7f, offsetY + keySize * 0.35f, keySize * 0.1f, keySize * 0.15f);
                    g.FillRectangle(redBrush, offsetX + keySize * 0.85f, offsetY + keySize * 0.35f, keySize * 0.1f, keySize * 0.2f);
                }
                using (var bgBrush = new SolidBrush(Color.FromArgb(26, 26, 46)))
                    g.FillEllipse(bgBrush, offsetX + keySize * 0.12f, offsetY + keySize * 0.14f, keySize * 0.25f, keySize * 0.25f);
            }
            return bmp;
        }

        private void CreateLoginPanel()
        {
            _loginPanel = new Panel { Size = new Size(460, 560), BackColor = _colorPanel, Visible = false };
            _loginPanel.Paint += (s, e) =>
            {
                using var path = GetRoundedRectPath(_loginPanel.ClientRectangle, 16);
                using var pen = new Pen(_colorBorder, 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(pen, path);
            };

            var y = 30;

            var logoPic = new PictureBox { Size = new Size(100, 100), SizeMode = PictureBoxSizeMode.Zoom, Image = CreatePatsKillerLogo(100), BackColor = Color.Transparent };
            logoPic.Location = new Point((_loginPanel.Width - 100) / 2, y);
            _loginPanel.Controls.Add(logoPic);
            y += logoPic.Height + 15;

            var lblWelcome = new Label { Text = "Welcome Back", Font = new Font("Segoe UI", 20F, FontStyle.Bold), ForeColor = _colorText, AutoSize = true, BackColor = Color.Transparent };
            lblWelcome.Location = new Point((_loginPanel.Width - lblWelcome.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblWelcome);
            y += lblWelcome.Height + 8;

            var lblSubtitle = new Label { Text = "Sign in to access your tokens", Font = new Font("Segoe UI", 10F), ForeColor = _colorTextDim, AutoSize = true, BackColor = Color.Transparent };
            lblSubtitle.Location = new Point((_loginPanel.Width - lblSubtitle.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblSubtitle);
            y += lblSubtitle.Height + 18;

            var btnGoogle = new Button
            {
                Text = "     Continue with Google",
                Size = new Size(360, 50),
                Location = new Point((_loginPanel.Width - 360) / 2, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorGoogleBtn,
                ForeColor = Color.FromArgb(60, 60, 60),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                ImageAlign = ContentAlignment.MiddleLeft
            };
            btnGoogle.FlatAppearance.BorderSize = 0;
            btnGoogle.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 245, 245);
            btnGoogle.Image = CreateGoogleIcon(24);
            btnGoogle.TextImageRelation = TextImageRelation.ImageBeforeText;
            btnGoogle.Padding = new Padding(15, 0, 0, 0);
            btnGoogle.Click += BtnGoogle_Click;
            _loginPanel.Controls.Add(btnGoogle);
            y += btnGoogle.Height + 16;

            var lblDivider = new Label { Text = "───────────  or sign in with email  ───────────", Font = new Font("Segoe UI", 8F), ForeColor = _colorTextDim, AutoSize = true, BackColor = Color.Transparent };
            lblDivider.Location = new Point((_loginPanel.Width - lblDivider.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblDivider);
            y += lblDivider.Height + 18;

            var lblEmail = new Label { Text = "Email", Font = new Font("Segoe UI", 9F), ForeColor = _colorTextDim, AutoSize = true, Location = new Point(30, y), BackColor = Color.Transparent };
            _loginPanel.Controls.Add(lblEmail);
            y += lblEmail.Height + 6;

            var txtEmail = new TextBox { Name = "txtEmail", Size = new Size(360, 40), Location = new Point(30, y), BackColor = _colorInput, ForeColor = _colorTextDim, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11F), Text = "you@example.com" };
            txtEmail.GotFocus += (s, e) => { if (txtEmail.Text == "you@example.com") { txtEmail.Text = ""; txtEmail.ForeColor = _colorText; } };
            _loginPanel.Controls.Add(txtEmail);
            y += txtEmail.Height + 16;

            var lblPassword = new Label { Text = "Password", Font = new Font("Segoe UI", 9F), ForeColor = _colorTextDim, AutoSize = true, Location = new Point(30, y), BackColor = Color.Transparent };
            _loginPanel.Controls.Add(lblPassword);
            y += lblPassword.Height + 6;

            var txtPassword = new TextBox { Name = "txtPassword", Size = new Size(360, 40), Location = new Point(30, y), BackColor = _colorInput, ForeColor = _colorText, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11F), UseSystemPasswordChar = true };
            _loginPanel.Controls.Add(txtPassword);
            y += txtPassword.Height + 18;

            var btnSignIn = new Button { Text = "Sign In", Size = new Size(360, 45), Location = new Point(30, y), FlatStyle = FlatStyle.Flat, BackColor = _colorRed, ForeColor = Color.White, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSignIn.FlatAppearance.BorderSize = 0;
            btnSignIn.Click += async (s, e) =>
            {
                var email = _loginPanel.Controls.Find("txtEmail", false).FirstOrDefault() as TextBox;
                var pwd = _loginPanel.Controls.Find("txtPassword", false).FirstOrDefault() as TextBox;
                if (email != null && pwd != null)
                    await DoEmailLoginAsync(email.Text, pwd.Text);
            };
            _loginPanel.Controls.Add(btnSignIn);
            y += btnSignIn.Height + 16;

            var lblRegister = new Label { Text = "Don't have an account? Register at patskiller.com", Font = new Font("Segoe UI", 9F), ForeColor = _colorTextDim, AutoSize = true, BackColor = Color.Transparent, Cursor = Cursors.Hand };
            lblRegister.Location = new Point((_loginPanel.Width - lblRegister.PreferredWidth) / 2, y);
            lblRegister.Click += (s, e) => OpenUrl("https://patskiller.com/register");
            lblRegister.MouseEnter += (s, e) => lblRegister.ForeColor = _colorRed;
            lblRegister.MouseLeave += (s, e) => lblRegister.ForeColor = _colorTextDim;
            _loginPanel.Controls.Add(lblRegister);

            _contentPanel.Controls.Add(_loginPanel);
        }

        private System.Linq.IQueryable<T>? FirstOrDefault<T>() => throw new NotImplementedException();

        private Image CreateGoogleIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var center = size / 2f;
                var radius = size * 0.4f;
                using (var pen = new Pen(Color.FromArgb(66, 133, 244), size * 0.15f)) g.DrawArc(pen, center - radius, center - radius, radius * 2, radius * 2, -45, 90);
                using (var pen = new Pen(Color.FromArgb(52, 168, 83), size * 0.15f)) g.DrawArc(pen, center - radius, center - radius, radius * 2, radius * 2, 45, 90);
                using (var pen = new Pen(Color.FromArgb(251, 188, 5), size * 0.15f)) g.DrawArc(pen, center - radius, center - radius, radius * 2, radius * 2, 135, 90);
                using (var pen = new Pen(Color.FromArgb(234, 67, 53), size * 0.15f)) g.DrawArc(pen, center - radius, center - radius, radius * 2, radius * 2, 225, 90);
            }
            return bmp;
        }

        private void CreateWaitingPanel()
        {
            _waitingPanel = new Panel { Size = new Size(420, 420), BackColor = _colorPanel, Visible = false };
            var y = 40;

            var logoPic = new PictureBox { Size = new Size(100, 100), SizeMode = PictureBoxSizeMode.Zoom, Image = CreatePatsKillerLogo(100), BackColor = Color.Transparent };
            logoPic.Location = new Point((_waitingPanel.Width - 100) / 2, y);
            _waitingPanel.Controls.Add(logoPic);
            y += 120;

            var lblTitle = new Label { Text = "Complete Sign In", Font = new Font("Segoe UI", 20F, FontStyle.Bold), ForeColor = _colorText, AutoSize = true, BackColor = Color.Transparent };
            lblTitle.Location = new Point((_waitingPanel.Width - lblTitle.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblTitle);
            y += lblTitle.Height + 24;

            var lblMsg = new Label { Text = "A browser window has opened.\nPlease sign in with Google to continue.", Font = new Font("Segoe UI", 10F), ForeColor = _colorTextDim, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
            lblMsg.Location = new Point((_waitingPanel.Width - lblMsg.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblMsg);
            y += lblMsg.Height + 28;

            var dotsPanel = new Panel { Size = new Size(80, 20), Location = new Point((_waitingPanel.Width - 80) / 2, y), BackColor = Color.Transparent };
            var dot1 = new Panel { Size = new Size(14, 14), Location = new Point(5, 3), BackColor = _colorRed };
            var dot2 = new Panel { Size = new Size(14, 14), Location = new Point(30, 3), BackColor = _colorRed };
            var dot3 = new Panel { Size = new Size(14, 14), Location = new Point(55, 3), BackColor = _colorRed };
            dot1.Paint += (s, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var b = new SolidBrush(dot1.BackColor); e.Graphics.FillEllipse(b, 0, 0, 14, 14); };
            dot2.Paint += (s, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var b = new SolidBrush(dot2.BackColor); e.Graphics.FillEllipse(b, 0, 0, 14, 14); };
            dot3.Paint += (s, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var b = new SolidBrush(dot3.BackColor); e.Graphics.FillEllipse(b, 0, 0, 14, 14); };
            dotsPanel.Controls.AddRange(new Control[] { dot1, dot2, dot3 });
            _waitingPanel.Controls.Add(dotsPanel);

            var animTimer = new System.Windows.Forms.Timer { Interval = 300 };
            var animState = 0;
            animTimer.Tick += (s, e) =>
            {
                dot1.BackColor = animState == 0 ? _colorRed : Color.FromArgb(80, _colorRed);
                dot2.BackColor = animState == 1 ? _colorRed : Color.FromArgb(80, _colorRed);
                dot3.BackColor = animState == 2 ? _colorRed : Color.FromArgb(80, _colorRed);
                dot1.Invalidate(); dot2.Invalidate(); dot3.Invalidate();
                animState = (animState + 1) % 3;
            };
            animTimer.Start();
            y += dotsPanel.Height + 18;

            _lblWaitingStatus = new Label { Name = "lblWaitingStatus", Text = "Waiting for authentication...", Font = new Font("Segoe UI", 9F), ForeColor = _colorTextDim, AutoSize = true, BackColor = Color.Transparent };
            _lblWaitingStatus.Location = new Point((_waitingPanel.Width - _lblWaitingStatus.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(_lblWaitingStatus);
            y += _lblWaitingStatus.Height + 18;

            var btnReopen = new Button { Text = "🌐  Reopen Browser", Size = new Size(280, 45), Location = new Point((_waitingPanel.Width - 280) / 2, y), FlatStyle = FlatStyle.Flat, BackColor = _colorInput, ForeColor = _colorText, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnReopen.FlatAppearance.BorderColor = _colorBorder;
            btnReopen.FlatAppearance.BorderSize = 1;
            btnReopen.Click += BtnReopenBrowser_Click;
            _waitingPanel.Controls.Add(btnReopen);
            y += btnReopen.Height + 16;

            var lblCancel = new Label { Text = "Cancel", Font = new Font("Segoe UI", 10F), ForeColor = _colorTextDim, AutoSize = true, Cursor = Cursors.Hand, BackColor = Color.Transparent };
            lblCancel.Location = new Point((_waitingPanel.Width - lblCancel.PreferredWidth) / 2, y);
            lblCancel.Click += (s, e) => { _cts?.Cancel(); Logger.Info("[GoogleLoginForm] Login cancelled by user"); ShowLoginState(); };
            lblCancel.MouseEnter += (s, e) => lblCancel.ForeColor = _colorText;
            lblCancel.MouseLeave += (s, e) => lblCancel.ForeColor = _colorTextDim;
            _waitingPanel.Controls.Add(lblCancel);

            _contentPanel.Controls.Add(_waitingPanel);
        }

        private void CreateSuccessPanel()
        {
            _successPanel = new Panel { Size = new Size(420, 420), BackColor = _colorPanel, Visible = false };
            var y = 40;

            var successIcon = new Panel { Size = new Size(96, 96), Location = new Point((_successPanel.Width - 96) / 2, y), BackColor = Color.Transparent };
            successIcon.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new LinearGradientBrush(new Point(0, 0), new Point(96, 96), Color.FromArgb(76, 175, 80), Color.FromArgb(56, 142, 60)))
                    e.Graphics.FillEllipse(brush, 0, 0, 94, 94);
                using (var pen = new Pen(Color.White, 6)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; e.Graphics.DrawLine(pen, 25, 50, 42, 67); e.Graphics.DrawLine(pen, 42, 67, 72, 32); }
            };
            _successPanel.Controls.Add(successIcon);
            y += successIcon.Height + 20;

            var lblWelcome = new Label { Name = "lblSuccessTitle", Text = "Welcome!", Font = new Font("Segoe UI", 22F, FontStyle.Bold), ForeColor = _colorText, AutoSize = true, BackColor = Color.Transparent };
            lblWelcome.Location = new Point((_successPanel.Width - lblWelcome.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblWelcome);
            y += lblWelcome.Height + 18;

            var lblSignedAs = new Label { Text = "Signed in as", Font = new Font("Segoe UI", 10F), ForeColor = _colorTextDim, AutoSize = true, BackColor = Color.Transparent };
            lblSignedAs.Location = new Point((_successPanel.Width - lblSignedAs.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblSignedAs);
            y += 25;

            var lblEmail = new Label { Name = "lblSuccessEmail", Text = "user@example.com", Font = new Font("Segoe UI", 13F, FontStyle.Bold), ForeColor = _colorText, AutoSize = true, BackColor = Color.Transparent };
            lblEmail.Location = new Point((_successPanel.Width - lblEmail.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblEmail);
            y += lblEmail.Height + 22;

            var tokenBox = new Panel { Size = new Size(340, 60), Location = new Point((_successPanel.Width - 340) / 2, y), BackColor = _colorInput };
            tokenBox.Paint += (s, e) => { using var pen = new Pen(_colorBorder, 1); using var path = GetRoundedRectPath(tokenBox.ClientRectangle, 12); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.DrawPath(pen, path); };
            tokenBox.Controls.Add(new Label { Text = "Available Tokens", Font = new Font("Segoe UI", 10F), ForeColor = _colorTextDim, Location = new Point(20, 20), AutoSize = true, BackColor = Color.Transparent });
            var lblTokenCount = new Label { Name = "lblSuccessTokens", Text = "0", Font = new Font("Segoe UI", 20F, FontStyle.Bold), ForeColor = _colorGreen, AutoSize = true, BackColor = Color.Transparent };
            lblTokenCount.Location = new Point(tokenBox.Width - lblTokenCount.PreferredWidth - 25, 15);
            tokenBox.Controls.Add(lblTokenCount);
            _successPanel.Controls.Add(tokenBox);
            y += 80;

            var btnStart = new Button { Text = "Start Programming", Size = new Size(340, 55), Location = new Point((_successPanel.Width - 340) / 2, y), FlatStyle = FlatStyle.Flat, BackColor = _colorRed, ForeColor = Color.White, Font = new Font("Segoe UI", 13F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
            _successPanel.Controls.Add(btnStart);

            _contentPanel.Controls.Add(_successPanel);
        }

        private void CreateErrorPanel()
        {
            _errorPanel = new Panel { Size = new Size(420, 380), BackColor = _colorPanel, Visible = false };
            var y = 40;

            var errorIcon = new Panel { Size = new Size(96, 96), Location = new Point((_errorPanel.Width - 96) / 2, y), BackColor = Color.Transparent };
            errorIcon.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new LinearGradientBrush(new Point(0, 0), new Point(96, 96), Color.FromArgb(244, 67, 54), Color.FromArgb(211, 47, 47)))
                    e.Graphics.FillEllipse(brush, 0, 0, 94, 94);
                using (var pen = new Pen(Color.White, 6)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; e.Graphics.DrawLine(pen, 30, 30, 64, 64); e.Graphics.DrawLine(pen, 64, 30, 30, 64); }
            };
            _errorPanel.Controls.Add(errorIcon);
            y += errorIcon.Height + 20;

            var lblTitle = new Label { Text = "Sign In Failed", Font = new Font("Segoe UI", 22F, FontStyle.Bold), ForeColor = _colorText, AutoSize = true, BackColor = Color.Transparent };
            lblTitle.Location = new Point((_errorPanel.Width - lblTitle.PreferredWidth) / 2, y);
            _errorPanel.Controls.Add(lblTitle);
            y += lblTitle.Height + 18;

            var lblMsg = new Label { Name = "lblErrorMsg", Text = "Authentication was cancelled or timed out.\nPlease try again.", Font = new Font("Segoe UI", 10F), ForeColor = _colorTextDim, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
            lblMsg.Location = new Point((_errorPanel.Width - lblMsg.PreferredWidth) / 2, y);
            _errorPanel.Controls.Add(lblMsg);
            y += lblMsg.Height + 26;

            var btnRetry = new Button { Text = "Try Again", Size = new Size(300, 55), Location = new Point((_errorPanel.Width - 300) / 2, y), FlatStyle = FlatStyle.Flat, BackColor = _colorRed, ForeColor = Color.White, Font = new Font("Segoe UI", 13F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnRetry.FlatAppearance.BorderSize = 0;
            btnRetry.Click += async (s, e) => await StartGoogleAuthAsync();
            _errorPanel.Controls.Add(btnRetry);
            y += btnRetry.Height + 16;

            var lblBack = new Label { Text = "Back to Login", Font = new Font("Segoe UI", 10F), ForeColor = _colorTextDim, AutoSize = true, Cursor = Cursors.Hand, BackColor = Color.Transparent };
            lblBack.Location = new Point((_errorPanel.Width - lblBack.PreferredWidth) / 2, y);
            lblBack.Click += (s, e) => ShowLoginState();
            lblBack.MouseEnter += (s, e) => lblBack.ForeColor = _colorText;
            lblBack.MouseLeave += (s, e) => lblBack.ForeColor = _colorTextDim;
            _errorPanel.Controls.Add(lblBack);

            _contentPanel.Controls.Add(_errorPanel);
        }

        // ============ STATE MANAGEMENT ============
        private void ShowLoginState()
        {
            _loginPanel.Visible = true;
            _waitingPanel.Visible = false;
            _successPanel.Visible = false;
            _errorPanel.Visible = false;
            CenterPanel(_loginPanel);
        }

        private void ShowWaitingState()
        {
            _loginPanel.Visible = false;
            _waitingPanel.Visible = true;
            _successPanel.Visible = false;
            _errorPanel.Visible = false;
            UpdateWaitingStatus("Waiting for authentication...");
            CenterPanel(_waitingPanel);
        }

        private void UpdateWaitingStatus(string text)
        {
            if (_lblWaitingStatus != null && !IsDisposed)
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(() => UpdateWaitingStatus(text)));
                else
                {
                    _lblWaitingStatus.Text = text;
                    _lblWaitingStatus.Location = new Point((_waitingPanel.Width - _lblWaitingStatus.PreferredWidth) / 2, _lblWaitingStatus.Location.Y);
                    Logger.Debug($"[GoogleLoginForm] Status: {text}");
                }
            }
        }

        private void ShowSuccessState(string email, int tokens)
        {
            foreach (Control c in _successPanel.Controls)
            {
                if (c.Name == "lblSuccessEmail")
                {
                    c.Text = email;
                    c.Location = new Point((_successPanel.Width - c.PreferredSize.Width) / 2, c.Location.Y);
                }
            }
            foreach (Control c in _successPanel.Controls)
            {
                if (c is Panel tokenBox)
                {
                    foreach (Control tc in tokenBox.Controls)
                    {
                        if (tc.Name == "lblSuccessTokens")
                        {
                            tc.Text = tokens.ToString();
                            tc.Location = new Point(tokenBox.Width - tc.PreferredSize.Width - 25, 15);
                        }
                    }
                }
            }

            _loginPanel.Visible = false;
            _waitingPanel.Visible = false;
            _successPanel.Visible = true;
            _errorPanel.Visible = false;
            CenterPanel(_successPanel);

            _lblTokens.Text = $"Tokens: {tokens}";
            _lblStatus.Text = email;
            PositionHeaderLabels();
            
            Logger.Info($"[GoogleLoginForm] Success state shown. Email: {email}, Tokens: {tokens}");
            AutoCloseAfterSuccess();
        }

        private async void AutoCloseAfterSuccess()
        {
            try
            {
                await Task.Delay(500);
                if (IsDisposed || !Visible) return;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch { }
        }

        private void ShowErrorState(string message = "Authentication was cancelled or timed out.\nPlease try again.")
        {
            foreach (Control c in _errorPanel.Controls)
            {
                if (c.Name == "lblErrorMsg")
                {
                    c.Text = message;
                    c.Location = new Point((_errorPanel.Width - c.PreferredSize.Width) / 2, c.Location.Y);
                }
            }
            _loginPanel.Visible = false;
            _waitingPanel.Visible = false;
            _successPanel.Visible = false;
            _errorPanel.Visible = true;
            CenterPanel(_errorPanel);
            Logger.Warning($"[GoogleLoginForm] Error state shown: {message}");
        }

        private void CenterPanel(Panel panel)
        {
            if (_contentPanel != null && panel != null)
            {
                panel.Location = new Point(
                    Math.Max(0, (_contentPanel.Width - panel.Width) / 2),
                    Math.Max(0, (_contentPanel.Height - panel.Height) / 2)
                );
            }
        }

        private void CenterActivePanel()
        {
            if (_loginPanel?.Visible == true) CenterPanel(_loginPanel);
            else if (_waitingPanel?.Visible == true) CenterPanel(_waitingPanel);
            else if (_successPanel?.Visible == true) CenterPanel(_successPanel);
            else if (_errorPanel?.Visible == true) CenterPanel(_errorPanel);
        }

        // ============ EVENT HANDLERS ============
        private async void BtnGoogle_Click(object? sender, EventArgs e) => await StartGoogleAuthAsync();

        private void BtnReopenBrowser_Click(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentSessionCode))
            {
                Logger.Info($"[GoogleLoginForm] Reopening browser for session: {_currentSessionCode}");
                OpenUrl(AUTH_PAGE_URL + _currentSessionCode);
            }
        }

        // ============ AUTH FLOW ============
        private async Task StartGoogleAuthAsync()
        {
            try
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                _pollFailureCount = 0;

                Logger.Info("[GoogleLoginForm] Starting Google auth flow...");

                var session = await CreateSessionAsync();
                if (session == null)
                {
                    ShowErrorState("Could not connect to server.\nPlease check your internet connection.");
                    return;
                }

                _currentSessionCode = session.SessionCode;
                Logger.Info($"[GoogleLoginForm] Session created: {session.SessionCode}");
                
                OpenUrl(AUTH_PAGE_URL + session.SessionCode);
                ShowWaitingState();
                await PollForCompletionAsync(session.SessionCode, _cts.Token);
            }
            catch (Exception ex)
            {
                Logger.Error("[GoogleLoginForm] StartGoogleAuthAsync error", ex);
                ShowErrorState("Failed to start authentication.\nPlease try again.");
            }
        }

        private async Task DoEmailLoginAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || email == "you@example.com" || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please enter your email and password.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Logger.Info($"[GoogleLoginForm] Attempting email login for: {email}");
                
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/auth/v1/token?grant_type=password");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                
                var body = JsonSerializer.Serialize(new { email = email, password = password });
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                Logger.Debug($"[GoogleLoginForm] Email login response: {response.StatusCode}");
                Logger.Debug($"[GoogleLoginForm] Email login body: {json.Substring(0, Math.Min(200, json.Length))}...");

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warning($"[GoogleLoginForm] Email login failed: {response.StatusCode}");
                    MessageBox.Show("Invalid email or password.\nPlease try again or use Google sign-in.", "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                AuthToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
                RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
                
                if (root.TryGetProperty("user", out var user))
                    UserEmail = user.TryGetProperty("email", out var ue) ? ue.GetString() : email;
                else
                    UserEmail = email;

                Logger.Info($"[GoogleLoginForm] Email login successful. Fetching token count...");
                TokenCount = await FetchTokenCountAsync(AuthToken!);
                
                Logger.Info($"[GoogleLoginForm] Email login complete: {UserEmail}, Tokens: {TokenCount}");
                this.BeginInvoke(new Action(() => ShowSuccessState(UserEmail ?? "User", TokenCount)));
            }
            catch (Exception ex)
            {
                Logger.Error("[GoogleLoginForm] DoEmailLoginAsync error", ex);
                MessageBox.Show($"Login failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============ API CALLS ============
        private async Task<SessionInfo?> CreateSessionAsync()
        {
            try
            {
                Logger.Debug("[GoogleLoginForm] Creating desktop auth session...");
                
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/create-desktop-auth-session");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                
                var body = JsonSerializer.Serialize(new { machineId = _machineId });
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                Logger.Debug($"[GoogleLoginForm] CreateSession response: {response.StatusCode} - {json}");

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error($"[GoogleLoginForm] CreateSession failed: {response.StatusCode} - {json}");
                    return null;
                }

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var sessionCode = root.TryGetProperty("sessionCode", out var sc) ? sc.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(sessionCode))
                {
                    Logger.Error($"[GoogleLoginForm] CreateSession: No sessionCode in response");
                    return null;
                }
                
                return new SessionInfo { SessionCode = sessionCode };
            }
            catch (TaskCanceledException)
            {
                Logger.Warning("[GoogleLoginForm] CreateSessionAsync timed out");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error("[GoogleLoginForm] CreateSessionAsync error", ex);
                return null;
            }
        }

        private async Task PollForCompletionAsync(string sessionCode, CancellationToken ct)
        {
            try
            {
                var timeout = DateTime.UtcNow.AddMinutes(5);
                var pollCount = 0;
                
                Logger.Info($"[GoogleLoginForm] Starting polling for session: {sessionCode}");
                
                while (!ct.IsCancellationRequested && DateTime.UtcNow < timeout)
                {
                    await Task.Delay(POLL_INTERVAL_MS, ct);
                    pollCount++;

                    var result = await CheckSessionAsync(sessionCode);
                    
                    if (result == null)
                    {
                        _pollFailureCount++;
                        Logger.Warning($"[GoogleLoginForm] Poll failure {_pollFailureCount}/{MAX_POLL_FAILURES} (poll #{pollCount})");
                        
                        UpdateWaitingStatus($"Retrying... ({_pollFailureCount}/{MAX_POLL_FAILURES})");
                        
                        if (_pollFailureCount >= MAX_POLL_FAILURES)
                        {
                            Logger.Error("[GoogleLoginForm] Max poll failures reached");
                            this.BeginInvoke(new Action(() => ShowErrorState("Connection issues detected.\nPlease check your internet and try again.")));
                            return;
                        }
                        continue;
                    }
                    
                    _pollFailureCount = 0;
                    
                    Logger.Debug($"[GoogleLoginForm] Poll #{pollCount} - Status: {result.Status}");

                    if (result.Status == "complete" && !string.IsNullOrEmpty(result.Token))
                    {
                        Logger.Info($"[GoogleLoginForm] Session complete! Email: {result.Email}");
                        
                        AuthToken = result.Token;
                        RefreshToken = result.RefreshToken;
                        UserEmail = result.Email;
                        
                        // Check if tokenBalance was included in response
                        if (result.TokenCount > 0)
                        {
                            Logger.Info($"[GoogleLoginForm] Token count from session response: {result.TokenCount}");
                            TokenCount = result.TokenCount;
                        }
                        else
                        {
                            // Fetch token count from API
                            UpdateWaitingStatus("Fetching account info...");
                            Logger.Info("[GoogleLoginForm] Fetching token count from API...");
                            TokenCount = await FetchTokenCountAsync(result.Token);
                        }
                        
                        Logger.Info($"[GoogleLoginForm] Login complete: {result.Email}, Tokens: {TokenCount}");
                        this.BeginInvoke(new Action(() => ShowSuccessState(result.Email ?? "User", TokenCount)));
                        return;
                    }
                    else if (result.Status == "expired" || result.Status == "invalid")
                    {
                        Logger.Warning($"[GoogleLoginForm] Session {result.Status}");
                        this.BeginInvoke(new Action(() => ShowErrorState("Session expired.\nPlease try again.")));
                        return;
                    }
                    
                    UpdateWaitingStatus("Waiting for authentication...");
                }
                
                if (!ct.IsCancellationRequested)
                {
                    Logger.Warning("[GoogleLoginForm] Polling timed out after 5 minutes");
                    this.BeginInvoke(new Action(() => ShowErrorState("Authentication timed out.\nPlease try again.")));
                }
            }
            catch (OperationCanceledException) 
            {
                Logger.Info("[GoogleLoginForm] Polling cancelled");
            }
            catch (Exception ex)
            {
                Logger.Error("[GoogleLoginForm] PollForCompletionAsync error", ex);
                this.BeginInvoke(new Action(() => ShowErrorState("An error occurred.\nPlease try again.")));
            }
        }

        private async Task<SessionResult?> CheckSessionAsync(string sessionCode)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/check-desktop-auth-session");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                
                var body = JsonSerializer.Serialize(new { sessionCode = sessionCode, machineId = _machineId });
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                Logger.Debug($"[GoogleLoginForm] CheckSession: {response.StatusCode} - {json.Substring(0, Math.Min(500, json.Length))}");

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warning($"[GoogleLoginForm] CheckSession failed: {response.StatusCode}");
                    return null;
                }

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";

                var result = new SessionResult { Status = status };

                if (status == "complete")
                {
                    result.Token = root.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
                    result.RefreshToken = root.TryGetProperty("refreshToken", out var r) ? r.GetString() : null;
                    result.Email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
                    
                    // Try to get token count from response
                    if (root.TryGetProperty("tokenBalance", out var tb))
                    {
                        result.TokenCount = tb.GetInt32();
                        Logger.Info($"[GoogleLoginForm] Got tokenBalance from response: {result.TokenCount}");
                    }
                    else if (root.TryGetProperty("token_balance", out var tb2))
                    {
                        result.TokenCount = tb2.GetInt32();
                        Logger.Info($"[GoogleLoginForm] Got token_balance from response: {result.TokenCount}");
                    }
                    else
                    {
                        Logger.Debug("[GoogleLoginForm] No token balance in session response - will fetch separately");
                    }
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                Logger.Warning("[GoogleLoginForm] CheckSessionAsync timed out");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error("[GoogleLoginForm] CheckSessionAsync error", ex);
                return null;
            }
        }

        /// <summary>
        /// Fetch actual token balance from API after authentication
        /// Includes detailed logging for debugging
        /// </summary>
        private async Task<int> FetchTokenCountAsync(string accessToken)
        {
            Logger.Info("[GoogleLoginForm] === FETCHING TOKEN COUNT ===");
            Logger.Debug($"[GoogleLoginForm] Access token preview: {accessToken.Substring(0, Math.Min(50, accessToken.Length))}...");
            
            try
            {
                // METHOD 1: Try dedicated get-user-tokens endpoint
                Logger.Info("[GoogleLoginForm] Method 1: Trying get-user-tokens endpoint...");
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/functions/v1/get-user-tokens");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {accessToken}");

                Logger.Debug($"[GoogleLoginForm] Request URL: {request.RequestUri}");
                Logger.Debug($"[GoogleLoginForm] Request headers: apikey=*****, Authorization=Bearer ***");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                Logger.Info($"[GoogleLoginForm] get-user-tokens response: Status={response.StatusCode}");
                Logger.Debug($"[GoogleLoginForm] get-user-tokens body: {json}");

                if (response.IsSuccessStatusCode)
                {
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    // Check all possible property names
                    if (root.TryGetProperty("tokens", out var tokens))
                    {
                        var count = tokens.GetInt32();
                        Logger.Info($"[GoogleLoginForm] SUCCESS: Got 'tokens' = {count}");
                        return count;
                    }
                    if (root.TryGetProperty("tokenBalance", out var balance))
                    {
                        var count = balance.GetInt32();
                        Logger.Info($"[GoogleLoginForm] SUCCESS: Got 'tokenBalance' = {count}");
                        return count;
                    }
                    if (root.TryGetProperty("token_balance", out var tb))
                    {
                        var count = tb.GetInt32();
                        Logger.Info($"[GoogleLoginForm] SUCCESS: Got 'token_balance' = {count}");
                        return count;
                    }
                    
                    // Log all properties in response
                    Logger.Warning("[GoogleLoginForm] Response OK but no token property found. Properties in response:");
                    foreach (var prop in root.EnumerateObject())
                    {
                        Logger.Debug($"[GoogleLoginForm]   - {prop.Name}: {prop.Value}");
                    }
                }
                else
                {
                    Logger.Warning($"[GoogleLoginForm] get-user-tokens failed: {response.StatusCode} - {json}");
                }
                
                // METHOD 2: Fallback to profiles table
                Logger.Info("[GoogleLoginForm] Method 2: Falling back to profiles table...");
                return await FetchTokenCountFromProfileAsync(accessToken);
            }
            catch (Exception ex)
            {
                Logger.Error("[GoogleLoginForm] FetchTokenCountAsync error", ex);
                Logger.Info("[GoogleLoginForm] Trying fallback method...");
                return await FetchTokenCountFromProfileAsync(accessToken);
            }
        }

        private async Task<int> FetchTokenCountFromProfileAsync(string accessToken)
        {
            try
            {
                Logger.Info("[GoogleLoginForm] Querying profiles table directly...");
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{SUPABASE_URL}/rest/v1/profiles?select=token_balance");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                request.Headers.Add("Authorization", $"Bearer {accessToken}");

                Logger.Debug($"[GoogleLoginForm] Request URL: {request.RequestUri}");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                Logger.Info($"[GoogleLoginForm] Profiles response: Status={response.StatusCode}");
                Logger.Debug($"[GoogleLoginForm] Profiles body: {json}");

                if (response.IsSuccessStatusCode)
                {
                    var doc = JsonDocument.Parse(json);
                    var array = doc.RootElement;
                    
                    Logger.Debug($"[GoogleLoginForm] Profiles array length: {array.GetArrayLength()}");
                    
                    if (array.GetArrayLength() > 0)
                    {
                        var profile = array[0];
                        Logger.Debug($"[GoogleLoginForm] First profile: {profile}");
                        
                        if (profile.TryGetProperty("token_balance", out var tb))
                        {
                            var count = tb.GetInt32();
                            Logger.Info($"[GoogleLoginForm] SUCCESS from profiles: token_balance = {count}");
                            return count;
                        }
                        else
                        {
                            Logger.Warning("[GoogleLoginForm] Profile found but no token_balance property");
                            foreach (var prop in profile.EnumerateObject())
                            {
                                Logger.Debug($"[GoogleLoginForm]   - {prop.Name}: {prop.Value}");
                            }
                        }
                    }
                    else
                    {
                        Logger.Warning("[GoogleLoginForm] Profiles query returned empty array");
                    }
                }
                else
                {
                    Logger.Warning($"[GoogleLoginForm] Profiles query failed: {response.StatusCode} - {json}");
                }

                Logger.Warning("[GoogleLoginForm] All token fetch methods failed, returning 0");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("[GoogleLoginForm] FetchTokenCountFromProfileAsync error", ex);
                return 0;
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error($"[GoogleLoginForm] Failed to open URL: {url}", ex);
            }
        }

        private static GraphicsPath GetRoundedRectPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            var arc = new Rectangle(rect.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private class SessionInfo { public string SessionCode { get; set; } = ""; }
        private class SessionResult { public string Status { get; set; } = ""; public string? Token { get; set; } public string? RefreshToken { get; set; } public string? Email { get; set; } public int TokenCount { get; set; } }
    }
}
