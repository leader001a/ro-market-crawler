namespace RoMarketCrawler.Controls;

/// <summary>
/// DataGridView that always shows the vertical scrollbar.
/// Usage: set Height to PreferredSize.Height - 1 so the DataGridView
/// always thinks content overflows by 1px, keeping the scrollbar visible.
/// </summary>
public class AlwaysVScrollDataGridView : DataGridView
{
}
