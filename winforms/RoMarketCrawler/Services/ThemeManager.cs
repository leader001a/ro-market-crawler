using System.Runtime.InteropServices;
using RoMarketCrawler.Interfaces;
using RoMarketCrawler.Models;

namespace RoMarketCrawler.Services;

/// <summary>
/// Theme manager service for centralized theme management.
/// Handles dark mode APIs and control styling.
/// </summary>
public class ThemeManager : IThemeManager
{
    #region Windows API

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

    #endregion

    private static bool _darkModeInitialized = false;
    private ThemeType _currentTheme = ThemeType.Dark;
    private ThemeColors _colors = ThemeColors.Dark;

    /// <inheritdoc/>
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <inheritdoc/>
    public ThemeType CurrentTheme => _currentTheme;

    /// <inheritdoc/>
    public ThemeColors Colors => _colors;

    public ThemeManager(ThemeType initialTheme = ThemeType.Dark)
    {
        InitializeDarkModeSupport();
        _currentTheme = initialTheme;
        _colors = ThemeColors.ForTheme(initialTheme);
    }

    /// <inheritdoc/>
    public void SetTheme(ThemeType theme)
    {
        if (_currentTheme == theme) return;

        var oldTheme = _currentTheme;
        _currentTheme = theme;
        _colors = ThemeColors.ForTheme(theme);

        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs
        {
            OldTheme = oldTheme,
            NewTheme = theme,
            Colors = _colors
        });
    }

    /// <summary>
    /// Initialize dark mode support for the application (call once at startup)
    /// </summary>
    public static void InitializeDarkModeSupport()
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
    public static void ApplyDarkModeToTitleBar(IntPtr handle, bool isDark)
    {
        try
        {
            int useImmersiveDarkMode = isDark ? 1 : 0;

            // Try Windows 10 20H1+ attribute first
            if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int)) != 0)
            {
                // Fall back to pre-20H1 attribute
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
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
    public static void ApplyScrollBarTheme(Control control, bool isDark)
    {
        string themeName = isDark ? "DarkMode_Explorer" : "Explorer";
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
    public static void ApplyThemeToControlHandle(Control control, string themeName)
    {
        if (control.IsHandleCreated)
        {
            SetWindowTheme(control.Handle, themeName, null);
        }
        else
        {
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

    /// <summary>
    /// Flush Windows menu theme cache
    /// </summary>
    public static void FlushMenuThemeCache()
    {
        try { FlushMenuThemes(); } catch { }
    }

    #region Control Styling Helpers

    /// <summary>
    /// Apply theme to a TextBox
    /// </summary>
    public void ApplyToTextBox(TextBox txt)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            txt.BackColor = Colors.Grid;
            txt.ForeColor = Colors.Text;
            txt.BorderStyle = BorderStyle.FixedSingle;
        }
        else
        {
            txt.BackColor = SystemColors.Window;
            txt.ForeColor = SystemColors.WindowText;
            txt.BorderStyle = BorderStyle.FixedSingle;
        }
        ApplyScrollBarTheme(txt, _currentTheme == ThemeType.Dark);
    }

    /// <summary>
    /// Apply theme to a ComboBox
    /// </summary>
    public void ApplyToComboBox(ComboBox combo)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            combo.BackColor = Colors.Grid;
            combo.ForeColor = Colors.Text;
            combo.FlatStyle = FlatStyle.Flat;
        }
        else
        {
            combo.BackColor = SystemColors.Window;
            combo.ForeColor = SystemColors.WindowText;
            combo.FlatStyle = FlatStyle.Standard;
        }
    }

    /// <summary>
    /// Apply theme to a Button
    /// </summary>
    public void ApplyToButton(Button btn, bool isPrimary = false)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            btn.FlatStyle = FlatStyle.Flat;
            if (isPrimary)
            {
                btn.BackColor = Colors.Accent;
                btn.ForeColor = Colors.AccentText;
                btn.FlatAppearance.BorderColor = Colors.Accent;
                btn.FlatAppearance.MouseOverBackColor = Colors.AccentHover;
            }
            else
            {
                btn.BackColor = Colors.Panel;
                btn.ForeColor = Colors.Text;
                btn.FlatAppearance.BorderColor = Colors.Border;
                btn.FlatAppearance.MouseOverBackColor = Colors.GridAlt;
            }
        }
        else
        {
            btn.FlatAppearance.BorderColor = SystemColors.ControlDark;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = Color.Empty;
            btn.FlatAppearance.MouseDownBackColor = Color.Empty;
            btn.FlatStyle = FlatStyle.Standard;
            btn.BackColor = SystemColors.Control;
            btn.ForeColor = SystemColors.ControlText;
            btn.UseVisualStyleBackColor = true;
        }
    }

    /// <summary>
    /// Apply theme to a Label
    /// </summary>
    public void ApplyToLabel(Label lbl, bool isLink = false, bool isHeader = false)
    {
        if (isLink)
        {
            lbl.ForeColor = Colors.Accent;
        }
        else if (isHeader)
        {
            lbl.ForeColor = Colors.LinkColor;
            lbl.BackColor = Colors.Grid;
        }
        else
        {
            lbl.ForeColor = Colors.Text;
        }
    }

    /// <summary>
    /// Apply theme to a Panel
    /// </summary>
    public void ApplyToPanel(Panel panel, bool isBorder = false)
    {
        panel.BackColor = isBorder ? Colors.Border : Colors.Panel;
    }

    /// <summary>
    /// Apply theme to a RichTextBox
    /// </summary>
    public void ApplyToRichTextBox(RichTextBox rtb)
    {
        rtb.BackColor = Colors.Grid;
        rtb.ForeColor = Colors.Text;
        ApplyScrollBarTheme(rtb, _currentTheme == ThemeType.Dark);
    }

    /// <summary>
    /// Apply theme to a CheckBox
    /// </summary>
    public void ApplyToCheckBox(CheckBox chk)
    {
        chk.ForeColor = Colors.Text;
        chk.BackColor = Color.Transparent;
    }

    /// <summary>
    /// Apply theme to a FlowLayoutPanel
    /// </summary>
    public void ApplyToFlowLayoutPanel(FlowLayoutPanel flp)
    {
        flp.BackColor = Colors.Panel;
    }

    /// <summary>
    /// Apply theme to a ProgressBar (using system styling)
    /// </summary>
    public void ApplyToProgressBar(ProgressBar pb)
    {
        if (_currentTheme == ThemeType.Dark)
        {
            pb.BackColor = Colors.Grid;
            ApplyScrollBarTheme(pb, true);
        }
        else
        {
            pb.BackColor = SystemColors.Control;
            ApplyScrollBarTheme(pb, false);
        }
    }

    #endregion
}
