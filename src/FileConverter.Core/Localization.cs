using System.Text.Json;

namespace FileConverter.Core;

public sealed class Localization
{
    private Dictionary<string, string> values = [];
    private readonly Dictionary<string, string> english;
    public Localization() => english = Read("en");
    public static string[] Languages => typeof(Localization).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith("FileConverter.Core.Data.Locales.") && n.EndsWith(".json"))
        .Select(n => n["FileConverter.Core.Data.Locales.".Length..^5]).Distinct().Order().ToArray();
    private static Dictionary<string, string> Read(string language)
    {
        var name = typeof(Localization).Assembly.GetManifestResourceNames().FirstOrDefault(n =>
            n.Equals("FileConverter.Core.Data.Locales." + language + ".json", StringComparison.OrdinalIgnoreCase));
        if (name is null) return [];
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
    public void Use(string language) { this.language = language; values = Read(language); }
    private string language = "zh-CN";
    public string Translate(string key, string chinese, string fallback)
    {
        var localized = this[key];
        return localized == key ? language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? chinese : fallback : localized;
    }
    public string Language => language;
    public string this[string key] => values.GetValueOrDefault(key, english.GetValueOrDefault(key, key));
}
