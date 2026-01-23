using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
    /// Professional OAuth login form using QR Code + Polling
    /// No localhost server, no Windows security blocks
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
        
        // QR Code display
        private PictureBox _qrCodeBox = null!;
        private Label _lblSessionCode = null!;

        // Dark theme colors (matching mockup)
        private readonly Color _colorBackground = Color.FromArgb(30, 30, 30);
        private readonly Color _colorHeader = Color.FromArgb(37, 37, 38);
        private readonly Color _colorPanel = Color.FromArgb(45, 45, 48);
        private readonly Color _colorInput = Color.FromArgb(60, 60, 60);
        private readonly Color _colorBorder = Color.FromArgb(80, 80, 80);
        private readonly Color _colorText = Color.FromArgb(255, 255, 255);
        private readonly Color _colorTextDim = Color.FromArgb(150, 150, 150);
        private readonly Color _colorRed = Color.FromArgb(233, 69, 96);
        private readonly Color _colorGreen = Color.FromArgb(76, 175, 80);

        // Results
        public string? AuthToken { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? UserEmail { get; private set; }

        // API Configuration - FIXED: Correct Supabase URL and endpoints
        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjacx.supabase.co";
        private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0.iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";
        
        // FIXED: Correct auth page URL (was /auth, now /desktop-auth)
        private const string AUTH_PAGE_URL = "https://patskiller.com/desktop-auth?session=";

        // Polling
        private CancellationTokenSource? _cts;
        private string? _currentSessionCode;
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _machineId;

        public GoogleLoginForm()
        {
            // Generate a machine ID for this device
            _machineId = GetMachineId();
            
            InitializeComponent();
            ShowLoginState();
        }

        /// <summary>
        /// Get unique machine identifier
        /// </summary>
        private static string GetMachineId()
        {
            try
            {
                // Use machine name + a hash for uniqueness
                var data = Environment.MachineName + Environment.UserName;
                using var sha = System.Security.Cryptography.SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
                return Convert.ToBase64String(hash)[..16]; // First 16 chars
            }
            catch
            {
                return Environment.MachineName;
            }
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
                using (var brush = new SolidBrush(Color.FromArgb(26, 26, 46)))
                    g.FillRectangle(brush, 0, 0, 50, 50);
                using (var pen = new Pen(_colorRed, 2))
                    g.DrawRectangle(pen, 1, 1, 47, 47);
                using (var brush = new SolidBrush(_colorRed))
                {
                    g.FillEllipse(brush, 8, 12, 18, 18);
                    g.FillRectangle(brush, 22, 18, 22, 6);
                    g.FillRectangle(brush, 36, 24, 3, 6);
                    g.FillRectangle(brush, 41, 24, 3, 8);
                }
                using (var brush = new SolidBrush(Color.FromArgb(26, 26, 46)))
                    g.FillEllipse(brush, 13, 17, 8, 8);
            }
            return bmp;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // ============ LOGIN PANEL (with QR Code) ============
        private void CreateLoginPanel()
        {
            _loginPanel = new Panel
            {
                Size = new Size(450, 580),
                BackColor = _colorPanel,
                Visible = false
            };

            var y = 25;

            // Logo at top
            var logoPic = new PictureBox
            {
                Size = new Size(70, 70),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _logoBox.Image,
                Location = new Point((_loginPanel.Width - 70) / 2, y)
            };
            _loginPanel.Controls.Add(logoPic);
            y += 85;

            // Title
            var lblWelcome = new Label
            {
                Text = "Sign In",
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblWelcome.Location = new Point((_loginPanel.Width - lblWelcome.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblWelcome);
            y += 45;

            // QR Code placeholder
            _qrCodeBox = new PictureBox
            {
                Size = new Size(180, 180),
                Location = new Point((_loginPanel.Width - 180) / 2, y),
                BackColor = Color.White,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            _loginPanel.Controls.Add(_qrCodeBox);
            y += 195;

            // Session code display
            _lblSessionCode = new Label
            {
                Text = "--------",
                Font = new Font("Consolas", 16F, FontStyle.Bold),
                ForeColor = _colorRed,
                AutoSize = true
            };
            _lblSessionCode.Location = new Point((_loginPanel.Width - _lblSessionCode.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(_lblSessionCode);
            y += 35;

            // Scan instruction
            var lblScan = new Label
            {
                Text = "Scan with your phone camera",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true
            };
            lblScan.Location = new Point((_loginPanel.Width - lblScan.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblScan);
            y += 35;

            // OR divider
            var lblOr = new Label
            {
                Text = "───────────  or  ───────────",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true
            };
            lblOr.Location = new Point((_loginPanel.Width - lblOr.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblOr);
            y += 35;

            // Open Browser Button
            var btnBrowser = new Button
            {
                Text = "🌐  Open Browser Instead",
                Size = new Size(360, 50),
                Location = new Point((_loginPanel.Width - 360) / 2, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorRed,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnBrowser.FlatAppearance.BorderSize = 0;
            btnBrowser.Click += BtnOpenBrowser_Click;
            _loginPanel.Controls.Add(btnBrowser);
            y += 65;

            // Loading indicator
            var lblLoading = new Label
            {
                Name = "lblLoginLoading",
                Text = "Generating QR code...",
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colorTextDim,
                AutoSize = true
            };
            lblLoading.Location = new Point((_loginPanel.Width - lblLoading.PreferredWidth) / 2, y);
            _loginPanel.Controls.Add(lblLoading);

            _contentPanel.Controls.Add(_loginPanel);
        }

        // ============ WAITING PANEL ============
        private void CreateWaitingPanel()
        {
            _waitingPanel = new Panel
            {
                Size = new Size(450, 420),
                BackColor = _colorPanel,
                Visible = false
            };

            var y = 40;

            var logoPic = new PictureBox
            {
                Size = new Size(80, 80),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _logoBox.Image,
                Location = new Point((_waitingPanel.Width - 80) / 2, y)
            };
            _waitingPanel.Controls.Add(logoPic);
            y += 100;

            var lblTitle = new Label
            {
                Text = "Waiting for Login",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblTitle.Location = new Point((_waitingPanel.Width - lblTitle.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblTitle);
            y += 40;

            var lblMsg = new Label
            {
                Text = "Complete sign-in on your phone or browser.\nThis window will update automatically.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblMsg.Location = new Point((_waitingPanel.Width - lblMsg.PreferredWidth) / 2, y);
            _waitingPanel.Controls.Add(lblMsg);
            y += 70;

            // Animated dots
            var dotsPanel = new Panel
            {
                Size = new Size(100, 20),
                Location = new Point((_waitingPanel.Width - 100) / 2, y),
                BackColor = Color.Transparent
            };
            var dot1 = new Panel { Size = new Size(12, 12), Location = new Point(20, 4), BackColor = _colorRed };
            var dot2 = new Panel { Size = new Size(12, 12), Location = new Point(45, 4), BackColor = _colorRed };
            var dot3 = new Panel { Size = new Size(12, 12), Location = new Point(70, 4), BackColor = _colorRed };
            dotsPanel.Controls.AddRange(new Control[] { dot1, dot2, dot3 });
            _waitingPanel.Controls.Add(dotsPanel);

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
            y += 60;

            // Cancel button
            var btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(200, 40),
                Location = new Point((_waitingPanel.Width - 200) / 2, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = _colorInput,
                ForeColor = _colorText,
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = _colorBorder;
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
                Size = new Size(450, 400),
                BackColor = _colorPanel,
                Visible = false
            };

            var y = 40;

            var successIcon = new Panel
            {
                Size = new Size(90, 90),
                Location = new Point((_successPanel.Width - 90) / 2, y),
                BackColor = _colorGreen
            };
            var lblCheck = new Label
            {
                Text = "✓",
                Font = new Font("Segoe UI", 40F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = false,
                Size = new Size(90, 90),
                TextAlign = ContentAlignment.MiddleCenter
            };
            successIcon.Controls.Add(lblCheck);
            _successPanel.Controls.Add(successIcon);
            y += 110;

            var lblWelcome = new Label
            {
                Text = "Welcome!",
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblWelcome.Location = new Point((_successPanel.Width - lblWelcome.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblWelcome);
            y += 40;

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

            var lblEmail = new Label
            {
                Name = "lblSuccessEmail",
                Text = "user@example.com",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblEmail.Location = new Point((_successPanel.Width - lblEmail.PreferredWidth) / 2, y);
            _successPanel.Controls.Add(lblEmail);
            y += 50;

            var btnStart = new Button
            {
                Text = "Start Programming",
                Size = new Size(300, 55),
                Location = new Point((_successPanel.Width - 300) / 2, y),
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

        // ============ ERROR PANEL ============
        private void CreateErrorPanel()
        {
            _errorPanel = new Panel
            {
                Size = new Size(450, 350),
                BackColor = _colorPanel,
                Visible = false
            };

            var y = 40;

            var errorIcon = new Panel
            {
                Size = new Size(90, 90),
                Location = new Point((_errorPanel.Width - 90) / 2, y),
                BackColor = _colorRed
            };
            var lblX = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 40F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = false,
                Size = new Size(90, 90),
                TextAlign = ContentAlignment.MiddleCenter
            };
            errorIcon.Controls.Add(lblX);
            _errorPanel.Controls.Add(errorIcon);
            y += 110;

            var lblTitle = new Label
            {
                Text = "Sign In Failed",
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = _colorText,
                AutoSize = true
            };
            lblTitle.Location = new Point((_errorPanel.Width - lblTitle.PreferredWidth) / 2, y);
            _errorPanel.Controls.Add(lblTitle);
            y += 40;

            var lblMsg = new Label
            {
                Name = "lblErrorMsg",
                Text = "Session expired or was cancelled.\nPlease try again.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colorTextDim,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            lblMsg.Location = new Point((_errorPanel.Width - lblMsg.PreferredWidth) / 2, y);
            _errorPanel.Controls.Add(lblMsg);
            y += 60;

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
            btnRetry.Click += async (s, e) => await StartAuthFlowAsync();
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
            CenterPanel(_loginPanel);
            
            // Start auth flow when showing login
            _ = StartAuthFlowAsync();
        }

        private void ShowWaitingState()
        {
            _loginPanel.Visible = false;
            _waitingPanel.Visible = true;
            _successPanel.Visible = false;
            _errorPanel.Visible = false;
            CenterPanel(_waitingPanel);
        }

        private void ShowSuccessState(string email)
        {
            foreach (Control c in _successPanel.Controls)
            {
                if (c.Name == "lblSuccessEmail")
                {
                    c.Text = email;
                    c.Location = new Point((_successPanel.Width - c.PreferredSize.Width) / 2, c.Location.Y);
                }
            }

            _loginPanel.Visible = false;
            _waitingPanel.Visible = false;
            _successPanel.Visible = true;
            _errorPanel.Visible = false;
            CenterPanel(_successPanel);

            _lblTokens.Text = "Tokens: --";
            _lblStatus.Text = email;
        }

        private void ShowErrorState(string message = "Session expired or was cancelled.\nPlease try again.")
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
            panel.Location = new Point(
                (_contentPanel.Width - panel.Width) / 2,
                (_contentPanel.Height - panel.Height) / 2
            );
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_loginPanel?.Visible == true) CenterPanel(_loginPanel);
            else if (_waitingPanel?.Visible == true) CenterPanel(_waitingPanel);
            else if (_successPanel?.Visible == true) CenterPanel(_successPanel);
            else if (_errorPanel?.Visible == true) CenterPanel(_errorPanel);
        }

        // ============ AUTH FLOW ============
        private async Task StartAuthFlowAsync()
        {
            try
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                // Update UI
                UpdateLoadingLabel("Creating session...");
                _qrCodeBox.Image = null;
                _lblSessionCode.Text = "--------";

                // Create session via API
                var session = await CreateSessionAsync();
                if (session == null)
                {
                    ShowErrorState("Could not connect to server.\nPlease check your internet connection.");
                    return;
                }

                _currentSessionCode = session.SessionCode;
                _lblSessionCode.Text = session.SessionCode;

                // Generate QR code
                UpdateLoadingLabel("Generating QR code...");
                var authUrl = AUTH_PAGE_URL + session.SessionCode;
                var qrImage = GenerateQRCode(authUrl);
                _qrCodeBox.Image = qrImage;

                UpdateLoadingLabel("Scan QR code or click button below");

                // Start polling
                _ = PollForCompletionAsync(session.SessionCode, _cts.Token);
            }
            catch (Exception ex)
            {
                Logger.Error("StartAuthFlowAsync error", ex);
                ShowErrorState("Failed to start authentication.\nPlease try again.");
            }
        }

        private void UpdateLoadingLabel(string text)
        {
            foreach (Control c in _loginPanel.Controls)
            {
                if (c.Name == "lblLoginLoading")
                {
                    c.Text = text;
                    c.Location = new Point((_loginPanel.Width - c.PreferredSize.Width) / 2, c.Location.Y);
                }
            }
        }

        private void BtnOpenBrowser_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentSessionCode))
            {
                MessageBox.Show("Please wait for session to be created.", "Please Wait", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var url = AUTH_PAGE_URL + _currentSessionCode;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            ShowWaitingState();
        }

        // ============ API CALLS ============
        
        /// <summary>
        /// FIXED: Creates a desktop auth session using the correct endpoint
        /// - Uses create-desktop-auth-session (not create-auth-session)
        /// - Uses apikey header (not Authorization: Bearer)
        /// - Sends machineId in request body
        /// </summary>
        private async Task<SessionInfo?> CreateSessionAsync()
        {
            try
            {
                // FIXED: Correct endpoint name
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/create-desktop-auth-session");
                
                // FIXED: Use apikey header instead of Authorization: Bearer
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                
                // FIXED: Send machineId in the request body
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
                return new SessionInfo
                {
                    SessionId = "", // Not returned by new API
                    SessionCode = doc.RootElement.GetProperty("sessionCode").GetString() ?? "",
                    ExpiresAt = doc.RootElement.GetProperty("expiresAt").GetString() ?? ""
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
                while (!ct.IsCancellationRequested)
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
                        Logger.Info($"Login successful: {result.Email}");

                        this.BeginInvoke(new Action(() => ShowSuccessState(result.Email ?? "User")));
                        return;
                    }
                    else if (result.Status == "expired" || result.Status == "invalid")
                    {
                        this.BeginInvoke(new Action(() => ShowErrorState("Session expired.\nPlease try again.")));
                        return;
                    }
                    // else: still pending, continue polling
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled, do nothing
            }
            catch (Exception ex)
            {
                Logger.Error("PollForCompletionAsync error", ex);
            }
        }

        /// <summary>
        /// FIXED: Checks the desktop auth session using the correct endpoint
        /// - Uses check-desktop-auth-session (not check-auth-session)
        /// - Uses POST method (not GET)
        /// - Uses apikey header (not Authorization: Bearer)
        /// - Sends sessionCode and machineId in request body
        /// - Reads accessToken (not token) from response
        /// </summary>
        private async Task<SessionResult?> CheckSessionAsync(string sessionCode)
        {
            try
            {
                // FIXED: Correct endpoint name and use POST method
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/check-desktop-auth-session");
                
                // FIXED: Use apikey header instead of Authorization: Bearer
                request.Headers.Add("apikey", SUPABASE_ANON_KEY);
                
                // FIXED: Send sessionCode and machineId in request body (POST, not GET query params)
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
                var status = doc.RootElement.GetProperty("status").GetString() ?? "";

                var result = new SessionResult { Status = status };

                if (status == "complete")
                {
                    // FIXED: The API returns "accessToken", not "token"
                    result.Token = doc.RootElement.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
                    result.RefreshToken = doc.RootElement.TryGetProperty("refreshToken", out var r) ? r.GetString() : null;
                    result.Email = doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error("CheckSessionAsync error", ex);
                return null;
            }
        }

        // ============ QR CODE GENERATION ============
        private Image GenerateQRCode(string content)
        {
            // Simple QR code generation using built-in .NET
            // For production, consider using QRCoder NuGet package
            return GenerateSimpleQRCode(content, 180);
        }

        private Image GenerateSimpleQRCode(string content, int size)
        {
            // This is a placeholder that creates a visual representation
            // In production, use QRCoder library for real QR codes
            // Install: dotnet add package QRCoder
            
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw a pattern that represents a QR code visually
                var random = new Random(content.GetHashCode());
                var cellSize = size / 25;
                
                // Draw position patterns (corners)
                DrawPositionPattern(g, 2, 2, cellSize);
                DrawPositionPattern(g, size - 9 * cellSize, 2, cellSize);
                DrawPositionPattern(g, 2, size - 9 * cellSize, cellSize);

                // Draw data modules (simplified)
                for (int x = 9; x < 16; x++)
                {
                    for (int y = 0; y < 25; y++)
                    {
                        if (random.Next(2) == 1)
                        {
                            g.FillRectangle(Brushes.Black, x * cellSize, y * cellSize, cellSize, cellSize);
                        }
                    }
                }

                for (int x = 0; x < 9; x++)
                {
                    for (int y = 9; y < 16; y++)
                    {
                        if (random.Next(2) == 1)
                        {
                            g.FillRectangle(Brushes.Black, x * cellSize, y * cellSize, cellSize, cellSize);
                        }
                    }
                }

                for (int x = 16; x < 25; x++)
                {
                    for (int y = 9; y < 25; y++)
                    {
                        if (random.Next(2) == 1)
                        {
                            g.FillRectangle(Brushes.Black, x * cellSize, y * cellSize, cellSize, cellSize);
                        }
                    }
                }

                // Add center logo placeholder
                var logoSize = size / 5;
                var logoRect = new Rectangle((size - logoSize) / 2, (size - logoSize) / 2, logoSize, logoSize);
                g.FillRectangle(Brushes.White, logoRect);
                g.FillRectangle(new SolidBrush(_colorRed), 
                    logoRect.X + 4, logoRect.Y + 4, logoRect.Width - 8, logoRect.Height - 8);
            }
            return bmp;
        }

        private void DrawPositionPattern(Graphics g, int x, int y, int cellSize)
        {
            // Outer black square
            g.FillRectangle(Brushes.Black, x, y, 7 * cellSize, 7 * cellSize);
            // Inner white square
            g.FillRectangle(Brushes.White, x + cellSize, y + cellSize, 5 * cellSize, 5 * cellSize);
            // Center black square
            g.FillRectangle(Brushes.Black, x + 2 * cellSize, y + 2 * cellSize, 3 * cellSize, 3 * cellSize);
        }

        // ============ HELPER CLASSES ============
        private class SessionInfo
        {
            public string SessionId { get; set; } = "";
            public string SessionCode { get; set; } = "";
            public string ExpiresAt { get; set; } = "";
        }

        private class SessionResult
        {
            public string Status { get; set; } = "";
            public string? Token { get; set; }
            public string? RefreshToken { get; set; }
            public string? Email { get; set; }
        }
    }
}