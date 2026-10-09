using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.DataManagement;
using FoxyBrowser716.DataObjects.Basic;
using FoxyBrowser716.DataObjects.Complex;
using Material.Icons;
using Material.Icons.WinUI3;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FoxyBrowser716.Controls.MainWindow;

[ObservableObject]
public sealed partial class NewTabGroupCard : UserControl
{
    [ObservableProperty] public partial Visibility MoveTipsVisible { get; set; } = Visibility.Collapsed;
    
    public bool IsCollapsed { get; private set; }

    private FRGBInput? rgbInput;
    
    public TabGroup TabGroup
    {
        get;
        set
        {
            SetProperty(ref field, value);

            if (rgbInput is not null)
            {
                contextStack.Children.Remove(rgbInput);
            }

            rgbInput = new FRGBInput(TabGroup.GroupColor.A, TabGroup.GroupColor.R, TabGroup.GroupColor.G, TabGroup.GroupColor.B)
            {
                CurrentTheme = CurrentTheme,
            };
            rgbInput.OnValueChanged += c =>
            {
                TabGroup.GroupColor = c;
            };
            contextStack.Children.Add(rgbInput);
        }
    }
    
    public NewTabGroupCard()
    {
        InitializeComponent();
    }

    public Theme CurrentTheme
    {
        get;
        set
        {
            SetProperty(ref field, value);
            rgbInput?.CurrentTheme = CurrentTheme;
            ButtonClose.CurrentTheme = CurrentTheme with {SecondaryForegroundColor = CurrentTheme.NoColor, PrimaryHighlightColor = CurrentTheme.NoColor};
        }
    } = DefaultThemes.DarkMode;
    
    private void ButtonCollapse_OnOnClick(object sender, RoutedEventArgs e)
    {
        SetCollapsed(!IsCollapsed);
    }

    private void ListViewBase_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WebviewTab wt)
        {
            TabGroup.TabManager?.SwapActiveTabTo(wt.Id);
        }
    }
    
    public void SetCollapsed(bool isCollapsed)
    {
        IsCollapsed = isCollapsed;
        TabHolder.Visibility = IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        if (ButtonCollapse.Content is MaterialIcon icon)
            icon.Kind = IsCollapsed ? MaterialIconKind.ExpandMore : MaterialIconKind.ExpandLess;
    }
    
    private void TabCard_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // ButtonCollapse.Visibility = e.NewSize.Width > Icon.Width + ButtonCollapse.Width ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TabCard_OnClick(NewTabCard obj)
    {
        if (obj.Tag is WebviewTab wt)
            TabGroup.TabManager.SwapActiveTabTo(wt.Id);
    }

    private void TabCard_OnCloseRequested(NewTabCard obj)
    {
        if (obj.Tag is WebviewTab wt)
            TabGroup.TabManager.RemoveTab(wt.Id);
    }

    private async void TabCard_OnDuplicateRequested(NewTabCard obj)
    {
        if (obj.Tag is WebviewTab wt)
        {
            var id = TabGroup.TabManager.AddTab(wt.Info.Url);
            TabGroup.TabManager.MoveTabToGroup(id,TabGroup.Id);
            TabGroup.TabManager.SwapActiveTabTo(id);
        }
    }

