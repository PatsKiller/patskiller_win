using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Communication;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;
using PatsKillerPro.Vehicle;

namespace PatsKillerPro.Forms
{
    /// <summary>
    /// PATS Target Blocks Form - Read/Write target data for BCM, PCM, ABS, RFA, ESCL
    /// Token Cost: Read = FREE, Write = 1 TOKEN per module
    /// </summary>
    public class TargetsForm : Form
    {
        private readonly Color BG = Color.FromArgb(26, 26, 30);
        private readonly Color SURFACE = Color.FromArgb(35, 35, 40);
        private readonly Color CARD = Color.FromArgb(42, 42, 48);
        private readonly Color BORDER = Color.FromArgb(58, 58, 66);
        private readonly Color TEXT = Color.FromArgb(240, 240, 240);
        private readonly Color TEXT_DIM = Color.FromArgb(160, 160, 165);
        private readonly Color ACCENT = Color.FromArgb(59, 130, 246);
        private readonly Color SUCCESS = Color.FromArgb(34, 197, 94);
        private readonly Color WARNING = Color.FromArgb(234, 179, 8);
        private readonly Color DANGER = Color.FromArgb(239, 68, 68);
        private readonly Color BTN_BG = Color.FromArgb(54, 54, 64);

        private const ushort DID_TARGET_BLOCK = 0xC00C;
        private const ushort DID_RFA_TARGET = 0xC194;
        private const ushort DID_ESCL_TARGET = 0xD130;

        private TabControl _tabControl = null!;
        private TextBox _txtBcmTarget = null!, _txtPcmTarget = null!, _txtAbsTarget = null!;
        private TextBox _txtRfaTarget = null!, _txtEsclTarget = null!;
        private TextBox _txtBcmOriginal = null!, _txtPcmOriginal = null!, _txtAbsOriginal = null!;
        private TextBox _txtRfaOriginal = null!, _txtEsclOriginal = null!;
        private Button _btnReadAll = null!, _btnWriteSelected = null!, _btnImport = null!, _btnExport = null!;
        private CheckBox _chkBcm = null!, _chkPcm = null!, _chkAbs = null!, _chkRfa = null!, _chkEscl = null!;
        private Label _lblStatus = null!;
        private RichTextBox _txtLog = null!;
        private ToolTip _toolTip = null!;

        private readonly UdsService _uds;
        private readonly string _vin;
        private bool _hasUnsavedChanges = false;

        public TargetsForm(UdsService uds, string vin)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
            _vin = vin ?? "Unknown";
            InitializeComponent();
            BuildUI();
        }

        private void InitializeComponent()
        {
            Text = "PATS Target Blocks";
            Size = new Size(900, 700);
            MinimumSize = new Size(800, 600);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BG;
            ForeColor = TEXT;
            Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            _toolTip = ToolTipHelper.CreateToolTip();
        }

        private void BuildUI()
        {
            var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = BG, Padding = new Padding(15) };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

