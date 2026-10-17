using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using FoxyBrowser716.Controls.Generic;
using FoxyBrowser716.DataObjects.Basic;
using FoxyBrowser716.DataObjects.Complex;
using Material.Icons;
using Material.Icons.WinUI3;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FoxyBrowser716.Controls.MainWindow;

[ObservableObject]
public sealed partial class NewTabCard : UserControl
{
	[ObservableProperty] public partial bool ForceHighlight { get; set; }
	[ObservableProperty] public partial string Title { get; set; }
	
	[ObservableProperty] public partial UIElement Icon { get; set; }
	
	[ObservableProperty] public partial Visibility CloseVisible { get; set; } = Visibility.Visible;
	[ObservableProperty] public partial Visibility DuplicateVisible { get; set; } = Visibility.Visible;

	[ObservableProperty] 
	public partial ObservableCollection<FMenuItem> MenuOptions { get; set; } = []; // TODO got to disable context menu when empty
	
	[ObservableProperty] public partial Func<ObservableCollection<FMenuItem>>? MenuItemFactory { get; set; }

	//C set for open tabs (not pins): drives the audio badge and the mute / volume button
	[ObservableProperty] public partial WebviewTab? Tab { get; set; }

	public bool InDrag { get; set; } = false;
	
	public event Action<NewTabCard>? DuplicateRequested;
	public event Action<NewTabCard>? CloseRequested;
	public event Action<NewTabCard>? OnClick;
	
	public event Action<NewTabCard, ManipulationStartedRoutedEventArgs>? OnDragStarted;
	public event Action<NewTabCard, ManipulationDeltaRoutedEventArgs>? OnDragMoved;
	public event Action<NewTabCard, ManipulationCompletedRoutedEventArgs>? OnDragCompleted;
	
	public static readonly DependencyProperty CurrentThemeProperty = DependencyProperty.Register(
		nameof(CurrentTheme),
		typeof(Theme),
		typeof(NewTabCard),
		new PropertyMetadata(DefaultThemes.DarkMode, (d, e) => ((NewTabCard)d).OnCurrentThemeChanged((Theme)e.NewValue)));

	public Theme CurrentTheme
	{
		get => (Theme)GetValue(CurrentThemeProperty);
		set => SetValue(CurrentThemeProperty, value);
	}
	
	private void OnCurrentThemeChanged(Theme newTheme)
	{
		ApplyTheme();
	}
	
	public NewTabCard()
	{
		InitializeComponent();
	}
	
	private SolidColorBrush GetBorderBrush(Theme theme, bool forceHighlight) => new(forceHighlight ? theme.PrimaryHighlightColor : theme.SecondaryBackgroundColor); 

	private void ApplyTheme()
	{
		// Root.BorderBrush = new SolidColorBrush(ForceHighlight ? CurrentTheme.PrimaryHighlightColor : CurrentTheme.SecondaryBackgroundColor);
		Root.Background = new SolidColorBrush(_mouseOver ? CurrentTheme.PrimaryAccentColorSlightTransparent : CurrentTheme.PrimaryBackgroundColorVeryTransparent);

		if (Icon is FrameworkElement iconElement)
		{
			iconElement.SetValue(ForegroundProperty, new SolidColorBrush(CurrentTheme.PrimaryForegroundColor));
		}

		ButtonClose.CurrentTheme = CurrentTheme with
		{
			SecondaryForegroundColor = CurrentTheme.NoColor,
			PrimaryHighlightColor = CurrentTheme.NoColor
		};
		ButtonDuplicate.CurrentTheme = CurrentTheme;
		ButtonAudio.CurrentTheme = CurrentTheme;
	}

	partial void OnTabChanged(WebviewTab? oldValue, WebviewTab? newValue)
	{
		if (oldValue is not null) oldValue.PropertyChanged -= Tab_OnPropertyChanged;
		if (newValue is not null) newValue.PropertyChanged += Tab_OnPropertyChanged;
		UpdateAudio();
	}

