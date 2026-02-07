using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PatsKillerPro.Forms
{
    /// <summary>
    /// Desktop Google sign-in bootstrap.
    ///
    /// Expected behavior (used by MainForm):
    /// - DialogResult.OK    => AuthToken/RefreshToken/UserEmail are populated
    /// - DialogResult.Retry => user wants license key activation instead
    /// - DialogResult.Cancel=> user cancelled
    /// </summary>
    public partial class GoogleLoginForm : Form
    {
        // --- Results consumed by MainForm ---
        public string AuthToken { get; private set; } = string.Empty;
        public string RefreshToken { get; private set; } = string.Empty;
        public string UserEmail { get; private set; } = string.Empty;

        // --- Backend config (matches your current project values) ---
        private const string SUPABASE_URL = "https://kmpnplpijuzzgtforyls.supabase.co";
        private const string API_KEY_PART1 = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
        private const string API_KEY_PART2 = ".eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpand1enpndGZvcnlscyIsInJvbGUiOiJhbm9uIiwiaWF0IjoxNzU4NjM0NDQyLCJleHAiOjIwNzQyMTA0NDJ9.oqldgEpQ70a1Dd8EdPQOpQnOjvYzVvZt1xJWW5dT9pw";

        // Your production web route (v2.12) for desktop auth
        private const string AUTH_URL_BASE = "https://patskiller.com/api/desktop-auth?code=";

        // Edge functions
        private const string CREATE_SESSION_FN = "/functions/v1/create-desktop-auth-session";
        private const string CHECK_SESSION_FN = "/functions/v1/check-desktop-auth-session";

        // Polling
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(3);

        private readonly HttpClient _http = new HttpClient();
        private CancellationTokenSource? _cts;

        // UI
        private readonly Button _btnGoogle;
        private readonly Button _btnLicense;
        private readonly Button _btnCancel;
        private readonly Label _lblStatus;
        private readonly ProgressBar _progress;
        private readonly LinkLabel _lnkOpenAgain;

        private string _lastAuthUrl = string.Empty;
        private string _sessionCode = string.Empty;

        public GoogleLoginForm()
        {
            Text = "PatsKiller Pro — Sign in";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 320);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

            var title = new Label
            {
                Text = "Sign in to PatsKiller Pro",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Dock = DockStyle.Top,
                Padding = new Padding(0, 0, 0, 8)
            };

            var subtitle = new Label
            {
                Text = "Use Google to unlock Pro features. If you purchased a license key, you can activate it instead.",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 46
            };

            _btnGoogle = new Button
            {
                Text = "Continue with Google",
                Height = 44,
                Dock = DockStyle.Top
            };
            _btnGoogle.Click += async (_, __) => await StartGoogleAuthAsync();

            _btnLicense = new Button
            {
                Text = "I have a license key",
                Height = 40,
                Dock = DockStyle.Top
            };
            _btnLicense.Click += (_, __) =>
            {
                DialogResult = DialogResult.Retry;
                Close();
            };

            _lblStatus = new Label
            {
                Text = "",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 44
            };

            _progress = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Visible = false,
                Dock = DockStyle.Top,
                Height = 12
            };

            _lnkOpenAgain = new LinkLabel
            {
                Text = "Browser didn't open? Click here to open it again.",
                AutoSize = true,
                Dock = DockStyle.Top,
                Visible = false
            };
            _lnkOpenAgain.LinkClicked += (_, __) =>
            {
                if (!string.IsNullOrWhiteSpace(_lastAuthUrl))
                {
                    TryOpenBrowser(_lastAuthUrl);
                }
            };

            _btnCancel = new Button
            {
                Text = "Cancel",
                Height = 36,
                Dock = DockStyle.Right,
                Width = 110
            };
            _btnCancel.Click += (_, __) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            var buttonsRow = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 8, 0, 0) };
            buttonsRow.Controls.Add(_btnCancel);

            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
            content.Controls.Add(buttonsRow);

            // Stack in reverse order because DockStyle.Top
            content.Controls.Add(_lnkOpenAgain);
            content.Controls.Add(_progress);
            content.Controls.Add(_lblStatus);
            content.Controls.Add(Spacer(12));
            content.Controls.Add(_btnLicense);
            content.Controls.Add(Spacer(10));
            content.Controls.Add(_btnGoogle);
            content.Controls.Add(Spacer(10));
            content.Controls.Add(subtitle);
            content.Controls.Add(title);

            Controls.Add(content);

            // Ensure the button is truly clickable (WinForms edge cases with disabled parents, etc.)
            _btnGoogle.TabStop = true;
            _btnGoogle.Enabled = true;
            ActiveControl = _btnGoogle;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { /* ignore */ }

            _http.Dispose();
            base.OnFormClosed(e);
        }

        private static Control Spacer(int height) => new Panel { Dock = DockStyle.Top, Height = height };

        private static string GetAnonKey() => API_KEY_PART1 + API_KEY_PART2;

        private async Task StartGoogleAuthAsync()
        {
            if (_progress.Visible)
                return; // already running

            SetBusy(true, "Starting Google sign-in...");

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                // 1) Create session
                var machineId = Environment.MachineName;
                var sessionCode = await CreateDesktopAuthSessionAsync(machineId, _cts.Token);
                _sessionCode = sessionCode;

                // 2) Open browser
                _lastAuthUrl = AUTH_URL_BASE + Uri.EscapeDataString(sessionCode);
                if (!TryOpenBrowser(_lastAuthUrl))
                {
                    // Show link as fallback
                    _lnkOpenAgain.Visible = true;
                }

                // 3) Poll until complete
                SetBusy(true, "Browser opened. Complete Google sign-in, then return here...");
                var result = await PollForCompletionAsync(sessionCode, machineId, _cts.Token);

                if (!result.IsComplete)
                {
                    SetBusy(false, "");
                    MessageBox.Show(this,
                        result.Message,
                        "Sign-in not complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                // Success
                AuthToken = result.AccessToken;
                RefreshToken = result.RefreshToken;
                UserEmail = result.Email;

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                SetBusy(false, "");
            }
            catch (Exception ex)
            {
                SetBusy(false, "");
                MessageBox.Show(this,
                    "Could not start Google sign-in.\n\n" + ex.Message,
                    "Sign-in error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void SetBusy(bool busy, string status)
        {
            _btnGoogle.Enabled = !busy;
            _btnLicense.Enabled = !busy;
            _progress.Visible = busy;
            _lblStatus.Text = status;

            if (!busy)
            {
                _lnkOpenAgain.Visible = false;
                _lastAuthUrl = string.Empty;
            }
        }

        private static bool TryOpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> CreateDesktopAuthSessionAsync(string machineId, CancellationToken ct)
        {
            var endpoint = SUPABASE_URL.TrimEnd('/') + CREATE_SESSION_FN;

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("apikey", GetAnonKey());
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GetAnonKey());

            var payload = new
            {
                machineId = machineId,
                app = "PatsKillerPro"
            };

            var json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Create session failed ({(int)response.StatusCode}): {body}");
            }

            // Expected: {"sessionCode":"..."} (but tolerate different shapes)
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (TryGetString(root, "sessionCode", out var code))
                return code;
            if (TryGetString(root, "code", out var code2))
                return code2;
            if (TryGetString(root, "session_code", out var code3))
                return code3;

            throw new InvalidOperationException("Create session succeeded but did not return a session code.");
        }

        private async Task<AuthPollResult> PollForCompletionAsync(string sessionCode, string machineId, CancellationToken ct)
        {
            var endpoint = SUPABASE_URL.TrimEnd('/') + CHECK_SESSION_FN;

            var deadline = DateTime.UtcNow + PollTimeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("apikey", GetAnonKey());
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GetAnonKey());

                var payload = new
                {
                    sessionCode = sessionCode,
                    machineId = machineId
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    // Keep polling on transient failures, but surface last error if we time out.
                    await Task.Delay(PollInterval, ct);
                    continue;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var status = "";
                if (TryGetString(root, "status", out var s)) status = s;

                // Common statuses: pending / complete / expired / error
                if (string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "authenticated", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    // Tokens might be top-level or nested under "data"
                    if (TryGetTokens(root, out var email, out var at, out var rt))
                    {
                        return AuthPollResult.Complete(email, at, rt);
                    }

                    return AuthPollResult.NotComplete("Sign-in completed, but the server did not return tokens.");
                }

                if (string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase))
                    return AuthPollResult.NotComplete("This sign-in session expired. Please try again.");

                if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetString(root, "message", out var msg))
                        return AuthPollResult.NotComplete(msg);
                    return AuthPollResult.NotComplete("The server reported an error during sign-in.");
                }

                // Otherwise pending / unknown
                await Task.Delay(PollInterval, ct);
            }

            return AuthPollResult.NotComplete("Timed out waiting for sign-in. Please try again.");
        }

        private static bool TryGetString(JsonElement root, string prop, out string value)
        {
            value = string.Empty;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty(prop, out var el))
                return false;

            if (el.ValueKind == JsonValueKind.String)
            {
                value = el.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static bool TryGetTokens(JsonElement root, out string email, out string accessToken, out string refreshToken)
        {
            email = string.Empty;
            accessToken = string.Empty;
            refreshToken = string.Empty;

            // top-level
            if (TryGetString(root, "email", out var e)) email = e;
            if (TryGetString(root, "accessToken", out var at)) accessToken = at;
            if (TryGetString(root, "refreshToken", out var rt)) refreshToken = rt;
            if (!string.IsNullOrWhiteSpace(accessToken) && !string.IsNullOrWhiteSpace(refreshToken) && !string.IsNullOrWhiteSpace(email))
                return true;

            // snake_case
            if (string.IsNullOrWhiteSpace(email) && TryGetString(root, "user_email", out var e2)) email = e2;
            if (string.IsNullOrWhiteSpace(accessToken) && TryGetString(root, "access_token", out var at2)) accessToken = at2;
            if (string.IsNullOrWhiteSpace(refreshToken) && TryGetString(root, "refresh_token", out var rt2)) refreshToken = rt2;
            if (!string.IsNullOrWhiteSpace(accessToken) && !string.IsNullOrWhiteSpace(refreshToken) && !string.IsNullOrWhiteSpace(email))
                return true;

            // nested "data"
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (TryGetString(data, "email", out var de)) email = de;
                if (TryGetString(data, "accessToken", out var dat)) accessToken = dat;
                if (TryGetString(data, "refreshToken", out var drt)) refreshToken = drt;

                if (string.IsNullOrWhiteSpace(accessToken) && TryGetString(data, "access_token", out var dat2)) accessToken = dat2;
                if (string.IsNullOrWhiteSpace(refreshToken) && TryGetString(data, "refresh_token", out var drt2)) refreshToken = drt2;

                return !string.IsNullOrWhiteSpace(accessToken) && !string.IsNullOrWhiteSpace(refreshToken) && !string.IsNullOrWhiteSpace(email);
            }

            return false;
        }

        private readonly struct AuthPollResult
        {
            public bool IsComplete { get; }
            public string Message { get; }
            public string Email { get; }
            public string AccessToken { get; }
            public string RefreshToken { get; }

            private AuthPollResult(bool isComplete, string message, string email, string accessToken, string refreshToken)
            {
                IsComplete = isComplete;
                Message = message;
                Email = email;
                AccessToken = accessToken;
                RefreshToken = refreshToken;
            }

            public static AuthPollResult Complete(string email, string accessToken, string refreshToken) =>
                new AuthPollResult(true, string.Empty, email, accessToken, refreshToken);

            public static AuthPollResult NotComplete(string message) =>
                new AuthPollResult(false, message, string.Empty, string.Empty, string.Empty);
        }
    }
}
