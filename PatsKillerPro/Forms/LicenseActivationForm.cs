using System;
using System.Drawing;
using System.Windows.Forms;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Forms
{
    public class LicenseActivationForm : Form
    {
        // Dark Theme (V15 Navy)
        private static readonly Color BgColor = ColorTranslator.FromHtml("#0F172A");
        private static readonly Color Surface = ColorTranslator.FromHtml("#1E293B");
        private static readonly Color Accent  = ColorTranslator.FromHtml("#E94796");
        private static readonly Color TextMain= Color.White;
        private static readonly Color TextDim = ColorTranslator.FromHtml("#94A3B8");

        private TextBox[] _keys = new TextBox[4];
        private Button _btnActivate;

        public LicenseActivationForm()
        {
            this.Text = "Activate License";
            this.Size = new Size(500, 450);
            this.BackColor = BgColor;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            InitializeUI();
        }

        private void InitializeUI()
        {
            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30) };

            // Title
            var lblTitle = new Label { Text = "Activate License", Font = new Font("Segoe UI", 18F, FontStyle.Bold), ForeColor = Accent, AutoSize = true, Location = new Point(30, 30) };
            
            // Instructions
            var lblInst = new Label { Text = "Enter the 16-character license key found in your email.", Font = new Font("Segoe UI", 9F), ForeColor = TextDim, AutoSize = true, Location = new Point(30, 70) };

            // Machine ID Box
            var pnlId = new Panel { Size = new Size(420, 60), Location = new Point(30, 110), BackColor = Surface };
            var lblId = new Label { Text = $"Machine ID: {MachineIdentity.MachineId}", ForeColor = TextMain, Font = new Font("Consolas", 10F), Location = new Point(10, 20), AutoSize = true };
            pnlId.Controls.Add(lblId);

            // Inputs
            var pnlKeys = new Panel { Location = new Point(30, 190), Size = new Size(420, 40) };
            for(int i=0; i<4; i++) {
                _keys[i] = new TextBox { 
                    Size = new Size(80, 30), 
                    Location = new Point(i * 100, 0), 
                    BackColor = Surface, 
                    ForeColor = TextMain, 
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 14F),
                    MaxLength = 4,
                    TextAlign = HorizontalAlignment.Center,
                    CharacterCasing = CharacterCasing.Upper
                };
                pnlKeys.Controls.Add(_keys[i]);
            }

            // Buttons
            _btnActivate = new Button { Text = "Activate Now", Size = new Size(420, 50), Location = new Point(30, 260), BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 12F, FontStyle.Bold), Cursor = Cursors.Hand };
            _btnActivate.FlatAppearance.BorderSize = 0;
            _btnActivate.Click += BtnActivate_Click;

            var btnSso = new Label { Text = "Or sign in with Google", ForeColor = TextDim, AutoSize = true, Location = new Point(180, 330), Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9F, FontStyle.Underline) };
            btnSso.Click += (s,e) => { this.DialogResult = DialogResult.Retry; this.Close(); };

            mainPanel.Controls.AddRange(new Control[] { lblTitle, lblInst, pnlId, pnlKeys, _btnActivate, btnSso });
            this.Controls.Add(mainPanel);
        }

        private async void BtnActivate_Click(object sender, EventArgs e)
        {
            string key = $"{_keys[0].Text}-{_keys[1].Text}-{_keys[2].Text}-{_keys[3].Text}";
            _btnActivate.Text = "Activating...";
            _btnActivate.Enabled = false;

            var result = await LicenseService.Instance.ActivateAsync(key);
            
            if (result.IsValid) {
                MessageBox.Show("Activation Successful!", "PatsKiller Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
                this.Close();
            } else {
                MessageBox.Show($"Activation Failed: {result.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnActivate.Text = "Activate Now";
                _btnActivate.Enabled = true;
            }
        }
    }
}