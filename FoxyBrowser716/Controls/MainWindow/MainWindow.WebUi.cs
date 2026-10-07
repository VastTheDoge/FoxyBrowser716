using System.ComponentModel;
using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.WebUi;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.ErrorHandeler;
using Material.Icons.WinUI3;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WinUIEx;

namespace FoxyBrowser716.Controls.MainWindow;

/// <summary>
/// The window's side of the web content: FoxyBrowser-themed replacements for WebView2's built-in UI
/// (page context menu, permission prompts, alert/confirm/prompt, sign-in, downloads) plus the history panel,
/// toasts and live settings. Each <see cref="WebviewTab"/> forwards its WebView2 events here.
/// See Docs/architecture/web-ui.md.
/// </summary>
public sealed partial class MainWindow
{
	/// <summary>An InPrivate window: no history, session-only downloads, permission decisions are not remembered.</summary>
	public bool IsPrivate { get; private set; }

	private DownloadManager? _privateDownloads;

	/// <summary>The downloads this window shows: the instance's, or a session-only list for a private window.</summary>
	public DownloadManager Downloads => IsPrivate ? _privateDownloads ??= new DownloadManager(null) : Instance.Downloads;

	private WebPromptHost? _prompts;
	private ToastHost? _toasts;
	private FContextMenu? _webContextMenu;
	private Action? _pendingWebMenuCompletion;

	private Popup? _downloadsPopup;
	private DownloadsPanel? _downloadsPanel;
	private Popup? _historyPopup;
	private HistoryPanel? _historyPanel;

	private void InitializeWebUi()
	{
		_prompts = new WebPromptHost(BorderGrid, GetTabAreaBounds);
		_toasts = new ToastHost(BorderGrid);

		_webContextMenu = new FContextMenu { UseSolidBackground = true };
		_webContextMenu.OnClose += () =>
		{
			var complete = _pendingWebMenuCompletion;
			_pendingWebMenuCompletion = null;
			complete?.Invoke();
		};
		BorderGrid.Children.Add(_webContextMenu);

		TabHolder.SizeChanged += (_, _) => _prompts.Reposition();
		Instance.Settings.PropertyChanged += OnSettingsChanged;

		ApplyWebUiTheme();
	}

	private void ShutdownWebUi()
	{
		Instance.Settings.PropertyChanged -= OnSettingsChanged;
		_prompts?.CancelAll();
	}

	private void ApplyWebUiTheme()
	{
		if (_prompts is null) return; // ApplyTheme runs in the constructor, before InitializeWebUi

		_prompts.CurrentTheme = CurrentTheme;
		_toasts!.CurrentTheme = CurrentTheme;
		_webContextMenu!.CurrentTheme = CurrentTheme;
		if (_downloadsPanel is not null) _downloadsPanel.CurrentTheme = CurrentTheme;
		if (_historyPanel is not null) _historyPanel.CurrentTheme = CurrentTheme;
	}

	/// <summary>The tab content area in <see cref="BorderGrid"/> coordinates.</summary>
	private Rect GetTabAreaBounds()
	{
		var origin = TabHolder.TransformToVisual(BorderGrid).TransformPoint(new Point(0, 0));
		return new Rect(origin.X, origin.Y, TabHolder.ActualWidth, TabHolder.ActualHeight);
	}

	public void ShowToast(string title, string? message = null, MaterialIconKind icon = MaterialIconKind.Information, bool isError = false)
		=> _toasts?.Show(title, message, icon, isError);

	private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
	{
		foreach (var tab in TabManager.GetAllTabs().Values)
			tab.ApplySettings();
	}

