using FoxyBrowser716.DataManagement;

namespace FoxyBrowser716.DataObjects.Basic;

public class Extension
{
	public string FolderPath { get; init; }
	public bool IsEnabled { get; set; } = true;
	public string Id { get; init; }
	public string WebviewName { get; init; }

	public ExtensionManifestBase Manifest { get; init; }
}