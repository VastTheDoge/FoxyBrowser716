using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.WebUi;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Settings;
using FoxyBrowser716.ErrorHandeler;
using Material.Icons.WinUI3;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.Controls.SettingsPage.SettingsCustomControls;

/// <summary>
/// The per-site permission manager: every remembered decision grouped by site, with Allow/Block/Forget,
/// plus the decisions the browser engine stored on its own (from before this manager existed, or from its
/// built-in prompt while themed dialogs are off), which can be reset.
/// </summary>
public sealed partial class SitePermissionsController : ThemedUserControl
{
	private readonly MainWindow.MainWindow _mainWindow;
	private readonly StackPanel _root;
	private readonly Border _card;
	private readonly List<(FTextButton button, bool danger)> _buttons = [];
	private readonly List<FIconButton> _iconButtons = [];
	private readonly List<TextBlock> _primaryText = [];
	private readonly List<TextBlock> _secondaryText = [];
	private readonly List<MaterialIcon> _icons = [];
	private readonly List<Border> _groups = [];
	private IReadOnlyList<CoreWebView2PermissionSetting>? _engineSettings;
	private bool _waitingForEngine;

	public SitePermissionsController(MainWindow.MainWindow mainWindow)
	{
		_mainWindow = mainWindow;
		_root = new StackPanel { Spacing = 6 };
		_card = new Border
		{
			Child = _root,
			Padding = new Thickness(6),
			CornerRadius = new CornerRadius(5),
			BorderThickness = new Thickness(2),
			Margin = new Thickness(0, 6, 0, 6),
		};
		Content = _card;

		Loaded += async (_, _) =>
		{
			_mainWindow.Instance.SitePermissions.Changed += OnChanged;
			Refresh();
			await LoadEngineSettings();
		};
		Unloaded += (_, _) => _mainWindow.Instance.SitePermissions.Changed -= OnChanged;
	}

	private void OnChanged() => DispatcherQueue.TryEnqueue(Refresh);

	private async Task LoadEngineSettings()
	{
		try
		{
			var webview = _mainWindow.ExtensionPopupWebview;
			if (webview.CoreWebView2?.Profile is not { } profile)
			{
				// the settings page is built before the window's WebView2 exists; load once it does
				if (!_waitingForEngine)
				{
					_waitingForEngine = true;
					webview.CoreWebView2Initialized += async (_, _) => await LoadEngineSettings();
				}
				return;
			}
			_engineSettings = await profile.GetNonDefaultPermissionSettingsAsync();
			Refresh();
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
		}
	}

