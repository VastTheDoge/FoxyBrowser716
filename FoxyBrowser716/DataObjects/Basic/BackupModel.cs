using FoxyBrowser716.Controls.MainWindow;

namespace FoxyBrowser716.DataObjects.Basic;

public record AppBackupModel
{
    public WindowBackupModel[] Windows { get; set; } = [];

    //C false while the browser runs (crash backup, always restored); true once the last window closed normally
    public bool ClosedCleanly { get; set; }
}

public record WindowBackupModel
{
    public string InstanceName { get; set; } = string.Empty;
    
    public int ActiveTabId { get; set; }
    
    public Dictionary<int, string> Tabs { get; set; } = [];
    public TabGroupBackupModel[] TabGroups { get; set; } = [];
    
    public Rect Bounds { get; set; } = new();
    public MainWindow.BrowserWindowState State { get; set; } = MainWindow.BrowserWindowState.Normal;
}

public record TabGroupBackupModel
{
    public string Name { get; set; } = string.Empty;
    public Color GroupColor { get; set; }
    public Dictionary<int, string>  Tabs { get; set; } = [];
}