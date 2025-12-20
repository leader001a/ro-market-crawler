using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace RoMarketCrawler.Controls;

/// <summary>
/// TextBox with rounded corners and modern styling
/// </summary>
public class RoundedTextBox : UserControl
{
    #region Fields

    private readonly TextBox _innerTextBox;
    private bool _isFocused = false;
    private int _cornerRadius = 8;
    private int _borderWidth = 1;

    // Colors
    private Color _backgroundColor = Color.FromArgb(50, 50, 60);
    private Color _unfocusedBorderColor = Color.FromArgb(70, 75, 90);
    private Color _focusedBorderColor = Color.FromArgb(70, 130, 200);
    private Color _textColor = Color.FromArgb(230, 230, 235);
    private Color _placeholderColor = Color.FromArgb(140, 140, 150);

    private string _placeholderText = string.Empty;

    #endregion

    #region Properties

    /// <summary>
    /// Corner radius in pixels
    /// </summary>
    public int CornerRadius
    {
        get => _cornerRadius;
        set { _cornerRadius = Math.Max(0, value); Invalidate(); }
    }

    /// <summary>
    /// Border width in pixels
    /// </summary>
    public int BorderWidth
    {
        get => _borderWidth;
        set { _borderWidth = Math.Max(1, value); UpdateInnerTextBoxPadding(); Invalidate(); }
    }

    /// <summary>
    /// Background color
    /// </summary>
    public Color BackgroundColor
    {
        get => _backgroundColor;
        set { _backgroundColor = value; _innerTextBox.BackColor = value; Invalidate(); }
    }

    /// <summary>
    /// Border color when unfocused
    /// </summary>
    public Color UnfocusedBorderColor
    {
        get => _unfocusedBorderColor;
        set { _unfocusedBorderColor = value; Invalidate(); }
    }

    /// <summary>
    /// Border color when focused
    /// </summary>
    public Color FocusedBorderColor
    {
        get => _focusedBorderColor;
        set { _focusedBorderColor = value; Invalidate(); }
    }

    /// <summary>
    /// Text color
    /// </summary>
    public Color TextColor
    {
        get => _textColor;
        set { _textColor = value; _innerTextBox.ForeColor = value; }
    }

    /// <summary>
    /// Placeholder text color
    /// </summary>
    public Color PlaceholderColor
    {
        get => _placeholderColor;
        set { _placeholderColor = value; Invalidate(); }
    }

    /// <summary>
    /// Placeholder text shown when empty
    /// </summary>
    public string PlaceholderText
    {
        get => _placeholderText;
        set { _placeholderText = value; Invalidate(); }
    }

    /// <summary>
    /// The actual text content
    /// </summary>
    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    [AllowNull]
    public override string Text
    {
        get => _innerTextBox.Text;
        set => _innerTextBox.Text = value ?? string.Empty;
    }

    /// <summary>
    /// Whether the text box is read-only
    /// </summary>
    public bool ReadOnly
    {
        get => _innerTextBox.ReadOnly;
        set => _innerTextBox.ReadOnly = value;
    }

    /// <summary>
    /// Maximum length of text
    /// </summary>
    public int MaxLength
    {
        get => _innerTextBox.MaxLength;
        set => _innerTextBox.MaxLength = value;
    }

    /// <summary>
    /// Character used for password masking
    /// </summary>
    public char PasswordChar
    {
        get => _innerTextBox.PasswordChar;
        set => _innerTextBox.PasswordChar = value;
    }

    /// <summary>
    /// Whether to use system password char
    /// </summary>
    public bool UseSystemPasswordChar
    {
        get => _innerTextBox.UseSystemPasswordChar;
        set => _innerTextBox.UseSystemPasswordChar = value;
    }

    /// <summary>
    /// Gets the inner TextBox for advanced operations
    /// </summary>
    [Browsable(false)]
    public TextBox InnerTextBox => _innerTextBox;

    #endregion

    #region Events

    /// <summary>
    /// Raised when text changes
    /// </summary>
    public new event EventHandler? TextChanged;

    /// <summary>
    /// Raised when a key is pressed
    /// </summary>
    public new event KeyEventHandler? KeyDown;

    /// <summary>
    /// Raised when a key is released
    /// </summary>
    public new event KeyEventHandler? KeyUp;

    /// <summary>
    /// Raised when a key is pressed (character)
    /// </summary>
    public new event KeyPressEventHandler? KeyPress;

    #endregion

    #region Constructor

