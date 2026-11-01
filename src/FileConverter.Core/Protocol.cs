using System.Text.Json;

namespace FileConverter.Core;

public sealed record ConversionRequest(int Version, Guid JobId, string Input, Preset Preset, ToolSettings Tools, HardwareMode Hardware, string Workspace);
public sealed record WorkerMessage(int Version, Guid JobId, string Kind, double Progress = 0, string? Message = null, string[]? Outputs = null);
public sealed record ShellRequest(int Version, string Preset, string[] Files);

public static class WorkerProtocol
{
    public const int Version = 1;
    public static void Write(TextWriter writer, WorkerMessage message)
    {
        lock (writer) { writer.WriteLine(JsonSerializer.Serialize(message, AppJson.WireOptions)); writer.Flush(); }
    }
}

public sealed class CommandLine
{
    public bool Settings { get; private set; }
    public bool Version { get; private set; }
    public bool Verbose { get; private set; }
    public bool Register { get; private set; }
    public bool Unregister { get; private set; }
    public bool ApplyShell { get; private set; }
    public string? IntegrationUserSid { get; private set; }
    public string? IntegrationLocalAppData { get; private set; }
    public string? Preset { get; private set; }
    public string? RequestFile { get; private set; }
    public List<string> Files { get; } = [];
    public static CommandLine Parse(string[] arguments)
    {
        var result = new CommandLine();
        for (var i = 0; i < arguments.Length; i++)
        {
            string Value() => ++i < arguments.Length ? arguments[i] : throw new ArgumentException("命令参数缺少值。");
            switch (arguments[i].ToLowerInvariant())
            {
                case "--settings": result.Settings = true; break;
                case "--version": result.Version = true; break;
                case "--verbose": result.Verbose = true; break;
                case "--register-shell-extension": result.Register = true; break;
                case "--unregister-shell-extension": result.Unregister = true; break;
                case "--apply-shell-mode": result.ApplyShell = true; break;
                case "--post-install-init": result.Register = true; break;
                case "--integration-user-sid": result.IntegrationUserSid = Value(); break;
                case "--integration-local-app-data": result.IntegrationLocalAppData = Value(); break;
                case "--conversion-preset": result.Preset = Value(); break;
                case "--input-files": result.Files.AddRange(File.ReadAllLines(Value()).Where(l => !string.IsNullOrWhiteSpace(l))); break;
                case "--request": result.RequestFile = Value(); break;
                case "--": result.Files.AddRange(arguments.Skip(i + 1)); return result;
                default:
                    if (arguments[i].StartsWith("--")) throw new ArgumentException("未知命令参数：" + arguments[i]);
                    result.Files.Add(arguments[i]); break;
            }
        }
        return result;
    }
}
