
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.Storage;
using FoxyBrowser716.Controls.Generic;
using Material.Icons;
using Material.Icons.WinUI3;
using WinRT.Interop;
using WinUIEx;
using System.Collections.Generic;
using System;
using FoxyBrowser716.Controls.MainWindow;

namespace FoxyBrowser716.DataObjects.Settings;

public static class SettingsHelper
{
	public static ThemedUserControl GetEditor(this ISetting setting, MainWindow window)
	{
		switch (setting)
		{
			case DividerSetting dividerSetting:
				return new DividerSettingControl(dividerSetting);
			case HeaderSetting headerSetting:
				return new HeaderSettingControl(headerSetting);
			case SubheadingSetting subheadingSetting:
				return new SubheadingSettingControl(subheadingSetting);

			
			case BoolSetting boolSetting:
				return new BoolSettingControl(boolSetting);
			case IntSetting intSetting:
				return new IntSettingControl(intSetting);
			case DecimalSetting decimalSetting:
				return new DecimalSettingControl(decimalSetting);
			case StringSetting stringSetting:
				return new StringSettingControl(stringSetting);
			
			case ComboSetting comboSetting:
				return new ComboSettingControl(comboSetting);
			case ColorSetting colorSetting:
				return new ColorSettingControl(colorSetting);
			case FilePickerSetting filePickerSetting:
				return new FilePickerSettingControl(filePickerSetting, window);
			case FolderPickerSetting folderPickerSetting:
				return new FolderPickerSettingControl(folderPickerSetting, window);
			case ButtonSetting buttonSetting:
				return new ButtonSettingControl(buttonSetting);
			case SliderSetting sliderSetting:
				return new SliderSettingControl(sliderSetting);
			case CustomControlSetting customControlSetting:
				return new CustomControlSettingControl(customControlSetting, customControlSetting.ControlFactory(window));
			default:
				throw new ArgumentOutOfRangeException();
		}
	}
}

// Base class for settings with title and description
public abstract class BaseSettingControl : ThemedUserControl
{
	protected Grid MainGrid;
	protected TextBlock TitleBlock;
	protected TextBlock DescriptionBlock;
	protected StackPanel LeftPanel;

	protected BaseSettingControl(string title, string description, Theme currentTheme)
	{
		InitializeLayout(title, description);
	}

	private void InitializeLayout(string title, string description)
	{
		MainGrid = new Grid
		{
			Margin = new Thickness(0),
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }
			}
		};

		LeftPanel = new StackPanel
		{
			Orientation = Orientation.Vertical,
			VerticalAlignment = VerticalAlignment.Top
		};

		TitleBlock = new TextBlock
		{
			Text = title,
			FontSize = 16,
			FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			Margin = new Thickness(0, 0, 0, 0)
		};

		DescriptionBlock = new TextBlock
		{
			Text = description,
			FontSize = 12,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 0, 0, 0),
			Opacity = 0.8
		};

		LeftPanel.Children.Add(TitleBlock);
		LeftPanel.Children.Add(DescriptionBlock);

		Grid.SetColumn(LeftPanel, 0);
		MainGrid.Children.Add(LeftPanel);

		Content = MainGrid;
	}

	protected void AddControlToRight(FrameworkElement control)
	{
		Grid.SetColumn(control, 1);
		control.VerticalAlignment = VerticalAlignment.Center;
		control.Margin = new Thickness(16, 0, 0, 0);
		MainGrid.Children.Add(control);
	}

	protected void AddControlBelow(FrameworkElement control)
	{
		// If there's not enough space, add below the description
		control.Margin = new Thickness(0, 8, 0, 0);
		LeftPanel.Children.Add(control);
	}

	protected override void ApplyTheme()
	{
		TitleBlock.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
		DescriptionBlock.Foreground = new SolidColorBrush(CurrentTheme.SecondaryForegroundColor);
	}
}

