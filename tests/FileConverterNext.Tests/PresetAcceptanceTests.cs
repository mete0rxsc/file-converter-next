using System.Text.Json;
using FileConverterNext.Core;
using FileConverterNext.Worker;
using ImageMagick;
using Xunit;

namespace FileConverterNext.Tests;

public sealed class PresetAcceptanceTests
{
    [Fact]
    public async Task EveryDefaultPresetWithoutCdHardwareProducesReadableOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "FileConverterNext-presets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var tools = ConversionTests.Tools();
        var sourceWav = Path.Combine(root, "音频.wav");
        var sourceMp4 = Path.Combine(root, "视频.mp4");
        var sourceVob = Path.Combine(root, "DVD.vob");
        var sourcePng = Path.Combine(root, "图片.png");
        var sourcePdf = Path.Combine(root, "文档.pdf");
        try
        {
            using (var image = new MagickImage(MagickColors.Orange, 96, 64)) image.Write(sourcePng);
            using (var image = new MagickImage(MagickColors.White, 96, 64)) image.Write(sourcePdf, MagickFormat.Pdf);
            await ProcessRunner.RunAsync(tools.FFmpeg, ["-nostdin", "-y", "-f", "lavfi", "-i", "sine=duration=0.3", sourceWav], default);
            await ProcessRunner.RunAsync(tools.FFmpeg,
                ["-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=size=96x64:rate=4:duration=0.3",
                 "-f", "lavfi", "-i", "sine=duration=0.3", "-c:v", "libx264", "-shortest", sourceMp4], default);
            await ProcessRunner.RunAsync(tools.FFmpeg, ["-nostdin", "-y", "-i", sourceMp4, "-c:v", "mpeg2video", "-c:a", "mp2", "-f", "vob", sourceVob], default);
            var failures = new List<string>(); var passed = new List<string>(); var untested = new List<string>();
            foreach (var preset in SettingsStore.LoadDefaults().Presets)
            {
                if (preset.InputTypes.Contains("cda") && preset.InputTypes.Count == 1) { untested.Add(preset.Name + ": requires real audio CD"); continue; }
                var input = new[] { sourcePng, sourceWav, sourceMp4, sourceVob, sourcePdf }.FirstOrDefault(path => preset.InputTypes.Contains(Path.GetExtension(path).TrimStart('.')));
                if (input is null) { failures.Add(preset.Name + ": no representative fixture"); continue; }
                var request = new ConversionRequest(1, Guid.NewGuid(), input, preset, tools, HardwareMode.Off, Path.Combine(root, Guid.NewGuid().ToString("N")));
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    var outputs = await new ConversionEngine().ConvertAsync(request, (_, _) => { }, timeout.Token);
                    Assert.NotEmpty(outputs); Assert.All(outputs, path => Assert.True(new FileInfo(path).Length > 0));
                    passed.Add(preset.Name);
                }
                catch (Exception error) { failures.Add(preset.Name + ": " + error); }
            }
            var repo = Directory.GetParent(Directory.GetParent(Directory.GetParent(Directory.GetParent(Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName)!.FullName)!.FullName)!.FullName)!.FullName;
            var report = Path.Combine(repo, "artifacts", "preset-acceptance.json");
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Passed = passed, Untested = untested, Failures = failures }, AppJson.Options));
            Assert.Empty(failures); Assert.Equal(76, passed.Count + untested.Count);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
    [Fact]
    public async Task NvidiaHardwareEncodingAndSoftwareFallbackProduceValidVideo()
    {
        var tools = ConversionTests.Tools();
        var root = Path.Combine(Path.GetTempPath(), "FileConverterNext-hardware", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.mp4");
            await ProcessRunner.RunAsync(tools.FFmpeg, ["-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=size=256x256:duration=0.3", "-c:v", "libx264", input], default);
            var preset = new Preset { Name = "NVENC", OutputType = OutputType.Mp4, InputTypes = ["mp4"], Settings = PresetDefaults.For(OutputType.Mp4) };
            // First verify the actual hardware path, so software fallback cannot mask a failed GPU test.
            var nativeOutput = Path.Combine(root, "hardware.mp4");
            await ProcessRunner.RunAsync(tools.FFmpeg, FfmpegArguments.Build(preset, input, nativeOutput, HardwareMode.CUDA), default);
            Assert.True(File.Exists(nativeOutput));
            var messages = new List<string>();
            var request = new ConversionRequest(1, Guid.NewGuid(), input, preset, tools, HardwareMode.AMF, Path.Combine(root, "fallback"));
            var outputs = await new ConversionEngine().ConvertAsync(request, (_, text) => messages.Add(text), default);
            Assert.Single(outputs); Assert.Contains(messages, text => text.Contains("回退"));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
