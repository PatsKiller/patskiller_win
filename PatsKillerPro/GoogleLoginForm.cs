using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PatsKillerPro.Utils;

namespace PatsKillerPro
{
    /// <summary>
    /// Embedded Google OAuth login form using WebView2
    /// </summary>
    public class GoogleLoginForm : Form
    {
        private WebView2? _webView;
        private Label _lblStatus = null!;
        private Button _btnCancel = null!;
        private Panel _contentPanel = null!;
        private Label _lblLoading = null!;

        // Dark theme colors
        private readonly Color _colorBackground = Color.FromArgb(30, 30, 30);
        private readonly Color _colorPanel = Color.FromArgb(45, 45, 48);
        private readonly Color _colorText = Color.FromArgb(220, 220, 220);
        private readonly Color _colorTextDim = Color.FromArgb(150, 150, 150);

        // Results
        public string? AuthToken { get; private set; }
        public string? UserEmail { get; private set; }

        // Login URL - using the discrete API endpoint
        private const string LOGIN_URL = "https://patskiller.com/api/desktop-auth?mode=embedded";

        public GoogleLoginForm()
        {
            InitializeComponent();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Initialize WebView2 after form is shown
            _ = InitializeWebViewAsync();
        }

        private void InitializeComponent()
        {
            this.Text = "Sign in with Google - PatsKiller Pro";
            this.Size = new Size(950, 850);
            this.MinimumSize = new Size(800, 700);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = false;
            this.BackColor = _colorBackground;
            this.ShowInTaskbar = false;

            // Dark title bar
            try
            {
                var attribute = 20; // DWMWA_USE_IMMERSIVE_DARK_MODE
                var value = 1;
                DwmSetWindowAttribute(this.Handle, attribute, ref value, sizeof(int));
            }
            catch { }

            // Bottom panel FIRST (so it docks properly)
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = _colorPanel,
                Padding = new Padding(10)
            };

            _lblStatus = new Label
            {
                Text = "Initializing...",
                ForeColor = _colorTextDim,
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Location = new Point(10, 15)
            };
            bottomPanel.Controls.Add(_lblStatus);

            _btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(80, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 65),
                ForeColor = _colorText,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Right
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 85);
            _btnCancel.Location = new Point(bottomPanel.Width - _btnCancel.Width - 10, 10);
            _btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            bottomPanel.Controls.Add(_btnCancel);
            bottomPanel.Resize += (s, e) =>
            {
                _btnCancel.Location = new Point(bottomPanel.Width - _btnCancel.Width - 10, 10);
            };

            this.Controls.Add(bottomPanel);

