using System;
using System.Drawing;
using System.Windows.Forms;

namespace PatsKillerPro.Controls
{
    /// <summary>
    /// Visual key slot panel showing 8 slots with programmed/empty status
    /// Click an empty slot to select it for the next key programming
    /// </summary>
    public class KeySlotPanel : UserControl
    {
        // Theme colors (match MainForm)
        private readonly Color BG = Color.FromArgb(26, 26, 30);
        private readonly Color SURFACE = Color.FromArgb(35, 35, 40);
        private readonly Color BORDER = Color.FromArgb(58, 58, 66);
        private readonly Color TEXT = Color.FromArgb(240, 240, 240);
        private readonly Color TEXT_DIM = Color.FromArgb(160, 160, 165);
        private readonly Color TEXT_MUTED = Color.FromArgb(112, 112, 117);
        private readonly Color SUCCESS = Color.FromArgb(34, 197, 94);
        private readonly Color ACCENT = Color.FromArgb(59, 130, 246);

        private int _programmedKeys = 0;
        private int _maxKeys = 8;
        private int _selectedSlot = -1; // -1 = auto (next available)
        private readonly Button[] _slotButtons = new Button[8];
        private Label _lblInfo = null!;

        /// <summary>
        /// Number of currently programmed keys (0-8)
        /// </summary>
        public int ProgrammedKeys
        {
            get => _programmedKeys;
            set
            {
                _programmedKeys = Math.Max(0, Math.Min(value, _maxKeys));
                // Auto-advance selection if needed
                if (_selectedSlot > 0 && _selectedSlot <= _programmedKeys)
                {
                    _selectedSlot = -1;
                }
                UpdateSlotDisplay();
            }
        }

        /// <summary>
        /// Maximum number of key slots (typically 8)
        /// </summary>
        public int MaxKeys
        {
            get => _maxKeys;
            set
            {
                _maxKeys = Math.Max(1, Math.Min(value, 8));
                UpdateSlotDisplay();
            }
        }

        /// <summary>
        /// Currently selected slot for next key (-1 = auto/next available)
        /// </summary>
        public int SelectedSlot
        {
            get => _selectedSlot;
            set
            {
                _selectedSlot = value;
                UpdateSlotDisplay();
                SlotSelected?.Invoke(this, _selectedSlot);
            }
        }

        /// <summary>
        /// Get the next available slot for programming
        /// </summary>
        public int NextAvailableSlot => _selectedSlot > 0 ? _selectedSlot : _programmedKeys + 1;

        /// <summary>
        /// Fired when user clicks to select a slot
        /// </summary>
        public event EventHandler<int>? SlotSelected;

        public KeySlotPanel()
        {
            InitializePanel();
        }

        private void InitializePanel()
        {
            this.BackColor = SURFACE;
            this.Size = new Size(400, 65);
            this.Padding = new Padding(8);

            // Create 8 slot buttons in a flow layout
            int slotSize = 38;
            int spacing = 6;
            int startX = 8;
            int startY = 8;

            for (int i = 0; i < 8; i++)
            {
                var btn = new Button
                {
                    Size = new Size(slotSize, slotSize),
                    Location = new Point(startX + (i * (slotSize + spacing)), startY),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Tag = i + 1, // Slot number (1-8)
                    TabStop = false
                };
                btn.FlatAppearance.BorderSize = 2;
                btn.Click += SlotButton_Click;

                _slotButtons[i] = btn;
                Controls.Add(btn);
            }

            // Info label below slots
            _lblInfo = new Label
            {
                Text = "Next → Slot 1",
                Font = new Font("Segoe UI", 8),
                ForeColor = TEXT_MUTED,
                Location = new Point(startX, startY + slotSize + 4),
                AutoSize = true
            };
            Controls.Add(_lblInfo);

            UpdateSlotDisplay();
        }

        private void SlotButton_Click(object? sender, EventArgs e)
        {
            if (sender is Button btn && btn.Tag is int slot)
            {
                // Only allow selecting empty slots
                if (slot > _programmedKeys && slot <= _maxKeys)
                {
                    SelectedSlot = (_selectedSlot == slot) ? -1 : slot; // Toggle selection
                }
            }
        }

        private void UpdateSlotDisplay()
        {
            for (int i = 0; i < 8; i++)
            {
                int slot = i + 1;
                var btn = _slotButtons[i];

                if (slot > _maxKeys)
                {
                    btn.Visible = false;
                    continue;
                }

                btn.Visible = true;
                bool isProgrammed = slot <= _programmedKeys;
                bool isSelected = slot == _selectedSlot;
                bool isNextAuto = !isSelected && _selectedSlot == -1 && slot == _programmedKeys + 1;

                // Set appearance based on state
                if (isProgrammed)
                {
                    btn.BackColor = Color.FromArgb(30, SUCCESS);
                    btn.FlatAppearance.BorderColor = SUCCESS;
                    btn.ForeColor = SUCCESS;
                    btn.Text = "✓";
                    btn.Cursor = Cursors.Default;
                }
                else if (isSelected)
                {
                    btn.BackColor = Color.FromArgb(40, ACCENT);
                    btn.FlatAppearance.BorderColor = ACCENT;
                    btn.ForeColor = ACCENT;
                    btn.Text = "▶";
                    btn.Cursor = Cursors.Hand;
                }
                else if (isNextAuto)
                {
                    btn.BackColor = Color.FromArgb(15, ACCENT);
                    btn.FlatAppearance.BorderColor = Color.FromArgb(80, ACCENT);
                    btn.ForeColor = TEXT_DIM;
                    btn.Text = slot.ToString();
                    btn.Cursor = Cursors.Hand;
                }
                else
                {
                    btn.BackColor = BG;
                    btn.FlatAppearance.BorderColor = BORDER;
                    btn.ForeColor = TEXT_MUTED;
                    btn.Text = slot.ToString();
                    btn.Cursor = Cursors.Hand;
                }
            }

            // Update info label
            if (_programmedKeys >= _maxKeys)
            {
                _lblInfo.Text = "All slots full - erase to add keys";
                _lblInfo.ForeColor = TEXT_MUTED;
            }
            else if (_selectedSlot > 0)
            {
                _lblInfo.Text = $"Selected: Slot {_selectedSlot}";
                _lblInfo.ForeColor = ACCENT;
            }
            else
            {
                _lblInfo.Text = $"Next → Slot {_programmedKeys + 1}";
                _lblInfo.ForeColor = TEXT_DIM;
            }
        }

        /// <summary>
        /// Reset to auto-select mode
        /// </summary>
        public void ResetSelection()
        {
            SelectedSlot = -1;
        }
    }
}