/// <summary>
/// Title and description for a custom settings section (extensions, site permissions...), with the
/// section's own control underneath at full width.
/// </summary>
public class CustomControlSettingControl : BaseSettingControl
{
	public ThemedUserControl Inner { get; }

	public CustomControlSettingControl(CustomControlSetting setting, ThemedUserControl inner) : base(setting.Name, setting.Description, DefaultThemes.DarkMode)
	{
		Inner = inner;
		inner.HorizontalAlignment = HorizontalAlignment.Stretch;
		AddControlBelow(inner);
	}

	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		if (Inner is not null) Inner.CurrentTheme = CurrentTheme;
	}
}

// Formatting controls
public class DividerSettingControl : ThemedUserControl
{
	public DividerSettingControl(DividerSetting setting)
	{
		Content = new Border
		{
			Height = 1,
			Background = new SolidColorBrush(CurrentTheme.SecondaryAccentColor),
			Margin = new Thickness(10, 4, 10, 4)
		};
	}

	protected override void ApplyTheme()
	{
		if (Content is Border border)
			border.Background = new SolidColorBrush(CurrentTheme.SecondaryAccentColor);
	}
}

public class HeaderSettingControl : ThemedUserControl
{
	private TextBlock headerText;

	public HeaderSettingControl(HeaderSetting setting)
	{
		headerText = new TextBlock
		{
			Text = setting.Name,
			FontSize = 20,
			FontWeight = Microsoft.UI.Text.FontWeights.Bold,
			Margin = new Thickness(8, 16, 8, 8)
		};
		Content = headerText;
	}

	protected override void ApplyTheme()
	{
		headerText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
	}
}

public class SubheadingSettingControl : ThemedUserControl
{
	private TextBlock subheadingText;

	public SubheadingSettingControl(SubheadingSetting setting)
	{
		subheadingText = new TextBlock
		{
			Text = setting.Name,
			FontSize = 16,
			FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			Margin = new Thickness(8, 12, 8, 4)
		};
		Content = subheadingText;
	}

	protected override void ApplyTheme()
	{
		subheadingText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
	}
}

// Value type controls
public class BoolSettingControl : BaseSettingControl
{
	private FTextButton trueButton;
	private FTextButton falseButton;
	private readonly BoolSetting setting;

	public BoolSettingControl(BoolSetting boolSetting) : base(boolSetting.Name, boolSetting.Description, DefaultThemes.DarkMode)
	{
		setting = boolSetting;
		InitializeBoolControl();
	}

	private void InitializeBoolControl()
	{
		var buttonPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8
		};

		trueButton = new FTextButton
		{
			ButtonText = setting.TrueText,
			ForceHighlight = setting.Value,
			Margin = new Thickness(2,2,0,2),
			CornerRadius = new CornerRadius(5),
		};

		falseButton = new FTextButton
		{
			ButtonText = setting.FalseText,
			ForceHighlight = !setting.Value,
			Margin = new Thickness(2),
			CornerRadius = new CornerRadius(5),
		};

		trueButton.OnClick += (_, _) =>
		{
			setting.Value = true;
			UpdateButtonStates();
		};

		falseButton.OnClick += (_, _) =>
		{
			setting.Value = false;
			UpdateButtonStates();
		};

		buttonPanel.Children.Add(trueButton);
		buttonPanel.Children.Add(falseButton);

		AddControlToRight(buttonPanel);
	}

	private void UpdateButtonStates()
	{
		trueButton.ForceHighlight = setting.Value;
		falseButton.ForceHighlight = !setting.Value;
	}

	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		trueButton.CurrentTheme = CurrentTheme;
		falseButton.CurrentTheme = CurrentTheme;
	}
}

public class IntSettingControl : BaseSettingControl
{
	private FTextInput numberBox;
	private readonly IntSetting setting;