	private void Tab_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(WebviewTab.IsPlayingAudio) or nameof(WebviewTab.IsMuted) or nameof(WebviewTab.Volume))
			UpdateAudio();
	}

	private void UpdateAudio()
	{
		var show = Tab is { } t && (t.IsPlayingAudio || t.IsMuted);
		var kind = Tab is not { } tab ? MaterialIconKind.VolumeHigh
			: tab.IsMuted || tab.Volume <= 0 ? MaterialIconKind.VolumeOff
			: tab.Volume < 0.5 ? MaterialIconKind.VolumeMedium
			: MaterialIconKind.VolumeHigh;
		AudioBadge.Visibility = ButtonAudio.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		AudioBadgeIcon.Kind = kind;
		ButtonAudio.Content = new MaterialIcon { Kind = kind };
	}

	private void ButtonAudio_OnOnClick(object sender, RoutedEventArgs e) => Tab?.ToggleMute();

	private void ButtonAudio_OnPointerEntered(object sender, PointerRoutedEventArgs e)
	{
		if (Tab is not { } tab) return;
		var slider = new Slider
		{
			Minimum = 0, Maximum = 100, StepFrequency = 1, Width = 120, Value = Math.Round(tab.Volume * 100),
			Margin = new Thickness(10, 0, 10, 0),
		};
		var accent = new SolidColorBrush(CurrentTheme.PrimaryAccentColor);
		var track = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColor);
		foreach (var key in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
			         "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed" })
			slider.Resources[key] = accent;
		foreach (var key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" })
			slider.Resources[key] = track;
		slider.ValueChanged += async (_, args) => await tab.SetVolume(args.NewValue / 100);

		var label = new TextBlock
		{
			Width = 32, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right,
			Text = $"{slider.Value:0}%", Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor),
		};
		slider.ValueChanged += (_, args) => label.Text = $"{args.NewValue:0}%";

		var flyout = new Flyout
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal, Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(8),
				BorderThickness = new Thickness(2),
				BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColor),
				Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent),
				Children = { label, slider },
			},
			FlyoutPresenterStyle = ((Flyout)Root.ContextFlyout).FlyoutPresenterStyle,
		};
		if (FindLeftBar() is { } leftBar)
		{
			flyout.Opened += (_, _) => leftBar.HoldOpen();
			flyout.Closed += (_, _) => leftBar.ReleaseOpenHold();
		}
		flyout.ShowAt(ButtonAudio, new FlyoutShowOptions
		{
			ShowMode = FlyoutShowMode.TransientWithDismissOnPointerMoveAway,
			Placement = FlyoutPlacementMode.Right,
		});
	}

	private NewLeftBar? FindLeftBar()
	{
		for (var el = VisualTreeHelper.GetParent(this); el is not null; el = VisualTreeHelper.GetParent(el))
			if (el is NewLeftBar bar) return bar;
		return null;
	}

	private void ButtonClose_OnOnClick(object sender, RoutedEventArgs e)
	{
		CloseRequested?.Invoke(this);
	}

	private void ButtonDuplicate_OnOnClick(object sender, RoutedEventArgs e)
	{
		DuplicateRequested?.Invoke(this);
	}
	
	#region ColorAnimators
	private bool _mouseOver;
	private void Root_OnPointerEntered(object sender, PointerRoutedEventArgs e)
	{
		_mouseOver = true;
		ChangeColorAnimation(Root.Background, CurrentTheme.PrimaryAccentColorSlightTransparent);
	}

	private void Root_OnPointerExited(object sender, PointerRoutedEventArgs e)
	{
		_mouseOver = false;
		ChangeColorAnimation(Root.Background, CurrentTheme.PrimaryBackgroundColorVeryTransparent);
	}
	
	private void Root_OnPointerReleased(object sender, PointerRoutedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;
		if (ButtonDuplicate.PointerOver || ButtonClose.PointerOver || ButtonAudio.PointerOver || InDrag) return;
        
		ChangeColorAnimation(Root.Background, CurrentTheme.PrimaryBackgroundColorVeryTransparent, 0.3);
		OnClick?.Invoke(this);
	}
	
	private void Root_OnPointerPressed(object sender, PointerRoutedEventArgs e)
	{
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
		if (ButtonDuplicate.PointerOver || ButtonClose.PointerOver || ButtonAudio.PointerOver) return;

		ChangeColorAnimation(Root.Background, CurrentTheme.PrimaryHighlightColorSlightTransparent, 0.05);
	}
	#endregion

	private void Root_OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
	{
		if (ButtonDuplicate.PointerOver || ButtonClose.PointerOver || ButtonAudio.PointerOver)
		{
			e.Complete();
			return;
		}
		
		OnDragStarted?.Invoke(this, e);
		e.Handled = true;
	}

	private void Root_OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
	{
		OnDragCompleted?.Invoke(this, e);
		e.Handled = true;
	}

	private void Root_OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
	{
		OnDragMoved?.Invoke(this, e);
		e.Handled = true;
	}

	private void FlyoutBase_OnOpened(object? sender, object e)
	{
		if (MenuItemFactory is not null)
			MenuOptions = MenuItemFactory();
	}
}