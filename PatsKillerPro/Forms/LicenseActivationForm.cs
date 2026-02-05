using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Forms
{
    /// <summary>
    /// Professional license activation form matching the v15 dark theme.
    /// Four-segment key entry (XXXX-XXXX-XXXX-XXXX) with auto-advance,
    /// machine identity display, and link to switch to Google SSO.
    /// </summary>
    public class LicenseActivationForm : Form
    {
        // ═══════════════ THEME (matches MainForm v15 palette) ═══════════
        private static readonly Color BG       = Color.FromArgb(26, 26, 30);
        private static readonly Color SURFACE  = Color.FromArgb(35, 35, 40);
        private static readonly Color CARD     = Color.FromArgb(42, 42, 48);
        private static readonly Color BORDER   = Color.FromArgb(58, 58, 66);
        private static readonly Color TEXT     = Color.FromArgb(240, 240, 240);
        private static readonly Color TEXT_DIM = Color.FromArgb(160, 160, 165);
        private static readonly Color TEXT_MUTED = Color.FromArgb(112, 112, 117);
        private static readonly Color ACCENT   = Color.FromArgb(59, 130, 246);
        private static readonly Color SUCCESS  = Color.FromArgb(34, 197, 94);
        private static readonly Color DANGER   = Color.FromArgb(239, 68, 68);
        private static readonly Color WARNING  = Color.FromArgb(234, 179, 8);

        // ═══════════════ PUBLIC RESULT ══════════════════════════════════
        /// <summary>True if user chose "Sign in with Google instead".</summary>
        public bool SwitchToGoogle { get; private set; }

        // ═══════════════ CONTROLS ══════════════════════════════════════
        private readonly TextBox[] _keyBoxes = new TextBox[4];
        private Label _lblStatus = null!;
        private Button _btnActivate = null!;
        private Button _btnCancel = null!;
        private LinkLabel _lnkGoogle = null!;
        private Panel _card = null!;

        // ═══════════════ CONSTRUCTOR ═══════════════════════════════════
        public LicenseActivationForm()
        {
            SetupForm();
            BuildUI();
            ApplyDarkTitleBar();
        }

        // ═══════════════ FORM SETUP ═══════════════════════════════════
        private void SetupForm()
        {
            Text = "Activate License — PatsKiller Pro";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = BG;
            ForeColor = TEXT;
            Font = new Font("Segoe UI", 10F);
            ClientSize = new Size(520, 520);
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
        }

        private void ApplyDarkTitleBar()
        {
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        // ═══════════════ UI CONSTRUCTION ═══════════════════════════════
        private void BuildUI()
        {
            // Card panel (centered with margin)
            _card = new Panel
            {
                Size = new Size(480, 480),
                Location = new Point(20, 20),
                BackColor = CARD
            };
            _card.Paint += (s, e) =>
            {
                using var pen = new Pen(BORDER, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, _card.Width - 1, _card.Height - 1);
            };
            Controls.Add(_card);

            int y = 28;
            int cardW = _card.Width;
            int contentW = cardW - 60;
            int padL = 30;

            // ── Icon + Title ──────────────────────────────────────────
            var iconPanel = new Panel
            {
                Size = new Size(44, 44),
                Location = new Point(padL, y),
                BackColor = Color.Transparent
            };
            iconPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(SUCCESS, 2.5f);
                var r = new Rectangle(4, 4, 36, 36);
                e.Graphics.DrawEllipse(pen, r);
                // Key icon (simplified)
                using var brush = new SolidBrush(SUCCESS);
                e.Graphics.FillEllipse(brush, 14, 12, 10, 10);
                e.Graphics.FillRectangle(brush, 20, 20, 4, 14);
                e.Graphics.FillRectangle(brush, 20, 28, 8, 3);
                e.Graphics.FillRectangle(brush, 20, 32, 6, 3);
            };
            _card.Controls.Add(iconPanel);

            var lblTitle = new Label
            {
                Text = "License Activation",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = TEXT,
                AutoSize = true,
                Location = new Point(padL + 52, y + 6)
            };
            _card.Controls.Add(lblTitle);
            y += 60;

            var lblSub = new Label
            {
                Text = "Enter your license key to activate PatsKiller Pro",
                Font = new Font("Segoe UI", 10),
                ForeColor = TEXT_MUTED,
                Size = new Size(contentW, 22),
                Location = new Point(padL, y)
            };
            _card.Controls.Add(lblSub);
            y += 38;

            // ── Machine Identity (card-style) ─────────────────────────
            var machineCard = new Panel
            {
                Size = new Size(contentW, 72),
                Location = new Point(padL, y),
                BackColor = SURFACE
            };
            machineCard.Paint += (s, e) =>
            {
                using var pen = new Pen(BORDER, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, machineCard.Width - 1, machineCard.Height - 1);
            };

            var lblMachineTitle = new Label
            {
                Text = "Machine Identity",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = TEXT_DIM,
                Location = new Point(14, 8),
                AutoSize = true
            };
            machineCard.Controls.Add(lblMachineTitle);

            var lblHwId = new Label
            {
                Text = $"Hardware ID:   {MachineIdentity.DisplayId}",
                Font = new Font("Consolas", 9.5f),
                ForeColor = TEXT_DIM,
                Location = new Point(14, 30),
                AutoSize = true
            };
            machineCard.Controls.Add(lblHwId);

            var lblSiid = new Label
            {
                Text = $"Instance ID:     {MachineIdentity.DisplaySiid}",
                Font = new Font("Consolas", 9.5f),
                ForeColor = TEXT_DIM,
                Location = new Point(14, 50),
                AutoSize = true
            };
            machineCard.Controls.Add(lblSiid);

            _card.Controls.Add(machineCard);
            y += 88;

            // ── License Key Label ─────────────────────────────────────
            var lblKey = new Label
            {
                Text = "License Key",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = TEXT_DIM,
                Location = new Point(padL, y),
                AutoSize = true
            };
            _card.Controls.Add(lblKey);
            y += 28;

            // ── Four-segment key input ────────────────────────────────
            int boxW = 92;
            int gap = 12;
            int dashW = 16;
            int totalW = (boxW * 4) + (dashW * 3) + (gap * 6);
            int startX = padL + (contentW - totalW) / 2;

            var keyPanel = new FlowLayoutPanel
            {
                Size = new Size(contentW, 52),
                Location = new Point(padL, y),
                BackColor = Color.Transparent,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            for (int i = 0; i < 4; i++)
            {
                _keyBoxes[i] = new TextBox
                {
                    Size = new Size(boxW, 42),
                    MaxLength = 4,
                    TextAlign = HorizontalAlignment.Center,
                    Font = new Font("Consolas", 16, FontStyle.Bold),
                    BackColor = SURFACE,
                    ForeColor = TEXT,
                    BorderStyle = BorderStyle.FixedSingle,
                    CharacterCasing = CharacterCasing.Upper,
                    Margin = new Padding(0, 0, 0, 0)
                };

                // Auto-advance on 4 chars
                int idx = i;
                _keyBoxes[i].TextChanged += (s, e) =>
                {
                    if (_keyBoxes[idx].Text.Length == 4 && idx < 3)
                        _keyBoxes[idx + 1].Focus();

                    // Update status when all boxes have 4 chars
                    UpdateActivateState();
                };

                // Handle paste: split across boxes
                _keyBoxes[i].KeyDown += (s, e) =>
                {
                    if (e.Control && e.KeyCode == Keys.V)
                    {
                        e.SuppressKeyPress = true;
                        PasteKey();
                    }
                    // Handle backspace to jump to previous box
                    if (e.KeyCode == Keys.Back && _keyBoxes[idx].Text.Length == 0 && idx > 0)
                    {
                        _keyBoxes[idx - 1].Focus();
                        _keyBoxes[idx - 1].SelectionStart = _keyBoxes[idx - 1].Text.Length;
                    }
                };

                keyPanel.Controls.Add(_keyBoxes[i]);

                if (i < 3)
                {
                    var dash = new Label
                    {
                        Text = "–",
                        Font = new Font("Segoe UI", 18, FontStyle.Bold),
                        ForeColor = TEXT_MUTED,
                        Size = new Size(dashW + 4, 42),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Margin = new Padding(2, 0, 2, 0)
                    };
                    keyPanel.Controls.Add(dash);
                }
            }

            _card.Controls.Add(keyPanel);
            y += 62;

            // ── Status label ──────────────────────────────────────────
            _lblStatus = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = TEXT_MUTED,
                Size = new Size(contentW, 20),
                Location = new Point(padL, y),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _card.Controls.Add(_lblStatus);
            y += 30;

            // ── Activate button ───────────────────────────────────────
            _btnActivate = new Button
            {
                Text = "✓  Activate License",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                Size = new Size(contentW, 50),
                Location = new Point(padL, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = SUCCESS,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnActivate.FlatAppearance.BorderSize = 0;
            _btnActivate.Click += async (s, e) => await DoActivate();
            _card.Controls.Add(_btnActivate);
            y += 62;

            // ── Cancel button ─────────────────────────────────────────
            _btnCancel = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Size = new Size(contentW, 42),
                Location = new Point(padL, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = SURFACE,
                ForeColor = TEXT_DIM,
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = BORDER;
            _btnCancel.FlatAppearance.BorderSize = 1;
            _btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            _card.Controls.Add(_btnCancel);
            y += 54;

            // ── Google SSO link ───────────────────────────────────────
            _lnkGoogle = new LinkLabel
            {
                Text = "← Sign in with Google instead",
                Font = new Font("Segoe UI", 10),
                LinkColor = ACCENT,
                ActiveLinkColor = Color.FromArgb(100, 160, 255),
                VisitedLinkColor = ACCENT,
                Size = new Size(contentW, 24),
                Location = new Point(padL, y),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _lnkGoogle.LinkClicked += (s, e) =>
            {
                SwitchToGoogle = true;
                DialogResult = DialogResult.Retry; // Signal: switch to Google
                Close();
            };
            _card.Controls.Add(_lnkGoogle);

            // Focus first key box on load
            Shown += (s, e) => _keyBoxes[0].Focus();
        }

        // ═══════════════ KEY INPUT HELPERS ═════════════════════════════

        private void PasteKey()
        {
            try
            {
                var clip = Clipboard.GetText().Trim().ToUpperInvariant();
                // Remove common separators
                clip = clip.Replace("-", "").Replace(" ", "").Replace("_", "");

                if (clip.Length >= 16)
                {
                    for (int i = 0; i < 4; i++)
                        _keyBoxes[i].Text = clip.Substring(i * 4, 4);
                    _keyBoxes[3].Focus();
                }
                else if (clip.Length > 0)
                {
                    _keyBoxes[0].Text = clip.Length >= 4 ? clip[..4] : clip;
                }
            }
            catch { }
        }

        private string GetFullKey()
        {
            return $"{_keyBoxes[0].Text}-{_keyBoxes[1].Text}-{_keyBoxes[2].Text}-{_keyBoxes[3].Text}";
        }

        private bool IsKeyComplete()
        {
            return _keyBoxes[0].Text.Length == 4 &&
                   _keyBoxes[1].Text.Length == 4 &&
                   _keyBoxes[2].Text.Length == 4 &&
                   _keyBoxes[3].Text.Length == 4;
        }

        private void UpdateActivateState()
        {
            _btnActivate.Enabled = IsKeyComplete();
            _btnActivate.BackColor = IsKeyComplete() ? SUCCESS : Color.FromArgb(50, 50, 56);
        }

        // ═══════════════ ACTIVATION LOGIC ═════════════════════════════

        private async Task DoActivate()
        {
            if (!IsKeyComplete()) return;

            var key = GetFullKey();
            _btnActivate.Enabled = false;
            _btnActivate.Text = "Activating…";
            _btnActivate.BackColor = Color.FromArgb(50, 50, 56);
            _lblStatus.Text = "Contacting license server…";
            _lblStatus.ForeColor = ACCENT;
            SetKeyBoxesEnabled(false);

            try
            {
                var result = await LicenseService.Instance.ActivateAsync(key);

                if (result.IsValid)
                {
                    _lblStatus.Text = $"✓ {result.Message}";
                    _lblStatus.ForeColor = SUCCESS;
                    _btnActivate.Text = "✓ Activated!";
                    _btnActivate.BackColor = SUCCESS;

                    await Task.Delay(800);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    _lblStatus.Text = result.Message;
                    _lblStatus.ForeColor = DANGER;
                    _btnActivate.Text = "✓  Activate License";
                    _btnActivate.Enabled = true;
                    _btnActivate.BackColor = SUCCESS;
                    SetKeyBoxesEnabled(true);
                    _keyBoxes[0].Focus();
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Error: {ex.Message}";
                _lblStatus.ForeColor = DANGER;
                _btnActivate.Text = "✓  Activate License";
                _btnActivate.Enabled = true;
                _btnActivate.BackColor = SUCCESS;
                SetKeyBoxesEnabled(true);
            }
        }

        private void SetKeyBoxesEnabled(bool enabled)
        {
            foreach (var box in _keyBoxes)
            {
                box.Enabled = enabled;
                box.BackColor = enabled ? SURFACE : Color.FromArgb(30, 30, 34);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
