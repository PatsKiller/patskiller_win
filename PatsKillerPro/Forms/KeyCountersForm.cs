using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.Communication;
using PatsKillerPro.Services;
using PatsKillerPro.Utils;
using PatsKillerPro.Vehicle;

namespace PatsKillerPro.Forms
{
    /// <summary>
    /// Key Counters Form - Read/Write Min/Max key counter values
    /// Token Cost: Read = FREE, Write Min/Max/Both = 1 TOKEN (single BCM session)
    /// </summary>
    public class KeyCountersForm : Form
    {
        private readonly Color BG = Color.FromArgb(26, 26, 30), SURFACE = Color.FromArgb(35, 35, 40), CARD = Color.FromArgb(42, 42, 48);
        private readonly Color BORDER = Color.FromArgb(58, 58, 66), TEXT = Color.FromArgb(240, 240, 240), TEXT_DIM = Color.FromArgb(160, 160, 165);
        private readonly Color ACCENT = Color.FromArgb(59, 130, 246), SUCCESS = Color.FromArgb(34, 197, 94), WARNING = Color.FromArgb(234, 179, 8), DANGER = Color.FromArgb(239, 68, 68), BTN_BG = Color.FromArgb(54, 54, 64);

        private const ushort DID_MIN_KEY_COUNTER = 0x5B13;
        private const ushort DID_MAX_KEY_COUNTER = 0x5B14;

        private NumericUpDown _numMin = null!, _numMax = null!;
        private Label _lblCurrentMin = null!, _lblCurrentMax = null!, _lblStatus = null!;
        private Button _btnRead = null!, _btnWriteMin = null!, _btnWriteMax = null!, _btnWriteBoth = null!;
        private RichTextBox _txtLog = null!;
        private ToolTip _toolTip = null!;

        private readonly UdsService _uds;
        private readonly string _vin;
        private int _originalMin = -1, _originalMax = -1;

        public KeyCountersForm(UdsService uds, string vin)
        {
            _uds = uds ?? throw new ArgumentNullException(nameof(uds));
            _vin = vin ?? "Unknown";
            InitializeComponent();
            BuildUI();
        }

        private void InitializeComponent()
        {
            Text = "Key Counters"; Size = new Size(650, 620); MinimumSize = new Size(600, 580);
            StartPosition = FormStartPosition.CenterParent; BackColor = BG; ForeColor = TEXT; Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; ShowInTaskbar = false;
            _toolTip = ToolTipHelper.CreateToolTip();
        }

