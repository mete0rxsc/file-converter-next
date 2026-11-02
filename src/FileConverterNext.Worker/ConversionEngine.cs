using System.Globalization;
using System.Text.Json;
using FileConverterNext.Core;
using ImageMagick;

namespace FileConverterNext.Worker;

public sealed class ConversionEngine
{
    public async Task<string[]> ConvertAsync(ConversionRequest request, Action<double, string> progress, CancellationToken token)
    {
        var input = request.Input;
        var extension = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        var preset = request.Preset;
        Directory.CreateDirectory(request.Workspace);
        token.ThrowIfCancellationRequested();
        if (extension == "cda")
        {
            var wav = Path.Combine(request.Workspace, "cd-audio.wav");
            await Task.Run(() => AudioCd.Extract(input, wav, (value) => progress(value * .4, "读取音频 CD"), token), token);
            input = wav; extension = "wav";
        }
        if (Formats.IsOffice(extension))
        {
            var pdf = Path.Combine(request.Workspace, "office.pdf");
            await OfficeExporter.ExportAsync(input, pdf, request.Tools.LibreOffice, request.Workspace, token, text => progress(.02, text));
            input = pdf; extension = "pdf";
            progress(.1, "Office 已导出 PDF");
        }
        if (extension == "pdf" && preset.OutputType == OutputType.Pdf)
        {
            var copied = Output(request, 0);
            File.Copy(input, copied);
            await ValidatePdf(copied, request.Tools, token);
            return [copied];
        }
        if (extension == "pdf")
        {
            var gs = ProcessRunner.ResolveTool(request.Tools.Ghostscript, "gswin64c.exe");
            var pattern = Path.Combine(request.Workspace, "page-%06d.png");
            await ProcessRunner.RunAsync(gs, ["-dSAFER", "-dBATCH", "-dNOPAUSE", "-sDEVICE=pngalpha", "-r200", "-sOutputFile=" + pattern, "-f", input], token);
            var pages = Directory.GetFiles(request.Workspace, "page-*.png").Order(StringComparer.Ordinal).ToArray();
            if (pages.Length == 0) throw new InvalidDataException("PDF 未生成任何页面。");
            for (var i = 0; i < pages.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                await ConvertImage(pages[i], Output(request, i), preset, request.Tools, token);
                progress((i + 1d) / pages.Length, $"处理页面 {i + 1}/{pages.Length}");
            }
            return Enumerable.Range(0, pages.Length).Select(i => Output(request, i)).ToArray();
        }
        if (Formats.IsImageOutput(preset.OutputType) && (Formats.IsImage(extension) || extension == "gif" && preset.OutputType != OutputType.Gif))
        {
            var output = Output(request, 0);
            await ConvertImage(input, output, preset, request.Tools, token);
            progress(1, "图片转换完成");
            return [output];
        }
        var target = Output(request, 0);
        await ConvertMedia(input, target, request, progress, token);
        return [target];
    }
    private static string Output(ConversionRequest request, int index) =>
        Path.Combine(request.Workspace, $"output-{index:D6}.{request.Preset.OutputType.ToString().ToLowerInvariant()}");

