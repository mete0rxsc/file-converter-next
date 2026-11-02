using System.Runtime.Versioning;
using FileConverterNext.Core;

namespace FileConverterNext.Worker;

[SupportedOSPlatform("windows")]
public static partial class OfficeExporter
{
    public static async Task ExportAsync(string input, string output, string configuredExecutable, string workspace, CancellationToken token, Action<string>? status = null)
    {
        token.ThrowIfCancellationRequested();
        var executable = ProcessRunner.ResolveTool(configuredExecutable, "soffice.com");
        if (Path.GetFileName(executable).Equals("soffice.exe", StringComparison.OrdinalIgnoreCase))
        {
            var commandLineLauncher = Path.Combine(Path.GetDirectoryName(executable)!, "soffice.com");
            if (File.Exists(commandLineLauncher)) executable = commandLineLauncher;
        }
        var profile = Path.Combine(workspace, "libreoffice-profile");
        var outputDirectory = Path.Combine(workspace, "office-export");
        var template = FindProfileTemplate();
        if (template is null) Directory.CreateDirectory(profile);
        else CopyProfile(template, profile);
        Directory.CreateDirectory(outputDirectory);
        status?.Invoke("LibreOffice：导出 PDF");
        var profileUri = new Uri(Path.GetFullPath(profile)).AbsoluteUri;
        var filter = Path.GetExtension(input).ToLowerInvariant() switch
        {
            ".doc" or ".docx" or ".odt" => "writer_pdf_Export",
            ".xls" or ".xlsx" or ".ods" => "calc_pdf_Export",
            ".ppt" or ".pptx" or ".odp" => "impress_pdf_Export",
            _ => throw new NotSupportedException("LibreOffice 不支持此文档格式。")
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await ProcessRunner.RunAsync(executable,
                [$"-env:UserInstallation={profileUri}", "--headless", "--invisible", "--nodefault", "--nologo", "--nolockcheck", "--convert-to", "pdf:" + filter, "--outdir", outputDirectory, Path.GetFullPath(input)], deadline.Token,
                workingDirectory: Path.GetDirectoryName(executable));
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("LibreOffice 导出 PDF 超过 5 分钟，已结束本次转换进程。", error);
        }
        token.ThrowIfCancellationRequested();
        var generated = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(input) + ".pdf");
        if (File.Exists(generated)) File.Move(generated, output, true);
        if (!File.Exists(output)) throw new IOException("LibreOffice 未生成 PDF 文件。");
        if (new FileInfo(output).Length == 0) throw new InvalidDataException("LibreOffice 生成了空 PDF 文件。");
    }

    private static string? FindProfileTemplate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "libreoffice-profile");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void CopyProfile(string source, string destination)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
