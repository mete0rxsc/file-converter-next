using CommunityToolkit.Mvvm.ComponentModel;
using FileConverter.Core;
using System.Globalization;

namespace File_Conventer.ViewModels;

public partial class ParameterChoice(string value, string label, Action<string> select) : ObservableObject
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public string GroupName { get; init; } = "";
    [ObservableProperty] private bool isChecked;
    partial void OnIsCheckedChanged(bool value) { if (value) select(Value); }
    public override string ToString() => Label;
}
public partial class PresetParameter : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string[] Options { get; init; } = [];
    public string GroupName { get; } = Guid.NewGuid().ToString("N");
    public ParameterChoice[] Choices { get; private set; } = [];
    public IReadOnlyList<double> Ticks { get; private set; } = [];
    private OutputType output;
    public bool IsFlag => Key is "EnableAudio" or "EnableVideo" or "EnableFFMPEGCustomCommand" or "ImageClampSizePowerOf2";
    public bool IsRadio => Key is "AudioEncodingMode" or "AudioChannelCount" or "ImageRotation" or "VideoRotation";
    public bool IsChoice => Key == "VideoEncodingSpeed";
    public bool IsSlider => Key is "AudioBitrate" or "ImageQuality" or "VideoQuality" or "ImageScale" or "VideoScale" or "VideoFramesPerSecond";
    public bool IsNumber => !IsFlag && !IsChoice && !IsRadio && !IsSlider && Key != "FFMPEGCustomCommand";
    public bool IsText => !IsFlag && !IsChoice && !IsRadio && !IsSlider && !IsNumber;
    public bool IsMultiline => Key == "FFMPEGCustomCommand";
    [ObservableProperty] private bool isVisible = true;
    [ObservableProperty] private bool isEnabled = true;
    public decimal Minimum => (decimal)(Ticks.Count > 0 ? Ticks.Min() : Key is "ImageScale" or "VideoScale" ? .075 : Key == "VideoFramesPerSecond" ? 1 : 0);
    public decimal Maximum => (decimal)(Ticks.Count > 0 ? Ticks.Max() : Key == "VideoQuality" ? output switch { OutputType.Avi => 30, OutputType.Ogv => 10, OutputType.Webm => 63, _ => 51 }
        : Key == "ImageScale" ? 16 : Key == "VideoScale" ? 2 : Key == "VideoFramesPerSecond" ? 30 : Key.Contains("Quality") ? 100 : 100000);
    public decimal Increment => Key.Contains("Scale") ? .075m : Key == "ImageQuality" ? 5 : 1;
    public bool FlagValue { get => bool.TryParse(Value, out var v) && v; set => Value = value.ToString(); }
    public decimal? NumberValue { get => decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null; set { if (value.HasValue) Value = value.Value.ToString(CultureInfo.InvariantCulture); } }
    public double SliderValue
    {
        get => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        set { var next = Ticks.Count > 0 ? Ticks.MinBy(t => Math.Abs(t - value)) : Math.Round(value / (double)Increment) * (double)Increment; Value = next.ToString("0.####", CultureInfo.InvariantCulture); }
    }
    public string DisplayValue => Key.Contains("Scale") ? (SliderValue * 100).ToString("0.#", CultureInfo.CurrentCulture) + "%"
        : SliderValue.ToString("0.#", CultureInfo.CurrentCulture) + (Key == "AudioBitrate" ? " kbps" : "");
    public ParameterChoice? SelectedChoice { get => Choices.FirstOrDefault(c => c.Value == Value); set { if (value is not null) Value = value.Value; } }
    [ObservableProperty] private string value = "";
    partial void OnValueChanged(string value)
    {
        foreach (var choice in Choices) choice.IsChecked = choice.Value == value;
        foreach (var name in new[] { nameof(FlagValue), nameof(NumberValue), nameof(SliderValue), nameof(DisplayValue), nameof(SelectedChoice) }) OnPropertyChanged(name);
    }
    public void Configure(Localization language, OutputType output, string audioMode)
    {
        this.output = output;
        IEnumerable<(string Value, string Label)> choices = Key switch {
            "AudioChannelCount" => new[] { ("0", language["SameChannelCountOption"]), ("1", language["MonoOption"]), ("2", language["StereoOption"]) },
            "ImageRotation" or "VideoRotation" => new[] { ("0", language["ZeroDegreesRotationTitle"]), ("90", "90°"), ("180", "180°"), ("270", "270°") },
            "AudioEncodingMode" when output == OutputType.Wav => new[] { ("Wav8", "8-bits"), ("Wav16", "16-bits"), ("Wav24", "24-bits"), ("Wav32", "32-bits") },
            "AudioEncodingMode" => new[] { ("Mp3VBR", "VBR"), ("Mp3CBR", "CBR") },
            "VideoEncodingSpeed" => Options.Select(v => (v, language["VideoEncodingSpeed" + v + "Name"])),
            _ => []
        };
        Choices = choices.Select(c => new ParameterChoice(c.Value, c.Label, v => Value = v) { GroupName = GroupName, IsChecked = c.Value == Value }).ToArray();
        SetAudioMode(audioMode);
    }
    public void SetAudioMode(string audioMode)
    {
        Ticks = Key switch {
            "ImageScale" => new double[] { .075, .125, .25, .5, .75, 1, 1.25, 1.5, 1.75, 2, 3, 4, 6, 8, 12, 16 },
            "VideoScale" => new double[] { .075, .125, .25, .5, .75, 1, 1.25, 1.5, 1.75, 2 },
            "AudioBitrate" when audioMode == "Mp3VBR" => new double[] { 65, 85, 100, 115, 130, 165, 175, 190, 225, 245 },
            "AudioBitrate" when audioMode == "Mp3CBR" || output == OutputType.Avi => new double[] { 8, 16, 24, 32, 40, 48, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 },
            "AudioBitrate" when output is OutputType.Ogg or OutputType.Ogv or OutputType.Webm => new double[] { 32, 48, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 500 },
            "AudioBitrate" => new double[] { 16, 32, 48, 64, 80, 96, 112, 128, 155, 192, 224, 256, 340, 460 },
            _ => []
        };
        OnPropertyChanged(nameof(Ticks)); OnPropertyChanged(nameof(Minimum)); OnPropertyChanged(nameof(Maximum));
    }
}