	public IntSettingControl(IntSetting intSetting) : base(intSetting.Name, intSetting.Description, DefaultThemes.DarkMode)
	{
		setting = intSetting;
		InitializeIntControl();
	}

	private void InitializeIntControl()
	{
		numberBox = new FTextInput
		{
			Width = 200,
		};
		
		numberBox.Text = setting.Value.ToString();

		numberBox.InputMiddleware = (before, after) =>
		{
			if (!int.TryParse(after, out var value)) 
				if (string.IsNullOrEmpty(after)) value = 0; 
				else if (after == "-") { setting.Value = 0; return after; }
				else return before;
			
			if (setting.MinValue is not null && value < setting.MinValue) value = setting.MinValue.Value;
			if (setting.MaxValue is not null && value > setting.MaxValue) value = setting.MaxValue.Value;
			
			setting.Value = value;
			
			return value == 0 ? "" : after;
		};

		AddControlToRight(numberBox);
		
	}

	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		numberBox.CurrentTheme = CurrentTheme;
	}
}

public class DecimalSettingControl : BaseSettingControl
{
	private FTextInput numberBox;
	private readonly DecimalSetting setting;

	public DecimalSettingControl(DecimalSetting decimalSetting) : base(decimalSetting.Name, decimalSetting.Description, DefaultThemes.DarkMode)
	{
		setting = decimalSetting;
		InitializeDecimalControl();
	}

	private void InitializeDecimalControl()
	{
		numberBox = new FTextInput
		{
			Width = 200,
		};
		
		numberBox.Text = setting.Value.ToString();

		numberBox.InputMiddleware = (before, after) =>
		{
			if (!decimal.TryParse(after, out var value)) 
				if (string.IsNullOrEmpty(after)) value = 0; 
				else if (after == "-") { setting.Value = 0; return after; }
				else return before;
			
			if (setting.MinValue is not null && value < setting.MinValue) value = setting.MinValue.Value;
			if (setting.MaxValue is not null && value > setting.MaxValue) value = setting.MaxValue.Value;
			
			setting.Value = value;
			
			return value == 0 ? "" : after;
		};

		AddControlToRight(numberBox);
		
	}

	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		numberBox.CurrentTheme = CurrentTheme;
	}
}

public class StringSettingControl : BaseSettingControl
{
	private FTextInput textBox;
	private readonly StringSetting setting;

	public StringSettingControl(StringSetting stringSetting) : base(stringSetting.Name, stringSetting.Description, DefaultThemes.DarkMode)
	{
		setting = stringSetting;
		InitializeStringControl();
	}

	private void InitializeStringControl()
	{
		textBox = new FTextInput
		{
			MinWidth = 200
		};
		
		textBox.Text = setting.Value;

		textBox.OnTextChanged += (s) =>
		{
			setting.Value = s;
		};

		AddControlToRight(textBox);
	}

	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		
		textBox.CurrentTheme = CurrentTheme;
	}
}

public class ComboSettingControl : BaseSettingControl
{
	private readonly ComboSetting setting;
	private StackPanel optionsPanel;
	private readonly List<FTextButton> optionButtons = new();

	public ComboSettingControl(ComboSetting comboSetting) : base(comboSetting.Name, comboSetting.Description, DefaultThemes.DarkMode)
	{
		setting = comboSetting;
		InitializeComboControl();
	}

	private void InitializeComboControl()
	{
		// a few short options (Ask/Allow/Block, Auto/Light/Dark) read better side by side, like the On/Off pair
		var horizontal = setting.Options.Length <= 4 && setting.Options.All(o => o.name.Length <= 12);

		optionsPanel = new StackPanel
		{
			Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
			Spacing = horizontal ? 2 : 6
		};

		foreach (var (name, id) in setting.Options)
		{
			var btn = new FTextButton
			{
				ButtonText = name,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				MinWidth = horizontal ? 0 : 180,
				CornerRadius = new CornerRadius(5),
				Margin = new Thickness(2),
			};
			btn.OnClick += (_, _) =>
			{
				setting.Value = id;
				UpdateSelection();
			};
			optionButtons.Add(btn);
			optionsPanel.Children.Add(btn);
		}

		UpdateSelection();
		AddControlToRight(optionsPanel);
	}

