using System;
using System.Drawing;
using System.Windows.Forms;

namespace PatsKillerPro
{
    /// <summary>
    /// Confirmation dialog for Erase All Keys with mandatory safety checkboxes
    /// </summary>
    public class EraseKeysConfirmationForm : Form
    {
        // Theme colors
        private readonly Color BG = Color.FromArgb(26, 26, 30);
        private readonly Color SURFACE = Color.FromArgb(35, 35, 40);
        private readonly Color CARD = Color.FromArgb(42, 42, 48);
        private readonly Color BORDER = Color.FromArgb(58, 58, 66);
        private readonly Color TEXT = Color.FromArgb(240, 240, 240);
        private readonly Color TEXT_DIM = Color.FromArgb(160, 160, 165);
        private readonly Color DANGER = Color.FromArgb(239, 68, 68);
        private readonly Color WARNING = Color.FromArgb(234, 179, 8);
        private readonly Color BTN_BG = Color.FromArgb(54, 54, 64);

        private CheckBox _chk1 = null!;
        private CheckBox _chk2 = null!;
        private CheckBox _chk3 = null!;
        private Button _btnErase = null!;

        public EraseKeysConfirmationForm()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "⚠️ ERASE ALL KEYS - CRITICAL WARNING";
            ClientSize = new Size(520, 420);
            BackColor = BG;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            int y = 20;

            // Warning icon and title
            var lblWarning = new Label
            {
                Text = "⚠️ CRITICAL WARNING",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = DANGER,
                Location = new Point(20, y),
                AutoSize = true
            };
            Controls.Add(lblWarning);
            y += 50;

            // Warning message box
            var warningPanel = new Panel
            {
                Location = new Point(20, y),
                Size = new Size(480, 80),
                BackColor = Color.FromArgb(40, 239, 68, 68)
            };
            warningPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(DANGER, 2);
                e.Graphics.DrawRectangle(pen, 1, 1, warningPanel.Width - 3, warningPanel.Height - 3);
            };

            var lblMessage = new Label
            {
                Text = "Erasing all keys will render the vehicle UNABLE TO START!\n\n" +
                       "You MUST program at least 2 new keys immediately after erasing,\n" +
                       "or the vehicle will require dealer-level recovery.",
                Font = new Font("Segoe UI", 10),
                ForeColor = TEXT,
                Location = new Point(15, 10),
                Size = new Size(450, 60)
            };
            warningPanel.Controls.Add(lblMessage);
            Controls.Add(warningPanel);
            y += 100;

            // Checkbox section title
            var lblConfirm = new Label
            {
                Text = "You must confirm ALL of the following:",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = WARNING,
                Location = new Point(20, y),
                AutoSize = true
            };
            Controls.Add(lblConfirm);
            y += 35;

            // Checkbox 1
            _chk1 = CreateCheckbox(
                "I have at least 2 unprogrammed PATS keys ready to program",
                new Point(20, y)
            );
            Controls.Add(_chk1);
            y += 45;

            // Checkbox 2
            _chk2 = CreateCheckbox(
                "I understand I MUST program 2 keys immediately after erasing",
                new Point(20, y)
            );
            Controls.Add(_chk2);
            y += 45;

            // Checkbox 3
            _chk3 = CreateCheckbox(
                "I understand the vehicle will NOT START until 2+ keys are programmed",
                new Point(20, y)
            );
            Controls.Add(_chk3);
            y += 60;

            // Button panel
            var btnPanel = new FlowLayoutPanel
            {
                Location = new Point(20, y),
                Size = new Size(480, 50),
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(120, 44),
                BackColor = BTN_BG,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(10, 0, 0, 0)
            };
            btnCancel.FlatAppearance.BorderColor = BORDER;
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            btnPanel.Controls.Add(btnCancel);

            _btnErase = new Button
            {
                Text = "🗑️ ERASE ALL KEYS",
                Size = new Size(180, 44),
                BackColor = DANGER,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnErase.FlatAppearance.BorderSize = 0;
            _btnErase.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            btnPanel.Controls.Add(_btnErase);

            Controls.Add(btnPanel);

            // Token cost label
            var lblCost = new Label
            {
                Text = "Cost: FREE if session active, otherwise 1 token",
                Font = new Font("Segoe UI", 9),
                ForeColor = TEXT_MUTED,
                Location = new Point(20, ClientSize.Height - 30),
                AutoSize = true
            };
            Controls.Add(lblCost);
        }

        private CheckBox CreateCheckbox(string text, Point location)
        {
            var chk = new CheckBox
            {
                Text = text,
                Font = new Font("Segoe UI", 10),
                ForeColor = TEXT,
                Location = location,
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            chk.CheckedChanged += Checkbox_CheckedChanged;
            return chk;
        }

        private void Checkbox_CheckedChanged(object? sender, EventArgs e)
        {
            // Enable erase button only when ALL checkboxes are checked
            bool allChecked = _chk1.Checked && _chk2.Checked && _chk3.Checked;
            _btnErase.Enabled = allChecked;
            
            // Visual feedback
            _btnErase.BackColor = allChecked ? DANGER : BTN_BG;
        }

        /// <summary>
        /// Show the confirmation dialog and return true if user confirmed
        /// </summary>
        public static bool ShowConfirmation(IWin32Window? owner = null)
        {
            using var form = new EraseKeysConfirmationForm();
            return form.ShowDialog(owner) == DialogResult.OK;
        }
    }
}