            // Content panel that fills remaining space
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _colorBackground
            };
            this.Controls.Add(_contentPanel);

            // Loading label (centered in content panel)
            _lblLoading = new Label
            {
                Text = "Loading...",
                ForeColor = _colorText,
                Font = new Font("Segoe UI", 14F),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            _contentPanel.Controls.Add(_lblLoading);

            // Handle form closing
            this.FormClosing += (s, e) =>
            {
                try
                {
                    _webView?.Dispose();
                }
                catch { }
            };
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private async Task InitializeWebViewAsync()
        {
            try
            {
                _lblLoading.Text = "Initializing browser...";
                _lblStatus.Text = "Please wait...";

                // Create WebView2 user data folder
                var userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PatsKiller Pro", "WebView2");
                
                System.IO.Directory.CreateDirectory(userDataFolder);

                // Create environment
                _lblLoading.Text = "Starting browser engine...";
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                // Create WebView2 control
                _webView = new WebView2
                {
                    Dock = DockStyle.Fill
                };

                // Initialize WebView2
                _lblLoading.Text = "Connecting...";
                await _webView.EnsureCoreWebView2Async(env);

                // Configure settings
                var settings = _webView.CoreWebView2.Settings;
                settings.IsStatusBarEnabled = false;
                settings.AreDefaultContextMenusEnabled = true;
                settings.IsZoomControlEnabled = false;
                settings.AreDevToolsEnabled = true;
                settings.IsWebMessageEnabled = true;
                settings.AreDefaultScriptDialogsEnabled = true;
                settings.AreHostObjectsAllowed = true;
                
                // Important: Set User-Agent to look like regular browser
                settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edg/122.0.0.0";

                // Handle navigation starting
                _webView.CoreWebView2.NavigationStarting += (s, e) =>
                {
                    Logger.Info($"Navigating to: {e.Uri}");
                    
                    // Check for callback URL
                    if (e.Uri.StartsWith("patskiller://", StringComparison.OrdinalIgnoreCase))
                    {
                        e.Cancel = true;
                        HandleCallback(e.Uri);
                    }
                };

                // Handle navigation completed
                _webView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    var url = _webView.CoreWebView2.Source;
                    Logger.Info($"Navigation completed: {url}, Success: {e.IsSuccess}");
                    
                    if (e.IsSuccess)
                    {
                        if (url.Contains("accounts.google.com"))
                        {
                            _lblStatus.Text = "Sign in with your Google account";
                        }
                        else if (url.Contains("patskiller.com"))
                        {
                            _lblStatus.Text = "Processing...";
                        }
                        else
                        {
                            _lblStatus.Text = "Loading...";
                        }
                    }
                    else
                    {
                        _lblStatus.Text = $"Error: {e.WebErrorStatus}";
                        Logger.Warning($"Navigation failed: {e.WebErrorStatus}");
                    }
                };

                // Handle new window requests (OAuth popups)
                _webView.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    Logger.Info($"Popup requested: {e.Uri}");
                    e.Handled = true;
                    _webView.CoreWebView2.Navigate(e.Uri);
                };

                // NOW add WebView to content panel (remove loading label first)
                _contentPanel.Controls.Clear();
                _contentPanel.Controls.Add(_webView);

                // Force layout update
                _contentPanel.PerformLayout();
                _webView.Refresh();

                // Navigate to login page
                _lblStatus.Text = "Loading login page...";
                Logger.Info($"Navigating to: {LOGIN_URL}");
                _webView.CoreWebView2.Navigate(LOGIN_URL);

            }
            catch (WebView2RuntimeNotFoundException)
            {
                Logger.Error("WebView2 runtime not found");
                _lblLoading.Text = "WebView2 Runtime not installed";
                _lblStatus.Text = "Please install Microsoft Edge WebView2 Runtime";
                
                var result = MessageBox.Show(
                    "Microsoft Edge WebView2 Runtime is required.\n\n" +
                    "Would you like to download it now?",
                    "WebView2 Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://developer.microsoft.com/microsoft-edge/webview2/",
                        UseShellExecute = true
                    });
                }

                this.DialogResult = DialogResult.Abort;
                this.Close();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize WebView2", ex);
                _lblLoading.Text = $"Error: {ex.Message}";
                _lblStatus.Text = "Browser initialization failed";
                
                MessageBox.Show(
                    $"Could not initialize the browser:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                this.DialogResult = DialogResult.Abort;
                this.Close();
            }
        }

        private void HandleCallback(string url)
        {
            try
            {
                Logger.Info($"Processing callback: {url}");
                
                // Parse: patskiller://callback?token=XXX&email=YYY
                var uri = new Uri(url);
                var query = uri.Query.TrimStart('?');
                var queryParams = new System.Collections.Generic.Dictionary<string, string>();
                
                foreach (var param in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = param.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        queryParams[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
                    }
                }

                queryParams.TryGetValue("token", out var token);
                queryParams.TryGetValue("email", out var email);

                if (!string.IsNullOrEmpty(token))
                {
                    AuthToken = token;
                    UserEmail = email;
                    Logger.Info($"Login successful for: {email}");
                    _lblStatus.Text = "Login successful!";

                    // Close dialog with success
                    this.BeginInvoke(new Action(() =>
                    {
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }));
                }
                else
                {
                    Logger.Warning("Callback received but no token");
                    _lblStatus.Text = "Login failed - no token received";
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to parse callback", ex);
                _lblStatus.Text = "Login failed - invalid response";
            }
        }
    }
}
