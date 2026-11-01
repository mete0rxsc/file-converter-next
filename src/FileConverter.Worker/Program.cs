using System.Text.Json;
using FileConverter.Core;

namespace FileConverter.Worker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ConversionRequest? request = null;
        using var cancelled = new CancellationTokenSource();
        try
        {
            if (args.Length != 2 || args[0] != "--request") throw new ArgumentException("请通过主程序启动转换工作进程。");
            request = JsonSerializer.Deserialize<ConversionRequest>(await File.ReadAllTextAsync(args[1]), AppJson.Options) ?? throw new InvalidDataException("任务为空。");
            if (request.Version != WorkerProtocol.Version) throw new InvalidDataException("任务协议不匹配。");
            var captured = request;
            _ = Task.Run(async () =>
            {
                while (await Console.In.ReadLineAsync() is { } line)
                    if (line == "cancel") { cancelled.Cancel(); break; }
                // Parent pipe closed: do not leave Office/native work running after the UI exits.
                cancelled.Cancel();
            });
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(Timeout.Infinite, cancelled.Token); }
                catch (OperationCanceledException)
                {
                    // Last resort for a hung native decoder/COM call after the parent dies.
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    Environment.Exit(2);
                }
            });
            void Report(double progress, string text) => WorkerProtocol.Write(Console.Out, new(1, captured.JobId, "progress", Math.Clamp(progress, 0, 1), text));
            var outputs = await new ConversionEngine().ConvertAsync(request, Report, cancelled.Token);
            WorkerProtocol.Write(Console.Out, new(1, request.JobId, "result", 1, Outputs: outputs));
            return 0;
        }
        catch (Exception error)
        {
            WorkerProtocol.Write(Console.Out, new(1, request?.JobId ?? Guid.Empty, "error", Message: error is OperationCanceledException ? "转换已取消。" : error.ToString()));
            return error is OperationCanceledException ? 2 : 1;
        }
    }
}
