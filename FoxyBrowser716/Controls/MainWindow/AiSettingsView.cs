using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.Controls.SettingsPage.SettingsCustomControls;
using FoxyBrowser716.DataObjects.Complex;
using FoxyBrowser716.DataObjects.Complex.Ai;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.Controls.MainWindow;

/// <summary>
/// The assistant's own settings, shown inside the chat panel (gear button): provider keys and server, the Claude
/// budget, default permissions, the system prompt, and tab-group naming. The settings page only has the on/off switch. Keys and the
/// budget are shared by every instance; the rest belong to this instance like any other setting.
/// </summary>
public sealed partial class AiSettingsView : ThemedUserControl
{
	private readonly List<ThemedUserControl> _rows = [];
	private readonly BudgetRow _budget;

	/// <summary>A key or the server changed, so model lists should be fetched again.</summary>
	public event Action? ConnectionChanged;

	public AiSettingsView(MainWindow window)
	{
		var settings = window.Instance.Settings;
		var panel = new StackPanel { Spacing = 14, Margin = new Thickness(8, 4, 8, 12) };

		void Add(ThemedUserControl row)
		{
			_rows.Add(row);
			panel.Children.Add(row);
		}

		Add(new HeaderSettingControl(new HeaderSetting("Claude")));
		Add(new SecretSettingControl(new SecretSetting("API key",
			"A Console API key (starts with sk-ant-api), from platform.claude.com → API keys. Stored in Windows Credential Manager and shared by every instance.",
			AiCredentials.Get(AiProviderKind.Claude) ?? "",
			key =>
			{
				AiCredentials.Set(AiProviderKind.Claude, key);
				ConnectionChanged?.Invoke();
			},
			() => ClaudeConversation.TestKeyAsync())));
		Add(new DecimalSettingControl(new DecimalSetting("Monthly budget ($)",
			"What you expect to spend on Claude per month, such as your plan's API credits. The panel shows the estimated spend against it. Shared by every instance.",
			AiBudget.Cap, AiBudget.SetCap, 0m)));
		Add(new IntSettingControl(new IntSetting("Budget resets on day",
			"Day of the month a new budget period starts (short months use their last day).",
			AiBudget.ResetDay, AiBudget.SetResetDay, 1, 31)));
		Add(_budget = new BudgetRow());

		Add(new HeaderSettingControl(new HeaderSetting("OpenAI-compatible server")));
		Add(new StringSettingControl(new StringSetting("Base URL",
			"The /v1 address of a server that speaks OpenAI's chat completions API: OpenAI, OpenRouter, Mistral, or a local one such as Ollama (http://localhost:11434/v1) or LM Studio (http://localhost:1234/v1).",
			settings.OpenAiBaseUrl,
			url =>
			{
				settings.OpenAiBaseUrl = url;
				ConnectionChanged?.Invoke();
			})));
		Add(new SecretSettingControl(new SecretSetting("API key",
			"Leave empty for local servers that don't need one. Stored in Windows Credential Manager and shared by every instance.",
			AiCredentials.Get(AiProviderKind.OpenAiCompatible) ?? "",
			key =>
			{
				AiCredentials.Set(AiProviderKind.OpenAiCompatible, key);
				ConnectionChanged?.Invoke();
			},
			() => OpenAiCompatibleConversation.TestAsync(settings))));

		Add(new HeaderSettingControl(new HeaderSetting("Permissions")));
		Add(new ComboSettingControl(new ComboSetting("Default for new chats",
			"What the assistant may do without asking. Read-only: reads on its own, asks before every action. Ask: asks before everything. Brave: never asks. Custom: per tool, below. The mode button at the top changes it for one chat.",
			(int)settings.AiPermissionMode,
			id => settings.AiPermissionMode = (AiPermissionMode)id,
			Enum.GetValues<AiPermissionMode>().Select(m => (AiPermissions.Name(m), (int)m)).ToArray())));
		var permissions = new AiToolPermissionsController(window);
		Add(new CustomControlSettingControl(new CustomControlSetting("Custom permissions",
			"Used when a chat is in Custom mode. Tools not changed here behave like Read-only.", _ => permissions), permissions));

		Add(new HeaderSettingControl(new HeaderSetting("Chat")));
		Add(new StringSettingControl(new StringSetting("System prompt",
			"The instructions every new chat starts with. Changes apply to chats started afterwards; leave it empty to use the default.",
			AiChat.SystemPrompt(settings),
			prompt => settings.AiSystemPrompt = AiChat.ToStoredSystemPrompt(prompt),
			multiline: true,
			resetValue: AiChat.DefaultSystemPrompt)));

		Add(new HeaderSettingControl(new HeaderSetting("Tab groups")));
		Add(new BoolSettingControl(new BoolSetting("Name tab groups",
			"Name new tab groups from their tabs with the quick model below, while they still have the default \"Group N\" name. Each name is one small request.",
			settings.AiNameTabGroups, on => settings.AiNameTabGroups = on)));
		Add(new ComboSettingControl(new ComboSetting("Quick model provider", "Where the quick model runs.",
			(int)settings.AiQuickModel.Provider,
			id => settings.AiQuickModel = settings.AiQuickModel with { Provider = (AiProviderKind)id },
			("Claude", (int)AiProviderKind.Claude), ("OpenAI-compatible", (int)AiProviderKind.OpenAiCompatible))));
		Add(new StringSettingControl(new StringSetting("Quick model",
			$"A small, cheap model id, such as {ClaudeConversation.QuickModel}.",
			settings.AiQuickModel.Model,
			model => settings.AiQuickModel = settings.AiQuickModel with { Model = model.Trim() })));

		Content = panel;
		AiBudget.Changed += _budget.Update;
		Unloaded += (_, _) => AiBudget.Changed -= _budget.Update;
		ApplyTheme();
	}

	protected override void ApplyTheme()
	{
		if (_rows is null) return; // base constructor runs before ours
		foreach (var row in _rows) row.CurrentTheme = CurrentTheme;
	}

	/// <summary>This period's estimated spend, with a button to clear it.</summary>
	private sealed partial class BudgetRow : BaseSettingControl
	{
		private readonly FTextButton _clear;

		public BudgetRow() : base("This period", "", DefaultThemes.DarkMode)
		{
			_clear = new FTextButton { ButtonText = "Clear", Height = 28, CornerRadius = new CornerRadius(5) };
			_clear.OnClick += (_, _) => AiBudget.ClearPeriod();
			AddControlToRight(_clear);
			Update();
		}

		public void Update()
		{
			var (spent, cap, start, next) = AiBudget.Current();
			DescriptionBlock.Text = $"≈${spent:0.00} of ${cap:0.##} since {start:MMM d}; resets {next:MMM d}. " +
				"Estimated from list prices for this browser's Claude use only. Clear it to count from now.";
		}

		protected override void ApplyTheme()
		{
			base.ApplyTheme();
			if (_clear is not null) _clear.CurrentTheme = CurrentTheme;
		}
	}
}
