using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FileConverterNext.Core;

public sealed class SettingsStore(string? path = null, string? previousDataDirectory = null)
{
    public string FilePath { get; } = path ?? AppPaths.SettingsFile;
    private readonly string? previousDirectory = previousDataDirectory ?? (path is null ? AppPaths.PreviousDataDirectory : null);
    public AppSettings Load()
    {
        if (File.Exists(FilePath))
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), AppJson.Options)
                ?? throw new InvalidDataException("设置文件为空。");
            if (settings.SchemaVersion != 1) throw new InvalidDataException("设置文件版本不受支持，请保留文件并升级程序。");
            Validate(settings);
            return settings;
        }
        if (previousDirectory is not null)
        {
            var previousFile = Path.Combine(previousDirectory, "settings.json");
            if (File.Exists(previousFile))
            {
                var original = File.ReadAllBytes(previousFile);
                var migrated = JsonSerializer.Deserialize<AppSettings>(original, AppJson.Options)
                    ?? throw new InvalidDataException("旧设置文件为空，已保留原文件。");
                if (migrated.SchemaVersion != 1) throw new InvalidDataException("旧设置版本不受支持，已保留原文件。");
                Validate(migrated);
                var backup = Path.Combine(Path.GetDirectoryName(FilePath)!, "settings.migration-" + Guid.NewGuid().ToString("N") + ".bak");
                AtomicWrite(backup, original);
                AtomicWrite(FilePath, original);
                return migrated;
            }
        }
        var legacy = Path.Combine(Path.GetDirectoryName(FilePath)!, "Settings.user.xml");
        if (!File.Exists(legacy) && previousDirectory is not null) legacy = Path.Combine(previousDirectory, "Settings.user.xml");
        var result = File.Exists(legacy) ? ReadLegacy(legacy) : LoadDefaults();
        if (File.Exists(legacy))
        {
            var backup = Path.Combine(Path.GetDirectoryName(FilePath)!, "Settings.user.xml.migration-" + Guid.NewGuid().ToString("N") + ".bak");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(legacy, backup, false);
        }
        Save(result);
        return result;
    }

    public void Save(AppSettings settings)
    {
        Validate(settings);
        AtomicWrite(FilePath, JsonSerializer.SerializeToUtf8Bytes(settings, AppJson.Options));
    }

    public static AppSettings LoadDefaults()
    {
        using var stream = typeof(SettingsStore).Assembly.GetManifestResourceStream("FileConverterNext.Core.Data.Settings.default.xml")!;
        return ReadLegacy(stream);
    }
    public static AppSettings ReadLegacy(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadLegacy(stream);
    }
    public static AppSettings ReadLegacy(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("无效的预设文件。");
        var settings = new AppSettings();
        foreach (var item in root.Elements("ConversionPreset"))
        {
            if (!Enum.TryParse<OutputType>((string?)item.Attribute("OutputType"), true, out var output)) throw new InvalidDataException("未知输出格式。");
            var preset = new Preset
            {
                Name = (string?)item.Attribute("Name") ?? "未命名",
                OutputType = output,
                InputTypes = item.Elements("InputTypes").Select(e => e.Value.Trim().TrimStart('.').ToLowerInvariant()).Distinct().ToList(),
                OutputFileNameTemplate = (string?)item.Element("OutputFileNameTemplate") ?? "(p)(f)",
                ConversionArchiveFolderName = (string?)item.Element("ConversionArchiveFolderName") ?? "Originals",
                IsDefaultSettings = (bool?)item.Attribute("IsDefaultSettings") ?? false,
                InputPostConversionAction = Enum.TryParse<PostAction>((string?)item.Element("InputPostConversionAction"), out var action) ? action : PostAction.None
            };
            foreach (var value in item.Elements("Settings"))
            {
                var key = (string?)value.Attribute("Key");
                if (!string.IsNullOrWhiteSpace(key)) preset.Settings[key] = (string?)value.Attribute("Value") ?? "";
            }
            settings.Presets.Add(preset);
        }
        settings.MaximumNumberOfSimultaneousConversions = Math.Clamp((int?)root.Element("MaximumNumberOfSimultaneousConversions") ?? 2, 1, 64);
        settings.Language = (string?)root.Element("ApplicationLanguageName") ?? "zh-CN";
        settings.CopyFilesInClipboardAfterConversion = (bool?)root.Element("CopyFilesInClipboardAfterConversion") ?? false;
        settings.ExitApplicationWhenConversionsFinished = (bool?)root.Element("ExitApplicationWhenConversionsFinished") ?? false;
        settings.DurationBetweenEndOfConversionsAndApplicationExit = (double?)root.Element("DurationBetweenEndOfConversionsAndApplicationExit") ?? 3;
        settings.HardwareAccelerationMode = Enum.TryParse<HardwareMode>((string?)root.Element("HardwareAccelerationMode"), out var hw) ? hw : HardwareMode.Off;
        settings.Updates.CheckAtStartup = (bool?)root.Element("CheckUpgradeAtStartup") ?? true;
        settings.Folders = settings.Presets.SelectMany(p => ParentFolders(p.Name)).Distinct().ToList();
        return settings;
    }

    public static void ExportLegacy(string path, IEnumerable<Preset> presets)
    {
        var root = new XElement("Settings", new XAttribute("SerializationVersion", "4"));
        foreach (var p in presets)
            root.Add(new XElement("ConversionPreset", new XAttribute("Name", p.Name), new XAttribute("OutputType", p.OutputType),
                new XAttribute("IsDefaultSettings", p.IsDefaultSettings), p.InputTypes.Select(e => new XElement("InputTypes", e)),
                new XElement("InputPostConversionAction", p.InputPostConversionAction),
                p.Settings.Select(e => new XElement("Settings", new XAttribute("Key", e.Key), new XAttribute("Value", e.Value))),
                new XElement("OutputFileNameTemplate", p.OutputFileNameTemplate),
                new XElement("ConversionArchiveFolderName", p.ConversionArchiveFolderName)));
        AtomicWrite(path, System.Text.Encoding.UTF8.GetBytes(new XDocument(root).ToString()));
    }
    public static IEnumerable<string> ParentFolders(string name)
    {
        var parts = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < parts.Length; i++) yield return string.Join('/', parts.Take(i));
    }
    public static void AtomicWrite(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            File.Move(temp, fullPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void Validate(AppSettings settings)
    {
        if (settings.MaximumNumberOfSimultaneousConversions is < 1 or > 64) throw new InvalidDataException("并发数必须在 1–64 之间。");
        if (!double.IsFinite(settings.DurationBetweenEndOfConversionsAndApplicationExit) || settings.DurationBetweenEndOfConversionsAndApplicationExit < 0)
            throw new InvalidDataException("退出等待时间必须是非负数。");
        if (settings.Presets.Select(p => p.Id).Distinct().Count() != settings.Presets.Count) throw new InvalidDataException("预设标识重复。");
        foreach (var p in settings.Presets)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Contains('\0') || p.Name.Length > 1024) throw new InvalidDataException("预设名称无效。");
            if (!Enum.IsDefined(p.OutputType) || !Enum.IsDefined(p.InputPostConversionAction)) throw new InvalidDataException("预设类型无效。");
            if (p.InputTypes.Any(e => string.IsNullOrWhiteSpace(e) || e.Any(c => !char.IsAsciiLetterOrDigit(c)))) throw new InvalidDataException("输入扩展名无效。");
        }
    }
}
