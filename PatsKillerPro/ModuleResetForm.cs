using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PatsKillerPro
{
    /// <summary>
    /// Module Reset Selection Form - scan vehicle and select modules to reset
    /// </summary>
    public class ModuleResetForm : Form
    {
        // Theme
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
        private readonly Color BTN_BG = Color.FromArgb(54, 54, 64);

        // Module definitions
        public class ModuleInfo
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public uint Address { get; set; }
            public int TokenCost { get; set; } = 1;
            public bool IsAvailable { get; set; }
            public bool IsSelected { get; set; }
        }

        private static readonly List<ModuleInfo> AllModules = new()
        {
            new ModuleInfo { Id = "BCM", Name = "BCM", Description = "Body Control Module - Door locks, windows, lighting", Address = 0x726, TokenCost = 1 },
            new ModuleInfo { Id = "PCM", Name = "PCM", Description = "Powertrain Control Module - Engine, transmission", Address = 0x7E0, TokenCost = 1 },
            new ModuleInfo { Id = "ABS", Name = "ABS", Description = "Anti-lock Brake System", Address = 0x760, TokenCost = 1 },
            new ModuleInfo { Id = "IPC", Name = "IPC", Description = "Instrument Panel Cluster - Gauges, warnings", Address = 0x720, TokenCost = 1 },
            new ModuleInfo { Id = "TCM", Name = "TCM", Description = "Transmission Control Module", Address = 0x7E1, TokenCost = 1 },
            new ModuleInfo { Id = "RCM", Name = "RCM", Description = "Restraint Control Module - Airbags", Address = 0x737, TokenCost = 1 },
            new ModuleInfo { Id = "APIM", Name = "APIM", Description = "Accessory Protocol Interface Module - SYNC", Address = 0x7D0, TokenCost = 1 },
            new ModuleInfo { Id = "PSCM", Name = "PSCM", Description = "Power Steering Control Module", Address = 0x730, TokenCost = 1 },
        };

        private List<ModuleInfo> _modules = new();
        private Label _lblTotal = null!;
        private Button _btnReset = null!;
        private Button _btnScan = null!;
        private Panel _moduleList = null!;
        private Label _lblStatus = null!;
        private bool _isScanning = false;

        public List<ModuleInfo> SelectedModules => _modules.Where(m => m.IsSelected).ToList();
        public int TotalTokenCost => SelectedModules.Sum(m => m.TokenCost);

        public ModuleResetForm()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "Module Parameter Reset";
            ClientSize = new Size(500, 550);
            BackColor = BG;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            // Title
            var title = new Label
            {
                Text = "MODULE RESET SCAN",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = TEXT,
                Dock = DockStyle.Top,
                Height = 50,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(title);

            // Status label
            _lblStatus = new Label
            {
                Text = "Click 'Scan Vehicle' to detect available modules",
                Font = new Font("Segoe UI", 10),
                ForeColor = TEXT_DIM,
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_lblStatus);

            // Scan button at top
            var scanPanel = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(20, 5, 20, 5) };
            _btnScan = new Button
            {
                Text = "🔍 Scan Vehicle",
                Dock = DockStyle.Fill,
                BackColor = ACCENT,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnScan.FlatAppearance.BorderSize = 0;
            _btnScan.Click += BtnScan_Click;
            scanPanel.Controls.Add(_btnScan);
            Controls.Add(scanPanel);

            // Module list panel
            _moduleList = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20, 10, 20, 10),
                BackColor = SURFACE
            };
            Controls.Add(_moduleList);

            // Bottom panel with total and buttons
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 100,
                BackColor = CARD,
                Padding = new Padding(20)
            };

            _lblTotal = new Label
            {
                Text = "Selected: 0 modules = 0 tokens",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = WARNING,
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter
            };
            bottomPanel.Controls.Add(_lblTotal);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0)
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(100, 40),
                BackColor = BTN_BG,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(10, 0, 0, 0)
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttonPanel.Controls.Add(btnCancel);

            _btnReset = new Button
            {
                Text = "⚠ Reset Selected",
                Size = new Size(150, 40),
                BackColor = DANGER,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnReset.FlatAppearance.BorderSize = 0;
            _btnReset.Click += BtnReset_Click;
            buttonPanel.Controls.Add(_btnReset);

            bottomPanel.Controls.Add(buttonPanel);
            Controls.Add(bottomPanel);

            // Reorder controls (WinForms quirk)
            Controls.SetChildIndex(title, Controls.Count - 1);
            Controls.SetChildIndex(_lblStatus, Controls.Count - 2);
            Controls.SetChildIndex(scanPanel, Controls.Count - 3);
        }

        private async void BtnScan_Click(object? sender, EventArgs e)
        {
            if (_isScanning) return;
            _isScanning = true;
            _btnScan.Enabled = false;
            _btnScan.Text = "Scanning...";
            _lblStatus.Text = "Scanning vehicle modules...";
            _moduleList.Controls.Clear();
            _modules.Clear();

            try
            {
                // Simulate module scan (in real implementation, use J2534 to probe each module)
                await Task.Delay(500);

                // For now, simulate finding modules
                var foundModules = new[] { "BCM", "PCM", "ABS", "IPC" };
                
                foreach (var moduleDef in AllModules)
                {
                    var module = new ModuleInfo
                    {
                        Id = moduleDef.Id,
                        Name = moduleDef.Name,
                        Description = moduleDef.Description,
                        Address = moduleDef.Address,
                        TokenCost = moduleDef.TokenCost,
                        IsAvailable = foundModules.Contains(moduleDef.Id),
                        IsSelected = false
                    };
                    _modules.Add(module);
                }

                BuildModuleList();
                _lblStatus.Text = $"Found {_modules.Count(m => m.IsAvailable)} modules";
                _lblStatus.ForeColor = SUCCESS;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Scan failed: {ex.Message}";
                _lblStatus.ForeColor = DANGER;
            }
            finally
            {
                _isScanning = false;
                _btnScan.Enabled = true;
                _btnScan.Text = "🔍 Scan Vehicle";
            }
        }

        private void BuildModuleList()
        {
            _moduleList.Controls.Clear();
            int y = 0;

            foreach (var module in _modules)
            {
                var row = new Panel
                {
                    Size = new Size(_moduleList.ClientSize.Width - 40, 60),
                    Location = new Point(0, y),
                    BackColor = module.IsAvailable ? CARD : Color.FromArgb(30, 30, 35)
                };

                var chk = new CheckBox
                {
                    Text = "",
                    Location = new Point(10, 20),
                    Size = new Size(20, 20),
                    Enabled = module.IsAvailable,
                    Checked = module.IsSelected,
                    Tag = module
                };
                chk.CheckedChanged += Chk_CheckedChanged;
                row.Controls.Add(chk);

                var lblName = new Label
                {
                    Text = module.Name,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    ForeColor = module.IsAvailable ? TEXT : TEXT_MUTED,
                    Location = new Point(40, 8),
                    AutoSize = true
                };
                row.Controls.Add(lblName);

                var lblDesc = new Label
                {
                    Text = module.Description,
                    Font = new Font("Segoe UI", 9),
                    ForeColor = module.IsAvailable ? TEXT_DIM : TEXT_MUTED,
                    Location = new Point(40, 30),
                    AutoSize = true
                };
                row.Controls.Add(lblDesc);

                var lblToken = new Label
                {
                    Text = module.IsAvailable ? $"{module.TokenCost} tok" : "N/A",
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    ForeColor = module.IsAvailable ? WARNING : TEXT_MUTED,
                    Location = new Point(row.Width - 60, 18),
                    AutoSize = true
                };
                row.Controls.Add(lblToken);

                if (!module.IsAvailable)
                {
                    var lblNotFound = new Label
                    {
                        Text = "(Not Found)",
                        Font = new Font("Segoe UI", 8),
                        ForeColor = TEXT_MUTED,
                        Location = new Point(100, 10),
                        AutoSize = true
                    };
                    row.Controls.Add(lblNotFound);
                }

                _moduleList.Controls.Add(row);
                y += 65;
            }
        }

        private void Chk_CheckedChanged(object? sender, EventArgs e)
        {
            if (sender is CheckBox chk && chk.Tag is ModuleInfo module)
            {
                module.IsSelected = chk.Checked;
                UpdateTotal();
            }
        }

        private void UpdateTotal()
        {
            var selected = SelectedModules;
            var cost = TotalTokenCost;
            
            _lblTotal.Text = $"Selected: {selected.Count} module(s) = {cost} token(s)";
            _lblTotal.ForeColor = cost > 0 ? WARNING : TEXT_DIM;
            _btnReset.Enabled = selected.Count > 0;
        }

        private void BtnReset_Click(object? sender, EventArgs e)
        {
            var selected = SelectedModules;
            if (selected.Count == 0) return;

            var modules = string.Join(", ", selected.Select(m => m.Name));
            var result = MessageBox.Show(
                $"Reset the following modules?\n\n{modules}\n\nTotal cost: {TotalTokenCost} token(s)\n\n⚠️ This will reset module parameters to factory defaults.",
                "Confirm Module Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