    public RoundedTextBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer, true);

        _innerTextBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = _backgroundColor,
            ForeColor = _textColor,
            Font = Font
        };

        _innerTextBox.GotFocus += InnerTextBox_GotFocus;
        _innerTextBox.LostFocus += InnerTextBox_LostFocus;
        _innerTextBox.TextChanged += InnerTextBox_TextChanged;
        _innerTextBox.KeyDown += InnerTextBox_KeyDown;
        _innerTextBox.KeyUp += InnerTextBox_KeyUp;
        _innerTextBox.KeyPress += InnerTextBox_KeyPress;

        Controls.Add(_innerTextBox);

        Size = new Size(200, 32);
        Font = new Font("Malgun Gothic", 9);
        BackColor = Color.Transparent;

        UpdateInnerTextBoxPadding();
    }

    #endregion

    #region Layout

    private void UpdateInnerTextBoxPadding()
    {
        var padding = _cornerRadius / 2 + _borderWidth + 4;
        _innerTextBox.Location = new Point(padding, (Height - _innerTextBox.Height) / 2);
        _innerTextBox.Width = Width - padding * 2;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateInnerTextBoxPadding();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _innerTextBox.Font = Font;
        UpdateInnerTextBoxPadding();
    }

    #endregion

    #region Paint

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        // Create rounded path
        using var path = CreateRoundedRectanglePath(rect, _cornerRadius);

        // Fill background
        using (var brush = new SolidBrush(_backgroundColor))
        {
            g.FillPath(brush, path);
        }

        // Draw border
        var borderColor = _isFocused ? _focusedBorderColor : _unfocusedBorderColor;
        using (var pen = new Pen(borderColor, _borderWidth))
        {
            g.DrawPath(pen, path);
        }

        // Draw placeholder if empty and not focused
        if (string.IsNullOrEmpty(_innerTextBox.Text) && !_isFocused && !string.IsNullOrEmpty(_placeholderText))
        {
            var textRect = new Rectangle(
                _cornerRadius / 2 + _borderWidth + 4,
                0,
                Width - (_cornerRadius + _borderWidth * 2 + 8),
                Height);

            TextRenderer.DrawText(g, _placeholderText, Font, textRect, _placeholderColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int diameter = radius * 2;
        var arcRect = new Rectangle(rect.X, rect.Y, diameter, diameter);

        // Top-left arc
        path.AddArc(arcRect, 180, 90);

        // Top-right arc
        arcRect.X = rect.Right - diameter;
        path.AddArc(arcRect, 270, 90);

        // Bottom-right arc
        arcRect.Y = rect.Bottom - diameter;
        path.AddArc(arcRect, 0, 90);

        // Bottom-left arc
        arcRect.X = rect.Left;
        path.AddArc(arcRect, 90, 90);

        path.CloseFigure();
        return path;
    }

    #endregion

    #region Event Handlers

    private void InnerTextBox_GotFocus(object? sender, EventArgs e)
    {
        _isFocused = true;
        Invalidate();
    }

    private void InnerTextBox_LostFocus(object? sender, EventArgs e)
    {
        _isFocused = false;
        Invalidate();
    }

    private void InnerTextBox_TextChanged(object? sender, EventArgs e)
    {
        TextChanged?.Invoke(this, e);
        Invalidate(); // For placeholder visibility
    }

    private void InnerTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        KeyDown?.Invoke(this, e);
    }

    private void InnerTextBox_KeyUp(object? sender, KeyEventArgs e)
    {
        KeyUp?.Invoke(this, e);
    }

    private void InnerTextBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        KeyPress?.Invoke(this, e);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _innerTextBox.Focus();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Focus the inner text box
    /// </summary>
    public new void Focus()
    {
        _innerTextBox.Focus();
    }

    /// <summary>
    /// Select all text
    /// </summary>
    public void SelectAll()
    {
        _innerTextBox.SelectAll();
    }

    /// <summary>
    /// Clear the text
    /// </summary>
    public void Clear()
    {
        _innerTextBox.Clear();
    }

    /// <summary>
    /// Apply theme colors
    /// </summary>
    public void ApplyTheme(Color background, Color text, Color unfocusedBorder, Color focusedBorder, Color placeholder)
    {
        _backgroundColor = background;
        _textColor = text;
        _unfocusedBorderColor = unfocusedBorder;
        _focusedBorderColor = focusedBorder;
        _placeholderColor = placeholder;

        _innerTextBox.BackColor = background;
        _innerTextBox.ForeColor = text;
        Invalidate();
    }

    #endregion
}
