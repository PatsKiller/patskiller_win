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

namespace PatsKillerPro
{
    /// <summary>
    /// Polished OAuth login form.
    /// Fixes: Text clipping, input styling, responsiveness, and visual hierarchy.
    /// </summary>
    public class GoogleLoginForm : Form
    {
        // ============ LOCAL LOGGER ============
        private static class Logger
        {
            public static void Info(string message) => Write("INFO", message);
            public static void Error(string message, Exception ex) => Write("ERROR", $"{message}\n{ex}");
            private static void Write(string level, string message)
            {
                try { System.Diagnostics.Trace.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}"); } catch { }
            }
        }

        // ============ UI CONTROLS ============
        private Panel _headerPanel = null!;
        private Panel _contentPanel = null!;
        private PictureBox _logoBox = null!;
        private Label _lblTitle = null!;

        // Panels
        private Panel _loginPanel = null!;
        private FlowLayoutPanel _loginStack = null!;
        private Panel _waitingPanel = null!;
        private Panel _successPanel = null!;
        private Panel _errorPanel = null!;

        // ============ THEME COLORS ============
        private readonly Color _colBgTop = ColorTranslator.FromHtml("#111827");
        private readonly Color _colBgBot = ColorTranslator.FromHtml("#0B1220");
        private readonly Color _colHeader = ColorTranslator.FromHtml("#1F2937");
        private readonly Color _colHeaderBorder = ColorTranslator.FromHtml("#374151");
        
        // Input & Surfaces
        private readonly Color _colInputBg = ColorTranslator.FromHtml("#161E2E"); // Darker input bg
        private readonly Color _colInputBorder = ColorTranslator.FromHtml("#374151");
        private readonly Color _colInputActive = ColorTranslator.FromHtml("#E94796"); // Pink highlight

        // Text
        private readonly Color _colTextMain = ColorTranslator.FromHtml("#F9FAFB");
        private readonly Color _colTextDim = ColorTranslator.FromHtml("#9CA3AF");
        private readonly Color _colTextLabel = ColorTranslator.FromHtml("#D1D5DB");

        // Brand
        private readonly Color _colAccent = ColorTranslator.FromHtml("#E94796"); // Brand Pink
        private readonly Color _colAccentHover = ColorTranslator.FromHtml("#DB2777");
        private readonly Color _colSuccess = ColorTranslator.FromHtml("#10B981");
        private readonly Color _colGoogleSurface = Color.White;

        // ============ DATA ============
        public string? AuthToken { get; private set; }
        public string? RefreshToken { get; private set; }
        public string? UserEmail { get; private set; }
        public int TokenCount { get; private set; }

        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjacx.supabase.co";
        // Split key to prevent build errors
        private const string SUPABASE_KEY_PART1 = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0";
        private const string SUPABASE_KEY_PART2 = ".iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";
        private const string AUTH_PAGE_URL = "https://patskiller.com/desktop-auth?session=";

        private CancellationTokenSource? _cts;
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _machineId;

        public GoogleLoginForm()
        {
            _machineId = GetMachineId();
            InitializeComponent();
            ShowLoginState();
        }

        private static string GetMachineId()
        {
            try { return Environment.MachineName; } catch { return "UnknownPC"; }
        }

