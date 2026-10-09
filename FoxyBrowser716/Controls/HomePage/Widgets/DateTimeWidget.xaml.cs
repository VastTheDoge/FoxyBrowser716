namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Date/Time Widget", MaterialIconKind.Clock, WidgetCategory.TimeDate)]
public partial class DateTimeWidget : WidgetBase
{
	protected DateTimeWidget()
	{
		InitializeComponent();
	}

    protected override async Task Initialize()
    {
	    CreateLiveTimer(TimeSpan.FromMilliseconds(100), Refresh);
	    ApplyTheme();
    }

    protected override void ApplyTheme()
    {
        TimeOfTheBlock.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
        DateOfTheBlock.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
        RootGrid.Background = new SolidColorBrush(CurrentTheme.PrimaryBackgroundColorVeryTransparent);
        RootGrid.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryBackgroundColorSlightTransparent);
    }

    private void Refresh()
    {
	    TimeOfTheBlock.Text = DateTime.Now.ToLongTimeString();
	    DateOfTheBlock.Text = DateTime.Now.ToLongDateString();
    }
}
