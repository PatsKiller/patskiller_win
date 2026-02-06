using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PatsKillerPro
{
    public class GoogleLoginForm : Form
    {
        // IMPORTANT:
        // This form launches the system browser for Google/Supabase auth.
        // Embedded WebViews are unreliable for Google OAuth.

        // v2.12 recommended route:
        //   https://patskiller.com/api/desktop-auth?callback=...
        private const string AUTH_URL = "https://patskiller.com/api/desktop-auth";

        private readonly RoundedTextBox txtEmail;
        private readonly RoundedTextBox txtPassword;
        private readonly RoundedButton btnGoogle;
        private readonly RoundedButton btnSignIn;
        private readonly Label lblStatus;

        // Optional: desktop callback (custom scheme or localhost). If your app passes a callback,
        // set it before calling ShowDialog().
        public string? CallbackUrl { get; set; }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        public GoogleLoginForm()
        {
            Text = "PatsKiller Pro";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(420, 620);
            BackColor = Color.FromArgb(10, 15, 35);
            DoubleBuffered = true;

            // Drag anywhere on the form header region
            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
            };

            // Header
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(8, 12, 28)
            };

            var title = new Label
            {
                Text = "PatsKiller Pro",
                ForeColor = Color.HotPink,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(12, 14)
            };

            var btnClose = new Button
            {
                Text = "✕",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 28),
                Location = new Point(370, 10),
                TabStop = false
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (_, __) => Close();

            header.Controls.Add(title);
            header.Controls.Add(btnClose);
            Controls.Add(header);

            // Content
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 22, 28, 22)
            };
            Controls.Add(content);

            var lblWelcome = new Label
            {
                Text = "Welcome",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 22, FontStyle.Italic),
                AutoSize = true,
                Location = new Point(0, 10)
            };

            var lblSub = new Label
            {
                Text = "Sign in to access your account",
                ForeColor = Color.FromArgb(170, 180, 210),
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(0, 58)
            };

            btnGoogle = new RoundedButton
            {
                Text = "Continue with Google",
                Height = 44,
                Width = 360,
                Location = new Point(0, 92),
                BackColor = Color.White,
                ForeColor = Color.Black,
                Cursor = Cursors.Hand
            };

            // Make sure the click always fires even if user clicks label text area
            btnGoogle.Click += (_, __) => StartGoogleLoginFlow();
            btnGoogle.MouseUp += (_, __) => { /* extra safety */ };

            var lblOr = new Label
            {
                Text = "or sign in with email",
                ForeColor = Color.FromArgb(140, 150, 185),
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(0, 148)
            };

            txtEmail = new RoundedTextBox
            {
                Placeholder = "you@example.com",
                Location = new Point(0, 176),
                Width = 360
            };

            txtPassword = new RoundedTextBox
            {
                Placeholder = "Password",
                Location = new Point(0, 236),
                Width = 360,
                UseSystemPasswordChar = true
            };

            btnSignIn = new RoundedButton
            {
                Text = "Sign In",
                Height = 46,
                Width = 360,
                Location = new Point(0, 310),
                BackColor = Color.HotPink,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnSignIn.Click += (_, __) =>
            {
                // This desktop build uses browser auth; email/password sign-in is handled by the web flow.
                StartGoogleLoginFlow();
            };

            lblStatus = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(255, 120, 120),
                AutoSize = true,
                Location = new Point(0, 370)
            };

            var lnkActivate = new LinkLabel
            {
                Text = "Have a license key? Activate it here",
                LinkColor = Color.HotPink,
                ActiveLinkColor = Color.HotPink,
                VisitedLinkColor = Color.HotPink,
                AutoSize = true,
                Location = new Point(0, 410)
            };
            lnkActivate.Click += (_, __) =>
            {
                // Your app can open the license activation UI or a help page.
                // Keep it simple for now.
                MessageBox.Show("Open License Management to paste your key after signing in.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            content.Controls.Add(lblWelcome);
            content.Controls.Add(lblSub);
            content.Controls.Add(btnGoogle);
            content.Controls.Add(lblOr);
            content.Controls.Add(txtEmail);
            content.Controls.Add(txtPassword);
            content.Controls.Add(btnSignIn);
            content.Controls.Add(lblStatus);
            content.Controls.Add(lnkActivate);

            // Ensure button is clickable (z-order safety)
            btnGoogle.BringToFront();
        }

        private void StartGoogleLoginFlow()
        {
            try
            {
                var url = AUTH_URL;

                // Append callback if provided
                if (!string.IsNullOrWhiteSpace(CallbackUrl))
                {
                    var encoded = Uri.EscapeDataString(CallbackUrl);
                    url = url.Contains("?")
                        ? $"{url}&callback={encoded}"
                        : $"{url}?callback={encoded}";
                }

                OpenUrlInBrowser(url);

                lblStatus.Text = "Browser opened — complete sign-in, then return to the app.";
                lblStatus.ForeColor = Color.FromArgb(170, 180, 210);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Failed to start login: " + ex.Message;
                lblStatus.ForeColor = Color.FromArgb(255, 120, 120);
            }
        }

        private static void OpenUrlInBrowser(string url)
        {
            // UseShellExecute is required for default browser on modern Windows.
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        // Minimal UI paint polish (optional)
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Soft vignette effect
            using var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(18, 24, 48), Color.FromArgb(8, 10, 18), 90f);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }
}
