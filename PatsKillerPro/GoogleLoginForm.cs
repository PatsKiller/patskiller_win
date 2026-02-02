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
            // Form settings - compact professional size
            this.Text = "PatsKiller Pro - Sign In";
            this.ClientSize = new Size(420, 580);
            this.MinimumSize = new Size(420, 580);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = _colorBackground;
            this.Font = new Font("Segoe UI", 10F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
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
                BackColor = _colorHeader,
                Padding = new Padding(16, 12, 16, 12)
            };

            // Logo
            _logoBox = new PictureBox
            {
                Size = new Size(48, 48),
                Location = new Point(16, 11),
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
                ForeColor = _colorRed,
                AutoSize = true,
                Location = new Point(72, 22),
                BackColor = Color.Transparent
            };
            _headerPanel.Controls.Add(_lblTitle);

            // Token/Status labels (hidden until logged in)
            _lblTokens = new Label { Visible = false, AutoSize = true, BackColor = Color.Transparent };
            _lblStatus = new Label { Visible = false, AutoSize = true, BackColor = Color.Transparent };
            _headerPanel.Controls.Add(_lblTokens);
            _headerPanel.Controls.Add(_lblStatus);
            
            // Subtitle in header (optional - hidden for cleaner look)
            _lblSubtitle = new Label { Visible = false };

            this.Controls.Add(_headerPanel);
        }

        private void CreateContentPanel()
        {
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _colorBackground,
                Padding = new Padding(24)
            };
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
                    Color.FromArgb(26, 26, 46), Color.FromArgb(22, 33, 62)))
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
                
                using (var bgBrush = new SolidBrush(Color.FromArgb(26, 26, 46)))
                {
                    g.FillEllipse(bgBrush, offsetX + keySize * 0.12f, offsetY + keySize * 0.14f, keySize * 0.25f, keySize * 0.25f);
                }
            }
            return bmp;
        }

        // ============ LOGIN PANEL (Main Login UI) ============
        private void CreateLoginPanel()
        {
            _loginPanel = new Panel
            {
                Size = new Size(372, 450),
                BackColor = _colorBackground,
                Visible = false
            };

            var y = 0;
            var panelW = _loginPanel.Width;
            var btnW = panelW;

            // "Welcome" title - italic style
            var lblWelcome = new Label
            {
                Text = "Welcome",
                Font = new Font("Segoe UI", 32F, FontStyle.Italic),
                ForeColor = _colorText,
                Size = new Size(panelW, 50),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _loginPanel.Controls.Add(lblWelcome);
            y += 50;

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
            var btnGoogle = CreateRoundedButton("Continue with Google", btnW, 48);
            btnGoogle.Location = new Point(0, y);
            btnGoogle.BackColor = _colorGoogleBtn;
            btnGoogle.ForeColor = Color.FromArgb(60, 60, 60);
            btnGoogle.Font = new Font("Segoe UI Semibold", 12F);
            btnGoogle.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            btnGoogle.FlatAppearance.BorderSize = 1;
            btnGoogle.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 245, 245);
            btnGoogle.Click += BtnGoogle_Click;
            _loginPanel.Controls.Add(btnGoogle);
            y += 68;

            // Divider with lines
            var dividerPanel = new Panel
            {
                Size = new Size(panelW, 20),
                Location = new Point(0, y),
                BackColor = Color.Transparent
            };
            dividerPanel.Paint += (s, e) =>
            {
                var lineY = 10;
                var textWidth = 140;
                var lineColor = Color.FromArgb(80, 80, 80);
                using var pen = new Pen(lineColor, 1);
                
                // Left line
                e.Graphics.DrawLine(pen, 0, lineY, (panelW - textWidth) / 2 - 10, lineY);
                // Right line
                e.Graphics.DrawLine(pen, (panelW + textWidth) / 2 + 10, lineY, panelW, lineY);
            };
            
            var lblDivider = new Label
            {
                Text = "or sign in with email",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                Size = new Size(panelW, 20),
                Location = new Point(0, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            dividerPanel.Controls.Add(lblDivider);
            _loginPanel.Controls.Add(dividerPanel);
            y += 35;

            // Email field with floating label
            var emailField = CreateFloatingLabelField("Email", "you@example.com", btnW, false);
            emailField.Location = new Point(0, y);
            emailField.Name = "emailField";
            _loginPanel.Controls.Add(emailField);
            y += 70;

            // Password field with floating label
            var passwordField = CreateFloatingLabelField("Password", "", btnW, true);
            passwordField.Location = new Point(0, y);
            passwordField.Name = "passwordField";
            _loginPanel.Controls.Add(passwordField);
            y += 80;

            // Sign In button (red)
            var btnSignIn = CreateRoundedButton("Sign In", btnW, 48);
            btnSignIn.Location = new Point(0, y);
            btnSignIn.BackColor = _colorRed;
            btnSignIn.ForeColor = Color.White;
            btnSignIn.Font = new Font("Segoe UI Semibold", 13F);
            btnSignIn.FlatAppearance.BorderSize = 0;
            btnSignIn.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 50, 80);
            btnSignIn.Click += BtnSignIn_Click;
            _loginPanel.Controls.Add(btnSignIn);

            _contentPanel.Controls.Add(_loginPanel);
        }

        /// <summary>
        /// Creates a modern floating label input field
        /// </summary>
        private Panel CreateFloatingLabelField(string labelText, string placeholder, int width, bool isPassword)
        {
            var container = new Panel
            {
                Size = new Size(width, 58),
                BackColor = Color.Transparent
            };

            // Border panel (rounded appearance simulated)
            var borderPanel = new Panel
            {
                Size = new Size(width, 50),
                Location = new Point(0, 8),
                BackColor = _colorInput
            };
            borderPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(_colorBorder, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, borderPanel.Width - 1, borderPanel.Height - 1);
            };

            // Floating label
            var lbl = new Label
            {
                Text = labelText,
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Location = new Point(10, 0),
                BackColor = _colorBackground,
                Padding = new Padding(4, 0, 4, 0)
            };
            container.Controls.Add(lbl);

            // Text input
            var txt = new TextBox
            {
                Name = "txt" + labelText.Replace(" ", ""),
                Size = new Size(width - 24, 28),
                Location = new Point(12, 11),
                BackColor = _colorInput,
                ForeColor = string.IsNullOrEmpty(placeholder) ? _colorText : _colorTextDim,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 12F),
                Text = placeholder,
                UseSystemPasswordChar = isPassword
            };

            // Placeholder behavior
            if (!string.IsNullOrEmpty(placeholder))
            {
                txt.GotFocus += (s, e) =>
                {
                    if (txt.Text == placeholder)
                    {
                        txt.Text = "";
                        txt.ForeColor = _colorText;
                    }
                };
                txt.LostFocus += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(txt.Text))
                    {
                        txt.Text = placeholder;
                        txt.ForeColor = _colorTextDim;
                    }
                };
            }

            borderPanel.Controls.Add(txt);
            container.Controls.Add(borderPanel);

            return container;
        }

        /// <summary>
        /// Creates a button with rounded corners
        /// </summary>
        private Button CreateRoundedButton(string text, int width, int height)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            
            // Create rounded region
            btn.Region = CreateRoundedRegion(width, height, 6);
            
            return btn;
        }

        private Region CreateRoundedRegion(int width, int height, int radius)
        {
            var path = new GraphicsPath();
            var rect = new Rectangle(0, 0, width, height);
            var diameter = radius * 2;
            
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            
            return new Region(path);
        }

        // ============ WAITING PANEL (Step 2) ============
        private void CreateWaitingPanel()
        {
            _waitingPanel = new Panel
            {
                Size = new Size(372, 300),
                BackColor = _colorBackground,
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
                BackColor = _colorBackground,
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
                BackColor = _colorBackground,
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
            Panel? activePanel = null;
            if (_loginPanel.Visible) activePanel = _loginPanel;
            else if (_waitingPanel.Visible) activePanel = _waitingPanel;
            else if (_successPanel.Visible) activePanel = _successPanel;
            else if (_errorPanel.Visible) activePanel = _errorPanel;

            if (activePanel != null)
            {
                var headerHeight = _headerPanel?.Height ?? 70;
                var availableHeight = this.ClientSize.Height - headerHeight;
                var x = (_contentPanel.ClientSize.Width - activePanel.Width) / 2;
                var y = (availableHeight - activePanel.Height) / 2;
                activePanel.Location = new Point(Math.Max(0, x), Math.Max(0, y));
            }
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