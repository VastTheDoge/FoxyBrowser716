using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.Generic;

/// <summary>A themed check box built on <see cref="FTextButton"/> (checked = highlighted, with a check icon).</summary>
public sealed partial class FCheckBox : UserControl
{
	private readonly FTextButton _button;
	private readonly MaterialIcon _icon;

	public event Action<bool>? Toggled;

	public bool IsChecked
	{
		get;
		set
		{
			field = value;
			UpdateVisual();
		}
	}

	public string Text
	{
		get => _button.ButtonText;
		set => _button.ButtonText = value;
	}

	public Theme CurrentTheme
	{
		get;
		set
		{
			field = value;
			_button.CurrentTheme = value;
			UpdateVisual();
		}
	} = DefaultThemes.DarkMode;

	public FCheckBox()
	{
		_icon = new MaterialIcon { Kind = MaterialIconKind.CheckboxBlankOutline };
		_button = new FTextButton
		{
			Icon = _icon,
			CornerRadius = new CornerRadius(5),
			FontSize = 13,
			FontWeight = Microsoft.UI.Text.FontWeights.Normal,
			ContentHorizontalAlignment = HorizontalAlignment.Left,
			HorizontalAlignment = HorizontalAlignment.Left,
			Height = 24,
		};
		_button.OnClick += (_, _) =>
		{
			IsChecked = !IsChecked;
			Toggled?.Invoke(IsChecked);
		};
		Content = _button;
		UpdateVisual();
	}

	private void UpdateVisual()
	{
		_icon.Kind = IsChecked ? MaterialIconKind.CheckboxMarked : MaterialIconKind.CheckboxBlankOutline;
		_icon.Foreground = new SolidColorBrush(IsChecked ? CurrentTheme.PrimaryHighlightColor : CurrentTheme.PrimaryForegroundColor);
	}
}
