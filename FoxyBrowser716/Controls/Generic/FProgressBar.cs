namespace FoxyBrowser716.Controls.Generic;

/// <summary>A thin themed progress bar (highlight fill on an accent track).</summary>
public sealed partial class FProgressBar : UserControl
{
	private readonly Grid _root;
	private readonly Border _fill;
	private readonly Border _track;

	/// <summary>0 to 1. Values outside are clamped.</summary>
	public double Value
	{
		get;
		set
		{
			field = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1);
			_root.ColumnDefinitions[0].Width = new GridLength(field, GridUnitType.Star);
			_root.ColumnDefinitions[1].Width = new GridLength(1 - field, GridUnitType.Star);
		}
	}

	/// <summary>Dims the fill, e.g. while paused.</summary>
	public bool IsMuted
	{
		get;
		set
		{
			field = value;
			ApplyTheme();
		}
	}

	public Theme CurrentTheme
	{
		get;
		set
		{
			field = value;
			ApplyTheme();
		}
	} = DefaultThemes.DarkMode;

	public FProgressBar()
	{
		_fill = new Border { CornerRadius = new CornerRadius(2) };
		_track = new Border { CornerRadius = new CornerRadius(2) };
		_root = new Grid
		{
			Height = 4,
			CornerRadius = new CornerRadius(2),
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
			},
		};
		Grid.SetColumnSpan(_track, 2);
		_root.Children.Add(_track);
		_root.Children.Add(_fill);
		Content = _root;
		ApplyTheme();
	}

	private void ApplyTheme()
	{
		_track.Background = new SolidColorBrush(CurrentTheme.PrimaryAccentColorVeryTransparent);
		_fill.Background = new SolidColorBrush(IsMuted ? CurrentTheme.SecondaryForegroundColorVeryTransparent : CurrentTheme.PrimaryHighlightColor);
	}
}
