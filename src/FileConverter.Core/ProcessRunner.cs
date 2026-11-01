using System.Diagnostics;
using System.Text;

namespace FileConverter.Core;

public static class ProcessRunner
{
    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token, Action<string>? onLine = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        process.Start();
        using var cancellation = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        async Task ReadAsync(StreamReader reader, StringBuilder buffer, bool report)
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                if (buffer.Length < 256 * 1024) buffer.AppendLine(line);
                if (report) onLine?.Invoke(line);
            }
        }
        await Task.WhenAll(ReadAsync(process.StandardOutput, stdout, true), ReadAsync(process.StandardError, stderr, false), process.WaitForExitAsync(token));
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} 返回 {process.ExitCode}：{stderr}");
        return stdout.ToString();
    }

    public static string ResolveTool(string configured, string name)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? Path.GetFullPath(configured) : throw new FileNotFoundException("配置的工具不存在：" + configured);
        foreach (var root in new[] { AppContext.BaseDirectory, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")) })
        {
            foreach (var relative in new[] { name, Path.Combine("tools", name), Path.Combine("tools", Path.GetFileNameWithoutExtension(name), name) })
            { var candidate = Path.Combine(root, relative); if (File.Exists(candidate)) return candidate; }
        }
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            var candidate = Path.Combine(folder.Trim('"'), name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException($"找不到 {name}。请安装完整发行包，或在设置中指定工具路径。");
    }
}
