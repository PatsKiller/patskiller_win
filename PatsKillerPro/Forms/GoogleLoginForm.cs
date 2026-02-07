using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PatsKillerPro
{
    /// <summary>
    /// Desktop sign-in dialog (Google OAuth).
    ///
    /// This dialog uses a session-code polling flow:
    /// 1) Desktop calls Supabase Edge Function `create-desktop-auth-session` -> gets a one-time session code.
    /// 2) Desktop opens the browser to PUBLIC_SITE_URL + `/api/desktop-auth?session=<code>`.
    /// 3) The web page completes OAuth and calls `complete-desktop-auth-session`.
    /// 4) Desktop polls `check-desktop-auth-session` until status=complete, then receives tokens.
    /// </summary>
    public sealed class GoogleLoginForm : Form
    {
        // ---- Public results (MainForm expects these) ----
        public string? AuthToken { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? UserEmail { get; private set; }

        // ---- Config (override via env vars for production) ----
        private static string SupabaseUrl =>
            Environment.GetEnvironmentVariable("PATSKILLER_SUPABASE_URL")
            ?? "https://kmpnplpijuzzbftsjacx.supabase.co";

        // Supabase anon key (public). Override via PATSKILLER_SUPABASE_ANON_KEY if you rotated keys.
        private static string SupabaseAnonKey =>
            Environment.GetEnvironmentVariable("PATSKILLER_SUPABASE_ANON_KEY")
            ?? "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0.iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";

        // Website base URL (Lovable custom domain). Override via PATSKILLER_PUBLIC_SITE_URL.
        private static string PublicSiteUrl =>
            (Environment.GetEnvironmentVariable("PATSKILLER_PUBLIC_SITE_URL") ?? "https://patskiller.com").TrimEnd('/');

        // v2.12 style route (must support ?session=...)
        private static string AuthUrlBase => $"{PublicSiteUrl}/api/desktop-auth?session=";

        // ---- Internal ----
        private readonly HttpClient _http = new();
        // Explicit WinForms timer to avoid ambiguity with System.Threading.Timer (implicit global usings)
        private readonly System.Windows.Forms.Timer _pollTimer = new();
        private string? _sessionCode;
        private bool _pollInProgress;

        // ---- UI ----
        private readonly Button _btnGoogle = new();
        private readonly Button _btnLicenseKey = new();
        private readonly Button _btnCancel = new();
        private readonly Label _lblTitle = new();
        private readonly Label _lblBody = new();
        private readonly Label _lblStatus = new();

        public GoogleLoginForm()
        {
            Text = "PatsKiller Pro — Sign in";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new System.Drawing.Size(460, 210);

            _lblTitle.Text = "Sign in to PatsKiller Pro";
            _lblTitle.AutoSize = true;
            _lblTitle.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            _lblTitle.Location = new System.Drawing.Point(18, 16);

            _lblBody.Text = "Use Google to unlock Pro features. If you already have a license key, you can activate it instead.";
            _lblBody.AutoSize = false;
            _lblBody.Width = ClientSize.Width - 36;
            _lblBody.Height = 40;
            _lblBody.Location = new System.Drawing.Point(18, 44);

            _btnGoogle.Text = "Continue with Google";
            _btnGoogle.Width = ClientSize.Width - 36;
            _btnGoogle.Height = 30;
            _btnGoogle.Location = new System.Drawing.Point(18, 90);
            _btnGoogle.Click += async (_, __) => await StartGoogleAuthAsync();

            _btnLicenseKey.Text = "I have a license key";
            _btnLicenseKey.Width = ClientSize.Width - 36;
            _btnLicenseKey.Height = 30;
            _btnLicenseKey.Location = new System.Drawing.Point(18, 126);
            _btnLicenseKey.Click += (_, __) =>
            {
                DialogResult = DialogResult.Retry;
                Close();
            };

            _btnCancel.Text = "Cancel";
            _btnCancel.Width = 90;
            _btnCancel.Height = 28;
            _btnCancel.Location = new System.Drawing.Point(ClientSize.Width - 18 - 90, 166);
            _btnCancel.Click += (_, __) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _lblStatus.Text = "";
            _lblStatus.AutoSize = false;
            _lblStatus.Width = ClientSize.Width - 18 - 18 - _btnCancel.Width - 8;
            _lblStatus.Height = 40;
            _lblStatus.Location = new System.Drawing.Point(18, 162);

            Controls.AddRange(new Control[] { _lblTitle, _lblBody, _btnGoogle, _btnLicenseKey, _btnCancel, _lblStatus });

            // Http defaults
            _http.Timeout = TimeSpan.FromSeconds(15);
            _http.DefaultRequestHeaders.Add("apikey", SupabaseAnonKey);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SupabaseAnonKey);

            // Poll timer
            _pollTimer.Interval = 2000;
            _pollTimer.Tick += async (_, __) => await PollOnceAsync();
        }

        private void SetBusy(bool busy)
        {
            _btnGoogle.Enabled = !busy;
            _btnLicenseKey.Enabled = !busy;
            _btnCancel.Enabled = true;
            UseWaitCursor = busy;
        }

        private async Task StartGoogleAuthAsync()
        {
            try
            {
                SetBusy(true);
                _lblStatus.Text = "Creating sign-in session…";

                _sessionCode = await CreateDesktopAuthSessionAsync();
                if (string.IsNullOrWhiteSpace(_sessionCode))
                    throw new Exception("create-desktop-auth-session returned an empty session code.");

                var authUrl = AuthUrlBase + Uri.EscapeDataString(_sessionCode);

                _lblStatus.Text = "Opening browser…";
                TryOpenBrowser(authUrl);

                _lblStatus.Text = "Waiting for sign-in in your browser…";
                _pollTimer.Start();
            }
            catch (Exception ex)
            {
                _pollTimer.Stop();
                _sessionCode = null;
                SetBusy(false);

                MessageBox.Show(
                    this,
                    "Could not start Google sign-in.\n\n" +
                    ex.Message +
                    "\n\n" +
                    "Check your Supabase settings:\n" +
                    $"SUPABASE_URL: {SupabaseUrl}\n" +
                    "(You can override via PATSKILLER_SUPABASE_URL / PATSKILLER_SUPABASE_ANON_KEY.)",
                    "Sign-in error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void TryOpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // As a fallback, try cmd /c start
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{url}\"",
                    CreateNoWindow = true
                });
            }
        }

        private async Task<string> CreateDesktopAuthSessionAsync()
        {
            var endpoint = $"{SupabaseUrl.TrimEnd('/')}/functions/v1/create-desktop-auth-session";

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"create-desktop-auth-session failed ({(int)resp.StatusCode}) {resp.ReasonPhrase}. Body: {json}");
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<CreateSessionResponse>(json, opts);
            return data?.SessionCode ?? data?.session_code ?? "";
        }

        private async Task PollOnceAsync()
        {
            if (_pollInProgress) return;
            if (string.IsNullOrWhiteSpace(_sessionCode)) return;

            _pollInProgress = true;
            try
            {
                var endpoint = $"{SupabaseUrl.TrimEnd('/')}/functions/v1/check-desktop-auth-session";
                var payload = JsonSerializer.Serialize(new { sessionCode = _sessionCode });

                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                using var resp = await _http.SendAsync(req);
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    // keep polling, but show last error unobtrusively
                    _lblStatus.Text = $"Waiting for sign-in… (server {(int)resp.StatusCode})";
                    return;
                }

                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<CheckSessionResponse>(json, opts);

                var status = (data?.status ?? "").Trim().ToLowerInvariant();
                if (status == "complete")
                {
                    AuthToken = data?.accessToken;
                    RefreshToken = data?.refreshToken;
                    UserEmail = data?.email;

                    _pollTimer.Stop();
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                if (status == "expired" || status == "failed")
                {
                    _pollTimer.Stop();
                    SetBusy(false);
                    _lblStatus.Text = "";
                    MessageBox.Show(this, data?.message ?? "Sign-in session expired. Please try again.", "Sign-in", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch
            {
                // transient error: don't crash the UI; keep polling
                _lblStatus.Text = "Waiting for sign-in… (network hiccup)";
            }
            finally
            {
                _pollInProgress = false;
            }
        }

        private sealed class CreateSessionResponse
        {
            public string? SessionCode { get; set; }
            public string? session_code { get; set; }
        }

        private sealed class CheckSessionResponse
        {
            public string? status { get; set; }
            public string? accessToken { get; set; }
            public string? refreshToken { get; set; }
            public string? email { get; set; }
            public string? message { get; set; }
        }
    }
}
