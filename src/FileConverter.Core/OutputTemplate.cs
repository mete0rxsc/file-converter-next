using System.Globalization;
using System.Text.RegularExpressions;

namespace FileConverter.Core;

public static partial class OutputTemplate
{
    [GeneratedRegex(@"\(d:(?<format>[^)]*)\)")]
    private static partial Regex DateToken();
    public static string Resolve(string input, OutputType output, string template, int index = 1, int count = 1, DateTime? now = null)
    {
        var fullInput = Path.GetFullPath(input);
        if (string.IsNullOrEmpty(template)) return Path.ChangeExtension(fullInput, output.ToString().ToLowerInvariant());
        var parent = Path.GetDirectoryName(fullInput)! + Path.DirectorySeparatorChar;
        var file = Path.GetFileNameWithoutExtension(fullInput);
        var inputExt = Path.GetExtension(fullInput).TrimStart('.');
        var outputExt = output.ToString().ToLowerInvariant();
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["path"] = parent, ["p"] = parent, ["filename"] = file, ["f"] = file, ["F"] = file.ToUpperInvariant(),
            ["outputext"] = outputExt, ["o"] = outputExt, ["O"] = outputExt.ToUpperInvariant(),
            ["inputext"] = inputExt, ["i"] = inputExt, ["I"] = inputExt.ToUpperInvariant(),
            ["n:i"] = index.ToString(CultureInfo.InvariantCulture), ["n:c"] = count.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var (shortName, longName, folder) in new[]
        {
            ("d", "documents", Environment.SpecialFolder.MyDocuments), ("m", "music", Environment.SpecialFolder.MyMusic),
            ("v", "videos", Environment.SpecialFolder.MyVideos), ("p", "pictures", Environment.SpecialFolder.MyPictures)
        })
        {
            values["p:" + shortName] = values["p:" + longName] = Environment.GetFolderPath(folder) + Path.DirectorySeparatorChar;
        }
        var dir = new DirectoryInfo(Path.GetDirectoryName(fullInput)!);
        for (var n = 0; dir != null && !string.IsNullOrEmpty(dir.Name); n++, dir = dir.Parent)
        { var name = dir.Parent is null ? dir.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : dir.Name; values["d" + n] = name; values["D" + n] = name.ToUpperInvariant(); }
        var result = template;
        foreach (var (key, value) in values) result = result.Replace("(" + key + ")", value, StringComparison.Ordinal);
        result = DateToken().Replace(result, m => (now ?? DateTime.Now).ToString(m.Groups["format"].Value, CultureInfo.CurrentCulture).Replace('/', '-').Replace(':', '\''));
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("输出路径模板为空。");
        // Upstream templates omit the extension; append it unconditionally, matching v2 behavior.
        return Path.GetFullPath(result + "." + outputExt, Path.GetDirectoryName(fullInput)!);
    }
}