        private void BuildUI()
        {
            var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = BG, Padding = new Padding(20) };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));   // Instructions
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));  // Counters
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // Buttons
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Log

            // Header
            var header = new Panel { Dock = DockStyle.Fill, BackColor = CARD, Padding = new Padding(15, 10, 15, 10) };
            header.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, header.Width - 1, header.Height - 1); };
            var headerFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            headerFlow.Controls.Add(new Label { Text = "🔢 KEY COUNTERS", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = ACCENT, AutoSize = true, Margin = new Padding(0, 8, 20, 0) });
            _lblStatus = new Label { Text = "Ready", Font = new Font("Segoe UI", 10), ForeColor = SUCCESS, AutoSize = true, Margin = new Padding(20, 12, 0, 0) };
            headerFlow.Controls.Add(_lblStatus);
            header.Controls.Add(headerFlow);
            mainLayout.Controls.Add(header, 0, 0);

            // Instructions Panel
            var instructionPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 45, 60), Padding = new Padding(12), Margin = new Padding(0, 8, 0, 8) };
            instructionPanel.Paint += (s, e) => { using var p = new Pen(ACCENT, 1); e.Graphics.DrawRectangle(p, 0, 0, instructionPanel.Width - 1, instructionPanel.Height - 1); };
            var instructionLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = TEXT_DIM,
                Font = new Font("Segoe UI", 9),
                Text = "ℹ️ KEY COUNTER SETTINGS\n" +
                       "• Min Counter (0x5B13): Minimum keys required for vehicle start. Typical: 2\n" +
                       "• Max Counter (0x5B14): Maximum keys that can be programmed. Typical: 8\n" +
                       "• All operations are FREE (tokens only charged for incode conversions)"
            };
            instructionPanel.Controls.Add(instructionLabel);
            mainLayout.Controls.Add(instructionPanel, 0, 1);

            // Counter Panel
            var counterPanel = new Panel { Dock = DockStyle.Fill, BackColor = SURFACE, Padding = new Padding(20), Margin = new Padding(0, 5, 0, 5) };
            counterPanel.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, counterPanel.Width - 1, counterPanel.Height - 1); };
            var counterLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Color.Transparent };
            counterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            counterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            counterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

            // Min Counter
            counterLayout.Controls.Add(new Label { Text = "Minimum Keys", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 10, 0, 5) }, 0, 0);
            _lblCurrentMin = new Label { Text = "Current: --", ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 5, 0, 10) };
            counterLayout.Controls.Add(_lblCurrentMin, 0, 1);
            _numMin = new NumericUpDown { Minimum = 0, Maximum = 8, Value = 2, Width = 100, Font = new Font("Segoe UI", 14, FontStyle.Bold), BackColor = Color.FromArgb(45, 45, 55), ForeColor = TEXT };
            counterLayout.Controls.Add(_numMin, 0, 2);

            // Max Counter
            counterLayout.Controls.Add(new Label { Text = "Maximum Keys", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 10, 0, 5) }, 1, 0);
            _lblCurrentMax = new Label { Text = "Current: --", ForeColor = TEXT_DIM, AutoSize = true, Margin = new Padding(0, 5, 0, 10) };
            counterLayout.Controls.Add(_lblCurrentMax, 1, 1);
            _numMax = new NumericUpDown { Minimum = 0, Maximum = 8, Value = 8, Width = 100, Font = new Font("Segoe UI", 14, FontStyle.Bold), BackColor = Color.FromArgb(45, 45, 55), ForeColor = TEXT };
            counterLayout.Controls.Add(_numMax, 1, 2);

            // Info Panel
            var infoPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 40, 50), Margin = new Padding(10, 0, 0, 0) };
            infoPanel.Paint += (s, e) => { using var p = new Pen(ACCENT, 1); e.Graphics.DrawRectangle(p, 0, 0, infoPanel.Width - 1, infoPanel.Height - 1); };
            infoPanel.Controls.Add(new Label { Text = "📋 DIDs:\n0x5B13 = Min\n0x5B14 = Max\n\n✓ Min ≤ Max\n✓ Valid: 0-8", ForeColor = TEXT_DIM, Dock = DockStyle.Fill, Padding = new Padding(10), Font = new Font("Segoe UI", 9) });
            counterLayout.Controls.Add(infoPanel, 2, 0);
            counterLayout.SetRowSpan(infoPanel, 3);
            counterPanel.Controls.Add(counterLayout);
            mainLayout.Controls.Add(counterPanel, 0, 2);

            // Buttons with tooltips
            var buttonBar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 5, 0, 5) };
            _btnRead = CreateButton("📖 Read Counters", ACCENT); _btnRead.Click += BtnRead_Click;
            _toolTip.SetToolTip(_btnRead, ToolTipHelper.GetToolTip("KeyCountersRead"));
            buttonBar.Controls.Add(_btnRead);

            buttonBar.Controls.Add(new Label { Text = "│", ForeColor = BORDER, AutoSize = true, Margin = new Padding(10, 12, 10, 0) });

            _btnWriteMin = CreateButton("💾 Write Min", WARNING); _btnWriteMin.Click += BtnWriteMin_Click;
            _toolTip.SetToolTip(_btnWriteMin, "Write minimum key counter value\n[FREE]");
            buttonBar.Controls.Add(_btnWriteMin);

            _btnWriteMax = CreateButton("💾 Write Max", WARNING); _btnWriteMax.Click += BtnWriteMax_Click;
            _toolTip.SetToolTip(_btnWriteMax, "Write maximum key counter value\n[FREE]");
            buttonBar.Controls.Add(_btnWriteMax);

            _btnWriteBoth = CreateButton("💾 Write Both", SUCCESS); _btnWriteBoth.Click += BtnWriteBoth_Click;
            _toolTip.SetToolTip(_btnWriteBoth, "Write BOTH Min and Max counters\nSingle BCM unlock session\n[FREE]");
            buttonBar.Controls.Add(_btnWriteBoth);

            mainLayout.Controls.Add(buttonBar, 0, 3);

            // Log
            var logPanel = new Panel { Dock = DockStyle.Fill, BackColor = SURFACE, Padding = new Padding(10) };
            logPanel.Paint += (s, e) => { using var p = new Pen(BORDER); e.Graphics.DrawRectangle(p, 0, 0, logPanel.Width - 1, logPanel.Height - 1); };
            _txtLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 35), ForeColor = TEXT_DIM, Font = new Font("Consolas", 9), ReadOnly = true, BorderStyle = BorderStyle.None };
            logPanel.Controls.Add(_txtLog);
            mainLayout.Controls.Add(logPanel, 0, 4);

            Controls.Add(mainLayout);
        }

        private Button CreateButton(string text, Color bgColor)
        {
            var btn = new Button { Text = text, AutoSize = true, MinimumSize = new Size(120, 40), Padding = new Padding(10, 5, 10, 5), FlatStyle = FlatStyle.Flat, BackColor = bgColor, ForeColor = TEXT, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
            btn.FlatAppearance.BorderColor = BORDER; return btn;
        }

        private void Log(string msg, Color? color = null) { if (_txtLog.InvokeRequired) { _txtLog.Invoke(new Action(() => Log(msg, color))); return; } _txtLog.SelectionStart = _txtLog.TextLength; _txtLog.SelectionColor = color ?? TEXT_DIM; _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n"); _txtLog.ScrollToCaret(); }
        private void SetStatus(string text, Color color) { if (_lblStatus.InvokeRequired) { _lblStatus.Invoke(new Action(() => SetStatus(text, color))); return; } _lblStatus.Text = text; _lblStatus.ForeColor = color; }

        private async void BtnRead_Click(object? sender, EventArgs e)
        {
            SetStatus("Reading...", WARNING); _btnRead.Enabled = false;
            try
            {
                await Task.Run(() =>
                {
                    Log("=== Reading Key Counters ===", ACCENT);
                    _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                    System.Threading.Thread.Sleep(50);

                    var minData = _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, DID_MIN_KEY_COUNTER);
                    if (minData != null && minData.Length > 0)
                    {
                        _originalMin = minData[0];
                        Invoke(new Action(() => { _lblCurrentMin.Text = $"Current: {_originalMin}"; _numMin.Value = _originalMin; }));
                        Log($"  ✓ Min Counter: {_originalMin}", SUCCESS);
                    }
                    else Log("  ✗ Min Counter: No response", DANGER);

                    System.Threading.Thread.Sleep(50);
                    var maxData = _uds.ReadDataByIdentifier(ModuleAddresses.BCM_TX, DID_MAX_KEY_COUNTER);
                    if (maxData != null && maxData.Length > 0)
                    {
                        _originalMax = maxData[0];
                        Invoke(new Action(() => { _lblCurrentMax.Text = $"Current: {_originalMax}"; _numMax.Value = _originalMax; }));
                        Log($"  ✓ Max Counter: {_originalMax}", SUCCESS);
                    }
                    else Log("  ✗ Max Counter: No response", DANGER);

                    Log("=== Read Complete (FREE) ===", SUCCESS);
                });
                SetStatus("Read complete", SUCCESS);
            }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Read failed", DANGER); }
            finally { _btnRead.Enabled = true; }
        }

        private async void BtnWriteMin_Click(object? sender, EventArgs e) => await WriteSingleCounter("Min", DID_MIN_KEY_COUNTER, (int)_numMin.Value);
        private async void BtnWriteMax_Click(object? sender, EventArgs e) => await WriteSingleCounter("Max", DID_MAX_KEY_COUNTER, (int)_numMax.Value);

        private async Task WriteSingleCounter(string name, ushort did, int value)
        {
            if (name == "Min" && value > _numMax.Value) { MessageBox.Show("Min cannot be greater than Max.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (name == "Max" && value < _numMin.Value) { MessageBox.Show("Max cannot be less than Min.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            using var confirm = new KeyCounterWriteConfirmationForm(name == "Min" ? value : -1, name == "Max" ? value : -1, false);
            if (confirm.ShowDialog(this) != DialogResult.OK) return;

            SetStatus($"Writing {name}...", WARNING);
            var btn = name == "Min" ? _btnWriteMin : _btnWriteMax; btn.Enabled = false;
            try
            {
                await Task.Run(() =>
                {
                    Log($"=== Writing {name} Counter ===", WARNING);

                    _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                    System.Threading.Thread.Sleep(50);
                    if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX)) { Log("  ✗ Security access denied", DANGER); return; }
                    System.Threading.Thread.Sleep(50);

                    var success = _uds.WriteDataByIdentifier(ModuleAddresses.BCM_TX, did, new byte[] { (byte)value });
                    if (success)
                    {
                        Log($"  ✓ {name} Counter = {value} (FREE)", SUCCESS);
                        ProActivityLogger.Instance.LogActivity(new ActivityLogEntry { Action = $"key_counter_{name.ToLower()}", ActionCategory = "key_counters", Vin = _vin, Success = true, TokenChange = 0, Details = $"{name} counter set to {value}", Metadata = new { value } });
                        if (name == "Min") Invoke(new Action(() => _lblCurrentMin.Text = $"Current: {value}"));
                        else Invoke(new Action(() => _lblCurrentMax.Text = $"Current: {value}"));
                    }
                    else Log($"  ✗ {name} write failed", DANGER);
                });
                SetStatus($"{name} written", SUCCESS);
            }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Write failed", DANGER); }
            finally { btn.Enabled = true; }
        }

        /// <summary>
        /// Write Both - FREE (no token charge for utility operations)
        /// </summary>
        private async void BtnWriteBoth_Click(object? sender, EventArgs e)
        {
            int minVal = (int)_numMin.Value, maxVal = (int)_numMax.Value;
            if (minVal > maxVal) { MessageBox.Show("Min cannot be greater than Max.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            using var confirm = new KeyCounterWriteConfirmationForm(minVal, maxVal, true);
            if (confirm.ShowDialog(this) != DialogResult.OK) return;

            SetStatus("Writing both...", WARNING); _btnWriteBoth.Enabled = false; _btnWriteMin.Enabled = false; _btnWriteMax.Enabled = false;
            try
            {
                await Task.Run(() =>
                {
                    Log("=== Writing Both Counters (Single Session) ===", WARNING);

                    // Single BCM unlock session
                    _uds.StartExtendedSession(ModuleAddresses.BCM_TX);
                    System.Threading.Thread.Sleep(50);
                    if (!_uds.RequestSecurityAccess(ModuleAddresses.BCM_TX)) { Log("  ✗ Security access denied", DANGER); return; }
                    Log("  ✓ BCM unlocked", SUCCESS);
                    System.Threading.Thread.Sleep(50);

                    // Write Min
                    var minSuccess = _uds.WriteDataByIdentifier(ModuleAddresses.BCM_TX, DID_MIN_KEY_COUNTER, new byte[] { (byte)minVal });
                    if (minSuccess)
                    {
                        Log($"  ✓ Min Counter = {minVal}", SUCCESS);
                        Invoke(new Action(() => _lblCurrentMin.Text = $"Current: {minVal}"));
                    }
                    else { Log($"  ✗ Min write failed", DANGER); }

                    System.Threading.Thread.Sleep(50);

                    // Write Max (same session)
                    var maxSuccess = _uds.WriteDataByIdentifier(ModuleAddresses.BCM_TX, DID_MAX_KEY_COUNTER, new byte[] { (byte)maxVal });
                    if (maxSuccess)
                    {
                        Log($"  ✓ Max Counter = {maxVal}", SUCCESS);
                        Invoke(new Action(() => _lblCurrentMax.Text = $"Current: {maxVal}"));
                    }
                    else { Log($"  ✗ Max write failed", DANGER); }

                    if (minSuccess && maxSuccess)
                    {
                        Log("=== Both Written Successfully (FREE) ===", SUCCESS);
                        ProActivityLogger.Instance.LogActivity(new ActivityLogEntry { Action = "key_counter_both", ActionCategory = "key_counters", Vin = _vin, Success = true, TokenChange = 0, Details = $"Min={minVal}, Max={maxVal}", Metadata = new { min = minVal, max = maxVal } });
                    }
                });
                SetStatus("Both written", SUCCESS);
            }
            catch (Exception ex) { Log($"Error: {ex.Message}", DANGER); SetStatus("Write failed", DANGER); }
            finally { _btnWriteBoth.Enabled = true; _btnWriteMin.Enabled = true; _btnWriteMax.Enabled = true; }
        }
    }

    public class KeyCounterWriteConfirmationForm : Form
    {
        private readonly Color BG = Color.FromArgb(26, 26, 30), CARD = Color.FromArgb(42, 42, 48), BORDER = Color.FromArgb(58, 58, 66), TEXT = Color.FromArgb(240, 240, 240), WARNING = Color.FromArgb(234, 179, 8), DANGER = Color.FromArgb(239, 68, 68);
        private CheckBox _chk1 = null!, _chk2 = null!, _chk3 = null!;
        private Button _btnWrite = null!;
        private readonly int _minValue, _maxValue;
        private readonly bool _writeBoth;

        public KeyCounterWriteConfirmationForm(int minValue, int maxValue, bool writeBoth)
        {
            _minValue = minValue; _maxValue = maxValue; _writeBoth = writeBoth;
            InitUI();
        }

        private void InitUI()
        {
            Text = "Confirm Write"; Size = new Size(450, 320); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            BackColor = BG; ForeColor = TEXT; Font = new Font("Segoe UI", 10F);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(20) };

            var warnPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(60, 40, 20), Height = 50 };
            warnPanel.Paint += (s, e) => { using var p = new Pen(WARNING, 2); e.Graphics.DrawRectangle(p, 1, 1, warnPanel.Width - 3, warnPanel.Height - 3); };
            var msg = _writeBoth ? $"Writing Min={_minValue}, Max={_maxValue}" : (_minValue >= 0 ? $"Writing Min={_minValue}" : $"Writing Max={_maxValue}");
            warnPanel.Controls.Add(new Label { Text = $"⚠️ {msg}", ForeColor = WARNING, Font = new Font("Segoe UI", 11, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
            layout.Controls.Add(warnPanel, 0, 0);

            // All utility operations are FREE
            layout.Controls.Add(new Label { Text = "Token Cost: FREE", ForeColor = Color.FromArgb(34, 197, 94), Font = new Font("Segoe UI", 11), AutoSize = true, Margin = new Padding(0, 15, 0, 15) }, 0, 1);

            _chk1 = new CheckBox { Text = "I understand this affects vehicle security", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 5, 0, 5) }; _chk1.CheckedChanged += UpdateBtn; layout.Controls.Add(_chk1, 0, 2);
            _chk2 = new CheckBox { Text = "I have read the current counter values", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 5, 0, 5) }; _chk2.CheckedChanged += UpdateBtn; layout.Controls.Add(_chk2, 0, 3);
            _chk3 = new CheckBox { Text = "I accept responsibility for this change", ForeColor = TEXT, AutoSize = true, Margin = new Padding(0, 5, 0, 5) }; _chk3.CheckedChanged += UpdateBtn; layout.Controls.Add(_chk3, 0, 4);

            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 15, 0, 0) };
            var btnCancel = new Button { Text = "Cancel", Size = new Size(100, 40), FlatStyle = FlatStyle.Flat, BackColor = CARD, ForeColor = TEXT, DialogResult = DialogResult.Cancel }; btnCancel.FlatAppearance.BorderColor = BORDER; buttonPanel.Controls.Add(btnCancel);
            _btnWrite = new Button { Text = "✓ WRITE", Size = new Size(120, 40), FlatStyle = FlatStyle.Flat, BackColor = DANGER, ForeColor = TEXT, Font = new Font("Segoe UI", 10, FontStyle.Bold), Enabled = false, DialogResult = DialogResult.OK, Margin = new Padding(0, 0, 10, 0) }; _btnWrite.FlatAppearance.BorderColor = DANGER; buttonPanel.Controls.Add(_btnWrite);
            layout.Controls.Add(buttonPanel, 0, 5);

            Controls.Add(layout);
            AcceptButton = _btnWrite; CancelButton = btnCancel;
        }

        private void UpdateBtn(object? s, EventArgs e) => _btnWrite.Enabled = _chk1.Checked && _chk2.Checked && _chk3.Checked;
    }
}