	private void UpdateSelection()
	{
		for (int i = 0; i < optionButtons.Count; i++)
		{
			var id = setting.Options[i].id;
			optionButtons[i].ForceHighlight = (id == setting.Value);
		}
	}

	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		foreach (var btn in optionButtons)
		{
			btn.CurrentTheme = CurrentTheme;
		}
	}
}

public class ColorSettingControl : BaseSettingControl
{
	private readonly ColorSetting setting;
	private FRGBInput rgbInput;

	public ColorSettingControl(ColorSetting colorSetting) : base(colorSetting.Name, colorSetting.Description, DefaultThemes.DarkMode)
	{
		setting = colorSetting;
		InitializeColorControl();
	}

	private void InitializeColorControl()
	{
		var panel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 12,
			VerticalAlignment = VerticalAlignment.Center
		};

		rgbInput = new FRGBInput(null, setting.Value.R, setting.Value.G, setting.Value.B);
		rgbInput.OnValueChanged += c =>
		{
			setting.Value = c;
		};
		
		// previewEllipse = new Microsoft.UI.Xaml.Shapes.Ellipse
		// {
		// 	Width = 36,
		// 	Height = 36,
		// 	StrokeThickness = 2,
		// 	Fill = new SolidColorBrush(setting.Value)
		// };
		//
		// rBox = CreateChannelBox(setting.Value.R, "R");
		// gBox = CreateChannelBox(setting.Value.G, "G");
		// bBox = CreateChannelBox(setting.Value.B, "B");

		panel.Children.Add(rgbInput);

		AddControlToRight(panel);
	}
	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		rgbInput.CurrentTheme = CurrentTheme;
	}
}

public class FilePickerSettingControl : BaseSettingControl
{
	private FTextInput pathTextBox;
	private FIconButton browseButton;
	private readonly FilePickerSetting setting;

	public FilePickerSettingControl(FilePickerSetting filePickerSetting, WindowEx window) : base(filePickerSetting.Name, filePickerSetting.Description, DefaultThemes.DarkMode)
	{
		setting = filePickerSetting;
		InitializeFilePickerControl(window);
	}

	private void InitializeFilePickerControl(WindowEx window)
	{
		var panel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8
		};

		pathTextBox = new FTextInput()
		{
			MinWidth = 172,
			Margin = new Thickness(2, 2, 0, 2)
		};
		pathTextBox.Text = setting.Value;

		browseButton = new FIconButton
		{
			Content = new MaterialIcon { Kind = MaterialIconKind.Folder},
			Width = 26,
			Height = 26,
			Margin = new Thickness(2)
		};

		pathTextBox.OnTextChanged += (t) =>
		{
			setting.Value = t;
		};
		
		browseButton.OnClick += async (_, _) =>
		{
			var picker = new FileOpenPicker();
			picker.FileTypeFilter.Add("*");
			
			InitializeWithWindow.Initialize(picker, window.GetWindowHandle());
			
			var file = await picker.PickSingleFileAsync();
			if (file != null)
			{
				pathTextBox.Text = file.Path;
				setting.Value = file.Path;
			}
		};

		panel.Children.Add(pathTextBox);
		panel.Children.Add(browseButton);

		AddControlToRight(panel);
	}

 protected override void ApplyTheme()
	{
		base.ApplyTheme();
		if (browseButton != null) browseButton.CurrentTheme = CurrentTheme;
	}
}

public class ButtonSettingControl : BaseSettingControl
{
	private readonly ButtonSetting setting;
	private readonly List<FTextButton> buttons = new();

