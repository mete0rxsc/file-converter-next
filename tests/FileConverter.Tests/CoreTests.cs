using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileConverter.Core;
using FileConverter.Shell.Protocol;
using Xunit;

namespace FileConverter.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FileConverter-tests", Guid.NewGuid().ToString("N"));
    public CoreTests() => Directory.CreateDirectory(root);
    private string PathFor(string name) => Path.Combine(root, name);
    [Fact] public void UpstreamBaselineHasAllOutputsAndPresets()
    {
        var settings = SettingsStore.LoadDefaults();
        Assert.Equal(76, settings.Presets.Count);
        Assert.Equal(17, settings.Presets.Select(p => p.OutputType).Distinct().Count());
        Assert.Contains(settings.Presets, p => p.InputTypes.Contains("cda"));
        Assert.Contains(settings.Presets, p => p.InputTypes.Contains("vob"));
        Assert.Contains(settings.Presets, p => p.InputTypes.Contains("xcf"));
        SettingsStore.Validate(settings);
    }
    [Fact] public void PresetXmlRoundtripPreservesData()
    {
        var original = SettingsStore.LoadDefaults();
        SettingsStore.ExportLegacy(PathFor("presets.xml"), original.Presets);
        var copy = SettingsStore.ReadLegacy(PathFor("presets.xml"));
        Assert.Equal(original.Presets.Count, copy.Presets.Count);
        for (var i = 0; i < copy.Presets.Count; i++)
        {
            Assert.Equal(original.Presets[i].Name, copy.Presets[i].Name);
            Assert.Equal(original.Presets[i].OutputType, copy.Presets[i].OutputType);
            Assert.Equal(original.Presets[i].Settings.OrderBy(p => p.Key), copy.Presets[i].Settings.OrderBy(p => p.Key));
            Assert.Equal(original.Presets[i].InputTypes, copy.Presets[i].InputTypes);
            Assert.Equal(original.Presets[i].OutputFileNameTemplate, copy.Presets[i].OutputFileNameTemplate);
        }
    }
    [Fact] public void MigrationBacksUpLegacyAndPreservesStableIds()
    {
        SettingsStore.ExportLegacy(PathFor("Settings.user.xml"), SettingsStore.LoadDefaults().Presets);
        var store = new SettingsStore(PathFor("settings.json")); var first = store.Load(); var second = store.Load();
        Assert.Single(Directory.GetFiles(root, "*.bak"));
        Assert.Equal(first.Presets.Select(p => p.Id), second.Presets.Select(p => p.Id));
    }
    [Fact] public void BrokenSettingsAreNotOverwritten()
    {
        File.WriteAllText(PathFor("settings.json"), "{broken");
        Assert.ThrowsAny<Exception>(() => new SettingsStore(PathFor("settings.json")).Load());
        Assert.Equal("{broken", File.ReadAllText(PathFor("settings.json")));
    }
    [Fact] public void LegacyXmlRejectsExternalEntities()
    {
        File.WriteAllText(PathFor("evil.xml"), "<!DOCTYPE Settings [<!ENTITY x SYSTEM 'file:///c:/windows/win.ini'>]><Settings>&x;</Settings>");
        Assert.ThrowsAny<Exception>(() => SettingsStore.ReadLegacy(PathFor("evil.xml")));
    }
    [Fact] public void OutputTemplatePreservesCaseNumberDateAndDirectories()
    {
        var output = OutputTemplate.Resolve(PathFor("中文 文件.png"), OutputType.Jpg, "(p)(F)-(I)-(O)-(n:i)-(n:c)-(d:yyyyMMdd)", 2, 3, new DateTime(2026, 11, 1));
        Assert.Equal(PathFor("中文 文件-PNG-JPG-2-3-20261101.jpg"), output);
    }
    [Fact] public void CommitNeverOverwritesInputOrExistingFile()
    {
        var input = PathFor("input.png"); File.WriteAllText(input, "source");
        using var transaction = new OutputTransaction(); File.WriteAllText(transaction.Stage("out.png"), "new");
        var outputs = transaction.Commit([(transaction.Stage("out.png"), input)], input, new Preset(), default);
        Assert.Equal("source", File.ReadAllText(input)); Assert.Equal("new", File.ReadAllText(outputs[0]));
        Assert.NotEqual(input, outputs[0]);
        Assert.EndsWith(" (2).png", outputs[0]);
    }
    [Fact] public void EmptyTemplateUsesOriginalDefaultPath()
    {
        var input = PathFor("中文 文件.png");
        Assert.Equal(PathFor("中文 文件.jpg"), OutputTemplate.Resolve(input, OutputType.Jpg, ""));
    }
    [Fact] public async Task ConcurrentCommitsReserveDifferentPaths()
    {
        var input = PathFor("input"); File.WriteAllText(input, "source");
        var paths = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() =>
        {
            using var transaction = new OutputTransaction();
            File.WriteAllText(transaction.Stage("output"), i.ToString());
            return transaction.Commit([(transaction.Stage("output"), PathFor("same.bin"))], input, new Preset(), default)[0];
        })));
        Assert.Equal(12, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(12, paths.Select(File.ReadAllText).Distinct().Count());
    }
    [Fact] public void FailedMultiOutputRollsBackOnlyOwnedDestinations()
    {
        var input = PathFor("input"); File.WriteAllText(input, "source");
        var blocker = PathFor("blocker"); File.WriteAllText(blocker, "existing");
        using var transaction = new OutputTransaction();
        File.WriteAllText(transaction.Stage("a"), "a"); File.WriteAllText(transaction.Stage("b"), "b");
        Assert.ThrowsAny<IOException>(() => transaction.Commit([(transaction.Stage("a"), PathFor("a.out")), (transaction.Stage("b"), Path.Combine(blocker, "b.out"))], input, new() { InputPostConversionAction = PostAction.Delete }, default));
        Assert.False(File.Exists(PathFor("a.out"))); Assert.Equal("source", File.ReadAllText(input)); Assert.Equal("existing", File.ReadAllText(blocker));
    }
    [Fact] public void CancelledCommitPreservesInput()
    {
        var input = PathFor("input"); File.WriteAllText(input, "source");
        using var transaction = new OutputTransaction(); File.WriteAllText(transaction.Stage("a"), "a");
        Assert.Throws<OperationCanceledException>(() => transaction.Commit([(transaction.Stage("a"), PathFor("a.out"))], input, new() { InputPostConversionAction = PostAction.Delete }, new CancellationToken(true)));
        Assert.True(File.Exists(input)); Assert.False(File.Exists(PathFor("a.out")));
    }
    [Fact] public void PostActionRunsOnlyAfterSuccessfulCommit()
    {
        var input = PathFor("input"); File.WriteAllText(input, "source");
        using var transaction = new OutputTransaction(); File.WriteAllText(transaction.Stage("a"), "a");
        var paths = transaction.Commit([(transaction.Stage("a"), PathFor("a.out"))], input, new() { InputPostConversionAction = PostAction.MoveInArchiveFolder, ConversionArchiveFolderName = "Originals" }, default);
        Assert.Single(paths); Assert.False(File.Exists(input)); Assert.Equal("source", File.ReadAllText(PathFor("Originals/input")));
    }
    [Fact] public void BadArchivePathCannotMoveInputOutsideDirectory()
    {
        var input = PathFor("input"); File.WriteAllText(input, "source");
        using var transaction = new OutputTransaction(); File.WriteAllText(transaction.Stage("a"), "a");
        Assert.Throws<InvalidDataException>(() => transaction.Commit([(transaction.Stage("a"), PathFor("a.out"))], input, new() { InputPostConversionAction = PostAction.MoveInArchiveFolder, ConversionArchiveFolderName = "../elsewhere" }, default));
        Assert.True(File.Exists(input)); Assert.True(File.Exists(PathFor("a.out")));
    }
    [Fact] public void MenuRoundTripFiltersMixedSelectionsAndPreservesOrder()
    {
        var menu = new MenuSnapshot(@"C:\转换\app.exe", "配置", true,
            [new("1", "图片/PNG", ["png"]), new("2", "通用/PDF", ["png", "jpg"]), new("3", "音频/MP3", ["wav"])]);
        var copy = MenuSnapshot.Decode(menu.Encode());
        var selected = copy.Select([@"C:\a.PNG", @"C:\b.jpg", @"C:\other.PNG"]);
        Assert.Equal(2, selected.Count); Assert.False(selected[0].Enabled); Assert.True(selected[1].Enabled);
        Assert.Equal("1", selected[0].Preset.Id); Assert.Equal(menu.Executable, copy.Executable);
        Assert.ThrowsAny<Exception>(() => MenuSnapshot.Decode(menu.Encode()[..^1]));
        Assert.ThrowsAny<Exception>(() => MenuSnapshot.Decode([..menu.Encode(), 0]));
    }
    [Fact] public void UnknownExtensionsDisableOtherwiseRelatedPresets()
    {
        var menu = new MenuSnapshot("app", "settings", false, [new("1", "PNG", ["png"])]);
        Assert.False(Assert.Single(menu.Select(["a.png", "no-extension"])).Enabled);
        Assert.Empty(menu.Select(["a.unknown"]));
    }
    [Fact] public void CommandLineHandlesLegacyAndLongInputList()
    {
        File.WriteAllLines(PathFor("input.txt"), Enumerable.Range(0, 5000).Select(i => $"C:\\中文\\file {i}.mp4"));
        var cmd = CommandLine.Parse(["--conversion-preset", "Folder/MP3", "--verbose", "--input-files", PathFor("input.txt")]);
        Assert.Equal(5000, cmd.Files.Count); Assert.Equal("Folder/MP3", cmd.Preset); Assert.True(cmd.Verbose);
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(["--unknown"]));
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(["--conversion-preset"]));
    }
    [Fact] public void FfmpegUsesSafeArgumentsAndValidEightBitWavCodec()
    {
        var wav = new Preset { OutputType = OutputType.Wav, Settings = new() { ["AudioEncodingMode"] = "Wav8" } };
        var args = FfmpegArguments.Build(wav, @"C:\中文 file.wav", @"C:\out.wav");
        Assert.Contains("pcm_u8", args); Assert.Contains(@"C:\中文 file.wav", args);
        var custom = FfmpegArguments.SplitCommand("-metadata title=\"a b\" -af \"volume=0.5\"");
        Assert.Equal(["-metadata", "title=a b", "-af", "volume=0.5"], custom);
    }
    [Fact] public void H264QualityDirectionMatchesOriginal()
    {
        var p = new Preset { OutputType = OutputType.Mp4, Settings = new() { ["VideoQuality"] = "30" } };
        var args = FfmpegArguments.Build(p, "input", "out.mp4");
        Assert.Equal("21", args[args.IndexOf("-crf") + 1]); Assert.Contains("format=yuv420p", args[args.IndexOf("-vf") + 1]);
    }
    [Fact] public async Task MissingUpdateRepositoryMakesNoNetworkRequest()
    {
        using var update = new UpdateService(new FakeHttp(_ => throw new Exception("unexpected")));
        Assert.Null(await update.CheckAsync("", default));
    }
    [Fact] public async Task ReleaseFeedParsesNotesAndPreventsDowngrade()
    {
        foreach (var tag in new[] { "v3.0.0", "v" + UpdateService.CurrentVersion })
        {
            var current = JsonSerializer.Serialize(new { tag_name = tag, body = "current", assets = Array.Empty<object>() });
            using var same = new UpdateService(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StringContent(current) }));
            Assert.Null(await same.CheckAsync("test/repo", default));
        }
        var newer = """{"tag_name":"v3.1.0","body":"test notes","assets":[{"name":"app-x64.msi","browser_download_url":"https://example.test/app.msi","digest":"sha256:HASH","size":12}]}""".Replace("HASH", new string('a', 64));
        using var update = new UpdateService(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StringContent(newer) }));
        var release = await update.CheckAsync("test/repo", default);
        Assert.NotNull(release); Assert.Equal("test notes", release.Notes); Assert.Equal(new string('a', 64), release.Sha256);
    }
    [Fact] public async Task UpdateDownloadVerifiesDigestAndCleansFailedOrCancelledDownloads()
    {
        var body = Encoding.UTF8.GetBytes("MSI test fixture");
        using var update = new UpdateService(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }));
        var release = new ReleaseInfo("v3.1.0", "notes", new("https://example.test/app.msi"), "app-x64.msi", Convert.ToHexString(SHA256.HashData(body)), body.Length);
        var path = await update.DownloadAsync(release, root, null, default); Assert.Equal(body, File.ReadAllBytes(path));
        var count = Directory.GetFiles(root).Length;
        await Assert.ThrowsAsync<InvalidDataException>(() => update.DownloadAsync(release with { Sha256 = new string('0', 64) }, root, null, default));
        Assert.Equal(count, Directory.GetFiles(root).Length);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update.DownloadAsync(release, root, null, new CancellationToken(true)));
        Assert.Equal(count, Directory.GetFiles(root).Length);
    }
    [Fact] public void AllOriginalLocalesAreAvailable()
    {
        Assert.Contains("zh-CN", Localization.Languages); Assert.Contains("zh-TW", Localization.Languages);
        Assert.Contains("ar-EG", Localization.Languages); Assert.True(Localization.Languages.Length >= 26);
        var l = new Localization(); l.Use("zh-CN"); Assert.Equal("设置", l["Settings"]);
    }
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }
}