        private void InitializeComponent()
        {
            // Window Setup
            Text = "PatsKiller Pro";
            ClientSize = new Size(420, 680); // Slightly narrower/taller for modern mobile-like feel
            MinimumSize = new Size(400, 600);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = _colBgTop;
            Font = new Font("Segoe UI", 10F);
            DoubleBuffered = true;

            // Dark Mode Title Bar
            try { int val = 1; DwmSetWindowAttribute(Handle, 20, ref val, 4); } catch { }

            // Layout
            CreateHeaderPanel();
            CreateContentPanel();
            CreateLoginPanel();
            CreateWaitingPanel();
            CreateSuccessPanel();
            CreateErrorPanel();

            // Events
            FormClosing += (s, e) => _cts?.Cancel();
            Resize += (s, e) => { UpdateResponsiveLayout(); CenterActivePanel(); };
            Shown += (s, e) => { UpdateResponsiveLayout(); CenterActivePanel(); };
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new LinearGradientBrush(ClientRectangle, _colBgTop, _colBgBot, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        // ============ HEADER ============
        private void CreateHeaderPanel()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.Transparent,
                Padding = new Padding(20, 0, 20, 0)
            };

            // Custom Paint for Border
            _headerPanel.Paint += (s, e) =>
            {
                using var p = new Pen(_colHeaderBorder);
                e.Graphics.DrawLine(p, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
            };

            // Logo
            _logoBox = new PictureBox
            {
                Size = new Size(48, 48),
                Location = new Point(20, 16), // Vertically centered (80-48)/2 = 16
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = CreateBrandLogo(48)
            };
            _headerPanel.Controls.Add(_logoBox);

            // Title
            _lblTitle = new Label
            {
                Text = "PatsKiller Pro",
                ForeColor = _colAccent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                AutoSize = true,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _headerPanel.Controls.Add(_lblTitle);

            // Responsive Header Layout
            _headerPanel.Resize += (s, e) =>
            {
                // Align title next to logo
                int x = _logoBox.Right + 12;
                int availW = _headerPanel.Width - x - 10;
                
                _lblTitle.Location = new Point(x, (_headerPanel.Height - _lblTitle.Height) / 2);
                
                // Scale font if text is too long (prevents clipping)
                float fontSize = 18f;
                while (TextRenderer.MeasureText(_lblTitle.Text, _lblTitle.Font).Width > availW && fontSize > 12f)
                {
                    fontSize -= 1f;
                    _lblTitle.Font = new Font("Segoe UI", fontSize, FontStyle.Bold);
                }
            };

            Controls.Add(_headerPanel);
        }

        // ============ CONTENT AREA ============
        private void CreateContentPanel()
        {
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0), // Padding handled by internal panels
                AutoScroll = false
            };
            Controls.Add(_contentPanel);
        }

        // ============ LOGIN UI ============
        private void CreateLoginPanel()
        {
            _loginPanel = new Panel { Visible = false, AutoSize = true };
            _loginStack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            _loginPanel.Controls.Add(_loginStack);

            // 1. Welcome Text
            var lblWelcome = new Label
            {
                Name = "lblWelcome",
                Text = "Welcome",
                Font = new Font("Segoe UI", 36F, FontStyle.Italic), // Large but handled by resize logic
                ForeColor = _colTextMain,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 0, 0)
            };

            // 2. Subtitle
            var lblSub = new Label
            {
                Name = "lblSub",
                Text = "Sign in to access your account",
                Font = new Font("Segoe UI", 11F),
                ForeColor = _colTextDim,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 5, 0, 30) // Spacing below subtitle
            };

            // 3. Google Button
            var btnGoogle = CreateGoogleButton();
            btnGoogle.Margin = new Padding(0, 0, 0, 20);

            // 4. Divider
            var divider = CreateDivider();
            divider.Margin = new Padding(0, 0, 0, 20);

            // 5. Inputs
            var lblEmail = CreateLabel("Email Address");
            var txtEmail = CreateInput("name@example.com", false);
            
            var lblPass = CreateLabel("Password");
            var txtPass = CreateInput("••••••••", true);

            // 6. Sign In Button
            var btnSign = CreateButton("Sign In", _colAccent, _colAccentHover);
            btnSign.Click += (s, e) => MessageBox.Show("Please use 'Continue with Google'.", "Info");

            // Add to Stack
            _loginStack.Controls.AddRange(new Control[] {
                lblWelcome, lblSub, btnGoogle, divider, 
                lblEmail, txtEmail, lblPass, txtPass, btnSign
            });

