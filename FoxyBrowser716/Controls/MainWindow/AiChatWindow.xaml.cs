using System.ComponentModel;
using CommunityToolkit.WinUI.UI.Controls;
using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.WebUi;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.ErrorHandeler;
using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.DataObjects.Complex.Ai;
using FoxyBrowser716.DataObjects.Settings;
using Material.Icons.WinUI3;
using System.Threading;
using Microsoft.UI.Xaml.Documents;

namespace FoxyBrowser716.Controls.MainWindow;

public sealed partial class AiChatWindow : UserControl
{
    private const string BillingUrl = "https://platform.claude.com/settings/billing";

    private enum PanelView { Chat, History, Settings, Models }

    private AiHandler _aiHandler = null!;
    private MainWindow _mainWindow = null!;
    private TabGroupNamer _groupNamer = null!;
    private bool _busy;
    private CancellationTokenSource? _turnCancel;
    private PanelView _view = PanelView.Chat;
    private FContextMenu _menu = null!;
    // Claude Doc
    /// <summary>Permission mode picked before the first message of a new chat; null = the settings default.</summary>
    private AiPermissionMode? _nextChatMode;

    public event Action? CloseRequested;

    public AiChatWindow()
    {
        InitializeComponent();
        Input.Multiline = true;
        Input.SubmitOnEnter = true;
        ApplyTheme();
        //C the budget or defaults may have changed while the panel was closed
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible && _aiHandler is not null)
            {
                UpdateToolbar();
                UpdateUsage();
            }
        });
    }

    private BrowserSettings Settings => _mainWindow.Instance.Settings;

    public async Task Initialize(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _aiHandler = new AiHandler(mainWindow);
        _groupNamer = new TabGroupNamer(mainWindow);

        _menu = new FContextMenu { UseSolidBackground = true, CurrentTheme = CurrentTheme };
        Grid.SetRowSpan(_menu, 5);
        Root.Children.Add(_menu);

        try
        {
            await Task.WhenAll(AiBudget.EnsureLoaded(), _aiHandler.LoadSaved());
        }
        catch (Exception e)
        {
            FoxyLogger.AddError(e);
        }

        AiBudget.Changed += UpdateUsage;
        Settings.PropertyChanged += OnSettingsChanged;
        mainWindow.Closed += (_, _) =>
        {
            AiBudget.Changed -= UpdateUsage;
            Settings.PropertyChanged -= OnSettingsChanged;
        };

        ShowChat(null);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserSettings.AiEnabled) && !Settings.AiEnabled && Visibility == Visibility.Visible)
            ButtonClose_OnClick(this, new RoutedEventArgs());
    }

    internal Theme CurrentTheme { get; set { field = value; ApplyTheme(); } } = DefaultThemes.DarkMode;

    private void ApplyTheme()
    {
        Root.BorderBrush = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);
        Root.Background = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);

        TitleText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
        MText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
        UsageText.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
        BillingLink.Foreground = new SolidColorBrush(CurrentTheme.PrimaryAccentColor);

        Input.CurrentTheme = CurrentTheme;
        ButtonModel.CurrentTheme = CurrentTheme;
        ButtonEffort.CurrentTheme = CurrentTheme;
        ButtonMode.CurrentTheme = CurrentTheme;
        ButtonSend.CurrentTheme = CurrentTheme;
        ButtonSettings.CurrentTheme = CurrentTheme;
        ButtonHistory.CurrentTheme = CurrentTheme;
        ButtonClose.CurrentTheme = CurrentTheme with
        {
            SecondaryForegroundColor = CurrentTheme.NoColor,
            PrimaryHighlightColor = CurrentTheme.NoColor
        };
        ButtonNewChat.CurrentTheme = CurrentTheme;
        if (_menu is not null) _menu.CurrentTheme = CurrentTheme;
        foreach (var child in MessageList.Children)
            if (child is ThemedUserControl themed) themed.CurrentTheme = CurrentTheme;
        //C rebuilds the themed tooltips
        if (_aiHandler is not null)
        {
            UpdateToolbar();
            UpdateUsage();
        }
    }

    #region Views
    private void ShowChat(AiChat? chat, string? note = null)
    {
        _view = PanelView.Chat;
        MessageList.Children.Clear();
        TitleText.Text = chat is null ? "New Chat" : $"Chat #{chat.Id}";

        if (chat is not null)
            foreach (var entry in chat.Transcript)
                MessageList.Children.Add(GetMessageBubble(entry.Role, entry.Text).Item1);

        MText.Text = note ?? "Ask anything, or ask me to open, group or navigate your tabs.";
        MText.Visibility = chat is null || chat.Transcript.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ButtonNewChat.Visibility = Visibility.Collapsed;
        SetInputVisible(true);
        UpdateToolbar();
        UpdateUsage();
        ScrollToEnd();
    }

    private void ShowPanel(PanelView view, string title, UIElement content)
    {
        _view = view;
        MessageList.Children.Clear();
        MessageList.Children.Add(content);
        TitleText.Text = title;
        MText.Visibility = Visibility.Collapsed;
        ButtonNewChat.Visibility = view == PanelView.History ? Visibility.Visible : Visibility.Collapsed;
        SetInputVisible(false);
        Scroller.ChangeView(null, 0, null, true);
    }

    private void ShowSettings()
    {
        var view = new AiSettingsView(_mainWindow) { CurrentTheme = CurrentTheme };
        view.ConnectionChanged += UpdateToolbar;
        ShowPanel(PanelView.Settings, "Assistant settings", view);
    }

    private void ShowModels()
    {
        var picker = new AiModelPicker(Settings, CurrentModel, CurrentTheme);
        picker.Picked += OnModelPicked;
        ShowPanel(PanelView.Models, "Choose a model", picker);
    }

    private void ShowHistory()
    {
        var list = new StackPanel();
        foreach (var chat in _aiHandler.Chats.Values.OrderByDescending(c => c.CreationTime))
            list.Children.Add(HistoryCard(chat, list));
        if (list.Children.Count == 0)
            list.Children.Add(new TextBlock
            {
                Text = "No chats yet.",
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor),
            });
        ShowPanel(PanelView.History, "History", list);
    }

    private void SetInputVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Input.Visibility = visibility;
        //C while a reply runs the send button stays as the stop button
        ButtonSend.Visibility = visible || _busy ? Visibility.Visible : Visibility.Collapsed;
        ButtonSend.Content = new MaterialIcon { Kind = _busy ? MaterialIconKind.Stop : MaterialIconKind.Send };
        ButtonSend.ToolTipText = _busy ? "Stop the reply" : "Send";
    }

    private void ScrollToEnd()
    {
        Scroller.UpdateLayout();
        Scroller.ChangeView(null, Scroller.ScrollableHeight, null);
    }

    private void Toggle(PanelView view, Action show)
    {
        if (_busy) return;
        if (_view == view) ShowChat(_aiHandler.ActiveChat);
        else show();
    }

    private void ButtonSettings_OnClick(object sender, RoutedEventArgs e) => Toggle(PanelView.Settings, ShowSettings);

    private void ButtonHistory_OnClick(object sender, RoutedEventArgs e) => Toggle(PanelView.History, ShowHistory);

    private void ButtonModel_OnClick(object sender, RoutedEventArgs e) => Toggle(PanelView.Models, ShowModels);

    private Grid HistoryCard(AiChat chat, StackPanel list)
    {
        var chatCard = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star)},
                new ColumnDefinition { Width = GridLength.Auto},
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto},
                new RowDefinition { Height = GridLength.Auto},
            },
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(5),
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColor),
        };

        var buttonRemove = new FIconButton
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.Delete },
            CurrentTheme = CurrentTheme,
            Width = 26,
            Height = 26,
        };
        buttonRemove.OnClick += (_, _) =>
        {
            _aiHandler.DeleteChat(chat.Id);
            list.Children.Remove(chatCard);
        };

        var title = new TextBlock
        {
            Text = $"Chat #{chat.Id}",
            FontSize = 20,
            Height = 26,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
            Margin = new Thickness(5, 5, 5, 0),
        };

        var date = new TextBlock
        {
            Text = $"{chat.CreationTime:dd/MM/yyyy HH:mm:ss} · {AiModels.DisplayName(chat.Model)}",
            FontSize = 14,
            Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor),
            Margin = new Thickness(5, 0, 5, 5),
        };

        chatCard.Children.Add(title);
        chatCard.Children.Add(buttonRemove);
        chatCard.Children.Add(date);
        Grid.SetColumn(buttonRemove, 1);
        Grid.SetRow(date, 1);

        chatCard.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || buttonRemove.PointerOver) return;
            _aiHandler.SwapChat(chat.Id);
            ShowChat(chat);
        };
        return chatCard;
    }
    #endregion

    #region Model, effort, permission mode
    private AiModelChoice CurrentModel => _aiHandler.ActiveChat?.Model ?? Settings.AiModel;

    private AiEffort CurrentEffort => _aiHandler.ActiveChat?.Effort ?? Settings.AiEffort;

    private AiPermissionMode CurrentMode => _aiHandler.ActiveChat?.PermissionMode ?? _nextChatMode ?? Settings.AiPermissionMode;

    private static string EffortName(AiEffort effort) => effort == AiEffort.XHigh ? "Extra high" : effort.ToString();

    private void UpdateToolbar()
    {
        var model = CurrentModel;
        ButtonModel.ButtonText = AiModels.DisplayName(model);
        FToolTip.Set(ButtonModel, $"{model.Model} ({(model.Provider == AiProviderKind.Claude ? "Claude" : "OpenAI-compatible")}). Click to change the model.", CurrentTheme);

        ButtonEffort.Visibility = model.Provider == AiProviderKind.Claude ? Visibility.Visible : Visibility.Collapsed;
        ButtonEffort.ButtonText = EffortName(CurrentEffort);
        FToolTip.Set(ButtonEffort, "Effort: how much Claude thinks before answering. Higher can answer hard questions better but is slower and costs more.", CurrentTheme);

        var mode = CurrentMode;
        ButtonMode.Content = new MaterialIcon { Kind = AiPermissions.Icon(mode) };
        ButtonMode.ToolTipText = $"Permissions for this chat: {AiPermissions.Name(mode)}\n{AiPermissions.Explain(mode)}";
    }

    // Claude Doc
    /// <summary>
    /// Picking a model sets it for the open chat and as the default for new ones. A chat can't change provider (its
    /// history is in that provider's format), so a model from the other provider starts a new chat.
    /// </summary>
    private void OnModelPicked(AiModelChoice choice)
    {
        Settings.AiModel = choice;
        var chat = _aiHandler.ActiveChat;

        if (chat is not null && chat.Provider == choice.Provider)
        {
            chat.SetModel(choice);
            _aiHandler.Save(chat);
        }
        else if (chat is not null && chat.Transcript.Count == 0)
            _aiHandler.DeleteChat(chat.Id); // nothing said yet: the next message starts a chat with the new model
        else if (chat is not null)
        {
            ShowChat(_aiHandler.NewChat(choice, chat.PermissionMode),
                "New chat: a chat can't switch between Claude and OpenAI-compatible models. The previous one is in History.");
            return;
        }

        ShowChat(_aiHandler.ActiveChat);
    }

    private void ButtonEffort_OnClick(object sender, RoutedEventArgs e)
    {
        var current = CurrentEffort;
        _menu.SetItems(Enum.GetValues<AiEffort>().Select(effort =>
            new FContextMenu.MenuItem(new MaterialIcon { Kind = MaterialIconKind.Brain }, 1, EffortName(effort), () =>
            {
                Settings.AiEffort = effort;
                if (_aiHandler.ActiveChat is { } chat)
                {
                    chat.Effort = effort;
                    _aiHandler.Save(chat);
                }
                UpdateToolbar();
            })
            {
                IsChecked = effort == current,
            }));
        OpenMenuAt(ButtonEffort);
    }

    // Claude Doc
    /// <summary>Picks what the assistant may do without asking, for this chat (mid-reply too, from the next tool call).</summary>
    private void ButtonMode_OnClick(object sender, RoutedEventArgs e)
    {
        var current = CurrentMode;
        _menu.SetItems(Enum.GetValues<AiPermissionMode>().Select(mode =>
            new FContextMenu.MenuItem(new MaterialIcon { Kind = AiPermissions.Icon(mode) }, 1, AiPermissions.Name(mode), () =>
            {
                if (_aiHandler.ActiveChat is { } chat)
                {
                    chat.PermissionMode = mode;
                    _aiHandler.Save(chat);
                }
                else _nextChatMode = mode;
                UpdateToolbar();
            })
            {
                IsChecked = mode == current,
            }));
        OpenMenuAt(ButtonMode);
    }

    private void OpenMenuAt(FrameworkElement anchor)
    {
        var position = anchor.TransformToVisual(Root).TransformPoint(new Point(0, anchor.ActualHeight));
        _menu.PlaceWithin(position, new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
    }
    #endregion

    #region Usage
    private void UpdateUsage()
    {
        if (_aiHandler is null) return;
        var usage = _aiHandler.ActiveChat?.Usage ?? AiUsage.Zero;
        var claude = CurrentModel.Provider == AiProviderKind.Claude;

        List<string> parts = [];
        if (usage.TotalInputTokens + usage.OutputTokens > 0)
        {
            parts.Add($"{FormatTokens(usage.TotalInputTokens)} in · {FormatTokens(usage.OutputTokens)} out");
            if (usage.CostUsd is { } cost) parts.Add($"≈{FormatCost(cost)}");
        }
        if (claude && AiBudget.IsLoaded)
        {
            var (spent, cap, _, _) = AiBudget.Current();
            parts.Add($"month ≈${spent:0.00} of ${cap:0.##}{(cap > 0 && spent >= cap ? " (over budget)" : "")}");
        }

        UsageText.Text = string.Join(" · ", parts);
        FToolTip.Set(UsageText, UsageToolTip(usage, claude), CurrentTheme);
        BillingText.Visibility = claude ? Visibility.Visible : Visibility.Collapsed;
    }

    private string UsageToolTip(AiUsage chatUsage, bool claude)
    {
        var sb = new StringBuilder();
        sb.AppendLine("This chat:");
        sb.AppendLine($"  Input: {chatUsage.InputTokens:N0} tokens");
        if (chatUsage.CacheReadTokens + chatUsage.CacheWriteTokens > 0)
            sb.AppendLine($"  Cached input: {chatUsage.CacheReadTokens:N0} read, {chatUsage.CacheWriteTokens:N0} written");
        sb.AppendLine($"  Output: {chatUsage.OutputTokens:N0} tokens (includes thinking)");
        if (chatUsage.CostUsd is { } cost) sb.AppendLine($"  Estimated cost: {FormatCost(cost)}");

        if (_aiHandler.Chats.Count > 1)
        {
            var total = _aiHandler.TotalUsage;
            sb.AppendLine();
            sb.AppendLine($"All chats in this window: {total.TotalInputTokens:N0} in, {total.OutputTokens:N0} out"
                + (total.CostUsd is { } totalCost ? $", ≈{FormatCost(totalCost)}" : ""));
        }

        if (claude && AiBudget.IsLoaded)
        {
            var (spent, cap, start, next) = AiBudget.Current();
            sb.AppendLine();
            sb.AppendLine($"Claude since {start:MMM d}: ≈${spent:0.00} of your ${cap:0.##} budget; resets {next:MMM d}.");
            sb.AppendLine("Counts this browser's use only (every instance). Set the budget in the assistant's settings.");
        }

        sb.AppendLine();
        sb.Append("Costs are estimated from list prices. Your actual bill and remaining credits are in the provider's console.");
        return sb.ToString();
    }

    private static string FormatTokens(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000d:0.#}M",
        >= 1_000 => $"{tokens / 1_000d:0.#}k",
        _ => tokens.ToString(),
    };

    private static string FormatCost(decimal cost) => cost < 0.01m ? $"${cost:0.0000}" : $"${cost:0.00}";

    private void BillingLink_OnClick(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        _mainWindow.TabManager.SwapActiveTabTo(_mainWindow.TabManager.AddTab(BillingUrl));
    }
    #endregion

    internal void ButtonClose_OnClick(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        CloseRequested?.Invoke();
    }

    #region Approval
    private Task<bool> ShowApprovalCard(AiApprovalRequest request, CancellationToken cancellationToken)
    {
        var decision = new TaskCompletionSource<bool>();
        var (bubble, _) = GetMessageBubble(AiRole.Assistant, $"**Allow?** {request.Description}");
        bubble.BorderThickness = new Thickness(2);
        bubble.BorderBrush = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 4,
            Margin = new Thickness(5, 4, 5, 0),
        };
        var deny = WebUiStyle.TextButton("Deny", () => Decide(false), MaterialIconKind.Close);
        var allow = WebUiStyle.TextButton("Allow", () => Decide(true), MaterialIconKind.Check);
        allow.ForceHighlight = true;
        deny.CurrentTheme = allow.CurrentTheme = CurrentTheme;
        buttons.Children.Add(deny);
        buttons.Children.Add(allow);

        MessageList.Children.Add(new StackPanel { Children = { bubble, buttons } });
        ScrollToEnd();
        cancellationToken.Register(() => Decide(false));
        return decision.Task;

        void Decide(bool allowed)
        {
            if (decision.Task.IsCompleted) return;
            buttons.Children.Clear();
            buttons.Children.Add(new TextBlock
            {
                Text = allowed ? "Allowed" : "Denied",
                FontSize = 12,
                Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor),
            });
            bubble.BorderBrush = new SolidColorBrush(CurrentTheme.NoColor);
            decision.SetResult(allowed);
        }
    }
    #endregion

    private void ButtonSend_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy) _turnCancel?.Cancel();
        else Input_OnEnterPressed();
    }

    private async void Input_OnEnterPressed()
    {
        if (_busy || _view != PanelView.Chat) return;

        //C CurrentText, not Text: FTextInput.Text isn't updated while typing
        var input = Input.CurrentText.Trim();
        if (input.Length == 0) return;
        Input.SetText("");

        var chat = _aiHandler.ActiveChat ?? _aiHandler.NewChat(permissionMode: _nextChatMode);
        _nextChatMode = null;
        TitleText.Text = $"Chat #{chat.Id}";
        MText.Visibility = Visibility.Collapsed;

        MessageList.Children.Add(GetMessageBubble(AiRole.User, input).Item1);
        var (aiBubble, aiText) = GetMessageBubble(AiRole.Assistant, "");
        MessageList.Children.Add(aiBubble);
        ScrollToEnd();

        _busy = true;
        using var cancel = _turnCancel = new CancellationTokenSource();
        SetInputVisible(false);
        try
        {
            //C stays on the UI thread: the providers' awaits resume here, so callbacks and tools can touch the UI
            await chat.UserRequest(input, new AiTurnCallbacks
            {
                Text = text => StreamText(aiText, text),
                Usage = _ => UpdateUsage(),
                Approve = async request =>
                {
                    FlushStream();
                    var allowed = await ShowApprovalCard(request, cancel.Token);
                    if (aiText.Text.Length == 0) MessageList.Children.Remove(aiBubble);
                    (aiBubble, aiText) = GetMessageBubble(AiRole.Assistant, "");
                    MessageList.Children.Add(aiBubble);
                    ScrollToEnd();
                    return allowed;
                },
            }, cancel.Token);
            FlushStream();
            if (aiText.Text.Length == 0) MessageList.Children.Remove(aiBubble);
            _aiHandler.Save(chat);
        }
        catch (Exception) when (cancel.IsCancellationRequested)
        {
            //C the turn was rolled back like any failed one; tool actions already done stay done
            FlushStream();
            Input.SetText(input);
            aiText.Text += (aiText.Text.Length > 0 ? "\n\n" : "") + "*(Stopped. The message is back in the box if you want to send it again.)*";
            aiBubble.Background = new SolidColorBrush(CurrentTheme.NoColor);
        }
        catch (Exception e)
        {
            FlushStream();
            if (e is not AiNotConfiguredException) FoxyLogger.AddError(e);

            //C the turn was rolled back, so hand the message back for a retry
            Input.SetText(input);
            aiText.Text = e is AiNotConfiguredException ? e.Message : $"**Error:** {e.Message}";
            aiBubble.Background = new SolidColorBrush(CurrentTheme.NoColor);
            if (e is AiNotConfiguredException)
                MessageList.Children.Add(OpenSettingsButton());
        }
        finally
        {
            _busy = false;
            _turnCancel = null;
            SetInputVisible(true);
            UpdateUsage();
            ScrollToEnd();
        }
    }

    #region Streaming
    //C MarkdownTextBlock re-parses its whole text on every change, so streamed text is applied in batches
    private readonly StringBuilder _streamBuffer = new();
    private MarkdownTextBlock? _streamTarget;
    private DispatcherTimer? _streamTimer;

    private void StreamText(MarkdownTextBlock target, string text)
    {
        if (_streamTarget != target) FlushStream();
        _streamTarget = target;
        _streamBuffer.Append(text);
        if (_streamTimer is null)
        {
            _streamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _streamTimer.Tick += (_, _) => FlushStream();
        }
        if (!_streamTimer.IsEnabled) _streamTimer.Start();
    }

    private void FlushStream()
    {
        _streamTimer?.Stop();
        if (_streamTarget is { } target && _streamBuffer.Length > 0)
            target.Text += _streamBuffer.ToString();
        _streamBuffer.Clear();
        _streamTarget = null;
    }
    #endregion

    private FTextButton OpenSettingsButton()
    {
        var button = new FTextButton
        {
            ButtonText = "Open assistant settings",
            Icon = new MaterialIcon { Kind = MaterialIconKind.Cog },
            CornerRadius = new CornerRadius(5),
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5, 5, 5, 0),
            CurrentTheme = CurrentTheme,
        };
        button.OnClick += (_, _) => ShowSettings();
        return button;
    }

    private (Border, MarkdownTextBlock) GetMessageBubble(AiRole role, string text)
    {
        var textBlock = new MarkdownTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
            Margin = new Thickness(5),
            Background = new SolidColorBrush(Colors.Transparent),

            // Code block styling
            CodeBackground = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColor),
            CodeBorderBrush = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor),
            CodeBorderThickness = new Thickness(1),
            CodeForeground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
            CodeFontFamily = new FontFamily("Consolas"),
            CodeMargin = new Thickness(0, 8, 0, 8),
            CodePadding = new Thickness(12),

            // Inline code styling
            InlineCodeBackground = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColor),
            InlineCodeBorderBrush = new SolidColorBrush(CurrentTheme.SecondaryHighlightColor),
            InlineCodeForeground = new SolidColorBrush(CurrentTheme.PrimaryAccentColor),
            InlineCodePadding = new Thickness(4, 2, 4, 2),
            InlineCodeMargin = new Thickness(2, 0, 2, 0),

            // Header styling
            Header1Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
            Header2Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
            Header3Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
            Header4Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
            Header5Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
            Header6Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),

            // Quote styling
            QuoteBackground = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent),
            QuoteBorderBrush = new SolidColorBrush(CurrentTheme.PrimaryHighlightColor),
            QuoteForeground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor),
            QuoteBorderThickness = new Thickness(4, 0, 0, 0),
            QuoteMargin = new Thickness(0, 8, 0, 8),
            QuotePadding = new Thickness(12, 8, 12, 8),

            // Link styling
            LinkForeground = new SolidColorBrush(CurrentTheme.PrimaryAccentColor),

            // Table styling
            TableBorderBrush = new SolidColorBrush(CurrentTheme.SecondaryHighlightColor),
            TableCellPadding = new Thickness(8, 4, 8, 4),
            TableMargin = new Thickness(0, 8, 0, 8),

            // List styling
            ListMargin = new Thickness(0, 4, 0, 4),
            ListGutterWidth = 32,

            // Horizontal rule styling
            HorizontalRuleBrush = new SolidColorBrush(CurrentTheme.SecondaryHighlightColor),
            HorizontalRuleMargin = new Thickness(0, 16, 0, 16),
            HorizontalRuleThickness = 1,

            // Enable syntax highlighting
            UseSyntaxHighlighting = true,
        };

        textBlock.LinkClicked += (_, e) =>
        {
            _mainWindow.TabManager.SwapActiveTabTo(_mainWindow.TabManager.AddTab(e.Link));
        };

        return (new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(role == AiRole.Assistant ? 50 : 5, 5,
                role == AiRole.User ? 50 : 5, 0),
            Child = textBlock,
            CornerRadius = new CornerRadius(10,role == AiRole.Assistant ? 0 : 10,
                10,role == AiRole.User ? 0 : 10),
            Background = role == AiRole.User ?
                new SolidColorBrush(CurrentTheme.SecondaryHighlightColor)
                : new SolidColorBrush(CurrentTheme.PrimaryBackgroundColor),
        }, textBlock);
    }

    private void ButtonNewChat_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _nextChatMode = null;
        ShowChat(_aiHandler.NewChat());
    }
}
