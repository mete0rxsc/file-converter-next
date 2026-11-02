using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using FileConverterNext.Core;
using ImageMagick;
using Xunit;

namespace FileConverterNext.Tests;

public sealed class OfficeTests
{
    [Fact]
    public async Task DocumentFormatsConvertWithoutStartingMicrosoftOffice()
    {
        var root = Path.Combine(Path.GetTempPath(), "FileConverterNext-document-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var officeBefore = OfficeProcessIds();
        try
        {
            var office = LibreOfficePath();
            var fixtureProfile = await CreateInitializedProfile(office, root);
            var writer = CopyFixture(Path.Combine("share", "template", "common", "internal", "idxexample.odt"), root, "writer 中文.odt");
            var calc = CopyFixture(Path.Combine("help", "media", "files", "scalc", "trigon.ods"), root, "calc 中文.ods");
            var impress = CopyPresentationFixture(root);
            var inputs = new List<string>
            {
                writer,
                await ConvertLibreOffice(office, root, fixtureProfile, writer, "doc", "MS Word 97"),
                await ConvertLibreOffice(office, root, fixtureProfile, writer, "docx", "Office Open XML Text"),
                calc,
                await ConvertLibreOffice(office, root, fixtureProfile, calc, "xls", "MS Excel 97"),
                await ConvertLibreOffice(office, root, fixtureProfile, calc, "xlsx", "Calc MS Excel 2007 XML"),
                impress,
                await ConvertLibreOffice(office, root, fixtureProfile, impress, "ppt", "MS PowerPoint 97"),
                await ConvertLibreOffice(office, root, fixtureProfile, impress, "pptx", "Impress MS PowerPoint 2007 XML")
            };

            var worker = Path.Combine(PublishDirectory(), "worker", "FileConverterNext.Worker.exe");
            foreach (var input in inputs)
            {
                var extension = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
                foreach (var output in new[] { OutputType.Pdf, OutputType.Png })
                {
                    var preset = new Preset { Name = "Document acceptance", OutputType = output, InputTypes = [extension], Settings = PresetDefaults.For(output) };
                    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    var files = await new ConversionClient(worker).ConvertAsync(input, preset, new() { Tools = ConversionTests.Tools() }, null, deadline.Token);
                    Assert.NotEmpty(files);
                    Assert.All(files, path => Assert.True(new FileInfo(path).Length > 0));
                    if (output == OutputType.Png)
                        foreach (var path in files) { using var image = new MagickImage(path); Assert.True(image.Width > 0 && image.Height > 0); }
                    Assert.True(File.Exists(input));
                }
            }
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        Assert.Empty(OfficeProcessIds().Except(officeBefore));
    }

    private static string CopyFixture(string relativePath, string root, string targetName)
    {
        var source = Path.Combine(PublishDirectory(), "tools", "libreoffice", relativePath);
        Assert.True(File.Exists(source), $"LibreOffice test fixture is missing: {source}");
        var target = Path.Combine(root, targetName);
        File.Copy(source, target);
        return target;
    }

    private static string CopyPresentationFixture(string root)
    {
        var source = Path.Combine(PublishDirectory(), "tools", "libreoffice", "share", "template", "common", "presnt", "Beehive.otp");
        Assert.True(File.Exists(source), $"LibreOffice test fixture is missing: {source}");
        var target = Path.Combine(root, "impress 中文.odp");
        using var input = ZipFile.OpenRead(source);
        using var output = ZipFile.Open(target, ZipArchiveMode.Create);
        var mime = output.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var writer = new StreamWriter(mime.Open(), new UTF8Encoding(false))) writer.Write("application/vnd.oasis.opendocument.presentation");
        foreach (var entry in input.Entries.Where(entry => entry.FullName != "mimetype"))
        {
            var copy = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            using var inputStream = entry.Open();
            using var outputStream = copy.Open();
            inputStream.CopyTo(outputStream);
        }
        return target;
    }

    private static async Task<string> CreateInitializedProfile(string office, string root)
    {
        var profile = Path.Combine(root, "profiles", "fixture");
        Directory.CreateDirectory(profile);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var uri = new Uri(Path.GetFullPath(profile)).AbsoluteUri;
        try
        {
            await ProcessRunner.RunAsync(office,
                [$"-env:UserInstallation={uri}", "--headless", "--invisible", "--nodefault", "--nologo", "--terminate_after_init"], deadline.Token,
                workingDirectory: Path.GetDirectoryName(office));
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException("LibreOffice fixture profile initialization exceeded 30 seconds.", error);
        }
        return profile;
    }

    private static async Task<string> ConvertLibreOffice(string office, string root, string profile, string input, string extension, string filter)
    {
        var outputDirectory = Path.Combine(root, "generated", extension, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        var uri = new Uri(Path.GetFullPath(profile)).AbsoluteUri;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await ProcessRunner.RunAsync(office,
                [$"-env:UserInstallation={uri}", "--headless", "--invisible", "--nodefault", "--nologo", "--nolockcheck", "--convert-to", extension + ":" + filter, "--outdir", outputDirectory, Path.GetFullPath(input)], deadline.Token,
                workingDirectory: Path.GetDirectoryName(office));
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"LibreOffice fixture export to {extension} exceeded 15 seconds.", error);
        }
        var result = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(input) + "." + extension);
        Assert.True(File.Exists(result), $"LibreOffice did not create {extension} from {input}.");
        return result;
    }

    private static string LibreOfficePath() => Path.Combine(PublishDirectory(), "tools", "libreoffice", "program", "soffice.com");

    private static string PublishDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "File Converter Next.sln"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("repository"), "artifacts", "publish-next");
    }

    private static HashSet<int> OfficeProcessIds() => Process.GetProcesses()
        .Where(process => process.ProcessName is "WINWORD" or "EXCEL" or "POWERPNT")
        .Select(process => { try { return process.Id; } finally { process.Dispose(); } })
        .ToHashSet();
}
