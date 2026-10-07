using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.WebUi;

/// <summary>Values the user entered when a prompt button was clicked.</summary>
public sealed class WebPromptResult
{
	public string Text { get; init; } = string.Empty;
	public string UserName { get; init; } = string.Empty;
	public string Password { get; init; } = string.Empty;
	public bool IsChecked { get; init; }
}

public sealed record WebPromptButton(string Label, bool IsPrimary, Action<WebPromptResult> Clicked, bool IsDanger = false);

/// <summary>
/// Describes one site prompt (permission request, alert/confirm/prompt, sign-in). <see cref="WebPromptHost"/>
/// queues these per tab and shows them with <see cref="WebPromptCard"/>.
/// </summary>
public sealed class WebPromptSpec
{
	public required string Title { get; init; }
	public string? Message { get; init; }
	public MaterialIconKind Icon { get; init; } = MaterialIconKind.Web;

	public bool HasTextInput { get; init; }
	public string? DefaultText { get; init; }

	/// <summary>Show user name + password fields (basic authentication).</summary>
	public bool HasCredentials { get; init; }

	/// <summary>Label for an optional check box (e.g. "Remember this decision"); null hides it.</summary>
	public string? CheckboxText { get; init; }
	public bool CheckboxDefault { get; init; }

	public required IReadOnlyList<WebPromptButton> Buttons { get; init; }

	/// <summary>Runs when the prompt goes away without a button click (tab or window closed). Must complete any deferral.</summary>
	public required Action Cancelled { get; init; }
}

/// <summary>The themed card for a <see cref="WebPromptSpec"/>.</summary>
public sealed partial class WebPromptCard : ThemedUserControl
{
	private readonly WebPromptSpec _spec;
	private readonly Border _card;
	private readonly MaterialIcon _icon;
	private readonly TextBlock _title;
	private readonly TextBlock? _message;
	private readonly FTextInput? _textInput;
	private readonly FTextInput? _userInput;
	private readonly PasswordBox? _passwordInput;
	private readonly FCheckBox? _checkBox;
	private readonly List<(FTextButton button, WebPromptButton spec)> _buttons = [];

	/// <summary>Raised once, when a button is clicked. The host removes the card, then runs the button action.</summary>
	public event Action<WebPromptButton, WebPromptResult>? ButtonClicked;

	public WebPromptCard(WebPromptSpec spec)
	{
		_spec = spec;

		var stack = new StackPanel { Spacing = 8 };

		_icon = new MaterialIcon { Kind = spec.Icon, Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Top };
		_title = WebUiStyle.Text(spec.Title, 15, bold: true);
		var header = new Grid
		{
			ColumnSpacing = 8,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
			},
		};
		Grid.SetColumn(_title, 1);
		header.Children.Add(_icon);
		header.Children.Add(_title);
		stack.Children.Add(header);

		if (!string.IsNullOrEmpty(spec.Message))
		{
			_message = WebUiStyle.Text(spec.Message, 13);
			_message.IsTextSelectionEnabled = true;
			stack.Children.Add(new ScrollViewer
			{
				Content = _message,
				MaxHeight = 260,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			});
		}

		if (spec.HasTextInput)
		{
			_textInput = new FTextInput(spec.DefaultText ?? string.Empty) { AcceptsReturn = false, MinHeight = 28 };
			_textInput.EnterPressed += ClickPrimary;
			stack.Children.Add(_textInput);
		}

		if (spec.HasCredentials)
		{
			_userInput = new FTextInput { AcceptsReturn = false, PlaceHolderText = "User name", MinHeight = 28 };
			_userInput.EnterPressed += () => _passwordInput?.Focus(FocusState.Programmatic);
			_passwordInput = new PasswordBox
			{
				PlaceholderText = "Password",
				CornerRadius = new CornerRadius(5),
				BorderThickness = new Thickness(2),
				Padding = new Thickness(10, 2, 10, 2),
				MinHeight = 28,
			};
			_passwordInput.KeyUp += (_, e) =>
			{
				if (e.Key == Windows.System.VirtualKey.Enter) ClickPrimary();
			};
			stack.Children.Add(_userInput);
			stack.Children.Add(_passwordInput);
		}

		if (spec.CheckboxText is { } checkboxText)
		{
			_checkBox = new FCheckBox { Text = checkboxText, IsChecked = spec.CheckboxDefault };
			stack.Children.Add(_checkBox);
		}

		var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 4 };
		foreach (var buttonSpec in spec.Buttons)
		{
			var button = WebUiStyle.TextButton(buttonSpec.Label, () => Click(buttonSpec));
			button.MinWidth = 72;
			button.ForceHighlight = buttonSpec.IsPrimary;
			_buttons.Add((button, buttonSpec));
			buttonRow.Children.Add(button);
		}
		stack.Children.Add(buttonRow);

		_card = WebUiStyle.Card(stack, 12);
		Content = _card;

		Loaded += (_, _) =>
		{
			// put the cursor where the user is expected to type
			if (_textInput is not null) _textInput.FocusInput();
			else _userInput?.FocusInput();
		};

		ApplyTheme();
	}

	private void ClickPrimary()
	{
		var primary = _spec.Buttons.FirstOrDefault(b => b.IsPrimary) ?? _spec.Buttons.LastOrDefault();
		if (primary is not null) Click(primary);
	}

	private bool _clicked;
	private void Click(WebPromptButton button)
	{
		if (_clicked) return;
		_clicked = true;

		ButtonClicked?.Invoke(button, new WebPromptResult
		{
			Text = _textInput?.CurrentText ?? string.Empty,
			UserName = _userInput?.CurrentText ?? string.Empty,
			Password = _passwordInput?.Password ?? string.Empty,
			IsChecked = _checkBox?.IsChecked ?? false,
		});
	}

	protected override void ApplyTheme()
	{
		if (_card is null) return; // base constructor runs before ours

		WebUiStyle.ApplyCardTheme(_card, CurrentTheme, solid: true);
		_icon.Foreground = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
		_title.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		if (_message is not null) _message.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		if (_textInput is not null) _textInput.CurrentTheme = CurrentTheme;
		if (_userInput is not null) _userInput.CurrentTheme = CurrentTheme;
		if (_checkBox is not null) _checkBox.CurrentTheme = CurrentTheme;
		if (_passwordInput is not null) ThemePasswordBox(_passwordInput, CurrentTheme);

		foreach (var (button, spec) in _buttons)
			button.CurrentTheme = spec.IsDanger ? WebUiStyle.DangerTheme(CurrentTheme) : CurrentTheme;
	}

	/// <summary>
	/// PasswordBox has no F-control equivalent; restyle it through its lightweight-styling resources so the
	/// hover/focus states also use theme colors instead of the system ones.
	/// </summary>
	private static void ThemePasswordBox(PasswordBox box, Theme theme)
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
