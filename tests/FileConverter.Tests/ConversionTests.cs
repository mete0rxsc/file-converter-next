using System.Text.Json;
using FileConverter.Core;
using FileConverter.Worker;
using ImageMagick;
using Xunit;

namespace FileConverter.Tests;

public sealed class ConversionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FileConverter-conversion-tests", Guid.NewGuid().ToString("N"));
    public ConversionTests() => Directory.CreateDirectory(root);
    private string FilePath(string name) => Path.Combine(root, name);
    private static string Repo
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "File Conventer.sln"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("repo");
        }
    }
    public static ToolSettings Tools()
    {
        var dependencies = Path.Combine(Repo, "artifacts", "dependencies");
        return new()
        {
            FFmpeg = Directory.GetFiles(Path.Combine(dependencies, "ffmpeg"), "ffmpeg.exe", SearchOption.AllDirectories).Single(),
            FFprobe = Directory.GetFiles(Path.Combine(dependencies, "ffmpeg"), "ffprobe.exe", SearchOption.AllDirectories).Single(),
            Ghostscript = Path.Combine(dependencies, "ghostscript", "bin", "gswin64c.exe")
        };
    }
    private string Image()
    {
        var path = FilePath("中文 图片.png");
        using var image = new MagickImage(MagickColors.CornflowerBlue, 96, 64); image.Write(path); return path;
    }
    private ConversionRequest Request(string input, OutputType output)
    {
        var preset = new Preset { Name = "Test", OutputType = output, InputTypes = [Path.GetExtension(input).TrimStart('.')], Settings = PresetDefaults.For(output) };
        return new(1, Guid.NewGuid(), input, preset, Tools(), HardwareMode.Off, Path.Combine(root, Guid.NewGuid().ToString("N")));
    }
    [Theory]
    [InlineData(OutputType.Avif)] [InlineData(OutputType.Gif)] [InlineData(OutputType.Ico)]
    [InlineData(OutputType.Jpg)] [InlineData(OutputType.Png)] [InlineData(OutputType.Webp)] [InlineData(OutputType.Pdf)]
    public async Task ImageOutputsAreReadableAndRespectTransform(OutputType output)
    {
        var request = Request(Image(), output);
        request.Preset.Settings["ImageScale"] = "0.5";
        var files = await new ConversionEngine().ConvertAsync(request, (_, _) => { }, default);
        Assert.Single(files); Assert.True(new FileInfo(files[0]).Length > 0);
        if (output != OutputType.Pdf)
        {
            using var result = new MagickImage(files[0]); Assert.Equal(output == OutputType.Ico ? 32u : 48u, result.Width);
            Assert.Equal(32u, result.Height);
        }
    }
    [Fact] public async Task PdfPagesAreAllConverted()
    {
        var pdf = FilePath("两页.pdf");
        using (var images = new MagickImageCollection())
        {
            images.Add(new MagickImage(MagickColors.Red, 120, 80));
            images.Add(new MagickImage(MagickColors.Green, 120, 80));
            images.Write(pdf, MagickFormat.Pdf);
        }
        var files = await new ConversionEngine().ConvertAsync(Request(pdf, OutputType.Png), (_, _) => { }, default);
        Assert.Equal(2, files.Length);
        foreach (var file in files) { using var image = new MagickImage(file); Assert.True(image.Width > 0); }
    }
    [Theory]
    [InlineData(OutputType.Aac)] [InlineData(OutputType.Flac)] [InlineData(OutputType.Mp3)] [InlineData(OutputType.Ogg)] [InlineData(OutputType.Wav)]
    public async Task AudioOutputsContainAudioStreams(OutputType output)
    {
        var tools = Tools(); var wav = FilePath("中文 audio.wav");
        await ProcessRunner.RunAsync(tools.FFmpeg, ["-nostdin", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.4", wav], default);
        var request = Request(wav, output);
        var files = await new ConversionEngine().ConvertAsync(request, (_, _) => { }, default);
        using var json = JsonDocument.Parse(await ProcessRunner.RunAsync(tools.FFprobe, ["-v", "error", "-show_streams", "-of", "json", files[0]], default));
        Assert.Contains(json.RootElement.GetProperty("streams").EnumerateArray(), s => s.GetProperty("codec_type").GetString() == "audio");
    }
    [Theory] [InlineData("Wav8", 8)] [InlineData("Wav16", 16)] [InlineData("Wav24", 24)] [InlineData("Wav32", 32)]
    public async Task AllWavBitDepthsAreCorrect(string encoding, int bits)
    {
        var tools = Tools(); var wav = FilePath("source.wav");
        await ProcessRunner.RunAsync(tools.FFmpeg, ["-nostdin", "-y", "-f", "lavfi", "-i", "sine=duration=0.2", wav], default);
        var request = Request(wav, OutputType.Wav); request.Preset.Settings["AudioEncodingMode"] = encoding;
        var files = await new ConversionEngine().ConvertAsync(request, (_, _) => { }, default);
        using var json = JsonDocument.Parse(await ProcessRunner.RunAsync(tools.FFprobe, ["-v", "error", "-show_streams", "-of", "json", files[0]], default));
        Assert.Equal(bits, json.RootElement.GetProperty("streams")[0].GetProperty("bits_per_sample").GetInt32());
    }
    [Theory]
    [InlineData(OutputType.Avi)] [InlineData(OutputType.Mkv)] [InlineData(OutputType.Mp4)]
    [InlineData(OutputType.Ogv)] [InlineData(OutputType.Webm)] [InlineData(OutputType.Gif)]
    public async Task VideoOutputsContainReadableVideoStreams(OutputType output)
    {
        var tools = Tools(); var video = FilePath("中文 video.mp4");
        await ProcessRunner.RunAsync(tools.FFmpeg,
            ["-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=size=96x64:rate=12:duration=0.5",
                "-f", "lavfi", "-i", "sine=duration=0.5", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-shortest", video], default);
        var request = Request(video, output);
        var files = await new ConversionEngine().ConvertAsync(request, (_, _) => { }, default);
        using var json = JsonDocument.Parse(await ProcessRunner.RunAsync(tools.FFprobe, ["-v", "error", "-show_streams", "-of", "json", files[0]], default));
        Assert.Contains(json.RootElement.GetProperty("streams").EnumerateArray(), s => s.GetProperty("codec_type").GetString() == "video");
    }
    [Fact] public async Task SeparateWorkerCommitsVerifiedOutputAndKeepsInput()
    {
        var input = Image(); var settings = new AppSettings { Tools = Tools() };
        var preset = Request(input, OutputType.Webp).Preset;
        var worker = Path.Combine(Repo, "src", "FileConverter.Worker", "bin", "Debug", "net10.0-windows", "FileConverter.Worker.exe");
        var result = await new ConversionClient(worker).ConvertAsync(input, preset, settings, null, default);
        Assert.Single(result); Assert.True(File.Exists(input)); using var check = new MagickImage(result[0]); Assert.Equal(96u, check.Width);
    }
    [Fact] public async Task CorruptImageWorkerDoesNotDeleteInputOrCreateOutput()
    {
        var input = FilePath("corrupt.png"); File.WriteAllText(input, "not an image");
        var preset = Request(input, OutputType.Webp).Preset; preset.InputPostConversionAction = PostAction.Delete;
        var worker = Path.Combine(Repo, "src", "FileConverter.Worker", "bin", "Debug", "net10.0-windows", "FileConverter.Worker.exe");
        await Assert.ThrowsAnyAsync<Exception>(() => new ConversionClient(worker).ConvertAsync(input, preset, new() { Tools = Tools() }, null, default));
        Assert.Equal("not an image", File.ReadAllText(input)); Assert.False(File.Exists(FilePath("corrupt.webp")));
    }
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }
}
