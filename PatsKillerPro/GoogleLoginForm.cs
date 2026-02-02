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

namespace PatsKillerPro
{
    /// <summary>
    /// Professional OAuth login form matching PatsKiller Pro mockup v2
    /// Features: Google OAuth primary, Email/password secondary, No QR code
    /// </summary>
    public class GoogleLoginForm : Form
    {
        // ============ LOCAL LOGGER (BUILD-SAFE) ============
        private static class Logger
        {
            public static void Info(string message) => Write("INFO", message, null);
            public static void Debug(string message) => Write("DEBUG", message, null);
            public static void Warning(string message) => Write("WARN", message, null);
            public static void Error(string message) => Write("ERROR", message, null);
            public static void Error(string message, Exception ex) => Write("ERROR", message, ex);

            private static void Write(string level, string message, Exception? ex)
            {
                try
                {
                    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                    if (ex != null) line += $"\n{ex}";
                    System.Diagnostics.Trace.WriteLine(line);
                }
                catch { }
            }
        }

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
        private FlowLayoutPanel _loginStack = null!;
        private Panel _waitingPanel = null!;
        private Panel _successPanel = null!;
        private Panel _errorPanel = null!;

        // ============ THEME COLORS (Matching Mockup) ============
        private readonly Color _colorBackground = ColorTranslator.FromHtml("#111827");     // Top (deep navy)
        private readonly Color _colorBackground2 = ColorTranslator.FromHtml("#0B1220");    // Bottom (near-black navy)

        private readonly Color _colorHeader = ColorTranslator.FromHtml("#2D3346");
        private readonly Color _colorHeader2 = ColorTranslator.FromHtml("#242A3B");
        private readonly Color _colorHeaderBorder = ColorTranslator.FromHtml("#3A4158");

        private readonly Color _colorPanel = ColorTranslator.FromHtml("#1B2433");
        private readonly Color _colorInput = ColorTranslator.FromHtml("#1F2937");
        private readonly Color _colorBorder = ColorTranslator.FromHtml("#374151");

        private readonly Color _colorText = ColorTranslator.FromHtml("#F8FAFC");
        private readonly Color _colorTextDim = ColorTranslator.FromHtml("#CBD5E1");     // secondary text
        private readonly Color _colorTextMuted = ColorTranslator.FromHtml("#94A3B8");   // tertiary text
        private readonly Color _colorLabelText = ColorTranslator.FromHtml("#D1D5DB");   // field labels

        private readonly Color _colorRed = ColorTranslator.FromHtml("#E94796");            // PatsKiller Pink
        private readonly Color _colorRedDark = ColorTranslator.FromHtml("#DB2777");        // Button Pink (darker)
        private readonly Color _colorGreen = ColorTranslator.FromHtml("#22C55E");          // Success Green
        private readonly Color _colorGoogleBtn = Color.White;                              // Google button surface

        // ============ RESULTS ============
        public string? AuthToken { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? UserEmail { get; private set; }
        public int TokenCount { get; private set; }

        // ============ API CONFIGURATION ============
        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjacx.supabase.co";
        
        // Split string to prevent "Newline in constant" build errors during copy/paste
        private const string SUPABASE_ANON_KEY = 
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0" +
            ".iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";
            
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
            ClientSize = new Size(460, 660);
            MinimumSize = new Size(460, 660);
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

            try
            {
                var attribute = 20; // DWMWA_USE_IMMERSIVE_DARK_MODE
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

            FormClosing += (s, e) => _cts?.Cancel();
            Load += (s, e) => { EnforceDefaultWindowSize(); UpdateResponsiveLayout(); CenterActivePanel(); };
            Shown += (s, e) => { EnforceDefaultWindowSize(); UpdateResponsiveLayout(); CenterActivePanel(); };
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
                Font = new Font("Segoe UI", 20.5F, FontStyle.Bold),
                ForeColor = _colorRed,
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                UseCompatibleTextRendering = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _headerPanel.Controls.Add(_lblTitle);

            _headerPanel.Layout += (s, e) =>
            {
                var x = _logoBox.Right + 14;
                var w = Math.Max(10, _headerPanel.ClientSize.Width - x - 18);
                _lblTitle.Bounds = new Rectangle(x, 0, w, _headerPanel.Height);
            };

            // Token/Status labels
            _lblTokens = new Label { Visible = false, AutoSize = true, BackColor = Color.Transparent };
            _lblStatus = new Label { Visible = false, AutoSize = true, BackColor = Color.Transparent };
            _headerPanel.Controls.Add(_lblTokens);
            _headerPanel.Controls.Add(_lblStatus);

            _lblSubtitle = new Label { Visible = false };
            Controls.Add(_headerPanel);
        }

        private void CreateContentPanel()
        {
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(24, 10, 24, 24),
                AutoScroll = false // FIX: Disabled scrolling to keep UI clean
            };

            _contentPanel.Resize += (s, e) =>
            {
                UpdateResponsiveLayout();
                CenterActivePanel();
            };

            Controls.Add(_contentPanel);
        }

