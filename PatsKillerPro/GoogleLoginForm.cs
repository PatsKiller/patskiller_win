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

        // ============ THEME COLORS (Matching Mockup) ============
        // Background is a subtle vertical gradient; most controls sit on transparent panels.
        private readonly Color _colorBackground = ColorTranslator.FromHtml("#111827");     // Top (deep navy)
        private readonly Color _colorBackground2 = ColorTranslator.FromHtml("#0B1220");    // Bottom (near-black navy)

        // Header is slightly lighter than body
        private readonly Color _colorHeader = ColorTranslator.FromHtml("#2D3346");
        private readonly Color _colorHeader2 = ColorTranslator.FromHtml("#242A3B");
        private readonly Color _colorHeaderBorder = ColorTranslator.FromHtml("#3A4158");

        // Surfaces / inputs
        private readonly Color _colorPanel = ColorTranslator.FromHtml("#1B2433");
        private readonly Color _colorInput = ColorTranslator.FromHtml("#1F2937");
        private readonly Color _colorBorder = ColorTranslator.FromHtml("#374151");

        // Typography
        private readonly Color _colorText = ColorTranslator.FromHtml("#F3F4F6");
        private readonly Color _colorTextDim = ColorTranslator.FromHtml("#9CA3AF");

        // Brand / status
        private readonly Color _colorRed = ColorTranslator.FromHtml("#E94796");            // PatsKiller Pink
        private readonly Color _colorRedDark = ColorTranslator.FromHtml("#DB2777");        // Button Pink (darker)
        private readonly Color _colorGreen = ColorTranslator.FromHtml("#22C55E");          // Success Green
        private readonly Color _colorGoogleBtn = Color.White;                              // Google button surface

        // ============ RESULTS ============
 ============
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
            // Form settings
            Text = "PatsKiller Pro - Sign In";
            ClientSize = new Size(420, 600);
            MinimumSize = new Size(420, 600);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = _colorBackground;
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            ResizeRedraw = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            // Enable dark title bar on Windows 10/11
            try
            {
                var attribute = 20; // DWMWA_USE_IMMERSIVE_DARK_MODE
                var value = 1;
                DwmSetWindowAttribute(this.Handle, attribute, ref value, sizeof(int));
            }
            catch { }

            // IMPORTANT: Dock order matters. Add header first, then fill content.
            CreateHeaderPanel();
            CreateContentPanel();

            CreateLoginPanel();
            CreateWaitingPanel();
            CreateSuccessPanel();
            CreateErrorPanel();

            FormClosing += (s, e) => _cts?.Cancel();
            Load += (s, e) => CenterActivePanel();
            Resize += (s, e) => CenterActivePanel();
        }