	#region Context menu
	internal void OnTabContextMenuRequested(WebviewTab tab, CoreWebView2ContextMenuRequestedEventArgs args)
	{
		if (!Instance.Settings.ThemedContextMenu || _webContextMenu is null) return; // engine menu

		// a new right-click while our menu is still open: finish the old request without a selection
		var previous = _pendingWebMenuCompletion;
		_pendingWebMenuCompletion = null;
		previous?.Invoke();

		var deferral = args.GetDeferral();
		args.Handled = true;

		int? selectedCommand = null;
		var finished = false;
		_pendingWebMenuCompletion = () =>
		{
			if (finished) return;
			finished = true;
			try
			{
				if (selectedCommand is { } id) args.SelectedCommandId = id;
				deferral.Complete();
			}
			catch (Exception e) { FoxyLogger.AddWarning("Completing the page context menu failed", e.Message); }
		};

		// Location is in raw pixels relative to the WebView; XAML layout is in DIPs
		var scale = tab.Core.XamlRoot?.RasterizationScale ?? 1.0;
		var position = tab.Core.TransformToVisual(BorderGrid)
			.TransformPoint(new Point(args.Location.X / scale, args.Location.Y / scale));
		var bounds = new Rect(0, 0, BorderGrid.ActualWidth, BorderGrid.ActualHeight);

		var target = args.ContextMenuTarget;
		var extraItems = BuildExtraContextMenuItems(target);
		var parents = new Stack<IList<CoreWebView2ContextMenuItem>>();

		void ShowLevel(IList<CoreWebView2ContextMenuItem> level)
		{
			var items = new List<FContextMenu.MenuItem>();

			if (parents.Count > 0)
			{
				items.Add(new FContextMenu.MenuItem(Icon(MaterialIconKind.ChevronLeft), 1, "Back", () => ShowLevel(parents.Pop()), closeOnClick: false));
				items.Add(FContextMenu.MenuItem.Separator());
			}
			else if (extraItems.Count > 0)
			{
				items.AddRange(extraItems);
				items.Add(FContextMenu.MenuItem.Separator());
			}

			foreach (var item in level)
			{
				switch (item.Kind)
				{
					case CoreWebView2ContextMenuItemKind.Separator:
						items.Add(FContextMenu.MenuItem.Separator());
						continue;
					case CoreWebView2ContextMenuItemKind.Submenu:
						var children = item.Children;
						items.Add(new FContextMenu.MenuItem(IconFor(item.Name), 1, $"{CleanLabel(item.Label)}  ›", () =>
						{
							parents.Push(level);
							ShowLevel(children);
						}, closeOnClick: false) { IsEnabled = item.IsEnabled });
						continue;
				}

				// replaced by our own "Open link in new tab" above
				if (item.Name == "openLinkInNewWindow") continue;

				var commandId = item.CommandId;
				items.Add(new FContextMenu.MenuItem(IconFor(item.Name), 1, CleanLabel(item.Label), () => selectedCommand = commandId)
				{
					IsEnabled = item.IsEnabled,
					IsChecked = item.Kind is CoreWebView2ContextMenuItemKind.CheckBox or CoreWebView2ContextMenuItemKind.Radio ? item.IsChecked : null,
				});
			}

			_webContextMenu.SetItems(items);
			_webContextMenu.PlaceWithin(position, bounds);
		}

		try
		{
			ShowLevel(args.MenuItems);
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
			_pendingWebMenuCompletion?.Invoke();
			_pendingWebMenuCompletion = null;
		}
	}

	private List<FContextMenu.MenuItem> BuildExtraContextMenuItems(CoreWebView2ContextMenuTarget target)
	{
		var items = new List<FContextMenu.MenuItem>();

		if (target.HasLinkUri && !string.IsNullOrEmpty(target.LinkUri))
		{
			var link = target.LinkUri;
			items.Add(new FContextMenu.MenuItem(Icon(MaterialIconKind.OpenInNew), 1, "Open link in new tab", () => TabManager.AddTab(link)));
			if (!IsPrivate)
				items.Add(new FContextMenu.MenuItem(Icon(MaterialIconKind.Incognito), 1, "Open link in private window",
					() => _ = Instance.CreateWindow([link], isPrivate: true)));
		}

		if (target.HasSelection && !string.IsNullOrWhiteSpace(target.SelectionText))
		{
			var selection = target.SelectionText.Trim();
			var shown = selection.Length > 24 ? selection[..24] + "…" : selection;
			var engine = Instance.Cache.CurrentSearchEngine;
			items.Add(new FContextMenu.MenuItem(Icon(MaterialIconKind.Magnify), 1,
				$"Search {InfoGetter.GetSearchEngineName(engine)} for “{shown}”",
				() => TabManager.SwapActiveTabTo(TabManager.AddTab(InfoGetter.GetSearchUrl(engine, selection)))));
		}

		return items;
	}

