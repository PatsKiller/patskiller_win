using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PatsKillerPro.J2534;

namespace PatsKillerPro.Forms
{
    /// <summary>
    /// Operator-safe device selection dialog.
    /// Performs a "full probe" per device: open/connect + read VBATT + attempt VIN read.
    /// Ready = device can connect (VIN may be unavailable).
    /// </summary>
    public sealed class DeviceSelectForm : Form
    {
        public sealed class ProbeSelection
        {
            public J2534DeviceInfo Device { get; init; } = null!;
            public string? Vin { get; init; }
            public double Voltage { get; init; }
            public bool Connected { get; init; }
            public string Status { get; init; } = "";
        }

        private sealed class ProbeRow
        {
            public J2534DeviceInfo Device { get; init; } = null!;
            public string DeviceName => Device.Name;
            public string Vendor => Device.Vendor;
            public string Voltage { get; set; } = "—";
            public string Vin { get; set; } = "—";
            public string Status { get; set; } = "Pending";
            public bool Ready { get; set; }
        }

        private readonly TimeSpan _perDeviceTimeout;
        private readonly BindingList<ProbeRow> _rows;
        private readonly DataGridView _grid;
        private readonly Button _btnConnect;
        private readonly Button _btnCancel;
        private readonly Button _btnReprobe;
        private readonly Label _lblProgress;
        private CancellationTokenSource? _cts;

        public ProbeSelection? Selection { get; private set; }

