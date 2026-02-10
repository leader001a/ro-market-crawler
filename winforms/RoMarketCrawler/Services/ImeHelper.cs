using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RoMarketCrawler.Services;

/// <summary>
/// Helper class to fix IME full-width corruption caused by WebView2.
/// WebView2's Chromium engine can change the global IME conversion mode
/// from half-width to full-width, causing "100" to be typed as "１００".
/// </summary>
public static class ImeHelper
{
    private const int IME_CMODE_FULLSHAPE = 0x0008;

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmGetConversionStatus(IntPtr hIMC, out int conversion, out int sentence);

    [DllImport("imm32.dll")]
    private static extern bool ImmSetConversionStatus(IntPtr hIMC, int conversion, int sentence);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    /// <summary>
    /// Reset IME conversion mode to half-width (반각) for the given control.
    /// Preserves Korean/English mode, only clears the full-width bit.
    /// </summary>
    public static void ResetToHalfWidth(Control control)
    {
        if (control == null || !control.IsHandleCreated) return;

        try
        {
            var hIMC = ImmGetContext(control.Handle);
            if (hIMC == IntPtr.Zero) return;

            try
            {
                if (ImmGetConversionStatus(hIMC, out int conversion, out int sentence))
                {
                    if ((conversion & IME_CMODE_FULLSHAPE) != 0)
                    {
                        int newConversion = conversion & ~IME_CMODE_FULLSHAPE;
                        ImmSetConversionStatus(hIMC, newConversion, sentence);
                        Debug.WriteLine("[ImeHelper] Reset IME from full-width to half-width");
                    }
                }
            }
            finally
            {
                ImmReleaseContext(control.Handle, hIMC);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ImeHelper] Failed to reset IME: {ex.Message}");
        }
    }

    /// <summary>
    /// Attach IME half-width reset to all TextBox controls within a container (recursive).
    /// Resets IME to half-width whenever a TextBox receives focus.
    /// </summary>
    public static void AttachToAllTextBoxes(Control container)
    {
        foreach (Control control in container.Controls)
        {
            if (control is TextBox textBox)
            {
                textBox.GotFocus += (s, e) => ResetToHalfWidth(textBox);
            }
            else if (control is ToolStrip toolStrip)
            {
                foreach (ToolStripItem item in toolStrip.Items)
                {
                    if (item is ToolStripTextBox tsTextBox)
                    {
                        tsTextBox.GotFocus += (s, e) => ResetToHalfWidth(tsTextBox.TextBox);
                    }
                }
            }
            else if (control is NumericUpDown nud)
            {
                // NumericUpDown has an internal TextBox child control
                foreach (Control child in nud.Controls)
                {
                    if (child is TextBox nudTextBox)
                    {
                        nudTextBox.GotFocus += (s, e) => ResetToHalfWidth(nudTextBox);
                        break;
                    }
                }
            }

            // Recurse into child containers
            if (control.HasChildren)
            {
                AttachToAllTextBoxes(control);
            }
        }
    }
}