	private static MaterialIcon Icon(MaterialIconKind kind) => new() { Kind = kind };

	/// <summary>Labels mark access keys with '&amp;' ("&amp;Back"); "&amp;&amp;" is a literal ampersand.</summary>
	private static string CleanLabel(string label) =>
		label.Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&");

	/// <summary>Icons for WebView2's built-in menu items (CoreWebView2ContextMenuItem.Name).</summary>
	private static MaterialIcon? IconFor(string name)
	{
		MaterialIconKind? kind = name switch
		{
			"back" => MaterialIconKind.ArrowLeft,
			"forward" => MaterialIconKind.ArrowRight,
			"reload" => MaterialIconKind.Refresh,
			"saveAs" or "saveLinkAs" or "saveImageAs" or "saveMediaAs" => MaterialIconKind.ContentSave,
			"print" => MaterialIconKind.Printer,
			"createQrCode" => MaterialIconKind.Qrcode,
			"inspectElement" => MaterialIconKind.CodeTags,
			"emoji" => MaterialIconKind.EmoticonHappy,
			"undo" => MaterialIconKind.Undo,
			"redo" => MaterialIconKind.Redo,
			"cut" => MaterialIconKind.ContentCut,
			"copy" => MaterialIconKind.ContentCopy,
			"paste" or "pasteAndMatchStyle" => MaterialIconKind.ContentPaste,
			"selectAll" => MaterialIconKind.SelectAll,
			"copyLinkLocation" or "copyLinkToHighlight" or "copyImageLocation" => MaterialIconKind.LinkVariant,
			"copyImage" => MaterialIconKind.Image,
			"pictureInPicture" => MaterialIconKind.Monitor,
			"loop" => MaterialIconKind.Refresh,
			"share" => MaterialIconKind.Share,
			"webCapture" or "screenshot" => MaterialIconKind.CameraOutline,
			"readAloud" => MaterialIconKind.VolumeHigh,
			"translate" => MaterialIconKind.Translate,
			"spellCheck" or "spellcheck" => MaterialIconKind.Check,
			_ => null,
		};
		return kind is { } k ? Icon(k) : null;
	}
	#endregion

	#region Site prompts
	internal void OnTabPermissionRequested(WebviewTab tab, CoreWebView2PermissionRequestedEventArgs args)
	{
		var origin = SitePermissionManager.GetOrigin(args.Uri);
		var kind = args.PermissionKind;

		// remembered decisions and Allow/Block defaults apply silently, themed prompts or not
		if (Instance.SitePermissions.Resolve(origin, kind, Instance.Settings) is { } decision)
		{
			args.SavesInProfile = false;
			args.State = decision == PermissionDecision.Allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
			return;
		}

		if (!Instance.Settings.ThemedDialogs || _prompts is null) return; // engine prompt

		args.SavesInProfile = false;
		var deferral = args.GetDeferral();
		var completed = false;
		void Complete(CoreWebView2PermissionState state)
		{
			if (completed) return;
			completed = true;
			args.State = state;
			deferral.Complete();
		}

		void Decide(PermissionDecision chosen, WebPromptResult result)
		{
			if (result.IsChecked && !IsPrivate)
			{
				Instance.SitePermissions.SetDecision(origin, kind, chosen);
				ShowToast($"Saved for {SitePermissionManager.GetDisplayHost(origin)}",
					$"{SitePermissionManager.GetDisplayName(kind)}: {(chosen == PermissionDecision.Allow ? "allowed" : "blocked")}. Change it in Settings > Site Permissions.",
					SitePermissionManager.GetIcon(kind));
			}
			Complete(chosen == PermissionDecision.Allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny);
		}

		_prompts.Enqueue(tab.Id, new WebPromptSpec
		{
			Title = $"{SitePermissionManager.GetDisplayHost(origin)} wants to",
			Message = SitePermissionManager.GetRequestText(kind),
			Icon = SitePermissionManager.GetIcon(kind),
			// private windows never remember decisions
			CheckboxText = IsPrivate ? null : "Remember this decision",
			CheckboxDefault = Instance.Settings.RememberPermissionDecisions,
			Buttons =
			[
				new WebPromptButton("Block", false, r => Decide(PermissionDecision.Block, r), IsDanger: true),
				new WebPromptButton("Allow", true, r => Decide(PermissionDecision.Allow, r)),
			],
			Cancelled = () => Complete(CoreWebView2PermissionState.Deny),
		});
	}

