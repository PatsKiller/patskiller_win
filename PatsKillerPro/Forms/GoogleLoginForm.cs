using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PatsKillerPro.Forms
{
    /// <summary>
    /// Desktop Google sign-in launcher.
    /// Uses Lovable route /api/desktop-auth (direct callback flow):
    ///   https://patskiller.com/api/desktop-auth?callback=http://127.0.0.1:PORT
    /// The webpage redirects back to http://127.0.0.1:PORT/callback?token=...&email=...
    /// </summary>
    public sealed class GoogleLoginForm : Form
    {
        public string AuthToken { get; private set; } = string.Empty;
        public string RefreshToken { get; private set; } = string.Empty; // Not provided by the web flow today
        public string UserEmail { get; private set; } = string.Empty;

        private readonly Label _lblTitle;
        private readonly Label _lblBody;
        private readonly Label _lblStatus;
        private readonly Button _btnGoogle;
        private readonly Button _btnLicense;
        private readonly Button _btnCancel;

        private CancellationTokenSource? _cts;
        private HttpListener? _listener;

        private static string PublicSiteUrl
        {
            get
            {
                // Defaults to production domain
                var env = Environment.GetEnvironmentVariable("PATSKILLER_PUBLIC_SITE_URL");
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim().TrimEnd('/');
                return "https://patskiller.com";
            }
        }

        public GoogleLoginForm()
        {
            Text = "PatsKiller Pro — Sign in";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(420, 210);

            _lblTitle = new Label
            {
                Text = "Sign in to PatsKiller Pro",
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(16, 16)
            };

            _lblBody = new Label
            {
                Text = "Use Google to unlock Pro features.\nIf you already have a license key, use the license option.",
                AutoSize = true,
                Location = new System.Drawing.Point(16, 48)
            };

            _btnGoogle = new Button
            {
                Text = "Continue with Google",
                Width = 380,
                Height = 32,
                Location = new System.Drawing.Point(16, 92)
            };
            _btnGoogle.Click += async (_, __) => await StartGoogleSignInAsync();

            _btnLicense = new Button
            {
                Text = "I have a license key",
                Width = 380,
                Height = 32,
                Location = new System.Drawing.Point(16, 128)
            };
            _btnLicense.Click += (_, __) => { DialogResult = DialogResult.Retry; Close(); };

            _btnCancel = new Button
            {
                Text = "Cancel",
                Width = 90,
                Height = 28,
                Location = new System.Drawing.Point(ClientSize.Width - 16 - 90, 168),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };
            _btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

            _lblStatus = new Label
            {
                Text = "",
                AutoSize = true,
                ForeColor = System.Drawing.Color.DimGray,
                Location = new System.Drawing.Point(16, 172)
            };

            Controls.AddRange(new Control[] { _lblTitle, _lblBody, _btnGoogle, _btnLicense, _btnCancel, _lblStatus });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
            }
            catch { /* no-op */ }
        }

        private async Task StartGoogleSignInAsync()
        {
            // Defensive: prevent double-click chaos
            _btnGoogle.Enabled = false;
            _btnLicense.Enabled = false;
            _btnCancel.Enabled = false;

            _cts = new CancellationTokenSource();

            try
            {
                _lblStatus.Text = "Opening browser for Google sign-in...";

                var callbackBase = StartLoopbackListener();
                var authUrl = $"{PublicSiteUrl}/api/desktop-auth?callback={Uri.EscapeDataString(callbackBase)}";

                // Launch system browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });

                _lblStatus.Text = "Waiting for sign-in to complete...";

                // Wait up to 2 minutes for callback
                var (token, email) = await WaitForCallbackAsync(TimeSpan.FromMinutes(2), _cts.Token);

                if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
                    throw new InvalidOperationException("Callback received, but token/email were missing.");

                AuthToken = token;
                UserEmail = email;

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                // user closed the form
                DialogResult = DialogResult.Cancel;
                Close();
            }
            catch (TimeoutException)
            {
                MessageBox.Show(
                    "Timed out waiting for Google sign-in.\n\n" +
                    "Make sure the browser redirect completes and that your firewall isn’t blocking localhost callbacks.",
                    "Sign-in error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                ResetUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not start Google sign-in.\n\n" + ex.Message +
                    $"\n\nSite: {PublicSiteUrl}",
                    "Sign-in error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                ResetUi();
            }
        }

        private void ResetUi()
        {
            _lblStatus.Text = "";
            _btnGoogle.Enabled = true;
            _btnLicense.Enabled = true;
            _btnCancel.Enabled = true;
        }

        private string StartLoopbackListener()
        {
            var port = GetFreeTcpPort();
            var prefix = $"http://127.0.0.1:{port}/";

            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();

            // We return the base URL (without /callback). The web page appends /callback.
            return prefix.TrimEnd('/');
        }

        private async Task<(string token, string email)> WaitForCallbackAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (_listener == null) throw new InvalidOperationException("Listener not started.");

            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                var contextTask = _listener.GetContextAsync();
                var remaining = deadline - DateTime.UtcNow;
                var delayTask = Task.Delay(remaining, ct);

                var completed = await Task.WhenAny(contextTask, delayTask);
                if (completed != contextTask)
                    throw new TimeoutException();

                var ctx = await contextTask;

                // Parse and respond quickly so the browser can close
                var url = ctx.Request.Url;
                var path = url?.AbsolutePath ?? string.Empty;

                // Handle any preflight (favicon, etc.)
                var q = url?.Query ?? string.Empty;
                var query = ParseQuery(q);

                var token = query.TryGetValue("token", out var t) ? t : string.Empty;
                var email = query.TryGetValue("email", out var e) ? e : string.Empty;

                // Always respond with a friendly page
                var html = "<html><head><title>PatsKiller Pro</title></head><body style='font-family:Segoe UI'>" +
                           "<h3>Sign-in complete.</h3><p>You can close this window and return to the app.</p>" +
                           "</body></html>";
                var buf = Encoding.UTF8.GetBytes(html);

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = buf.Length;
                await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length, ct);
                ctx.Response.OutputStream.Close();

                // If this wasn't the callback hit, keep waiting
                if (!path.StartsWith("/callback", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(email))
                    return (token, email);

                // If callback was hit but missing params, keep waiting (sometimes first hit is empty)
            }

            throw new TimeoutException();
        }

        private static int GetFreeTcpPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query)) return dict;

            var q = query.StartsWith("?") ? query.Substring(1) : query;
            var pairs = q.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in pairs)
            {
                var kv = p.Split('=', 2);
                var key = WebUtility.UrlDecode(kv[0] ?? string.Empty) ?? string.Empty;
                var val = kv.Length > 1 ? (WebUtility.UrlDecode(kv[1]) ?? string.Empty) : string.Empty;
                if (key.Length == 0) continue;
                dict[key] = val;
            }

            return dict;
        }
    }
}
