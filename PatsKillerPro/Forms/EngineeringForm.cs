using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Communication;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;
using PatsKillerPro.Vehicle;

namespace PatsKillerPro.Forms
{
    /// <summary>
    /// Engineering Mode Form - Extended diagnostic operations
    /// Token Cost: Read = FREE, Write = 1 TOKEN, Routine = 1 TOKEN
    /// </summary>
    public class EngineeringForm : Form
    {
        private readonly Color BG = Color.FromArgb(26, 26, 30), SURFACE = Color.FromArgb(35, 35, 40), CARD = Color.FromArgb(42, 42, 48);
        private readonly Color BORDER = Color.FromArgb(58, 58, 66), TEXT = Color.FromArgb(240, 240, 240), TEXT_DIM = Color.FromArgb(160, 160, 165);
        private readonly Color ACCENT = Color.FromArgb(59, 130, 246), SUCCESS = Color.FromArgb(34, 197, 94), WARNING = Color.FromArgb(234, 179, 8), DANGER = Color.FromArgb(239, 68, 68), BTN_BG = Color.FromArgb(54, 54, 64);

        private readonly Dictionary<string, (string Name, ushort DID)> _commonDids = new() {
            ["VIN"] = ("VIN", 0xF190), ["ECU_Serial"] = ("ECU Serial", 0xF18C), ["Software_Version"] = ("Software Version", 0xF195),
            ["PATS_Status"] = ("PATS Status", 0xC126), ["Key_Count"] = ("Key Count", 0xC125), ["Outcode"] = ("PATS Outcode", 0xC123),
            ["Target_Block"] = ("Target Block", 0xC00C), ["Min_Key_Counter"] = ("Min Key Counter", 0x5B13), ["Max_Key_Counter"] = ("Max Key Counter", 0x5B14), ["Crash_Event"] = ("Crash Event", 0x5B17),
        };
        private readonly Dictionary<string, (string Name, ushort RoutineId)> _commonRoutines = new() {
            ["PATS_Incode"] = ("PATS Incode Submit", 0x716D), ["WKIP"] = ("Write Key In Progress", 0x716C), ["ESCL_Init"] = ("ESCL Initialize", 0xF110), ["KAM_Clear"] = ("KAM Clear", 0x0201),
        };

        private TabControl _tabControl = null!;
        private ComboBox _cmbModule = null!, _cmbDid = null!, _cmbRoutine = null!;
        private TextBox _txtDid = null!, _txtDidValue = null!, _txtRoutineId = null!, _txtRoutineData = null!;
        private TextBox _txtRawRequest = null!, _txtRawResponse = null!, _txtOutcode = null!, _txtIncode = null!;
        private Button _btnDidRead = null!, _btnDidWrite = null!, _btnRoutineStart = null!, _btnRoutineStop = null!;
        private Button _btnRawSend = null!, _btnReadOutcode = null!, _btnGetIncode = null!, _btnUnlock = null!;
        private Label _lblStatus = null!;
        private RichTextBox _txtLog = null!;

        private readonly UdsService _uds;
        private readonly string _vin;
        private uint _currentModuleAddr;

        public EngineeringForm(UdsService uds, string vin)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
            _vin = vin ?? "Unknown";
            _currentModuleAddr = ModuleAddresses.BCM_TX;
            InitializeComponent();
            BuildUI();
        }

        private void InitializeComponent()
        {
            Text = "Engineering Mode"; Size = new Size(950, 750); MinimumSize = new Size(900, 700);
            StartPosition = FormStartPosition.CenterParent; BackColor = BG; ForeColor = TEXT; Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true; ShowInTaskbar = false;
        }