        private readonly Size _desiredClientSize = new Size(460, 660);

        private void EnforceDefaultWindowSize()
        {
            try
            {
                if (ClientSize.Width < _desiredClientSize.Width || ClientSize.Height < _desiredClientSize.Height)
                {
                    ClientSize = _desiredClientSize;
                }
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;
                MinimizeBox = true;
                MinimumSize = new Size(_desiredClientSize.Width, _desiredClientSize.Height);
            }
            catch { }
        }

        private void LoadLogo()
        {
            try
            {
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
                }
            }
            return bmp;
        }

        private Image CreateGoogleGIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var stroke = Math.Max(2f, size * 0.18f);
            var inset = stroke / 2f;
            var rect = new RectangleF(inset, inset, size - stroke, size - stroke);

            using var blue = new Pen(Color.FromArgb(66, 133, 244), stroke);
            using var red = new Pen(Color.FromArgb(234, 67, 53), stroke);
            using var yellow = new Pen(Color.FromArgb(251, 188, 5), stroke);
            using var green = new Pen(Color.FromArgb(52, 168, 83), stroke);

            g.DrawArc(red, rect, 315, 90);
            g.DrawArc(yellow, rect, 45, 90);
            g.DrawArc(green, rect, 135, 80);
            g.DrawArc(blue, rect, 215, 105);

