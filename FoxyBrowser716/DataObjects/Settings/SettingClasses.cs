
using FoxyBrowser716.Controls.MainWindow;

namespace FoxyBrowser716.DataObjects.Settings;

// formatting
public class DividerSetting() : ISetting { public string Name => "Divider"; }

public class HeaderSetting(string name) : ISetting { public string Name => name; }
public class SubheadingSetting(string name) : ISetting { public string Name => name; }

// value types
public class BoolSetting(string name, string description, bool defaultValue, Action<bool> onValueChanged, string trueText = "On", string falseText = "Off") : Setting<bool>(name, description, defaultValue, onValueChanged)
{
    public string TrueText { get; } = trueText;
    public string FalseText { get; } = falseText;
}
public class IntSetting(string name, string description, int defaultValue, Action<int> onValueChanged, int? minValue = null, int? maxValue = null) : Setting<int>(name, description, defaultValue, onValueChanged)
{
    public int? MinValue { get; } = minValue;
    public int? MaxValue { get; } = maxValue;
}
public class DecimalSetting(string name, string description, decimal defaultValue, Action<decimal> onValueChanged, decimal? minValue = null, decimal? maxValue = null) : Setting<decimal>(name, description, defaultValue, onValueChanged)
{
    public decimal? MinValue { get; } = minValue;
    public decimal? MaxValue { get; } = maxValue;
}
public class StringSetting(string name, string description, string defaultValue, Action<string> onValueChanged, bool multiline = false) : Setting<string>(name, description, defaultValue, onValueChanged)
{
    public bool Multiline { get; } = multiline;
}
/// <summary>A password box; the value is never shown. Used for API keys.</summary>
public class SecretSetting(string name, string description, string defaultValue, Action<string> onValueChanged) : Setting<string>(name, description, defaultValue, onValueChanged);

// complex types
public class ComboSetting(string name, string description, int defaultValue, Action<int> onValueChanged, params (string name, int id)[] options) : Setting<int>(name, description, defaultValue, onValueChanged)
{
    public (string name, int id)[] Options { get; } = options;
}
public class ColorSetting(string name, string description, Color defaultValue, Action<Color> onValueChanged) : Setting<Color>(name, description, defaultValue, onValueChanged);
public class FilePickerSetting(string name, string description, string defaultValue, Action<string> onValueChanged, bool supportUrls = false, params string[] fileTypes) : Setting<string>(name, description, defaultValue, onValueChanged)
{
    public bool SupportUrls { get; } = supportUrls;
    public string[] FileTypes { get; } = fileTypes;
}
public class FolderPickerSetting(string name, string description, string defaultValue, Action<string> onValueChanged) : Setting<string>(name, description, defaultValue, onValueChanged);
public class CustomControlSetting(string name, string description, Func<MainWindow, ThemedUserControl?> controlFactory) : ISetting 
{ 
    public string Name => name; 
    public string Description => description; 
    public Func<MainWindow, ThemedUserControl> ControlFactory => controlFactory; 
}

public class ButtonSetting(string name, string description, params (string label, Action action)[] buttons) : ISetting
{
    public string Name => name;
    public string Description => description;
    public (string label, Action action)[] Buttons { get; } = buttons;
}

public class SliderSetting(string name, string description, double defaultValue, Action<double> onValueChanged, double minValue, double maxValue, double stepSize = 1.0) : Setting<double>(name, description, defaultValue, onValueChanged)
{
    public double MinValue { get; } = minValue;
    public double MaxValue { get; } = maxValue;
    public double StepSize { get; } = stepSize;
}
