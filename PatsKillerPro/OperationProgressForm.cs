using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PatsKillerPro
{
    /// <summary>
    /// Full-screen operation progress splash screen with step tracking
    /// </summary>
    public class OperationProgressForm : Form
    {
        // Theme colors
        private readonly Color BG = Color.FromArgb(20, 20, 24);
        private readonly Color SURFACE = Color.FromArgb(35, 35, 40);
        private readonly Color CARD = Color.FromArgb(42, 42, 48);
        private readonly Color BORDER = Color.FromArgb(58, 58, 66);
        private readonly Color TEXT = Color.FromArgb(240, 240, 240);
        private readonly Color TEXT_DIM = Color.FromArgb(160, 160, 165);
        private readonly Color TEXT_MUTED = Color.FromArgb(112, 112, 117);
        private readonly Color SUCCESS = Color.FromArgb(34, 197, 94);
        private readonly Color WARNING = Color.FromArgb(234, 179, 8);
        private readonly Color DANGER = Color.FromArgb(239, 68, 68);
        private readonly Color ACCENT = Color.FromArgb(59, 130, 246);
        private readonly Color BTN_BG = Color.FromArgb(54, 54, 64);

        // Controls
        private PictureBox? _logo;
        private Label _lblOperation = null!;
        private Label _lblVehicle = null!;
        private Label _lblProgress = null!;
        private ProgressBar _progressBar = null!;
        private Panel _stepsPanel = null!;
        private Label _lblInstruction = null!;
        private Label _lblTokenCost = null!;
        private Button _btnCancel = null!;

        // State
        private string _operationName = "Operation";
        private string _vehicleInfo = "";
        private int _tokenCost = 0;
        private List<OperationStep> _steps = new();
        private int _currentStep = 0;
        private bool _canCancel = true;
        private CancellationTokenSource? _cancellationSource;

        public class OperationStep
        {
            public string Name { get; set; } = "";
            public StepStatus Status { get; set; } = StepStatus.Pending;
        }

        public enum StepStatus
        {
            Pending,
            InProgress,
            Completed,
            Failed
        }

        /// <summary>
        /// Whether cancel was requested
        /// </summary>
        public bool CancelRequested { get; private set; }

        /// <summary>
        /// Cancellation token for async operations
        /// </summary>
        public CancellationToken CancellationToken => _cancellationSource?.Token ?? CancellationToken.None;

        public OperationProgressForm()
        {
            _cancellationSource = new CancellationTokenSource();
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "PatsKiller Pro - Operation In Progress";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = BG;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;

            // Center panel
            var centerPanel = new Panel
            {
                Size = new Size(600, 500),
                BackColor = CARD,
                Anchor = AnchorStyles.None
            };
            centerPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(BORDER, 2);
                e.Graphics.DrawRectangle(pen, 1, 1, centerPanel.Width - 3, centerPanel.Height - 3);
            };
            Controls.Add(centerPanel);

            // Position center panel
            this.Resize += (s, e) =>
            {
                centerPanel.Location = new Point(
                    (ClientSize.Width - centerPanel.Width) / 2,
                    (ClientSize.Height - centerPanel.Height) / 2
                );
            };

            int y = 30;

            // Logo placeholder (or text)
            _lblOperation = new Label
            {
                Text = "PATSKILLER PRO",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = ACCENT,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 50
            };
            centerPanel.Controls.Add(_lblOperation);

            // Operation name
            var lblOpTitle = new Label
            {
                Text = "PROGRAMMING KEY",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = TEXT,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(0, 10, 0, 0)
            };
            centerPanel.Controls.Add(lblOpTitle);
            _lblOperation = lblOpTitle;

            // Vehicle info
            _lblVehicle = new Label
            {
                Text = "2021 Ford F-150 XLT",
                Font = new Font("Segoe UI", 11),
                ForeColor = TEXT_DIM,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 30
            };
            centerPanel.Controls.Add(_lblVehicle);

            // Progress percentage
            _lblProgress = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 28, FontStyle.Bold),
                ForeColor = SUCCESS,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 50,
                Padding = new Padding(0, 10, 0, 0)
            };
            centerPanel.Controls.Add(_lblProgress);

            // Progress bar
            var progressPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(50, 5, 50, 5)
            };
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            progressPanel.Controls.Add(_progressBar);
            centerPanel.Controls.Add(progressPanel);

            // Steps panel
            _stepsPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 150,
                Padding = new Padding(50, 10, 50, 10),
                BackColor = Color.Transparent
            };
            centerPanel.Controls.Add(_stepsPanel);

            // Instruction label
            _lblInstruction = new Label
            {
                Text = "Please wait...",
                Font = new Font("Segoe UI", 10),
                ForeColor = WARNING,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 30
            };
            centerPanel.Controls.Add(_lblInstruction);

            // Token cost
            _lblTokenCost = new Label
            {
                Text = "Token cost: 1",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = TEXT_MUTED,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 25
            };
            centerPanel.Controls.Add(_lblTokenCost);

            // Cancel button
            var btnPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                Padding = new Padding(200, 5, 200, 5)
            };
            _btnCancel = new Button
            {
                Text = "Cancel",
                Dock = DockStyle.Fill,
                BackColor = BTN_BG,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = BORDER;
            _btnCancel.Click += BtnCancel_Click;
            btnPanel.Controls.Add(_btnCancel);
            centerPanel.Controls.Add(btnPanel);

            // Reorder controls (WinForms adds from bottom)
            var controls = new Control[] { _lblOperation, _lblVehicle, _lblProgress, progressPanel, _stepsPanel, _lblInstruction, _lblTokenCost, btnPanel };
            for (int i = controls.Length - 1; i >= 0; i--)
            {
                centerPanel.Controls.SetChildIndex(controls[i], 0);
            }
        }

        /// <summary>
        /// Configure the operation display
        /// </summary>
        public void Configure(string operationName, string vehicleInfo, int tokenCost, List<string> stepNames)
        {
            _operationName = operationName;
            _vehicleInfo = vehicleInfo;
            _tokenCost = tokenCost;

            _steps.Clear();
            foreach (var name in stepNames)
            {
                _steps.Add(new OperationStep { Name = name, Status = StepStatus.Pending });
            }

            if (IsHandleCreated)
            {
                BeginInvoke(new Action(UpdateDisplay));
            }
        }

        private void UpdateDisplay()
        {
            _lblOperation.Text = _operationName.ToUpper();
            _lblVehicle.Text = _vehicleInfo;
            _lblTokenCost.Text = $"Token cost: {(_tokenCost == 0 ? "FREE" : _tokenCost.ToString())}";

            // Rebuild steps display
            _stepsPanel.Controls.Clear();
            int stepY = 5;
            foreach (var step in _steps)
            {
                var lbl = new Label
                {
                    Location = new Point(0, stepY),
                    Size = new Size(_stepsPanel.Width - 20, 22),
                    Font = new Font("Segoe UI", 10),
                    Text = GetStepText(step),
                    ForeColor = GetStepColor(step)
                };
                _stepsPanel.Controls.Add(lbl);
                stepY += 24;
            }

            // Update progress
            int completed = 0;
            foreach (var step in _steps)
            {
                if (step.Status == StepStatus.Completed) completed++;
            }
            int percent = _steps.Count > 0 ? (completed * 100) / _steps.Count : 0;
            _lblProgress.Text = $"{percent}%";
            _progressBar.Value = percent;
        }

        private string GetStepText(OperationStep step)
        {
            string prefix = step.Status switch
            {
                StepStatus.Completed => "✓",
                StepStatus.InProgress => "⟳",
                StepStatus.Failed => "✗",
                _ => "○"
            };
            string suffix = step.Status == StepStatus.InProgress ? "..." : "";
            return $"  {prefix}  {step.Name}{suffix}";
        }

        private Color GetStepColor(OperationStep step)
        {
            return step.Status switch
            {
                StepStatus.Completed => SUCCESS,
                StepStatus.InProgress => ACCENT,
                StepStatus.Failed => DANGER,
                _ => TEXT_MUTED
            };
        }

        /// <summary>
        /// Update a step's status
        /// </summary>
        public void UpdateStep(int stepIndex, StepStatus status)
        {
            if (stepIndex < 0 || stepIndex >= _steps.Count) return;

            _steps[stepIndex].Status = status;
            _currentStep = stepIndex;

            if (IsHandleCreated)
            {
                BeginInvoke(new Action(UpdateDisplay));
            }
        }

        /// <summary>
        /// Set the instruction text
        /// </summary>
        public void SetInstruction(string text)
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(() => _lblInstruction.Text = text));
            }
        }

        /// <summary>
        /// Mark operation as complete
        /// </summary>
        public void Complete(bool success)
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(() =>
                {
                    _lblProgress.Text = success ? "100%" : "FAILED";
                    _lblProgress.ForeColor = success ? SUCCESS : DANGER;
                    _progressBar.Value = success ? 100 : _progressBar.Value;
                    _lblInstruction.Text = success ? "Operation completed successfully!" : "Operation failed";
                    _btnCancel.Text = "Close";
                    _canCancel = false;
                }));
            }
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            if (!_canCancel)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to cancel this operation?\n\n" +
                "Cancelling mid-operation may leave the vehicle in an inconsistent state.",
                "Cancel Operation?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                CancelRequested = true;
                _cancellationSource?.Cancel();
                _btnCancel.Enabled = false;
                _btnCancel.Text = "Cancelling...";
                _lblInstruction.Text = "Cancelling operation...";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Prevent closing during operation unless cancelled or complete
            if (_canCancel && !CancelRequested && e.CloseReason == CloseReason.UserClosing)
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
                _cancellationSource?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
