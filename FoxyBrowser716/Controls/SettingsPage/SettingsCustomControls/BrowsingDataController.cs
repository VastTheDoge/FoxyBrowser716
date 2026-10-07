using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.WebUi;
using FoxyBrowser716.DataObjects.Settings;
using FoxyBrowser716.ErrorHandeler;
using Material.Icons.WinUI3;
using Microsoft.Web.WebView2.Core;

namespace FoxyBrowser716.Controls.SettingsPage.SettingsCustomControls;

/// <summary>"Clear browsing data" buttons for the instance. Each one asks for a second click before it runs.</summary>
public sealed partial class BrowsingDataController : ThemedUserControl
{
	private readonly MainWindow.MainWindow _mainWindow;
	private readonly Border _card;
	private readonly List<FTextButton> _buttons = [];

	public BrowsingDataController(MainWindow.MainWindow mainWindow)
	{
		_mainWindow = mainWindow;

		var panel = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 250, ItemHeight = 32 };
		Add(panel, "Clear history", MaterialIconKind.History, async () =>
		{
			_mainWindow.Instance.History.Clear();
			await Task.CompletedTask;
		});
		Add(panel, "Clear downloads list", MaterialIconKind.Download, async () =>
		{
			_mainWindow.Instance.Downloads.ClearFinished();
			await Task.CompletedTask;
		});
		Add(panel, "Clear cookies and site data", MaterialIconKind.Cookie,
			() => ClearEngineData(CoreWebView2BrowsingDataKinds.AllSite));
		Add(panel, "Clear cached images and files", MaterialIconKind.Broom,
			() => ClearEngineData(CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage));
		Add(panel, "Forget site permissions", MaterialIconKind.ShieldLock, async () =>
		{
			_mainWindow.Instance.SitePermissions.ForgetAll();
			await Task.CompletedTask;
		});
		Add(panel, "Clear everything", MaterialIconKind.DeleteSweep, async () =>
		{
			_mainWindow.Instance.History.Clear();
			_mainWindow.Instance.Downloads.ClearFinished();
			_mainWindow.Instance.SitePermissions.ForgetAll();
			await ClearEngineData(CoreWebView2BrowsingDataKinds.AllProfile);
		});

		_card = new Border
		{
			Child = panel,
			Padding = new Thickness(6),
			CornerRadius = new CornerRadius(5),
			BorderThickness = new Thickness(2),
			Margin = new Thickness(0, 6, 0, 6),
		};
		Content = _card;
		ApplyTheme();
	}

	private void Add(Panel panel, string text, MaterialIconKind icon, Func<Task> clear)
	{
		var button = WebUiStyle.TextButton(text, () => { }, icon);
		button.HorizontalAlignment = HorizontalAlignment.Stretch;
		button.ContentHorizontalAlignment = HorizontalAlignment.Left;
		WebUiStyle.MakeConfirming(button, "Click again to clear", async () =>
		{
			try
			{
				await clear();
				_mainWindow.ShowToast("Cleared", text.Replace("Clear ", string.Empty).Replace("Forget ", string.Empty), icon);
			}
			catch (Exception e)
			{
				FoxyLogger.AddError(e);
				_mainWindow.ShowToast("Clearing failed", e.Message, MaterialIconKind.AlertCircle, isError: true);
			}
		});
		_buttons.Add(button);
		panel.Children.Add(button);
	}

	/// <summary>Clears data held by the WebView2 profile shared by this instance's tabs.</summary>
	private async Task ClearEngineData(CoreWebView2BrowsingDataKinds kinds)
	{
		if (_mainWindow.ExtensionPopupWebview.CoreWebView2?.Profile is not { } profile)
			throw new Exception("The browser engine is still starting. Try again in a moment.");
		await profile.ClearBrowsingDataAsync(kinds);
	}

	protected override void ApplyTheme()
	{
		if (_card is null) return;

		_card.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
		_card.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
		foreach (var button in _buttons)
		{
			button.CurrentTheme = WebUiStyle.DangerTheme(CurrentTheme) with { PrimaryHighlightColor = CurrentTheme.NoColorVeryTransparent };
			WebUiStyle.ThemeIcon(button.Icon, CurrentTheme.PrimaryForegroundColor);
		}
	}
}
