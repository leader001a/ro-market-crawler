using System.Diagnostics;
using RoMarketCrawler.Helpers;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Models;
using RoMarketCrawler.Services;

namespace RoMarketCrawler.Controllers;

/// <summary>
/// Base class for tab controllers providing common functionality
/// </summary>
public abstract class BaseTabController : ITabController
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly TabPage _tabPage;
    protected float _baseFontSize = 12f;
    protected ThemeType _currentTheme = ThemeType.Dark;
    protected ThemeColors _colors = ThemeColors.Dark;
    protected bool _disposed = false;

    /// <inheritdoc/>
    public TabPage TabPage => _tabPage;

    /// <inheritdoc/>
    public abstract string TabName { get; }

    protected BaseTabController(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        _tabPage = new TabPage(TabName)
        {
            Padding = new Padding(10),
            UseVisualStyleBackColor = false
        };
    }

    /// <inheritdoc/>
    public abstract void Initialize();

    /// <inheritdoc/>
    public virtual void ApplyTheme(ThemeColors colors)
    {
        _colors = colors;
        // Compare background color to determine theme type (class reference comparison doesn't work)
        _currentTheme = colors.Background == ThemeColors.Dark.Background ? ThemeType.Dark : ThemeType.Classic;
        _tabPage.BackColor = colors.Background;
        _tabPage.ForeColor = colors.Text;
    }

    /// <inheritdoc/>
    public virtual void UpdateFontSize(float baseFontSize)
    {
        _baseFontSize = baseFontSize;
        // Apply font to all controls recursively
        ApplyFontSizeToAllControls(_tabPage, baseFontSize);
    }

    /// <summary>
    /// Recursively apply uniform font size to all controls
    /// </summary>
    protected void ApplyFontSizeToAllControls(Control parent, float baseFontSize)
    {
        var uniformFont = new Font("Malgun Gothic", baseFontSize);
        var uniformBoldFont = new Font("Malgun Gothic", baseFontSize, FontStyle.Bold);

        foreach (Control control in parent.Controls)
        {
            // Apply uniform font based on control type
            if (control is DataGridView dgv)
            {
                Helpers.DataGridViewHelper.UpdateFontSize(dgv, baseFontSize);
            }
            else if (control is ToolStrip toolStrip)
            {
                toolStrip.Font = uniformFont;
                ApplyFontToToolStripItems(toolStrip.Items, uniformFont);
            }
            else if (control is TextBox txt)
            {
                txt.Font = uniformFont;
            }
            else if (control is ComboBox combo)
            {
                combo.Font = uniformFont;
            }
            else if (control is CheckBox chk)
            {
                chk.Font = uniformFont;
            }
            else if (control is Button btn)
            {
                btn.Font = uniformBoldFont;
            }
            else if (control is LinkLabel link)
            {
                link.Font = uniformFont;
            }
            else if (control is Label lbl)
            {
                // Preserve bold style if already bold
                lbl.Font = lbl.Font.Bold ? uniformBoldFont : uniformFont;
            }
            else if (control is RichTextBox rtb)
            {
                rtb.Font = uniformFont;
            }
            else if (control is NumericUpDown nud)
            {
                nud.Font = uniformFont;
            }
            else if (control is Controls.RoundedButton rbtn)
            {
                rbtn.Font = uniformBoldFont;
            }
            else if (control is Controls.RoundedTextBox rtxt)
            {
                rtxt.Font = uniformFont;
            }
            else if (control is Controls.RoundedComboBox rcbo)
            {
                rcbo.Font = uniformFont;
            }

            // Recurse into child controls
            if (control.HasChildren)
            {
                ApplyFontSizeToAllControls(control, baseFontSize);
            }
        }
    }

    /// <summary>
    /// Apply uniform font to all ToolStrip items recursively
    /// </summary>
    private void ApplyFontToToolStripItems(ToolStripItemCollection items, Font uniformFont)
    {
        foreach (ToolStripItem item in items)
        {
            item.Font = uniformFont;

            if (item is ToolStripDropDownButton dropDown && dropDown.HasDropDownItems)
            {
                ApplyFontToToolStripItems(dropDown.DropDownItems, uniformFont);
            }
            else if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
            {
                ApplyFontToToolStripItems(menuItem.DropDownItems, uniformFont);
            }
        }
    }

    /// <inheritdoc/>
    public virtual void OnActivated()
    {
        Debug.WriteLine($"[{GetType().Name}] Tab activated");
    }

    /// <inheritdoc/>
    public virtual void OnDeactivated()
    {
        Debug.WriteLine($"[{GetType().Name}] Tab deactivated");
    }

    /// <inheritdoc/>
    public virtual Task SaveStateAsync()
    {
        return Task.CompletedTask;
    }

    #region Helper Methods

    /// <summary>
    /// Get a service from the service provider
    /// </summary>
    protected T GetService<T>() where T : notnull
    {
        return (T)ServiceProvider.GetService(typeof(T))!;
    }

    /// <summary>
    /// Apply standard label styling
    /// </summary>
    protected void ApplyLabelStyle(Label label, bool isHeader = false)
    {
        label.ForeColor = _colors.Text;
        label.Font = isHeader
            ? new Font("Malgun Gothic", _baseFontSize, FontStyle.Bold)
            : new Font("Malgun Gothic", _baseFontSize);
    }

    /// <summary>
    /// Apply standard button styling
    /// </summary>
    protected void ApplyButtonStyle(Button button, bool isPrimary = false)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            button.FlatStyle = FlatStyle.Flat;
            if (isPrimary)
            {
                button.BackColor = _colors.Accent;
                button.ForeColor = _colors.AccentText;
                button.FlatAppearance.BorderColor = _colors.Accent;
                button.FlatAppearance.MouseOverBackColor = _colors.AccentHover;
            }
            else
            {
                button.BackColor = _colors.Panel;
                button.ForeColor = _colors.Text;
                button.FlatAppearance.BorderColor = _colors.Border;
                button.FlatAppearance.MouseOverBackColor = _colors.GridAlt;
            }
        }
        else
        {
            button.FlatStyle = FlatStyle.Standard;
            button.BackColor = SystemColors.Control;
            button.ForeColor = SystemColors.ControlText;
            button.UseVisualStyleBackColor = true;
        }
    }

    /// <summary>
    /// Apply standard TextBox styling
    /// </summary>
    protected void ApplyTextBoxStyle(TextBox textBox)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            textBox.BackColor = _colors.Grid;
            textBox.ForeColor = _colors.Text;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
        else
        {
            textBox.BackColor = SystemColors.Window;
            textBox.ForeColor = SystemColors.WindowText;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
    }

    /// <summary>
    /// Apply standard ComboBox styling
    /// </summary>
    protected void ApplyComboBoxStyle(ComboBox comboBox)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            comboBox.BackColor = _colors.Grid;
            comboBox.ForeColor = _colors.Text;
            comboBox.FlatStyle = FlatStyle.Flat;
        }
        else
        {
            comboBox.BackColor = SystemColors.Window;
            comboBox.ForeColor = SystemColors.WindowText;
            comboBox.FlatStyle = FlatStyle.Standard;
        }
    }

    /// <summary>
    /// Apply standard DataGridView styling
    /// </summary>
    protected void ApplyDataGridViewStyle(DataGridView dgv)
    {
        DataGridViewHelper.ApplyTheme(dgv, _colors);
    }

    /// <summary>
    /// Apply standard Panel styling
    /// </summary>
    protected void ApplyPanelStyle(Panel panel, bool isBorderPanel = false)
    {
        panel.BackColor = isBorderPanel ? _colors.Border : _colors.Panel;
    }

    /// <summary>
    /// Apply standard TableLayoutPanel styling
    /// </summary>
    protected void ApplyTableLayoutPanelStyle(TableLayoutPanel tlp)
    {
        tlp.BackColor = _colors.Background;
    }

    /// <summary>
    /// Create a standard status label
    /// </summary>
    protected Label CreateStatusLabel()
    {
        var label = new Label
        {
            AutoSize = false,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Bottom,
            BackColor = _colors.Panel,
            ForeColor = _colors.Text,
            Font = new Font("Malgun Gothic", _baseFontSize),
            Padding = new Padding(5, 0, 0, 0)
        };
        return label;
    }

    /// <summary>
    /// Apply watermark image to a DataGridView
    /// </summary>
    protected void ApplyWatermark(DataGridView dgv, Image watermark)
    {
        dgv.Paint += (s, e) =>
        {
            if (s is not DataGridView grid) return;

            var targetHeight = grid.ClientSize.Height * 0.8;
            var scale = (float)targetHeight / watermark.Height;
            var watermarkWidth = (int)(watermark.Width * scale);
            var watermarkHeight = (int)(watermark.Height * scale);
            var x = (grid.ClientSize.Width - watermarkWidth) / 2;
            var contentTop = grid.ColumnHeadersHeight;
            var contentHeight = grid.ClientSize.Height - contentTop;
            var y = contentTop + (contentHeight - watermarkHeight) / 2;

            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(watermark, x, y, watermarkWidth, watermarkHeight);
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _tabPage.Dispose();
        }

        _disposed = true;
    }

    #endregion
}