	/// <summary>Only raised while BrowserSettings.ThemedDialogs is on (default script dialogs are off then).</summary>
	internal void OnTabScriptDialogOpening(WebviewTab tab, CoreWebView2ScriptDialogOpeningEventArgs args)
	{
		if (_prompts is null) return;

		var deferral = args.GetDeferral();
		var finished = false;
		void Finish(bool accept, string? resultText = null)
		{
			if (finished) return;
			finished = true;
			if (resultText is not null) args.ResultText = resultText;
			if (accept) args.Accept();
			deferral.Complete();
		}

		var host = SitePermissionManager.GetDisplayHost(args.Uri);
		WebPromptSpec spec = args.Kind switch
		{
			CoreWebView2ScriptDialogKind.Alert => new WebPromptSpec
			{
				Title = $"{host} says",
				Message = args.Message,
				Icon = MaterialIconKind.Information,
				Buttons = [new WebPromptButton("OK", true, _ => Finish(true))],
				Cancelled = () => Finish(false),
			},
			CoreWebView2ScriptDialogKind.Confirm => new WebPromptSpec
			{
				Title = $"{host} says",
				Message = args.Message,
				Icon = MaterialIconKind.HelpCircle,
				Buttons =
				[
					new WebPromptButton("Cancel", false, _ => Finish(false)),
					new WebPromptButton("OK", true, _ => Finish(true)),
				],
				Cancelled = () => Finish(false),
			},
			CoreWebView2ScriptDialogKind.Prompt => new WebPromptSpec
			{
				Title = $"{host} says",
				Message = args.Message,
				Icon = MaterialIconKind.HelpCircle,
				HasTextInput = true,
				DefaultText = args.DefaultText,
				Buttons =
				[
					new WebPromptButton("Cancel", false, _ => Finish(false)),
					new WebPromptButton("OK", true, r => Finish(true, r.Text)),
				],
				Cancelled = () => Finish(false),
			},
			_ => new WebPromptSpec
			{
				Title = "Leave this site?",
				Message = "Changes you made may not be saved.",
				Icon = MaterialIconKind.Alert,
				Buttons =
				[
					new WebPromptButton("Stay", false, _ => Finish(false)),
					new WebPromptButton("Leave", true, _ => Finish(true), IsDanger: true),
				],
				Cancelled = () => Finish(false),
			},
		};

		_prompts.Enqueue(tab.Id, spec);
	}

	internal void OnTabBasicAuthenticationRequested(WebviewTab tab, CoreWebView2BasicAuthenticationRequestedEventArgs args)
	{
		if (!Instance.Settings.ThemedDialogs || _prompts is null) return; // engine sign-in dialog

		var deferral = args.GetDeferral();
		var finished = false;
		void Finish(WebPromptResult? credentials)
		{
			if (finished) return;
			finished = true;
			if (credentials is null)
				args.Cancel = true;
			else
			{
				args.Response.UserName = credentials.UserName;
				args.Response.Password = credentials.Password;
			}
			deferral.Complete();
		}

		_prompts.Enqueue(tab.Id, new WebPromptSpec
		{
			Title = $"Sign in to {SitePermissionManager.GetDisplayHost(args.Uri)}",
			Message = string.IsNullOrWhiteSpace(args.Challenge) ? "This site needs a user name and password." : args.Challenge,
			Icon = MaterialIconKind.Lock,
			HasCredentials = true,
			Buttons =
			[
				new WebPromptButton("Cancel", false, _ => Finish(null)),
				new WebPromptButton("Sign in", true, r => Finish(r)),
			],
			Cancelled = () => Finish(null),
		});
	}
	#endregion