    private static async Task ConvertImage(string input, string output, Preset preset, ToolSettings tools, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var read = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
        read.Format = Path.GetExtension(input).ToLowerInvariant() switch
        { ".avif" => MagickFormat.Avif, ".cr2" => MagickFormat.Cr2, ".dng" => MagickFormat.Dng, _ => MagickFormat.Unknown };
        using var image = new MagickImage(input, read);
        image.Progress += (_, e) => { if (token.IsCancellationRequested) e.Cancel = true; };
        var scale = preset.Number("ImageScale", 1);
        if (scale <= 0 || scale > 100) throw new ArgumentException("图片缩放比例必须大于 0 且不超过 100。");
        if (Math.Abs(scale - 1) > .00001) image.Scale(new Percentage(scale * 100));
        var rotation = preset.Number("ImageRotation", 0);
        if (rotation != 0) image.Rotate(-rotation);
        if (preset.Flag("ImageClampSizePowerOf2") || preset.OutputType == OutputType.Ico)
        {
            uint size = 1;
            var min = Math.Min(image.Width, image.Height);
            while (size <= min / 2) size *= 2;
            if (preset.OutputType == OutputType.Ico) size = Math.Min(size, 256);
            image.Scale(new MagickGeometry(size, size) { IgnoreAspectRatio = true });
        }
        var max = (uint)Math.Clamp(preset.Number("ImageMaximumSize", 0), 0, 100000);
        if (max > 0) image.Scale(Math.Min(max, image.Width), Math.Min(max, image.Height));
        image.Quality = (uint)Math.Clamp(preset.Number("ImageQuality", preset.OutputType == OutputType.Png ? 95 : 90), 0, 100);
        if (preset.OutputType == OutputType.Pdf) image.Density = new Density(200);
        image.Format = preset.OutputType switch
        {
            OutputType.Avif => MagickFormat.Avif, OutputType.Gif => MagickFormat.Gif,
            OutputType.Ico => MagickFormat.Ico, OutputType.Jpg => MagickFormat.Jpeg,
            OutputType.Pdf => MagickFormat.Pdf, OutputType.Png => MagickFormat.Png,
            OutputType.Webp => MagickFormat.WebP, _ => throw new ArgumentException("不是图片输出格式。")
        };
        if (preset.OutputType == OutputType.Jpg) { image.BackgroundColor = MagickColors.White; image.Alpha(AlphaAction.Remove); }
        token.ThrowIfCancellationRequested();
        image.Write(output);
        token.ThrowIfCancellationRequested();
        if (preset.OutputType == OutputType.Pdf) await ValidatePdf(output, tools, token);
        else { using var check = new MagickImage(output); if (check.Width == 0 || check.Height == 0) throw new InvalidDataException("生成的图片尺寸无效。"); }
    }
    private static async Task ValidatePdf(string path, ToolSettings tools, CancellationToken token)
    {
        await ProcessRunner.RunAsync(ProcessRunner.ResolveTool(tools.Ghostscript, "gswin64c.exe"),
            ["-dSAFER", "-dBATCH", "-dNOPAUSE", "-sDEVICE=nullpage", "-f", path], token);
    }
    private static async Task ConvertMedia(string input, string output, ConversionRequest request, Action<double, string> progress, CancellationToken token)
    {
        var ffmpeg = ProcessRunner.ResolveTool(request.Tools.FFmpeg, "ffmpeg.exe");
        var ffprobe = ProcessRunner.ResolveTool(request.Tools.FFprobe, "ffprobe.exe");
        var probe = await ProcessRunner.RunAsync(ffprobe, ["-v", "error", "-show_entries", "format=duration", "-of", "json", input], token);
        using var metadata = JsonDocument.Parse(probe);
        var duration = metadata.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var d) &&
            double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
        void OnLine(string line)
        {
            if (line.StartsWith("out_time_us=") && double.TryParse(line.AsSpan(12), CultureInfo.InvariantCulture, out var microseconds) && duration > 0)
                progress(microseconds / 1_000_000 / duration, "转换中");
        }
        try { await ProcessRunner.RunAsync(ffmpeg, FfmpegArguments.Build(request.Preset, input, output, request.Hardware), token, OnLine); }
        catch (InvalidOperationException) when (request.Hardware != HardwareMode.Off && request.Preset.OutputType is OutputType.Mp4 or OutputType.Mkv && !request.Preset.Flag("EnableFFMPEGCustomCommand"))
        {
            token.ThrowIfCancellationRequested();
            progress(0, "硬件编码失败，回退软件编码");
            if (File.Exists(output)) File.Delete(output); // Owned staging file only.
            await ProcessRunner.RunAsync(ffmpeg, FfmpegArguments.Build(request.Preset, input, output), token, OnLine);
        }
        var validation = await ProcessRunner.RunAsync(ffprobe, ["-v", "error", "-show_streams", "-of", "json", output], token);
        using var checkedFile = JsonDocument.Parse(validation);
        if (!checkedFile.RootElement.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            throw new InvalidDataException("生成的媒体文件没有可读流。");
        progress(1, "媒体转换完成");
    }
}
