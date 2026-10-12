using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.DataObjects.Complex.Ai;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;

namespace FoxyBrowser716.Controls.MainWindow;

/// <summary>
/// Picks the chat's model, shown in the panel when the model button is clicked. Lists what each provider's API
/// offers (fetched, with a search box for long lists such as OpenRouter's) and takes a typed-in model id when the
/// one wanted isn't listed.
/// </summary>
public sealed partial class AiModelPicker : ThemedUserControl
{
	/// <summary>Buttons shown per provider; searching narrows the list further.</summary>
	private const int MaxShown = 150;

	private readonly BrowserSettings _settings;
	private readonly AiModelChoice _current;
	private readonly FTextInput _search;
	private readonly FIconButton _refresh;
	private readonly StackPanel _list = new() { Spacing = 2 };
	private readonly FTextInput _customId;
	private readonly List<FTextButton> _customProviders = [];
	private readonly FTextButton _useCustom;
	private readonly TextBlock _customTitle;
	private AiProviderKind _customProvider;

	private readonly Dictionary<AiProviderKind, (List<(string Id, string Name)>? Models, string? Message)> _results = [];

	public event Action<AiModelChoice>? Picked;

	public AiModelPicker(BrowserSettings settings, AiModelChoice current, Theme theme)
	{
		_settings = settings;
		_current = current;
		_customProvider = current.Provider;

		_search = new FTextInput { PlaceHolderText = "Search models", Height = 28, HorizontalAlignment = HorizontalAlignment.Stretch };
		_search.OnTextChanged += _ => Render();
		_refresh = new FIconButton { Content = new MaterialIcon { Kind = MaterialIconKind.Refresh }, Width = 28, Height = 28 };
		ToolTipService.SetToolTip(_refresh, "Fetch the model lists again");
		_refresh.OnClick += async (_, _) => await LoadAsync(refresh: true);

		var top = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
		top.Children.Add(_search);
		top.Children.Add(_refresh);
		Grid.SetColumn(_refresh, 1);

		_customTitle = new TextBlock { Text = "Model not listed? Type its id:", FontSize = 13, Margin = new Thickness(2, 10, 0, 0) };
		_customId = new FTextInput { PlaceHolderText = "Model id", Height = 28, HorizontalAlignment = HorizontalAlignment.Stretch };
		_customId.EnterPressed += UseCustom;
		var providers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
		foreach (var (name, kind) in new[] { ("Claude", AiProviderKind.Claude), ("OpenAI-compatible", AiProviderKind.OpenAiCompatible) })
		{
			var button = new FTextButton { ButtonText = name, Height = 28, CornerRadius = new CornerRadius(5) };
			button.OnClick += (_, _) =>
			{
				_customProvider = kind;
				UpdateCustomProviders();
			};
			_customProviders.Add(button);
			providers.Children.Add(button);
		}
		_useCustom = new FTextButton { ButtonText = "Use", Height = 28, CornerRadius = new CornerRadius(5) };
		_useCustom.OnClick += (_, _) => UseCustom();
		providers.Children.Add(_useCustom);

		Content = new StackPanel
		{
			Spacing = 6,
			Margin = new Thickness(8, 4, 8, 12),
			Children = { top, _list, _customTitle, _customId, providers },
		};

		CurrentTheme = theme;
		UpdateCustomProviders();
		_ = LoadAsync(refresh: false);
	}

	private void UseCustom()
	{
		var id = _customId.CurrentText.Trim();
		if (id.Length > 0) Picked?.Invoke(new AiModelChoice(_customProvider, id));
	}

	private void UpdateCustomProviders()
	{
		for (var i = 0; i < _customProviders.Count; i++)
			_customProviders[i].ForceHighlight = (AiProviderKind)i == _customProvider;
	}

	private async Task LoadAsync(bool refresh)
	{
		_results.Clear();
		Render();

		foreach (var provider in Enum.GetValues<AiProviderKind>())
		{
			// the default OpenAI address without a key isn't set up, it's just untouched
			if (provider == AiProviderKind.OpenAiCompatible && AiCredentials.Get(provider) is null
				&& _settings.OpenAiBaseUrl.TrimEnd('/') == "https://api.openai.com/v1")
			{
				_results[provider] = (null, "Not set up. Add a server (and key) in the assistant's settings.");
				continue;
			}

			try
			{
				_results[provider] = (await AiModels.GetAsync(provider, _settings, refresh), null);
			}
			catch (AiNotConfiguredException e)
			{
				_results[provider] = (null, e.Message.Replace("**", ""));
			}
			catch (Exception e)
			{
				_results[provider] = (null, $"Couldn't fetch the list: {e.Message}");
			}
			Render();
		}
	}

	private void Render()
	{
		_list.Children.Clear();
		var filter = _search.CurrentText.Trim();

		foreach (var provider in Enum.GetValues<AiProviderKind>())
		{
			_list.Children.Add(Heading(provider == AiProviderKind.Claude ? "Claude" : "OpenAI-compatible"));

			if (!_results.TryGetValue(provider, out var result))
			{
				_list.Children.Add(Note("Loading…"));
				continue;
			}
			if (result.Models is not { } models)
			{
				_list.Children.Add(Note(result.Message ?? ""));
				continue;
			}

			var matches = models
				.Where(m => filter.Length == 0 || m.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
				.ToList();
			foreach (var (id, name) in matches.Take(MaxShown))
			{
				var choice = new AiModelChoice(provider, id);
				var button = new FTextButton
				{
					ButtonText = name == id ? id : $"{name}  ·  {id}",
					Height = 28,
					CornerRadius = new CornerRadius(5),
					HorizontalAlignment = HorizontalAlignment.Stretch,
					ContentHorizontalAlignment = HorizontalAlignment.Left,
					ForceHighlight = choice == _current,
					CurrentTheme = CurrentTheme,
				};
				button.OnClick += (_, _) => Picked?.Invoke(choice);
				_list.Children.Add(button);
			}
			if (matches.Count == 0) _list.Children.Add(Note(filter.Length > 0 ? "No match." : "The server lists no models."));
			else if (matches.Count > MaxShown) _list.Children.Add(Note($"…{matches.Count - MaxShown} more. Search to narrow the list."));
		}
	}

	private TextBlock Heading(string text) => new()
	{
		Text = text,
		FontSize = 15,
		FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
		Margin = new Thickness(2, 8, 0, 2),
		Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
	};

	private TextBlock Note(string text) => new()
	{
		Text = text,
		FontSize = 12,
		TextWrapping = TextWrapping.Wrap,
		Margin = new Thickness(2, 0, 0, 0),
		Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor),
	};

	protected override void ApplyTheme()
	{
		if (_search is null) return; // base constructor runs before ours
		_search.CurrentTheme = CurrentTheme;
		_customId.CurrentTheme = CurrentTheme;
		_refresh.CurrentTheme = CurrentTheme;
		_useCustom.CurrentTheme = CurrentTheme;
		_customTitle.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		foreach (var button in _customProviders) button.CurrentTheme = CurrentTheme;
		Render();
	}
}
