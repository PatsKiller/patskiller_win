using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PatsKillerPro
{
    /// <summary>
    /// Full-screen operation progress splash screen with step tracking
    /// Shows during long operations like Program Key, Erase Keys, Parameter Reset
    /// </summary>
    public class OperationProgressForm : Form
    {
        // Theme colors (matching MainForm)
        private static readonly Color BG = Color.FromArgb(20, 20, 24);
        private static readonly Color CARD = Color.FromArgb(42, 42, 48);
        private static readonly Color BORDER = Color.FromArgb(58, 58, 66);
        private static readonly Color TEXT = Color.FromArgb(240, 240, 240);
        private static readonly Color TEXT_DIM = Color.FromArgb(160, 160, 165);
        private static readonly Color TEXT_MUTED = Color.FromArgb(112, 112, 117);
        private static readonly Color SUCCESS = Color.FromArgb(34, 197, 94);
        private static readonly Color WARNING = Color.FromArgb(234, 179, 8);
        private static readonly Color DANGER = Color.FromArgb(239, 68, 68);
        private static readonly Color ACCENT = Color.FromArgb(59, 130, 246);
        private static readonly Color BTN_BG = Color.FromArgb(54, 54, 64);

        // Controls
        private Panel _centerPanel = null!;
        private PictureBox _logo = null!;
        private Label _lblBrand = null!;
        private Label _lblOperation = null!;
        private Label _lblVehicle = null!;
        private Label _lblProgress = null!;
        private ProgressBar _progressBar = null!;
        private FlowLayoutPanel _stepsPanel = null!;
        private Label _lblInstruction = null!;
        private Label _lblTokenCost = null!;
        private Button _btnCancel = null!;

        // State
        private readonly List<StepInfo> _steps = new();
        private readonly List<Label> _stepLabels = new();
        private int _currentStepIndex = -1;
        private bool _isComplete = false;
        private bool _canCancel = true;
        private CancellationTokenSource? _cts;

        /// <summary>Step information</summary>
        public class StepInfo
        {
            public string Name { get; set; } = "";
            public StepStatus Status { get; set; } = StepStatus.Pending;
        }

        /// <summary>Step status</summary>
        public enum StepStatus { Pending, InProgress, Completed, Failed, Skipped }

        /// <summary>Whether user cancelled</summary>
        public bool WasCancelled { get; private set; }

        /// <summary>Cancellation token for async operations</summary>
        public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

        public OperationProgressForm()
        {
            _cts = new CancellationTokenSource();
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "PatsKiller Pro";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = BG;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;

            // Semi-transparent overlay
            this.Paint += (s, e) =>
            {
                using var brush = new SolidBrush(Color.FromArgb(200, BG));
                e.Graphics.FillRectangle(brush, ClientRectangle);
            };

            // Center panel (card)
            _centerPanel = new Panel
            {
                Size = new Size(550, 520),
                BackColor = CARD
            };
            _centerPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(BORDER, 2);
                e.Graphics.DrawRectangle(pen, 1, 1, _centerPanel.Width - 3, _centerPanel.Height - 3);
            };
            Controls.Add(_centerPanel);

            // Reposition on resize
            Resize += (s, e) => CenterPanel();
            Load += (s, e) => CenterPanel();

            int y = 25;

            // Logo
            _logo = new PictureBox
            {
                Size = new Size(64, 64),
                Location = new Point((_centerPanel.Width - 64) / 2, y),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            LoadLogo();
            _centerPanel.Controls.Add(_logo);
            y += 70;

            // Brand text
            _lblBrand = new Label
            {
                Text = "PATSKILLER PRO",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ACCENT,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y),
                Size = new Size(_centerPanel.Width, 35)
            };
            _centerPanel.Controls.Add(_lblBrand);
            y += 45;

            // Operation name
            _lblOperation = new Label
            {
                Text = "OPERATION",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = TEXT,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y),
                Size = new Size(_centerPanel.Width, 30)
            };
            _centerPanel.Controls.Add(_lblOperation);
            y += 30;

            // Vehicle info
            _lblVehicle = new Label
            {
                Text = "Vehicle",
                Font = new Font("Segoe UI", 10),
                ForeColor = TEXT_DIM,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y),
                Size = new Size(_centerPanel.Width, 22)
            };
            _centerPanel.Controls.Add(_lblVehicle);
            y += 30;

            // Progress percentage
            _lblProgress = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 32, FontStyle.Bold),
                ForeColor = ACCENT,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y),
                Size = new Size(_centerPanel.Width, 50)
            };
            _centerPanel.Controls.Add(_lblProgress);
            y += 50;

            // Progress bar
            _progressBar = new ProgressBar
            {
                Location = new Point(40, y),
                Size = new Size(_centerPanel.Width - 80, 22),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            _centerPanel.Controls.Add(_progressBar);
            y += 35;

            // Steps panel
            _stepsPanel = new FlowLayoutPanel
            {
                Location = new Point(60, y),
                Size = new Size(_centerPanel.Width - 120, 130),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.Transparent
            };
            _centerPanel.Controls.Add(_stepsPanel);
            y += 140;

            // Instruction label
            _lblInstruction = new Label
            {
                Text = "Please wait...",
                Font = new Font("Segoe UI", 10, FontStyle.Italic),
                ForeColor = WARNING,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y),
                Size = new Size(_centerPanel.Width, 25)
            };
            _centerPanel.Controls.Add(_lblInstruction);
            y += 30;

            // Token cost
            _lblTokenCost = new Label
            {
                Text = "Token cost: 0",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = TEXT_MUTED,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, y),
                Size = new Size(_centerPanel.Width, 20)
            };
            _centerPanel.Controls.Add(_lblTokenCost);
            y += 30;

            // Cancel button
            _btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(140, 40),
                Location = new Point((_centerPanel.Width - 140) / 2, y),
                BackColor = BTN_BG,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = BORDER;
            _btnCancel.Click += BtnCancel_Click;
            _centerPanel.Controls.Add(_btnCancel);
        }

        private void CenterPanel()
        {
            _centerPanel.Location = new Point(
                (ClientSize.Width - _centerPanel.Width) / 2,
                (ClientSize.Height - _centerPanel.Height) / 2
            );
        }

        private void LoadLogo()
        {
            try
            {
                // Try loading from embedded resource
                var asm = Assembly.GetExecutingAssembly();
                var resourceName = "PatsKillerPro.Resources.logo.png";
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    _logo.Image = Image.FromStream(stream);
                    return;
                }

                // Try loading from file
                var exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                var logoPath = Path.Combine(exePath, "Resources", "logo.png");
                if (File.Exists(logoPath))
                {
                    _logo.Image = Image.FromFile(logoPath);
                    return;
                }

                // Fallback: hide logo, show only text
                _logo.Visible = false;
            }
            catch
            {
                _logo.Visible = false;
            }
        }

        /// <summary>
        /// Configure the operation display
        /// </summary>
        public void Configure(string operationName, string vehicleInfo, int tokenCost, params string[] stepNames)
        {
            _lblOperation.Text = operationName.ToUpper();
            _lblVehicle.Text = vehicleInfo;
            _lblTokenCost.Text = tokenCost == 0 ? "Token cost: FREE" : $"Token cost: {tokenCost}";

            _steps.Clear();
            _stepLabels.Clear();
            _stepsPanel.Controls.Clear();

            foreach (var name in stepNames)
            {
                var step = new StepInfo { Name = name, Status = StepStatus.Pending };
                _steps.Add(step);

                var lbl = new Label
                {
                    Text = $"  ○  {name}",
                    Font = new Font("Segoe UI", 10),
                    ForeColor = TEXT_MUTED,
                    AutoSize = false,
                    Size = new Size(_stepsPanel.Width - 10, 24),
                    Margin = new Padding(0, 2, 0, 2)
                };
                _stepLabels.Add(lbl);
                _stepsPanel.Controls.Add(lbl);
            }

            _currentStepIndex = -1;
            UpdateProgress();
        }

        /// <summary>
        /// Start the next step
        /// </summary>
        public void StartStep(int index)
        {
            if (index < 0 || index >= _steps.Count) return;

            _currentStepIndex = index;
            _steps[index].Status = StepStatus.InProgress;
            UpdateStepLabel(index);
            UpdateProgress();
        }

        /// <summary>
        /// Complete the current step
        /// </summary>
        public void CompleteStep(int index, bool success = true)
        {
            if (index < 0 || index >= _steps.Count) return;

            _steps[index].Status = success ? StepStatus.Completed : StepStatus.Failed;
            UpdateStepLabel(index);
            UpdateProgress();
        }

        /// <summary>
        /// Skip a step
        /// </summary>
        public void SkipStep(int index)
        {
            if (index < 0 || index >= _steps.Count) return;

            _steps[index].Status = StepStatus.Skipped;
            UpdateStepLabel(index);
            UpdateProgress();
        }

        /// <summary>
        /// Set instruction text
        /// </summary>
        public void SetInstruction(string text)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => _lblInstruction.Text = text));
            else
                _lblInstruction.Text = text;
        }

        /// <summary>
        /// Mark operation as complete
        /// </summary>
        public void Complete(bool success, string? message = null)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Complete(success, message)));
                return;
            }

            _isComplete = true;
            _canCancel = false;

            _lblProgress.Text = success ? "100%" : "FAILED";
            _lblProgress.ForeColor = success ? SUCCESS : DANGER;
            _progressBar.Value = success ? 100 : _progressBar.Value;
            _lblInstruction.Text = message ?? (success ? "Operation completed successfully!" : "Operation failed");
            _lblInstruction.ForeColor = success ? SUCCESS : DANGER;
            _btnCancel.Text = "Close";
        }

        private void UpdateStepLabel(int index)
        {
            if (index < 0 || index >= _stepLabels.Count) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateStepLabel(index)));
                return;
            }

            var step = _steps[index];
            var lbl = _stepLabels[index];

            string icon;
            Color color;

            switch (step.Status)
            {
                case StepStatus.Completed:
                    icon = "✓";
                    color = SUCCESS;
                    break;
                case StepStatus.InProgress:
                    icon = "⟳";
                    color = ACCENT;
                    break;
                case StepStatus.Failed:
                    icon = "✗";
                    color = DANGER;
                    break;
                case StepStatus.Skipped:
                    icon = "—";
                    color = TEXT_MUTED;
                    break;
                default:
                    icon = "○";
                    color = TEXT_MUTED;
                    break;
            }

            lbl.Text = $"  {icon}  {step.Name}{(step.Status == StepStatus.InProgress ? "..." : "")}";
            lbl.ForeColor = color;
        }

        private void UpdateProgress()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateProgress));
                return;
            }

            int completed = 0;
            foreach (var step in _steps)
            {
                if (step.Status == StepStatus.Completed || step.Status == StepStatus.Skipped)
                    completed++;
            }

            int percent = _steps.Count > 0 ? (completed * 100) / _steps.Count : 0;
            _lblProgress.Text = $"{percent}%";
            _progressBar.Value = Math.Min(percent, 100);

            // Color based on progress
            if (percent >= 100)
                _lblProgress.ForeColor = SUCCESS;
            else if (percent >= 50)
                _lblProgress.ForeColor = WARNING;
            else
                _lblProgress.ForeColor = ACCENT;
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            if (_isComplete)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            if (!_canCancel) return;

            var result = MessageBox.Show(
                "Are you sure you want to cancel this operation?\n\n" +
                "⚠️ Cancelling mid-operation may leave the vehicle in an inconsistent state.",
                "Cancel Operation?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                WasCancelled = true;
                _cts?.Cancel();
                _btnCancel.Enabled = false;
                _btnCancel.Text = "Cancelling...";
                _lblInstruction.Text = "Cancelling operation...";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_isComplete && !WasCancelled && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                BtnCancel_Click(null, EventArgs.Empty);
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Dispose();
                _logo?.Image?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Run an operation with progress display
        /// </summary>
        public static async Task<T> RunAsync<T>(
            IWin32Window? owner,
            string operationName,
            string vehicleInfo,
            int tokenCost,
            string[] steps,
            Func<OperationProgressForm, Task<T>> operation)
        {
            using var form = new OperationProgressForm();
            form.Configure(operationName, vehicleInfo, tokenCost, steps);

            T result = default!;
            Exception? error = null;

            // Run operation on background thread
            var task = Task.Run(async () =>
            {
                try
                {
                    result = await operation(form);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            // Show form (blocks until closed)
            form.ShowDialog(owner);

            // Wait for task to complete
            await task;

            if (error != null)
                throw error;

            return result;
        }
    }
}