            _contentPanel.Controls.Add(_loginPanel);
        }

        private Control CreateInput(string placeholder, bool isPassword)
        {
            int height = 50;
            var pnl = new Panel { Height = height, BackColor = Color.Transparent, Margin = new Padding(0, 5, 0, 20) };
            
            // Background & Border Paint
            bool focused = false;
            pnl.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, pnl.Width - 1, pnl.Height - 1);
                using var path = GetRoundedPath(rect, 8);
                using var brush = new SolidBrush(_colInputBg);
                using var pen = new Pen(focused ? _colAccent : _colInputBorder, focused ? 1.5f : 1f);
                
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            };

            // Text Box
            var txt = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = _colInputBg,
                ForeColor = _colTextMain,
                Font = new Font("Segoe UI", 11F),
                PlaceholderText = placeholder,
                UseSystemPasswordChar = isPassword,
                Location = new Point(15, 13), // Vertically centered
                Width = pnl.Width - 60
            };

            // Action Icon (Eye or Arrow)
            var btnIcon = new PictureBox
            {
                Size = new Size(30, 30),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent,
                SizeMode = PictureBoxSizeMode.CenterImage
            };
            
            // Draw clean vector icons instead of blobs
            btnIcon.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                // Draw rounded pink background
                using var path = GetRoundedPath(new Rectangle(0,0,29,29), 8);
                using var brush = new SolidBrush(_colAccent);
                e.Graphics.FillPath(brush, path);

                // Draw Icon (White)
                using var pen = new Pen(Color.White, 2f);
                if (isPassword)
                {
                    // Eye Icon
                    e.Graphics.DrawEllipse(pen, 8, 10, 14, 10);
                    e.Graphics.FillEllipse(Brushes.White, 13, 13, 4, 4);
                }
                else
                {
                    // Arrow Icon
                    e.Graphics.DrawLine(pen, 10, 15, 20, 15);
                    e.Graphics.DrawLine(pen, 16, 11, 20, 15);
                    e.Graphics.DrawLine(pen, 16, 19, 20, 15);
                }
            };

            // Logic
            txt.Enter += (s, e) => { focused = true; pnl.Invalidate(); };
            txt.Leave += (s, e) => { focused = false; pnl.Invalidate(); };
            
            btnIcon.Click += (s, e) =>
            {
                if (isPassword) { txt.UseSystemPasswordChar = !txt.UseSystemPasswordChar; txt.Focus(); }
                else { txt.Text = ""; txt.Focus(); }
            };

            // Layout inside panel
            pnl.Controls.Add(btnIcon);
            pnl.Controls.Add(txt);
            
            pnl.Resize += (s, e) =>
            {
                btnIcon.Location = new Point(pnl.Width - 40, 10);
                txt.Width = pnl.Width - 55;
            };
            
            // Propagate clicks to focus text
            pnl.Click += (s,e) => txt.Focus();

            return pnl;
        }

        private Button CreateButton(string text, Color bg, Color hover)
        {
            var btn = new Button
            {
                Text = text,
                Height = 50,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12F),
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = hover;
            
            // Rounded Corners
            btn.Paint += (s, e) =>
            {
                using var path = GetRoundedPath(new Rectangle(0, 0, btn.Width, btn.Height), 8);
                btn.Region = new Region(path);
            };
            
            return btn;
        }

        private Button CreateGoogleButton()
        {
            var btn = new Button
            {
                Text = "  Continue with Google",
                Height = 50,
                FlatStyle = FlatStyle.Flat,
                BackColor = _colGoogleSurface,
                ForeColor = Color.FromArgb(60, 64, 67),
                Font = new Font("Segoe UI Semibold", 11F),
                Cursor = Cursors.Hand,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageAlign = ContentAlignment.MiddleRight,
                Image = CreateGoogleLogo(24),
                Margin = new Padding(0)
            };
            btn.FlatAppearance.BorderSize = 0;
            
            // Custom Border Paint
            btn.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
                using var path = GetRoundedPath(rect, 8);
                
                // Draw Border
                using var pen = new Pen(Color.FromArgb(218, 220, 224), 1);
                g.DrawPath(pen, path);
                
                // Clip region
                btn.Region = new Region(path);
            };
            
            btn.Click += async (s, e) => await StartGoogleAuthAsync();
            return btn;
        }

        private Panel CreateDivider()
        {
            var pnl = new Panel { Height = 20, BackColor = Color.Transparent };
            var lbl = new Label 
            { 
                Text = "or sign in with email", 
                ForeColor = _colTextDim, 
                AutoSize = true, 
                Font = new Font("Segoe UI", 9F)
            };
            pnl.Controls.Add(lbl);
            
            pnl.Paint += (s, e) =>
            {
                int y = pnl.Height / 2;
                int textW = lbl.Width + 20;
                int lineW = (pnl.Width - textW) / 2;
                using var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1);
                
                e.Graphics.DrawLine(pen, 0, y, lineW, y);
                e.Graphics.DrawLine(pen, pnl.Width - lineW, y, pnl.Width, y);
            };
            
            pnl.Resize += (s, e) => lbl.Location = new Point((pnl.Width - lbl.Width)/2, (pnl.Height - lbl.Height)/2);
            return pnl;
        }

        private Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = _colTextLabel,
                Font = new Font("Segoe UI", 9.5F),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0)
            };
        }

        // ============ LAYOUT LOGIC (Crucial for Fix) ============
        private void UpdateResponsiveLayout()
        {
            if (_contentPanel == null || _loginPanel == null) return;

            // Calculate width: Container width minus generous padding (e.g. 40px on each side)
            int pad = 40;
            int targetW = _contentPanel.Width - (pad * 2);
            targetW = Math.Max(280, Math.Min(400, targetW)); // Clamp width

            _loginPanel.SuspendLayout();
            _loginStack.SuspendLayout();

            // Apply width to container
            _loginPanel.Width = targetW;
            _loginStack.Width = targetW;

            // Apply width to ALL children
            foreach (Control c in _loginStack.Controls)
            {
                c.Width = targetW;
                
                // Center Labels specifically
                if (c is Label lbl)
                {
                    lbl.AutoSize = false; 
                    lbl.Width = targetW;
                    
                    // Measure height required for text wrapping
                    Size sz = TextRenderer.MeasureText(lbl.Text, lbl.Font, new Size(targetW, 1000), TextFormatFlags.WordBreak);
                    lbl.Height = sz.Height + 10;
                }
            }

            _loginStack.ResumeLayout();
            _loginPanel.ResumeLayout();
        }

        private void CenterActivePanel()
        {
            if (_contentPanel == null) return;
            Panel? active = null;
            if (_loginPanel?.Visible == true) active = _loginPanel;
            else if (_waitingPanel?.Visible == true) active = _waitingPanel;
            else if (_successPanel?.Visible == true) active = _successPanel;
            else if (_errorPanel?.Visible == true) active = _errorPanel;

            if (active != null)
            {
                active.Location = new Point(
                    (_contentPanel.Width - active.Width) / 2,
                    Math.Max(20, (_contentPanel.Height - active.Height) / 2)
                );
            }
        }

        // ============ GRAPHICS HELPERS ============
        private GraphicsPath GetRoundedPath(Rectangle rect, int r)
        {
            var path = new GraphicsPath();
            int d = r * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private Image CreateBrandLogo(int s)
        {
            var bmp = new Bitmap(s, s);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            // Background
            using var brush = new LinearGradientBrush(new Point(0,0), new Point(s,s), _colBgTop, _colBgBot);
            g.FillEllipse(brush, 0, 0, s, s);
            using var pen = new Pen(_colAccent, 2);
            g.DrawEllipse(pen, 1, 1, s-3, s-3);
            
            // Key Icon
            using var iconBrush = new SolidBrush(_colAccent);
            int cx = s/2, cy = s/2;
            g.FillEllipse(iconBrush, cx-8, cy-8, 10, 10); // Head
            g.FillRectangle(iconBrush, cx-2, cy-2, 14, 4); // Shaft
            g.FillRectangle(iconBrush, cx+6, cy+2, 2, 4); // Teeth
            
            return bmp;
        }

        private Image CreateGoogleLogo(int s)
        {
            var bmp = new Bitmap(s, s);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            float stroke = s * 0.15f;
            var rect = new RectangleF(stroke, stroke, s - stroke*2, s - stroke*2);
            
            using var r = new Pen(Color.FromArgb(234, 67, 53), stroke);
            using var y = new Pen(Color.FromArgb(251, 188, 5), stroke);
            using var gr = new Pen(Color.FromArgb(52, 168, 83), stroke);
            using var b = new Pen(Color.FromArgb(66, 133, 244), stroke);

            g.DrawArc(r, rect, 190, 100);
            g.DrawArc(y, rect, 40, 100);
            g.DrawArc(gr, rect, 130, 60);
            g.DrawArc(b, rect, 220, 100); // Rough approximation of G
            g.DrawLine(b, s/2, s/2, s-stroke, s/2);
            
            return bmp;
        }

        // ============ AUTH STATES (Waiting/Success/Error) ============
        // Simplified for brevity, follows same centering logic
        private void CreateWaitingPanel() 
        {
            _waitingPanel = CreateStatePanel("🔄", "Waiting...", "Check your browser to continue.", false);
            _waitingPanel.Controls[3].Click += (s, e) => { _cts?.Cancel(); ShowLoginState(); };
        }
        private void CreateSuccessPanel() 
        {
            _successPanel = CreateStatePanel("✓", "Success!", "You are now signed in.", true);
            _successPanel.Controls[3].Text = "Continue";
            _successPanel.Controls[3].Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        }
        private void CreateErrorPanel() 
        {
            _errorPanel = CreateStatePanel("⚠", "Error", "Sign in failed.", false);
            _errorPanel.Controls[3].Text = "Try Again";
            _errorPanel.Controls[3].Click += (s, e) => ShowLoginState();
        }

        private Panel CreateStatePanel(string icon, string title, string sub, bool success)
        {
            var pnl = new Panel { Size = new Size(300, 300), BackColor = Color.Transparent, Visible = false };
            var lblIcon = new Label { Text = icon, Font = new Font("Segoe UI", 48F), ForeColor = success ? _colSuccess : _colAccent, AutoSize = false, Size = new Size(300, 80), TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, 20) };
            var lblTitle = new Label { Text = title, Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = _colTextMain, AutoSize = false, Size = new Size(300, 40), TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, 110) };
            var lblSub = new Label { Text = sub, Font = new Font("Segoe UI", 11F), ForeColor = _colTextDim, AutoSize = false, Size = new Size(300, 40), TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, 150) };
            var btn = CreateButton("Cancel", _colHeader, _colHeaderBorder);
            btn.Width = 160; btn.Height = 40; btn.Location = new Point(70, 220);

            pnl.Controls.AddRange(new Control[] { lblIcon, lblTitle, lblSub, btn });
            _contentPanel.Controls.Add(pnl);
            return pnl;
        }

        // ============ LOGIC ============
        private void ShowLoginState() { HideAll(); _loginPanel.Visible = true; CenterActivePanel(); }
        private void HideAll() { _loginPanel.Visible = _waitingPanel.Visible = _successPanel.Visible = _errorPanel.Visible = false; }

        private async Task StartGoogleAuthAsync()
        {
            _cts?.Cancel(); _cts = new CancellationTokenSource();
            HideAll(); _waitingPanel.Visible = true; CenterActivePanel();
            
            try
            {
                var session = await CreateSessionAsync();
                if (session == null) throw new Exception("API Error");
                
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = AUTH_PAGE_URL + session.SessionCode, UseShellExecute = true });
                await PollSessionAsync(session.SessionCode, _cts.Token);
            }
            catch { HideAll(); _errorPanel.Visible = true; CenterActivePanel(); }
        }

        private async Task<SessionInfo?> CreateSessionAsync()
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/create-desktop-auth-session");
                req.Headers.Add("apikey", SUPABASE_KEY_PART1 + SUPABASE_KEY_PART2);
                req.Content = new StringContent(JsonSerializer.Serialize(new { machineId = _machineId }), Encoding.UTF8, "application/json");
                var res = await _httpClient.SendAsync(req);
                if (!res.IsSuccessStatusCode) return null;
                
                var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return new SessionInfo { SessionCode = json.RootElement.GetProperty("sessionCode").GetString() ?? "" };
            }
            catch { return null; }
        }

        private async Task PollSessionAsync(string code, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
                var req = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/check-desktop-auth-session");
                req.Headers.Add("apikey", SUPABASE_KEY_PART1 + SUPABASE_KEY_PART2);
                req.Content = new StringContent(JsonSerializer.Serialize(new { sessionCode = code, machineId = _machineId }), Encoding.UTF8, "application/json");
                
                var res = await _httpClient.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) continue;

                var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
                if (json.GetProperty("status").GetString() == "complete")
                {
                    UserEmail = json.GetProperty("email").GetString();
                    AuthToken = json.GetProperty("accessToken").GetString();
                    HideAll(); _successPanel.Visible = true; CenterActivePanel();
                    return;
                }
            }
        }

        private class SessionInfo { public string SessionCode { get; set; } = ""; }
    }
}