        public DeviceSelectForm(List<J2534DeviceInfo> devices, TimeSpan perDeviceTimeout)
        {
            _perDeviceTimeout = perDeviceTimeout;
            _rows = new BindingList<ProbeRow>(devices.Select(d => new ProbeRow { Device = d }).ToList());

            Text = "Select J2534 Device";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 860;
            Height = 520;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var header = new Label
            {
                Text = "We will probe each device (connect + read voltage + attempt VIN).\n" +
                       "Ready devices can be selected even if VIN is unavailable.",
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 220)
            };
            root.Controls.Add(header, 0, 0);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                DataSource = _rows,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                BackgroundColor = Color.FromArgb(28, 32, 40),
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Color.FromArgb(50, 55, 65)
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(36, 40, 50);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            _grid.EnableHeadersVisualStyles = false;
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(28, 32, 40);
            _grid.DefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(60, 80, 120);
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;
            _grid.RowHeadersVisible = false;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProbeRow.DeviceName), HeaderText = "Device", Width = 260 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProbeRow.Vendor), HeaderText = "Vendor", Width = 160 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProbeRow.Voltage), HeaderText = "VBATT", Width = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProbeRow.Vin), HeaderText = "VIN", Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProbeRow.Status), HeaderText = "Status", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            _grid.CellDoubleClick += (_, __) => TryCommitSelection();
            _grid.SelectionChanged += (_, __) => UpdateConnectButtonState();

            root.Controls.Add(_grid, 0, 1);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(footer, 0, 2);

            _lblProgress = new Label
            {
                Text = "Probing devices…",
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 200, 200),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 0, 0)
            };
            footer.Controls.Add(_lblProgress, 0, 0);

            var btnFlow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Anchor = AnchorStyles.Right
            };
            footer.Controls.Add(btnFlow, 1, 0);
            footer.SetColumnSpan(btnFlow, 2);

            _btnReprobe = new Button
            {
                Text = "Re-probe",
                AutoSize = true,
                Padding = new Padding(12, 6, 12, 6),
                BackColor = Color.FromArgb(45, 55, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnReprobe.FlatAppearance.BorderColor = Color.FromArgb(70, 80, 95);
            _btnReprobe.Click += async (_, __) => await StartProbeAsync();
            btnFlow.Controls.Add(_btnReprobe);

            _btnCancel = new Button
            {
                Text = "Cancel",
                AutoSize = true,
                Padding = new Padding(12, 6, 12, 6),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(85, 85, 85);
            _btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
            btnFlow.Controls.Add(_btnCancel);

            _btnConnect = new Button
            {
                Text = "Connect",
                AutoSize = true,
                Padding = new Padding(12, 6, 12, 6),
                BackColor = Color.FromArgb(35, 135, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            _btnConnect.FlatAppearance.BorderColor = Color.FromArgb(40, 160, 90);
            _btnConnect.Click += (_, __) => TryCommitSelection();
            btnFlow.Controls.Add(_btnConnect);

            Shown += async (_, __) => await StartProbeAsync();
            FormClosing += (_, __) => { try { _cts?.Cancel(); } catch { } };
        }

        private void UpdateConnectButtonState()
        {
            if (_grid.CurrentRow?.DataBoundItem is not ProbeRow row)
            {
                _btnConnect.Enabled = false;
                return;
            }

            // Allow selection even if not ready (operator override), but hint with disabled style.
            _btnConnect.Enabled = row.Device != null;
        }

        private void TryCommitSelection()
        {
            if (_grid.CurrentRow?.DataBoundItem is not ProbeRow row)
                return;

            Selection = new ProbeSelection
            {
                Device = row.Device,
                Vin = string.IsNullOrWhiteSpace(row.Vin) || row.Vin == "—" ? null : row.Vin,
                Voltage = double.TryParse(row.Voltage.Replace("V", "").Trim(), out var v) ? v : 0,
                Connected = row.Ready,
                Status = row.Status
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        private async Task StartProbeAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Reset UI
            foreach (var r in _rows)
            {
                r.Voltage = "—";
                r.Vin = "—";
                r.Status = "Pending";
                r.Ready = false;
            }
            _grid.Refresh();

            _btnReprobe.Enabled = false;
            _btnConnect.Enabled = false;
            _lblProgress.Text = "Probing devices…";

            try
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var row = _rows[i];
                    row.Status = $"Probing ({i + 1}/{_rows.Count})…";
                    _grid.Refresh();

                    var probe = await ProbeDeviceAsync(row.Device, token);

                    row.Ready = probe.Connected;
                    row.Status = probe.Status;
                    row.Voltage = probe.Voltage > 0 ? $"{probe.Voltage:0.0}V" : "—";
                    row.Vin = !string.IsNullOrWhiteSpace(probe.Vin) ? probe.Vin : "VIN unavailable";
                    _grid.Refresh();
                }

                // Auto-select first Ready
                var firstReady = _rows.Select((r, idx) => new { r, idx }).FirstOrDefault(x => x.r.Ready);
                if (firstReady != null)
                {
                    _grid.ClearSelection();
                    _grid.Rows[firstReady.idx].Selected = true;
                    _grid.CurrentCell = _grid.Rows[firstReady.idx].Cells[0];
                }
                else if (_grid.Rows.Count > 0)
                {
                    _grid.Rows[0].Selected = true;
                    _grid.CurrentCell = _grid.Rows[0].Cells[0];
                }

                _lblProgress.Text = "Probe complete. Select a device and click Connect.";
            }
            catch (OperationCanceledException)
            {
                _lblProgress.Text = "Probe canceled.";
            }
            finally
            {
                _btnReprobe.Enabled = true;
                UpdateConnectButtonState();
            }
        }

        private async Task<(bool Connected, double Voltage, string? Vin, string Status)> ProbeDeviceAsync(J2534DeviceInfo device, CancellationToken token)
        {
            // Full probe: Connect + Read VBATT + attempt VIN
            var timeoutCts = new CancellationTokenSource(_perDeviceTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            try
            {
                return await Task.Run(async () =>
                {
                    using var api = new J2534Api(device.FunctionLibrary);
                    var pats = new FordPatsService(api);

                    try
                    {
                        var ok = await pats.ConnectAsync().ConfigureAwait(false);
                        if (!ok)
                            return (false, 0d, (string?)null, "Not responding");

                        // Read VIN + VBATT (VIN may be unavailable; still treat as Ready)
                        try { await pats.ReadVehicleInfoAsync().ConfigureAwait(false); } catch { /* ignore */ }
                        var vin = pats.CurrentVin;
                        var vbatt = pats.BatteryVoltage;

                        var status = string.IsNullOrWhiteSpace(vin)
                            ? (vbatt > 0 ? "Ready (VIN unavailable)" : "Ready (no vehicle response)")
                            : "Ready";

                        return (true, vbatt, vin, status);
                    }
                    finally
                    {
                        try { pats.Disconnect(); } catch { }
                    }
                }, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (timeoutCts.IsCancellationRequested)
                    return (false, 0d, null, "Timeout");
                return (false, 0d, null, "Canceled");
            }
            catch (Exception ex)
            {
                return (false, 0d, null, $"Error: {ex.Message}");
            }
        }
    }
}
