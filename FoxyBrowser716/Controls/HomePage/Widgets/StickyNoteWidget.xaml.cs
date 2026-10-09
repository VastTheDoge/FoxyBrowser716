using FoxyBrowser716.DataObjects.Settings;
using Microsoft.UI.Dispatching;

namespace FoxyBrowser716.Controls.HomePage.Widgets;

[WidgetInfo("Sticky Note Widget", MaterialIconKind.NoteTextOutline, WidgetCategory.Tools)]
public partial class StickyNoteWidget : WidgetBase
{
	// (background, text) per color option; index 0 means "follow the browser theme"
	private static readonly (Color background, Color text)[] Palette =
	[
		(default, default),
		(Color.FromArgb(0xEE, 0xFF, 0xF5, 0x9D), Color.FromArgb(0xFF, 0x3A, 0x30, 0x00)), // yellow
		(Color.FromArgb(0xEE, 0xF8, 0xBB, 0xD0), Color.FromArgb(0xFF, 0x3A, 0x0F, 0x1F)), // pink
		(Color.FromArgb(0xEE, 0xB3, 0xE5, 0xFC), Color.FromArgb(0xFF, 0x0A, 0x2A, 0x3A)), // blue
		(Color.FromArgb(0xEE, 0xC8, 0xE6, 0xC9), Color.FromArgb(0xFF, 0x12, 0x30, 0x1A)), // green
	];

	private readonly StringSetting _noteSetting;
	private readonly DispatcherQueueTimer _saveDebounce;
	private int _colorIndex = 1;

	protected StickyNoteWidget()
	{
		InitializeComponent();

		_saveDebounce = DispatcherQueue.CreateTimer();
		_saveDebounce.Interval = TimeSpan.FromSeconds(1);
		_saveDebounce.IsRepeating = false;
		_saveDebounce.Tick += (_, _) => RequestSave();

		_noteSetting = new StringSetting("Note", "The note's text (you can also just type on the widget)", "", v =>
		{
			if (NoteBox.Text != v) NoteBox.Text = v;
		}, multiline: true);

		WidgetSettings =
		[
			_noteSetting,
			new IntSetting("Font Size", "", 16, v => NoteBox.FontSize = v, 8, 72),
			new ComboSetting("Color", "", 1, v => { _colorIndex = v; ApplyTheme(); },
				("Theme", 0), ("Yellow", 1), ("Pink", 2), ("Blue", 3), ("Green", 4)),
		];
		NoteBox.FontSize = 16;
	}

	protected override Task Initialize()
	{
		NoteBox.Text = _noteSetting.Value;
		Unloaded += (_, _) => FlushSave();
		ApplyTheme();
		return Task.CompletedTask;
	}

	private void NoteBox_OnTextChanged(object sender, TextChangedEventArgs e)
	{
		if (_noteSetting.Value == NoteBox.Text) return;
		_noteSetting.Value = NoteBox.Text;
		// typing happens outside edit mode, so save on our own once the user pauses
		_saveDebounce.Stop();
		_saveDebounce.Start();
	}

	private void NoteBox_OnLostFocus(object sender, RoutedEventArgs e) => FlushSave();

	private void FlushSave()
	{
		if (!_saveDebounce.IsRunning) return;
		_saveDebounce.Stop();
		RequestSave();
	}

	protected override void ApplyTheme()
	{
		var index = Math.Clamp(_colorIndex, 0, Palette.Length - 1);
		if (index == 0)
		{
			ApplyCardTheme(Card);
			NoteBox.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
			NoteBox.PlaceholderForeground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
			NoteBox.SelectionHighlightColor = new SolidColorBrush(CurrentTheme.SecondaryHighlightColor);
		}
		else
		{
			var (background, text) = Palette[index];
			Card.Background = new SolidColorBrush(background);
			Card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, text.R, text.G, text.B));
			NoteBox.Foreground = new SolidColorBrush(text);
			NoteBox.PlaceholderForeground = new SolidColorBrush(Color.FromArgb(0x99, text.R, text.G, text.B));
			NoteBox.SelectionHighlightColor = new SolidColorBrush(Color.FromArgb(0x66, text.R, text.G, text.B));
		}
	}
}
