using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PatsKillerPro
{
    /// <summary>
    /// Simple, themed keypad code prompt (replaces VisualBasic Interaction.InputBox).
    /// Enforces 5 digits, digits 1-9 only.
    /// </summary>
    public sealed class KeypadCodeInputForm : Form
    {
        private readonly TextBox _txt;
        private readonly Button _btnOk;

        public string Code => _txt.Text.Trim();

        public KeypadCodeInputForm()
        {
            Text = "Keypad Code";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 240);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(15, 23, 42); // #0F172A
            ForeColor = Color.FromArgb(248, 250, 252);

            var lblTitle = new Label
            {
                Text = "Enter New 5-Digit Keypad Code",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold, GraphicsUnit.Point),
                AutoSize = false,
                Location = new Point(18, 18),
                Size = new Size(484, 28)
            };

            var lblHint = new Label
            {
                Text = "Digits 1–9 only (no zeros). Example: 12345",
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = false,
                Location = new Point(18, 52),
                Size = new Size(484, 20)
            };

            var pnl = new Panel
            {
                Location = new Point(18, 86),
                Size = new Size(484, 56),
                BackColor = Color.FromArgb(30, 41, 59)
            };

            _txt = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 18F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(248, 250, 252),
                BackColor = Color.FromArgb(30, 41, 59),
                Location = new Point(16, 14),
                Width = 360,
                MaxLength = 5
            };

            // Keep it numeric and readable
            _txt.KeyPress += (_, e) =>
            {
                if (char.IsControl(e.KeyChar)) return;
                if (!char.IsDigit(e.KeyChar)) { e.Handled = true; return; }
                if (e.KeyChar == '0') { e.Handled = true; return; }
            };
            _txt.TextChanged += (_, __) => UpdateOkState();

            var lblValidation = new Label
            {
                Name = "lblValidation",
                Text = "",
                ForeColor = Color.FromArgb(248, 113, 113),
                AutoSize = false,
                Location = new Point(18, 146),
                Size = new Size(484, 20)
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(318, 186),
                Size = new Size(90, 34),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.FlatAppearance.BorderSize = 0;

            _btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(414, 186),
                Size = new Size(88, 34),
                BackColor = Color.FromArgb(236, 72, 153),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            _btnOk.FlatAppearance.BorderSize = 0;

            _btnOk.Click += (_, __) =>
            {
                var msg = ValidateCode(Code);
                if (msg != null)
                {
                    lblValidation.Text = msg;
                    DialogResult = DialogResult.None;
                }
            };

            pnl.Controls.Add(_txt);
            Controls.Add(lblTitle);
            Controls.Add(lblHint);
            Controls.Add(pnl);
            Controls.Add(lblValidation);
            Controls.Add(btnCancel);
            Controls.Add(_btnOk);

            AcceptButton = _btnOk;
            CancelButton = btnCancel;
        }

        private void UpdateOkState()
        {
            _btnOk.Enabled = ValidateCode(Code) == null;
        }

        private static string? ValidateCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "Enter a 5-digit code.";
            if (code.Length != 5) return "Code must be exactly 5 digits.";
            if (code.Any(ch => ch < '1' || ch > '9')) return "Digits must be 1–9 only.";
            return null;
        }
    }
}
