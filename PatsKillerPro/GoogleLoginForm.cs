using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Utils;

namespace PatsKillerPro
{
    /// <summary>
    /// Professional Google OAuth login form using external browser + localhost callback
    /// This is how Spotify, Discord, Slack, VS Code all handle OAuth
    /// </summary>
    public class GoogleLoginForm : Form
    {
        // UI Controls
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

        // Dark theme colors (matching mockup)
        private readonly Color _colorBackground = Color.FromArgb(30, 30, 30);      // #1e1e1e
        private readonly Color _colorHeader = Color.FromArgb(37, 37, 38);          // #252526
        private readonly Color _colorPanel = Color.FromArgb(45, 45, 48);           // #2d2d30
        private readonly Color _colorInput = Color.FromArgb(60, 60, 60);           // #3c3c3c
        private readonly Color _colorBorder = Color.FromArgb(80, 80, 80);
        private readonly Color _colorText = Color.FromArgb(255, 255, 255);
        private readonly Color _colorTextDim = Color.FromArgb(150, 150, 150);
        private readonly Color _colorRed = Color.FromArgb(233, 69, 96);            // #e94560
        private readonly Color _colorGreen = Color.FromArgb(76, 175, 80);

        // Results
        public string? AuthToken { get; private set; }
        public string? UserEmail { get; private set; }

        // OAuth settings
        private const int CALLBACK_PORT = 8765;
        private const string LOGIN_URL = "https://patskiller.com/api/desktop-auth?callback=http://localhost:{0}";
        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;

        public GoogleLoginForm()
        {
            InitializeComponent();
            ShowLoginState();
        }

        private void InitializeComponent()
        {
            // Form settings
            this.Text = "PatsKiller Pro 2026 (Ford & Lincoln PATS Solution)";
            this.Size = new Size(900, 700);
            this.MinimumSize = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.BackColor = _colorBackground;
            this.Font = new Font("Segoe UI", 9F);

            // Dark title bar
            try
            {
                var attribute = 20;
                var value = 1;
                DwmSetWindowAttribute(this.Handle, attribute, ref value, sizeof(int));
            }
            catch { }

            // ============ HEADER PANEL ============
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = _colorHeader,
                Padding = new Padding(20, 15, 20, 15)
            };

            // Logo
            _logoBox = new PictureBox
            {
                Size = new Size(50, 50),
                Location = new Point(20, 15),
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
                Location = new Point(80, 15)
            };
            _headerPanel.Controls.Add(_lblTitle);

            // Subtitle
            _lblSubtitle = new Label
            {
                Text = "Ford & Lincoln PATS Solution",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Location = new Point(82, 45)
            };
            _headerPanel.Controls.Add(_lblSubtitle);

            // Tokens display (right side)
            _lblTokens = new Label
            {
                Text = "Tokens: --",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = _colorGreen,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _headerPanel.Controls.Add(_lblTokens);

            _lblStatus = new Label
            {
                Text = "Not logged in",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _headerPanel.Controls.Add(_lblStatus);

            // Position right-side labels
            _headerPanel.Resize += (s, e) =>
            {
                _lblTokens.Location = new Point(_headerPanel.Width - _lblTokens.Width - 20, 18);
                _lblStatus.Location = new Point(_headerPanel.Width - _lblStatus.Width - 20, 45);
            };

            this.Controls.Add(_headerPanel);

            // ============ CONTENT PANEL ============
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _colorBackground,
                Padding = new Padding(40)
            };
            this.Controls.Add(_contentPanel);

            // Create all state panels
            CreateLoginPanel();
            CreateWaitingPanel();
            CreateSuccessPanel();
            CreateErrorPanel();

            // Handle form closing
            this.FormClosing += (s, e) =>
            {
                _cts?.Cancel();
                StopHttpListener();
            };

            // Initial layout
            this.Load += (s, e) =>
            {
                _lblTokens.Location = new Point(_headerPanel.Width - _lblTokens.Width - 20, 18);
                _lblStatus.Location = new Point(_headerPanel.Width - _lblStatus.Width - 20, 45);
            };
        }

