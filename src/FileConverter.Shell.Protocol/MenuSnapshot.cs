using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace FileConverter.Shell.Protocol;

public sealed record MenuPreset(string Id, string Name, string[] InputTypes);
public sealed record MenuSnapshot(string Executable, string SettingsLabel, bool Icons, MenuPreset[] Presets)
{
    public const int MaxSize = 4 * 1024 * 1024;
    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("FCM1"u8);
        WriteText(writer, Executable); WriteText(writer, SettingsLabel); writer.Write(Icons ? 1 : 0);
        writer.Write(Presets.Length);
        if (Presets.Length > 2048) throw new InvalidDataException("预设数量超过菜单限制。");
        foreach (var p in Presets)
        {
            ValidatePreset(p);
            WriteText(writer, p.Id); WriteText(writer, p.Name.Replace('\\', '/'));
            writer.Write(p.InputTypes.Length);
            foreach (var extension in p.InputTypes) WriteText(writer, extension.ToLowerInvariant());
        }
        if (stream.Length > MaxSize) throw new InvalidDataException("菜单快照超过大小限制。");
        return stream.ToArray();
    }
    public static MenuSnapshot Decode(byte[] bytes)
    {
        if (bytes.Length > MaxSize) throw new InvalidDataException("菜单快照过大。");
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        if (!reader.ReadBytes(4).SequenceEqual("FCM1"u8.ToArray())) throw new InvalidDataException("菜单版本不匹配。");
        var executable = ReadText(reader); var settings = ReadText(reader); var icons = reader.ReadInt32() != 0;
        var count = Count(reader, 2048);
        var presets = new MenuPreset[count];
        for (var i = 0; i < count; i++)
        {
            var id = ReadText(reader); var name = ReadText(reader);
            var inputs = new string[Count(reader, 256)];
            for (var j = 0; j < inputs.Length; j++) inputs[j] = ReadText(reader);
            presets[i] = new(id, name, inputs);
            ValidatePreset(presets[i]);
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("菜单快照含有额外数据。");
        return new(executable, settings, icons, presets);
    }
    private static void WriteText(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 65536 || value.Contains('\0')) throw new InvalidDataException("菜单文本无效。");
        writer.Write(bytes.Length); writer.Write(bytes);
    }
    private static void ValidatePreset(MenuPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Id) || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 1024 ||
            preset.Name.Replace('\\', '/').Split('/').Length > 16 || preset.InputTypes.Length > 256)
            throw new InvalidDataException("菜单预设无效或嵌套层级过深。");
    }
    private static string ReadText(BinaryReader reader)
    {
        var length = Count(reader, 65536);
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        var text = new UTF8Encoding(false, true).GetString(bytes);
        if (text.Contains('\0')) throw new InvalidDataException("菜单文本包含空字符。");
        return text;
    }
    private static int Count(BinaryReader reader, int max)
    {
        var n = reader.ReadInt32();
        return n >= 0 && n <= max ? n : throw new InvalidDataException("菜单长度无效。");
    }
    public IReadOnlyList<(MenuPreset Preset, bool Enabled)> Select(IEnumerable<string> paths)
    {
        var extensions = paths.Select(p => Path.GetExtension(p).TrimStart('.').ToLowerInvariant()).Distinct().ToArray();
        if (extensions.Length == 0) return [];
        return Presets.Where(p => extensions.Any(e => p.InputTypes.Contains(e)))
            .Select(p => (p, extensions.All(e => p.InputTypes.Contains(e)))).ToArray();
    }
}

public static partial class ShellPaths
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetEnvironmentVariableW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint ReadEnvironment(string name, char* value, uint size);
    public static unsafe string Data
    {
        get
        {
            var buffer = new char[32768];
            fixed (char* value = buffer)
            {
                var length = ReadEnvironment("FILECONVERTER_SHELL_TEST_ROOT", value, (uint)buffer.Length);
                if (length > 0 && length < buffer.Length) return Path.GetFullPath(new string(value, 0, (int)length));
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileConverter");
        }
    }
    public static string Snapshot => Path.Combine(Data, "menu.bin");
    public static string CreateRequest(string preset, IReadOnlyList<string> paths)
    {
        var directory = Path.Combine(Data, "Requests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(file);
        writer.WriteStartObject(); writer.WriteNumber("Version", 1); writer.WriteString("Preset", preset);
        writer.WriteStartArray("Files"); foreach (var input in paths) writer.WriteStringValue(input);
        writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush(); file.Flush(true);
        return path;
    }
    public static void Invoke(MenuSnapshot snapshot, MenuPreset? preset, IReadOnlyList<string> paths)
    {
        if (!File.Exists(snapshot.Executable)) throw new FileNotFoundException("FileConverter 程序不存在。");
        var info = new ProcessStartInfo(snapshot.Executable) { UseShellExecute = false };
        string? request = null;
        if (preset is null) info.ArgumentList.Add("--settings");
        else { request = CreateRequest(preset.Id, paths); info.ArgumentList.Add("--request"); info.ArgumentList.Add(request); }
        try { Process.Start(info)?.Dispose(); }
        catch { if (request is not null) File.Delete(request); throw; }
    }
}