	public ButtonSettingControl(ButtonSetting buttonSetting) : base(buttonSetting.Name, buttonSetting.Description, DefaultThemes.DarkMode)
	{
		setting = buttonSetting;
		InitializeButtons();
	}

	private void InitializeButtons()
	{
		var panel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8
		};

		foreach (var (label, action) in setting.Buttons)
		{
			var btn = new FTextButton
			{
				ButtonText = label,
				HorizontalAlignment = HorizontalAlignment.Left,
				MinWidth = 100
			};
			btn.OnClick += (_, _) => action?.Invoke();
			buttons.Add(btn);
			panel.Children.Add(btn);
		}

		AddControlToRight(panel);
	}

	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		foreach (var b in buttons) b.CurrentTheme = CurrentTheme;
	}
}

public class SliderSettingControl : BaseSettingControl
{
	private readonly SliderSetting setting;
	private Slider slider;
	private TextBlock valueText;

	//TODO: move to custom control
	public SliderSettingControl(SliderSetting sliderSetting) : base(sliderSetting.Name, sliderSetting.Description, DefaultThemes.DarkMode)
	{
		setting = sliderSetting;
		InitializeSlider();
	}

	private void InitializeSlider()
	{
		var panel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 12,
			VerticalAlignment = VerticalAlignment.Center
		};

		slider = new Slider
		{
			Minimum = setting.MinValue,
			Maximum = setting.MaxValue,
			StepFrequency = Math.Max(0.0001, setting.StepSize),
			Value = setting.Value,
			Width = 200,
			IsThumbToolTipEnabled = true
		};

		valueText = new TextBlock
		{
			Text = setting.Value.ToString("0.##"),
			VerticalAlignment = VerticalAlignment.Center
		};

		slider.ValueChanged += (_, e) =>
		{
			setting.Value = e.NewValue;
			valueText.Text = e.NewValue.ToString("0.##");
		};

		panel.Children.Add(slider);
		panel.Children.Add(valueText);
		AddControlToRight(panel);
	}

	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		valueText.Foreground = new SolidColorBrush(CurrentTheme.PrimaryForegroundColor);
	}
}

public class FolderPickerSettingControl : BaseSettingControl
{
	private FTextInput pathTextBox;
	private FIconButton browseButton;
	private readonly FolderPickerSetting setting;

	public FolderPickerSettingControl(FolderPickerSetting folderPickerSetting, WindowEx window) : base(folderPickerSetting.Name, folderPickerSetting.Description, DefaultThemes.DarkMode)
	{
		setting = folderPickerSetting;
		InitializeFolderPickerControl(window);
	}

	private void InitializeFolderPickerControl(WindowEx window)
	{
		var panel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8
		};

		pathTextBox = new FTextInput()
		{
			MinWidth = 172,
			Margin = new Thickness(2, 2, 0, 2)
		};
		pathTextBox.Text = setting.Value;

		browseButton = new FIconButton
		{
			Content = new MaterialIcon { Kind = MaterialIconKind.Folder},
			Width = 26,
			Height = 26,
			Margin = new Thickness(2)
		};

		pathTextBox.OnTextChanged += (t) =>
		{
			setting.Value = t;
		};

		browseButton.OnClick += async (_, _) =>
		{
			var picker = new FolderPicker();
			
			InitializeWithWindow.Initialize(picker, window.GetWindowHandle());
			
			var folder = await picker.PickSingleFolderAsync();
			if (folder != null)
			{
				pathTextBox.Text = folder.Path;
				setting.Value = folder.Path;
			}
		};

		panel.Children.Add(pathTextBox);
		panel.Children.Add(browseButton);

		AddControlToRight(panel);
	}

	protected override void ApplyTheme()
	{
		base.ApplyTheme();
		browseButton.CurrentTheme = CurrentTheme;
		pathTextBox.CurrentTheme = CurrentTheme;
	}
}