[System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var rect = this.ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                base.OnPaintBackground(e);
                return;
            }

            using var brush = new LinearGradientBrush(rect, _colorBackground, _colorBackground2, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, rect);
        }


        // ============ HEADER PANEL ============
                // ============ HEADER PANEL ============
        private void CreateHeaderPanel()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 86,
                BackColor = Color.Transparent,
                Padding = new Padding(18, 14, 18, 14)
            };

            _headerPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using var brush = new LinearGradientBrush(
                    _headerPanel.ClientRectangle,
                    _colorHeader,
                    _colorHeader2,
                    LinearGradientMode.Vertical);

                e.Graphics.FillRectangle(brush, _headerPanel.ClientRectangle);

                using var pen = new Pen(_colorHeaderBorder, 1);
                e.Graphics.DrawLine(pen, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
            };

            // Logo
            _logoBox = new PictureBox
            {
                Size = new Size(54, 54),
                Location = new Point(18, 16),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            LoadLogo();
            _headerPanel.Controls.Add(_logoBox);

            // Title
            _lblTitle = new Label
            {
                Text = "PatsKiller Pro",
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = _colorRed,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblTitle);

            _headerPanel.Layout += (s, e) =>
            {
                // Vertically center title next to logo (robust across DPI)
                var x = _logoBox.Right + 14;
                var y = (_headerPanel.Height - _lblTitle.Height) / 2;
                _lblTitle.Location = new Point(x, Math.Max(0, y));
            };

            // Token/Status labels (hidden until logged in)
            _lblTokens = new Label { Visible = false, AutoSize = true, BackColor = Color.Transparent };
            _lblStatus = new Label { Visible = false, AutoSize = true, BackColor = Color.Transparent };
            _headerPanel.Controls.Add(_lblTokens);
            _headerPanel.Controls.Add(_lblStatus);

            // Subtitle in header (unused in mockup)
            _lblSubtitle = new Label { Visible = false };

            Controls.Add(_headerPanel);
        }

        private void CreateContentPanel()
        {
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(24, 28, 24, 24)
            };

            _contentPanel.Resize += (s, e) => CenterActivePanel();
            Controls.Add(_contentPanel);
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

                _logoBox.Image = CreatePatsKillerLogo(48);
            }
            catch
            {
                _logoBox.Image = CreatePatsKillerLogo(48);
            }
        }

        private Image CreatePatsKillerLogo(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                
                using (var bgBrush = new LinearGradientBrush(
                    new Point(0, 0), new Point(size, size),
                    _colorBackground, _colorBackground2))
                {
                    g.FillRectangle(bgBrush, 0, 0, size, size);
                }
                
                using (var pen = new Pen(_colorRed, 2))
                {
                    g.DrawRectangle(pen, 1, 1, size - 3, size - 3);
                }
                
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
                
                using (var bgBrush = new SolidBrush(_colorBackground))
                {
                    g.FillEllipse(bgBrush, offsetX + keySize * 0.12f, offsetY + keySize * 0.14f, keySize * 0.25f, keySize * 0.25f);
                }
            }
            return bmp;
        }

        private Image CreateGoogleGIcon(int size)
        {
            // Simple vector-ish Google "G" (no external assets)
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var stroke = Math.Max(2f, size * 0.18f);
            var inset = stroke / 2f;
            var rect = new RectangleF(inset, inset, size - stroke, size - stroke);

            using var blue = new Pen(Color.FromArgb(66, 133, 244), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var red = new Pen(Color.FromArgb(234, 67, 53), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var yellow = new Pen(Color.FromArgb(251, 188, 5), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var green = new Pen(Color.FromArgb(52, 168, 83), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            // Approximate segments of the "G"
            g.DrawArc(red, rect, 315, 90);
            g.DrawArc(yellow, rect, 45, 90);
            g.DrawArc(green, rect, 135, 80);
            g.DrawArc(blue, rect, 215, 105);

            // Horizontal bar
            var y = size / 2f;
            g.DrawLine(blue, size * 0.52f, y, size * 0.86f, y);

            return bmp;
        }


        // ============ LOGIN PANEL (Main Login UI) ============
                // ============ LOGIN PANEL (Main Login UI) ============
        private void CreateLoginPanel()
        {
            // Size is derived from form width + padding to prevent DPI clipping.
            var panelW = Math.Max(320, ClientSize.Width - _contentPanel.Padding.Horizontal);
            _loginPanel = new Panel
            {
                Size = new Size(panelW, 470),
                BackColor = Color.Transparent,
                Visible = false
            };

            var y = 8;
            var btnW = panelW;

            // "Welcome" title (script-like)
            Font welcomeFont;
            try { welcomeFont = new Font("Segoe Script", 38F, FontStyle.Italic); }
            catch { welcomeFont = new Font("Segoe UI", 34F, FontStyle.Italic); }

            var lblWelcome = new Label
            {
                Text = "Welcome",
                Font = welcomeFont,
                ForeColor = _colorText,
                Size = new Size(panelW, 64),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _loginPanel.Controls.Add(lblWelcome);
            y += 66;

            // Subtitle
            var lblSubtitle = new Label
            {
                Text = "Sign in to access your account",
                Font = new Font("Segoe UI", 11F),
                ForeColor = _colorTextDim,
                Size = new Size(panelW, 24),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _loginPanel.Controls.Add(lblSubtitle);
            y += 40;

            // ===== GOOGLE SIGN IN BUTTON =====
            var btnGoogle = CreateRoundedButton("Continue with Google", btnW, 52, 10);
            btnGoogle.Location = new Point(0, y);
            btnGoogle.BackColor = _colorGoogleBtn;
            btnGoogle.ForeColor = Color.FromArgb(55, 65, 81); // Slate
            btnGoogle.Font = new Font("Segoe UI Semibold", 12F);
            btnGoogle.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 210);
            btnGoogle.FlatAppearance.BorderSize = 1;
            btnGoogle.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 245, 245);
            btnGoogle.Image = CreateGoogleGIcon(22);
            btnGoogle.ImageAlign = ContentAlignment.MiddleLeft;
            btnGoogle.TextImageRelation = TextImageRelation.ImageBeforeText;
            btnGoogle.Padding = new Padding(16, 0, 16, 0);
            btnGoogle.Click += BtnGoogle_Click;
            _loginPanel.Controls.Add(btnGoogle);
            y += 74;

            // Divider with lines
            var dividerPanel = new Panel
            {
                Size = new Size(panelW, 22),
                Location = new Point(0, y),
                BackColor = Color.Transparent
            };

            var lblDivider = new Label
            {
                Text = "or sign in with email",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                BackColor = Color.Transparent
            };
            dividerPanel.Controls.Add(lblDivider);

            dividerPanel.Layout += (s, e) =>
            {
                lblDivider.Location = new Point((dividerPanel.Width - lblDivider.Width) / 2, (dividerPanel.Height - lblDivider.Height) / 2);
                dividerPanel.Invalidate();
            };

            dividerPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var midY = dividerPanel.Height / 2;
                var gap = 14;
                var leftEnd = lblDivider.Left - gap;
                var rightStart = lblDivider.Right + gap;

                using var pen = new Pen(Color.FromArgb(70, 82, 105), 1); // muted blue-gray

                if (leftEnd > 0) e.Graphics.DrawLine(pen, 0, midY, leftEnd, midY);
                if (rightStart < dividerPanel.Width) e.Graphics.DrawLine(pen, rightStart, midY, dividerPanel.Width, midY);
            };

            _loginPanel.Controls.Add(dividerPanel);
            y += 38;

            // Email field
            var emailField = CreateFloatingLabelField("Email", "you@example.com", btnW, isPassword: false);
            emailField.Location = new Point(0, y);
            emailField.Name = "emailField";
            _loginPanel.Controls.Add(emailField);
            y += 76;

            // Password field
            var passwordField = CreateFloatingLabelField("Password", "", btnW, isPassword: true);
            passwordField.Location = new Point(0, y);
            passwordField.Name = "passwordField";
            _loginPanel.Controls.Add(passwordField);
            y += 86;

            // Sign In button (brand pink)
            var btnSignIn = CreateRoundedButton("Sign In", btnW, 56, 10);
            btnSignIn.Location = new Point(0, y);
            btnSignIn.BackColor = _colorRedDark;
            btnSignIn.ForeColor = Color.White;
            btnSignIn.Font = new Font("Segoe UI Semibold", 13F);
            btnSignIn.FlatAppearance.BorderSize = 0;
            btnSignIn.FlatAppearance.MouseOverBackColor = _colorRed;
            btnSignIn.Click += BtnSignIn_Click;
            _loginPanel.Controls.Add(btnSignIn);

            _contentPanel.Controls.Add(_loginPanel);
        }

        /// <summary>
        /// Creates a modern floating label input field (mockup style)
        /// </summary>
        private Panel CreateFloatingLabelField(string labelText, string placeholder, int width, bool isPassword)
        {
            var container = new Panel
            {
                Size = new Size(width, 62),
                BackColor = Color.Transparent
            };

            var radius = 10;
            var isFocused = false;

            // Floating label (sits over the border)
            var lbl = new Label
            {
                Text = labelText,
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Location = new Point(12, 0),
                BackColor = _colorBackground, // close enough against the gradient
                Padding = new Padding(4, 0, 4, 0)
            };
            container.Controls.Add(lbl);

            // Border panel (owner-painted rounded rect)
            var borderPanel = new Panel
            {
                Size = new Size(width, 54),
                Location = new Point(0, 10),
                BackColor = Color.Transparent
            };

            borderPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var rect = new Rectangle(0, 0, borderPanel.Width - 1, borderPanel.Height - 1);
                using var path = CreateRoundedRectPath(rect, radius);

                using var fill = new SolidBrush(_colorInput);
                e.Graphics.FillPath(fill, path);

                var borderColor = isFocused ? _colorRed : _colorBorder;
                using var pen = new Pen(borderColor, 1);
                e.Graphics.DrawPath(pen, path);
            };

            // Text input
            var txt = new TextBox
            {
                Name = "txt" + labelText.Replace(" ", ""),
                BorderStyle = BorderStyle.None,
                BackColor = _colorInput,
                ForeColor = _colorText,
                Font = new Font("Segoe UI", 12.5F),
                Location = new Point(16, 18),
                Size = new Size(width - 16 - 16 - 44, 24),
                PlaceholderText = placeholder,
                UseSystemPasswordChar = isPassword
            };

            txt.Enter += (s, e) => { isFocused = true; borderPanel.Invalidate(); };
            txt.Leave += (s, e) => { isFocused = false; borderPanel.Invalidate(); };

            borderPanel.Click += (s, e) => txt.Focus();
            container.Click += (s, e) => txt.Focus();
            lbl.Click += (s, e) => txt.Focus();

            // Right-side action button (mockup "..." pill)
            var btnAction = new Button
            {
                Size = new Size(34, 34),
                Location = new Point(width - 16 - 34, 10),
                Text = "⋯",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                BackColor = _colorRedDark,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnAction.FlatAppearance.BorderSize = 0;
            btnAction.Region = CreateRoundedRegion(btnAction.Width, btnAction.Height, 8);

            if (isPassword)
            {
                var shown = false;
                btnAction.Click += (s, e) =>
                {
                    shown = !shown;
                    txt.UseSystemPasswordChar = !shown;
                    txt.Focus();
                    txt.SelectionStart = txt.TextLength;
                };
            }
            else
            {
                btnAction.Click += (s, e) =>
                {
                    // Practical utility: click toggles clear / paste
                    if (!string.IsNullOrWhiteSpace(txt.Text))
                    {
                        txt.Clear();
                    }
                    else if (Clipboard.ContainsText())
                    {
                        txt.Text = Clipboard.GetText();
                        txt.SelectionStart = txt.TextLength;
                    }

                    txt.Focus();
                };
            }

            borderPanel.Controls.Add(txt);
            borderPanel.Controls.Add(btnAction);
            container.Controls.Add(borderPanel);

            return container;
        }

        /// <summary>
        /// Creates a button with rounded corners (mockup style)
        /// </summary>
        private Button CreateRoundedButton(string text, int width, int height)
            => CreateRoundedButton(text, width, height, 10);

        private Button CreateRoundedButton(string text, int width, int height, int radius)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false
            };

            btn.Region = CreateRoundedRegion(width, height, radius);

            // Keep region correct if DPI changes / control is resized
            btn.Resize += (s, e) =>
            {
                if (btn.Width > 0 && btn.Height > 0)
                    btn.Region = CreateRoundedRegion(btn.Width, btn.Height, radius);
            };

            return btn;
        }

        private Region CreateRoundedRegion(int width, int height, int radius)
        {
            var rect = new Rectangle(0, 0, width, height);
            var path = CreateRoundedRectPath(rect, radius);
            return new Region(path);
        }

        private GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }

            var diameter = radius * 2;
            var arcRect = new Rectangle(rect.Location, new Size(diameter, diameter));

            // Top-left
            path.AddArc(arcRect, 180, 90);

            // Top-right
            arcRect.X = rect.Right - diameter;
            path.AddArc(arcRect, 270, 90);

            // Bottom-right
            arcRect.Y = rect.Bottom - diameter;
            path.AddArc(arcRect, 0, 90);

            // Bottom-left
            arcRect.X = rect.Left;
            path.AddArc(arcRect, 90, 90);

            path.CloseFigure();
            return path;
        }

