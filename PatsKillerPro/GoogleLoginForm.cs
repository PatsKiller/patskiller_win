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
    /// Shows patskiller.com/desktop-login inside the app
    /// </summary>
    public class GoogleLoginForm : Form
    {
        private WebView2 _webView = null!;
        private Label _lblStatus = null!;
        private Button _btnCancel = null!;
        private Panel _loadingPanel = null!;

        // Dark theme colors (matching MainForm)
        private readonly Color _colorBackground = Color.FromArgb(30, 30, 30);
        private readonly Color _colorPanel = Color.FromArgb(45, 45, 48);
        private readonly Color _colorText = Color.FromArgb(220, 220, 220);
        private readonly Color _colorTextDim = Color.FromArgb(150, 150, 150);

        // Results
        public string? AuthToken { get; private set; }
        public string? UserEmail { get; private set; }

        // Callback URL scheme
        private const string CALLBACK_SCHEME = "patskiller://";
        // Discrete desktop login page (no header/footer, not crawlable)
        private const string LOGIN_URL = "https://patskiller.com/api/desktop-auth?mode=embedded";

        public GoogleLoginForm()
        {
            InitializeComponent();
            _ = InitializeWebViewAsync();
        }

        private void InitializeComponent()
        {
            this.Text = "Sign in with Google - PatsKiller Pro";
            this.Size = new Size(700, 750);
            this.MinimumSize = new Size(600, 650);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = _colorBackground;
            this.ShowInTaskbar = false;

            // Dark title bar
            try
            {
                var attribute = 20;
                var value = 1;
                DwmSetWindowAttribute(this.Handle, attribute, ref value, sizeof(int));
            }
            catch { }

            // Loading panel (shown while WebView2 initializes)
            _loadingPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _colorBackground
            };

            var lblLoading = new Label
            {
                Text = "Loading...",
                ForeColor = _colorText,
                Font = new Font("Segoe UI", 12F),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            _loadingPanel.Controls.Add(lblLoading);
            this.Controls.Add(_loadingPanel);

            // WebView2 control
            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                Visible = false
            };
            this.Controls.Add(_webView);

            // Bottom panel with status and cancel button
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = _colorPanel,
                Padding = new Padding(10, 10, 10, 10)
            };

            _lblStatus = new Label
            {
                Text = "Connecting to PatsKiller.com...",
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
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 85);
            _btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            bottomPanel.Controls.Add(_btnCancel);

            // Position cancel button on right
            bottomPanel.Resize += (s, e) =>
            {
                _btnCancel.Location = new Point(bottomPanel.Width - _btnCancel.Width - 10, 10);
            };

            this.Controls.Add(bottomPanel);

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
                // Initialize WebView2 with user data folder in AppData
                var userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PatsKiller Pro", "WebView2");

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webView.EnsureCoreWebView2Async(env);

                // Configure WebView2
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

                // Handle navigation - intercept callback URL
                _webView.CoreWebView2.NavigationStarting += WebView_NavigationStarting;

                // Handle navigation completed
                _webView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        _lblStatus.Text = "Sign in with your Google account";
                    }
                    else
                    {
                        _lblStatus.Text = "Failed to load. Check your connection.";
                    }
                };

                // Show WebView, hide loading
                _loadingPanel.Visible = false;
                _webView.Visible = true;
                _webView.BringToFront();

                // Navigate to login page
                _lblStatus.Text = "Loading login page...";
                _webView.CoreWebView2.Navigate(LOGIN_URL);

                Logger.Info("WebView2 initialized, navigating to login page");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize WebView2", ex);
                _lblStatus.Text = "Browser component failed to load";
                
                MessageBox.Show(
                    "Could not initialize the login browser.\n\n" +
                    "Please ensure Microsoft Edge WebView2 Runtime is installed.\n\n" +
                    "You can download it from:\n" +
                    "https://developer.microsoft.com/microsoft-edge/webview2/",
                    "WebView2 Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                this.DialogResult = DialogResult.Abort;
                this.Close();
            }
        }

        private void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var url = e.Uri;
            Logger.Info($"WebView navigating to: {url}");

            // Check if this is our callback URL
            if (url.StartsWith(CALLBACK_SCHEME, StringComparison.OrdinalIgnoreCase))
            {
                // Cancel navigation - we'll handle this ourselves
                e.Cancel = true;

                // Parse the callback URL: patskiller://callback?token=XXX&email=YYY
                try
                {
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

                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                    else
                    {
                        Logger.Warning("Callback received but no token found");
                        _lblStatus.Text = "Login failed - no token received";
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to parse callback URL", ex);
                    _lblStatus.Text = "Login failed - invalid response";
                }
            }
            // Also check for localhost callback (fallback)
            else if (url.Contains("localhost") && url.Contains("callback"))
            {
                e.Cancel = true;
                
                try
                {
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
                        Logger.Info($"Login successful (localhost callback) for: {email}");

                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to parse localhost callback", ex);
                }
            }
        }
    }
}
