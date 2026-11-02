using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileConverterNext.Core;
using FileConverterNext.Windows;
using FileConverterNext.Desktop;
using FileConverterNext.Desktop.ViewModels;
using FileConverterNext.Desktop.Views;
using System.Text.Json;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(FileConverterNext.UiTests.TestApplication))]
namespace FileConverterNext.UiTests;

public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class WindowTests
{
    [Fact]
    public void FailedRegistrySwitchRestoresPreviousHandlerClassesAndExecutable()
    {
        var sandbox = @"Software\FileConverterNext.RegistrationTests\" + Guid.NewGuid().ToString("N");
        using var user = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64);
        try
        {
            using var root = user.CreateSubKey(sandbox);
            const string previousClass = "{61699489-9EEA-43D6-BF5B-32DB32B7A101}";
            const string handler = @"Software\Classes\*\shellex\ContextMenuHandlers\FileConverterNext.Modern";
            const string previousHandler = @"Software\Classes\*\shellex\ContextMenuHandlers\FileConverter.Modern";
            using (var key = root.CreateSubKey(handler)) key.SetValue("", previousClass);
            using (var key = root.CreateSubKey(previousHandler)) key.SetValue("", "previous reconstructed handler");
            using (var key = root.CreateSubKey(@"Software\Classes\CLSID\" + previousClass + @"\InprocServer32")) key.SetValue("", "previous.dll");
            using (var key = root.CreateSubKey(@"Software\FileConverterNext.Modern")) { key.SetValue("Executable", "previous.exe"); key.SetValue("Unrelated", "preserve"); }
            Assert.Throws<IOException>(() => ShellRegistration.RegisterKeys(root, ShellRegistration.AotClassId, "new.dll", "new.exe", () => throw new IOException("Injected registration failure")));
            using var restoredHandler = root.OpenSubKey(handler); Assert.Equal(previousClass, restoredHandler!.GetValue(""));
            using var restoredClass = root.OpenSubKey(@"Software\Classes\CLSID\" + previousClass + @"\InprocServer32"); Assert.Equal("previous.dll", restoredClass!.GetValue(""));
            using var restoredMarker = root.OpenSubKey(@"Software\FileConverterNext.Modern"); Assert.Equal("previous.exe", restoredMarker!.GetValue("Executable")); Assert.Equal("preserve", restoredMarker.GetValue("Unrelated"));
            using var failedClass = root.OpenSubKey(@"Software\Classes\CLSID\" + ShellRegistration.AotClassId); Assert.Null(failedClass);
            using var restoredPreviousHandler = root.OpenSubKey(previousHandler); Assert.Equal("previous reconstructed handler", restoredPreviousHandler!.GetValue(""));
        }
        finally { user.DeleteSubKeyTree(sandbox, false); }
    }
    [Fact]
    public void IncompleteShellRegistrationPreservesExistingMenu()
    {
        var root = Path.Combine(Path.GetTempPath(), "FileConverterNext-registration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var menu = Path.Combine(root, "menu.bin");
            byte[] original = "existing menu fixture"u8.ToArray(); File.WriteAllBytes(menu, original);
            var executable = Path.Combine(root, "FileConverterNext.exe"); File.WriteAllBytes(executable, []);
            Assert.Throws<FileNotFoundException>(() => ShellRegistration.Register(SettingsStore.LoadDefaults(), root, executable, "Settings", root));
            Assert.Equal(original, File.ReadAllBytes(menu));
            // A UI release must still resolve the unchanged, versioned shell payload.
            var shell = Path.Combine(root, "shell", "3.0.5", "FileConverterNext.Shell.NativeAot.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(shell)!); File.WriteAllBytes(shell, []);
            Assert.Equal(shell, ShellRegistration.ValidateRegistration(SettingsStore.LoadDefaults(), root));
            Assert.Equal(original, File.ReadAllBytes(menu));
        }
        finally { Directory.Delete(root, true); }
    }
    private static string Repo => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var screenshots = Path.Combine(Repo, "artifacts", "screenshots");
        Directory.CreateDirectory(screenshots);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap); bitmap.Save(Path.Combine(screenshots, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    [AvaloniaFact]
    public void StartupUsesOriginalHelpSettingsAndConversionWindows()
    {
        Assert.IsType<HelpWindow>(App.CreateStartupWindow(CommandLine.Parse([])));
        Assert.IsType<SettingsWindow>(App.CreateStartupWindow(CommandLine.Parse(["--settings"])));
        Assert.IsType<MainWindow>(App.CreateStartupWindow(CommandLine.Parse(["--conversion-preset", "To Png", "file.jpg"])));
        Assert.IsType<MainWindow>(App.CreateStartupWindow(CommandLine.Parse(["--request", "request.json"])));
        var root = Path.Combine(Path.GetTempPath(), "FileConverterNext-ui-tests", Guid.NewGuid().ToString("N"));
        using var vm = new MainViewModel(new SettingsStore(Path.Combine(root, "settings.json")));
        var progress = new MainWindow { DataContext = vm }; progress.Show();
        try
        {
            vm.Jobs.Add(new JobViewModel(Path.Combine(root, "中文 文件.jpg"), vm.Presets.First(p => p.OutputType == OutputType.Png)) { State = JobState.Done, Progress = 100, Detail = "转换完成" });
            Assert.Empty(progress.GetVisualDescendants().OfType<TabControl>());
            Assert.Empty(progress.GetVisualDescendants().OfType<ComboBox>());
            Capture(progress, "conversion");
            var diagnostics = WindowNavigation.Show<DiagnosticsWindow>(progress);
            Capture(diagnostics, "diagnostics"); diagnostics.Close();
            var upgrade = WindowNavigation.Show<UpgradeWindow>(progress);
            Capture(upgrade, "upgrade"); upgrade.Close();
            var help = WindowNavigation.Show<HelpWindow>(progress);
            Capture(help, "help");
            var helpScroll = help.GetVisualDescendants().OfType<ScrollViewer>().First();
            helpScroll.Offset = new Vector(0, helpScroll.Extent.Height);
            Capture(help, "help-usage");
            help.Width = 640; help.Height = 520; helpScroll.Offset = default;
            Capture(help, "help-small"); help.Close();
            vm.L.Use("en");
            var englishHelp = new HelpWindow { DataContext = vm }; englishHelp.Show();
            Capture(englishHelp, "help-en"); englishHelp.Close();
        }
        finally { progress.Close(); Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public void OriginalSettingsSaveAndCancelPreservePresetEditsAndOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "FileConverterNext-ui-tests", Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(Path.Combine(root, "settings.json"));
        using var vm = new MainViewModel(store);
        var window = new SettingsWindow { DataContext = vm }; window.Show();
        try
        {
            Assert.Equal(76, vm.Presets.Count);
            Assert.Equal(3, window.GetVisualDescendants().OfType<TabControl>().Single().Items.Count);
            for (var page = 0; page < 3; page++) { vm.SelectedTab = page; Capture(window, "settings-" + page); }
            vm.SelectedTab = 0;
            vm.SelectedPreset = vm.Presets.First(p => p.OutputType == OutputType.Png);
            vm.SelectedTreeNode = vm.PresetTree.SelectMany(n => Descendants(n)).First(n => n.Preset == vm.SelectedPreset);
            var id = vm.SelectedPreset.Id;
            vm.PresetLeafName = "测试预设"; vm.PresetTemplate = "(p)(f)-converted";
            var parameter = vm.Parameters.First(p => p.Key == "ImageScale"); parameter.Value = "0.5";
            var jpg = vm.InputCategories.SelectMany(c => c.Extensions).First(e => e.Name == "jpg"); jpg.IsChecked = false;
            var other = vm.Presets.First(p => p.Id != id); vm.SelectedPreset = other;
            Assert.Equal("0.5", vm.Presets.First(p => p.Id == id).Settings["ImageScale"]);
            Assert.DoesNotContain("jpg", vm.Presets.First(p => p.Id == id).InputTypes);
            vm.CreateNewFolder();
            var folder = vm.SelectedTreeNode!;
            var moving = vm.PresetTree.SelectMany(Descendants).First(n => n.Preset?.Id == id);
            vm.MoveTreeItem(moving, folder);
            Assert.Contains(vm.Presets, p => p.Id == id && p.Name.EndsWith("/测试预设"));
            vm.SaveSettingsCommand.Execute(null);
            Assert.False(window.IsVisible);
            var saved = store.Load();
            Assert.Equal(76, saved.Presets.Count);
            Assert.Contains(saved.Presets, p => p.Id == id && p.OutputFileNameTemplate == "(p)(f)-converted");
            var existingCount = vm.Presets.Count;
            var cancel = new SettingsWindow { DataContext = vm }; cancel.Show();
            vm.NewPresetCommand.Execute(null);
            Assert.Equal(OutputType.Mkv, vm.SelectedPreset!.OutputType);
            Assert.Empty(vm.SelectedPreset.InputTypes);
            vm.DuplicatePresetCommand.Execute(null);
            Assert.Equal(existingCount + 2, vm.Presets.Count);
            Assert.Equal(vm.Presets.Count, vm.Presets.Select(p => p.Name).Distinct().Count());
            vm.SelectedPreset = vm.Presets.First(p => p.Id == id); vm.PresetLeafName = "未保存修改";
            cancel.Close();
            Assert.Equal(existingCount, vm.Presets.Count);
            Assert.DoesNotContain(vm.Presets, p => p.Name.EndsWith("未保存修改"));
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    private static IEnumerable<PresetTreeNode> Descendants(PresetTreeNode node) => new[] { node }.Concat(node.Children.SelectMany(Descendants));
    [AvaloniaFact]
    public async Task OriginalUpgradeDownloadsInBackgroundAndDefersInstallerUntilExit()
    {
        var root = Path.Combine(Path.GetTempPath(), "FileConverterNext-ui-tests", Guid.NewGuid().ToString("N"));
        using var vm = new MainViewModel(new SettingsStore(Path.Combine(root, "settings.json")), updateService: new UpdateService(new UpgradeHandler()));
        string? installer = null;
        try
        {
            vm.Settings.Updates.Repository = "acceptance/FileConverterNext";
            await vm.CheckUpdatesCommand.ExecuteAsync(null);
            Assert.True(vm.CanDownloadUpdate);
            var upgrade = new UpgradeWindow { DataContext = vm }; upgrade.Show();
            await vm.DownloadUpdateCommand.ExecuteAsync(null);
            Assert.False(upgrade.IsVisible);
            Assert.True(vm.IsUpdateReady);
            installer = vm.InstallerOnExit;
            Assert.NotNull(installer); Assert.True(File.Exists(installer));
            // The command only closes the update window; no msiexec starts during conversion.
            var ready = new UpgradeWindow { DataContext = vm }; ready.Show();
            Capture(ready, "upgrade-ready"); vm.InstallUpdateCommand.Execute(null);
            Assert.False(ready.IsVisible); Assert.Equal(installer, vm.InstallerOnExit);
            vm.CancelUpdateCommand.Execute(null); Assert.Null(vm.InstallerOnExit);
        }
        finally { if (installer is not null) File.Delete(installer); Directory.Delete(root, true); }
    }
    private sealed class UpgradeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var bytes = "FileConverterNext update test fixture"u8.ToArray();
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            HttpContent content = request.RequestUri!.Host == "api.github.com" ? new StringContent(JsonSerializer.Serialize(new
            {
                tag_name = "v" + new Version(UpdateService.CurrentVersion.Major, UpdateService.CurrentVersion.Minor, UpdateService.CurrentVersion.Build + 1), body = "更新说明", assets = new[] { new { name = "FileConverterNext-win-x64.msi", browser_download_url = "https://example.invalid/update.msi", digest = "sha256:" + digest, size = bytes.Length } }
            })) : new ByteArrayContent(bytes);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }
    [AvaloniaTheory]
    [InlineData(OutputType.Gif)] [InlineData(OutputType.Webp)] [InlineData(OutputType.Jpg)]
    [InlineData(OutputType.Png)] [InlineData(OutputType.Ico)] [InlineData(OutputType.Avif)] [InlineData(OutputType.Pdf)]
    public async Task RightClickRequestImmediatelyConvertsWithoutWorkbench(OutputType output)
    {
        var root = Path.Combine(Path.GetTempPath(), "FileConverterNext-ui-tests", Guid.NewGuid().ToString("N"));
        var worker = Path.Combine(Repo, "artifacts/publish-next/worker/FileConverterNext.Worker.exe");
        using var vm = new MainViewModel(new SettingsStore(Path.Combine(root, "settings.json")), worker);
        vm.Settings.Updates.CheckAtStartup = false;
        vm.Settings.Tools = new()
        {
            FFmpeg = Path.Combine(Repo, "artifacts/publish-next/tools/ffmpeg.exe"),
            FFprobe = Path.Combine(Repo, "artifacts/publish-next/tools/ffprobe.exe"),
            Ghostscript = Path.Combine(Repo, "artifacts/publish-next/tools/ghostscript/gswin64c.exe")
        };
        var window = new MainWindow { DataContext = vm }; window.Show();
        try
        {
            var input = Path.Combine(root, "中文 图片.png"); File.Copy(Path.Combine(Repo, "File Converter Next/Assets/SuccessIcon.png"), input);
            var preset = vm.Presets.First(p => p.OutputType == output && p.InputTypes.Contains("png"));
            var request = Path.Combine(root, "menu-request.json");
            await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new ShellRequest(1, preset.Id.ToString("D"), [input]), AppJson.Options));
            await vm.InitializeAsync(CommandLine.Parse(["--request", request]));
            Assert.Single(vm.Jobs); Assert.Equal(JobState.Done, vm.Jobs[0].State);
            Assert.Single(vm.Jobs[0].Outputs); Assert.True(File.Exists(vm.Jobs[0].Outputs[0])); Assert.True(File.Exists(input));
            Assert.Equal("." + output.ToString().ToLowerInvariant(), Path.GetExtension(vm.Jobs[0].Outputs[0]));
            Capture(window, "conversion-success-" + output);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
}
