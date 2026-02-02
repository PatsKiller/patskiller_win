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
        // ============ COLOR PALETTE (Mockup Exact) ============
        private readonly Color _colBackground = ColorTranslator.FromHtml("#0F172A"); // Dark Navy
        private readonly Color _colInputSurface = ColorTranslator.FromHtml("#1E293B"); // Slightly lighter slate
        private readonly Color _colInputBorder = ColorTranslator.FromHtml("#334155"); // Border slate
        private readonly Color _colAccent = ColorTranslator.FromHtml("#EC4899"); // Hot Pink
        private readonly Color _colAccentHover = ColorTranslator.FromHtml("#DB2777");
        private readonly Color _colTextWhite = ColorTranslator.FromHtml("#F8FAFC");
        private readonly Color _colTextGray = ColorTranslator.FromHtml("#94A3B8");
        private readonly Color _colGoogleBg = Color.White;

        // ============ STATE ============
        private CancellationTokenSource? _cts;
        private const string SUPABASE_URL = "https://kmpnplpijuzzbftsjacx.supabase.co";
        private const string API_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImttcG5wbHBpanV6emJmdHNqYWN4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzA5ODgwMTgsImV4cCI6MjA0NjU2NDAxOH0.iqKMFa_Ye7LCG-n7F1a1rgdsVBPkz3TmT_x0lMm8TT8";
        private const string AUTH_URL = "https://patskiller.com/desktop-auth?session=";
        private readonly HttpClient _http = new HttpClient();

        // ============ UI ELEMENTS ============
        private Panel _contentContainer;

        public GoogleLoginForm()
        {
            // Form Setup
            this.DoubleBuffered = true;
            this.FormBorderStyle = FormBorderStyle.None; // Custom chrome
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(400, 600);
            this.BackColor = _colBackground;
            this.Text = "PatsKiller Pro";

            // Enable drag to move
            this.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };

            InitializeUI();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private void InitializeUI()
        {
            // 1. HEADER
            var header = CreateHeader();
            this.Controls.Add(header);

            // 2. CONTENT CONTAINER (Centers the login form)
            _contentContainer = new Panel
            {
                Size = new Size(340, 500), // Width of the actual form content
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.None
            };
            _contentContainer.Location = new Point((this.Width - _contentContainer.Width) / 2, 80);
            this.Controls.Add(_contentContainer);

            // 3. BUILD FORM
            BuildLoginForm();
        }

        private void BuildLoginForm()
        {
            int y = 0;

            // Welcome
            var lblWelcome = new Label
            {
                Text = "Welcome",
                Font = new Font("Segoe UI", 32F, FontStyle.Italic),
                ForeColor = _colTextWhite,
                AutoSize = false,
                Size = new Size(_contentContainer.Width, 60),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y)
            };
            _contentContainer.Controls.Add(lblWelcome);
            y += 60;

            // Subtitle
            var lblSub = new Label
            {
                Text = "Sign in to access your account",
                Font = new Font("Segoe UI", 10F),
                ForeColor = _colTextGray,
                AutoSize = false,
                Size = new Size(_contentContainer.Width, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y)
            };
            _contentContainer.Controls.Add(lblSub);
            y += 45;

            // Google Button
            var btnGoogle = new Button
            {
                Text = "Continue with Google",
                Font = new Font("Segoe UI Semibold", 11F),
                ForeColor = Color.FromArgb(55, 65, 81),
                BackColor = _colGoogleBg,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(_contentContainer.Width, 50),
                Location = new Point(0, y),
                Cursor = Cursors.Hand,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = DrawGoogleG(20) // Custom GDI+ Icon
            };
            btnGoogle.FlatAppearance.BorderSize = 0;
            btnGoogle.Region = GetRoundedRegion(btnGoogle.ClientRectangle, 8);
            btnGoogle.Click += (s, e) => StartGoogleAuth();
            _contentContainer.Controls.Add(btnGoogle);
            y += 70;

            // Divider
            var pnlDivider = new Panel { Size = new Size(_contentContainer.Width, 20), Location = new Point(0, y) };
            pnlDivider.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var txt = "or sign in with email";
                var font = new Font("Segoe UI", 9F);
                var size = g.MeasureString(txt, font);
                var midH = pnlDivider.Height / 2;
                var midW = pnlDivider.Width / 2;
                
                using var brush = new SolidBrush(_colTextGray); // Text color
                using var pen = new Pen(Color.FromArgb(60, 255, 255, 255)); // Line color

                g.DrawString(txt, font, brush, midW - size.Width / 2, midH - size.Height / 2);
                g.DrawLine(pen, 0, midH, midW - size.Width / 2 - 10, midH);
                g.DrawLine(pen, midW + size.Width / 2 + 10, midH, pnlDivider.Width, midH);
            };
            _contentContainer.Controls.Add(pnlDivider);
            y += 35;

            // Email Input (Custom Control)
            var inputEmail = CreateLabeledInput("Email", "you@example.com", false);
            inputEmail.Location = new Point(0, y);
            _contentContainer.Controls.Add(inputEmail);
            y += 75;

            // Password Input
            var inputPass = CreateLabeledInput("Password", "", true);
            inputPass.Location = new Point(0, y);
            _contentContainer.Controls.Add(inputPass);
            y += 85;

            // Sign In Button
            var btnSign = new Button
            {
                Text = "Sign In",
                Font = new Font("Segoe UI Semibold", 12F),
                ForeColor = Color.White,
                BackColor = _colAccent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(_contentContainer.Width, 50),
                Location = new Point(0, y),
                Cursor = Cursors.Hand
            };
            btnSign.FlatAppearance.BorderSize = 0;
            btnSign.Region = GetRoundedRegion(btnSign.ClientRectangle, 8);
            btnSign.Click += (s, e) => MessageBox.Show("Please use Google Sign In for now.", "Beta");
            _contentContainer.Controls.Add(btnSign);
        }

        // ============ CUSTOM INPUT CONTROL ============
        // This simulates the "Label intersecting the border" look from the mockup
        private Panel CreateLabeledInput(string labelText, string placeholder, bool isPassword)
        {
            var pnl = new Panel { Size = new Size(_contentContainer.Width, 60), BackColor = Color.Transparent };

            // 1. The Border Container (Sits lower)
            var borderPnl = new Panel
            {
                Size = new Size(pnl.Width, 50),
                Location = new Point(0, 10), // Push down to make room for label overlap
                BackColor = Color.Transparent
            };

            // Custom Paint for Border and Background
            borderPnl.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, borderPnl.Width - 1, borderPnl.Height - 1);
                using var path = GetRoundedPath(r, 8);
                using var brush = new SolidBrush(_colInputSurface);
                using var pen = new Pen(_colInputBorder, 1);
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            };
            pnl.Controls.Add(borderPnl);

            // 2. The Label (Sits on top, creating the "cutout" effect)
            var lbl = new Label
            {
                Text = labelText,
                Font = new Font("Segoe UI", 9F),
                ForeColor = _colTextGray,
                BackColor = _colBackground, // IMPORTANT: Matches form background to "mask" the border
                AutoSize = true,
                Location = new Point(12, 0) // Overlaps the top border of borderPnl
            };
            // Ensure label is drawn after (on top of) the border panel logic
            // Note: In WinForms, z-ordering is tricky. We place this IN the main panel, not the border panel.
            pnl.Controls.Add(lbl);
            lbl.BringToFront();

            // 3. The TextBox
            var txt = new TextBox
            {
                Text = isPassword ? "" : placeholder,
                PlaceholderText = isPassword ? "" : placeholder, // For newer .NET
                BorderStyle = BorderStyle.None,
                BackColor = _colInputSurface,
                ForeColor = _colTextWhite,
                Font = new Font("Segoe UI", 11F),
                Location = new Point(15, 14),
                Width = borderPnl.Width - 60,
                UseSystemPasswordChar = isPassword
            };
            // Clear placeholder on focus for older .NET simulation
            if (!isPassword) {
                txt.Enter += (s, e) => { if (txt.Text == placeholder) { txt.Text = ""; txt.ForeColor = _colTextWhite; } };
                txt.Leave += (s, e) => { if (txt.Text == "") { txt.Text = placeholder; txt.ForeColor = Color.Gray; } else { txt.ForeColor = _colTextWhite; } };
                if (txt.Text == placeholder) txt.ForeColor = Color.Gray;
            }
            
            borderPnl.Controls.Add(txt);

            // 4. The Pink Icon (Three dots)
            var iconBox = new PictureBox
            {
                Size = new Size(24, 24),
                Location = new Point(borderPnl.Width - 36, 13),
                BackColor = Color.Transparent,
                Image = DrawPinkDots(24)
            };
            borderPnl.Controls.Add(iconBox);

            return pnl;
        }

        // ============ HEADER CONTROL ============
        private Panel CreateHeader()
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.Transparent, Padding = new Padding(20, 0, 20, 0) };

            // Logo Box
            var logo = new PictureBox
            {
                Size = new Size(36, 36),
                Location = new Point(20, 12),
                Image = DrawBrandLogo(36)
            };
            p.Controls.Add(logo);

            // Title
            var title = new Label
            {
                Text = "PatsKiller Pro",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = _colAccent,
                AutoSize = true,
                Location = new Point(65, 15)
            };
            p.Controls.Add(title);

            // Window Controls (Fake macOS style dots for the aesthetic)
            var pnlControls = new Panel { Size = new Size(60, 20), Location = new Point(p.Width - 80, 20), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            pnlControls.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var b1 = new SolidBrush(ColorTranslator.FromHtml("#334155")); // Greyed out for style
                e.Graphics.FillEllipse(b1, 0, 0, 12, 12);
                e.Graphics.FillEllipse(b1, 20, 0, 12, 12);
                
                // Active Close Button (Red-ish on hover logic could go here, keeping simple for now)
                using var bClose = new SolidBrush(ColorTranslator.FromHtml("#334155"));
                e.Graphics.FillEllipse(bClose, 40, 0, 12, 12);
            };
            
            // Actual Click Handler for Close
            var btnClose = new Label { Text = "", Size = new Size(60, 20), Location = pnlControls.Location, Anchor = AnchorStyles.Right | AnchorStyles.Top, Cursor = Cursors.Hand };
            btnClose.Click += (s, e) => Application.Exit();
            p.Controls.Add(pnlControls);
            p.Controls.Add(btnClose);
            btnClose.BringToFront();

            return p;
        }

        // ============ GRAPHICS HELPERS ============

        private Bitmap DrawBrandLogo(int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Pink Border Box
            using var pen = new Pen(_colAccent, 2);
            g.DrawRectangle(pen, 1, 1, size - 3, size - 3);

            // Key Icon
            using var brush = new SolidBrush(_colAccent);
            g.FillEllipse(brush, 6, 12, 10, 10); // Head
            g.FillRectangle(brush, 14, 15, 14, 4); // Shaft
            g.FillRectangle(brush, 22, 19, 4, 5); // Tooth
            return bmp;
        }

        private Bitmap DrawPinkDots(int size)
        {
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Pink Background
            using var brush = new SolidBrush(_colAccent);
            g.FillPath(brush, GetRoundedPath(new Rectangle(0, 0, size, size), 4));

            // 3 White Dots
            using var white = new SolidBrush(Color.White);
            int y = size / 2 - 2;
            g.FillEllipse(white, 4, y, 4, 4);
            g.FillEllipse(white, 10, y, 4, 4);
            g.FillEllipse(white, 16, y, 4, 4);

            return bmp;
        }

        private Bitmap DrawGoogleG(int size)
        {
            // Simple G logo approximation
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            var rect = new Rectangle(1, 1, size-2, size-2);
            using var penR = new Pen(Color.Red, 2);
            using var penB = new Pen(Color.Blue, 2);
            using var penG = new Pen(Color.Green, 2);
            using var penY = new Pen(Color.Yellow, 2);

            g.DrawArc(penR, rect, 180, 90);
            g.DrawArc(penG, rect, 90, 90);
            g.DrawArc(penY, rect, 45, 45); // Simplified
            g.DrawArc(penB, rect, 270, 90);
            return bmp;
        }

        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private Region GetRoundedRegion(Rectangle rect, int radius) => new Region(GetRoundedPath(rect, radius));

        // ============ AUTH LOGIC ============
        private async void StartGoogleAuth()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/functions/v1/create-desktop-auth-session");
                req.Headers.Add("apikey", API_KEY);
                req.Content = new StringContent(JsonSerializer.Serialize(new { machineId = Environment.MachineName }), Encoding.UTF8, "application/json");

                var res = await _http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    var code = json.RootElement.GetProperty("sessionCode").GetString();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = AUTH_URL + code, UseShellExecute = true });
                    PollSession(code, _cts.Token);
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
                    req.Headers.Add("apikey", API_KEY);
                    req.Content = new StringContent(JsonSerializer.Serialize(new { sessionCode = code, machineId = Environment.MachineName }), Encoding.UTF8, "application/json");
                    
                    var res = await _http.SendAsync(req);
                    if (!res.IsSuccessStatusCode) continue;
                    
                    var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
                    if (json.GetProperty("status").GetString() == "complete") {
                        MessageBox.Show("Success! Logged in as " + json.GetProperty("email").GetString());
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                        return;
                    }
                } catch { }
            }
        }
    }
}