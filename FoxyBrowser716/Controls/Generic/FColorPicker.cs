using Microsoft.UI.Xaml.Shapes;

namespace FoxyBrowser716.Controls.Generic;

// Claude Doc
/// <summary>
/// A themed color picker: a saturation/value square and a hue bar to drag on, with hex and RGB boxes under
/// them. Opaque colors only. <see cref="ColorChanged"/> fires for what the user does, not for setting <see cref="Color"/>.
/// </summary>
public sealed partial class FColorPicker : UserControl
{
	private const double SquareWidth = 190;
	private const double SquareHeight = 140;
	private const double HueWidth = 18;
	private const double ThumbRadius = 7;

	private readonly Border _squareBorder;
	private readonly Border _hueBorder;
	private readonly SolidColorBrush _hueFill = new(Colors.Red);
	private readonly Ellipse _squareThumb;
	private readonly Ellipse _squareRing;
	private readonly Border _hueThumb;
	private readonly Border _swatch;
	private readonly FTextInput _hex;
	private readonly FRGBInput _rgb;

	//C h in degrees, s and v in 0..1; kept apart from the color so a grey keeps its hue while dragging
	private double _h, _s = 1, _v = 1;
	private bool _syncing;
	private bool _draggingSquare, _draggingHue;

	public event Action<Color>? ColorChanged;

	public Color Color
	{
		get => FromHsv(_h, _s, _v);
		set => SetColor(value, keepHue: true, raise: false, from: null);
	}

	public Theme CurrentTheme
	{
		get => field;
		set
		{
			field = value;
			ApplyTheme();
		}
	} = DefaultThemes.DarkMode;

