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
    /// Features: Google OAuth primary, Email/password secondary, No QR code
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
        
        // Content panels for different states
        private Panel _loginPanel = null!;
        private Panel _waitingPanel = null!;
        private Panel _successPanel = null!;
        private Panel _errorPanel = null!;

        // ============ DARK THEME COLORS (Matching Mockup) ============
        private readonly Color _colorBackground = Color.FromArgb(30, 30, 30);      // #1e1e1e
        private readonly Color _colorHeader = Color.FromArgb(37, 37, 38);          // #252526
        private readonly Color _colorPanel = Color.FromArgb(45, 45, 48);           // #2d2d30
        private readonly Color _colorInput = Color.FromArgb(60, 60, 60);           // #3c3c3c
        private readonly Color _colorBorder = Color.FromArgb(70, 70, 70);          // #464646
        private readonly Color _colorText = Color.FromArgb(255, 255, 255);         // White
        private readonly Color _colorTextDim = Color.FromArgb(150, 150, 150);      // Gray
        private readonly Color _colorRed = Color.FromArgb(233, 69, 96);            // #e94560 - PatsKiller Red
        private readonly Color _colorGreen = Color.FromArgb(76, 175, 80);          // #4caf50 - Success Green
        private readonly Color _colorGoogleBtn = Color.FromArgb(255, 255, 255);    // White for Google button

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
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _machineId;

        // ============ CONSTRUCTOR ============
        public GoogleLoginForm()
        {
            _machineId = GetMachineId();
            InitializeComponent();
            ShowLoginState();
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

        // ============ INITIALIZATION ============
        private void InitializeComponent()
        {
            // Form settings - large size for login dialog
            this.Text = "PatsKiller Pro - Sign In";
            this.ClientSize = new Size(520, 620);
            this.MinimumSize = new Size(520, 620);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = _colorBackground;
            this.Font = new Font("Segoe UI", 10F);
            this.AutoScaleMode = AutoScaleMode.None; // Disable auto-scaling to prevent shrinking
            this.DoubleBuffered = true;

            // Enable dark title bar on Windows 10/11
            try
            {
                var attribute = 20; // DWMWA_USE_IMMERSIVE_DARK_MODE
                var value = 1;
                DwmSetWindowAttribute(this.Handle, attribute, ref value, sizeof(int));
            }
            catch { }

            // Create content panel first, then header docks on top
            CreateContentPanel();
            CreateHeaderPanel();
            
            CreateLoginPanel();
            CreateWaitingPanel();
            CreateSuccessPanel();
            CreateErrorPanel();

            this.FormClosing += (s, e) => _cts?.Cancel();
            this.Load += (s, e) => CenterActivePanel();
            this.Resize += (s, e) => CenterActivePanel();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // ============ HEADER PANEL ============
        private void CreateHeaderPanel()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = _colorHeader
            };

            // Logo
            _logoBox = new PictureBox
            {
                Size = new Size(50, 50),
                Location = new Point(15, 10),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            LoadLogo();
            _headerPanel.Controls.Add(_logoBox);

            // Title
            _lblTitle = new Label
            {
                Text = "PatsKiller Pro",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true,
                Location = new Point(75, 12),
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblTitle);

            // Subtitle
            _lblSubtitle = new Label
            {
                Text = "Ford & Lincoln PATS Solution",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Location = new Point(77, 40),
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblSubtitle);

            // Token/Status labels (hidden until logged in)
            _lblTokens = new Label { Visible = false, AutoSize = true, BackColor = Color.Transparent };
            _lblStatus = new Label { Visible = false, AutoSize = true, BackColor = Color.Transparent };
            _headerPanel.Controls.Add(_lblTokens);
            _headerPanel.Controls.Add(_lblStatus);

            this.Controls.Add(_headerPanel);
        }

        private void PositionHeaderLabels()
        {
            // No-op for simplified header
        }

        private void CreateContentPanel()
        {
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _colorBackground,
                Padding = new Padding(10) // Minimal padding
            };
            this.Controls.Add(_contentPanel);
        }

        private void LoadLogo()
        {
            try
            {
                // Priority: embedded resource -> loose file in output -> fallback drawn mark
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

                    // Load without locking the file on disk
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

        /// <summary>
        /// Creates the PatsKiller logo programmatically (key icon with red accents)
        /// </summary>
        private Image CreatePatsKillerLogo(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                
                // Background with gradient
                using (var bgBrush = new LinearGradientBrush(
                    new Point(0, 0), new Point(size, size),
                    Color.FromArgb(26, 26, 46), Color.FromArgb(22, 33, 62)))
                {
                    g.FillRectangle(bgBrush, 0, 0, size, size);
                }
                
                // Red border
                using (var pen = new Pen(_colorRed, 2))
                {
                    g.DrawRectangle(pen, 1, 1, size - 3, size - 3);
                }
                
                // Key icon
                var keySize = size * 0.6f;
                var offsetX = (size - keySize) / 2;
                var offsetY = (size - keySize) / 2;
                
                using (var redBrush = new SolidBrush(_colorRed))
                {
                    // Key head (circle)
                    g.FillEllipse(redBrush, offsetX, offsetY + 2, keySize * 0.5f, keySize * 0.5f);
                    // Key shaft
                    g.FillRectangle(redBrush, offsetX + keySize * 0.35f, offsetY + keySize * 0.2f, keySize * 0.6f, keySize * 0.15f);
                    // Key teeth
                    g.FillRectangle(redBrush, offsetX + keySize * 0.7f, offsetY + keySize * 0.35f, keySize * 0.1f, keySize * 0.15f);
                    g.FillRectangle(redBrush, offsetX + keySize * 0.85f, offsetY + keySize * 0.35f, keySize * 0.1f, keySize * 0.2f);
                }
                
                // Inner circle hole in key head
                using (var bgBrush = new SolidBrush(Color.FromArgb(26, 26, 46)))
                {
                    g.FillEllipse(bgBrush, offsetX + keySize * 0.12f, offsetY + keySize * 0.14f, keySize * 0.25f, keySize * 0.25f);
                }
            }
            return bmp;
        }

        // ============ LOGIN PANEL (Step 1) ============
        private void CreateLoginPanel()
        {
            _loginPanel = new Panel
            {
                Size = new Size(480, 500),
                BackColor = _colorPanel,
                Visible = false
            };

            // Round corners effect via region
            _loginPanel.Paint += (s, e) =>
            {
                using var path = GetRoundedRectPath(_loginPanel.ClientRectangle, 12);
                using var pen = new Pen(_colorBorder, 1);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(pen, path);
            };

            var y = 40;
            var panelW = _loginPanel.Width;
            var btnW = 400;
            var padL = (panelW - btnW) / 2;

            // "Welcome Back" title (no logo - already in header)
            var lblWelcome = new Label
            {
                Text = "Welcome Back",
                Font = new Font("Segoe UI", 26F, FontStyle.Bold),
                ForeColor = _colorText,
                Size = new Size(panelW - 20, 50),
                Location = new Point(10, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _loginPanel.Controls.Add(lblWelcome);
            y += 55;

            // Subtitle
            var lblSubtitle = new Label
            {
                Text = "Sign in to access your tokens",
                Font = new Font("Segoe UI", 12F),
                ForeColor = _colorTextDim,
                Size = new Size(panelW - 20, 30),
                Location = new Point(10, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _loginPanel.Controls.Add(lblSubtitle);
            y += 45;

            // ===== GOOGLE SIGN IN BUTTON =====
            var btnGoogle = new Button
            {
                Text = "Continue with Google",
                Size = new Size(btnW, 55),
                Location = new Point(padL, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorGoogleBtn,
                ForeColor = Color.FromArgb(50, 50, 50),
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnGoogle.FlatAppearance.BorderSize = 1;
            btnGoogle.FlatAppearance.BorderColor = _colorBorder;
            btnGoogle.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 240, 240);
            btnGoogle.Click += BtnGoogle_Click;
            _loginPanel.Controls.Add(btnGoogle);
            y += 70;

            // Divider
            var lblDivider = new Label
            {
                Text = "───────  or sign in with email  ───────",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                Size = new Size(btnW, 28),
                Location = new Point(padL, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _loginPanel.Controls.Add(lblDivider);
            y += 38;

            // Email label
            var lblEmail = new Label
            {
                Text = "Email",
                Font = new Font("Segoe UI", 11F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Location = new Point(padL, y),
                BackColor = Color.Transparent
            };
            _loginPanel.Controls.Add(lblEmail);
            y += 28;

            // Email input
            var txtEmail = new TextBox
            {
                Name = "txtEmail",
                Size = new Size(btnW, 38),
                Location = new Point(padL, y),
                BackColor = _colorInput,
                ForeColor = _colorTextDim,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 12F),
                Text = "you@example.com"
            };
            txtEmail.GotFocus += (s, e) => { if (txtEmail.Text == "you@example.com") { txtEmail.Text = ""; txtEmail.ForeColor = _colorText; } };
            _loginPanel.Controls.Add(txtEmail);
            y += 50;

            // Password label
            var lblPassword = new Label
            {
                Text = "Password",
                Font = new Font("Segoe UI", 11F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Location = new Point(padL, y),
                BackColor = Color.Transparent
            };
            _loginPanel.Controls.Add(lblPassword);
            y += 28;

            // Password input
            var txtPassword = new TextBox
            {
                Name = "txtPassword",
                Size = new Size(btnW, 38),
                Location = new Point(padL, y),
                BackColor = _colorInput,
                ForeColor = _colorText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 12F),
                UseSystemPasswordChar = true
            };
            _loginPanel.Controls.Add(txtPassword);
            y += 55;

            // Sign In button
            var btnSignIn = new Button
            {
                Text = "Sign In",
                Size = new Size(btnW, 52),
                Location = new Point(padL, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorRed,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSignIn.FlatAppearance.BorderSize = 0;
            btnSignIn.Click += (s, e) => 
            {
                MessageBox.Show(
                    "Email/password login coming soon.\n\nPlease use 'Continue with Google'.",
                    "Coming Soon",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            };
            _loginPanel.Controls.Add(btnSignIn);

            _contentPanel.Controls.Add(_loginPanel);
        }

        private Image CreateGoogleIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                
                // Simplified Google "G" icon
                var center = size / 2f;
                var radius = size * 0.4f;
                
                // Blue arc (right side)
                using (var pen = new Pen(Color.FromArgb(66, 133, 244), size * 0.15f))
                    g.DrawArc(pen, center - radius, center - radius, radius * 2, radius * 2, -45, 90);
                
                // Green arc (bottom right)
                using (var pen = new Pen(Color.FromArgb(52, 168, 83), size * 0.15f))
                    g.DrawArc(pen, center - radius, center - radius, radius * 2, radius * 2, 45, 90);
                
                // Yellow arc (bottom left)
                using (var pen = new Pen(Color.FromArgb(251, 188, 5), size * 0.15f))
                    g.DrawArc(pen, center - radius, center - radius, radius * 2, radius * 2, 135, 90);
                
                // Red arc (top)
                using (var pen = new Pen(Color.FromArgb(234, 67, 53), size * 0.15f))
                    g.DrawArc(pen, center - radius, center - radius, radius * 2, radius * 2, 225, 90);
            }
            return bmp;
        }

        // ============ WAITING PANEL (Step 2) ============
        private void CreateWaitingPanel()
        {
            _waitingPanel = new Panel
            {
                Size = new Size(420, 400),
                BackColor = _colorPanel,
                Visible = false
            };

            var y = 40;

            // Logo
            var logoPic = new PictureBox
            {
                Size = new Size(100, 100),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = CreatePatsKillerLogo(100),
                BackColor = Color.Transparent
            };
            logoPic.Location = new Point((_waitingPanel.Width - 100) / 2, y);
            _waitingPanel.Controls.Add(logoPic);
            y += 120;

            // Title
            var lblTitle = new Label
            {
                Text = "Complete Sign In",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblTitle.Location = new Point((_waitingPanel.Width - lblTitle.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblTitle);
            // spacing after title
            y += lblTitle.Height + 24;

            // Message
            var lblMsg = new Label
            {
                Text = "A browser window has opened.\nPlease sign in with Google to continue.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            lblMsg.Location = new Point((_waitingPanel.Width - lblMsg.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblMsg);
            // spacing after message
            y += lblMsg.Height + 28;

            // Animated dots container
            var dotsPanel = new Panel
            {
                Size = new Size(80, 20),
                Location = new Point((_waitingPanel.Width - 80) / 2, y),
                BackColor = Color.Transparent
            };
            var dot1 = new Panel { Size = new Size(14, 14), Location = new Point(5, 3), BackColor = _colorRed };
            var dot2 = new Panel { Size = new Size(14, 14), Location = new Point(30, 3), BackColor = _colorRed };
            var dot3 = new Panel { Size = new Size(14, 14), Location = new Point(55, 3), BackColor = _colorRed };
            
            // Make dots circular
            dot1.Paint += (s, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillEllipse(new SolidBrush(_colorRed), 0, 0, 14, 14); };
            dot2.Paint += (s, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillEllipse(new SolidBrush(_colorRed), 0, 0, 14, 14); };
            dot3.Paint += (s, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillEllipse(new SolidBrush(_colorRed), 0, 0, 14, 14); };
            
            dotsPanel.Controls.AddRange(new Control[] { dot1, dot2, dot3 });
            _waitingPanel.Controls.Add(dotsPanel);

            // Animation timer
            var animTimer = new System.Windows.Forms.Timer { Interval = 300 };
            var animState = 0;
            animTimer.Tick += (s, e) =>
            {
                dot1.BackColor = animState == 0 ? _colorRed : Color.FromArgb(80, _colorRed);
                dot2.BackColor = animState == 1 ? _colorRed : Color.FromArgb(80, _colorRed);
                dot3.BackColor = animState == 2 ? _colorRed : Color.FromArgb(80, _colorRed);
                dot1.Invalidate();
                dot2.Invalidate();
                dot3.Invalidate();
                animState = (animState + 1) % 3;
            };
            animTimer.Start();
            // spacing after animated dots
            y += dotsPanel.Height + 18;

            // "Waiting for authentication..."
            var lblWaiting = new Label
            {
                Text = "Waiting for authentication...",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblWaiting.Location = new Point((_waitingPanel.Width - lblWaiting.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblWaiting);
            y += lblWaiting.Height + 18;

            // Reopen Browser button
            var btnReopen = new Button
            {
                Text = "🌐  Reopen Browser",
                Size = new Size(280, 45),
                Location = new Point((_waitingPanel.Width - 280) / 2, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorInput,
                ForeColor = _colorText,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnReopen.FlatAppearance.BorderColor = _colorBorder;
            btnReopen.FlatAppearance.BorderSize = 1;
            btnReopen.Click += BtnReopenBrowser_Click;
            _waitingPanel.Controls.Add(btnReopen);
            y += btnReopen.Height + 16;

            // Cancel link
            var lblCancel = new Label
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            lblCancel.Location = new Point((_waitingPanel.Width - lblCancel.PreferredWidth) / 2, y);
            lblCancel.Click += (s, e) =>
            {
                _cts?.Cancel();
                Logger.Info("Login cancelled");
                ShowLoginState();
            };
            lblCancel.MouseEnter += (s, e) => lblCancel.ForeColor = _colorText;
            lblCancel.MouseLeave += (s, e) => lblCancel.ForeColor = _colorTextDim;
            _waitingPanel.Controls.Add(lblCancel);

            _contentPanel.Controls.Add(_waitingPanel);
        }

        // ============ SUCCESS PANEL (Step 3) ============
        private void CreateSuccessPanel()
        {
            _successPanel = new Panel
            {
                Size = new Size(420, 420),
                BackColor = _colorPanel,
                Visible = false
            };

            var y = 40;

            // Success checkmark circle
            var successIcon = new Panel
            {
                Size = new Size(96, 96),
                Location = new Point((_successPanel.Width - 96) / 2, y),
                BackColor = Color.Transparent
            };
            successIcon.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                
                // Gradient green circle
                using (var brush = new LinearGradientBrush(
                    new Point(0, 0), new Point(96, 96),
                    Color.FromArgb(76, 175, 80), Color.FromArgb(56, 142, 60)))
                {
                    e.Graphics.FillEllipse(brush, 0, 0, 94, 94);
                }
                
                // White checkmark
                using (var pen = new Pen(Color.White, 6))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(pen, 25, 50, 42, 67);
                    e.Graphics.DrawLine(pen, 42, 67, 72, 32);
                }
            };
            _successPanel.Controls.Add(successIcon);
            // spacing after success icon
            y += successIcon.Height + 20;

            // "Welcome!"
            var lblWelcome = new Label
            {
                Text = "Welcome!",
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblWelcome.Location = new Point((_successPanel.Width - lblWelcome.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblWelcome);
            y += lblWelcome.Height + 18;

            // "Signed in as"
            var lblSignedAs = new Label
            {
                Text = "Signed in as",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblSignedAs.Location = new Point((_successPanel.Width - lblSignedAs.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblSignedAs);
            y += 25;

            // Email (dynamic)
            var lblEmail = new Label
            {
                Name = "lblSuccessEmail",
                Text = "user@example.com",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblEmail.Location = new Point((_successPanel.Width - lblEmail.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblEmail);
            y += lblEmail.Height + 22;

            // Token count box
            var tokenBox = new Panel
            {
                Size = new Size(340, 60),
                Location = new Point((_successPanel.Width - 340) / 2, y),
                BackColor = _colorInput
            };
            tokenBox.Paint += (s, e) =>
            {
                using var pen = new Pen(_colorBorder, 1);
                using var path = GetRoundedRectPath(tokenBox.ClientRectangle, 12);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(pen, path);
            };

            var lblTokenLabel = new Label
            {
                Text = "Available Tokens",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                Location = new Point(20, 20),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            tokenBox.Controls.Add(lblTokenLabel);

            var lblTokenCount = new Label
            {
                Name = "lblSuccessTokens",
                Text = "0",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = _colorGreen,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblTokenCount.Location = new Point(tokenBox.Width - lblTokenCount.PreferredWidth - 25, 15);
            tokenBox.Controls.Add(lblTokenCount);

            _successPanel.Controls.Add(tokenBox);
            y += 80;

            // Start Programming button
            var btnStart = new Button
            {
                Text = "Start Programming",
                Size = new Size(340, 55),
                Location = new Point((_successPanel.Width - 340) / 2, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorRed,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            _successPanel.Controls.Add(btnStart);

            _contentPanel.Controls.Add(_successPanel);
        }

        // ============ ERROR PANEL (Step 4) ============
        private void CreateErrorPanel()
        {
            _errorPanel = new Panel
            {
                Size = new Size(420, 380),
                BackColor = _colorPanel,
                Visible = false
            };

            var y = 40;

            // Error X circle
            var errorIcon = new Panel
            {
                Size = new Size(96, 96),
                Location = new Point((_errorPanel.Width - 96) / 2, y),
                BackColor = Color.Transparent
            };
            errorIcon.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                
                // Gradient red circle
                using (var brush = new LinearGradientBrush(
                    new Point(0, 0), new Point(96, 96),
                    Color.FromArgb(244, 67, 54), Color.FromArgb(211, 47, 47)))
                {
                    e.Graphics.FillEllipse(brush, 0, 0, 94, 94);
                }
                
                // White X
                using (var pen = new Pen(Color.White, 6))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(pen, 30, 30, 64, 64);
                    e.Graphics.DrawLine(pen, 64, 30, 30, 64);
                }
            };
            _errorPanel.Controls.Add(errorIcon);
            // spacing after icon
            y += errorIcon.Height + 20;

            // "Sign In Failed"
            var lblTitle = new Label
            {
                Text = "Sign In Failed",
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblTitle.Location = new Point((_errorPanel.Width - lblTitle.PreferredWidth) / 2, y);
            _errorPanel.Controls.Add(lblTitle);
            y += lblTitle.Height + 18;

            // Error message
            var lblMsg = new Label
            {
                Name = "lblErrorMsg",
                Text = "Authentication was cancelled or timed out.\nPlease try again.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            lblMsg.Location = new Point((_errorPanel.Width - lblMsg.PreferredWidth) / 2, y);
            _errorPanel.Controls.Add(lblMsg);
            y += lblMsg.Height + 26;

            // Try Again button
            var btnRetry = new Button
            {
                Text = "Try Again",
                Size = new Size(300, 55),
                Location = new Point((_errorPanel.Width - 300) / 2, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorRed,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRetry.FlatAppearance.BorderSize = 0;
            btnRetry.Click += async (s, e) => await StartGoogleAuthAsync();
            _errorPanel.Controls.Add(btnRetry);
            y += btnRetry.Height + 16;

            // Back to Login link
            var lblBack = new Label
            {
                Text = "Back to Login",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
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
            CenterPanel(_waitingPanel);
        }

        private void ShowSuccessState(string email, int tokens)
        {
            // Update email label
            foreach (Control c in _successPanel.Controls)
            {
                if (c.Name == "lblSuccessEmail")
                {
                    c.Text = email;
                    c.Location = new Point((_successPanel.Width - c.PreferredSize.Width) / 2, c.Location.Y);
                }
            }
            
            // Update token count
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
            AutoCloseAfterSuccess();
        }

        private async void AutoCloseAfterSuccess()
        {
            // Auto-start the main app: briefly show success state, then close this dialog.
            try
            {
                await Task.Delay(350);
                if (IsDisposed) return;

                // If the user manually closed it already, don't fight them.
                if (!Visible) return;

                DialogResult = DialogResult.OK;
                Close();
            }
            catch
            {
                // Ignore any race conditions during shutdown.
            }
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
        private async void BtnGoogle_Click(object? sender, EventArgs e)
        {
            await StartGoogleAuthAsync();
        }

        private void BtnReopenBrowser_Click(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentSessionCode))
            {
                var url = AUTH_PAGE_URL + _currentSessionCode;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        }

        // ============ AUTH FLOW ============
        private async Task StartGoogleAuthAsync()
        {
            try
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                Logger.Info("Opening Google login...");

                // Create session
                var session = await CreateSessionAsync();
                if (session == null)
                {
                    ShowErrorState("Could not connect to server.\nPlease check your internet connection.");
                    return;
                }

                _currentSessionCode = session.SessionCode;

                // Open browser to auth page
                var url = AUTH_PAGE_URL + session.SessionCode;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                // Show waiting state
                ShowWaitingState();

                // Start polling
                await PollForCompletionAsync(session.SessionCode, _cts.Token);
            }
            catch (Exception ex)
            {
                Logger.Error("StartGoogleAuthAsync error", ex);
                ShowErrorState("Failed to start authentication.\nPlease try again.");
            }
        }

        // ============ API CALLS ============
        private async Task<SessionInfo?> CreateSessionAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/create-desktop-auth-session");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                
                var body = JsonSerializer.Serialize(new { machineId = _machineId });
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                Logger.Info($"CreateSession response: {response.StatusCode} - {json}");

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error($"CreateSession failed: {response.StatusCode} - {json}");
                    return null;
                }

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var sessionCode = root.TryGetProperty("sessionCode", out var sc) ? sc.GetString() ?? "" : "";
                var expiresAt = root.TryGetProperty("expiresAt", out var ea) ? ea.GetString() ?? "" : "";
                
                if (string.IsNullOrEmpty(sessionCode))
                {
                    Logger.Error($"CreateSession: No sessionCode in response: {json}");
                    return null;
                }
                
                return new SessionInfo
                {
                    SessionCode = sessionCode,
                    ExpiresAt = expiresAt
                };
            }
            catch (Exception ex)
            {
                Logger.Error("CreateSessionAsync error", ex);
                return null;
            }
        }

        private async Task PollForCompletionAsync(string sessionCode, CancellationToken ct)
        {
            try
            {
                var timeout = DateTime.UtcNow.AddMinutes(5); // 5 minute timeout
                
                while (!ct.IsCancellationRequested && DateTime.UtcNow < timeout)
                {
                    await Task.Delay(2000, ct);

                    var result = await CheckSessionAsync(sessionCode);
                    
                    if (result == null)
                        continue;

                    if (result.Status == "complete" && !string.IsNullOrEmpty(result.Token))
                    {
                        AuthToken = result.Token;
                        RefreshToken = result.RefreshToken;
                        UserEmail = result.Email;
                        TokenCount = result.TokenCount;
                        
                        Logger.Info($"Login successful: {result.Email}");

                        this.BeginInvoke(new Action(() => ShowSuccessState(result.Email ?? "User", result.TokenCount)));
                        return;
                    }
                    else if (result.Status == "expired" || result.Status == "invalid")
                    {
                        this.BeginInvoke(new Action(() => ShowErrorState("Session expired.\nPlease try again.")));
                        return;
                    }
                }
                
                // Timeout
                if (!ct.IsCancellationRequested)
                {
                    this.BeginInvoke(new Action(() => ShowErrorState("Authentication timed out.\nPlease try again.")));
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled by user
            }
            catch (Exception ex)
            {
                Logger.Error("PollForCompletionAsync error", ex);
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

                Logger.Debug($"CheckSession response: {response.StatusCode} - {json}");

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warning($"CheckSession failed: {response.StatusCode} - {json}");
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
                    
                    // Try to get token count (if available)
                    // TODO: Fetch token count from a separate API endpoint
                    result.TokenCount = 0;
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("CheckSessionAsync error", ex);
                return null;
            }
        }

        // ============ HELPERS ============
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

        // ============ HELPER CLASSES ============
        private class SessionInfo
        {
            public string SessionCode { get; set; } = "";
            public string ExpiresAt { get; set; } = "";
        }

        private class SessionResult
        {
            public string Status { get; set; } = "";
            public string? Token { get; set; }
            public string? RefreshToken { get; set; }
            public string? Email { get; set; }
            public int TokenCount { get; set; }
        }
    }
}