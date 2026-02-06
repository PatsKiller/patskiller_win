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
    /// License Management (Phase 3).
    /// Strict policy: License activation/validation requires Google SSO identity (email + Bearer token),
    /// and token operations require BOTH SSO + valid license.
    /// </summary>
    public class LicenseActivationForm : Form
    {
        // V15 Navy theme
        private static readonly Color BgColor  = ColorTranslator.FromHtml("#0F172A");
        private static readonly Color Surface  = ColorTranslator.FromHtml("#1E293B");
        private static readonly Color Border   = ColorTranslator.FromHtml("#334155");
        private static readonly Color Accent   = ColorTranslator.FromHtml("#EC4899");
        private static readonly Color Success  = ColorTranslator.FromHtml("#22C55E");
        private static readonly Color Warning  = ColorTranslator.FromHtml("#EAB308");
        private static readonly Color Danger   = ColorTranslator.FromHtml("#EF4444");
        private static readonly Color TextMain = Color.White;
        private static readonly Color TextDim  = ColorTranslator.FromHtml("#94A3B8");

        private readonly TextBox[] _keyParts = new TextBox[4];
        private Label _lblSignedIn = null!;
        private Label _lblLicenseLine = null!;
        private Label _lblLicenseDetail = null!;
        private Button _btnActivate = null!;
        private Button _btnRevalidate = null!;
        private Button _btnDeactivate = null!;
        private RichTextBox _log = null!;
        private LinkLabel _lnkSignIn = null!;

        public LicenseActivationForm()
        {
            Text = "License Management";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = BgColor;
            ForeColor = TextMain;
            Font = new Font("Segoe UI", 10F);
            ClientSize = new Size(820, 620);

            BuildUI();
            Shown += async (_, __) => await RefreshUiAsync();
        }

        private void BuildUI()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BgColor,
                Padding = new Padding(22),
                ColumnCount = 1,
                RowCount = 6,
                AutoSize = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // title
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // identity + status
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // machine
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // key entry
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // log
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons

            // Title
            var title = new Label
            {
                Text = "License Management",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = Accent,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            var subtitle = new Label
            {
                Text = "Licenses are bound to this computer and the signed-in email (SSO).",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextDim,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14)
            };
            root.Controls.Add(title, 0, 0);
            root.Controls.Add(subtitle, 0, 0);
            root.SetCellPosition(subtitle, new TableLayoutPanelCellPosition(0, 0));
            subtitle.Location = new Point(subtitle.Location.X, subtitle.Location.Y + 40);

            // Identity / Status card
            var identityCard = CardPanel();
            var identityLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(14)
            };
            identityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            identityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            identityLayout.Controls.Add(MakeKey("Signed-in Email:"), 0, 0);
            _lblSignedIn = MakeVal("--");
            identityLayout.Controls.Add(_lblSignedIn, 1, 0);

            identityLayout.Controls.Add(MakeKey("License Status:"), 0, 1);
            _lblLicenseLine = MakeVal("Checking...");
            _lblLicenseLine.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            identityLayout.Controls.Add(_lblLicenseLine, 1, 1);

            _lblLicenseDetail = new Label
            {
                AutoSize = true,
                ForeColor = TextDim,
                Font = new Font("Segoe UI", 9F),
                Margin = new Padding(0, 8, 0, 0)
            };
            identityLayout.Controls.Add(_lblLicenseDetail, 1, 2);

            _lnkSignIn = new LinkLabel
            {
                Text = "Sign in with Google",
                LinkColor = Accent,
                ActiveLinkColor = Accent,
                VisitedLinkColor = Accent,
                AutoSize = true,
                Margin = new Padding(0, 8, 12, 0),
                Visible = false
            };
            _lnkSignIn.Click += (_, __) =>
            {
                DialogResult = DialogResult.Retry;
                Close();
            };
            identityLayout.Controls.Add(_lnkSignIn, 0, 2);

            identityCard.Controls.Add(identityLayout);
            root.Controls.Add(identityCard, 0, 1);

            // Machine binding card
            var machineCard = CardPanel();
            var m = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ColumnCount = 3,
                RowCount = 5,
                Padding = new Padding(14)
            };
            m.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            m.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            m.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var hdr = new Label
            {
                Text = "Machine Binding",
                ForeColor = TextMain,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            m.Controls.Add(hdr, 0, 0);
            m.SetColumnSpan(hdr, 3);

            AddCopyRow(m, 1, "Machine Name", Environment.MachineName);
            AddCopyRow(m, 2, "HW Machine ID", MachineIdentity.MachineId);
            AddCopyRow(m, 3, "SIID", MachineIdentity.SIID);
            AddCopyRow(m, 4, "Combined ID", MachineIdentity.CombinedId);

            machineCard.Controls.Add(m);
            root.Controls.Add(machineCard, 0, 2);

            // Key entry card
            var keyCard = CardPanel();
            var k = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(14)
            };

            var keyHdr = new Label
            {
                Text = "Activate / Replace License Key",
                ForeColor = TextMain,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            var keyHint = new Label
            {
                Text = "Enter the 16-character key from your email (format: XXXX-XXXX-XXXX-XXXX).",
                ForeColor = TextDim,
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            k.Controls.Add(keyHdr, 0, 0);
            k.Controls.Add(keyHint, 0, 1);

            var keyRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            for (var i = 0; i < 4; i++)
            {
                _keyParts[i] = new TextBox
                {
                    Width = 92,
                    Height = 34,
                    BackColor = BgColor,
                    ForeColor = TextMain,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 16F, FontStyle.Bold),
                    MaxLength = 4,
                    TextAlign = HorizontalAlignment.Center,
                    CharacterCasing = CharacterCasing.Upper,
                    Margin = new Padding(i == 0 ? 0 : 10, 0, 0, 0)
                };

                var idx = i;
                _keyParts[i].TextChanged += (_, __) =>
                {
                    if (_keyParts[idx].Text.Length == 4 && idx < 3)
                        _keyParts[idx + 1].Focus();
                };

                _keyParts[i].KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Back && _keyParts[idx].SelectionStart == 0 && idx > 0 && _keyParts[idx].TextLength == 0)
                    {
                        _keyParts[idx - 1].Focus();
                        _keyParts[idx - 1].SelectionStart = _keyParts[idx - 1].TextLength;
                        e.SuppressKeyPress = true;
                    }
                };

                keyRow.Controls.Add(_keyParts[i]);
                if (i < 3)
                {
                    keyRow.Controls.Add(new Label
                    {
                        Text = "-",
                        ForeColor = TextDim,
                        AutoSize = true,
                        Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                        Margin = new Padding(6, 6, 0, 0)
                    });
                }
            }

            _btnActivate = PrimaryButton("Activate / Replace");
            _btnActivate.Width = 260;
            _btnActivate.Click += async (_, __) => await ActivateAsync();

            var keyActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 12, 0, 0)
            };
            keyActions.Controls.Add(_btnActivate);

            k.Controls.Add(keyRow, 0, 2);
            k.Controls.Add(keyActions, 0, 3);

            keyCard.Controls.Add(k);
            root.Controls.Add(keyCard, 0, 3);

            // Log panel
            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = BgColor,
                ForeColor = TextMain,
                Font = new Font("Consolas", 10F),
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true,
                Margin = new Padding(0, 12, 0, 12)
            };
            root.Controls.Add(_log, 0, 4);

            // Footer buttons
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };

            var btnClose = SecondaryButton("Close");
            btnClose.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

            _btnDeactivate = SecondaryButton("Deactivate This Machine");
            _btnDeactivate.Click += async (_, __) => await DeactivateAsync();

            _btnRevalidate = SecondaryButton("Revalidate Now");
            _btnRevalidate.Click += async (_, __) => await RevalidateAsync();

            footer.Controls.Add(btnClose);
            footer.Controls.Add(_btnDeactivate);
            footer.Controls.Add(_btnRevalidate);

            root.Controls.Add(footer, 0, 5);

            Controls.Add(root);
        }

        private Panel CardPanel()
        {
            var p = new Panel
            {
                Dock = DockStyle.Top,
                BackColor = Surface,
                Margin = new Padding(0, 0, 0, 14),
                Padding = new Padding(1)
            };
            p.Paint += (_, e) =>
            {
                using var pen = new Pen(Border);
                e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }

        private Label MakeKey(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = TextDim,
            Font = new Font("Segoe UI", 9F),
            Margin = new Padding(0, 0, 12, 0)
        };

        private Label MakeVal(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 10F),
            MaximumSize = new Size(520, 0)
        };

        private Button PrimaryButton(string text)
        {
            var b = new Button
            {
                Text = text,
                Height = 40,
                BackColor = Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 10, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private Button SecondaryButton(string text)
        {
            var b = new Button
            {
                Text = text,
                Height = 40,
                BackColor = BgColor,
                ForeColor = TextMain,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 10, 0)
            };
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        private void AddCopyRow(TableLayoutPanel t, int row, string label, string value)
        {
            // row 0 is header
            var targetRow = row;

            while (t.RowStyles.Count <= targetRow)
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var k = MakeKey(label + ":");
            var v = new TextBox
            {
                Text = value,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = BgColor,
                ForeColor = TextMain,
                Font = new Font("Consolas", 10F),
                Width = 520
            };
            var copy = SecondaryButton("Copy");
            copy.Width = 90;
            copy.Height = 30;
            copy.Margin = new Padding(10, 0, 0, 0);
            copy.Click += (_, __) =>
            {
                try { Clipboard.SetText(value); } catch { }
                AppendLog("success", $"Copied {label}");
            };

            t.Controls.Add(k, 0, targetRow);
            t.Controls.Add(v, 1, targetRow);
            t.Controls.Add(copy, 2, targetRow);
        }

        private void AppendLog(string type, string message)
        {
            var c = type == "success" ? Success : type == "warning" ? Warning : type == "error" ? Danger : TextDim;
            _log.SelectionColor = TextDim;
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
            _log.SelectionColor = c;
            _log.AppendText($"{message}\n");
            _log.ScrollToCaret();
        }

        private async Task RefreshUiAsync()
        {
            var email = LicenseService.Instance.UserEmail ?? "";
            _lblSignedIn.Text = string.IsNullOrWhiteSpace(email) ? "Not signed in" : email;
            _lblSignedIn.ForeColor = string.IsNullOrWhiteSpace(email) ? Warning : TextMain;

            // Strict mode: show sign-in link if missing identity
            _lnkSignIn.Visible = !LicenseService.Instance.HasSsoIdentity;

            var res = await LicenseService.Instance.ValidateAsync();
            RenderStatus(res);
        }

        private void RenderStatus(LicenseValidationResult res)
        {
            if (!LicenseService.Instance.HasSsoIdentity)
            {
                _lblLicenseLine.Text = "SSO required (sign in to validate)";
                _lblLicenseLine.ForeColor = Warning;
                _lblLicenseDetail.Text = "Licensing is enforced per-user. Sign in with Google, then activate your key.";
                _btnActivate.Enabled = false;
                _btnRevalidate.Enabled = false;
                _btnDeactivate.Enabled = false;
                return;
            }

            if (res.IsValid)
            {
                _lblLicenseLine.Text = "Active";
                _lblLicenseLine.ForeColor = Success;

                var exp = LicenseService.Instance.ExpiresAt?.ToString("yyyy-MM-dd") ?? "Never";
                var who = LicenseService.Instance.CustomerName ?? "—";
                var em = LicenseService.Instance.CustomerEmail ?? "—";
                var typ = LicenseService.Instance.LicenseType ?? "—";
                var used = $"{LicenseService.Instance.MachinesUsed}/{LicenseService.Instance.MaxMachines}";

                _lblLicenseDetail.Text = $"Licensed To: {who}\nEmail: {em}\nType: {typ}\nExpires: {exp}\nMachines: {used}";
                _btnActivate.Enabled = true;
                _btnRevalidate.Enabled = true;
                _btnDeactivate.Enabled = true;
            }
            else if (res.HasLicense)
            {
                _lblLicenseLine.Text = "Attention needed";
                _lblLicenseLine.ForeColor = Warning;
                _lblLicenseDetail.Text = res.Message ?? "License exists but is not valid for this machine/account.";
                _btnActivate.Enabled = true;
                _btnRevalidate.Enabled = true;
                _btnDeactivate.Enabled = true;
            }
            else
            {
                _lblLicenseLine.Text = "Not activated";
                _lblLicenseLine.ForeColor = TextDim;
                _lblLicenseDetail.Text = "No license on this machine. Enter your key to activate.";
                _btnActivate.Enabled = true;
                _btnRevalidate.Enabled = false;
                _btnDeactivate.Enabled = false;
            }
        }

        private string BuildKey()
        {
            var parts = _keyParts.Select(x => (x.Text ?? "").Trim().ToUpperInvariant()).ToArray();
            if (parts.Any(p => p.Length != 4))
                return "";
            return string.Join("-", parts);
        }

        private async Task ActivateAsync()
        {
            var key = BuildKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Please enter a full license key (XXXX-XXXX-XXXX-XXXX).", "Invalid Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            AppendLog("info", $"Activating key on {Environment.MachineName}...");

            var res = await LicenseService.Instance.ActivateAsync(key);

            AppendLog(res.IsValid ? "success" : "error", res.Message ?? (res.IsValid ? "Activation successful." : "Activation failed."));
            RenderStatus(res);

            if (res.IsValid)
                DialogResult = DialogResult.OK;

            SetBusy(false);
        }

        private async Task RevalidateAsync()
        {
            SetBusy(true);
            AppendLog("info", "Revalidating license with server...");
            var res = await LicenseService.Instance.ValidateAsync();
            AppendLog(res.IsValid ? "success" : "warning", res.Message ?? "Revalidation complete.");
            RenderStatus(res);
            SetBusy(false);
        }

        private async Task DeactivateAsync()
        {
            if (string.IsNullOrWhiteSpace(LicenseService.Instance.LicenseKey))
            {
                MessageBox.Show("No license is currently stored on this machine.", "Nothing to Deactivate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var dr = MessageBox.Show("Deactivate this machine from the license?\n\nThis will free up a machine slot on your license.", "Confirm Deactivation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dr != DialogResult.Yes) return;

            SetBusy(true);
            AppendLog("info", "Deactivating this machine...");
            var ok = await LicenseService.Instance.DeactivateAsync();
            AppendLog(ok ? "success" : "error", ok ? "Machine deactivated." : "Deactivation failed.");
            await RefreshUiAsync();
            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnActivate.Enabled = !busy && _btnActivate.Enabled;
            _btnRevalidate.Enabled = !busy && _btnRevalidate.Enabled;
            _btnDeactivate.Enabled = !busy && _btnDeactivate.Enabled;
        }
    }
}
