using System;
using System.Drawing;
using System.Windows.Forms;

namespace PatsKillerPro.Forms
{
    public sealed class LicenseEmailMismatchDialog : Form
    {
        public enum Choice
        {
            Cancel = 0,
            SwitchAccount = 1,
            EnterDifferentLicense = 2
        }

        public Choice ResultChoice { get; private set; } = Choice.Cancel;

        public LicenseEmailMismatchDialog(string licensedEmail, string signedInEmail)
        {
            Text = "License Attention Needed";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Padding = new Padding(18);

            var title = new Label
            {
                Text = "License email mismatch",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = true,
                ForeColor = Color.Black,
                Margin = new Padding(0, 0, 0, 10)
            };

            var body = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Font = new Font("Segoe UI", 10),
                Text = $"This license belongs to {licensedEmail}, but you are signed in as {signedInEmail}.\n\n" +
                       "To continue, switch to the correct Google account or enter a different license key.",
                Margin = new Padding(0, 0, 0, 14)
            };

            var btnSwitch = new Button
            {
                Text = "Switch Account",
                AutoSize = true,
                Padding = new Padding(14, 8, 14, 8),
                Margin = new Padding(0, 0, 10, 0)
            };
            btnSwitch.Click += (_, __) => { ResultChoice = Choice.SwitchAccount; DialogResult = DialogResult.OK; Close(); };

            var btnEnter = new Button
            {
                Text = "Enter Different License Key",
                AutoSize = true,
                Padding = new Padding(14, 8, 14, 8),
                Margin = new Padding(0, 0, 10, 0)
            };
            btnEnter.Click += (_, __) => { ResultChoice = Choice.EnterDifferentLicense; DialogResult = DialogResult.OK; Close(); };

            var btnCancel = new Button
            {
                Text = "Cancel",
                AutoSize = true,
                Padding = new Padding(14, 8, 14, 8)
            };
            btnCancel.Click += (_, __) => { ResultChoice = Choice.Cancel; DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = btnSwitch;
            CancelButton = btnCancel;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
            buttons.Controls.Add(btnSwitch);
            buttons.Controls.Add(btnEnter);
            buttons.Controls.Add(btnCancel);

            var root = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            root.Controls.Add(title);
            root.Controls.Add(body);
            root.Controls.Add(buttons);

            Controls.Add(root);

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }
    }
}
