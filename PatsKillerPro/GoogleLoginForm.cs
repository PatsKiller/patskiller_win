using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
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
        
        // Fonts
        private readonly Font _fontTitle = new Font("Segoe UI", 32F, FontStyle.Italic | FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontSub = new Font("Segoe UI", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontInput = new Font("Segoe UI", 16F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontLabel = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontBtnGoogle = new Font("Segoe UI Semibold", 15F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _fontBtnSign = new Font("Segoe UI Semibold", 16F, FontStyle.Regular, GraphicsUnit.Pixel);

        // ============ API CONSTANTS ============
        private CancellationTokenSource? _cts;
        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjacx.supabase.co";
        private const string API_KEY_PART1 = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0";
        private const string API_KEY_PART2 = ".iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";
        private const string AUTH_URL = "https://patskiller.com/desktop-auth?session=";
        private readonly HttpClient _http = new HttpClient();

        // ============ UI STATE ============
        private Panel _mainContainer = null!;

        public GoogleLoginForm()
        {
            // 1. Form Configuration
            this.AutoScaleMode = AutoScaleMode.None; // Prevent DPI scaling from breaking layout
            this.DoubleBuffered = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(400, 600);
            this.BackColor = _colBackground;
            this.Text = "PatsKiller Pro";
            
            // Allow dragging the borderless form
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
            var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.Transparent };
            
            // Logo
            var logo = new PictureBox { Size = new Size(32, 32), Location = new Point(24, 14), Image = DrawLogo(32) };
            var title = new Label { Text = "PatsKiller Pro", Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = _colAccent, AutoSize = true, Location = new Point(64, 18) };
            
            // Close Button
            var btnClose = new Label { Text = "●", Font = new Font("Segoe UI", 14F), ForeColor = _colBorder, AutoSize = true, Location = new Point(360, 14), Cursor = Cursors.Hand };
            btnClose.Click += (s, e) => Application.Exit();
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = _colAccent;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = _colBorder;

            header.Controls.Add(logo);
            header.Controls.Add(title);
            header.Controls.Add(btnClose);
            this.Controls.Add(header);

            // -- Main Content Container --
            int contentWidth = 320;
            _mainContainer = new Panel 
            { 
                Size = new Size(contentWidth, 500), 
                Location = new Point((this.Width - contentWidth) / 2, 80),
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
                Size = new Size(width, 50),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y)
            };
            _mainContainer.Controls.Add(lblWelcome);
            y += 50;

            // 2. Subtitle
            var lblSub = new Label
            {
                Text = "Sign in to access your account",
                Font = _fontSub,
                ForeColor = _colTextMuted,
                AutoSize = false,
                Size = new Size(width, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y)
            };
            _mainContainer.Controls.Add(lblSub);
            y += 45; // Spacing

            // 3. Google Button (CUSTOM PAINT for perfect alignment)
            var btnGoogle = new Button
            {
                Text = "", // Drawing text manually to fix overlap issues
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(width, 45),
                Location = new Point(0, y),
                Cursor = Cursors.Hand
            };
            btnGoogle.FlatAppearance.BorderSize = 0;
            
            // Pre-generate the logo to save resources on redraw
            var googleIcon = DrawGoogleG(24);

            btnGoogle.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // Rounded Corners
                using var path = GetRoundedPath(new Rectangle(0, 0, btnGoogle.Width, btnGoogle.Height), 6);
                btnGoogle.Region = new Region(path);
                
                // Draw Background
                using var bgBrush = new SolidBrush(Color.White);
                g.FillPath(bgBrush, path);

                // Draw Icon (Fixed Left Position)
                g.DrawImage(googleIcon, 16, (btnGoogle.Height - 24) / 2);

                // Draw Text (Centered)
                string text = "Continue with Google";
                SizeF textSize = g.MeasureString(text, _fontBtnGoogle);
                
                float textX = (btnGoogle.Width - textSize.Width) / 2;
                float textY = (btnGoogle.Height - textSize.Height) / 2;
                
                // Prevent overlap if button is too small
                if (textX < 45) textX = 45; 

                using var textBrush = new SolidBrush(Color.FromArgb(55, 65, 81));
                g.DrawString(text, _fontBtnGoogle, textBrush, textX, textY);
            };
            
            btnGoogle.Click += (s, e) => StartGoogleAuth();
            _mainContainer.Controls.Add(btnGoogle);
            y += 65;

            // 4. Divider
            var divPanel = new Panel { Size = new Size(width, 20), Location = new Point(0, y) };
            divPanel.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                string txt = "or sign in with email";
                SizeF sz = g.MeasureString(txt, _fontLabel);
                float midX = width / 2;
                float midY = 10;
                
                using var brush = new SolidBrush(_colTextMuted);
                using var pen = new Pen(Color.FromArgb(40, 255, 255, 255));
                
                g.DrawString(txt, _fontLabel, brush, midX - (sz.Width / 2), midY - (sz.Height / 2));
                g.DrawLine(pen, 0, midY, midX - (sz.Width / 2) - 10, midY);
                g.DrawLine(pen, midX + (sz.Width / 2) + 10, midY, width, midY);
            };
            _mainContainer.Controls.Add(divPanel);
            y += 40;

            // 5. Email Field (Custom)
            var emailPnl = CreateFloatingLabelInput("Email", "you@example.com", false);
            emailPnl.Location = new Point(0, y);
            _mainContainer.Controls.Add(emailPnl);
            y += 75;

            // 6. Password Field (Custom)
            var passPnl = CreateFloatingLabelInput("Password", "", true);
            passPnl.Location = new Point(0, y);
            _mainContainer.Controls.Add(passPnl);
            y += 85;

            // 7. Sign In Button
            var btnSign = new Button
            {
                Text = "", // Draw Manually for smoother font rendering
                BackColor = _colAccent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(width, 45),
                Location = new Point(0, y),
                Cursor = Cursors.Hand
            };
            btnSign.FlatAppearance.BorderSize = 0;
            btnSign.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = GetRoundedPath(new Rectangle(0, 0, btnSign.Width, btnSign.Height), 6);
                btnSign.Region = new Region(path);
                
                using var bg = new SolidBrush(_colAccent);
                e.Graphics.FillPath(bg, path);

                // Centered Text
                string txt = "Sign In";
                SizeF sz = e.Graphics.MeasureString(txt, _fontBtnSign);
                using var br = new SolidBrush(Color.White);
                e.Graphics.DrawString(txt, _fontBtnSign, br, (btnSign.Width - sz.Width)/2, (btnSign.Height - sz.Height)/2);
            };
            btnSign.Click += (s, e) => MessageBox.Show("Please use Google Sign In.");
            _mainContainer.Controls.Add(btnSign);
        }

        private Panel CreateFloatingLabelInput(string label, string placeholder, bool isPassword)
        {
            int h = 55;
            int w = _mainContainer.Width;
            
            var container = new Panel { Size = new Size(w, h), BackColor = Color.Transparent };
            
            // Label overlaps the border
            var lbl = new Label 
            { 
                Text = label, 
                Font = _fontLabel, 
                ForeColor = _colTextMuted, 
                BackColor = _colBackground, 
                AutoSize = true,
                Location = new Point(12, 0)
            };
            
            var boxPnl = new Panel 
            { 
                Size = new Size(w, 45), 
                Location = new Point(0, 8), 
                BackColor = Color.Transparent 
            };

            boxPnl.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(_colBorder, 1);
                using var brush = new SolidBrush(_colSurface);
                var rect = new Rectangle(0, 0, boxPnl.Width - 1, boxPnl.Height - 1);
                using var path = GetRoundedPath(rect, 6);
                
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
                Location = new Point(12, 11),
                Width = w - 50,
                UseSystemPasswordChar = isPassword
            };

            var icon = new PictureBox
            {
                Size = new Size(24, 24),
                Location = new Point(w - 32, 10),
                Image = DrawPinkDots(24),
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
            using var p = new Pen(_colAccent, 2);
            g.DrawRectangle(p, 1, 1, s-3, s-3);
            using var br = new SolidBrush(_colAccent);
            g.FillEllipse(br, 6, 12, 8, 8);
            g.FillRectangle(br, 12, 15, 12, 3);
            return b;
        }

        private Bitmap DrawPinkDots(int s)
        {
            var b = new Bitmap(s, s);
            using var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var br = new SolidBrush(_colAccent);
            g.FillPath(br, GetRoundedPath(new Rectangle(0,0,s,s), 4));
            using var w = new SolidBrush(Color.White);
            g.FillEllipse(w, 5, 10, 3, 3);
            g.FillEllipse(w, 10, 10, 3, 3);
            g.FillEllipse(w, 15, 10, 3, 3);
            return b;
        }

        private Bitmap DrawGoogleG(int s)
        {
            var b = new Bitmap(s, s);
            using var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, s, s);
            using var pR = new Pen(Color.FromArgb(234, 67, 53), 2.5f);
            using var pB = new Pen(Color.FromArgb(66, 133, 244), 2.5f);
            using var pG = new Pen(Color.FromArgb(52, 168, 83), 2.5f);
            using var pY = new Pen(Color.FromArgb(251, 188, 5), 2.5f);
            
            // Draw standard G arcs
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
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/create-desktop-auth-session");
                req.Headers.Add("apikey", API_KEY_PART1 + API_KEY_PART2);
                req.Content = new StringContent(JsonSerializer.Serialize(new { machineId = Environment.MachineName }), Encoding.UTF8, "application/json");

                var res = await _http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    var code = json.RootElement.GetProperty("sessionCode").GetString();
                    if (!string.IsNullOrEmpty(code))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = AUTH_URL + code, UseShellExecute = true });
                        PollSession(code, _cts.Token);
                    }
                }
            }
            catch { MessageBox.Show("Connection Error"); }
        }

        private async void PollSession(string code, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000);
                try {
                    var req = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/check-desktop-auth-session");
                    req.Headers.Add("apikey", API_KEY_PART1 + API_KEY_PART2);
                    req.Content = new StringContent(JsonSerializer.Serialize(new { sessionCode = code, machineId = Environment.MachineName }), Encoding.UTF8, "application/json");
                    
                    var res = await _http.SendAsync(req, ct);
                    if (!res.IsSuccessStatusCode) continue;
                    
                    var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
                    if (json.GetProperty("status").GetString() == "complete") {
                        this.UserEmail = json.TryGetProperty("email", out var e) ? e.GetString() : null;
                        this.AuthToken = json.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
                        this.RefreshToken = json.TryGetProperty("refreshToken", out var r) ? r.GetString() : null;
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                        return;
                    }
                } catch { }
            }
        }
    }
}