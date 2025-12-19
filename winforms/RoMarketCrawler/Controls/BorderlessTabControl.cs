namespace RoMarketCrawler.Controls;

public class BorderlessTabControl : TabControl
{
    private const int WM_PAINT = 0x000F;

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Paint the entire background with the parent's background color
        // This helps cover any border artifacts
        if (Parent != null)
        {
            using var brush = new SolidBrush(Parent.BackColor);
            pevent.Graphics.FillRectangle(brush, ClientRectangle);
        }
        else
        {
            base.OnPaintBackground(pevent);
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // After default processing, paint over borders AND tab separator lines
        if (m.Msg == WM_PAINT && Parent != null)
        {
            using var g = CreateGraphics();
            using var brush = new SolidBrush(Parent.BackColor);

            // Cover left border of content area
            g.FillRectangle(brush, 0, ItemSize.Height, 4, Height - ItemSize.Height);
            // Cover right border of content area
            g.FillRectangle(brush, Width - 4, ItemSize.Height, 4, Height - ItemSize.Height);
            // Cover bottom border of content area
            g.FillRectangle(brush, 0, Height - 4, Width, 4);

            // === IMPORTANT: Cover tab separator lines in the tab strip area ===
            // Cover the left edge before first tab
            g.FillRectangle(brush, 0, 0, 5, ItemSize.Height + 5);

            // Cover gaps BETWEEN each tab (the native Windows separator lines)
            for (int i = 0; i < TabCount; i++)
            {
                var tabRect = GetTabRect(i);

                // Cover area to the LEFT of this tab (catches separator line)
                g.FillRectangle(brush, tabRect.X - 3, 0, 6, ItemSize.Height + 5);

                // Cover area to the RIGHT of this tab (catches separator line)
                g.FillRectangle(brush, tabRect.Right - 3, 0, 6, ItemSize.Height + 5);
            }

            // Cover the right edge after last tab
            if (TabCount > 0)
            {
                var lastTabRect = GetTabRect(TabCount - 1);
                g.FillRectangle(brush, lastTabRect.Right - 2, 0, Width - lastTabRect.Right + 5, ItemSize.Height + 5);
            }

            // Cover the top edge
            g.FillRectangle(brush, 0, 0, Width, 3);

            // Cover the bottom of the tab strip (border between tabs and content)
            g.FillRectangle(brush, 0, ItemSize.Height - 2, Width, 10);
        }
    }
}
