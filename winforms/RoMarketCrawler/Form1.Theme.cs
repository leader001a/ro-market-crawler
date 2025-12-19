using System.Runtime.InteropServices;

namespace RoMarketCrawler;

/// <summary>
/// Form1 partial class - Theme and styling methods
/// </summary>
using RoMarketCrawler.Models;

public partial class Form1
{
    #region Dark Mode Scrollbar API

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern void FlushMenuThemes();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private static bool _darkModeInitialized = false;
    private readonly HashSet<Control> _scrollBarHandlerInitialized = new();

    /// <summary>
    /// Initialize dark mode support for the application (call once at startup)
    /// </summary>
    private static void InitializeDarkModeSupport()
    {
        if (_darkModeInitialized) return;

        try
        {
            // SetPreferredAppMode: 0=Default, 1=AllowDark, 2=ForceDark, 3=ForceLight
            SetPreferredAppMode(1); // AllowDark
            _darkModeInitialized = true;
        }
        catch
        {
            // Ignore errors on older Windows versions
        }
    }

    /// <summary>
    /// Apply dark mode to the window title bar
    /// </summary>
    private void ApplyDarkModeToTitleBar(bool isDark)
    {
        try
        {
            int useImmersiveDarkMode = isDark ? 1 : 0;

            // Try Windows 10 20H1+ attribute first
            if (DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int)) != 0)
            {
                // Fall back to pre-20H1 attribute
                DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
            }
        }
        catch
        {
            // Ignore errors on older Windows versions
        }
    }

    /// <summary>
    /// Apply dark or light scrollbar theme to a control and all its children recursively
    /// </summary>
    private void ApplyScrollBarTheme(Control control, bool isDark)
    {
        string themeName = isDark ? "DarkMode_Explorer" : "Explorer";

        // Apply to the control itself
        ApplyThemeToControlHandle(control, themeName);

        // Recursively apply to all existing child controls
        foreach (Control child in control.Controls)
        {
            ApplyScrollBarTheme(child, isDark);
        }

        // Invalidate the control and all children to force redraw
        if (control.IsHandleCreated)
        {
            control.Invalidate(true);
        }
    }

    /// <summary>
    /// Apply theme to a single control's handle
    /// </summary>
    private void ApplyThemeToControlHandle(Control control, string themeName)
    {
        if (control.IsHandleCreated)
        {
            SetWindowTheme(control.Handle, themeName, null);
        }
        else
        {
            // Capture themeName for the closure
            string capturedTheme = themeName;
            control.HandleCreated += (s, e) =>
            {
                if (s is Control c)
                {
                    SetWindowTheme(c.Handle, capturedTheme, null);
                }
            };
        }
    }

    #endregion

    #region Theme Application

    private void ApplyThemeColors()
    {
        if (_currentTheme == ThemeType.Dark)
        {
            // kafra.kr Dark Theme - optimized for visibility
            ThemeBackground = Color.FromArgb(30, 30, 35);
            ThemePanel = Color.FromArgb(45, 45, 55);           // Slightly brighter for headers
            ThemeGrid = Color.FromArgb(35, 35, 42);
            ThemeGridAlt = Color.FromArgb(45, 45, 55);         // More contrast with ThemeGrid
            ThemeAccent = Color.FromArgb(70, 130, 200);
            ThemeAccentHover = Color.FromArgb(90, 150, 220);
            ThemeAccentText = Color.White;
            ThemeText = Color.FromArgb(230, 230, 235);
            ThemeTextMuted = Color.FromArgb(160, 160, 170);
            ThemeLinkColor = Color.FromArgb(100, 180, 255);    // Light blue for links
            ThemeBorder = Color.FromArgb(70, 75, 90);          // Brighter grid lines
            ThemeSaleColor = Color.FromArgb(100, 200, 120);
            ThemeBuyColor = Color.FromArgb(255, 180, 80);
        }
        else // Classic
        {
            // Windows Classic Theme - use system colors for native look
            ThemeBackground = SystemColors.Control;
            ThemePanel = SystemColors.Control;
            ThemeGrid = SystemColors.Window;
            ThemeGridAlt = Color.FromArgb(240, 240, 245);    // Subtle alternating row
            ThemeAccent = SystemColors.Highlight;
            ThemeAccentHover = SystemColors.HotTrack;
            ThemeAccentText = SystemColors.HighlightText;
            ThemeText = SystemColors.WindowText;             // Use WindowText for better contrast
            ThemeTextMuted = SystemColors.GrayText;
            ThemeLinkColor = Color.FromArgb(0, 102, 204);    // Standard link blue
            ThemeBorder = SystemColors.ActiveBorder;         // More visible border
            ThemeSaleColor = Color.FromArgb(0, 128, 0);      // Darker green for light bg
            ThemeBuyColor = Color.FromArgb(180, 100, 0);     // Darker orange for light bg
        }
    }

    private void SetTheme(ThemeType theme)
    {
        if (_currentTheme == theme) return;

        _currentTheme = theme;
        ApplyThemeColors();
        ApplyDarkModeToTitleBar(_currentTheme == ThemeType.Dark);
        ApplyThemeToAllControls(this);
        UpdateThemeMenuChecks();
        _autoCompleteDropdown?.UpdateTheme(ThemeGrid, ThemeText, ThemeAccent, ThemeBorder);
        SaveSettings();
    }

    private void ApplyThemeToAllControls(Control parent)
    {
        // Apply to form
        if (parent == this)
        {
            BackColor = ThemeBackground;
            ForeColor = ThemeText;
        }

        foreach (Control control in parent.Controls)
        {
            // Apply based on control type
            if (control is MenuStrip menu)
            {
                if (_currentTheme == ThemeType.Dark)
                {
                    menu.BackColor = ThemePanel;
                    menu.ForeColor = ThemeText;
                    menu.Renderer = new DarkMenuRenderer();

                    // Apply dark theme to all dropdown menus
                    foreach (ToolStripItem item in menu.Items)
                    {
                        if (item is ToolStripMenuItem menuItem)
                        {
                            SetupDarkThemeDropDowns(menuItem);
                        }
                    }
                }
                else
                {
                    // Classic theme - use system default rendering
                    menu.Renderer = new ToolStripProfessionalRenderer();
                    menu.BackColor = SystemColors.MenuBar;
                    menu.ForeColor = SystemColors.MenuText;

                    // Reset all dropdown menu renderers to system default
                    foreach (ToolStripItem item in menu.Items)
                    {
                        if (item is ToolStripMenuItem menuItem)
                        {
                            ResetDropDownRenderers(menuItem);
                        }
                    }

                    // Flush Windows menu theme cache to force refresh
                    try { FlushMenuThemes(); } catch { }
                }
                // Update menu item colors
                foreach (ToolStripItem item in menu.Items)
                {
                    item.ForeColor = _currentTheme == ThemeType.Dark ? ThemeText : SystemColors.MenuText;
                    if (item is ToolStripMenuItem menuItem)
                    {
                        UpdateMenuItemColors(menuItem);
                    }
                }
            }
            else if (control is TabControl tab)
            {
                tab.BackColor = ThemeBackground;
                tab.ForeColor = ThemeText;
                tab.Refresh();  // Force immediate redraw of owner-drawn tabs
            }
            else if (control is TabPage tabPage)
            {
                tabPage.BackColor = ThemeBackground;
                tabPage.ForeColor = ThemeText;
            }
            else if (control is DataGridView dgv)
            {
                ApplyDataGridViewStyle(dgv);
            }
            else if (control is TextBox txt)
            {
                if (_currentTheme == ThemeType.Dark)
                {
                    txt.BackColor = ThemeGrid;
                    txt.ForeColor = ThemeText;
                    txt.BorderStyle = BorderStyle.FixedSingle;
                }
                else
                {
                    txt.BackColor = SystemColors.Window;
                    txt.ForeColor = SystemColors.WindowText;
                    // Use FixedSingle for better visibility on modern Windows
                    txt.BorderStyle = BorderStyle.FixedSingle;
                }
                ApplyScrollBarTheme(txt, _currentTheme == ThemeType.Dark);
            }
            else if (control is ComboBox combo)
            {
                if (_currentTheme == ThemeType.Dark)
                {
                    combo.BackColor = ThemeGrid;
                    combo.ForeColor = ThemeText;
                    combo.FlatStyle = FlatStyle.Flat;
                }
                else
                {
                    combo.BackColor = SystemColors.Window;
                    combo.ForeColor = SystemColors.WindowText;
                    combo.FlatStyle = FlatStyle.Standard;
                }
            }
            else if (control is Button btn)
            {
                if (_currentTheme == ThemeType.Dark)
                {
                    btn.FlatStyle = FlatStyle.Flat;
                    bool isPrimary = btn.Tag as string == "Primary";
                    if (isPrimary)
                    {
                        btn.BackColor = ThemeAccent;
                        btn.ForeColor = ThemeAccentText;
                        btn.FlatAppearance.BorderColor = ThemeAccent;
                        btn.FlatAppearance.MouseOverBackColor = ThemeAccentHover;
                    }
                    else
                    {
                        btn.BackColor = ThemePanel;
                        btn.ForeColor = ThemeText;
                        btn.FlatAppearance.BorderColor = ThemeBorder;
                        btn.FlatAppearance.MouseOverBackColor = ThemeGridAlt;
                    }
                }
                else
                {
                    // Classic theme - use standard Windows button appearance
                    // Reset FlatAppearance to defaults first (clears dark theme settings)
                    btn.FlatAppearance.BorderColor = SystemColors.ControlDark;
                    btn.FlatAppearance.BorderSize = 1;
                    btn.FlatAppearance.MouseOverBackColor = Color.Empty;
                    btn.FlatAppearance.MouseDownBackColor = Color.Empty;
                    // Then set standard style
                    btn.FlatStyle = FlatStyle.Standard;
                    btn.BackColor = SystemColors.Control;
                    btn.ForeColor = SystemColors.ControlText;
                    btn.UseVisualStyleBackColor = true;
                }
            }
            else if (control is Label lbl)
            {
                // Check if this is a search history link (marked with tuple tag)
                if (lbl.Tag is (string tagType, string _) && tagType == "SearchHistoryLink")
                {
                    lbl.ForeColor = ThemeAccent;  // Keep accent color for clickable links
                }
                else if (control == _lblItemName)
                {
                    lbl.ForeColor = ThemeLinkColor;
                    lbl.BackColor = ThemeGrid;
                }
                // Update status labels (those with non-transparent background)
                else if (lbl.BackColor != Color.Transparent && lbl.BackColor != lbl.Parent?.BackColor)
                {
                    lbl.ForeColor = ThemeText;
                    lbl.BackColor = ThemePanel;
                }
                else
                {
                    lbl.ForeColor = ThemeText;
                }
            }
            else if (control is Panel panel)
            {
                // Check if it's a border panel
                if (panel.Padding == new Padding(1))
                {
                    panel.BackColor = ThemeBorder;
                }
                else
                {
                    panel.BackColor = ThemePanel;
                }
            }
            else if (control is TableLayoutPanel tlp)
            {
                tlp.BackColor = ThemePanel;
            }
            else if (control is FlowLayoutPanel flp)
            {
                flp.BackColor = ThemePanel;
            }
            else if (control is CheckBox checkBox)
            {
                checkBox.ForeColor = _currentTheme == ThemeType.Dark ? ThemeText : SystemColors.ControlText;
            }
            else if (control is PictureBox pic)
            {
                pic.BackColor = ThemeGrid;
            }
            else if (control is ListBox listBox)
            {
                listBox.BackColor = _currentTheme == ThemeType.Dark ? ThemeGrid : SystemColors.Window;
                listBox.ForeColor = _currentTheme == ThemeType.Dark ? ThemeText : SystemColors.WindowText;
                listBox.BorderStyle = _currentTheme == ThemeType.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                ApplyScrollBarTheme(listBox, _currentTheme == ThemeType.Dark);
            }
            else if (control is TreeView treeView)
            {
                treeView.BackColor = _currentTheme == ThemeType.Dark ? ThemeGrid : SystemColors.Window;
                treeView.ForeColor = _currentTheme == ThemeType.Dark ? ThemeText : SystemColors.WindowText;
                treeView.BorderStyle = _currentTheme == ThemeType.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                ApplyScrollBarTheme(treeView, _currentTheme == ThemeType.Dark);
            }
            else if (control is ListView listView)
            {
                listView.BackColor = _currentTheme == ThemeType.Dark ? ThemeGrid : SystemColors.Window;
                listView.ForeColor = _currentTheme == ThemeType.Dark ? ThemeText : SystemColors.WindowText;
                listView.BorderStyle = _currentTheme == ThemeType.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                ApplyScrollBarTheme(listView, _currentTheme == ThemeType.Dark);
            }
            else if (control is RichTextBox richTextBox)
            {
                richTextBox.BackColor = _currentTheme == ThemeType.Dark ? ThemeGrid : SystemColors.Window;
                richTextBox.ForeColor = _currentTheme == ThemeType.Dark ? ThemeText : SystemColors.WindowText;
                richTextBox.BorderStyle = _currentTheme == ThemeType.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                ApplyScrollBarTheme(richTextBox, _currentTheme == ThemeType.Dark);
            }
            else if (control is StatusStrip statusStrip)
            {
                // Use brighter/darker colors for better visibility
                var statusTextColor = _currentTheme == ThemeType.Dark
                    ? Color.FromArgb(200, 200, 200)  // Brighter for dark theme
                    : Color.FromArgb(60, 60, 60);     // Darker for light theme
                var statusFont = new Font("Malgun Gothic", _baseFontSize - 2, FontStyle.Bold);

                statusStrip.BackColor = _currentTheme == ThemeType.Dark ? ThemePanel : SystemColors.Control;
                statusStrip.ForeColor = statusTextColor;
                statusStrip.Font = statusFont;
                foreach (ToolStripItem item in statusStrip.Items)
                {
                    item.ForeColor = statusTextColor;
                    item.Font = statusFont;
                }
            }
            else if (control is ToolStrip toolStrip)
            {
                ApplyToolStripStyle(toolStrip);
            }
            else if (control is HScrollBar || control is VScrollBar)
            {
                // Apply scrollbar theme to standalone scrollbars
                ApplyScrollBarTheme(control, _currentTheme == ThemeType.Dark);
            }

            // Recurse
            if (control.HasChildren)
            {
                ApplyThemeToAllControls(control);
            }
        }
    }

    private void ApplyToolStripStyle(ToolStrip toolStrip)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            toolStrip.BackColor = ThemePanel;
            toolStrip.ForeColor = ThemeText;
            toolStrip.Renderer = new DarkToolStripRenderer();
        }
        else
        {
            // Classic theme - use professional renderer with default system colors
            toolStrip.Renderer = new ToolStripProfessionalRenderer();
            toolStrip.BackColor = SystemColors.Control;
            toolStrip.ForeColor = SystemColors.ControlText;
        }

        // Apply theme to all items
        foreach (ToolStripItem item in toolStrip.Items)
        {
            ApplyToolStripItemStyle(item);
        }
    }

    private void ApplyToolStripItemStyle(ToolStripItem item)
    {
        if (item is ToolStripComboBox combo)
        {
            if (_currentTheme == ThemeType.Dark)
            {
                combo.BackColor = ThemeGrid;
                combo.ForeColor = ThemeText;
                combo.ComboBox.BackColor = ThemeGrid;
                combo.ComboBox.ForeColor = ThemeText;
                combo.ComboBox.FlatStyle = FlatStyle.Flat;
            }
            else
            {
                combo.BackColor = SystemColors.Window;
                combo.ForeColor = SystemColors.WindowText;
                combo.ComboBox.BackColor = SystemColors.Window;
                combo.ComboBox.ForeColor = SystemColors.WindowText;
                combo.ComboBox.FlatStyle = FlatStyle.Standard;
            }
        }
        else if (item is ToolStripTextBox textBox)
        {
            if (_currentTheme == ThemeType.Dark)
            {
                textBox.BackColor = ThemeGrid;
                textBox.ForeColor = ThemeText;
                textBox.TextBox.BackColor = ThemeGrid;
                textBox.TextBox.ForeColor = ThemeText;
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
            else
            {
                textBox.BackColor = SystemColors.Window;
                textBox.ForeColor = SystemColors.WindowText;
                textBox.TextBox.BackColor = SystemColors.Window;
                textBox.TextBox.ForeColor = SystemColors.WindowText;
                // Use FixedSingle for better visibility on modern Windows
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
        }
        else if (item is ToolStripButton button)
        {
            if (_currentTheme == ThemeType.Dark)
            {
                button.ForeColor = ThemeText;
                // Don't set BackColor for buttons in dark theme - renderer handles it
            }
            else
            {
                button.ForeColor = SystemColors.ControlText;
                button.BackColor = SystemColors.Control;
            }
        }
        else if (item is ToolStripDropDownButton dropDown)
        {
            if (_currentTheme == ThemeType.Dark)
            {
                dropDown.ForeColor = ThemeText;
            }
            else
            {
                dropDown.ForeColor = SystemColors.ControlText;
                dropDown.BackColor = SystemColors.Control;
            }

            // Apply to dropdown items
            foreach (ToolStripItem subItem in dropDown.DropDownItems)
            {
                ApplyToolStripItemStyle(subItem);
            }
        }
        else if (item is ToolStripLabel label)
        {
            if (_currentTheme == ThemeType.Dark)
            {
                label.ForeColor = ThemeText;
            }
            else
            {
                label.ForeColor = SystemColors.ControlText;
            }
        }
        else if (item is ToolStripControlHost host)
        {
            // Handle hosted controls like Panel, NumericUpDown, etc.
            if (host.Control is Panel panel)
            {
                ApplyThemeToAllControls(panel);
                if (_currentTheme == ThemeType.Dark)
                {
                    panel.BackColor = ThemePanel;
                }
                else
                {
                    panel.BackColor = SystemColors.Control;
                }
            }
            else if (host.Control is ProgressBar progress)
            {
                // ProgressBar doesn't need special theming
            }
        }
    }

    private void UpdateMenuItemColors(ToolStripMenuItem menuItem)
    {
        var textColor = _currentTheme == ThemeType.Dark ? ThemeText : SystemColors.MenuText;
        menuItem.ForeColor = textColor;
        foreach (ToolStripItem subItem in menuItem.DropDownItems)
        {
            subItem.ForeColor = textColor;
            if (subItem is ToolStripMenuItem subMenuItem)
            {
                UpdateMenuItemColors(subMenuItem);
            }
        }
    }

    private readonly HashSet<ToolStripMenuItem> _dropDownHandlerAttached = new();

    /// <summary>
    /// Recursively setup dropdown menu theme handlers and reset renderers for classic theme
    /// </summary>
    private void ResetDropDownRenderers(ToolStripMenuItem menuItem)
    {
        // Attach DropDownOpening event handler (only once per menu item)
        if (!_dropDownHandlerAttached.Contains(menuItem))
        {
            _dropDownHandlerAttached.Add(menuItem);
            menuItem.DropDownOpening += MenuItem_DropDownOpening;
        }

        // Reset the dropdown's renderer to system default
        if (menuItem.HasDropDownItems)
        {
            ApplyClassicThemeToDropDown(menuItem.DropDown);

            // Recursively reset sub-menu renderers
            foreach (ToolStripItem subItem in menuItem.DropDownItems)
            {
                if (subItem is ToolStripMenuItem subMenuItem)
                {
                    ResetDropDownRenderers(subMenuItem);
                }
            }
        }
    }

    /// <summary>
    /// Event handler for dropdown opening - enforces theme on each open
    /// </summary>
    private void MenuItem_DropDownOpening(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem menuItem)
        {
            if (_currentTheme == ThemeType.Dark)
            {
                ApplyDarkThemeToDropDown(menuItem.DropDown);
            }
            else
            {
                ApplyClassicThemeToDropDown(menuItem.DropDown);
            }
        }
    }

    /// <summary>
    /// Apply classic (system) theme to a dropdown menu
    /// </summary>
    private void ApplyClassicThemeToDropDown(ToolStripDropDown dropDown)
    {
        // Use ToolStripProfessionalRenderer with default colors for proper system look
        dropDown.Renderer = new ToolStripProfessionalRenderer();
        dropDown.BackColor = SystemColors.Menu;
        dropDown.ForeColor = SystemColors.MenuText;

        // Also update all items in the dropdown
        foreach (ToolStripItem item in dropDown.Items)
        {
            item.BackColor = SystemColors.Menu;
            item.ForeColor = SystemColors.MenuText;
        }
    }

    /// <summary>
    /// Recursively setup dark theme for dropdown menus
    /// </summary>
    private void SetupDarkThemeDropDowns(ToolStripMenuItem menuItem)
    {
        // Attach DropDownOpening event handler (only once per menu item)
        if (!_dropDownHandlerAttached.Contains(menuItem))
        {
            _dropDownHandlerAttached.Add(menuItem);
            menuItem.DropDownOpening += MenuItem_DropDownOpening;
        }

        // Apply dark theme to dropdown if it has items
        if (menuItem.HasDropDownItems)
        {
            ApplyDarkThemeToDropDown(menuItem.DropDown);

            // Recursively setup sub-menu dropdowns
            foreach (ToolStripItem subItem in menuItem.DropDownItems)
            {
                if (subItem is ToolStripMenuItem subMenuItem)
                {
                    SetupDarkThemeDropDowns(subMenuItem);
                }
            }
        }
    }

    /// <summary>
    /// Apply dark theme to a dropdown menu
    /// </summary>
    private void ApplyDarkThemeToDropDown(ToolStripDropDown dropDown)
    {
        dropDown.Renderer = new DarkMenuRenderer();
        dropDown.BackColor = Color.FromArgb(40, 40, 48);
        dropDown.ForeColor = ThemeText;

        // Also update all items in the dropdown
        foreach (ToolStripItem item in dropDown.Items)
        {
            item.BackColor = Color.FromArgb(40, 40, 48);
            item.ForeColor = ThemeText;
        }
    }

    private void UpdateThemeMenuChecks()
    {
        if (_menuStrip?.Items[0] is ToolStripMenuItem viewMenu)
        {
            // Find theme menu by searching for menu item with theme items
            foreach (var menuItem in viewMenu.DropDownItems.OfType<ToolStripMenuItem>())
            {
                // Check if this is the theme menu by looking for ThemeType tags
                var hasThemeItems = menuItem.DropDownItems.OfType<ToolStripMenuItem>()
                    .Any(sub => sub.Tag is ThemeType);

                if (hasThemeItems)
                {
                    foreach (var item in menuItem.DropDownItems.OfType<ToolStripMenuItem>())
                    {
                        if (item.Tag is ThemeType theme)
                        {
                            item.Checked = _currentTheme == theme;
                        }
                    }
                    break;
                }
            }
        }
    }

    #endregion

    #region Menu Renderer

    // Custom menu renderer for dark theme
    private class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }
    }

    private class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(60, 60, 70);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 70);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 70);
        public override Color MenuItemBorder => Color.FromArgb(70, 130, 200);
        public override Color MenuBorder => Color.FromArgb(55, 55, 65);
        public override Color ToolStripDropDownBackground => Color.FromArgb(40, 40, 48);
        public override Color ImageMarginGradientBegin => Color.FromArgb(40, 40, 48);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(40, 40, 48);
        public override Color ImageMarginGradientEnd => Color.FromArgb(40, 40, 48);
        public override Color SeparatorDark => Color.FromArgb(55, 55, 65);
        public override Color SeparatorLight => Color.FromArgb(55, 55, 65);
    }

    #endregion

    #region TabControl Styling

    private void ApplyTabControlStyle(TabControl tabControl)
    {
        tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabControl.DrawItem += TabControl_DrawItem;
        tabControl.Paint += TabControl_Paint;
        tabControl.Padding = new Point(12, 5);
        tabControl.ItemSize = new Size(180, 30);
    }

    private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabControl) return;

        var tab = tabControl.TabPages[e.Index];
        var isSelected = e.Index == tabControl.SelectedIndex;
        var bounds = e.Bounds;
        var stripBgColor = _currentTheme == ThemeType.Dark ? ThemeBackground : SystemColors.Control;
        using var stripBgBrush = new SolidBrush(stripBgColor);

        // First, cover the ENTIRE top strip with background color
        e.Graphics.FillRectangle(stripBgBrush, 0, 0, tabControl.Width, 5);

        // Cover area to the LEFT of first tab
        if (e.Index == 0)
        {
            e.Graphics.FillRectangle(stripBgBrush, 0, 0, bounds.X + 5, tabControl.ItemSize.Height + 15);
        }

        // Draw the tab (includes extended border coverage)
        if (_currentTheme == ThemeType.Dark)
        {
            DrawDarkThemeTab(e.Graphics, tabControl, bounds, tab.Text, isSelected);
        }
        else
        {
            DrawClassicThemeTab(e.Graphics, tabControl, bounds, tab.Text, isSelected);
        }

        // After drawing tab, fill gap to the RIGHT of this tab (more aggressively)
        if (e.Index < tabControl.TabCount - 1)
        {
            var nextBounds = tabControl.GetTabRect(e.Index + 1);
            var gapStart = bounds.Right - 5;
            var gapWidth = nextBounds.X - bounds.Right + 10;
            if (gapWidth > 0)
            {
                e.Graphics.FillRectangle(stripBgBrush, gapStart, 0, gapWidth, tabControl.ItemSize.Height + 15);
            }
        }

        // Fill empty strip area after last tab
        if (e.Index == tabControl.TabCount - 1)
        {
            var emptyAreaX = bounds.Right - 2;
            var emptyAreaWidth = tabControl.Width - bounds.Right + 5;
            if (emptyAreaWidth > 0)
            {
                e.Graphics.FillRectangle(stripBgBrush, emptyAreaX, 0, emptyAreaWidth, tabControl.ItemSize.Height + 15);
            }
        }

        // Cover bottom border of tab strip (line between tabs and content)
        e.Graphics.FillRectangle(stripBgBrush, 0, tabControl.ItemSize.Height, tabControl.Width, 15);
    }

    private void DrawDarkThemeTab(Graphics g, TabControl tabControl, Rectangle bounds, string text, bool isSelected)
    {
        Color tabColor = isSelected ? ThemeAccent : ThemeBackground;
        Color textColor = isSelected ? ThemeAccentText : ThemeTextMuted;

        // Step 1: Fill VERY LARGE extended area with background color (aggressive border removal)
        using var borderBrush = new SolidBrush(ThemeBackground);

        // Cover much larger area to ensure all system borders are hidden (especially left/right edges)
        var extendedArea = new Rectangle(bounds.X - 8, bounds.Y - 5, bounds.Width + 16, bounds.Height + 15);
        g.FillRectangle(borderBrush, extendedArea);

        // Step 2: Fill the actual tab content area with tab color
        using var tabBrush = new SolidBrush(tabColor);
        g.FillRectangle(tabBrush, bounds);

        // Step 3: Draw text
        using var textBrush = new SolidBrush(textColor);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, tabControl.Font, textBrush, bounds, sf);
    }

    private void DrawClassicThemeTab(Graphics g, TabControl tabControl, Rectangle bounds, string text, bool isSelected)
    {
        Color tabColor = isSelected ? SystemColors.Window : SystemColors.Control;
        Color textColor = SystemColors.ControlText;

        // Step 1: Fill VERY LARGE extended area with background color (aggressive border removal)
        using var borderBrush = new SolidBrush(SystemColors.Control);
        var extendedArea = new Rectangle(bounds.X - 5, bounds.Y - 5, bounds.Width + 10, bounds.Height + 12);
        g.FillRectangle(borderBrush, extendedArea);

        // Step 2: Fill the actual tab content area with tab color
        using var tabBrush = new SolidBrush(tabColor);
        g.FillRectangle(tabBrush, bounds);

        // Step 3: Draw text
        using var textBrush = new SolidBrush(textColor);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, tabControl.Font, textBrush, bounds, sf);
    }


    private void TabControl_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not TabControl tabControl) return;

        var borderCoverColor = _currentTheme == ThemeType.Dark ? ThemeBackground : SystemColors.Control;
        using var coverBrush = new SolidBrush(borderCoverColor);

        var tabStripHeight = tabControl.ItemSize.Height;
        var totalHeight = tabControl.Height;
        var totalWidth = tabControl.Width;

        // Cover all edge areas - entire control height for full border removal

        // Left edge - full height (covers content area border too)
        e.Graphics.FillRectangle(coverBrush, 0, 0, 4, totalHeight);

        // Top edge (full width)
        e.Graphics.FillRectangle(coverBrush, 0, 0, totalWidth, 6);

        // Right edge - full height (covers content area border too)
        e.Graphics.FillRectangle(coverBrush, totalWidth - 4, 0, 4, totalHeight);

        // Bottom edge - full width (covers content area border)
        e.Graphics.FillRectangle(coverBrush, 0, totalHeight - 4, totalWidth, 4);

        // Bottom of tab strip (border between tabs and content)
        e.Graphics.FillRectangle(coverBrush, 0, tabStripHeight - 2, totalWidth, 18);

        // Also fill the area before first tab if there's any gap
        if (tabControl.TabCount > 0)
        {
            var firstTabRect = tabControl.GetTabRect(0);
            if (firstTabRect.X > 0)
            {
                e.Graphics.FillRectangle(coverBrush, 0, 0, firstTabRect.X + 3, tabStripHeight + 15);
            }
        }
    }

    private void ApplyTabPageStyle(TabPage tabPage)
    {
        tabPage.BackColor = ThemeBackground;
        tabPage.ForeColor = ThemeText;
    }

    #endregion

    #region Control Styling Methods

    private void ApplyButtonStyle(Button button, bool isPrimary = true)
    {
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Malgun Gothic", _baseFontSize - 3, FontStyle.Bold);
        button.Height = 30;
        button.Tag = isPrimary ? "Primary" : "Secondary";

        if (_currentTheme == ThemeType.Dark)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;

            if (isPrimary)
            {
                button.BackColor = ThemeAccent;
                button.ForeColor = ThemeAccentText;
                button.FlatAppearance.BorderColor = ThemeAccent;
                button.FlatAppearance.MouseOverBackColor = ThemeAccentHover;
            }
            else
            {
                button.BackColor = ThemePanel;
                button.ForeColor = ThemeText;
                button.FlatAppearance.BorderColor = ThemeBorder;
                button.FlatAppearance.MouseOverBackColor = ThemeGridAlt;
            }
        }
        else
        {
            // Classic theme - use standard Windows button style
            button.FlatStyle = FlatStyle.Standard;
            button.BackColor = SystemColors.Control;
            button.ForeColor = SystemColors.ControlText;
            button.UseVisualStyleBackColor = true;
        }
    }

    private void ApplyTextBoxStyle(TextBox textBox)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            textBox.BackColor = Color.FromArgb(50, 50, 60);
            textBox.ForeColor = ThemeText;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
        else
        {
            textBox.BackColor = SystemColors.Window;
            textBox.ForeColor = SystemColors.WindowText;
            textBox.BorderStyle = BorderStyle.Fixed3D;
        }
        textBox.Font = new Font("Malgun Gothic", _baseFontSize - 2);
    }

    private void ApplyComboBoxStyle(ComboBox comboBox)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            comboBox.BackColor = Color.FromArgb(50, 50, 60);
            comboBox.ForeColor = ThemeText;
            comboBox.FlatStyle = FlatStyle.Flat;
        }
        else
        {
            comboBox.BackColor = SystemColors.Window;
            comboBox.ForeColor = SystemColors.WindowText;
            comboBox.FlatStyle = FlatStyle.Standard;
        }
        comboBox.Font = new Font("Malgun Gothic", _baseFontSize - 3);
    }

    private void ApplyLabelStyle(Label label, bool isHeader = false)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            label.ForeColor = isHeader ? ThemeText : ThemeTextMuted;
        }
        else
        {
            label.ForeColor = isHeader ? SystemColors.ControlText : SystemColors.GrayText;
        }
        label.Font = new Font("Malgun Gothic", isHeader ? _baseFontSize - 2 : _baseFontSize - 3, isHeader ? FontStyle.Bold : FontStyle.Regular);
    }

    private void ApplyDataGridViewStyle(DataGridView dgv)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            dgv.BackgroundColor = ThemeGrid;
            dgv.ForeColor = ThemeText;
            dgv.GridColor = ThemeBorder;
            dgv.BorderStyle = BorderStyle.FixedSingle;
            dgv.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

            // Header style - dark theme
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(55, 55, 68);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = ThemeText;
            dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(55, 55, 68);

            // Cell style - dark theme
            dgv.DefaultCellStyle.BackColor = ThemeGrid;
            dgv.DefaultCellStyle.ForeColor = ThemeText;
            dgv.DefaultCellStyle.SelectionBackColor = ThemeAccent;
            dgv.DefaultCellStyle.SelectionForeColor = ThemeAccentText;

            // Alternating row style
            dgv.AlternatingRowsDefaultCellStyle.BackColor = ThemeGridAlt;
            dgv.AlternatingRowsDefaultCellStyle.ForeColor = ThemeText;
        }
        else
        {
            // Classic theme - use system colors for native look
            dgv.BackgroundColor = SystemColors.Window;
            dgv.ForeColor = SystemColors.WindowText;
            dgv.GridColor = SystemColors.ControlDark;
            dgv.BorderStyle = BorderStyle.Fixed3D;
            dgv.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            dgv.EnableHeadersVisualStyles = true;  // Use Windows visual styles for headers
            dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Raised;

            // Header style - classic theme (will be overridden by EnableHeadersVisualStyles)
            dgv.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = SystemColors.Highlight;

            // Cell style - classic theme
            dgv.DefaultCellStyle.BackColor = SystemColors.Window;
            dgv.DefaultCellStyle.ForeColor = SystemColors.WindowText;
            dgv.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
            dgv.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;

            // Alternating row style
            dgv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 250);
            dgv.AlternatingRowsDefaultCellStyle.ForeColor = SystemColors.WindowText;
        }

        // Common styles
        dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", _baseFontSize - 3, FontStyle.Bold);
        dgv.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        dgv.ColumnHeadersHeight = 35;
        dgv.DefaultCellStyle.Font = new Font("Malgun Gothic", _baseFontSize - 3);
        dgv.DefaultCellStyle.Padding = new Padding(5, 3, 5, 3);
        dgv.RowTemplate.Height = 28;

        // Apply dark scrollbar theme (recursive - includes child controls)
        ApplyScrollBarTheme(dgv, _currentTheme == ThemeType.Dark);

        // Set up handler for lazily-created scrollbars (only once per DataGridView)
        if (!_scrollBarHandlerInitialized.Contains(dgv))
        {
            _scrollBarHandlerInitialized.Add(dgv);
            dgv.ControlAdded += (s, e) =>
            {
                if (e.Control is HScrollBar || e.Control is VScrollBar)
                {
                    ApplyScrollBarTheme(e.Control, _currentTheme == ThemeType.Dark);
                }
            };
        }
    }

    private void ApplyTableLayoutPanelStyle(TableLayoutPanel panel)
    {
        panel.BackColor = _currentTheme == ThemeType.Dark ? ThemeBackground : SystemColors.Control;
    }

    private void ApplyFlowLayoutPanelStyle(FlowLayoutPanel panel)
    {
        panel.BackColor = _currentTheme == ThemeType.Dark ? ThemeBackground : SystemColors.Control;
    }

    private void ApplyDetailTextBoxStyle(TextBox textBox)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            textBox.BackColor = ThemePanel;
            textBox.ForeColor = ThemeText;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
        else
        {
            textBox.BackColor = SystemColors.Window;
            textBox.ForeColor = SystemColors.WindowText;
            textBox.BorderStyle = BorderStyle.Fixed3D;
        }
        textBox.Font = new Font("Consolas", _baseFontSize - 2);

        // Apply dark scrollbar theme
        ApplyScrollBarTheme(textBox, _currentTheme == ThemeType.Dark);
    }

    private void ApplyStatusLabelStyle(Label label)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            label.ForeColor = ThemeTextMuted;
            label.BackColor = ThemeBackground;
        }
        else
        {
            label.ForeColor = SystemColors.ControlText;
            label.BackColor = SystemColors.Control;
        }
        label.Font = new Font("Malgun Gothic", _baseFontSize - 3);
        label.Padding = new Padding(10, 0, 0, 0);
    }

    #endregion
}
