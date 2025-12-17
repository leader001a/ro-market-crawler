using System.Diagnostics;

namespace RoMarketCrawler.Controls;

/// <summary>
/// Custom autocomplete dropdown that handles Korean IME input correctly.
/// Uses debounce timer to wait for IME composition to complete before searching.
/// </summary>
public class AutoCompleteDropdown : IDisposable
{
    // UI Components
    private readonly Form _dropdownForm;
    private readonly ListBox _listBox;
    private readonly System.Windows.Forms.Timer _debounceTimer;

    // State
    private TextBox? _currentTextBox;
    private readonly List<TextBox> _attachedTextBoxes = new();
    private IReadOnlyList<string> _dataSource = Array.Empty<string>();
    private readonly List<string> _filteredItems = new();
    private bool _isShowing;
    private bool _isSelectingItem;
    private string _lastSearchQuery = string.Empty;

    // Settings
    private const int DebounceDelayMs = 250; // Wait for Korean IME composition
    private const int MaxVisibleItems = 8;
    private const int MinQueryLength = 1;

    // Theme colors
    private Color _backgroundColor = Color.FromArgb(45, 45, 45);
    private Color _foregroundColor = Color.FromArgb(220, 220, 220);
    private Color _selectedColor = Color.FromArgb(0, 120, 215);
    private Color _borderColor = Color.FromArgb(80, 80, 80);

    public AutoCompleteDropdown()
    {
        // Initialize dropdown form
        _dropdownForm = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = _backgroundColor,
            Size = new Size(300, 200)
        };

