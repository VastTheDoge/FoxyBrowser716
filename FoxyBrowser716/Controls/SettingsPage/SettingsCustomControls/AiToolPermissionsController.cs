using FoxyBrowser716.DataObjects.Complex.Ai;
using FoxyBrowser716.DataObjects.Settings;

namespace FoxyBrowser716.Controls.SettingsPage.SettingsCustomControls;

/// <summary>Allow / Ask / Block for each assistant tool, used by the Custom permission mode.</summary>
public sealed partial class AiToolPermissionsController : ThemedUserControl
{
	private readonly List<ThemedUserControl> _rows = [];

	public AiToolPermissionsController(MainWindow.MainWindow mainWindow)
	{
		var settings = mainWindow.Instance.Settings;
		var panel = new StackPanel { Spacing = 8 };

		foreach (var tool in BrowserTools.Create(mainWindow))
		{
			var current = settings.AiToolPermissions.TryGetValue(tool.Name, out var choice) ? choice : AiPermissions.Default(tool);
			var row = new ComboSettingControl(new ComboSetting(tool.Title, KindText(tool.Kind), (int)current,
				id => settings.AiToolPermissions = new Dictionary<string, AiToolPermission>(settings.AiToolPermissions)
				{
					[tool.Name] = (AiToolPermission)id,
				},
				("Allow", (int)AiToolPermission.Allow), ("Ask", (int)AiToolPermission.Ask), ("Block", (int)AiToolPermission.Block)));
			_rows.Add(row);
			panel.Children.Add(row);
		}

		Content = panel;
		ApplyTheme();
	}

	private static string KindText(AiToolKind kind) => kind switch
	{
		AiToolKind.Read => "Reads only.",
		AiToolKind.Script => "Runs JavaScript in a page: can do anything you can do on that site.",
		_ => "Changes something: a page, a tab, or where it is.",
	};

	protected override void ApplyTheme()
	{
		if (_rows is null) return; // base constructor runs before ours
		foreach (var row in _rows) row.CurrentTheme = CurrentTheme;
	}
}
