using FoxyBrowser716.Controls.Generic;
using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.WebUi;

/// <summary>
/// Shared building blocks for the code-built web UI (prompts, downloads, history, toasts) so they match the
/// rest of FoxyBrowser: rounded 2px borders, translucent primary background, F-controls for buttons.
/// </summary>
internal static class WebUiStyle
{
	public static Border Card(UIElement child, double padding = 10) => new()
	{
		CornerRadius = new CornerRadius(10),
		BorderThickness = new Thickness(2),
		Padding = new Thickness(padding),
		Child = child,
	};

	/// <param name="solid">Less transparent, for cards drawn over web content.</param>
	public static void ApplyCardTheme(Border card, Theme theme, bool solid)
	{
		card.Background = new SolidColorBrush(solid ? theme.PrimaryBackgroundColorSlightTransparent : theme.PrimaryBackgroundColorVeryTransparent);
		card.BorderBrush = new SolidColorBrush(theme.SecondaryBackgroundColorSlightTransparent);
	}

	public static TextBlock Text(string text, double size = 13, bool bold = false, bool wrap = true) => new()
	{
		Text = text,
		FontSize = size,
		FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
		TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
		TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
		VerticalAlignment = VerticalAlignment.Center,
	};

	public static FIconButton IconButton(MaterialIconKind kind, Action onClick, double size = 24, double padding = 3)
	{
		var button = new FIconButton
		{
			Content = new MaterialIcon { Kind = kind },
			Width = size,
			Height = size,
			Padding = new Thickness(padding),
			VerticalAlignment = VerticalAlignment.Center,
		};
		button.OnClick += (_, _) => onClick();
		return button;
	}

	public static FTextButton TextButton(string text, Action onClick, MaterialIconKind? icon = null)
	{
		var button = new FTextButton
		{
			ButtonText = text,
			Icon = icon is { } kind ? new MaterialIcon { Kind = kind } : null,
			CornerRadius = new CornerRadius(5),
			FontSize = 13,
			Height = 26,
			Padding = new Thickness(4, 0, 4, 0),
			Margin = new Thickness(2),
			VerticalAlignment = VerticalAlignment.Center,
		};
		button.OnClick += (_, _) => onClick();
		return button;
	}

	/// <summary>Theme for buttons that delete or cancel something (red hover, like the window close button).</summary>
	public static Theme DangerTheme(Theme theme) => theme with
	{
		SecondaryForegroundColor = theme.NoColor,
		PrimaryHighlightColor = theme.NoColor,
	};

	/// <summary>Colors a MaterialIcon used as a button's icon (FTextButton does not forward Foreground to it).</summary>
	public static void ThemeIcon(UIElement? icon, Color color)
	{
		if (icon is MaterialIcon materialIcon)
			materialIcon.Foreground = new SolidColorBrush(color);
	}

	/// <summary>
	/// Two-step confirm for destructive buttons: the first click changes the label, a second click within a few
	/// seconds runs <paramref name="action"/>.
	/// </summary>
	public static void MakeConfirming(FTextButton button, string confirmText, Action action)
	{
		var original = button.ButtonText;
		var armed = false;
		var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
		timer.Interval = TimeSpan.FromSeconds(4);
		timer.IsRepeating = false;
		timer.Tick += (_, _) =>
		{
			armed = false;
			button.ButtonText = original;
			button.ForceHighlight = false;
		};

		button.OnClick += (_, _) =>
		{
			if (!armed)
			{
				armed = true;
				button.ButtonText = confirmText;
				button.ForceHighlight = true;
				timer.Start();
				return;
			}

			timer.Stop();
			armed = false;
			button.ButtonText = original;
			button.ForceHighlight = false;
			action();
		};
	}

	/// <summary>"2 minutes ago" style for recent times, otherwise the date.</summary>
	public static string FormatRelative(DateTime time)
	{
		var span = DateTime.Now - time;
		if (span < TimeSpan.FromMinutes(1)) return "just now";
		if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
		if (time.Date == DateTime.Today) return time.ToShortTimeString();
		if (time.Date == DateTime.Today.AddDays(-1)) return $"Yesterday {time.ToShortTimeString()}";
		return time.ToShortDateString();
	}

	/// <summary>
	/// PasswordBox has no F-control equivalent; restyle it through its lightweight-styling resources so the
	/// hover/focus states also use theme colors instead of the system ones.
	/// </summary>
	public static void ThemePasswordBox(PasswordBox box, Theme theme)
	{
		var background = new SolidColorBrush(theme.PrimaryAccentColorSlightTransparent);
		var focusedBackground = new SolidColorBrush(theme.PrimaryBackgroundColorSlightTransparent);
		var border = new SolidColorBrush(theme.SecondaryAccentColorSlightTransparent);
		var focusedBorder = new SolidColorBrush(theme.PrimaryHighlightColor);
		var foreground = new SolidColorBrush(theme.PrimaryForegroundColor);
		var placeholder = new SolidColorBrush(theme.SecondaryForegroundColor);

		box.Background = background;
		box.BorderBrush = border;
		box.Foreground = foreground;
		box.SelectionHighlightColor = new SolidColorBrush(theme.SecondaryHighlightColor);

		box.Resources["TextControlBackground"] = background;
		box.Resources["TextControlBackgroundPointerOver"] = background;
		box.Resources["TextControlBackgroundFocused"] = focusedBackground;
		box.Resources["TextControlBorderBrush"] = border;
		box.Resources["TextControlBorderBrushPointerOver"] = border;
		box.Resources["TextControlBorderBrushFocused"] = focusedBorder;
		box.Resources["TextControlForeground"] = foreground;
		box.Resources["TextControlForegroundPointerOver"] = foreground;
		box.Resources["TextControlForegroundFocused"] = foreground;
		box.Resources["TextControlPlaceholderForeground"] = placeholder;
		box.Resources["TextControlPlaceholderForegroundPointerOver"] = placeholder;
		box.Resources["TextControlPlaceholderForegroundFocused"] = placeholder;
		box.Resources["TextControlButtonForeground"] = foreground;
	}
}