        // Initialize listbox
        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _backgroundColor,
            ForeColor = _foregroundColor,
            Font = new Font("Malgun Gothic", 9f),
            IntegralHeight = false
        };
        _listBox.Click += ListBox_Click;
        _listBox.DoubleClick += ListBox_DoubleClick;
        _listBox.KeyDown += ListBox_KeyDown;

        _dropdownForm.Controls.Add(_listBox);

        // Initialize debounce timer
        _debounceTimer = new System.Windows.Forms.Timer
        {
            Interval = DebounceDelayMs
        };
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    /// <summary>
    /// Set the data source for autocomplete suggestions
    /// </summary>
    public void SetDataSource(IReadOnlyList<string> items)
    {
        _dataSource = items ?? Array.Empty<string>();
        Debug.WriteLine($"[AutoCompleteDropdown] DataSource set: {_dataSource.Count} items");
    }

    /// <summary>
    /// Update theme colors
    /// </summary>
    public void UpdateTheme(Color background, Color foreground, Color selected, Color border)
    {
        _backgroundColor = background;
        _foregroundColor = foreground;
        _selectedColor = selected;
        _borderColor = border;

        _dropdownForm.BackColor = _backgroundColor;
        _listBox.BackColor = _backgroundColor;
        _listBox.ForeColor = _foregroundColor;
    }

    /// <summary>
    /// Attach autocomplete to a TextBox
    /// </summary>
    public void AttachTo(TextBox? textBox)
    {
        if (textBox == null) return;
        if (_attachedTextBoxes.Contains(textBox)) return;

        // Remove any existing WinForms autocomplete
        textBox.AutoCompleteMode = AutoCompleteMode.None;
        textBox.AutoCompleteSource = AutoCompleteSource.None;

        // Attach events
        textBox.TextChanged += TextBox_TextChanged;
        textBox.KeyDown += TextBox_KeyDown;
        textBox.LostFocus += TextBox_LostFocus;
        textBox.LocationChanged += TextBox_LocationChanged;
        textBox.ParentChanged += TextBox_ParentChanged;

        _attachedTextBoxes.Add(textBox);
        Debug.WriteLine($"[AutoCompleteDropdown] Attached to TextBox");
    }

    /// <summary>
    /// Detach autocomplete from a TextBox
    /// </summary>
    public void DetachFrom(TextBox? textBox)
    {
        if (textBox == null) return;
        if (!_attachedTextBoxes.Contains(textBox)) return;

        textBox.TextChanged -= TextBox_TextChanged;
        textBox.KeyDown -= TextBox_KeyDown;
        textBox.LostFocus -= TextBox_LostFocus;
        textBox.LocationChanged -= TextBox_LocationChanged;
        textBox.ParentChanged -= TextBox_ParentChanged;

        _attachedTextBoxes.Remove(textBox);

        if (_currentTextBox == textBox)
        {
            HideDropdown();
            _currentTextBox = null;
        }
    }

    #region Event Handlers

    private void TextBox_TextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (_isSelectingItem) return; // Don't trigger when we're setting text from selection

        _currentTextBox = textBox;

        // Reset and start debounce timer
        _debounceTimer.Stop();

        var query = textBox.Text.Trim();
        if (query.Length >= MinQueryLength)
        {
            _debounceTimer.Start();
        }
        else
        {
            HideDropdown();
        }
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isShowing) return;

        switch (e.KeyCode)
        {
            case Keys.Down:
                e.Handled = true;
                e.SuppressKeyPress = true;
                MoveSelection(1);
                break;

            case Keys.Up:
                e.Handled = true;
                e.SuppressKeyPress = true;
                MoveSelection(-1);
                break;

            case Keys.Enter:
                if (_listBox.SelectedIndex >= 0)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    SelectItem();
                }
                break;

            case Keys.Escape:
                e.Handled = true;
                e.SuppressKeyPress = true;
                HideDropdown();
                break;

            case Keys.Tab:
                if (_listBox.SelectedIndex >= 0)
                {
                    SelectItem();
                }
                else
                {
                    HideDropdown();
                }
                break;
        }
    }

    private void TextBox_LostFocus(object? sender, EventArgs e)
    {
        // Delay hide to allow click on listbox
        Task.Delay(150).ContinueWith(_ =>
        {
            if (_dropdownForm.IsDisposed) return;

            _dropdownForm.Invoke(() =>
            {
                if (!_listBox.Focused && !(_currentTextBox?.Focused ?? false))
                {
                    HideDropdown();
                }
            });
        });
    }

    private void TextBox_LocationChanged(object? sender, EventArgs e)
    {
        if (_isShowing && sender == _currentTextBox)
        {
            UpdateDropdownPosition();
        }
    }

    private void TextBox_ParentChanged(object? sender, EventArgs e)
    {
        if (_isShowing && sender == _currentTextBox)
        {
            UpdateDropdownPosition();
        }
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();

        if (_currentTextBox == null) return;

        var query = _currentTextBox.Text.Trim();
        if (query.Length >= MinQueryLength && query != _lastSearchQuery)
        {
            _lastSearchQuery = query;
            PerformSearch(query);
        }
    }

    private void ListBox_Click(object? sender, EventArgs e)
    {
        // Single click just selects, double click applies
    }

    private void ListBox_DoubleClick(object? sender, EventArgs e)
    {
        if (_listBox.SelectedIndex >= 0)
        {
            SelectItem();
        }
    }

    private void ListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _listBox.SelectedIndex >= 0)
        {
            e.Handled = true;
            SelectItem();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            HideDropdown();
            _currentTextBox?.Focus();
        }
    }

    #endregion

    #region Private Methods

    private void PerformSearch(string query)
    {
        if (_dataSource.Count == 0) return;

        var queryLower = query.ToLowerInvariant();

        // Filter items (case-insensitive, prefix match first, then contains)
        _filteredItems.Clear();

        // First add prefix matches
        var prefixMatches = _dataSource
            .Where(s => s.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .Take(MaxVisibleItems)
            .ToList();
        _filteredItems.AddRange(prefixMatches);

        // Then add contains matches (if not already added)
        if (_filteredItems.Count < MaxVisibleItems)
        {
            var containsMatches = _dataSource
                .Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase)
                           && !s.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .Take(MaxVisibleItems - _filteredItems.Count);
            _filteredItems.AddRange(containsMatches);
        }

        if (_filteredItems.Count > 0)
        {
            UpdateListBox();
            ShowDropdown();
        }
        else
        {
            HideDropdown();
        }
    }

    private void UpdateListBox()
    {
        _listBox.BeginUpdate();
        _listBox.Items.Clear();
        foreach (var item in _filteredItems)
        {
            _listBox.Items.Add(item);
        }
        _listBox.EndUpdate();

        // Auto-select first item
        if (_listBox.Items.Count > 0)
        {
            _listBox.SelectedIndex = 0;
        }

        // Calculate height based on items
        var itemHeight = _listBox.ItemHeight;
        var visibleCount = Math.Min(_filteredItems.Count, MaxVisibleItems);
        var newHeight = (itemHeight * visibleCount) + 4; // +4 for border
        _dropdownForm.Height = Math.Max(newHeight, itemHeight + 4);
    }

    private void ShowDropdown()
    {
        if (_currentTextBox == null) return;
        if (_filteredItems.Count == 0) return;

        UpdateDropdownPosition();

        if (!_isShowing)
        {
            _dropdownForm.Show();
            _isShowing = true;
            _currentTextBox.Focus(); // Keep focus on textbox
        }
    }

    private void HideDropdown()
    {
        if (_isShowing)
        {
            _dropdownForm.Hide();
            _isShowing = false;
            _lastSearchQuery = string.Empty;
        }
    }

    private void UpdateDropdownPosition()
    {
        if (_currentTextBox == null) return;

        try
        {
            // Get screen position of the textbox
            var screenPos = _currentTextBox.PointToScreen(Point.Empty);
            var textBoxWidth = _currentTextBox.Width;
            var textBoxHeight = _currentTextBox.Height;

            // Position dropdown below textbox
            _dropdownForm.Location = new Point(screenPos.X, screenPos.Y + textBoxHeight);
            _dropdownForm.Width = Math.Max(textBoxWidth, 200);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoCompleteDropdown] Position update error: {ex.Message}");
        }
    }

    private void MoveSelection(int delta)
    {
        if (_listBox.Items.Count == 0) return;

        var newIndex = _listBox.SelectedIndex + delta;
        if (newIndex < 0) newIndex = 0;
        if (newIndex >= _listBox.Items.Count) newIndex = _listBox.Items.Count - 1;

        _listBox.SelectedIndex = newIndex;
    }

    private void SelectItem()
    {
        if (_currentTextBox == null) return;
        if (_listBox.SelectedIndex < 0) return;
        if (_listBox.SelectedIndex >= _filteredItems.Count) return;

        var selectedText = _filteredItems[_listBox.SelectedIndex];

        _isSelectingItem = true;
        try
        {
            _currentTextBox.Text = selectedText;
            _currentTextBox.SelectionStart = selectedText.Length;
        }
        finally
        {
            _isSelectingItem = false;
        }

        HideDropdown();
        _currentTextBox.Focus();
    }

    #endregion

    #region IDisposable

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;

        _debounceTimer.Stop();
        _debounceTimer.Dispose();

        foreach (var textBox in _attachedTextBoxes.ToList())
        {
            DetachFrom(textBox);
        }

        _dropdownForm.Close();
        _dropdownForm.Dispose();

        _disposed = true;
    }

    #endregion
}
