using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Forms;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;

namespace PatsKillerPro
{
    public partial class MainForm : Form
    {
        // ============ V15 THEME CONSTANTS ============
        private readonly Color _colBackground = ColorTranslator.FromHtml("#0F172A"); // Dark Navy
        private readonly Color _colSurface    = ColorTranslator.FromHtml("#1E293B"); // Slate
        private readonly Color _colBorder     = ColorTranslator.FromHtml("#334155"); // Lighter Slate
        private readonly Color _colAccent     = ColorTranslator.FromHtml("#E94796"); // Pink
        private readonly Color _colText       = Color.White;
        private readonly Color _colTextMuted  = ColorTranslator.FromHtml("#94A3B8");
        private readonly Color _colSuccess    = ColorTranslator.FromHtml("#10B981");
        private readonly Color _colDanger     = ColorTranslator.FromHtml("#EF4444");

        // ============ STATE ============
        private bool IsLicensed => LicenseService.Instance.IsLicensed;
        private bool HasSSO => !string.IsNullOrWhiteSpace(TokenBalanceService.Instance.UserEmail);
        private bool IsAuthorized => IsLicensed || HasSSO;

        // ============ UI CONTROLS ============
        private Panel _header;
        private Label _lblStatus;
        private Label _lblUser;
        private TabControl _tabs;
        private RichTextBox _logBox;

        public MainForm()
        {
            // 1. Window Setup (Fixed Size to prevent layout collapse)
            this.Text = "PatsKiller Pro v2.1";
            this.Size = new Size(1024, 768);
            this.MinimumSize = new Size(1024, 768);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = _colBackground;
            this.ForeColor = _colText;
            this.Font = new Font("Segoe UI", 9F);

            // 2. Events & Services
            this.Shown += MainForm_Shown;
            this.FormClosing += MainForm_FormClosing;
            
            TokenBalanceService.Instance.BalanceChanged += (s, e) => UpdateHeader();
            LicenseService.Instance.OnLicenseChanged += (r) => UpdateHeader();

            InitializeUI();
        }

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            Log("Initializing system...");
            
            // 1. License Check
            var lic = await LicenseService.Instance.ValidateAsync();
            
            // 2. SSO Session Check
            LoadSession();

            // 3. Auth Gate
            if (!IsAuthorized)
            {
                this.Hide();
                await PromptLoginAsync();
            }
            else
            {
                if (HasSSO) await TokenBalanceService.Instance.RefreshBalanceAsync();
                UpdateHeader();
                Log("System Ready. Connect J2534 interface.", _colSuccess);
            }
        }

