using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Forms
{
    /// <summary>
    /// License activation & management dialog.
    /// Hybrid auth rules:
    ///   - License OR SSO can unlock the app
    ///   - Token-consuming features still require Google SSO
    /// </summary>
    public sealed class LicenseActivationForm : Form
    {
        // Match MainForm V15 dark theme
        private readonly Color BG = Color.FromArgb(26, 26, 30);
        private readonly Color SURFACE = Color.FromArgb(35, 35, 40);
        private readonly Color CARD = Color.FromArgb(42, 42, 48);
        private readonly Color BORDER = Color.FromArgb(58, 58, 66);
        private readonly Color TEXT = Color.FromArgb(240, 240, 240);
        private readonly Color TEXT_DIM = Color.FromArgb(160, 160, 165);
        private readonly Color TEXT_MUTED = Color.FromArgb(112, 112, 117);
        private readonly Color ACCENT = Color.FromArgb(59, 130, 246);
        private readonly Color SUCCESS = Color.FromArgb(34, 197, 94);
        private readonly Color WARNING = Color.FromArgb(234, 179, 8);
        private readonly Color DANGER = Color.FromArgb(239, 68, 68);

        private readonly ToolTip _tip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 400, ReshowDelay = 200, ShowAlways = true };

        private Label _lblState = null!;
        private Label _lblDetails = null!;
        private TextBox[] _keyBoxes = null!;
        private Button _btnActivate = null!;
        private Button _btnRevalidate = null!;
        private Button _btnDeactivate = null!;

        public LicenseActivationForm()
        {
            Text = "License";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = BG;
            ForeColor = TEXT;
            Font = new Font("Segoe UI", 10F);
            ClientSize = new Size(640, 520);
            AutoScaleMode = AutoScaleMode.None;

            BuildUI();

            Shown += async (_, __) => await RefreshStatusAsync();
        }

        private void BuildUI()
        {
            var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
            Controls.Add(root);

            var contentWidth = ClientSize.Width - (root.Padding.Left + root.Padding.Right);

            var title = new Label
            {
                Text = "License Management",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = TEXT,
                AutoSize = true,
                Location = new Point(0, 0)
            };
            root.Controls.Add(title);

            _lblState = new Label
            {
                Text = "Checking license…",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = TEXT_DIM,
                AutoSize = true,
                Location = new Point(0, 38)
            };
            root.Controls.Add(_lblState);

            // Machine identity card
            var cardMachine = MakeCard("Machine Binding");
            cardMachine.Location = new Point(0, 70);
            cardMachine.Size = new Size(contentWidth, 140);
            cardMachine.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(cardMachine);

            var machineLines = new[]
            {
                ("Machine Name", MachineIdentity.MachineName),
                ("Machine ID", MachineIdentity.MachineId),
                ("Instance ID (SIID)", MachineIdentity.SIID),
                ("Combined ID", MachineIdentity.CombinedId),
            };
            var y = 32;
            foreach (var (k, v) in machineLines)
            {
                var lblK = new Label { Text = k + ":", ForeColor = TEXT_MUTED, AutoSize = true, Location = new Point(14, y) };
                var txtV = new TextBox
                {
                    Text = v,
                    ReadOnly = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = SURFACE,
                    ForeColor = TEXT,
                    Font = new Font("Consolas", 10),
                    Location = new Point(150, y - 4),
                    Width = 360,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                var btnCopy = new Button
                {
                    Text = "Copy",
                    BackColor = CARD,
                    ForeColor = TEXT,
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(cardMachine.Width - 92, y - 6),
                    Size = new Size(72, 26),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                };
                btnCopy.FlatAppearance.BorderColor = BORDER;
                btnCopy.FlatAppearance.BorderSize = 1;
                btnCopy.Click += (_, __) => { try { Clipboard.SetText(v); } catch { } };
                _tip.SetToolTip(btnCopy, "Copy to clipboard");

                cardMachine.Controls.Add(lblK);
                cardMachine.Controls.Add(txtV);
                cardMachine.Controls.Add(btnCopy);
                y += 28;
            }

            // License details card
            var cardDetails = MakeCard("License Status");
            cardDetails.Location = new Point(0, 220);
            cardDetails.Size = new Size(contentWidth, 110);
            cardDetails.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(cardDetails);

            _lblDetails = new Label
            {
                Text = "—",
                ForeColor = TEXT_DIM,
                AutoSize = false,
                Location = new Point(14, 32),
                Size = new Size(cardDetails.Width - 28, 70),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            cardDetails.Controls.Add(_lblDetails);

            // Activation card
            var cardActivate = MakeCard("Activate / Replace Key");
            cardActivate.Location = new Point(0, 340);
            cardActivate.Size = new Size(contentWidth, 118);
            cardActivate.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(cardActivate);

            _keyBoxes = new TextBox[4];
            var x0 = 14;
            for (var i = 0; i < 4; i++)
            {
                var tb = new TextBox
                {
                    Width = 92,
                    Location = new Point(x0 + i * 110, 42),
                    BackColor = SURFACE,
                    ForeColor = TEXT,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 14, FontStyle.Bold),
                    MaxLength = 4,
                    TextAlign = HorizontalAlignment.Center,
                    CharacterCasing = CharacterCasing.Upper
                };
                var idx = i;
                tb.TextChanged += (_, __) =>
                {
                    if (tb.Text.Length == 4 && idx < 3)
                        _keyBoxes[idx + 1].Focus();
                };
                cardActivate.Controls.Add(tb);
                _keyBoxes[i] = tb;

                if (i < 3)
                {
                    var dash = new Label
                    {
                        Text = "-",
                        ForeColor = TEXT_MUTED,
                        Font = new Font("Segoe UI", 14, FontStyle.Bold),
                        AutoSize = true,
                        Location = new Point(tb.Right + 6, 44)
                    };
                    cardActivate.Controls.Add(dash);
                }
            }

            _btnActivate = MakeBtn("Activate", ACCENT);
            _btnActivate.Location = new Point(cardActivate.Width - 140, 40);
            _btnActivate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnActivate.Click += async (_, __) => await ActivateAsync();
            cardActivate.Controls.Add(_btnActivate);

            // Footer buttons
            _btnRevalidate = MakeBtn("Revalidate", CARD);
            _btnRevalidate.Location = new Point(0, 0);
            _btnRevalidate.Click += async (_, __) => await RevalidateAsync();

            _btnDeactivate = MakeBtn("Deactivate", CARD);
            _btnDeactivate.Location = new Point(0, 0);
            _btnDeactivate.Click += async (_, __) => await DeactivateAsync();

            var btnClose = MakeBtn("Close", CARD);
            btnClose.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

            var lnkSso = new Label
            {
                Text = "Need tokens? Sign in with Google",
                ForeColor = TEXT_MUTED,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Location = new Point(0, 0)
            };
            lnkSso.Click += (_, __) => { DialogResult = DialogResult.Retry; Close(); };
            _tip.SetToolTip(lnkSso, "Token-consuming actions require Google SSO");

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                BackColor = Color.Transparent
            };
            root.Controls.Add(footer);

            // Layout footer left → right
            _btnRevalidate.Parent = footer;
            _btnDeactivate.Parent = footer;
            btnClose.Parent = footer;
            lnkSso.Parent = footer;

            _btnRevalidate.Location = new Point(0, 8);
            _btnDeactivate.Location = new Point(_btnRevalidate.Right + 10, 8);
            lnkSso.Location = new Point(_btnDeactivate.Right + 14, 14);
            btnClose.Location = new Point(footer.Width - 110, 8);
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Top;

            footer.Resize += (_, __) =>
            {
                btnClose.Location = new Point(footer.Width - btnClose.Width, 8);
            };
        }

        private Panel MakeCard(string header)
        {
            var p = new Panel { BackColor = CARD };
            p.Paint += (_, e) =>
            {
                using var pen = new Pen(BORDER);
                e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };

            var lbl = new Label
            {
                Text = header.ToUpperInvariant(),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = TEXT_DIM,
                AutoSize = true,
                Location = new Point(14, 10)
            };
            p.Controls.Add(lbl);
            return p;
        }

        private Button MakeBtn(string text, Color bg)
        {
            var b = new Button
            {
                Text = text,
                BackColor = bg,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(110, 28),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = BORDER;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        private string ComposeKey()
        {
            var parts = _keyBoxes.Select(x => (x.Text ?? "").Trim()).ToArray();
            if (parts.Any(p => p.Length != 4)) return "";
            return string.Join("-", parts).ToUpperInvariant();
        }

        private async Task RefreshStatusAsync()
        {
            // Ensure cache is loaded for display.
            try { await LicenseService.Instance.ValidateAsync(); } catch { /* best effort */ }

            var ls = LicenseService.Instance;
            var licensed = ls.IsLicensed;
            var grace = ls.InGracePeriod;

            if (licensed)
            {
                _lblState.ForeColor = grace ? WARNING : SUCCESS;
                _lblState.Text = grace
                    ? $"ACTIVE (offline grace: {ls.GraceDaysRemaining} days)"
                    : "ACTIVE";

                var exp = ls.ExpiresAt?.ToString("yyyy-MM-dd") ?? "—";
                var last = ls.LastValidatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
                var who = string.IsNullOrWhiteSpace(ls.LicensedTo) ? "—" : ls.LicensedTo;
                var typ = string.IsNullOrWhiteSpace(ls.LicenseType) ? "standard" : ls.LicenseType;
                var slots = (ls.MaxMachines > 0) ? $"{ls.MachinesUsed}/{ls.MaxMachines}" : "—";

                _lblDetails.Text =
                    $"Licensed To: {who}\n" +
                    $"Type: {typ}    Expires: {exp}\n" +
                    $"Machines: {slots}    Last Check: {last}";

                _btnDeactivate.Enabled = true;
                _btnRevalidate.Enabled = true;
                _btnActivate.Text = "Replace";
            }
            else
            {
                _lblState.ForeColor = TEXT_DIM;
                _lblState.Text = "NOT ACTIVATED";
                _lblDetails.Text = "No valid license found on this machine.\nEnter a license key below to activate offline mode.";

                _btnDeactivate.Enabled = false;
                _btnRevalidate.Enabled = true;
                _btnActivate.Text = "Activate";
            }
        }

        private async Task ActivateAsync()
        {
            var key = ComposeKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Enter a full key in the format XXXX-XXXX-XXXX-XXXX.", "License", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetBusy(true);
            try
            {
                var res = await LicenseService.Instance.ActivateAsync(key);
                if (res.IsValid)
                {
                    await RefreshStatusAsync();
                    MessageBox.Show("License activated on this machine.", "License", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK; // signal state change
                }
                else
                {
                    MessageBox.Show(res.Message, "License", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task RevalidateAsync()
        {
            SetBusy(true);
            try
            {
                var res = await LicenseService.Instance.ValidateAsync();
                await RefreshStatusAsync();
                MessageBox.Show(res.Message, "License", MessageBoxButtons.OK,
                    res.IsValid ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                DialogResult = DialogResult.OK;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task DeactivateAsync()
        {
            if (!LicenseService.Instance.IsLicensed)
                return;

            var ok = MessageBox.Show(
                "Deactivate this license on this machine?\n\nThis frees up a machine slot, but offline access will be removed until you activate again.",
                "Confirm Deactivation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;

            if (!ok) return;

            SetBusy(true);
            try
            {
                await LicenseService.Instance.DeactivateAsync();
                await RefreshStatusAsync();
                MessageBox.Show("License deactivated from this machine.", "License", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnActivate.Enabled = !busy;
            _btnRevalidate.Enabled = !busy;
            _btnDeactivate.Enabled = !busy && LicenseService.Instance.IsLicensed;
            foreach (var tb in _keyBoxes) tb.Enabled = !busy;
        }
    }
}
