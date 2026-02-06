using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PatsKillerPro
{
    public class GoogleLoginForm : Form
    {
        // ============ PUBLIC PROPERTIES ============
        public string? AuthToken { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? UserEmail { get; private set; }
        public int TokenCount { get; private set; } = 0;

        // ============ DESIGN CONSTANTS ============
        private readonly Color _colBackground = ColorTranslator.FromHtml("#0F172A"); // Dark Navy
        private readonly Color _colSurface = ColorTranslator.FromHtml("#1E293B");    // Input Fields
        private readonly Color _colBorder = ColorTranslator.FromHtml("#334155");     // Borders
        private readonly Color _colAccent = ColorTranslator.FromHtml("#EC4899");     // Brand Pink
        private readonly Color _colTextMain = ColorTranslator.FromHtml("#F8FAFC");   // White text
        private readonly Color _colTextMuted = ColorTranslator.FromHtml("#94A3B8");  // Gray text
        
        // SCALED UP FONTS
        private readonly Font _fontHeader = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font _fontTitle = new Font("Segoe UI", 42F, FontStyle.Italic | FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontSub = new Font("Segoe UI", 16F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontInput = new Font("Segoe UI", 18F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontLabel = new Font("Segoe UI", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontBtnGoogle = new Font("Segoe UI Semibold", 18F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontBtnSign = new Font("Segoe UI Semibold", 20F, FontStyle.Regular, GraphicsUnit.Pixel);

        // ============ API CONSTANTS ============
        private CancellationTokenSource? _cts;
        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjacx.supabase.co";
        private const string API_KEY_PART1 = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0";
        private const string API_KEY_PART2 = ".iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";
        private static string GetPublicSiteUrl()
        {
            // Override via env var for staging/dev (e.g., https://patskiller-revival.lovable.app)
            var v = Environment.GetEnvironmentVariable("PATS_PUBLIC_SITE_URL");
            return (string.IsNullOrWhiteSpace(v) ? "https://patskiller.com" : v).Trim().TrimEnd('/');
        }

        private static string BuildAuthUrl(string sessionCode)
        {
            return $"{GetPublicSiteUrl()}/api/desktop-auth?session={Uri.EscapeDataString(sessionCode)}";
        }
        private readonly HttpClient _http = new HttpClient();

        // ============ UI STATE ============
        private Panel _mainContainer = null!;

        public GoogleLoginForm()
        {
            // 1. Form Configuration (Larger Size)
            this.AutoScaleMode = AutoScaleMode.None; 
            this.DoubleBuffered = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(540, 780); // SCALED UP from 400x600
            this.BackColor = _colBackground;
            this.Text = "PatsKiller Pro";
            
            // Allow dragging
            this.MouseDown += (s, e) => { 
                if (e.Button == MouseButtons.Left) { 
                    ReleaseCapture(); 
                    SendMessage(Handle, 0xA1, 0x2, 0); 
                } 
            };

            InitializeUI();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private void InitializeUI()
        {
            // -- Header (Logo + Window Controls) --
            var header = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.Transparent };
            
            // Logo (Scaled)
            var logo = new PictureBox { Size = new Size(40, 40), Location = new Point(30, 20), Image = DrawLogo(40) };
            var title = new Label { Text = "PatsKiller Pro", Font = _fontHeader, ForeColor = _colAccent, AutoSize = true, Location = new Point(80, 28) };
            
            // Close Button
            var btnClose = new Label { Text = "●", Font = new Font("Segoe UI", 16F), ForeColor = _colBorder, AutoSize = true, Location = new Point(490, 20), Cursor = Cursors.Hand };
            btnClose.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = _colAccent;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = _colBorder;

            header.Controls.Add(logo);
            header.Controls.Add(title);
            header.Controls.Add(btnClose);
            this.Controls.Add(header);

            // -- Main Content Container --
            int contentWidth = 440; // SCALED UP from 320
            _mainContainer = new Panel 
            { 
                Size = new Size(contentWidth, 600), 
                Location = new Point((this.Width - contentWidth) / 2, 100),
                BackColor = Color.Transparent 
            };
            this.Controls.Add(_mainContainer);

            RenderFormContent();
        }

        private void RenderFormContent()
        {
            int y = 0;
            int width = _mainContainer.Width;

            // 1. Welcome Text
            var lblWelcome = new Label
            {
                Text = "Welcome",
                Font = _fontTitle,
                ForeColor = _colTextMain,
                AutoSize = false,
                Size = new Size(width, 70), // Taller
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y)
            };
            _mainContainer.Controls.Add(lblWelcome);
            y += 70;

            // 2. Subtitle
            var lblSub = new Label
            {
                Text = "Sign in to access your account",
                Font = _fontSub,
                ForeColor = _colTextMuted,
                AutoSize = false,
                Size = new Size(width, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y)
            };
            _mainContainer.Controls.Add(lblSub);
            y += 50; // More Spacing

            // 3. Google Button (Scaled Height)
            var btnGoogle = new Button
            {
                Text = "", 
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(width, 55), // SCALED UP from 45
                Location = new Point(0, y),
                Cursor = Cursors.Hand
            };
            btnGoogle.FlatAppearance.BorderSize = 0;
            
            var googleIcon = DrawGoogleG(30); // Larger Icon

            btnGoogle.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using var path = GetRoundedPath(new Rectangle(0, 0, btnGoogle.Width, btnGoogle.Height), 8);
                btnGoogle.Region = new Region(path);
                
                using var bgBrush = new SolidBrush(Color.White);
                g.FillPath(bgBrush, path);

                // Draw Icon
                g.DrawImage(googleIcon, 20, (btnGoogle.Height - 30) / 2);

                // Draw Text
                string text = "Continue with Google";
                SizeF textSize = g.MeasureString(text, _fontBtnGoogle);
                
                float textX = (btnGoogle.Width - textSize.Width) / 2;
                float textY = (btnGoogle.Height - textSize.Height) / 2;
                
                if (textX < 60) textX = 60; 

                using var textBrush = new SolidBrush(Color.FromArgb(55, 65, 81));
                g.DrawString(text, _fontBtnGoogle, textBrush, textX, textY);
            };
            
            btnGoogle.Click += (s, e) => StartGoogleAuth();
            _mainContainer.Controls.Add(btnGoogle);
            y += 80; // More spacing below button

            // 4. Divider
            var divPanel = new Panel { Size = new Size(width, 24), Location = new Point(0, y) };
            divPanel.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                string txt = "or sign in with email";
                SizeF sz = g.MeasureString(txt, _fontLabel);
                float midX = width / 2;
                float midY = 12;
                
                using var brush = new SolidBrush(_colTextMuted);
                using var pen = new Pen(Color.FromArgb(40, 255, 255, 255));
                
                g.DrawString(txt, _fontLabel, brush, midX - (sz.Width / 2), midY - (sz.Height / 2));
                g.DrawLine(pen, 0, midY, midX - (sz.Width / 2) - 15, midY);
                g.DrawLine(pen, midX + (sz.Width / 2) + 15, midY, width, midY);
            };
            _mainContainer.Controls.Add(divPanel);
            y += 50;

            // 5. Email Field
            var emailPnl = CreateFloatingLabelInput("Email", "you@example.com", false);
            emailPnl.Location = new Point(0, y);
            _mainContainer.Controls.Add(emailPnl);
            y += 90;

            // 6. Password Field
            var passPnl = CreateFloatingLabelInput("Password", "", true);
            passPnl.Location = new Point(0, y);
            _mainContainer.Controls.Add(passPnl);
            y += 100;

            // 7. Sign In Button (Scaled Height)
            var btnSign = new Button
            {
                Text = "",
                BackColor = _colAccent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(width, 55), // SCALED UP
                Location = new Point(0, y),
                Cursor = Cursors.Hand
            };
            btnSign.FlatAppearance.BorderSize = 0;
            btnSign.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = GetRoundedPath(new Rectangle(0, 0, btnSign.Width, btnSign.Height), 8);
                btnSign.Region = new Region(path);
                
                using var bg = new SolidBrush(_colAccent);
                e.Graphics.FillPath(bg, path);

                string txt = "Sign In";
                SizeF sz = e.Graphics.MeasureString(txt, _fontBtnSign);
                using var br = new SolidBrush(Color.White);
                e.Graphics.DrawString(txt, _fontBtnSign, br, (btnSign.Width - sz.Width)/2, (btnSign.Height - sz.Height)/2);
            };
            btnSign.Click += (s, e) => MessageBox.Show("Please use Google Sign In.");
            _mainContainer.Controls.Add(btnSign);

            // License activation shortcut (Desktop license mode)
            var lnkLicense = new Label
            {
                Text = "Have a license key? Activate it here",
                Font = _fontLabel,
                ForeColor = _colAccent,
                AutoSize = true,
                Cursor = Cursors.Hand,
            };
            // Center under the button
            lnkLicense.Location = new Point((width - lnkLicense.PreferredWidth) / 2, btnSign.Bottom + 18);
            lnkLicense.Click += (s, e) => { this.DialogResult = DialogResult.Retry; this.Close(); };
            lnkLicense.MouseEnter += (s, e) => lnkLicense.ForeColor = _colTextMain;
            lnkLicense.MouseLeave += (s, e) => lnkLicense.ForeColor = _colAccent;
            _mainContainer.Controls.Add(lnkLicense);
        }

        private Panel CreateFloatingLabelInput(string label, string placeholder, bool isPassword)
        {
            int h = 65; // Taller container
            int w = _mainContainer.Width;
            
            var container = new Panel { Size = new Size(w, h), BackColor = Color.Transparent };
            
            // Label
            var lbl = new Label 
            { 
                Text = label, 
                Font = _fontLabel, 
                ForeColor = _colTextMuted, 
                BackColor = _colBackground, 
                AutoSize = true,
                Location = new Point(16, 0)
            };
            
            var boxPnl = new Panel 
            { 
                Size = new Size(w, 55), // Taller Input
                Location = new Point(0, 10), 
                BackColor = Color.Transparent 
            };

            boxPnl.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(_colBorder, 1);
                using var brush = new SolidBrush(_colSurface);
                var rect = new Rectangle(0, 0, boxPnl.Width - 1, boxPnl.Height - 1);
                using var path = GetRoundedPath(rect, 8);
                
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            };

            var txt = new TextBox
            {
                Text = isPassword ? "" : placeholder,
                BorderStyle = BorderStyle.None,
                BackColor = _colSurface,
                ForeColor = _colTextMain,
                Font = _fontInput,
                Location = new Point(16, 14), // Adjusted padding
                Width = w - 60,
                UseSystemPasswordChar = isPassword
            };

            var icon = new PictureBox
            {
                Size = new Size(28, 28), // Larger icon
                Location = new Point(w - 40, 13),
                Image = DrawPinkDots(28),
                BackColor = Color.Transparent
            };

            boxPnl.Controls.Add(txt);
            boxPnl.Controls.Add(icon);
            container.Controls.Add(boxPnl);
            container.Controls.Add(lbl);
            lbl.BringToFront();

            return container;
        }

        // ============ GRAPHICS & ASSETS ============

        private Bitmap DrawLogo(int s)
        {
            var b = new Bitmap(s, s);
            using var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var p = new Pen(_colAccent, 2.5f);
            g.DrawRectangle(p, 1, 1, s-3, s-3);
            using var br = new SolidBrush(_colAccent);
            g.FillEllipse(br, s*0.2f, s*0.4f, s*0.25f, s*0.25f);
            g.FillRectangle(br, s*0.4f, s*0.5f, s*0.4f, s*0.1f);
            return b;
        }

        private Bitmap DrawPinkDots(int s)
        {
            var b = new Bitmap(s, s);
            using var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var br = new SolidBrush(_colAccent);
            g.FillPath(br, GetRoundedPath(new Rectangle(0,0,s,s), 6));
            using var w = new SolidBrush(Color.White);
            float d = s/6f;
            float cy = s/2f - d/2f;
            g.FillEllipse(w, s*0.2f, cy, d, d);
            g.FillEllipse(w, s*0.45f, cy, d, d);
            g.FillEllipse(w, s*0.7f, cy, d, d);
            return b;
        }

        private Bitmap DrawGoogleG(int s)
        {
            var b = new Bitmap(s, s);
            using var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, s, s);
            float width = s * 0.12f;
            using var pR = new Pen(Color.FromArgb(234, 67, 53), width);
            using var pB = new Pen(Color.FromArgb(66, 133, 244), width);
            using var pG = new Pen(Color.FromArgb(52, 168, 83), width);
            using var pY = new Pen(Color.FromArgb(251, 188, 5), width);
            
            g.DrawArc(pR, 2, 2, s-4, s-4, 180, 100);
            g.DrawArc(pY, 2, 2, s-4, s-4, 40, 50);
            g.DrawArc(pG, 2, 2, s-4, s-4, 90, 90);
            g.DrawArc(pB, 2, 2, s-4, s-4, 270, 90);
            return b;
        }

        private GraphicsPath GetRoundedPath(Rectangle r, int d)
        {
            var p = new GraphicsPath();
            int dia = d * 2;
            p.AddArc(r.X, r.Y, dia, dia, 180, 90);
            p.AddArc(r.Right - dia, r.Y, dia, dia, 270, 90);
            p.AddArc(r.Right - dia, r.Bottom - dia, dia, dia, 0, 90);
            p.AddArc(r.X, r.Bottom - dia, dia, dia, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ============ LOGIC ============

        private async void StartGoogleAuth()
        {
            try
            {
                btnGoogle.Enabled = false;

                // Combine Supabase anon key parts
                var anonKey = API_KEY_PART1 + API_KEY_PART2;

                // Create an auth session on Supabase (Edge Function)
                using var client = new HttpClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/create-desktop-auth-session");
                req.Headers.Add("apikey", anonKey);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", anonKey);

                var payload = new
                {
                    machine_name = Environment.MachineName,
                    created_at = DateTime.UtcNow.ToString("o")
                };

                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var res = await client.SendAsync(req, _cts.Token);
                var raw = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    // IMPORTANT: Do not embed literal newlines in a normal C# string.
                    // Use Environment.NewLine so this compiles reliably.
                    var nl = Environment.NewLine;
                    MessageBox.Show(
                        $"Failed to start Google sign-in.{nl}{nl}" +
                        $"HTTP {(int)res.StatusCode} ({res.ReasonPhrase}){nl}{nl}" +
                        raw,
                        "Authentication Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                string sessionCode = root.TryGetProperty("sessionCode", out var codeEl) && codeEl.ValueKind == JsonValueKind.String
                    ? (codeEl.GetString() ?? "")
                    : "";

                string authUrl = root.TryGetProperty("authUrl", out var urlEl) && urlEl.ValueKind == JsonValueKind.String
                    ? (urlEl.GetString() ?? "")
                    : "";

                if (string.IsNullOrWhiteSpace(authUrl) && !string.IsNullOrWhiteSpace(sessionCode))
                {
                    // Fallback: build URL from the public site URL
                    authUrl = BuildAuthUrl(sessionCode);
                }

                if (string.IsNullOrWhiteSpace(authUrl) || string.IsNullOrWhiteSpace(sessionCode))
                {
                    MessageBox.Show(
                        "Auth session created, but the response did not include a valid session code / URL.",
                        "Authentication Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                // Open system browser (Google blocks embedded webviews)
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

                // Poll for completion
                _ = Task.Run(() => PollSession(sessionCode, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Authentication error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnGoogle.Enabled = true;
            }
        }

        private async void PollSession(string code, CancellationToken ct)
        {
            try
            {
                var anonKey = API_KEY_PART1 + API_KEY_PART2;

                using var client = new HttpClient();

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(2000, ct);

                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/check-desktop-auth-session");
                    req.Headers.Add("apikey", anonKey);
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", anonKey);

                    req.Content = new StringContent(
                        JsonSerializer.Serialize(new { sessionCode = code }),
                        Encoding.UTF8,
                        "application/json"
                    );

                    using var res = await client.SendAsync(req, ct);
                    var raw = await res.Content.ReadAsStringAsync();

                    if (!res.IsSuccessStatusCode)
                        continue;

                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;

                    var status = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String
                        ? (statusEl.GetString() ?? "")
                        : "";

                    if (status == "complete")
                    {
                        var token = root.TryGetProperty("idToken", out var tEl) && tEl.ValueKind == JsonValueKind.String
                            ? (tEl.GetString() ?? "")
                            : "";

                        var email = root.TryGetProperty("email", out var eEl) && eEl.ValueKind == JsonValueKind.String
                            ? (eEl.GetString() ?? "")
                            : "";

                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            BeginInvoke(new Action(() =>
                            {
                                _onAuthSuccess?.Invoke(token, email);
                                Close();
                            }));
                            return;
                        }
                    }
                    else if (status == "error")
                    {
                        var msg = root.TryGetProperty("message", out var mEl) && mEl.ValueKind == JsonValueKind.String
                            ? (mEl.GetString() ?? "Authentication failed")
                            : "Authentication failed";

                        BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show(msg, "Authentication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch
            {
                // ignore background polling errors
            }
        }
    }
}