        private void BuildUI()
        {
            var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = BG, Padding = new Padding(15) };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            var header = new Panel { Dock = DockStyle.Fill, BackColor = CARD, Padding = new Padding(15, 10, 15, 10) };
            header.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, header.Width - 1, header.Height - 1); };
            var headerFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            headerFlow.Controls.Add(new Label { Text = "🔧 ENGINEERING MODE", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = ACCENT, AutoSize = true, Margin = new Padding(0, 8, 20, 0) });
            headerFlow.Controls.Add(new Label { Text = "Module:", ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 12, 5, 0) });
            _cmbModule = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SURFACE, ForeColor = TEXT, Margin = new Padding(0, 8, 20, 0) };
            _cmbModule.Items.AddRange(new[] { "BCM", "PCM", "ABS", "RFA", "ESCL", "IPC", "GWM" }); _cmbModule.SelectedIndex = 0;
            _cmbModule.SelectedIndexChanged += (s, e) => UpdateModuleAddress();
            headerFlow.Controls.Add(_cmbModule);
            _lblStatus = new Label { Text = "Ready", Font = new Font("Segoe UI", 10), ForeColor = SUCCESS, AutoSize = true, Margin = new Padding(20, 12, 0, 0) };
            headerFlow.Controls.Add(_lblStatus);
            header.Controls.Add(headerFlow);
            mainLayout.Controls.Add(header, 0, 0);

            _tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10), Margin = new Padding(0, 10, 0, 10) };
            _tabControl.TabPages.Add(CreateDidTab());
            _tabControl.TabPages.Add(CreateRoutineTab());
            _tabControl.TabPages.Add(CreateRawUdsTab());
            _tabControl.TabPages.Add(CreatePatsUnlockTab());
            mainLayout.Controls.Add(_tabControl, 0, 1);

            var logPanel = new Panel { Dock = DockStyle.Fill, BackColor = SURFACE, Padding = new Padding(10) };
            logPanel.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, logPanel.Width - 1, logPanel.Height - 1); };
            _txtLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 35), ForeColor = TEXT_DIM, Font = new Font("Consolas", 9), ReadOnly = true, BorderStyle = BorderStyle.None };
            logPanel.Controls.Add(_txtLog);
            mainLayout.Controls.Add(logPanel, 0, 2);

            mainLayout.Controls.Add(new Label { Text = "⚠️ Engineering Mode - Incorrect usage may damage modules. Use with caution.", ForeColor = WARNING, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, 0, 3);
            Controls.Add(mainLayout);
        }

        private TabPage CreateDidTab()
        {
            var tab = new TabPage("DID Read/Write") { BackColor = SURFACE, Padding = new Padding(15) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            layout.Controls.Add(new Label { Text = "Common DIDs:", ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 0);
            _cmbDid = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SURFACE, ForeColor = TEXT, Margin = new Padding(0, 5, 0, 5) };
            _cmbDid.Items.Add("-- Select --"); foreach (var kv in _commonDids) _cmbDid.Items.Add($"{kv.Value.Name} (0x{kv.Value.DID:X4})");
            _cmbDid.SelectedIndex = 0; _cmbDid.SelectedIndexChanged += (s, e) => { if (_cmbDid.SelectedIndex > 0) _txtDid.Text = $"{_commonDids.Values.ToArray()[_cmbDid.SelectedIndex - 1].DID:X4}"; };
            layout.Controls.Add(_cmbDid, 1, 0);

            layout.Controls.Add(new Label { Text = "DID (Hex):", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 1);
            _txtDid = new TextBox { Width = 150, BackColor = Color.FromArgb(45, 45, 55), ForeColor = TEXT, Font = new Font("Consolas", 11), Text = "F190", Margin = new Padding(0, 5, 0, 5) };
            layout.Controls.Add(_txtDid, 1, 1);

            layout.Controls.Add(new Label { Text = "Value (Hex):", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 2);
            _txtDidValue = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 55), ForeColor = TEXT, Font = new Font("Consolas", 11), Margin = new Padding(0, 5, 10, 5) };
            layout.Controls.Add(_txtDidValue, 1, 2); layout.SetColumnSpan(_txtDidValue, 2);

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 15, 0, 0) };
            _btnDidRead = CreateButton("📖 Read DID", ACCENT); _btnDidRead.Click += BtnDidRead_Click; btnPanel.Controls.Add(_btnDidRead);
            _btnDidWrite = CreateButton("💾 Write DID", WARNING); _btnDidWrite.Click += BtnDidWrite_Click; btnPanel.Controls.Add(_btnDidWrite);
            layout.Controls.Add(btnPanel, 1, 3); layout.SetColumnSpan(btnPanel, 2);

            var infoPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 40, 50), Margin = new Padding(0, 10, 0, 0) };
            infoPanel.Paint += (s, e) => { using var p = new Pen(ACCENT, 1); e.Graphics.DrawRectangle(p, 0, 0, infoPanel.Width - 1, infoPanel.Height - 1); };
            infoPanel.Controls.Add(new Label { Text = "ℹ️ Read = FREE, Write = 1 TOKEN\nDID format: 4 hex digits (e.g., F190)", ForeColor = TEXT_DIM, Dock = DockStyle.Fill, Padding = new Padding(10) });
            layout.Controls.Add(infoPanel, 0, 4); layout.SetColumnSpan(infoPanel, 3);
            tab.Controls.Add(layout);
            return tab;
        }

        private TabPage CreateRoutineTab()
        {
            var tab = new TabPage("Routine Control") { BackColor = SURFACE, Padding = new Padding(15) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            layout.Controls.Add(new Label { Text = "Common:", ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 0);
            _cmbRoutine = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SURFACE, ForeColor = TEXT, Margin = new Padding(0, 5, 0, 5) };
            _cmbRoutine.Items.Add("-- Select --"); foreach (var kv in _commonRoutines) _cmbRoutine.Items.Add($"{kv.Value.Name} (0x{kv.Value.RoutineId:X4})");
            _cmbRoutine.SelectedIndex = 0; _cmbRoutine.SelectedIndexChanged += (s, e) => { if (_cmbRoutine.SelectedIndex > 0) _txtRoutineId.Text = $"{_commonRoutines.Values.ToArray()[_cmbRoutine.SelectedIndex - 1].RoutineId:X4}"; };
            layout.Controls.Add(_cmbRoutine, 1, 0);

            layout.Controls.Add(new Label { Text = "Routine ID:", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 1);
            _txtRoutineId = new TextBox { Width = 150, BackColor = Color.FromArgb(45, 45, 55), ForeColor = TEXT, Font = new Font("Consolas", 11), Text = "716D", Margin = new Padding(0, 5, 0, 5) };
            layout.Controls.Add(_txtRoutineId, 1, 1);

            layout.Controls.Add(new Label { Text = "Data (Hex):", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 2);
            _txtRoutineData = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 55), ForeColor = TEXT, Font = new Font("Consolas", 11), Margin = new Padding(0, 5, 10, 5) };
            layout.Controls.Add(_txtRoutineData, 1, 2); layout.SetColumnSpan(_txtRoutineData, 2);

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 15, 0, 0) };
            _btnRoutineStart = CreateButton("▶ Start (0x01)", SUCCESS); _btnRoutineStart.Click += async (s, e) => await ExecuteRoutine(0x01, "Start"); btnPanel.Controls.Add(_btnRoutineStart);
            _btnRoutineStop = CreateButton("⏹ Stop (0x02)", DANGER); _btnRoutineStop.Click += async (s, e) => await ExecuteRoutine(0x02, "Stop"); btnPanel.Controls.Add(_btnRoutineStop);
            var btnResults = CreateButton("📋 Results (0x03)", BTN_BG); btnResults.Click += async (s, e) => await ExecuteRoutine(0x03, "Results"); btnPanel.Controls.Add(btnResults);
            layout.Controls.Add(btnPanel, 1, 3); layout.SetColumnSpan(btnPanel, 2);

            var infoPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 40, 50), Margin = new Padding(0, 10, 0, 0) };
            infoPanel.Paint += (s, e) => { using var p = new Pen(ACCENT, 1); e.Graphics.DrawRectangle(p, 0, 0, infoPanel.Width - 1, infoPanel.Height - 1); };
            infoPanel.Controls.Add(new Label { Text = "ℹ️ Routine Start = 1 TOKEN\n0x01=Start, 0x02=Stop, 0x03=Results", ForeColor = TEXT_DIM, Dock = DockStyle.Fill, Padding = new Padding(10) });
            layout.Controls.Add(infoPanel, 0, 4); layout.SetColumnSpan(infoPanel, 3);
            tab.Controls.Add(layout);
            return tab;
        }

        private TabPage CreateRawUdsTab()
        {
            var tab = new TabPage("Raw UDS") { BackColor = SURFACE, Padding = new Padding(15) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            layout.Controls.Add(new Label { Text = "Request (Hex):", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 0);
            _txtRawRequest = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 55), ForeColor = TEXT, Font = new Font("Consolas", 11), Text = "22 F1 90", Margin = new Padding(0, 5, 0, 5) };
            layout.Controls.Add(_txtRawRequest, 1, 0);

            layout.Controls.Add(new Label { Text = "Response:", ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 1);
            _txtRawResponse = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 35, 40), ForeColor = SUCCESS, Font = new Font("Consolas", 11), ReadOnly = true, Margin = new Padding(0, 5, 0, 5), Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
            layout.Controls.Add(_txtRawResponse, 1, 1);

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 15, 0, 0) };
            _btnRawSend = CreateButton("📤 Send Raw", WARNING); _btnRawSend.Click += BtnRawSend_Click; btnPanel.Controls.Add(_btnRawSend);
            layout.Controls.Add(btnPanel, 1, 2);

            var warnPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(50, 30, 30), Margin = new Padding(0, 10, 0, 0) };
            warnPanel.Paint += (s, e) => { using var p = new Pen(DANGER, 2); e.Graphics.DrawRectangle(p, 1, 1, warnPanel.Width - 3, warnPanel.Height - 3); };
            warnPanel.Controls.Add(new Label { Text = "⚠️ RAW UDS MODE - Use with extreme caution!\nIncorrect commands can brick modules permanently.\n[1 TOKEN for write operations]", ForeColor = DANGER, Dock = DockStyle.Fill, Padding = new Padding(10), Font = new Font("Segoe UI", 9, FontStyle.Bold) });
            layout.Controls.Add(warnPanel, 0, 3); layout.SetColumnSpan(warnPanel, 2);
            tab.Controls.Add(layout);
            return tab;
        }

        private TabPage CreatePatsUnlockTab()
        {
            var tab = new TabPage("PATS Unlock Helper") { BackColor = SURFACE, Padding = new Padding(15) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

            layout.Controls.Add(new Label { Text = "Step 1:", ForeColor = ACCENT, AutoSize = true, Margin = new Padding(0, 10, 0, 0), Font = new Font("Segoe UI", 10, FontStyle.Bold) }, 0, 0);
            _btnReadOutcode = CreateButton("📖 Read Outcode", ACCENT); _btnReadOutcode.Click += BtnReadOutcode_Click; layout.Controls.Add(_btnReadOutcode, 1, 0);

            layout.Controls.Add(new Label { Text = "Outcode:", ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 1);
            _txtOutcode = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 35, 40), ForeColor = WARNING, Font = new Font("Consolas", 14, FontStyle.Bold), ReadOnly = true, Margin = new Padding(0, 5, 10, 5), TextAlign = HorizontalAlignment.Center };
            layout.Controls.Add(_txtOutcode, 1, 1);

            layout.Controls.Add(new Label { Text = "Step 2:", ForeColor = ACCENT, AutoSize = true, Margin = new Padding(0, 10, 0, 0), Font = new Font("Segoe UI", 10, FontStyle.Bold) }, 0, 2);
            _btnGetIncode = CreateButton("🔑 Get Incode", WARNING); _btnGetIncode.Click += BtnGetIncode_Click; layout.Controls.Add(_btnGetIncode, 1, 2);

            layout.Controls.Add(new Label { Text = "Incode:", ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, 0, 3);
            _txtIncode = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 35, 40), ForeColor = SUCCESS, Font = new Font("Consolas", 14, FontStyle.Bold), ReadOnly = true, Margin = new Padding(0, 5, 10, 5), TextAlign = HorizontalAlignment.Center };
            layout.Controls.Add(_txtIncode, 1, 3);

            layout.Controls.Add(new Label { Text = "Step 3:", ForeColor = ACCENT, AutoSize = true, Margin = new Padding(0, 10, 0, 0), Font = new Font("Segoe UI", 10, FontStyle.Bold) }, 0, 4);
            _btnUnlock = CreateButton("🔓 Unlock PATS", SUCCESS); _btnUnlock.Click += BtnUnlock_Click; layout.Controls.Add(_btnUnlock, 1, 4);

            tab.Controls.Add(layout);
            return tab;
        }

        private Button CreateButton(string text, Color bgColor)
        {
            var btn = new Button { Text = text, Size = new Size(150, 40), FlatStyle = FlatStyle.Flat, BackColor = bgColor, ForeColor = TEXT, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
            btn.FlatAppearance.BorderColor = BORDER; return btn;
        }

        private void Log(string msg, Color? color = null) { if (_txtLog.InvokeRequired) { _txtLog.Invoke(new Action(() => Log(msg, color))); return; } _txtLog.SelectionStart = _txtLog.TextLength; _txtLog.SelectionColor = color ?? TEXT_DIM; _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n"); _txtLog.ScrollToCaret(); }
        private void SetStatus(string text, Color color) { if (_lblStatus.InvokeRequired) { _lblStatus.Invoke(new Action(() => SetStatus(text, color))); return; } _lblStatus.Text = text; _lblStatus.ForeColor = color; }

        private void UpdateModuleAddress() { _currentModuleAddr = _cmbModule.SelectedItem?.ToString() switch { "BCM" => ModuleAddresses.BCM_TX, "PCM" => ModuleAddresses.PCM_TX, "ABS" => ModuleAddresses.ABS_TX, "RFA" => ModuleAddresses.RFA_TX, "ESCL" => ModuleAddresses.ESCL_TX, "IPC" => ModuleAddresses.IPC_TX, "GWM" => ModuleAddresses.GWM_TX, _ => ModuleAddresses.BCM_TX }; Log($"Module: {_cmbModule.SelectedItem} (0x{_currentModuleAddr:X3})"); }

        private async void BtnDidRead_Click(object? sender, EventArgs e)
        {
            if (!ushort.TryParse(_txtDid.Text, System.Globalization.NumberStyles.HexNumber, null, out ushort did)) { MessageBox.Show("Invalid DID format.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            SetStatus("Reading...", WARNING); _btnDidRead.Enabled = false;
            try { await Task.Run(() => { Log($"Reading DID 0x{did:X4}..."); _uds.StartExtendedSession(_currentModuleAddr); System.Threading.Thread.Sleep(50); var data = _uds.ReadDataByIdentifier(_currentModuleAddr, did); if (data != null && data.Length > 0) { var hex = BitConverter.ToString(data).Replace("-", " "); Invoke(new Action(() => _txtDidValue.Text = hex)); Log($"  ✓ Response: {hex}", SUCCESS); } else Log("  ✗ No response", DANGER); }); SetStatus("Read complete", SUCCESS); }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Read failed", DANGER); } finally { _btnDidRead.Enabled = true; }
        }

        private async void BtnDidWrite_Click(object? sender, EventArgs e)
        {
            if (!ushort.TryParse(_txtDid.Text, System.Globalization.NumberStyles.HexNumber, null, out ushort did)) { MessageBox.Show("Invalid DID.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            var bytes = ParseHexString(_txtDidValue.Text); if (bytes == null || bytes.Length == 0) { MessageBox.Show("Invalid value.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (MessageBox.Show($"Write {bytes.Length} bytes to DID 0x{did:X4}?\n\n⚠️ Cost: 1 TOKEN", "Confirm Write", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!TokenBalanceService.Instance.HasEnoughTokens(1)) { MessageBox.Show("Insufficient tokens.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SetStatus("Writing...", WARNING); _btnDidWrite.Enabled = false;
            try { await Task.Run(async () => { Log($"Writing DID 0x{did:X4}..."); var tr = await TokenBalanceService.Instance.DeductForUtilityAsync("engineering_did_write", _vin); if (!tr.Success) { Log($"Token deduction failed: {tr.Error}", DANGER); return; } _uds.StartExtendedSession(_currentModuleAddr); System.Threading.Thread.Sleep(50); if (!_uds.RequestSecurityAccess(_currentModuleAddr)) { Log("  ✗ Security denied", DANGER); return; } System.Threading.Thread.Sleep(50); var ok = _uds.WriteDataByIdentifier(_currentModuleAddr, did, bytes); if (ok) { Log("  ✓ Written (1 token)", SUCCESS); ProActivityLogger.Instance.LogActivity(new ActivityLogEntry { Action = "engineering_did_write", ActionCategory = "engineering", Vin = _vin, Success = true, TokenChange = -1, Details = $"DID 0x{did:X4} written", Metadata = new { did = $"0x{did:X4}" } }); } else Log("  ✗ Write failed", DANGER); }); SetStatus("Write complete", SUCCESS); }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Write failed", DANGER); } finally { _btnDidWrite.Enabled = true; }
        }

        private async Task ExecuteRoutine(byte subFunction, string action)
        {
            if (!ushort.TryParse(_txtRoutineId.Text, System.Globalization.NumberStyles.HexNumber, null, out ushort routineId)) { MessageBox.Show("Invalid Routine ID.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            var data = ParseHexString(_txtRoutineData.Text) ?? Array.Empty<byte>();
            if (subFunction == 0x01 && !TokenBalanceService.Instance.HasEnoughTokens(1)) { MessageBox.Show("Insufficient tokens.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SetStatus($"Routine {action}...", WARNING);
            try { await Task.Run(async () => { Log($"Routine {action} 0x{routineId:X4}..."); if (subFunction == 0x01) { var tr = await TokenBalanceService.Instance.DeductForUtilityAsync("engineering_routine", _vin); if (!tr.Success) { Log($"Token deduction failed: {tr.Error}", DANGER); return; } } _uds.StartExtendedSession(_currentModuleAddr); System.Threading.Thread.Sleep(50); var routineData = new byte[2 + data.Length]; routineData[0] = (byte)(routineId >> 8); routineData[1] = (byte)(routineId & 0xFF); Array.Copy(data, 0, routineData, 2, data.Length); var response = _uds.RoutineControl(_currentModuleAddr, subFunction, routineData); if (response != null && response.Length > 0) Log($"  ✓ Response: {BitConverter.ToString(response).Replace("-", " ")}", SUCCESS); else Log($"  ✓ Routine {action} sent", SUCCESS); }); SetStatus($"Routine {action} done", SUCCESS); }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Routine failed", DANGER); }
        }

        private async void BtnRawSend_Click(object? sender, EventArgs e)
        {
            var bytes = ParseHexString(_txtRawRequest.Text); if (bytes == null || bytes.Length == 0) { MessageBox.Show("Invalid request.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            bool isWrite = bytes[0] == 0x2E || bytes[0] == 0x31 || bytes[0] == 0x3E;
            if (isWrite) { if (MessageBox.Show($"Send {bytes.Length} bytes?\n\n⚠️ DANGER! Cost: 1 TOKEN", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; if (!TokenBalanceService.Instance.HasEnoughTokens(1)) { MessageBox.Show("Insufficient tokens.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; } }
            SetStatus("Sending...", WARNING); _btnRawSend.Enabled = false;
            try { await Task.Run(async () => { Log($"Sending: {BitConverter.ToString(bytes).Replace("-", " ")}"); if (isWrite) { var tr = await TokenBalanceService.Instance.DeductForUtilityAsync("engineering_raw", _vin); if (!tr.Success) { Log($"Token deduction failed: {tr.Error}", DANGER); return; } } _uds.StartExtendedSession(_currentModuleAddr); System.Threading.Thread.Sleep(50); var response = _uds.SendRawRequest(_currentModuleAddr, bytes); var responseHex = response != null ? BitConverter.ToString(response).Replace("-", " ") : "No response"; Invoke(new Action(() => _txtRawResponse.Text = responseHex)); Log($"  Response: {responseHex}", response != null ? SUCCESS : WARNING); }); SetStatus("Send complete", SUCCESS); }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Send failed", DANGER); _txtRawResponse.Text = $"Error: {ex.Message}"; } finally { _btnRawSend.Enabled = true; }
        }

        private async void BtnReadOutcode_Click(object? sender, EventArgs e)
        {
            SetStatus("Reading outcode...", WARNING); _btnReadOutcode.Enabled = false;
            try { await Task.Run(() => { Log("Reading PATS Outcode..."); _uds.StartExtendedSession(ModuleAddresses.BCM_TX); System.Threading.Thread.Sleep(50); var data = _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, 0xC123); if (data != null && data.Length >= 6) { var outcode = BitConverter.ToString(data, 0, 6).Replace("-", ""); Invoke(new Action(() => _txtOutcode.Text = outcode)); Log($"  ✓ Outcode: {outcode}", SUCCESS); } else Log("  ✗ No outcode", DANGER); }); SetStatus("Outcode read", SUCCESS); }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Read failed", DANGER); } finally { _btnReadOutcode.Enabled = true; }
        }

        private async void BtnGetIncode_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_txtOutcode.Text)) { MessageBox.Show("Read outcode first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SetStatus("Getting incode...", WARNING); _btnGetIncode.Enabled = false;
            try { Log($"Requesting incode for {_txtOutcode.Text}..."); var result = await IncodeService.Instance.CalculateIncodeAsync(_txtOutcode.Text, _vin, _cmbModule.SelectedItem?.ToString()); if (result.Success && !string.IsNullOrEmpty(result.Incode)) { _txtIncode.Text = result.Incode; Log($"  ✓ Incode: {result.Incode} (Provider: {result.ProviderUsed}, Tokens: {result.TokensCharged})", SUCCESS); } else Log($"  ✗ Failed: {result.Error}", DANGER); SetStatus("Incode received", SUCCESS); }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Failed", DANGER); } finally { _btnGetIncode.Enabled = true; }
        }

        private async void BtnUnlock_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_txtIncode.Text)) { MessageBox.Show("Get incode first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SetStatus("Unlocking...", WARNING); _btnUnlock.Enabled = false;
            try { await Task.Run(() => { Log($"Unlocking PATS with {_txtIncode.Text}..."); _uds.StartExtendedSession(ModuleAddresses.BCM_TX); System.Threading.Thread.Sleep(100); if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX)) { Log("  ✗ Security denied", DANGER); return; } System.Threading.Thread.Sleep(100); var incodeBytes = ParseIncodeToBytes(_txtIncode.Text); var routineData = new byte[3 + incodeBytes.Length]; routineData[0] = 0x71; routineData[1] = 0x6D; routineData[2] = 0xCA; Array.Copy(incodeBytes, 0, routineData, 3, incodeBytes.Length); _uds.RoutineControl(ModuleAddresses.BCM_TX, 0x01, routineData); System.Threading.Thread.Sleep(200); var status = _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, 0xC126); if (status != null && status.Length > 0 && status[0] == 0xAA) Log("  ✓ PATS Unlocked!", SUCCESS); else Log($"  ⚠️ Status: 0x{(status != null && status.Length > 0 ? status[0].ToString("X2") : "??")}", WARNING); }); SetStatus("Unlock complete", SUCCESS); }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Unlock failed", DANGER); } finally { _btnUnlock.Enabled = true; }
        }

        private byte[]? ParseHexString(string hex) { try { if (string.IsNullOrWhiteSpace(hex)) return null; hex = hex.Replace(" ", "").Replace("-", "").Trim(); if (hex.Length % 2 != 0) return null; var bytes = new byte[hex.Length / 2]; for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16); return bytes; } catch { return null; } }
        private byte[] ParseIncodeToBytes(string incode) { incode = incode.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant(); var bytes = new byte[incode.Length / 2]; for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(incode.Substring(i * 2, 2), 16); return bytes; }
    }
}
