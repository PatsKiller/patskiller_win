using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Forms
{
    /// <summary>
    /// Modal dialog for license key activation.
    /// Shows machine identity, accepts XXXX-XXXX-XXXX-XXXX keys,
    /// activates via LicenseService, reports result.
    ///
    /// DialogResult meanings:
    ///   OK     – license activated successfully
    ///   Retry  – user clicked "Sign in with Google instead"
    ///   Cancel – user dismissed
    ///
    /// Phase 2 deliverable per Licensing Integration Design Spec v1.0
    /// </summary>
    public class LicenseActivationForm : Form
    {
        // ══════════ Result ══════════
        public bool Activated { get; private set; }
        public LicenseValidationResult? ActivationResult { get; private set; }

        // ══════════ Dark Theme ══════════
        private static readonly Color BgColor = Color.FromArgb(30, 30, 30);
        private static readonly Color HeaderBg = Color.FromArgb(37, 37, 38);
        private static readonly Color PanelBg = Color.FromArgb(45, 45, 48);
        private static readonly Color InputBg = Color.FromArgb(60, 60, 60);
        private static readonly Color BorderClr = Color.FromArgb(70, 70, 70);
        private static readonly Color TextClr = Color.FromArgb(255, 255, 255);
        private static readonly Color TextDim = Color.FromArgb(150, 150, 150);
        private static readonly Color Red = Color.FromArgb(233, 69, 96);
        private static readonly Color Green = Color.FromArgb(76, 175, 80);
        private static readonly Color Blue = Color.FromArgb(59, 130, 246);
        private static readonly Color Amber = Color.FromArgb(245, 158, 11);

        // ══════════ Controls ══════════
        private Label _lblMachineName = null!;
        private Label _lblMachineId = null!;
        private Label _lblSiid = null!;

        private TextBox _txtKey1 = null!;
        private TextBox _txtKey2 = null!;
        private TextBox _txtKey3 = null!;
        private TextBox _txtKey4 = null!;

        private Button _btnActivate = null!;
        private Button _btnCancel = null!;
        private Button _btnUseSso = null!;

        private Panel _statusPanel = null!;
        private Label _lblStatus = null!;
        private ProgressBar _progress = null!;

        private Panel _resultPanel = null!;
        private Label _lblResultIcon = null!;
        private Label _lblResultMsg = null!;
        private Label _lblResultDetail = null!;

        private bool _activating;

        // ══════════ Constructor ══════════
        public LicenseActivationForm()
        {
            InitUI();
            PopulateMachineInfo();
        }

        // ================================================================
        //  UI SETUP
        // ================================================================
        private void InitUI()
        {
            Text = "Activate License — PatsKiller Pro";
            Size = new Size(530, 530);
            MinimumSize = Size;
            MaximumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = BgColor;
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            // Dark title bar (Windows 10/11)
            try { int a = 20, v = 1; DwmSetWindowAttribute(Handle, a, ref v, 4); } catch { }

            BuildHeader();
            BuildBody();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);

        private void BuildHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = HeaderBg
            };

            header.Controls.Add(new Label
            {
                Text = "🔑 License Activation",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = TextClr,
                AutoSize = true,
                Location = new Point(20, 12),
                BackColor = Color.Transparent
            });
            header.Controls.Add(new Label
            {
                Text = "Enter your license key to activate PatsKiller Pro",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextDim,
                AutoSize = true,
                Location = new Point(22, 38),
                BackColor = Color.Transparent
            });

            Controls.Add(header);
        }

        private void BuildBody()
        {
            var body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(25, 15, 25, 15),
                BackColor = BgColor
            };

            int y = 10;

            // ── Machine Info ──
            body.Controls.Add(SectionLabel("Machine Identity", ref y));
            y += 2;

            var machinePanel = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(465, 72),
                BackColor = PanelBg
            };

            _lblMachineName = new Label { Font = new Font("Segoe UI", 9F), ForeColor = TextClr, AutoSize = true, Location = new Point(12, 8) };
            _lblMachineId = new Label { Font = new Font("Consolas", 8.5F), ForeColor = TextDim, AutoSize = true, Location = new Point(12, 28) };
            _lblSiid = new Label { Font = new Font("Consolas", 8.5F), ForeColor = TextDim, AutoSize = true, Location = new Point(12, 48) };
            machinePanel.Controls.AddRange(new Control[] { _lblMachineName, _lblMachineId, _lblSiid });
            body.Controls.Add(machinePanel);
            y += 84;

            // ── Key Entry ──
            body.Controls.Add(SectionLabel("License Key", ref y));
            y += 2;

            var keyPanel = new Panel { Location = new Point(0, y), Size = new Size(465, 48), BackColor = Color.Transparent };
            BuildKeyFields(keyPanel);
            body.Controls.Add(keyPanel);
            y += 56;

            // ── Buttons ──
            _btnActivate = MakeButton("✅ Activate License", Green, new Size(215, 40));
            _btnActivate.Location = new Point(0, y);
            _btnActivate.Enabled = false;
            _btnActivate.Click += BtnActivate_Click;
            body.Controls.Add(_btnActivate);

            _btnCancel = MakeButton("Cancel", BorderClr, new Size(100, 40));
            _btnCancel.Location = new Point(225, y);
            _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            body.Controls.Add(_btnCancel);
            y += 52;

            // ── Status (during activation) ──
            _statusPanel = new Panel { Location = new Point(0, y), Size = new Size(465, 50), Visible = false };
            _progress = new ProgressBar { Location = new Point(0, 0), Size = new Size(465, 8), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
            _lblStatus = new Label { Text = "Activating...", Font = new Font("Segoe UI", 9F), ForeColor = Amber, AutoSize = true, Location = new Point(0, 15) };
            _statusPanel.Controls.AddRange(new Control[] { _progress, _lblStatus });
            body.Controls.Add(_statusPanel);

            // ── Result (after activation) ──
            _resultPanel = new Panel { Location = new Point(0, y), Size = new Size(465, 80), BackColor = PanelBg, Visible = false, Padding = new Padding(12) };
            _lblResultIcon = new Label { Text = "✅", Font = new Font("Segoe UI", 20F), AutoSize = true, Location = new Point(12, 12), BackColor = Color.Transparent };
            _lblResultMsg = new Label { Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Green, AutoSize = true, Location = new Point(55, 12), BackColor = Color.Transparent };
            _lblResultDetail = new Label { Font = new Font("Segoe UI", 9F), ForeColor = TextDim, AutoSize = true, Location = new Point(55, 38), BackColor = Color.Transparent };
            _resultPanel.Controls.AddRange(new Control[] { _lblResultIcon, _lblResultMsg, _lblResultDetail });
            body.Controls.Add(_resultPanel);

            // ── SSO fallback link ──
            y += 92;
            _btnUseSso = new Button
            {
                Text = "← Sign in with Google instead",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Blue,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Underline),
                AutoSize = true,
                Location = new Point(0, y),
                Cursor = Cursors.Hand
            };
            _btnUseSso.FlatAppearance.BorderSize = 0;
            _btnUseSso.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 45);
            _btnUseSso.Click += (_, _) => { DialogResult = DialogResult.Retry; Close(); };
            body.Controls.Add(_btnUseSso);

            Controls.Add(body);
        }

        // ================================================================
        //  KEY ENTRY FIELDS
        // ================================================================
        private void BuildKeyFields(Panel parent)
        {
            int x = 0;
            const int boxW = 95, dashW = 20;

            _txtKey1 = MakeKeyBox(); _txtKey1.Location = new Point(x, 5); parent.Controls.Add(_txtKey1); x += boxW;
            parent.Controls.Add(MakeDash(x, 10)); x += dashW;
            _txtKey2 = MakeKeyBox(); _txtKey2.Location = new Point(x, 5); parent.Controls.Add(_txtKey2); x += boxW;
            parent.Controls.Add(MakeDash(x, 10)); x += dashW;
            _txtKey3 = MakeKeyBox(); _txtKey3.Location = new Point(x, 5); parent.Controls.Add(_txtKey3); x += boxW;
            parent.Controls.Add(MakeDash(x, 10)); x += dashW;
            _txtKey4 = MakeKeyBox(); _txtKey4.Location = new Point(x, 5); parent.Controls.Add(_txtKey4);

            // Auto-tab forward
            WireAutoTab(_txtKey1, _txtKey2);
            WireAutoTab(_txtKey2, _txtKey3);
            WireAutoTab(_txtKey3, _txtKey4);

            // Backspace to previous
            WireBackTab(_txtKey2, _txtKey1);
            WireBackTab(_txtKey3, _txtKey2);
            WireBackTab(_txtKey4, _txtKey3);

            // Validate on change
            foreach (var tb in new[] { _txtKey1, _txtKey2, _txtKey3, _txtKey4 })
                tb.TextChanged += (_, _) => _btnActivate.Enabled = !_activating && FullKey.Length == 19;
        }

        private static TextBox MakeKeyBox() => new()
        {
            Size = new Size(95, 30),
            Font = new Font("Consolas", 14F, FontStyle.Bold),
            BackColor = InputBg,
            ForeColor = TextClr,
            BorderStyle = BorderStyle.FixedSingle,
            MaxLength = 4,
            CharacterCasing = CharacterCasing.Upper,
            TextAlign = HorizontalAlignment.Center
        };

        private static Label MakeDash(int x, int y) => new()
        {
            Text = "—",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = TextDim,
            AutoSize = true,
            Location = new Point(x, y)
        };

        private static void WireAutoTab(TextBox current, TextBox next)
        {
            current.TextChanged += (_, _) => { if (current.Text.Length == 4) next.Focus(); };
        }

        private static void WireBackTab(TextBox current, TextBox prev)
        {
            current.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Back && current.Text.Length == 0)
                {
                    prev.Focus();
                    prev.SelectionStart = prev.Text.Length;
                    e.SuppressKeyPress = true;
                }
            };
        }

        /// <summary>Full key as "XXXX-XXXX-XXXX-XXXX" or "" if incomplete.</summary>
        private string FullKey
        {
            get
            {
                var k1 = _txtKey1.Text.Trim(); var k2 = _txtKey2.Text.Trim();
                var k3 = _txtKey3.Text.Trim(); var k4 = _txtKey4.Text.Trim();
                return (k1.Length == 4 && k2.Length == 4 && k3.Length == 4 && k4.Length == 4)
                    ? $"{k1}-{k2}-{k3}-{k4}" : "";
            }
        }

        /// <summary>
        /// Support Ctrl+V of full key "XXXX-XXXX-XXXX-XXXX" or "XXXXXXXXXXXXXXXX"
        /// into the first box — auto-distributes across all four fields.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.V) && _txtKey1.Focused && Clipboard.ContainsText())
            {
                var clean = Clipboard.GetText().Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
                if (clean.Length == 16 && Regex.IsMatch(clean, "^[A-Z0-9]+$"))
                {
                    _txtKey1.Text = clean[..4];
                    _txtKey2.Text = clean[4..8];
                    _txtKey3.Text = clean[8..12];
                    _txtKey4.Text = clean[12..16];
                    _txtKey4.Focus();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ================================================================
        //  MACHINE INFO
        // ================================================================
        private void PopulateMachineInfo()
        {
            _lblMachineName.Text = $"Machine:     {MachineIdentity.MachineName}";
            _lblMachineId.Text   = $"Hardware ID: {MachineIdentity.MachineId}";
            _lblSiid.Text        = $"Instance ID: {MachineIdentity.SIID}";
        }

        // ================================================================
        //  ACTIVATION
        // ================================================================
        private async void BtnActivate_Click(object? sender, EventArgs e)
        {
            var key = FullKey;
            if (string.IsNullOrEmpty(key)) return;

            _activating = true;
            SetBusy(true);
            _lblStatus.Text = "Contacting license server...";
            _lblStatus.ForeColor = Amber;

            try
            {
                var result = await LicenseService.Instance.ActivateAsync(key);
                ActivationResult = result;

                if (result.IsValid)
                {
                    Activated = true;
                    ShowResult(true,
                        $"Licensed to {result.LicensedTo}",
                        $"Type: {(result.LicenseType ?? "standard").ToUpperInvariant()} " +
                        $"• Expires: {(result.ExpiresAt?.ToString("yyyy-MM-dd") ?? "never")} " +
                        $"• Machines: {result.MachinesUsed}/{result.MaxMachines}");

                    // Auto-close after brief success display
                    await Task.Delay(2000);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    ShowResult(false, "Activation Failed", result.Message);
                    _activating = false;
                    SetBusy(false);
                }
            }
            catch (Exception ex)
            {
                ShowResult(false, "Activation Error", ex.Message);
                _activating = false;
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _btnActivate.Enabled = !busy;
            _btnCancel.Enabled = !busy;
            foreach (var tb in new[] { _txtKey1, _txtKey2, _txtKey3, _txtKey4 })
                tb.Enabled = !busy;

            _statusPanel.Visible = busy;
            _resultPanel.Visible = false;
        }

        private void ShowResult(bool success, string message, string detail)
        {
            _statusPanel.Visible = false;
            _resultPanel.Visible = true;

            _lblResultIcon.Text = success ? "✅" : "❌";
            _lblResultMsg.Text = message;
            _lblResultMsg.ForeColor = success ? Green : Red;
            _lblResultDetail.Text = detail;
        }

        // ════════ Helpers ════════
        private static Label SectionLabel(string text, ref int y)
        {
            var lbl = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = TextDim,
                AutoSize = true,
                Location = new Point(0, y)
            };
            y += 20;
            return lbl;
        }

        private static Button MakeButton(string text, Color bg, Size size)
        {
            var btn = new Button
            {
                Text = text,
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = size,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = bg;
            return btn;
        }
    }
}
