using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PatsKillerPro
{
    /// <summary>
    /// Click-through safety checklist used for any operation that writes to a module
    /// (or runs routines) to reduce bricked-module risk.
    ///
    /// User asked for click-to-confirm (no typing) confirmations.
    /// </summary>
    public sealed class SafetyChecklistForm : Form
    {
        private readonly CheckedListBox _checks;
        private readonly Button _btnContinue;

        public static bool Show(IWin32Window owner, string title, string subtitle, string[] checks)
        {
            using var f = new SafetyChecklistForm(title, subtitle, checks);
            return f.ShowDialog(owner) == DialogResult.OK;
        }

        private SafetyChecklistForm(string title, string subtitle, string[] checks)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 360);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(15, 23, 42); // #0F172A
            ForeColor = Color.FromArgb(248, 250, 252); // #F8FAFC

            var lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point),
                AutoSize = false,
                Location = new Point(18, 16),
                Size = new Size(524, 30)
            };

            var lblSub = new Label
            {
                Text = subtitle,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(148, 163, 184), // #94A3B8
                AutoSize = false,
                Location = new Point(18, 50),
                Size = new Size(524, 40)
            };

            _checks = new CheckedListBox
            {
                Location = new Point(18, 95),
                Size = new Size(524, 190),
                BackColor = Color.FromArgb(30, 41, 59), // #1E293B
                ForeColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true
            };

            foreach (var c in checks ?? Array.Empty<string>())
                _checks.Items.Add(c, false);

            _checks.ItemCheck += (_, __) => BeginInvoke(new Action(UpdateContinueState));

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(352, 305),
                Size = new Size(90, 34),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.FlatAppearance.BorderSize = 0;

            _btnContinue = new Button
            {
                Text = "I Understand — Continue",
                DialogResult = DialogResult.OK,
                Location = new Point(448, 305),
                Size = new Size(194, 34),
                BackColor = Color.FromArgb(236, 72, 153), // #EC4899
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            _btnContinue.FlatAppearance.BorderSize = 0;

            AcceptButton = _btnContinue;
            CancelButton = btnCancel;

            Controls.Add(lblTitle);
            Controls.Add(lblSub);
            Controls.Add(_checks);
            Controls.Add(btnCancel);
            Controls.Add(_btnContinue);

            UpdateContinueState();
        }

        private void UpdateContinueState()
        {
            // Require ALL checks to be ticked to proceed.
            _btnContinue.Enabled = _checks.Items.Count > 0 && _checks.CheckedItems.Count == _checks.Items.Count;
        }
    }
}
