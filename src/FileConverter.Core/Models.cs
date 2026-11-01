using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileConverter.Core;

public enum OutputType { Aac, Avi, Avif, Flac, Gif, Ico, Jpg, Mkv, Mp3, Mp4, Ogg, Ogv, Pdf, Png, Wav, Webm, Webp }
public enum PostAction { None, MoveInArchiveFolder, Delete }
public enum HardwareMode { Off, CUDA, AMF }
public enum ShellMode { NativeAot, NativeCpp }
public enum JobState { Queued, Running, Done, Failed, Cancelled }

public sealed class Preset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新预设";
    public OutputType OutputType { get; set; } = OutputType.Png;
    public List<string> InputTypes { get; set; } = [];
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
    public string OutputFileNameTemplate { get; set; } = "(p)(f)";
    public string ConversionArchiveFolderName { get; set; } = "Originals";
    public PostAction InputPostConversionAction { get; set; }
    public bool IsDefaultSettings { get; set; }
    public string Get(string key, string fallback = "") => Settings.GetValueOrDefault(key, fallback);
    public double Number(string key, double fallback) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;
    public bool Flag(string key, bool fallback = false) => bool.TryParse(Get(key), out var value) ? value : fallback;
    public Preset Clone()
    {
        var result = JsonSerializer.Deserialize<Preset>(JsonSerializer.Serialize(this, AppJson.Options), AppJson.Options)!;
        result.Id = Guid.NewGuid();
        return result;
    }
    public override string ToString() => Name;
}

public sealed class ShellSettings
{
    public ShellMode Mode { get; set; } = ShellMode.NativeAot;
    public bool DisplayPresetIcons { get; set; }
}
public sealed class UpdateSettings
{
    public string Repository { get; set; } = "";
    public bool CheckAtStartup { get; set; } = true;
}
public sealed class ToolSettings
{
    public string FFmpeg { get; set; } = "";
    public string FFprobe { get; set; } = "";
    public string Ghostscript { get; set; } = "";
}
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public List<Preset> Presets { get; set; } = [];
    public List<string> Folders { get; set; } = [];
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "Default";
    public int MaximumNumberOfSimultaneousConversions { get; set; } = 2;
    public bool ExitApplicationWhenConversionsFinished { get; set; }
    public double DurationBetweenEndOfConversionsAndApplicationExit { get; set; } = 3;
    public bool CopyFilesInClipboardAfterConversion { get; set; }
    public HardwareMode HardwareAccelerationMode { get; set; }
    public ShellSettings Shell { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
    public ToolSettings Tools { get; set; } = new();
}

public static class AppJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static JsonSerializerOptions WireOptions { get; } = new(Options) { WriteIndented = false };
}

public static class AppPaths
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileConverter");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string MenuFile => Path.Combine(DataDirectory, "menu.bin");
    public static string RequestsDirectory => Path.Combine(DataDirectory, "Requests");
    public static string LogsDirectory => Path.Combine(DataDirectory, "Logs");
}

public static class Formats
{
    public static readonly string[] Audio = "aac aiff ape cda flac m4a m4b mp3 oga ogg opus wav wma".Split(' ');
    public static readonly string[] Video = "3gp 3gpp avi bik flv m4v mkv mov mp4 mpg mpeg ogv rm ts vob webm wmv".Split(' ');
    public static readonly string[] Images = "arw avif bmp cr2 dds dng exr heic ico jfif jpg jpeg nef png psd raf svg tga tif tiff webp xcf".Split(' ');
    public static readonly string[] Documents = "pdf doc docx ppt pptx odp ods odt xls xlsx".Split(' ');
    public static readonly string[] Inputs = Audio.Concat(Video).Concat(Images).Concat(Documents).Append("gif").Distinct().Order().ToArray();
    public static bool IsOffice(string extension) => Documents.Contains(extension) && extension != "pdf";
    public static bool IsImage(string extension) => Images.Contains(extension);
    public static bool IsImageOutput(OutputType type) => type is OutputType.Avif or OutputType.Gif or OutputType.Ico or OutputType.Jpg or OutputType.Pdf or OutputType.Png or OutputType.Webp;
    public static bool IsVideoOutput(OutputType type) => type is OutputType.Avi or OutputType.Mkv or OutputType.Mp4 or OutputType.Ogv or OutputType.Webm;
    public static bool CanConvert(string input, OutputType output)
    {
        var known = Inputs.Contains(input);
        if (!known) return true; // Upstream's Misc category supports user-specified extensions.
        if (IsVideoOutput(output)) return Video.Contains(input) || input == "gif";
        if (output == OutputType.Gif) return Images.Contains(input) || Video.Contains(input) || input == "gif";
        if (output == OutputType.Pdf) return Images.Contains(input) || Documents.Contains(input);
        if (IsImageOutput(output)) return Images.Contains(input) || Documents.Contains(input) || input == "gif";
        return Audio.Contains(input) || Video.Contains(input);
    }
}