// ============ WAITING PANEL (Step 2) ============
        private void CreateWaitingPanel()
        {
            _waitingPanel = new Panel
            {
                Size = new Size(372, 300),
                BackColor = Color.Transparent,
                Visible = false
            };

            var y = 20;
            var panelW = _waitingPanel.Width;

            // Spinner placeholder (animated dots or spinner)
            var lblSpinner = new Label
            {
                Text = "🔄",
                Font = new Font("Segoe UI", 48F),
                Size = new Size(panelW, 80),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = _colorRed,
                BackColor = Color.Transparent
            };
            _waitingPanel.Controls.Add(lblSpinner);
            y += 90;

            var lblWaiting = new Label
            {
                Text = "Waiting for sign in...",
                Font = new Font("Segoe UI", 16F),
                ForeColor = _colorText,
                Size = new Size(panelW, 35),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _waitingPanel.Controls.Add(lblWaiting);
            y += 40;

            var lblInfo = new Label
            {
                Text = "Complete sign in in your browser.\nThis window will update automatically.",
                Font = new Font("Segoe UI", 11F),
                ForeColor = _colorTextDim,
                Size = new Size(panelW, 50),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _waitingPanel.Controls.Add(lblInfo);
            y += 70;

            var btnCancel = CreateRoundedButton("Cancel", 160, 42);
            btnCancel.Location = new Point((panelW - 160) / 2, y);
            btnCancel.BackColor = _colorInput;
            btnCancel.ForeColor = _colorText;
            btnCancel.Font = new Font("Segoe UI", 11F);
            btnCancel.FlatAppearance.BorderColor = _colorBorder;
            btnCancel.FlatAppearance.BorderSize = 1;
            btnCancel.Click += (s, e) =>
            {
                _cts?.Cancel();
                ShowLoginState();
            };
            _waitingPanel.Controls.Add(btnCancel);

            _contentPanel.Controls.Add(_waitingPanel);
        }

        // ============ SUCCESS PANEL ============
        private void CreateSuccessPanel()
        {
            _successPanel = new Panel
            {
                Size = new Size(372, 320),
                BackColor = Color.Transparent,
                Visible = false
            };

            var y = 20;
            var panelW = _successPanel.Width;

            var lblCheck = new Label
            {
                Text = "✓",
                Font = new Font("Segoe UI", 56F, FontStyle.Bold),
                ForeColor = _colorGreen,
                Size = new Size(panelW, 90),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _successPanel.Controls.Add(lblCheck);
            y += 100;

            var lblSuccess = new Label
            {
                Name = "lblSuccessTitle",
                Text = "Welcome!",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = _colorText,
                Size = new Size(panelW, 40),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _successPanel.Controls.Add(lblSuccess);
            y += 45;

            var lblEmail = new Label
            {
                Name = "lblSuccessEmail",
                Text = "",
                Font = new Font("Segoe UI", 12F),
                ForeColor = _colorTextDim,
                Size = new Size(panelW, 30),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _successPanel.Controls.Add(lblEmail);
            y += 50;

            var btnContinue = CreateRoundedButton("Continue", 200, 48);
            btnContinue.Location = new Point((panelW - 200) / 2, y);
            btnContinue.BackColor = _colorGreen;
            btnContinue.ForeColor = Color.White;
            btnContinue.Font = new Font("Segoe UI Semibold", 13F);
            btnContinue.FlatAppearance.BorderSize = 0;
            btnContinue.Click += (s, e) => this.DialogResult = DialogResult.OK;
            _successPanel.Controls.Add(btnContinue);

            _contentPanel.Controls.Add(_successPanel);
        }

        // ============ ERROR PANEL ============
        private void CreateErrorPanel()
        {
            _errorPanel = new Panel
            {
                Size = new Size(372, 300),
                BackColor = Color.Transparent,
                Visible = false
            };

            var y = 20;
            var panelW = _errorPanel.Width;

            var lblIcon = new Label
            {
                Text = "⚠",
                Font = new Font("Segoe UI", 48F),
                ForeColor = _colorRed,
                Size = new Size(panelW, 80),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _errorPanel.Controls.Add(lblIcon);
            y += 90;

            var lblError = new Label
            {
                Name = "lblErrorTitle",
                Text = "Sign In Failed",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = _colorText,
                Size = new Size(panelW, 35),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _errorPanel.Controls.Add(lblError);
            y += 40;

            var lblMessage = new Label
            {
                Name = "lblErrorMessage",
                Text = "",
                Font = new Font("Segoe UI", 11F),
                ForeColor = _colorTextDim,
                Size = new Size(panelW, 50),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _errorPanel.Controls.Add(lblMessage);
            y += 65;

            var btnRetry = CreateRoundedButton("Try Again", 160, 44);
            btnRetry.Location = new Point((panelW - 160) / 2, y);
            btnRetry.BackColor = _colorRed;
            btnRetry.ForeColor = Color.White;
            btnRetry.Font = new Font("Segoe UI Semibold", 12F);
            btnRetry.FlatAppearance.BorderSize = 0;
            btnRetry.Click += (s, e) => ShowLoginState();
            _errorPanel.Controls.Add(btnRetry);

            _contentPanel.Controls.Add(_errorPanel);
        }

        // ============ STATE MANAGEMENT ============
        private void ShowLoginState()
        {
            _loginPanel.Visible = true;
            _waitingPanel.Visible = false;
            _successPanel.Visible = false;
            _errorPanel.Visible = false;
            CenterActivePanel();
        }

        private void ShowWaitingState()
        {
            _loginPanel.Visible = false;
            _waitingPanel.Visible = true;
            _successPanel.Visible = false;
            _errorPanel.Visible = false;
            CenterActivePanel();
        }

        private void ShowSuccessState(string email, int tokenCount)
        {
            var lblEmail = _successPanel.Controls.Find("lblSuccessEmail", true);
            if (lblEmail.Length > 0)
            {
                lblEmail[0].Text = email;
            }

            _loginPanel.Visible = false;
            _waitingPanel.Visible = false;
            _successPanel.Visible = true;
            _errorPanel.Visible = false;
            CenterActivePanel();
        }

        private void ShowErrorState(string message)
        {
            var lblMessage = _errorPanel.Controls.Find("lblErrorMessage", true);
            if (lblMessage.Length > 0)
            {
                lblMessage[0].Text = message;
            }

            _loginPanel.Visible = false;
            _waitingPanel.Visible = false;
            _successPanel.Visible = false;
            _errorPanel.Visible = true;
            CenterActivePanel();
        }

                private void CenterActivePanel()
        {
            if (_contentPanel == null) return;

            Panel? activePanel = null;
            if (_loginPanel.Visible) activePanel = _loginPanel;
            else if (_waitingPanel.Visible) activePanel = _waitingPanel;
            else if (_successPanel.Visible) activePanel = _successPanel;
            else if (_errorPanel.Visible) activePanel = _errorPanel;

            if (activePanel == null) return;

            var x = (_contentPanel.ClientSize.Width - activePanel.Width) / 2;
            var y = (_contentPanel.ClientSize.Height - activePanel.Height) / 2;

            activePanel.Location = new Point(Math.Max(0, x), Math.Max(0, y));
        }

// ============ EVENT HANDLERS ============
        private async void BtnGoogle_Click(object? sender, EventArgs e)
        {
            await StartGoogleAuthAsync();
        }

        private void BtnSignIn_Click(object? sender, EventArgs e)
        {
            MessageBox.Show(
                "Email/password login coming soon.\n\nPlease use 'Continue with Google' for now.",
                "Coming Soon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        // ============ GOOGLE AUTH FLOW ============
        private async Task StartGoogleAuthAsync()
        {
            try
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                Logger.Info("Starting Google OAuth flow...");

                var session = await CreateSessionAsync();
                if (session == null)
                {
                    ShowErrorState("Failed to start authentication.\nPlease check your connection.");
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

                ShowWaitingState();
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
                var timeout = DateTime.UtcNow.AddMinutes(5);
                
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