        private void LoadLogo()
        {
            try
            {
                // Try to load logo from various locations
                var paths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo.png"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png"),
                    "Resources/logo.png",
                    "logo.png"
                };

                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        _logoBox.Image = Image.FromFile(path);
                        return;
                    }
                }

                // If no file found, create a placeholder logo
                _logoBox.Image = CreatePlaceholderLogo();
            }
            catch
            {
                _logoBox.Image = CreatePlaceholderLogo();
            }
        }

        private Image CreatePlaceholderLogo()
        {
            var bmp = new Bitmap(50, 50);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                
                // Dark blue background with red border
                using (var brush = new SolidBrush(Color.FromArgb(26, 26, 46)))
                    g.FillRectangle(brush, 0, 0, 50, 50);
                
                using (var pen = new Pen(_colorRed, 2))
                    g.DrawRectangle(pen, 1, 1, 47, 47);

                // Draw a key shape
                using (var brush = new SolidBrush(_colorRed))
                {
                    // Key head (circle)
                    g.FillEllipse(brush, 8, 12, 18, 18);
                    // Key shaft
                    g.FillRectangle(brush, 22, 18, 22, 6);
                    // Key teeth
                    g.FillRectangle(brush, 36, 24, 3, 6);
                    g.FillRectangle(brush, 41, 24, 3, 8);
                }
                
                // Inner circle (hole in key head)
                using (var brush = new SolidBrush(Color.FromArgb(26, 26, 46)))
                    g.FillEllipse(brush, 13, 17, 8, 8);
            }
            return bmp;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // ============ LOGIN PANEL ============
        private void CreateLoginPanel()
        {
            _loginPanel = new Panel
            {
                Size = new Size(420, 520),
                BackColor = _colorPanel,
                Visible = false
            };

            // Round corners effect via region (simplified - just padding)
            _loginPanel.Padding = new Padding(30);

            var y = 30;

            // Logo at top
            var logoPic = new PictureBox
            {
                Size = new Size(80, 80),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _logoBox.Image,
                Location = new Point((_loginPanel.Width - 80) / 2, y)
            };
            _loginPanel.Controls.Add(logoPic);
            y += 100;

            // Welcome text
            var lblWelcome = new Label
            {
                Text = "Welcome Back",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblWelcome.Location = new Point((_loginPanel.Width - lblWelcome.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblWelcome);
            y += 35;

            var lblSignIn = new Label
            {
                Text = "Sign in to access your tokens",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true
            };
            lblSignIn.Location = new Point((_loginPanel.Width - lblSignIn.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblSignIn);
            y += 50;

            // Google Sign In Button
            var btnGoogle = new Button
            {
                Text = "     Continue with Google",
                Size = new Size(360, 50),
                Location = new Point(30, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(60, 60, 60),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                ImageAlign = ContentAlignment.MiddleLeft
            };
            btnGoogle.FlatAppearance.BorderSize = 0;
            
            // Add Google icon
            try
            {
                btnGoogle.Image = CreateGoogleIcon();
                btnGoogle.ImageAlign = ContentAlignment.MiddleLeft;
                btnGoogle.TextImageRelation = TextImageRelation.ImageBeforeText;
                btnGoogle.Padding = new Padding(15, 0, 0, 0);
            }
            catch { }

            btnGoogle.Click += BtnGoogle_Click;
            _loginPanel.Controls.Add(btnGoogle);
            y += 70;

            // Divider
            var lblOr = new Label
            {
                Text = "─────────  or sign in with email  ─────────",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true
            };
            lblOr.Location = new Point((_loginPanel.Width - lblOr.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblOr);
            y += 40;

            // Email field
            var lblEmail = new Label
            {
                Text = "Email",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                Location = new Point(30, y),
                AutoSize = true
            };
            _loginPanel.Controls.Add(lblEmail);
            y += 22;

            var txtEmail = new TextBox
            {
                Size = new Size(360, 35),
                Location = new Point(30, y),
                BackColor = _colorInput,
                ForeColor = _colorText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 11F)
            };
            _loginPanel.Controls.Add(txtEmail);
            y += 45;

            // Password field
            var lblPassword = new Label
            {
                Text = "Password",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                Location = new Point(30, y),
                AutoSize = true
            };
            _loginPanel.Controls.Add(lblPassword);
            y += 22;

            var txtPassword = new TextBox
            {
                Size = new Size(360, 35),
                Location = new Point(30, y),
                BackColor = _colorInput,
                ForeColor = _colorText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 11F),
                PasswordChar = '•'
            };
            _loginPanel.Controls.Add(txtPassword);
            y += 50;

            // Sign In button
            var btnSignIn = new Button
            {
                Text = "Sign In",
                Size = new Size(360, 45),
                Location = new Point(30, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorRed,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSignIn.FlatAppearance.BorderSize = 0;
            _loginPanel.Controls.Add(btnSignIn);
            y += 60;

            // Register link
            var lblRegister = new Label
            {
                Text = "Don't have an account? Register at patskiller.com",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorRed,
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            lblRegister.Location = new Point((_loginPanel.Width - lblRegister.PreferredWidth) / 2, y);
            lblRegister.Click += (s, e) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://patskiller.com/register",
                    UseShellExecute = true
                });
            };
            _loginPanel.Controls.Add(lblRegister);

            _contentPanel.Controls.Add(_loginPanel);
        }

        private Image CreateGoogleIcon()
        {
            var bmp = new Bitmap(24, 24);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Simplified Google "G" icon
                using (var pen = new Pen(Color.FromArgb(66, 133, 244), 3))
                    g.DrawArc(pen, 2, 2, 20, 20, 45, 270);
                using (var brush = new SolidBrush(Color.FromArgb(66, 133, 244)))
                    g.FillRectangle(brush, 12, 10, 10, 4);
            }
            return bmp;
        }

        // ============ WAITING PANEL ============
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
                Size = new Size(80, 80),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _logoBox.Image,
                Location = new Point((_waitingPanel.Width - 80) / 2, y)
            };
            _waitingPanel.Controls.Add(logoPic);
            y += 100;

            // Title
            var lblTitle = new Label
            {
                Text = "Complete Sign In",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblTitle.Location = new Point((_waitingPanel.Width - lblTitle.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblTitle);
            y += 35;

            // Message
            var lblMsg = new Label
            {
                Text = "A browser window has opened.\nPlease sign in with Google to continue.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblMsg.Location = new Point((_waitingPanel.Width - lblMsg.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblMsg);
            y += 60;

            // Animated dots (using a timer to animate)
            var dotsPanel = new Panel
            {
                Size = new Size(100, 20),
                Location = new Point((_waitingPanel.Width - 100) / 2, y),
                BackColor = Color.Transparent
            };
            
            var dot1 = CreateDot(20);
            var dot2 = CreateDot(45);
            var dot3 = CreateDot(70);
            dotsPanel.Controls.AddRange(new Control[] { dot1, dot2, dot3 });
            _waitingPanel.Controls.Add(dotsPanel);

            // Animate dots
            var animTimer = new System.Windows.Forms.Timer { Interval = 300 };
            var animState = 0;
            animTimer.Tick += (s, e) =>
            {
                dot1.BackColor = animState == 0 ? _colorRed : Color.FromArgb(100, _colorRed);
                dot2.BackColor = animState == 1 ? _colorRed : Color.FromArgb(100, _colorRed);
                dot3.BackColor = animState == 2 ? _colorRed : Color.FromArgb(100, _colorRed);
                animState = (animState + 1) % 3;
            };
            animTimer.Start();
            y += 50;

            // Status
            var lblWaiting = new Label
            {
                Text = "Waiting for authentication...",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true
            };
            lblWaiting.Location = new Point((_waitingPanel.Width - lblWaiting.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblWaiting);
            y += 50;

            // Reopen browser button
            var btnReopen = new Button
            {
                Text = "🔗  Reopen Browser",
                Size = new Size(360, 45),
                Location = new Point(30, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorInput,
                ForeColor = _colorText,
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            btnReopen.FlatAppearance.BorderColor = _colorBorder;
            btnReopen.Click += (s, e) => OpenBrowser();
            _waitingPanel.Controls.Add(btnReopen);
            y += 55;

            // Cancel button
            var btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(360, 35),
                Location = new Point(30, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = _colorTextDim,
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) =>
            {
                _cts?.Cancel();
                StopHttpListener();
                ShowLoginState();
            };
            _waitingPanel.Controls.Add(btnCancel);

            _contentPanel.Controls.Add(_waitingPanel);
        }

        private Panel CreateDot(int x)
        {
            return new Panel
            {
                Size = new Size(12, 12),
                Location = new Point(x, 4),
                BackColor = _colorRed
            };
        }

        // ============ SUCCESS PANEL ============
        private void CreateSuccessPanel()
        {
            _successPanel = new Panel
            {
                Size = new Size(420, 380),
                BackColor = _colorPanel,
                Visible = false
            };

            var y = 40;

            // Success checkmark
            var successIcon = new Panel
            {
                Size = new Size(80, 80),
                Location = new Point((_successPanel.Width - 80) / 2, y),
                BackColor = _colorGreen
            };
            var lblCheck = new Label
            {
                Text = "✓",
                Font = new Font("Segoe UI", 36F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = false,
                Size = new Size(80, 80),
                TextAlign = ContentAlignment.MiddleCenter
            };
            successIcon.Controls.Add(lblCheck);
            _successPanel.Controls.Add(successIcon);
            y += 100;

            // Welcome
            var lblWelcome = new Label
            {
                Text = "Welcome!",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblWelcome.Location = new Point((_successPanel.Width - lblWelcome.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblWelcome);
            y += 35;

            // Signed in as
            var lblSignedIn = new Label
            {
                Text = "Signed in as",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true
            };
            lblSignedIn.Location = new Point((_successPanel.Width - lblSignedIn.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblSignedIn);
            y += 25;

            // Email
            var lblEmail = new Label
            {
                Name = "lblSuccessEmail",
                Text = "user@example.com",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblEmail.Location = new Point((_successPanel.Width - lblEmail.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblEmail);
            y += 45;

            // Token box
            var tokenBox = new Panel
            {
                Size = new Size(360, 60),
                Location = new Point(30, y),
                BackColor = _colorInput
            };
            
            var lblTokenLabel = new Label
            {
                Text = "Available Tokens",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                Location = new Point(15, 12),
                AutoSize = true
            };
            tokenBox.Controls.Add(lblTokenLabel);

            var lblTokenCount = new Label
            {
                Name = "lblSuccessTokens",
                Text = "247",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = _colorGreen,
                AutoSize = true
            };
            lblTokenCount.Location = new Point(tokenBox.Width - lblTokenCount.PreferredWidth - 15, 15);
            tokenBox.Controls.Add(lblTokenCount);
            
            _successPanel.Controls.Add(tokenBox);
            y += 80;

            // Start button
            var btnStart = new Button
            {
                Text = "Start Programming",
                Size = new Size(360, 50),
                Location = new Point(30, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorRed,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
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

        // ============ ERROR PANEL ============
        private void CreateErrorPanel()
        {
            _errorPanel = new Panel
            {
                Size = new Size(420, 320),
                BackColor = _colorPanel,
                Visible = false
            };

            var y = 40;

            // Error icon
            var errorIcon = new Panel
            {
                Size = new Size(80, 80),
                Location = new Point((_errorPanel.Width - 80) / 2, y),
                BackColor = _colorRed
            };
            var lblX = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 36F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = false,
                Size = new Size(80, 80),
                TextAlign = ContentAlignment.MiddleCenter
            };
            errorIcon.Controls.Add(lblX);
            _errorPanel.Controls.Add(errorIcon);
            y += 100;

            // Title
            var lblTitle = new Label
            {
                Text = "Sign In Failed",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblTitle.Location = new Point((_errorPanel.Width - lblTitle.PreferredWidth) / 2, y);
            _errorPanel.Controls.Add(lblTitle);
            y += 35;

            // Message
            var lblMsg = new Label
            {
                Text = "Authentication was cancelled or timed out.\nPlease try again.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblMsg.Location = new Point((_errorPanel.Width - lblMsg.PreferredWidth) / 2, y);
            _errorPanel.Controls.Add(lblMsg);
            y += 60;

            // Try Again button
            var btnRetry = new Button
            {
                Text = "Try Again",
                Size = new Size(360, 50),
                Location = new Point(30, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorRed,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRetry.FlatAppearance.BorderSize = 0;
            btnRetry.Click += (s, e) => ShowLoginState();
            _errorPanel.Controls.Add(btnRetry);
            y += 60;

            // Back button
            var btnBack = new Button
            {
                Text = "Back to Login",
                Size = new Size(360, 35),
                Location = new Point(30, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = _colorTextDim,
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            btnBack.FlatAppearance.BorderSize = 0;
            btnBack.Click += (s, e) => ShowLoginState();
            _errorPanel.Controls.Add(btnBack);

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
            // Update labels
            foreach (Control c in _successPanel.Controls)
            {
                if (c.Name == "lblSuccessEmail")
                    c.Text = email;
                if (c is Panel p)
                {
                    foreach (Control pc in p.Controls)
                    {
                        if (pc.Name == "lblSuccessTokens")
                            pc.Text = tokens.ToString();
                    }
                }
            }

            _loginPanel.Visible = false;
            _waitingPanel.Visible = false;
            _successPanel.Visible = true;
            _errorPanel.Visible = false;
            CenterPanel(_successPanel);

            // Update header
            _lblTokens.Text = $"Tokens: {tokens}";
            _lblStatus.Text = email;
        }

        private void ShowErrorState()
        {
            _loginPanel.Visible = false;
            _waitingPanel.Visible = false;
            _successPanel.Visible = false;
            _errorPanel.Visible = true;
            CenterPanel(_errorPanel);
        }

        private void CenterPanel(Panel panel)
        {
            panel.Location = new Point(
                (_contentPanel.Width - panel.Width) / 2,
                (_contentPanel.Height - panel.Height) / 2
            );
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            
            // Re-center the visible panel
            if (_loginPanel?.Visible == true) CenterPanel(_loginPanel);
            else if (_waitingPanel?.Visible == true) CenterPanel(_waitingPanel);
            else if (_successPanel?.Visible == true) CenterPanel(_successPanel);
            else if (_errorPanel?.Visible == true) CenterPanel(_errorPanel);
        }

        // ============ OAUTH LOGIC ============
        private async void BtnGoogle_Click(object? sender, EventArgs e)
        {
            ShowWaitingState();
            _cts = new CancellationTokenSource();

            try
            {
                // Start HTTP listener for callback
                StartHttpListener();

                // Open browser
                OpenBrowser();

                // Wait for callback (with timeout)
                var result = await WaitForCallbackAsync(_cts.Token);

                if (result.success)
                {
                    AuthToken = result.token;
                    UserEmail = result.email;
                    Logger.Info($"Login successful for: {result.email}");
                    
                    // TODO: Get actual token count from API
                    ShowSuccessState(result.email ?? "User", 0);
                }
                else
                {
                    ShowErrorState();
                }
            }
            catch (OperationCanceledException)
            {
                ShowLoginState();
            }
            catch (Exception ex)
            {
                Logger.Error("OAuth error", ex);
                ShowErrorState();
            }
            finally
            {
                StopHttpListener();
            }
        }

        private void OpenBrowser()
        {
            var url = string.Format(LOGIN_URL, CALLBACK_PORT);
            Logger.Info($"Opening browser: {url}");
            
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private void StartHttpListener()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{CALLBACK_PORT}/");
                _httpListener.Start();
                Logger.Info($"HTTP listener started on port {CALLBACK_PORT}");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to start HTTP listener", ex);
                throw;
            }
        }

        private void StopHttpListener()
        {
            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
                _httpListener = null;
            }
            catch { }
        }

        private async Task<(bool success, string? token, string? email)> WaitForCallbackAsync(CancellationToken ct)
        {
            if (_httpListener == null)
                return (false, null, null);

            // Set timeout (60 seconds)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    var contextTask = _httpListener.GetContextAsync();
                    var completedTask = await Task.WhenAny(contextTask, Task.Delay(-1, linkedCts.Token));

                    if (completedTask == contextTask)
                    {
                        var context = await contextTask;
                        var request = context.Request;
                        var response = context.Response;

                        Logger.Info($"Received callback: {request.Url}");

                        // Parse query parameters
                        var query = request.QueryString;
                        var token = query["token"];
                        var email = query["email"];

                        // Send response HTML
                        var html = GetSuccessHtml();
                        var buffer = System.Text.Encoding.UTF8.GetBytes(html);
                        response.ContentType = "text/html";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, linkedCts.Token);
                        response.Close();

                        if (!string.IsNullOrEmpty(token))
                        {
                            return (true, token, email);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout or cancelled
            }
            catch (Exception ex)
            {
                Logger.Error("Error waiting for callback", ex);
            }

            return (false, null, null);
        }

        private string GetSuccessHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <title>PatsKiller Pro - Success</title>
    <style>
        body { 
            font-family: 'Segoe UI', sans-serif; 
            background: #1e1e1e; 
            color: white; 
            display: flex; 
            justify-content: center; 
            align-items: center; 
            height: 100vh; 
            margin: 0;
        }
        .container { 
            text-align: center; 
            padding: 40px;
            background: #2d2d30;
            border-radius: 16px;
            border: 1px solid #404040;
        }
        .check { 
            font-size: 64px; 
            color: #4CAF50; 
        }
        h1 { margin: 20px 0 10px; }
        p { color: #888; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='check'>✓</div>
        <h1>Success!</h1>
        <p>You can close this window and return to PatsKiller Pro.</p>
    </div>
</body>
</html>";
        }
    }
}
