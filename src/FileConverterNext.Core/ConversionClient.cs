using System.Diagnostics;
using System.Text.Json;

namespace FileConverterNext.Core;

public sealed class ConversionClient(string workerPath)
{
    public async Task<IReadOnlyList<string>> ConvertAsync(string input, Preset preset, AppSettings settings, IProgress<WorkerMessage>? progress, CancellationToken cancellationToken)
    {
        input = Path.GetFullPath(input);
        if (!File.Exists(input)) throw new FileNotFoundException("输入文件不存在。", input);
        if (!preset.InputTypes.Contains(Path.GetExtension(input).TrimStart('.').ToLowerInvariant())) throw new ArgumentException("预设不支持此输入格式。");
        if (!Formats.CanConvert(Path.GetExtension(input).TrimStart('.').ToLowerInvariant(), preset.OutputType)) throw new ArgumentException("输入类别与输出格式不兼容。");
        using var transaction = new OutputTransaction();
        var request = new ConversionRequest(WorkerProtocol.Version, Guid.NewGuid(), input, preset, settings.Tools, settings.HardwareAccelerationMode, transaction.Workspace);
        var requestFile = transaction.Stage("request.json");
        await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request, AppJson.Options), cancellationToken);
        var info = new ProcessStartInfo(workerPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        info.ArgumentList.Add("--request"); info.ArgumentList.Add(requestFile);
        using var process = new Process { StartInfo = info };
        process.Start();
        // stdout/stderr must be drained concurrently to avoid pipe deadlocks.
        var errors = process.StandardError.ReadToEndAsync();
        using var stopWatchdog = new CancellationTokenSource();
        using var cancel = cancellationToken.Register(() =>
        {
            try { process.StandardInput.WriteLine("cancel"); process.StandardInput.Flush(); } catch (IOException) { } catch (InvalidOperationException) { }
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stopWatchdog.Token);
                    if (!process.HasExited) process.Kill(true);
                }
                catch (OperationCanceledException) { } catch (InvalidOperationException) { }
            });
        });
        WorkerMessage? result = null;
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                var message = JsonSerializer.Deserialize<WorkerMessage>(line, AppJson.Options) ?? throw new InvalidDataException("工作进程消息为空。");
                if (message.Version != WorkerProtocol.Version || message.JobId != request.JobId) throw new InvalidDataException("工作进程协议不匹配。");
                progress?.Report(message);
                if (message.Kind is "result" or "error") result = message;
            }
            await process.WaitForExitAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || result?.Kind != "result") throw new InvalidOperationException(result?.Message ?? await errors);
            var outputs = result.Outputs ?? throw new InvalidDataException("工作进程未返回结果。");
            var stagedFiles = new List<(string, string)>();
            for (var i = 0; i < outputs.Length; i++)
            {
                var staged = Path.GetFullPath(outputs[i]);
                if (!staged.StartsWith(transaction.Workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("工作进程返回了工作目录外的文件。");
                var desired = OutputTemplate.Resolve(input, preset.OutputType, preset.OutputFileNameTemplate, i + 1, outputs.Length);
                if (outputs.Length > 1 && !preset.OutputFileNameTemplate.Contains("(n:i)"))
                    desired = Path.Combine(Path.GetDirectoryName(desired)!, Path.GetFileNameWithoutExtension(desired) + $" - {i + 1}" + Path.GetExtension(desired));
                stagedFiles.Add((staged, desired));
            }
            return transaction.Commit(stagedFiles, input, preset, cancellationToken);
        }
        finally
        {
            stopWatchdog.Cancel();
            try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } } catch (InvalidOperationException) { }
        }
    }
}
