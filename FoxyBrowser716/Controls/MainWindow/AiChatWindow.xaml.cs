using CommunityToolkit.WinUI.UI.Controls;
using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.WebUi;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.ErrorHandeler;
using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.DataObjects.Complex.Ai;
using Material.Icons.WinUI3;
using Microsoft.UI.Xaml.Documents;

namespace FoxyBrowser716.Controls.MainWindow;

public sealed partial class AiChatWindow : UserControl
{
    /// <summary>Where Claude API credits are bought and the balance is shown; a normal API key can't read it.</summary>
    private const string BillingUrl = "https://platform.claude.com/settings/billing";

    private AiHandler _aiHandler = null!;
    private MainWindow _mainWindow = null!;
    private bool _busy;
    private bool _inHistory;
    private FContextMenu _modeMenu = null!;
    /// <summary>Permission mode picked before the first message of a new chat; null = the settings default.</summary>
    private AiPermissionMode? _nextChatMode;

    public event Action? CloseRequested;

    public AiChatWindow()
    {
        InitializeComponent();
        ApplyTheme();
        // settings may have changed (provider, model) while the panel was closed
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible && _aiHandler is not null) UpdateUsage();
        });
    }

    public Task Initialize(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _aiHandler = new AiHandler(mainWindow);

        _modeMenu = new FContextMenu { UseSolidBackground = true, CurrentTheme = CurrentTheme };
        Grid.SetRowSpan(_modeMenu, 4);
        Grid.SetColumnSpan(_modeMenu, 3);
        Root.Children.Add(_modeMenu);

        LoadChatMessages(null);
        return Task.CompletedTask;
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
        ButtonBraveMode.CurrentTheme = CurrentTheme;
        ButtonSend.CurrentTheme = CurrentTheme;
        ButtonHistory.CurrentTheme = CurrentTheme;
        ButtonClose.CurrentTheme = CurrentTheme with
        {
            SecondaryForegroundColor = CurrentTheme.NoColor,
            PrimaryHighlightColor = CurrentTheme.NoColor
        };
        ButtonNewChat.CurrentTheme = CurrentTheme;
        if (_modeMenu is not null) _modeMenu.CurrentTheme = CurrentTheme;
    }

    private void LoadChatMessages(AiChat? chat)
    {
        MessageList.Children.Clear();
        TitleText.Text = chat is null ? "New Chat" : $"Chat #{chat.Id}";

        if (chat is not null)
            foreach (var entry in chat.Transcript)
                MessageList.Children.Add(GetMessageBubble(entry.Role, entry.Text).Item1);

        MText.Visibility = chat is null || chat.Transcript.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetInputVisible(true);
        UpdateUsage();
        UpdateModeButton();
        ScrollToEnd();
    }

    private void SetInputVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Input.Visibility = visibility;
        ButtonBraveMode.Visibility = visibility;
        ButtonSend.Visibility = visibility;
    }

    private void ScrollToEnd()
    {
        Scroller.UpdateLayout();
        Scroller.ChangeView(null, Scroller.ScrollableHeight, null);
    }

    #region Usage
    /// <summary>Shows the model and what the open chat has used so far; hover for the breakdown.</summary>
    private void UpdateUsage()
    {
        var chat = _aiHandler.ActiveChat;
        var settings = _mainWindow.Instance.Settings;
        var provider = chat?.Provider ?? settings.AiProvider;
        var model = chat?.Model ?? (provider == AiProviderKind.Claude
            ? (string.IsNullOrWhiteSpace(settings.ClaudeModel) ? ClaudeConversation.DefaultModel : settings.ClaudeModel)
            : settings.OpenAiModel);

        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(model)) parts.Add(model.Trim());

        var usage = chat?.Usage ?? AiUsage.Zero;
        if (usage.TotalInputTokens + usage.OutputTokens > 0)
        {
            parts.Add($"{FormatTokens(usage.TotalInputTokens)} in · {FormatTokens(usage.OutputTokens)} out");
            if (usage.CostUsd is { } cost) parts.Add($"≈{FormatCost(cost)}");
        }

        UsageText.Text = string.Join(" · ", parts);
        ToolTipService.SetToolTip(UsageText, UsageToolTip(usage));
        BillingText.Visibility = provider == AiProviderKind.Claude ? Visibility.Visible : Visibility.Collapsed;
    }

    private string UsageToolTip(AiUsage chatUsage)
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

    private void ButtonHistory_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        if (_inHistory)
        {
            LoadChatMessages(_aiHandler.ActiveChat);
            ButtonNewChat.Visibility = Visibility.Collapsed;
            _inHistory = false;
        }
        else
        {
            var history = _aiHandler.Chats.Values.OrderBy(c => c.CreationTime);
            MessageList.Children.Clear();

            foreach (var chat in history)
            {
                var chatCard = new Grid()
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

                var buttonRemove = new FIconButton()
                {
                    Content = new MaterialIcon() {Kind = MaterialIconKind.Delete},
                    CurrentTheme = CurrentTheme,
                    Width = 26,
                    Height = 26,
                };
                buttonRemove.OnClick += (_, _) =>
                {
                    _aiHandler.DeleteChat(chat.Id);
                    MessageList.Children.Remove(chatCard);
                };

                var title = new TextBlock()
                {
                    Text = $"Chat #{chat.Id}",
                    FontSize = 20,
                    Height = 26,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
                    Margin = new Thickness(5, 5, 5, 0),
                };

                var date = new TextBlock()
                {
                    Text = $"{chat.CreationTime:dd/MM/yyyy HH:mm:ss} · {chat.Model}",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor),
                    Margin = new Thickness(5, 0, 5, 5),
                };

                chatCard.Children.Add(title);
                chatCard.Children.Add(buttonRemove);
                chatCard.Children.Add(date);

                Grid.SetColumn(title, 0);
                Grid.SetRow(title, 0);

                Grid.SetColumn(buttonRemove, 1);
                Grid.SetRow(buttonRemove, 0);

                Grid.SetColumn(date, 0);
                Grid.SetRow(date, 1);

                chatCard.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

                    if (buttonRemove.PointerOver) return;
                    _aiHandler.SwapChat(chat.Id);
                    LoadChatMessages(chat);

                    _inHistory = false;
                    ButtonNewChat.Visibility = Visibility.Collapsed;
                };

                MessageList.Children.Add(chatCard);
            }

            MText.Visibility = Visibility.Collapsed;
            ButtonNewChat.Visibility = Visibility.Visible;
            SetInputVisible(false);
            _inHistory = true;
        }
    }

    internal void ButtonClose_OnClick(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        CloseRequested?.Invoke();
    }

    #region Permission mode
    private AiPermissionMode CurrentMode => _aiHandler.ActiveChat?.PermissionMode ?? _nextChatMode ?? _mainWindow.Instance.Settings.AiPermissionMode;

    private void UpdateModeButton()
    {
        var mode = CurrentMode;
        ButtonBraveMode.Content = new MaterialIcon { Kind = AiPermissions.Icon(mode) };
        ToolTipService.SetToolTip(ButtonBraveMode, $"Permissions for this chat: {ModeName(mode)}\n{AiPermissions.Explain(mode)}");
    }

    private static string ModeName(AiPermissionMode mode) => mode == AiPermissionMode.ReadOnly ? "Read-only" : mode.ToString();

    /// <summary>The lightbulb: pick what the assistant may do without asking, for this chat.</summary>
    private void ButtonBraveMode_OnClick(object sender, RoutedEventArgs e)
    {
        var current = CurrentMode;
        _modeMenu.SetItems(Enum.GetValues<AiPermissionMode>().Select(mode =>
            new FContextMenu.MenuItem(new MaterialIcon { Kind = AiPermissions.Icon(mode) }, 1, ModeName(mode), () =>
            {
                if (_aiHandler.ActiveChat is { } chat) chat.PermissionMode = mode;
                else _nextChatMode = mode;
                UpdateModeButton();
            })
            {
                IsChecked = mode == current,
            }));

        var position = ButtonBraveMode.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        _modeMenu.PlaceWithin(position, new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
    }
    #endregion

    #region Approval
    /// <summary>Shows an Allow / Deny card for a tool call and waits for the user's answer.</summary>
    private Task<bool> ShowApprovalCard(AiApprovalRequest request)
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
        Input_OnEnterPressed();
    }

    private async void Input_OnEnterPressed()
    {
        if (_busy) return;

        // CurrentText, not Text: FTextInput.Text isn't updated while typing
        var input = Input.CurrentText.Trim();
        if (input.Length == 0) return;
        Input.SetText("");

        var chat = _aiHandler.ActiveChat ?? _aiHandler.NewChat(_nextChatMode);
        _nextChatMode = null;
        TitleText.Text = $"Chat #{chat.Id}";
        MText.Visibility = Visibility.Collapsed;

        MessageList.Children.Add(GetMessageBubble(AiRole.User, input).Item1);
        var (aiBubble, aiText) = GetMessageBubble(AiRole.Assistant, "");
        MessageList.Children.Add(aiBubble);
        ScrollToEnd();

        _busy = true;
        SetInputVisible(false);
        ButtonBraveMode.Visibility = Visibility.Visible; // the mode can change mid-reply; it applies from the next tool call
        try
        {
            // stays on the UI thread: the providers' awaits resume here, so callbacks and tools can touch the UI
            await chat.UserRequest(input, new AiTurnCallbacks
            {
                Text = text => aiText.Text += text,
                Usage = _ => UpdateUsage(),
                Approve = async request =>
                {
                    var allowed = await ShowApprovalCard(request);
                    // the rest of the reply goes below the card
                    if (aiText.Text.Length == 0) MessageList.Children.Remove(aiBubble);
                    (aiBubble, aiText) = GetMessageBubble(AiRole.Assistant, "");
                    MessageList.Children.Add(aiBubble);
                    ScrollToEnd();
                    return allowed;
                },
            });
            if (aiText.Text.Length == 0) MessageList.Children.Remove(aiBubble);
        }
        catch (Exception e)
        {
            if (e is not AiNotConfiguredException) FoxyLogger.AddError(e);

            // the turn was rolled back, so hand the message back for a retry
            Input.SetText(input);
            aiText.Text = e is AiNotConfiguredException ? e.Message : $"**Error:** {e.Message}";
            aiBubble.Background = new SolidColorBrush(CurrentTheme.NoColor);
            if (e is AiNotConfiguredException)
                MessageList.Children.Add(OpenSettingsButton());
        }
        finally
        {
            _busy = false;
            SetInputVisible(true);
            UpdateUsage();
            ScrollToEnd();
        }
    }

    private FTextButton OpenSettingsButton()
    {
        var button = new FTextButton
        {
            ButtonText = "Open settings",
            Icon = new MaterialIcon { Kind = MaterialIconKind.Cogs },
            CornerRadius = new CornerRadius(5),
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5, 5, 5, 0),
            CurrentTheme = CurrentTheme,
        };
        button.OnClick += (_, _) => _mainWindow.TabManager.SwapActiveTabTo(-2);
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
            // CodeStyling = [
            //     new Style("") { },
            // ],
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
        LoadChatMessages(_aiHandler.NewChat());
        
        _inHistory = false;
        ButtonNewChat.Visibility = Visibility.Collapsed;
    }
}