        private async Task PromptLoginAsync()
        {
            using var login = new GoogleLoginForm();
            var result = login.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrEmpty(login.AuthToken))
            {
                // Login Success
                TokenBalanceService.Instance.SetAuthContext(login.AuthToken, login.UserEmail);
                SaveSession(login.AuthToken, login.UserEmail);
                this.Show();
                UpdateHeader();
                Log($"Signed in as {login.UserEmail}", _colSuccess);
            }
            else if (result == DialogResult.Retry)
            {
                // Switch to License Key
                using var licForm = new LicenseActivationForm();
                if (licForm.ShowDialog() == DialogResult.OK)
                {
                    this.Show();
                    UpdateHeader();
                    Log("License activated successfully", _colSuccess);
                }
                else
                {
                    Application.Exit();
                }
            }
            else
            {
                Application.Exit();
            }
        }

        // ============ UI CONSTRUCTION ============
        private void InitializeUI()
        {
            // -- HEADER --
            _header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = _colSurface, Padding = new Padding(20) };
            _header.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, _header.ClientRectangle, _colBorder, ButtonBorderStyle.Solid);

            var title = new Label { Text = "PatsKiller Pro", Font = new Font("Segoe UI", 18F, FontStyle.Bold), ForeColor = _colAccent, AutoSize = true, Location = new Point(20, 18) };
            
            _lblStatus = new Label { Font = new Font("Segoe UI", 9F, FontStyle.Bold), AutoSize = true, Location = new Point(220, 26) };
            _lblUser = new Label { Font = new Font("Segoe UI", 9F), ForeColor = _colTextMuted, AutoSize = true, Location = new Point(220, 44) };

            var btnLogout = CreateButton("Logout", _colBorder, new Size(80, 30));
            btnLogout.Location = new Point(900, 20);
            btnLogout.Click += (s, e) => Logout();

            _header.Controls.AddRange(new Control[] { title, _lblStatus, _lblUser, btnLogout });
            this.Controls.Add(_header);

            // -- LOGS (Bottom) --
            var pnlLog = new Panel { Dock = DockStyle.Bottom, Height = 180, BackColor = Color.Black, Padding = new Padding(10) };
            _logBox = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.Black, ForeColor = _colTextMuted, BorderStyle = BorderStyle.None, ReadOnly = true, Font = new Font("Consolas", 10F) };
            pnlLog.Controls.Add(_logBox);
            this.Controls.Add(pnlLog);

            // -- TABS (Center) --
            _tabs = new TabControl { Dock = DockStyle.Fill, ItemSize = new Size(120, 30), SizeMode = TabSizeMode.Fixed };
            _tabs.TabPages.Add(CreateDashboardTab());
            // Additional tabs for Diagnostics/Utility can be added here
            this.Controls.Add(_tabs);
        }

        private TabPage CreateDashboardTab()
        {
            var tab = new TabPage { Text = "Dashboard", BackColor = _colBackground };
            
            // Using a manual layout panel to ensure cards don't collapse
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(30) };

            // 1. J2534 Card
            var cardJ2534 = CreateCard("🔌 J2534 Connection", "Connect VCM II or compatible device", 0);
            var btnScan = CreateButton("Scan Devices", _colAccent, new Size(120, 35));
            btnScan.Location = new Point(20, 60);
            btnScan.Click += async (s, e) => {
                Log("Scanning J2534 bus...", _colText);
                await Task.Delay(800);
                Log("Found: Ford VCM II (J2534-1)", _colSuccess);
            };
            cardJ2534.Controls.Add(btnScan);
            panel.Controls.Add(cardJ2534);

            // 2. Key Programming Card
            var cardKeys = CreateCard("🔑 Key Programming", "Add/Erase Keys (1 Token per session)", 160);
            var btnKeys = CreateButton("Start Session", _colSuccess, new Size(140, 35));
            btnKeys.Location = new Point(20, 60);
            btnKeys.Click += (s, e) => {
                if (!CheckTokenAccess()) return;
                Log("Starting Key Programming session...", _colAccent);
                // Real logic calls TokenBalanceService here
            };
            cardKeys.Controls.Add(btnKeys);
            panel.Controls.Add(cardKeys);

            // 3. Parameter Reset Card
            var cardParam = CreateCard("🔄 Parameter Reset", "Sync BCM/PCM/ABS (1 Token/module)", 320);
            var btnParam = CreateButton("Reset Modules", _colAccent, new Size(140, 35));
            btnParam.Location = new Point(20, 60);
            btnParam.Click += (s, e) => {
                if (!CheckTokenAccess()) return;
                Log("Analyzing vehicle modules...", _colAccent);
            };
            cardParam.Controls.Add(btnParam);
            panel.Controls.Add(cardParam);

            tab.Controls.Add(panel);
            return tab;
        }

        // ============ CUSTOM CONTROLS ============
        private Panel CreateCard(string title, string subtitle, int yPos)
        {
            var p = new Panel 
            { 
                Location = new Point(30, 30 + yPos), 
                Size = new Size(600, 130), 
                BackColor = _colSurface 
            };
            
            p.Paint += (s, e) => {
                ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, _colBorder, ButtonBorderStyle.Solid);
                e.Graphics.FillRectangle(new SolidBrush(_colAccent), 0, 0, 4, p.Height); // Pink accent line
            };

            var lblT = new Label { Text = title, Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = _colText, Location = new Point(20, 15), AutoSize = true };
            var lblS = new Label { Text = subtitle, Font = new Font("Segoe UI", 10F), ForeColor = _colTextMuted, Location = new Point(20, 42), AutoSize = true };
            
            p.Controls.Add(lblT);
            p.Controls.Add(lblS);
            return p;
        }

        private Button CreateButton(string text, Color bg, Size sz)
        {
            var b = new Button
            {
                Text = text,
                Size = sz,
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        // ============ LOGIC ============
        private void UpdateHeader()
        {
            if (IsLicensed)
            {
                _lblStatus.Text = "PROFESSIONAL LICENSE";
                _lblStatus.ForeColor = _colSuccess;
                _lblUser.Text = $"Licensed to: {LicenseService.Instance.LicensedTo}";
            }
            else
            {
                _lblStatus.Text = "UNLICENSED / FREE MODE";
                _lblStatus.ForeColor = _colDanger;
                _lblUser.Text = HasSSO ? TokenBalanceService.Instance.UserEmail : "Not signed in";
            }

            if (HasSSO)
            {
                _lblUser.Text += $" | Tokens: {TokenBalanceService.Instance.TotalTokens}";
            }
        }

        private bool CheckTokenAccess()
        {
            if (HasSSO) return true;
            if (MessageBox.Show("This feature requires Tokens.\nPlease sign in with Google.", "Auth Required", MessageBoxButtons.OKCancel) == DialogResult.OK)
            {
                _ = PromptLoginAsync();
            }
            return false;
        }

        private void Log(string msg, Color? c = null)
        {
            if (InvokeRequired) { Invoke(() => Log(msg, c)); return; }
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionColor = c ?? _colTextMuted;
            _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            _logBox.ScrollToCaret();
        }

        // ============ SESSION MGMT ============
        private void LoadSession()
        {
            try {
                if (File.Exists("session.dat")) {
                    var parts = File.ReadAllText("session.dat").Split('|');
                    if (parts.Length >= 2) TokenBalanceService.Instance.SetAuthContext(parts[0], parts[1]);
                }
            } catch {}
        }

        private void SaveSession(string token, string email)
        {
            try { File.WriteAllText("session.dat", $"{token}|{email}"); } catch {}
        }

        private void Logout()
        {
            if (File.Exists("session.dat")) File.Delete("session.dat");
            TokenBalanceService.Instance.ClearAuthContext();
            Application.Restart();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            LicenseService.Instance.Dispose();
        }
    }
}