// Group card drag events (for receiving tabs from outside)
    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.TryGetValue("DragType", out var type) && type.ToString() == "Tab" &&
            e.DataView.Properties.TryGetValue("SourceManager", out var smObj) && smObj is TabManager sourceManager)
        {
            if (!TabManager.CanAcceptTabsFrom(TabGroup.TabManager, sourceManager))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = $"Add to {TabGroup.Name}";
            
            // Visual feedback - highlight the group
            Root.Opacity = 0.7;
            e.Handled = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        Root.Opacity = 1.0;
        
        if (e.DataView.Properties.TryGetValue("DragItem", out var tabObj) && tabObj is WebviewTab tab &&
            e.DataView.Properties.TryGetValue("SourceManager", out var smObj) && smObj is TabManager sourceManager)
        {
            if (!TabManager.CanAcceptTabsFrom(TabGroup.TabManager, sourceManager)) return;

            if (sourceManager == TabGroup.TabManager)
            {
                TabGroup.TabManager.MoveTabToGroup(tab.Id, TabGroup.Id);
            }
            else
            {
                TabGroup.TabManager.MoveTabFromWindowToGroup(tab, sourceManager, TabGroup.Id);
            }
            e.Handled = true;
        }
    }

    private void Root_DragEnter(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.TryGetValue("DragType", out var type) && type.ToString() == "Tab" &&
            e.DataView.Properties.TryGetValue("SourceManager", out var smObj) && smObj is TabManager sourceManager)
        {
            if (!TabManager.CanAcceptTabsFrom(TabGroup.TabManager, sourceManager)) return;
            Root.Opacity = 0.7;
            e.Handled = true;
        }
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        Root.Opacity = 1.0;
        e.Handled = true;
    }

    // Tabs within group drag events (for reordering within the group)
    private void TabsList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is WebviewTab tab)
        {
            e.Data.Properties.Add("DragType", "Tab");
            e.Data.Properties.Add("DragItem", tab);
            e.Data.Properties.Add("SourceManager", TabGroup.TabManager);
            e.Data.Properties.Add("SourceGroupId", TabGroup.Id);
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private void TabsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.None)
        {
            if (args.Items.FirstOrDefault() is WebviewTab tab)
            {
                var cursorPosition = new Point(0, 0);
                if (GetCursorPos(out var lpPoint))
                {
                    cursorPosition.X = lpPoint.X;
                    cursorPosition.Y = lpPoint.Y;
                    
                    TabGroup.TabManager.CreateWindowWithTab(tab, new Rect(cursorPosition, new Size(250, 100)));
                }
            }
        }
    }

    private void TabsList_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.TryGetValue("SourceManager", out var smObj) && smObj is TabManager sourceManager)
        {
            if (!TabManager.CanAcceptTabsFrom(TabGroup.TabManager, sourceManager))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        
        if (e.DataView.Properties.ContainsKey("SourceGroupId"))
        {
            e.DragUIOverride.Caption = "Reorder";
        }
        else
        {
            e.DragUIOverride.Caption = $"Move to {TabGroup.Name}";
        }
        e.Handled = true;
    }

    private void TabsList_Drop(object sender, DragEventArgs e)
    {
        var listView = sender as ListView;
        
        if (e.DataView.Properties.TryGetValue("DragItem", out var tabObj) && 
            tabObj is WebviewTab tab &&
            e.DataView.Properties.TryGetValue("SourceManager", out var smObj) &&
            smObj is TabManager sourceManager)
        {
            if (!TabManager.CanAcceptTabsFrom(TabGroup.TabManager, sourceManager)) return;

            var isSameWindow = sourceManager == TabGroup.TabManager;
            var sourceGroupId = e.DataView.Properties.TryGetValue("SourceGroupId", out var idObj) ? (int)idObj : -1;

            // Find the drop position
            var position = e.GetPosition(listView);
            int targetIndex = TabGroup.Tabs.Count; // Default to end
            
            if (listView?.Items != null)
            {
                for (int i = 0; i < listView.Items.Count; i++)
                {
                    var container = listView.ContainerFromIndex(i) as ListViewItem;
                    if (container != null)
                    {
                        var bounds = container.TransformToVisual(listView).TransformBounds(
                            new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                        
                        if (position.Y < bounds.Top + bounds.Height / 2)
                        {
                            targetIndex = i;
                            break;
                        }
                    }
                }
            }
            
            if (isSameWindow)
            {
                // Only move if it's not from this group (ListView handles reorder within same group)
                if (sourceGroupId != TabGroup.Id)
                {
                    TabGroup.TabManager.MoveTabToGroup(tab.Id, TabGroup.Id, targetIndex);
                    e.Handled = true;
                }
            }
            else
            {
                TabGroup.TabManager.MoveTabFromWindowToGroup(tab, sourceManager, TabGroup.Id, targetIndex);
                e.Handled = true;
            }
        }
        // Otherwise, ListView handles reordering automatically
    }

    private void ButtonClose_OnOnClick(object sender, RoutedEventArgs e)
    {
        TabGroup.TabManager.RemoveGroup(TabGroup.Id);
    }

    private void DuplicateButton_OnOnClick(object sender, RoutedEventArgs e)
    {
        TabGroup? newGroup = null;
        
        foreach (var tab in TabGroup.Tabs)
        {
            var newTab = TabGroup.TabManager.AddTab(tab.Info.Url);

            newGroup ??= TabGroup.TabManager.CreateGroup();
            
            TabGroup.TabManager.MoveTabToGroup(newTab, newGroup.Id);
        }
        
        if (newGroup is null) return;
        
        TabGroup.TabManager.SwapActiveTabTo(newGroup.Tabs.First().Id);
        newGroup.Name = $"Copy of {newGroup.Name}";
        newGroup.GroupColor = TabGroup.GroupColor;
    }
}