            var header = new Panel { Dock = DockStyle.Fill, BackColor = CARD, Padding = new Padding(15, 10, 15, 10) };
            header.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, header.Width - 1, header.Height - 1); };
            var headerFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            headerFlow.Controls.Add(new Label { Text = "🎯 PATS TARGET BLOCKS", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = ACCENT, AutoSize = true, Margin = new Padding(0, 5, 20, 0) });
            headerFlow.Controls.Add(new Label { Text = $"VIN: {_vin}", Font = new Font("Segoe UI", 11), ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 8, 20, 0) });
            _lblStatus = new Label { Text = "Ready", Font = new Font("Segoe UI", 10), ForeColor = SUCCESS, AutoSize = true, Margin = new Padding(0, 9, 0, 0) };
            headerFlow.Controls.Add(_lblStatus);
            header.Controls.Add(headerFlow);
            mainLayout.Controls.Add(header, 0, 0);

            _tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10), Margin = new Padding(0, 10, 0, 10) };
            _tabControl.TabPages.Add(CreateModuleTab("BCM", ref _txtBcmTarget, ref _txtBcmOriginal, ref _chkBcm, "Body Control Module (10 bytes) DID: 0xC00C"));
            _tabControl.TabPages.Add(CreateModuleTab("PCM", ref _txtPcmTarget, ref _txtPcmOriginal, ref _chkPcm, "Powertrain Control Module (10 bytes) DID: 0xC00C"));
            _tabControl.TabPages.Add(CreateModuleTab("ABS", ref _txtAbsTarget, ref _txtAbsOriginal, ref _chkAbs, "Anti-lock Brake System (10 bytes) DID: 0xC00C"));
            _tabControl.TabPages.Add(CreateModuleTab("RFA", ref _txtRfaTarget, ref _txtRfaOriginal, ref _chkRfa, "Remote Function Actuator (8 bytes) DID: 0xC194"));
            _tabControl.TabPages.Add(CreateModuleTab("ESCL", ref _txtEsclTarget, ref _txtEsclOriginal, ref _chkEscl, "Electronic Steering Column Lock (8 bytes) DID: 0xD130"));
            mainLayout.Controls.Add(_tabControl, 0, 1);

            var buttonBar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 10) };
            _btnReadAll = CreateButton("📖 Read All", ACCENT); _btnReadAll.Click += BtnReadAll_Click; buttonBar.Controls.Add(_btnReadAll);
            _btnWriteSelected = CreateButton("💾 Write Selected", WARNING); _btnWriteSelected.Click += BtnWriteSelected_Click; buttonBar.Controls.Add(_btnWriteSelected);
            buttonBar.Controls.Add(new Label { Text = "│", ForeColor = BORDER, AutoSize = true, Margin = new Padding(10, 12, 10, 0) });
            _btnImport = CreateButton("📥 Import JSON", BTN_BG); _btnImport.Click += BtnImport_Click; buttonBar.Controls.Add(_btnImport);
            _btnExport = CreateButton("📤 Export JSON", BTN_BG); _btnExport.Click += BtnExport_Click; buttonBar.Controls.Add(_btnExport);
            mainLayout.Controls.Add(buttonBar, 0, 2);

            var logPanel = new Panel { Dock = DockStyle.Fill, BackColor = SURFACE, Padding = new Padding(10) };
            logPanel.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, logPanel.Width - 1, logPanel.Height - 1); };
            _txtLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 35), ForeColor = TEXT_DIM, Font = new Font("Consolas", 9), ReadOnly = true, BorderStyle = BorderStyle.None };
            logPanel.Controls.Add(_txtLog);
            mainLayout.Controls.Add(logPanel, 0, 3);

            Controls.Add(mainLayout);
        }

        private TabPage CreateModuleTab(string moduleName, ref TextBox txtCurrent, ref TextBox txtOriginal, ref CheckBox chkWrite, string tooltip)
        {
            var tab = new TabPage(moduleName) { BackColor = SURFACE, Padding = new Padding(15) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            chkWrite = new CheckBox { Text = $"Include {moduleName} in write", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            layout.Controls.Add(chkWrite, 0, 0); layout.SetColumnSpan(chkWrite, 2);

            layout.Controls.Add(new Label { Text = "Original:", ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 1);
            txtOriginal = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 35, 40), ForeColor = Color.FromArgb(112, 112, 117), Font = new Font("Consolas", 11), ReadOnly = true, Margin = new Padding(0, 5, 0, 5) };
            layout.Controls.Add(txtOriginal, 1, 1);

            layout.Controls.Add(new Label { Text = "New Value:", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 2);
            txtCurrent = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 55), ForeColor = TEXT, Font = new Font("Consolas", 11), Margin = new Padding(0, 5, 0, 5) };
            var origBox = txtOriginal; var curBox = txtCurrent;
            txtCurrent.TextChanged += (s, e) => { _hasUnsavedChanges = true; curBox.BackColor = (origBox.Text != curBox.Text && !string.IsNullOrEmpty(origBox.Text)) ? Color.FromArgb(60, 50, 30) : Color.FromArgb(45, 45, 55); };
            layout.Controls.Add(txtCurrent, 1, 2);

            var infoPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 40, 50), Margin = new Padding(0, 10, 0, 0) };
            infoPanel.Paint += (s, e) => { using var p = new Pen(ACCENT, 1); e.Graphics.DrawRectangle(p, 0, 0, infoPanel.Width - 1, infoPanel.Height - 1); };
            infoPanel.Controls.Add(new Label { Text = $"ℹ️ {tooltip}\n\nFormat: Hex bytes separated by spaces (e.g., A1 B2 C3 D4 E5 F6 G7 H8 I9 J0)", ForeColor = TEXT_DIM, Dock = DockStyle.Fill, Padding = new Padding(10) });
            layout.Controls.Add(infoPanel, 0, 3); layout.SetColumnSpan(infoPanel, 2);

            tab.Controls.Add(layout);
            return tab;
        }

        private Button CreateButton(string text, Color bgColor)
        {
            var btn = new Button { Text = text, Size = new Size(140, 40), FlatStyle = FlatStyle.Flat, BackColor = bgColor, ForeColor = TEXT, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
            btn.FlatAppearance.BorderColor = BORDER;
            return btn;
        }

        private void Log(string message, Color? color = null)
        {
            if (_txtLog.InvokeRequired) { _txtLog.Invoke(new Action(() => Log(message, color))); return; }
            _txtLog.SelectionStart = _txtLog.TextLength; _txtLog.SelectionColor = color ?? TEXT_DIM;
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n"); _txtLog.ScrollToCaret();
        }

        private void SetStatus(string text, Color color) { if (_lblStatus.InvokeRequired) { _lblStatus.Invoke(new Action(() => SetStatus(text, color))); return; } _lblStatus.Text = text; _lblStatus.ForeColor = color; }

        private async void BtnReadAll_Click(object? sender, EventArgs e)
        {
            SetStatus("Reading...", WARNING); _btnReadAll.Enabled = false;
            try {
                Log("=== Reading Target Blocks ===", ACCENT);
                await ReadModuleTarget("BCM", ModuleAddresses.BCM_TX, DID_TARGET_BLOCK, 10, _txtBcmTarget, _txtBcmOriginal);
                await ReadModuleTarget("PCM", ModuleAddresses.PCM_TX, DID_TARGET_BLOCK, 10, _txtPcmTarget, _txtPcmOriginal);
                await ReadModuleTarget("ABS", ModuleAddresses.ABS_TX, DID_TARGET_BLOCK, 10, _txtAbsTarget, _txtAbsOriginal);
                await ReadModuleTarget("RFA", ModuleAddresses.RFA_TX, DID_RFA_TARGET, 8, _txtRfaTarget, _txtRfaOriginal);
                await ReadModuleTarget("ESCL", ModuleAddresses.ESCL_TX, DID_ESCL_TARGET, 8, _txtEsclTarget, _txtEsclOriginal);
                Log("=== Read Complete ===", SUCCESS); SetStatus("Read complete", SUCCESS);
            } catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Read failed", DANGER); }
            finally { _btnReadAll.Enabled = true; }
        }

        private async Task ReadModuleTarget(string moduleName, uint moduleAddr, ushort did, int expectedLen, TextBox txtCurrent, TextBox txtOriginal)
        {
            await Task.Run(() => {
                try {
                    Log($"Reading {moduleName} target (DID 0x{did:X4})...");
                    _uds.StartExtendedSession(moduleAddr); System.Threading.Thread.Sleep(50);
                    var data = _uds.ReadDataByIdentifier(moduleAddr, did);
                    if (data != null && data.Length >= expectedLen) {
                        var hex = BitConverter.ToString(data, 0, expectedLen).Replace("-", " ");
                        Invoke(new Action(() => { txtOriginal.Text = hex; txtCurrent.Text = hex; }));
                        Log($"  ✓ {moduleName}: {hex}", SUCCESS);
                    } else Log($"  ✗ {moduleName}: No response", WARNING);
                } catch (Exception ex) { Log($"  ✗ {moduleName}: {ex.Message}", DANGER); }
            });
        }

        private async void BtnWriteSelected_Click(object? sender, EventArgs e)
        {
            int cnt = (_chkBcm.Checked ? 1 : 0) + (_chkPcm.Checked ? 1 : 0) + (_chkAbs.Checked ? 1 : 0) + (_chkRfa.Checked ? 1 : 0) + (_chkEscl.Checked ? 1 : 0);
            if (cnt == 0) { MessageBox.Show("Select at least one module.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            using var confirm = new TargetWriteConfirmationForm(cnt);
            if (confirm.ShowDialog(this) != DialogResult.OK) return;
            if (!TokenBalanceService.Instance.HasEnoughTokens(cnt)) { MessageBox.Show($"Insufficient tokens. Need {cnt}, have {TokenBalanceService.Instance.TotalTokens}", "Insufficient Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            SetStatus("Writing...", WARNING); _btnWriteSelected.Enabled = false;
            try {
                Log($"=== Writing Target Blocks ({cnt} modules) ===", ACCENT);
                if (_chkBcm.Checked) await WriteModuleTarget("BCM", ModuleAddresses.BCM_TX, DID_TARGET_BLOCK, _txtBcmTarget.Text);
                if (_chkPcm.Checked) await WriteModuleTarget("PCM", ModuleAddresses.PCM_TX, DID_TARGET_BLOCK, _txtPcmTarget.Text);
                if (_chkAbs.Checked) await WriteModuleTarget("ABS", ModuleAddresses.ABS_TX, DID_TARGET_BLOCK, _txtAbsTarget.Text);
                if (_chkRfa.Checked) await WriteModuleTarget("RFA", ModuleAddresses.RFA_TX, DID_RFA_TARGET, _txtRfaTarget.Text);
                if (_chkEscl.Checked) await WriteModuleTarget("ESCL", ModuleAddresses.ESCL_TX, DID_ESCL_TARGET, _txtEsclTarget.Text);
                Log("=== Write Complete ===", SUCCESS); SetStatus("Write complete", SUCCESS); _hasUnsavedChanges = false;
            } catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Write failed", DANGER); }
            finally { _btnWriteSelected.Enabled = true; }
        }

        private async Task WriteModuleTarget(string moduleName, uint moduleAddr, ushort did, string hexData)
        {
            await Task.Run(async () => {
                try {
                    Log($"Writing {moduleName} target (DID 0x{did:X4})...");
                    var bytes = ParseHexString(hexData);
                    if (bytes == null || bytes.Length == 0) { Log($"  ✗ {moduleName}: Invalid hex data", DANGER); return; }
                    var tokenResult = await TokenBalanceService.Instance.DeductForUtilityAsync($"target_write_{moduleName.ToLower()}", _vin);
                    if (!tokenResult.Success) { Log($"  ✗ {moduleName}: Token deduction failed - {tokenResult.Error}", DANGER); return; }
                    _uds.StartExtendedSession(moduleAddr); System.Threading.Thread.Sleep(50);
                    if (!_uds.RequestSecurityAccess(moduleAddr)) { Log($"  ✗ {moduleName}: Security access denied", DANGER); return; }
                    System.Threading.Thread.Sleep(50);
                    var success = _uds.WriteDataByIdentifier(moduleAddr, did, bytes);
                    if (success) { Log($"  ✓ {moduleName}: Written successfully (1 token)", SUCCESS); ProActivityLogger.Instance.LogActivity(new ActivityLogEntry { Action = $"target_write_{moduleName.ToLower()}", ActionCategory = "targets", Vin = _vin, Success = true, TokenChange = -1, Details = $"Target block written to {moduleName}", Metadata = new { did = $"0x{did:X4}", data = hexData } }); }
                    else Log($"  ✗ {moduleName}: Write failed", DANGER);
                } catch (Exception ex) { Log($"  ✗ {moduleName}: {ex.Message}", DANGER); }
            });
        }

        private byte[]? ParseHexString(string hex) { try { hex = hex.Replace(" ", "").Replace("-", "").Trim(); if (hex.Length % 2 != 0) return null; var bytes = new byte[hex.Length / 2]; for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16); return bytes; } catch { return null; } }

        private void BtnImport_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "JSON Files|*.json|All Files|*.*", Title = "Import Target Blocks" };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            try { var json = File.ReadAllText(ofd.FileName); var data = JsonSerializer.Deserialize<TargetBlocksData>(json);
                if (data != null) { if (!string.IsNullOrEmpty(data.BCM)) _txtBcmTarget.Text = data.BCM; if (!string.IsNullOrEmpty(data.PCM)) _txtPcmTarget.Text = data.PCM; if (!string.IsNullOrEmpty(data.ABS)) _txtAbsTarget.Text = data.ABS; if (!string.IsNullOrEmpty(data.RFA)) _txtRfaTarget.Text = data.RFA; if (!string.IsNullOrEmpty(data.ESCL)) _txtEsclTarget.Text = data.ESCL; Log($"Imported from {Path.GetFileName(ofd.FileName)}", SUCCESS); }
            } catch (Exception ex) { MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void BtnExport_Click(object? sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog { Filter = "JSON Files|*.json", Title = "Export Target Blocks", FileName = $"targets_{_vin}_{DateTime.Now:yyyyMMdd_HHmmss}.json" };
            if (sfd.ShowDialog() != DialogResult.OK) return;
            try { var data = new TargetBlocksData { VIN = _vin, ExportDate = DateTime.Now.ToString("o"), BCM = _txtBcmTarget.Text, PCM = _txtPcmTarget.Text, ABS = _txtAbsTarget.Text, RFA = _txtRfaTarget.Text, ESCL = _txtEsclTarget.Text };
                File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })); Log($"Exported to {Path.GetFileName(sfd.FileName)}", SUCCESS);
            } catch (Exception ex) { MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { if (_hasUnsavedChanges && MessageBox.Show("Unsaved changes. Close anyway?", "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) { e.Cancel = true; return; } base.OnFormClosing(e); }

        private class TargetBlocksData { public string? VIN { get; set; } public string? ExportDate { get; set; } public string? BCM { get; set; } public string? PCM { get; set; } public string? ABS { get; set; } public string? RFA { get; set; } public string? ESCL { get; set; } }
    }

    public class TargetWriteConfirmationForm : Form
    {
        private readonly Color BG = Color.FromArgb(26, 26, 30), CARD = Color.FromArgb(42, 42, 48), BORDER = Color.FromArgb(58, 58, 66), TEXT = Color.FromArgb(240, 240, 240), WARNING = Color.FromArgb(234, 179, 8), DANGER = Color.FromArgb(239, 68, 68);
        private CheckBox _chk1 = null!, _chk2 = null!, _chk3 = null!; private Button _btnWrite = null!; private readonly int _moduleCount;
        public TargetWriteConfirmationForm(int moduleCount) { _moduleCount = moduleCount; InitUI(); }
        private void InitUI()
        {
            Text = "Confirm Write"; Size = new Size(500, 350); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; BackColor = BG; ForeColor = TEXT; Font = new Font("Segoe UI", 10F);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(20) };
            var warnPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(60, 40, 20), Height = 50 };
            warnPanel.Paint += (s, e) => { using var p = new Pen(WARNING, 2); e.Graphics.DrawRectangle(p, 1, 1, warnPanel.Width - 3, warnPanel.Height - 3); };
            warnPanel.Controls.Add(new Label { Text = $"⚠️ Writing target blocks to {_moduleCount} module(s)", ForeColor = WARNING, Font = new Font("Segoe UI", 11, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
            layout.Controls.Add(warnPanel, 0, 0);
            layout.Controls.Add(new Label { Text = $"Token Cost: {_moduleCount} token(s)", ForeColor = TEXT, Font = new Font("Segoe UI", 11), AutoSize = true, Margin = new Padding(0, 15, 0, 15) }, 0, 1);
            _chk1 = new CheckBox { Text = "I have verified the target data is correct", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 5, 0, 5) }; _chk1.CheckedChanged += UpdateBtn; layout.Controls.Add(_chk1, 0, 2);
            _chk2 = new CheckBox { Text = "I understand this will overwrite existing data", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 5, 0, 5) }; _chk2.CheckedChanged += UpdateBtn; layout.Controls.Add(_chk2, 0, 3);
            _chk3 = new CheckBox { Text = "I have a backup of original values", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 5, 0, 5) }; _chk3.CheckedChanged += UpdateBtn; layout.Controls.Add(_chk3, 0, 4);
            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 20, 0, 0) };
            var btnCancel = new Button { Text = "Cancel", Size = new Size(100, 40), FlatStyle = FlatStyle.Flat, BackColor = CARD, ForeColor = TEXT, DialogResult = DialogResult.Cancel }; btnCancel.FlatAppearance.BorderColor = BORDER; buttonPanel.Controls.Add(btnCancel);
            _btnWrite = new Button { Text = "✓ WRITE", Size = new Size(120, 40), FlatStyle = FlatStyle.Flat, BackColor = DANGER, ForeColor = TEXT, Font = new Font("Segoe UI", 10, FontStyle.Bold), Enabled = false, DialogResult = DialogResult.OK, Margin = new Padding(0, 0, 10, 0) }; _btnWrite.FlatAppearance.BorderColor = DANGER; buttonPanel.Controls.Add(_btnWrite);
            layout.Controls.Add(buttonPanel, 0, 5); Controls.Add(layout); AcceptButton = _btnWrite; CancelButton = btnCancel;
        }
        private void UpdateBtn(object? s, EventArgs e) => _btnWrite.Enabled = _chk1.Checked && _chk2.Checked && _chk3.Checked;
    }
}
