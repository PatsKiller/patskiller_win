using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PatsKillerPro
{
    /// <summary>
    /// Full-screen 10-minute timed security access countdown
    /// </summary>
    public class TimedSecurityAccessForm : Form
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
        private Label _lblTitle = null!;
        private Label _lblTimer = null!;
        private ProgressBar _progressBar = null!;
        private Label _lblPercent = null!;
        private Label _lblWarning = null!;
        private Label _lblInfo = null!;
        private Button _btnCancel = null!;

        // State
        private System.Windows.Forms.Timer _timer = null!;
        private int _totalSeconds;
        private int _remainingSeconds;
        private bool _cancelled = false;
        private CancellationTokenSource? _cancellationSource;
        private Func<Task<bool>>? _keepAliveAction;

        /// <summary>
        /// Whether the countdown completed successfully
        /// </summary>
        public bool Completed { get; private set; }

        /// <summary>
        /// Whether the user cancelled
        /// </summary>
        public bool Cancelled => _cancelled;

        /// <summary>
        /// Create timed access form
        /// </summary>
        /// <param name="durationSeconds">Duration in seconds (default 600 = 10 min)</param>
        /// <param name="keepAliveAction">Action to call every 2 seconds to keep session alive</param>
        public TimedSecurityAccessForm(int durationSeconds = 600, Func<Task<bool>>? keepAliveAction = null)
        {
            _totalSeconds = durationSeconds;
            _remainingSeconds = durationSeconds;
            _keepAliveAction = keepAliveAction;
            _cancellationSource = new CancellationTokenSource();
            
            InitializeUI();
            InitializeTimer();
        }

        private void InitializeUI()
        {
            Text = "PatsKiller Pro - Security Access";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = BG;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;

            // Center panel
            var centerPanel = new Panel
            {
                Size = new Size(500, 380),
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

            // Title
            _lblTitle = new Label
            {
                Text = "SECURITY ACCESS IN PROGRESS",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = ACCENT,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 50,
                Padding = new Padding(0, 15, 0, 0)
            };
            centerPanel.Controls.Add(_lblTitle);

            // Timer display
            _lblTimer = new Label
            {
                Text = "10:00",
                Font = new Font("Consolas", 64, FontStyle.Bold),
                ForeColor = SUCCESS,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 90
            };
            centerPanel.Controls.Add(_lblTimer);

            // Progress bar panel
            var progressPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(40, 5, 40, 5)
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

            // Percent label
            _lblPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = TEXT_DIM,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 25
            };
            centerPanel.Controls.Add(_lblPercent);

            // Warning panel
            var warningPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                Padding = new Padding(30, 10, 30, 10),
                BackColor = Color.FromArgb(40, 234, 179, 8)
            };
            warningPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(WARNING, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, warningPanel.Width - 1, warningPanel.Height - 1);
            };

            _lblWarning = new Label
            {
                Text = "⚠️ DO NOT disconnect tool or turn off ignition",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = WARNING,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            warningPanel.Controls.Add(_lblWarning);
            centerPanel.Controls.Add(warningPanel);

            // Info label
            _lblInfo = new Label
            {
                Text = "Timed security access required for this vehicle.\nThe vehicle does not support instant coded access.",
                Font = new Font("Segoe UI", 9),
                ForeColor = TEXT_MUTED,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 45,
                Padding = new Padding(0, 10, 0, 0)
            };
            centerPanel.Controls.Add(_lblInfo);

            // Cancel button
            var btnPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 55,
                Padding = new Padding(150, 10, 150, 5)
            };
            _btnCancel = new Button
            {
                Text = "Cancel",
                Dock = DockStyle.Fill,
                BackColor = BTN_BG,
                ForeColor = TEXT,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = BORDER;
            _btnCancel.Click += BtnCancel_Click;
            btnPanel.Controls.Add(_btnCancel);
            centerPanel.Controls.Add(btnPanel);

            // Reorder controls
            var controls = new Control[] { _lblTitle, _lblTimer, progressPanel, _lblPercent, warningPanel, _lblInfo, btnPanel };
            for (int i = controls.Length - 1; i >= 0; i--)
            {
                centerPanel.Controls.SetChildIndex(controls[i], 0);
            }
        }

        private void InitializeTimer()
        {
            _timer = new System.Windows.Forms.Timer
            {
                Interval = 1000 // 1 second
            };
            _timer.Tick += Timer_Tick;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
            
            // Start keep-alive task
            if (_keepAliveAction != null)
            {
                Task.Run(async () =>
                {
                    while (!_cancelled && _remainingSeconds > 0)
                    {
                        try
                        {
                            await _keepAliveAction();
                            await Task.Delay(2000, _cancellationSource!.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch
                        {
                            // Log but continue
                        }
                    }
                });
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _remainingSeconds--;

            if (_remainingSeconds <= 0)
            {
                _timer.Stop();
                Completed = true;
                
                _lblTimer.Text = "00:00";
                _lblTimer.ForeColor = SUCCESS;
                _progressBar.Value = 100;
                _lblPercent.Text = "100% - COMPLETE!";
                _lblPercent.ForeColor = SUCCESS;
                _lblWarning.Text = "✓ Security access granted!";
                _lblWarning.ForeColor = SUCCESS;
                _btnCancel.Text = "Continue";
                
                return;
            }

            // Update display
            int mins = _remainingSeconds / 60;
            int secs = _remainingSeconds % 60;
            _lblTimer.Text = $"{mins:D2}:{secs:D2}";

            // Update progress
            int elapsed = _totalSeconds - _remainingSeconds;
            int percent = (elapsed * 100) / _totalSeconds;
            _progressBar.Value = percent;
            _lblPercent.Text = $"{percent}%";

            // Color changes based on remaining time
            if (_remainingSeconds <= 30)
            {
                _lblTimer.ForeColor = SUCCESS; // Almost done!
            }
            else if (_remainingSeconds <= 120) // 2 minutes
            {
                _lblTimer.ForeColor = WARNING;
            }
            else
            {
                _lblTimer.ForeColor = TEXT;
            }
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            if (Completed)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to cancel security access?\n\n" +
                "You will need to restart the 10-minute countdown.",
                "Cancel Security Access?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                _cancelled = true;
                _cancellationSource?.Cancel();
                _timer.Stop();
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!Completed && !_cancelled && e.CloseReason == CloseReason.UserClosing)
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
                _timer?.Dispose();
                _cancellationSource?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Show timed access dialog and wait for completion
        /// </summary>
        /// <param name="owner">Parent window</param>
        /// <param name="durationSeconds">Duration (default 600)</param>
        /// <param name="keepAliveAction">Keep-alive callback</param>
        /// <returns>True if completed, false if cancelled</returns>
        public static bool ShowTimedAccess(IWin32Window? owner, int durationSeconds = 600, Func<Task<bool>>? keepAliveAction = null)
        {
            using var form = new TimedSecurityAccessForm(durationSeconds, keepAliveAction);
            form.ShowDialog(owner);
            return form.Completed;
        }
    }
}