	#region Downloads
	internal async void OnTabDownloadStarting(WebviewTab tab, CoreWebView2DownloadStartingEventArgs args)
	{
		// extension installs from the Chrome Web Store are handled by ExtensionManager
		if (tab.Core.CoreWebView2.Source.Contains("chromewebstore.google.com")) return;
		if (!Instance.Settings.ThemedDownloads) return; // engine download flyout

		args.Handled = true; // hides the engine flyout; the download itself continues
		var deferral = args.GetDeferral();
		try
		{
			if (Instance.Settings.AskWhereToSaveDownloads)
			{
				var suggested = args.ResultFilePath;
				var extension = Path.GetExtension(suggested);
				var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(suggested) };
				picker.FileTypeChoices.Add(string.IsNullOrEmpty(extension) ? "File" : $"{extension.TrimStart('.').ToUpperInvariant()} file",
					[string.IsNullOrEmpty(extension) ? "." : extension]);
				InitializeWithWindow.Initialize(picker, this.GetWindowHandle());

				var file = await picker.PickSaveFileAsync();
				if (file is null)
				{
					args.Cancel = true;
					return;
				}

				// the picker creates an empty placeholder; remove it so WebView2 writes to exactly this path
				var path = file.Path;
				try { await file.DeleteAsync(); } catch { /* WebView2 overwrites it either way */ }
				args.ResultFilePath = path;
			}

			Downloads.Track(args.DownloadOperation, args.ResultFilePath);
			if (Instance.Settings.ShowDownloadsOnStart)
				ShowDownloadsPanel();
		}
		catch (Exception e)
		{
			FoxyLogger.AddError(e);
		}
		finally
		{
			deferral.Complete();
		}
	}

	private string GetDownloadFolder()
	{
		var configured = Instance.Settings.DownloadFolder;
		return !string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)
			? configured
			: DownloadManager.GetSystemDownloadsFolder() ?? string.Empty;
	}

	public void ShowDownloadsPanel()
	{
		if (_downloadsPopup is null)
		{
			_downloadsPanel = new DownloadsPanel(Downloads, GetDownloadFolder) { Width = 380, CurrentTheme = CurrentTheme };
			_downloadsPopup = new Popup
			{
				Child = _downloadsPanel,
				IsLightDismissEnabled = true,
				LightDismissOverlayMode = LightDismissOverlayMode.On,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(32, 28, 0, 0),
			};
			BorderGrid.Children.Add(_downloadsPopup);
		}

		_downloadsPopup.IsOpen = true;
	}
	#endregion

	#region History
	public void ShowHistoryPanel(string? query = null)
	{
		if (_historyPopup is null)
		{
			_historyPanel = new HistoryPanel(Instance.History, url =>
			{
				TabManager.SwapActiveTabTo(TabManager.AddTab(url));
				_historyPopup!.IsOpen = false;
			}) { CurrentTheme = CurrentTheme };
			_historyPopup = new Popup
			{
				Child = _historyPanel,
				IsLightDismissEnabled = true,
				LightDismissOverlayMode = LightDismissOverlayMode.On,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(32, 28, 0, 0),
			};
			BorderGrid.Children.Add(_historyPopup);
		}

		_historyPanel!.Width = Math.Max(300, Math.Min(620, BorderGrid.ActualWidth - 48));
		_historyPanel.Height = Math.Max(240, BorderGrid.ActualHeight - 44);
		_historyPanel.SetQuery(query ?? string.Empty);
		_historyPopup.IsOpen = true;
	}
	#endregion
}