	public FColorPicker()
	{
		var squareGrid = new Grid { Width = SquareWidth, Height = SquareHeight, Background = new SolidColorBrush(Colors.Transparent) };
		_squareBorder = new Border
		{
			CornerRadius = new CornerRadius(6),
			BorderThickness = new Thickness(1),
			Background = _hueFill,
			Child = new Grid
			{
				Children =
				{
					new Rectangle { Fill = Horizontal(Colors.White, Colors.Transparent) },
					new Rectangle { Fill = Vertical(Colors.Transparent, Colors.Black) },
				},
			},
		};
		_squareThumb = new Ellipse
		{
			Width = ThumbRadius * 2, Height = ThumbRadius * 2,
			Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 2,
		};
		//C a black ring just outside the white one keeps the thumb visible on any color
		_squareRing = new Ellipse
		{
			Width = ThumbRadius * 2 + 2, Height = ThumbRadius * 2 + 2,
			Stroke = new SolidColorBrush(Colors.Black), StrokeThickness = 1,
		};
		var squareCanvas = new Canvas { IsHitTestVisible = false, Children = { _squareRing, _squareThumb } };
		squareGrid.Children.Add(_squareBorder);
		squareGrid.Children.Add(squareCanvas);
		squareGrid.PointerPressed += (_, e) =>
		{
			_draggingSquare = true;
			squareGrid.CapturePointer(e.Pointer);
			SetFromSquare(e.GetCurrentPoint(squareGrid).Position);
			e.Handled = true;
		};
		squareGrid.PointerMoved += (_, e) =>
		{
			if (_draggingSquare) SetFromSquare(e.GetCurrentPoint(squareGrid).Position);
		};
		squareGrid.PointerReleased += (_, e) => { _draggingSquare = false; squareGrid.ReleasePointerCapture(e.Pointer); };
		squareGrid.PointerCaptureLost += (_, _) => _draggingSquare = false;

		var hueGrid = new Grid { Width = HueWidth, Height = SquareHeight, Background = new SolidColorBrush(Colors.Transparent) };
		_hueBorder = new Border
		{
			CornerRadius = new CornerRadius(6),
			BorderThickness = new Thickness(1),
			Background = new LinearGradientBrush
			{
				StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
				GradientStops =
				{
					new GradientStop { Color = Colors.Red, Offset = 0 },
					new GradientStop { Color = Colors.Yellow, Offset = 1 / 6.0 },
					new GradientStop { Color = Colors.Lime, Offset = 2 / 6.0 },
					new GradientStop { Color = Colors.Cyan, Offset = 3 / 6.0 },
					new GradientStop { Color = Colors.Blue, Offset = 4 / 6.0 },
					new GradientStop { Color = Colors.Magenta, Offset = 5 / 6.0 },
					new GradientStop { Color = Colors.Red, Offset = 1 },
				},
			},
		};
		_hueThumb = new Border
		{
			Width = HueWidth + 6, Height = 6,
			Background = new SolidColorBrush(Colors.White),
			BorderBrush = new SolidColorBrush(Colors.Black), BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			IsHitTestVisible = false,
		};
		var hueCanvas = new Canvas { IsHitTestVisible = false, Children = { _hueThumb } };
		Canvas.SetLeft(_hueThumb, -3);
		hueGrid.Children.Add(_hueBorder);
		hueGrid.Children.Add(hueCanvas);
		hueGrid.PointerPressed += (_, e) =>
		{
			_draggingHue = true;
			hueGrid.CapturePointer(e.Pointer);
			SetFromHue(e.GetCurrentPoint(hueGrid).Position.Y);
			e.Handled = true;
		};
		hueGrid.PointerMoved += (_, e) =>
		{
			if (_draggingHue) SetFromHue(e.GetCurrentPoint(hueGrid).Position.Y);
		};
		hueGrid.PointerReleased += (_, e) => { _draggingHue = false; hueGrid.ReleasePointerCapture(e.Pointer); };
		hueGrid.PointerCaptureLost += (_, _) => _draggingHue = false;

		_swatch = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2) };
		_hex = new FTextInput { Width = 92, AcceptsReturn = false, PlaceHolderText = "#RRGGBB" };
		_hex.OnTextChanged += text =>
		{
			if (!_syncing && SavedTheme.TryParseHex(text, out var color)) SetColor(color, keepHue: true, raise: true, from: _hex);
		};
		_rgb = new FRGBInput(null, 255, 0, 0) { ShowAlpha = false };
		_rgb.OnValueChanged += color =>
		{
			if (!_syncing) SetColor(color, keepHue: true, raise: true, from: _rgb);
		};

		Content = new StackPanel
		{
			Spacing = 8,
			Children =
			{
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { squareGrid, hueGrid } },
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _swatch, _hex, _rgb } },
			},
		};

		Sync(from: null);
		ApplyTheme();
	}

	private void SetFromSquare(Point p)
	{
		_s = Math.Clamp(p.X / SquareWidth, 0, 1);
		_v = 1 - Math.Clamp(p.Y / SquareHeight, 0, 1);
		Sync(from: null);
		ColorChanged?.Invoke(Color);
	}

	private void SetFromHue(double y)
	{
		_h = Math.Clamp(y / SquareHeight, 0, 1) * 360;
		if (_h >= 360) _h = 0;
		Sync(from: null);
		ColorChanged?.Invoke(Color);
	}

	private void SetColor(Color color, bool keepHue, bool raise, UIElement? from)
	{
		var (h, s, v) = ToHsv(color);
		//C a grey has no hue of its own; keeping the old one stops the hue thumb jumping to red
		if (!(keepHue && s == 0)) _h = h;
		_s = s;
		_v = v;
		Sync(from);
		if (raise) ColorChanged?.Invoke(Color);
	}

	// Claude Doc
	/// <summary>Puts every part of the picker in step with the HSV state, except the box the change came from.</summary>
	private void Sync(UIElement? from)
	{
		_syncing = true;
		try
		{
			var color = Color;
			_hueFill.Color = FromHsv(_h, 1, 1);
			Canvas.SetLeft(_squareThumb, _s * SquareWidth - ThumbRadius);
			Canvas.SetTop(_squareThumb, (1 - _v) * SquareHeight - ThumbRadius);
			Canvas.SetLeft(_squareRing, _s * SquareWidth - ThumbRadius - 1);
			Canvas.SetTop(_squareRing, (1 - _v) * SquareHeight - ThumbRadius - 1);
			Canvas.SetTop(_hueThumb, _h / 360 * SquareHeight - 3);
			_swatch.Background = new SolidColorBrush(color);
			if (!ReferenceEquals(from, _hex)) _hex.SetText(SavedTheme.Hex(color));
			if (!ReferenceEquals(from, _rgb)) _rgb.SetColor(null, color.R, color.G, color.B);
		}
		finally
		{
			_syncing = false;
		}
	}

	private void ApplyTheme()
	{
		_squareBorder.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryAccentColorSlightTransparent);
		_hueBorder.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryAccentColorSlightTransparent);
		_swatch.BorderBrush = new SolidColorBrush(CurrentTheme.SecondaryAccentColorSlightTransparent);
		_hex.CurrentTheme = CurrentTheme;
		_rgb.CurrentTheme = CurrentTheme;
	}

	private static LinearGradientBrush Horizontal(Color from, Color to) => new()
	{
		StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
		GradientStops = { new GradientStop { Color = from, Offset = 0 }, new GradientStop { Color = to, Offset = 1 } },
	};

	private static LinearGradientBrush Vertical(Color from, Color to) => new()
	{
		StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
		GradientStops = { new GradientStop { Color = from, Offset = 0 }, new GradientStop { Color = to, Offset = 1 } },
	};

	public static (double h, double s, double v) ToHsv(Color color)
	{
		double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
		var max = Math.Max(r, Math.Max(g, b));
		var min = Math.Min(r, Math.Min(g, b));
		var delta = max - min;
		var h = 0.0;
		if (delta > 0)
		{
			if (max == r) h = 60 * (((g - b) / delta) % 6);
			else if (max == g) h = 60 * ((b - r) / delta + 2);
			else h = 60 * ((r - g) / delta + 4);
			if (h < 0) h += 360;
		}
		return (h, max == 0 ? 0 : delta / max, max);
	}

	public static Color FromHsv(double h, double s, double v)
	{
		var c = v * s;
		var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
		var m = v - c;
		var (r, g, b) = (h % 360) switch
		{
			< 60 => (c, x, 0.0),
			< 120 => (x, c, 0.0),
			< 180 => (0.0, c, x),
			< 240 => (0.0, x, c),
			< 300 => (x, 0.0, c),
			_ => (c, 0.0, x),
		};
		return Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
	}
}
