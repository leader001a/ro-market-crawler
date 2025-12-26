using RoMarketCrawler.Models;

namespace RoMarketCrawler.Helpers;

/// <summary>
/// Helper class for DataGridView styling and configuration.
/// Provides consistent styling across all grid views in the application.
/// </summary>
public static class DataGridViewHelper
{
    /// <summary>
    /// Apply dark theme styling to a DataGridView
    /// </summary>
    public static void ApplyDarkTheme(
        DataGridView dgv,
        Color gridColor,
        Color gridAltColor,
        Color textColor,
        Color accentColor,
        Color accentTextColor,
        Color borderColor)
    {
        dgv.BorderStyle = BorderStyle.FixedSingle;
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        dgv.EnableHeadersVisualStyles = false;
        dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

        // Header style
        dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(55, 55, 68);
        dgv.ColumnHeadersDefaultCellStyle.ForeColor = textColor;
        dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(55, 55, 68);

        // Cell style
        dgv.DefaultCellStyle.BackColor = gridColor;
        dgv.DefaultCellStyle.ForeColor = textColor;
        dgv.DefaultCellStyle.SelectionBackColor = accentColor;
        dgv.DefaultCellStyle.SelectionForeColor = accentTextColor;

        // Alternating row style
        dgv.AlternatingRowsDefaultCellStyle.BackColor = gridAltColor;
        dgv.AlternatingRowsDefaultCellStyle.ForeColor = textColor;

        // Grid and border colors
        dgv.GridColor = borderColor;
        dgv.BackgroundColor = gridColor;
    }

    /// <summary>
    /// Apply light theme styling to a DataGridView
    /// </summary>
    public static void ApplyLightTheme(DataGridView dgv)
    {
        dgv.BorderStyle = BorderStyle.Fixed3D;
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        dgv.EnableHeadersVisualStyles = true;
        dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Raised;

        // Header style
        dgv.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        dgv.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
        dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = SystemColors.Highlight;

        // Cell style
        dgv.DefaultCellStyle.BackColor = SystemColors.Window;
        dgv.DefaultCellStyle.ForeColor = SystemColors.WindowText;
        dgv.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
        dgv.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;

        // Alternating row style
        dgv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 248, 248);
        dgv.AlternatingRowsDefaultCellStyle.ForeColor = SystemColors.WindowText;

        // Grid and border colors
        dgv.GridColor = SystemColors.ControlDark;
        dgv.BackgroundColor = SystemColors.Window;
    }

    /// <summary>
    /// Apply theme-based styling to a DataGridView
    /// </summary>
    public static void ApplyTheme(
        DataGridView dgv,
        ThemeType theme,
        Color gridColor,
        Color gridAltColor,
        Color textColor,
        Color accentColor,
        Color accentTextColor,
        Color borderColor)
    {
        if (theme == ThemeType.Dark)
        {
            ApplyDarkTheme(dgv, gridColor, gridAltColor, textColor, accentColor, accentTextColor, borderColor);
        }
        else
        {
            ApplyLightTheme(dgv);
        }
    }

    /// <summary>
    /// Apply theme-based styling to a DataGridView using ThemeColors
    /// </summary>
    public static void ApplyTheme(DataGridView dgv, ThemeColors colors)
    {
        var theme = colors == ThemeColors.Dark ? ThemeType.Dark : ThemeType.Classic;
        ApplyTheme(dgv, theme, colors.Grid, colors.GridAlt, colors.Text, colors.Accent, colors.AccentText, colors.Border);
    }

    /// <summary>
    /// Configure common DataGridView settings for read-only display
    /// </summary>
    public static void ConfigureForDisplay(DataGridView dgv)
    {
        dgv.ReadOnly = true;
        dgv.AllowUserToAddRows = false;
        dgv.AllowUserToDeleteRows = false;
        dgv.AllowUserToResizeRows = false;
        dgv.RowHeadersVisible = false;
        dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgv.MultiSelect = false;
        dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }

    /// <summary>
    /// Configure DataGridView for editable grid with specific selection mode
    /// </summary>
    public static void ConfigureForEditing(
        DataGridView dgv,
        DataGridViewSelectionMode selectionMode = DataGridViewSelectionMode.CellSelect,
        bool multiSelect = true)
    {
        dgv.ReadOnly = false;
        dgv.AllowUserToAddRows = false;
        dgv.AllowUserToDeleteRows = false;
        dgv.RowHeadersVisible = false;
        dgv.SelectionMode = selectionMode;
        dgv.MultiSelect = multiSelect;
        dgv.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
    }

    /// <summary>
    /// Update font size for DataGridView (uniform font size)
    /// </summary>
    public static void UpdateFontSize(DataGridView dgv, float baseFontSize)
    {
        dgv.DefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize);
        dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", baseFontSize, FontStyle.Bold);
    }

    /// <summary>
    /// Add a text column with specified configuration
    /// </summary>
    public static DataGridViewTextBoxColumn AddTextColumn(
        DataGridView dgv,
        string name,
        string headerText,
        string dataPropertyName,
        int width = 100,
        bool visible = true,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = headerText,
            DataPropertyName = dataPropertyName,
            Width = width,
            Visible = visible,
            DefaultCellStyle = { Alignment = alignment }
        };
        dgv.Columns.Add(column);
        return column;
    }

    /// <summary>
    /// Add a combobox column with specified configuration
    /// </summary>
    public static DataGridViewComboBoxColumn AddComboBoxColumn(
        DataGridView dgv,
        string name,
        string headerText,
        string dataPropertyName,
        object dataSource,
        string displayMember,
        string valueMember,
        int width = 100)
    {
        var column = new DataGridViewComboBoxColumn
        {
            Name = name,
            HeaderText = headerText,
            DataPropertyName = dataPropertyName,
            DataSource = dataSource,
            DisplayMember = displayMember,
            ValueMember = valueMember,
            Width = width,
            FlatStyle = FlatStyle.Flat
        };
        dgv.Columns.Add(column);
        return column;
    }
}