	private void Refresh()
	{
		_root.Children.Clear();
		_buttons.Clear();
		_iconButtons.Clear();
		_primaryText.Clear();
		_secondaryText.Clear();
		_icons.Clear();
		_groups.Clear();

		var manager = _mainWindow.Instance.SitePermissions;
		var remembered = manager.GetAll();

		var header = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
				new ColumnDefinition { Width = GridLength.Auto },
			},
		};
		var intro = Secondary(remembered.Count == 0
			? "No remembered decisions yet. When a site asks for a permission, check \"Remember this decision\" to save your answer here."
			: "Sites you allowed or blocked. Anything not listed uses the defaults above.");
		header.Children.Add(intro);
		if (remembered.Count > 0)
		{
			var forgetAll = Button("Forget all", () => { }, MaterialIconKind.DeleteSweep, danger: true);
			WebUiStyle.MakeConfirming(forgetAll, "Click to confirm", manager.ForgetAll);
			Grid.SetColumn(forgetAll, 1);
			header.Children.Add(forgetAll);
		}
		_root.Children.Add(header);

		foreach (var site in remembered.GroupBy(p => p.Origin, StringComparer.OrdinalIgnoreCase))
		{
			var group = new StackPanel { Spacing = 2 };

			var siteHeader = new Grid
			{
				ColumnDefinitions =
				{
					new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
					new ColumnDefinition { Width = GridLength.Auto },
				},
			};
			var siteName = Primary(SitePermissionManager.GetDisplayHost(site.Key));
			siteName.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
			siteName.Margin = new Thickness(2, 0, 0, 0);
			var forgetSite = Button("Forget site", () => manager.ForgetSite(site.Key), MaterialIconKind.Delete, danger: true);
			Grid.SetColumn(forgetSite, 1);
			siteHeader.Children.Add(siteName);
			siteHeader.Children.Add(forgetSite);
			group.Children.Add(siteHeader);

			foreach (var permission in site)
				group.Children.Add(PermissionRow(
					permission.Kind,
					$"{SitePermissionManager.GetDisplayName(permission.Kind)} · decided {WebUiStyle.FormatRelative(permission.DecidedAt)}",
					permission.Decision,
					decision => manager.SetDecision(permission.Origin, permission.Kind, decision),
					() => manager.Forget(permission.Origin, permission.Kind)));

			_root.Children.Add(Group(group));
		}

		AddEngineSection();
		ApplyTheme();
	}

	private void AddEngineSection()
	{
		if (_engineSettings is not { Count: > 0 } engineSettings) return;

		var group = new StackPanel { Spacing = 2 };
		var title = Primary("Stored by the browser engine");
		title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
		group.Children.Add(title);
		group.Children.Add(Secondary("Saved by the engine's own prompt (before this manager, or while themed dialogs are off). The engine applies these before FoxyBrowser is asked, so reset one to manage it here instead."));

		foreach (var setting in engineSettings.OrderBy(s => s.PermissionOrigin))
		{
			var origin = setting.PermissionOrigin;
			var kind = setting.PermissionKind;
			var state = setting.PermissionState == CoreWebView2PermissionState.Allow ? "allowed" : "blocked";

			var row = new Grid
			{
				ColumnSpacing = 6,
				ColumnDefinitions =
				{
					new ColumnDefinition { Width = GridLength.Auto },
					new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
					new ColumnDefinition { Width = GridLength.Auto },
				},
			};
			var icon = Icon(SitePermissionManager.GetIcon(kind));
			var text = Primary($"{SitePermissionManager.GetDisplayHost(origin)}: {SitePermissionManager.GetDisplayName(kind)} {state}");
			text.TextTrimming = TextTrimming.CharacterEllipsis;
			var reset = Button("Reset", async () =>
			{
				try
				{
					if (_mainWindow.ExtensionPopupWebview.CoreWebView2?.Profile is { } profile)
						await profile.SetPermissionStateAsync(kind, origin, CoreWebView2PermissionState.Default);
					await LoadEngineSettings();
				}
				catch (Exception e)
				{
					FoxyLogger.AddError(e);
					_mainWindow.ShowToast("Could not reset that permission", e.Message, MaterialIconKind.AlertCircle, isError: true);
				}
			}, MaterialIconKind.Refresh);
			Grid.SetColumn(text, 1);
			Grid.SetColumn(reset, 2);
			row.Children.Add(icon);
			row.Children.Add(text);
			row.Children.Add(reset);
			group.Children.Add(row);
		}

		_root.Children.Add(Group(group));
	}

	private Grid PermissionRow(CoreWebView2PermissionKind kind, string label, PermissionDecision decision,
		Action<PermissionDecision> setDecision, Action forget)
	{
		var row = new Grid
		{
			ColumnSpacing = 6,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
				new ColumnDefinition { Width = GridLength.Auto },
			},
		};

		var text = Primary(label);
		text.TextTrimming = TextTrimming.CharacterEllipsis;

		var allow = Button("Allow", () => setDecision(PermissionDecision.Allow));
		var block = Button("Block", () => setDecision(PermissionDecision.Block));
		allow.ForceHighlight = decision == PermissionDecision.Allow;
		block.ForceHighlight = decision == PermissionDecision.Block;
		var forgetButton = WebUiStyle.IconButton(MaterialIconKind.Close, forget, 24, 4);
		_iconButtons.Add(forgetButton);

		var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
		actions.Children.Add(allow);
		actions.Children.Add(block);
		actions.Children.Add(forgetButton);

		Grid.SetColumn(text, 1);
		Grid.SetColumn(actions, 2);
		row.Children.Add(Icon(SitePermissionManager.GetIcon(kind)));
		row.Children.Add(text);
		row.Children.Add(actions);
		return row;
	}

	private Border Group(UIElement child)
	{
		var border = new Border
		{
			Child = child,
			Padding = new Thickness(6),
			CornerRadius = new CornerRadius(6),
			BorderThickness = new Thickness(2),
		};
		_groups.Add(border);
		return border;
	}

	private MaterialIcon Icon(MaterialIconKind kind)
	{
		var icon = new MaterialIcon { Kind = kind, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
		_icons.Add(icon);
		return icon;
	}

	private FTextButton Button(string text, Action onClick, MaterialIconKind? icon = null, bool danger = false)
	{
		var button = WebUiStyle.TextButton(text, onClick, icon);
		_buttons.Add((button, danger));
		return button;
	}

	private TextBlock Primary(string text)
	{
		var block = WebUiStyle.Text(text, 13, wrap: false);
		_primaryText.Add(block);
		return block;
	}

	private TextBlock Secondary(string text)
	{
		var block = WebUiStyle.Text(text, 12);
		_secondaryText.Add(block);
		return block;
	}

	protected override void ApplyTheme()
	{
		if (_card is null) return;

		_card.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
		_card.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
		foreach (var (button, danger) in _buttons)
		{
			button.CurrentTheme = danger ? WebUiStyle.DangerTheme(CurrentTheme) : CurrentTheme;
			WebUiStyle.ThemeIcon(button.Icon, CurrentTheme.PrimaryForegroundColor);
		}
		foreach (var button in _iconButtons) button.CurrentTheme = WebUiStyle.DangerTheme(CurrentTheme);
		foreach (var text in _primaryText) text.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		foreach (var text in _secondaryText) text.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
		foreach (var icon in _icons) icon.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		foreach (var group in _groups)
		{
			group.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColor);
			group.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
		}
	}
}