            return bmp;
        }

        // ============ LOGIN PANEL ============
        private void CreateLoginPanel()
        {
            _loginPanel = new Panel
            {
                BackColor = Color.Transparent,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Visible = false
            };

            _loginStack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            _loginPanel.Controls.Add(_loginStack);

            // ===== Welcome (Reduced Size) =====
            var lblWelcome = new Label
            {
                Name = "lblWelcome",
                Text = "Welcome",
                Font = new Font("Segoe UI", 32F, FontStyle.Italic), // Reduced to fit
                ForeColor = _colorText,
                BackColor = Color.Transparent,
                AutoSize = false,
                Height = 65,
                UseCompatibleTextRendering = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 0, 0)
            };

            // ===== Subtitle =====
            var lblSubtitle = new Label
            {
                Name = "lblLoginSubtitle",
                Text = "Sign in to access your account",
                Font = new Font("Segoe UI", 11F),
                ForeColor = _colorTextDim,
                BackColor = Color.Transparent,
                AutoSize = false,
                Height = 25,
                UseCompatibleTextRendering = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 0, 20)
            };

            // ===== Google Button =====
            var btnGoogle = CreateRoundedButton("Continue with Google", 10, 48, 10);
            btnGoogle.Name = "btnGoogle";
            btnGoogle.BackColor = _colorGoogleBtn;
            btnGoogle.ForeColor = Color.FromArgb(55, 65, 81);
            btnGoogle.Font = new Font("Segoe UI Semibold", 11F);
            btnGoogle.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 210);
            btnGoogle.FlatAppearance.BorderSize = 1;
            btnGoogle.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 245, 245);
            btnGoogle.Image = CreateGoogleGIcon(18);
            btnGoogle.ImageAlign = ContentAlignment.MiddleLeft;
            btnGoogle.TextImageRelation = TextImageRelation.ImageBeforeText;
            btnGoogle.Padding = new Padding(12, 0, 12, 0);
            btnGoogle.Margin = new Padding(0, 0, 0, 15);
            btnGoogle.Click += BtnGoogle_Click;

            // ===== Divider =====
            var divider = CreateDividerRow();
            divider.Name = "dividerRow";
            divider.Margin = new Padding(0, 0, 0, 10);

            // ===== Email Label + Box =====
            var lblEmail = CreateFieldLabel("Email");
            lblEmail.Name = "lblEmail";
            lblEmail.Margin = new Padding(0, 0, 0, 4);

            var emailBox = CreateInputBox("you@example.com", isPassword: false);
            emailBox.Name = "emailBox";
            emailBox.Margin = new Padding(0, 0, 0, 10);

            // ===== Password Label + Box =====
            var lblPassword = CreateFieldLabel("Password");
            lblPassword.Name = "lblPassword";
            lblPassword.Margin = new Padding(0, 0, 0, 4);

            var passwordBox = CreateInputBox("", isPassword: true);
            passwordBox.Name = "passwordBox";
            passwordBox.Margin = new Padding(0, 0, 0, 20);

            // ===== Sign In =====
            var btnSignIn = CreateRoundedButton("Sign In", 10, 50, 10);
            btnSignIn.Name = "btnSignIn";
            btnSignIn.BackColor = _colorRedDark;
            btnSignIn.ForeColor = Color.White;
            btnSignIn.Font = new Font("Segoe UI Semibold", 12.5F);
            btnSignIn.FlatAppearance.BorderSize = 0;
            btnSignIn.FlatAppearance.MouseOverBackColor = _colorRed;
            btnSignIn.Margin = new Padding(0, 0, 0, 0);
            btnSignIn.Click += BtnSignIn_Click;

            _loginStack.Controls.Add(lblWelcome);
            _loginStack.Controls.Add(lblSubtitle);
            _loginStack.Controls.Add(btnGoogle);
            _loginStack.Controls.Add(divider);
            _loginStack.Controls.Add(lblEmail);
            _loginStack.Controls.Add(emailBox);
            _loginStack.Controls.Add(lblPassword);
            _loginStack.Controls.Add(passwordBox);
            _loginStack.Controls.Add(btnSignIn);

            _contentPanel.Controls.Add(_loginPanel);

            UpdateResponsiveLayout();
        }

        private Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorLabelText,
                BackColor = Color.Transparent,
                AutoSize = false,
                Height = 18,
                UseCompatibleTextRendering = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Panel CreateDividerRow()
        {
            var row = new Panel { BackColor = Color.Transparent, Height = 26 };
            var lbl = new Label
            {
                Text = "or sign in with email",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextMuted,
                AutoSize = true,
                BackColor = Color.Transparent,
                UseCompatibleTextRendering = true
            };
            row.Controls.Add(lbl);

            row.Resize += (s, e) =>
            {
                lbl.Location = new Point((row.Width - lbl.Width) / 2, (row.Height - lbl.Height) / 2);
                row.Invalidate();
            };

            row.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var midY = row.Height / 2;
                var gap = 14;
                var leftEnd = lbl.Left - gap;
                var rightStart = lbl.Right + gap;

                using var pen = new Pen(Color.FromArgb(110, 82, 105), 1);
                if (leftEnd > 0) e.Graphics.DrawLine(pen, 0, midY, leftEnd, midY);
                if (rightStart < row.Width) e.Graphics.DrawLine(pen, rightStart, midY, row.Width, midY);
            };

            return row;
        }

        private Panel CreateInputBox(string placeholder, bool isPassword)
        {
            var container = new Panel { BackColor = Color.Transparent, Height = 50 };
            const int radius = 10;
            const int pillSize = 30;
            var isFocused = false;

            var borderPanel = new Panel { BackColor = Color.Transparent, Dock = DockStyle.Fill };

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

            var txt = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = _colorInput,
                ForeColor = _colorText,
                Font = new Font("Segoe UI", 12F),
                PlaceholderText = placeholder,
                UseSystemPasswordChar = isPassword
            };

            txt.Enter += (s, e) => { isFocused = true; borderPanel.Invalidate(); };
            txt.Leave += (s, e) => { isFocused = false; borderPanel.Invalidate(); };

            var btnPill = new Button
            {
                Size = new Size(pillSize, pillSize),
                Text = "",
                BackColor = _colorRedDark,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnPill.FlatAppearance.BorderSize = 0;
            btnPill.Region = CreateRoundedRegion(pillSize, pillSize, 10);
            btnPill.Click += (s, e) => { if (isPassword) { txt.UseSystemPasswordChar = !txt.UseSystemPasswordChar; } txt.Focus(); };

            borderPanel.Controls.Add(txt);
            borderPanel.Controls.Add(btnPill);

            borderPanel.Layout += (s, e) =>
            {
                btnPill.Location = new Point(borderPanel.Width - 12 - pillSize, (borderPanel.Height - pillSize) / 2);
                txt.Location = new Point(16, (borderPanel.Height - txt.PreferredHeight) / 2);
                txt.Width = Math.Max(10, btnPill.Left - 20);
            };

            container.Controls.Add(borderPanel);
            return container;
        }

        private void UpdateResponsiveLayout()
        {
            if (_contentPanel == null || _loginStack == null || _loginPanel == null) return;

            EnforceDefaultWindowSize();

            // Calculate width conservatively to prevent horizontal scrollbar
            var w = _contentPanel.ClientSize.Width - 60;
            w = Math.Max(280, Math.Min(380, w));

            _loginPanel.SuspendLayout();
            _loginStack.SuspendLayout();

            _loginPanel.Width = w;
            _loginStack.Width = w;

            foreach (Control c in _loginStack.Controls)
            {
                c.Width = w;
                // Force centered labels to maintain correct width
                if (c is Label lbl && (lbl.Name == "lblWelcome" || lbl.Name == "lblLoginSubtitle"))
                {
                    lbl.Width = w;
                }
            }

            var btnGoogle = _loginStack.Controls["btnGoogle"] as Button;
            if (btnGoogle != null) btnGoogle.Region = CreateRoundedRegion(btnGoogle.Width, btnGoogle.Height, 10);

            var btnSignIn = _loginStack.Controls["btnSignIn"] as Button;
            if (btnSignIn != null) btnSignIn.Region = CreateRoundedRegion(btnSignIn.Width, btnSignIn.Height, 10);

            _loginStack.ResumeLayout(true);
            _loginPanel.ResumeLayout(true);
        }

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
            var diameter = radius * 2;
            var arcRect = new Rectangle(rect.Location, new Size(diameter, diameter));
            path.AddArc(arcRect, 180, 90);
            arcRect.X = rect.Right - diameter;
            path.AddArc(arcRect, 270, 90);
            arcRect.Y = rect.Bottom - diameter;
            path.AddArc(arcRect, 0, 90);
            arcRect.X = rect.Left;
            path.AddArc(arcRect, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ============ WAITING PANEL ============
        private void CreateWaitingPanel()
        {
            _waitingPanel = new Panel { Size = new Size(372, 300), BackColor = Color.Transparent, Visible = false };
            var y = 20; var panelW = _waitingPanel.Width;

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
            _waitingPanel.Controls.Add(lblSpinner); y += 90;

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
            _waitingPanel.Controls.Add(lblWaiting); y += 40;

            var lblInfo = new Label
            {
                Text = "Complete sign in in your browser.\nThis window will update automatically.",
                Font = new Font("Segoe UI", 11F),
                ForeColor = _colorLabelText,
                Size = new Size(panelW, 50),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _waitingPanel.Controls.Add(lblInfo); y += 70;

            var btnCancel = CreateRoundedButton("Cancel", 160, 42, 10);
            btnCancel.Location = new Point((panelW - 160) / 2, y);
            btnCancel.BackColor = _colorInput;
            btnCancel.ForeColor = _colorText;
            btnCancel.Click += (s, e) => { _cts?.Cancel(); ShowLoginState(); };
            _waitingPanel.Controls.Add(btnCancel);

            _contentPanel.Controls.Add(_waitingPanel);
        }

        // ============ SUCCESS PANEL ============
        private void CreateSuccessPanel()
        {
            _successPanel = new Panel { Size = new Size(372, 320), BackColor = Color.Transparent, Visible = false };
            var y = 20; var panelW = _successPanel.Width;

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
            _successPanel.Controls.Add(lblCheck); y += 100;

            var lblSuccess = new Label
            {
                Text = "Welcome!",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = _colorText,
                Size = new Size(panelW, 40),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _successPanel.Controls.Add(lblSuccess); y += 45;

            var lblEmail = new Label
            {
                Name = "lblSuccessEmail",
                Text = "",
                Font = new Font("Segoe UI", 12F),
                ForeColor = _colorLabelText,
                Size = new Size(panelW, 30),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _successPanel.Controls.Add(lblEmail); y += 50;

            var btnContinue = CreateRoundedButton("Continue", 200, 48, 10);
            btnContinue.Location = new Point((panelW - 200) / 2, y);
            btnContinue.BackColor = _colorGreen;
            btnContinue.ForeColor = Color.White;
            btnContinue.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            _successPanel.Controls.Add(btnContinue);

            _contentPanel.Controls.Add(_successPanel);
        }

        // ============ ERROR PANEL ============
        private void CreateErrorPanel()
        {
            _errorPanel = new Panel { Size = new Size(372, 300), BackColor = Color.Transparent, Visible = false };
            var y = 20; var panelW = _errorPanel.Width;

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
            _errorPanel.Controls.Add(lblIcon); y += 90;

            var lblError = new Label
            {
                Text = "Sign In Failed",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = _colorText,
                Size = new Size(panelW, 35),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _errorPanel.Controls.Add(lblError); y += 40;

            var lblMessage = new Label
            {
                Name = "lblErrorMessage",
                Text = "",
                Font = new Font("Segoe UI", 11F),
                ForeColor = _colorLabelText,
                Size = new Size(panelW, 50),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _errorPanel.Controls.Add(lblMessage); y += 65;

            var btnRetry = CreateRoundedButton("Try Again", 160, 44, 10);
            btnRetry.Location = new Point((panelW - 160) / 2, y);
            btnRetry.BackColor = _colorRed;
            btnRetry.ForeColor = Color.White;
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
            if (lblEmail.Length > 0) lblEmail[0].Text = email;

            _loginPanel.Visible = false;
            _waitingPanel.Visible = false;
            _successPanel.Visible = true;
            _errorPanel.Visible = false;
            CenterActivePanel();

            BeginAutoCloseOk();
        }

        private void ShowErrorState(string message)
        {
            var lblMessage = _errorPanel.Controls.Find("lblErrorMessage", true);
            if (lblMessage.Length > 0) lblMessage[0].Text = message;

            _loginPanel.Visible = false;
            _waitingPanel.Visible = false;
            _successPanel.Visible = false;
            _errorPanel.Visible = true;
            CenterActivePanel();
        }

        private void CenterActivePanel()
        {
            if (_contentPanel == null || _loginPanel == null) return;
            UpdateResponsiveLayout();

            Panel? activePanel = null;
            if (_loginPanel.Visible) activePanel = _loginPanel;
            else if (_waitingPanel.Visible) activePanel = _waitingPanel;
            else if (_successPanel.Visible) activePanel = _successPanel;
            else if (_errorPanel.Visible) activePanel = _errorPanel;

            if (activePanel == null) return;

            var clientW = _contentPanel.ClientSize.Width;
            var clientH = _contentPanel.ClientSize.Height;

            // Center manually since AutoScroll is off
            activePanel.Location = new Point(
                Math.Max(0, (clientW - activePanel.Width) / 2),
                Math.Max(0, (clientH - activePanel.Height) / 2)
            );
        }

        private void BeginAutoCloseOk(int delayMs = 900)
        {
            var t = new System.Windows.Forms.Timer { Interval = Math.Max(250, delayMs) };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                if (IsDisposed) return;
                try { DialogResult = DialogResult.OK; Close(); } catch { }
            };
            t.Start();
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

                var session = await CreateSessionAsync();
                if (session == null)
                {
                    ShowErrorState("Failed to start authentication.\nPlease check your connection.");
                    return;
                }

                _currentSessionCode = session.SessionCode;
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

        private async Task<SessionInfo?> CreateSessionAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/create-desktop-auth-session");
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                var body = JsonSerializer.Serialize(new { machineId = _machineId });
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var sessionCode = root.TryGetProperty("sessionCode", out var sc) ? sc.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(sessionCode)) return null;
                return new SessionInfo { SessionCode = sessionCode };
            }
            catch { return null; }
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
                    if (result == null) continue;

                    if (result.Status == "complete" && !string.IsNullOrEmpty(result.Token))
                    {
                        AuthToken = result.Token;
                        RefreshToken = result.RefreshToken;
                        UserEmail = result.Email;
                        TokenCount = result.TokenCount;
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
                    this.BeginInvoke(new Action(() => ShowErrorState("Authentication timed out.")));
            }
            catch { this.BeginInvoke(new Action(() => ShowErrorState("An error occurred."))); }
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
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";

                var result = new SessionResult { Status = status };
                if (status == "complete")
                {
                    result.Token = root.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
                    result.RefreshToken = root.TryGetProperty("refreshToken", out var r) ? r.GetString() : null;
                    result.Email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
                }
                return result;
            }
            catch { return null; }
        }

        private class SessionInfo { public string SessionCode { get; set; } = ""; }
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