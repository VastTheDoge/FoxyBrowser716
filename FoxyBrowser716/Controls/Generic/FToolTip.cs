namespace FoxyBrowser716.Controls.Generic;

// Claude Doc
/// <summary>
/// Tooltips in the browser's theme. WinUI's default tooltip follows the Windows theme, so callers set the colors
/// here and call <see cref="Set"/> again when their theme changes.
/// </summary>
public static class FToolTip
{
	/// <param name="text">Null or empty removes the tooltip.</param>
	public static void Set(DependencyObject target, string? text, Theme theme)
	{
		if (string.IsNullOrEmpty(text))
		{
			target.ClearValue(ToolTipService.ToolTipProperty);
			return;
		}

		ToolTipService.SetToolTip(target, new ToolTip
		{
			Content = new TextBlock
			{
				Text = text,
				TextWrapping = TextWrapping.Wrap,
				Foreground = new SolidColorBrush(theme.PrimaryForegroundColor),
			},
			Background = new SolidColorBrush(theme.PrimaryBackgroundColor),
			BorderBrush = new SolidColorBrush(theme.SecondaryBackgroundColor),
			BorderThickness = new Thickness(2),
			CornerRadius = new CornerRadius(6),
		});
	}
}
