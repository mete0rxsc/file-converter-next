using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileConverterNext.Core;
using FileConverterNext.Windows;

namespace FileConverterNext.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsStore store;
    private readonly string? conversionWorkerPath;
    private readonly UpdateService updater;
    private CancellationTokenSource? updateCancellation, batchCancellation, exitCancellation;
    private ReleaseInfo? release;
    private string? downloadedInstaller;
    public string? InstallerOnExit => InstallUpdateOnExit ? downloadedInstaller : null;
    public bool InstallUpdateOnExit { get; private set; }
    public event Action? CloseUpgradeRequested;
    private bool settingsReadFailed;
    private bool loadingPreset, synchronizingInputs;
    public AppSettings Settings { get; private set; }
    private string? settingsBeforeEdit;
    public event Action? SettingsSaved;
    public void BeginSettingsEdit() => settingsBeforeEdit = JsonSerializer.Serialize(Settings, AppJson.Options);
    public void EndSettingsEdit(bool saved)
    {
        if (!saved && settingsBeforeEdit is not null)
        {
            Settings = JsonSerializer.Deserialize<AppSettings>(settingsBeforeEdit, AppJson.Options)!;
            Presets.Clear(); foreach (var preset in Settings.Presets) Presets.Add(preset);
            SelectedPreset = Presets.FirstOrDefault(); RebuildTree(); OnPropertyChanged(nameof(Settings));
        }
        settingsBeforeEdit = null;
    }
    public Localization L { get; } = new();
    public ObservableCollection<Preset> Presets { get; } = [];
    public ObservableCollection<PresetTreeNode> PresetTree { get; } = [];
    public ObservableCollection<JobViewModel> Jobs { get; } = [];
    public ObservableCollection<PresetParameter> Parameters { get; } = [];
    public ObservableCollection<InputCategory> InputCategories { get; } = [];
    public string[] Languages => Localization.Languages;
    public OutputType[] OutputTypes => Enum.GetValues<OutputType>();
    public PostAction[] PostActions => Enum.GetValues<PostAction>();
    public HardwareMode[] HardwareModes => Enum.GetValues<HardwareMode>();
    public string SettingsLocation => store.FilePath;
    public string VersionLabel => "File Converter Next " + UpdateService.CurrentVersion;
    public string AllExtensions => string.Join(' ', Formats.Inputs);
    public event Action? RequestClose;
    public event Action? UpgradeAvailable;
    public event Action<ulong, ulong, uint>? TaskbarChanged;
    public Func<IReadOnlyList<string>, Task>? CopyFilesToClipboard { get; set; }
    [ObservableProperty] private Preset? selectedPreset;
    [ObservableProperty] private PresetTreeNode? selectedTreeNode;
    [ObservableProperty] private string status = "就绪";
    [ObservableProperty] private string diagnostics = "";
    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private string presetName = "";
    [ObservableProperty] private OutputType presetOutput = OutputType.Png;
    [ObservableProperty] private string presetInputs = "";
    [ObservableProperty] private string presetTemplate = "(p)(f)";
    [ObservableProperty] private PostAction presetPostAction;
    [ObservableProperty] private string presetArchive = "Originals";
    [ObservableProperty] private string parameterJson = "{}";
    [ObservableProperty] private string folderName = "";
    [ObservableProperty] private string folderNewName = "";
    [ObservableProperty] private string previewOutput = "";
    [ObservableProperty] private string updateStatus = "尚未配置更新仓库";
    [ObservableProperty] private string releaseNotes = "";
    [ObservableProperty] private double downloadProgress;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isUpdating;
    [ObservableProperty] private bool isUpdateDownloading;
    [ObservableProperty] private bool isUpdateReady;
    public bool CanDownloadUpdate => release is not null && !IsUpdating && !IsUpdateReady;
    partial void OnIsUpdatingChanged(bool value) => OnPropertyChanged(nameof(CanDownloadUpdate));
    partial void OnIsUpdateReadyChanged(bool value) => OnPropertyChanged(nameof(CanDownloadUpdate));
    [ObservableProperty] private string presetLeafName = "";
    [ObservableProperty] private string customInputExtensions = "";
    public bool IsFolderSelected => SelectedTreeNode?.IsFolder == true;
    public bool IsPresetSelected => !IsFolderSelected && SelectedPreset is not null;
    public bool IsNothingSelected => !IsFolderSelected && SelectedPreset is null;
    public bool IsArchiveAction => PresetPostAction == PostAction.MoveInArchiveFolder;
    public string FolderLeafName
    {
        get => FolderNewName.Replace('\\', '/').Split('/')[^1];
        set { var slash = FolderName.LastIndexOf('/'); FolderNewName = (slash < 0 ? "" : FolderName[..(slash + 1)]) + value; }
    }
    partial void OnFolderNewNameChanged(string value) => OnPropertyChanged(nameof(FolderLeafName));
    public PostActionOption[] PostActionOptions => [
        new(PostAction.None, L["InputPostConversionActionNoneName"]),
        new(PostAction.MoveInArchiveFolder, L["InputPostConversionActionMoveInArchiveFolderName"]),
        new(PostAction.Delete, L["InputPostConversionActionDeleteName"])];
    public PostActionOption? SelectedPostAction
    {
        get => PostActionOptions.First(p => p.Action == PresetPostAction);
        set { if (value is not null) PresetPostAction = value.Action; }
    }

    public MainViewModel(SettingsStore? settingsStore = null, string? conversionWorkerPath = null, UpdateService? updateService = null)
    {
        updater = updateService ?? new();
        this.conversionWorkerPath = conversionWorkerPath;
        store = settingsStore ?? new();
        try { Settings = store.Load(); }
        catch (Exception error)
        {
            settingsReadFailed = true;
            Settings = SettingsStore.LoadDefaults();
            Status = "设置读取失败，使用内存默认值，原文件保留：" + error.Message;
            Log(error.ToString());
        }
        L.Use(Settings.Language);
        foreach (var p in Settings.Presets) Presets.Add(p);
        SelectedPreset = Presets.FirstOrDefault(); RebuildTree(); ApplyAppearance();
        Log(VersionLabel + " / .NET " + Environment.Version + "\n" + SettingsLocation);
    }
    public async Task InitializeAsync(CommandLine command)
    {
        try
        {
            if (command.RequestFile is { } path)
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 32 * 1024 * 1024) throw new InvalidDataException("菜单请求不存在或过大。");
                var request = JsonSerializer.Deserialize<ShellRequest>(await File.ReadAllTextAsync(path), AppJson.Options) ?? throw new InvalidDataException("菜单请求无效。");
                if (request.Version != 1) throw new InvalidDataException("菜单请求协议不匹配。");
                SelectedPreset = Presets.FirstOrDefault(p => p.Id.ToString("D") == request.Preset || p.Name == request.Preset)
                    ?? throw new InvalidDataException("预设已不存在，请重新打开右键菜单。");
                AddFiles(request.Files);
                if (Path.GetFullPath(path).StartsWith(AppPaths.RequestsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            }
            else
            {
                if (command.Preset is not null) SelectedPreset = Presets.FirstOrDefault(p => p.Name == command.Preset) ?? throw new ArgumentException("找不到预设：" + command.Preset);
                AddFiles(command.Files);
            }
            if (Jobs.Count > 0 && !command.Settings) await StartAsync();
            if (Settings.Updates.CheckAtStartup && !string.IsNullOrWhiteSpace(Settings.Updates.Repository)) await CheckUpdatesAsync();
        }
        catch (Exception error) { Error(error); }
    }
    partial void OnSelectedTreeNodeChanged(PresetTreeNode? value)
    {
        OnPropertyChanged(nameof(IsFolderSelected)); OnPropertyChanged(nameof(IsPresetSelected)); OnPropertyChanged(nameof(IsNothingSelected));
        if (value?.Preset is { } p) SelectedPreset = p;
        if (value?.IsFolder == true) { FolderName = value.FullName; FolderNewName = value.FullName; }
    }
    partial void OnSelectedPresetChanged(Preset? value)
    {
        var p = value;
        if (p is not null && FlattenTree(PresetTree).FirstOrDefault(n => n.Preset == p) is { } node) SelectedTreeNode = node;
        OnPropertyChanged(nameof(IsPresetSelected)); OnPropertyChanged(nameof(IsNothingSelected));
        if (p is null) return;
        loadingPreset = true;
        PresetName = p.Name; PresetOutput = p.OutputType; PresetInputs = string.Join(' ', p.InputTypes);
        PresetTemplate = p.OutputFileNameTemplate; PresetPostAction = p.InputPostConversionAction; PresetArchive = p.ConversionArchiveFolderName;
        PresetLeafName = p.Name.Replace('\\', '/').Split('/')[^1];
        CustomInputExtensions = string.Join(' ', p.InputTypes.Where(e => !Formats.Inputs.Contains(e)));
        ParameterJson = JsonSerializer.Serialize(p.Settings, AppJson.Options);
        LoadParameters(p.Settings); RefreshPreview();
        loadingPreset = false;
        RebuildInputCategories();
    }
    partial void OnPresetLeafNameChanged(string value)
    {
        if (loadingPreset || SelectedPreset is null) return;
        var path = PresetName.Replace('\\', '/'); var slash = path.LastIndexOf('/');
        PresetName = (slash < 0 ? "" : path[..(slash + 1)]) + value;
        SelectedPreset.Name = PresetName;
        if (SelectedTreeNode?.Preset == SelectedPreset) SelectedTreeNode.Name = value;
    }
    partial void OnPresetTemplateChanged(string value) { if (!loadingPreset && SelectedPreset is not null) SelectedPreset.OutputFileNameTemplate = value; RefreshPreview(); }
    partial void OnPresetPostActionChanged(PostAction value)
    {
        if (!loadingPreset && SelectedPreset is not null) SelectedPreset.InputPostConversionAction = value;
        OnPropertyChanged(nameof(SelectedPostAction)); OnPropertyChanged(nameof(IsArchiveAction));
    }
    partial void OnPresetArchiveChanged(string value) { if (!loadingPreset && SelectedPreset is not null) SelectedPreset.ConversionArchiveFolderName = value; }
    partial void OnCustomInputExtensionsChanged(string value)
    {
        if (loadingPreset || SelectedPreset is null) return;
        var extensions = value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries).Select(e => e.TrimStart('.').ToLowerInvariant()).Where(e => !Formats.Inputs.Contains(e));
        SelectedPreset.InputTypes = SelectedPreset.InputTypes.Where(Formats.Inputs.Contains).Concat(extensions).Distinct().ToList();
        PresetInputs = string.Join(' ', SelectedPreset.InputTypes);
    }
    partial void OnPresetOutputChanged(OutputType value)
    {
        if (!loadingPreset && SelectedPreset is not null && SelectedPreset.OutputType != value)
        {
            SelectedPreset.OutputType = value;
            SelectedPreset.Settings = PresetDefaults.For(value);
            SelectedPreset.InputTypes.RemoveAll(e => !Formats.CanConvert(e, value));
            LoadParameters(SelectedPreset.Settings); RebuildInputCategories();
        }
        RefreshPreview();
    }
    private void LoadParameters(Dictionary<string, string> parameters)
    {
        Parameters.Clear();
        foreach (var (key, value) in parameters)
        {
            var label = key switch
            {
                "AudioEncodingMode" => L["Encoding"], "AudioBitrate" => L["Quality"], "AudioChannelCount" => L["ChannelCountTitle"],
                "ImageQuality" or "VideoQuality" => L["Quality"], "ImageScale" or "VideoScale" => L["Scale"],
                "ImageRotation" or "VideoRotation" => L["Rotate"], "ImageClampSizePowerOf2" => L["ClampToLowestPowerOfTwoSize"],
                "ImageMaximumSize" => L.Translate("MaximumSize", "最大尺寸", "Maximum size"), "VideoEncodingSpeed" => L["EncodingSpeed"], "VideoFramesPerSecond" => L["FramesPerSecond"],
                "EnableAudio" => L["AudioTitle"], "EnableVideo" => L["VideoTitle"], "EnableFFMPEGCustomCommand" => L["AdvancedMode"],
                "FFMPEGCustomCommand" => L["FFMPEGCustomArguments"], _ => key
            };
            var options = key switch
            {
                "AudioEncodingMode" => PresetOutput == OutputType.Wav ? new[] { "Wav8", "Wav16", "Wav24", "Wav32" } : ["Mp3VBR", "Mp3CBR"],
                "VideoEncodingSpeed" => ["UltraFast", "SuperFast", "VeryFast", "Faster", "Fast", "Medium", "Slow", "Slower", "VerySlow"],
                _ when bool.TryParse(value, out _) => ["True", "False"],
                _ => Array.Empty<string>()
            };
            var parameter = new PresetParameter { Key = key, Label = label, Value = value, Options = options };
            parameter.Configure(L, PresetOutput, parameters.GetValueOrDefault("AudioEncodingMode", ""));
            parameter.PropertyChanged += (_, args) => {
                if (!loadingPreset && args.PropertyName == nameof(PresetParameter.Value) && SelectedPreset is not null)
                {
                    SelectedPreset.Settings[key] = parameter.Value;
                    if (key == "AudioEncodingMode") foreach (var p in Parameters.Where(p => p.Key == "AudioBitrate")) p.SetAudioMode(parameter.Value);
                    RefreshParameterVisibility();
                }
            };
            Parameters.Add(parameter);
        }
        RefreshParameterVisibility();
    }
    private void RefreshParameterVisibility()
    {
        var advanced = Parameters.FirstOrDefault(p => p.Key == "EnableFFMPEGCustomCommand")?.FlagValue == true;
        var audio = Parameters.FirstOrDefault(p => p.Key == "EnableAudio")?.FlagValue != false;
        foreach (var parameter in Parameters)
        {
            parameter.IsVisible = parameter.Key == "EnableFFMPEGCustomCommand" || (parameter.Key == "FFMPEGCustomCommand" ? advanced : !advanced);
            parameter.IsEnabled = !parameter.Key.StartsWith("Audio", StringComparison.Ordinal) || audio;
        }
    }
    private void RebuildInputCategories()
    {
        synchronizingInputs = true; InputCategories.Clear();
        foreach (var (key, chinese, english, extensions) in new[] {
            ("StringAudioName", "音频", "Audio", Formats.Audio), ("StringVideoName", "视频", "Video", Formats.Video),
            ("StringImageName", "图片", "Images", Formats.Images), ("StringDocumentName", "文档", "Documents", Formats.Documents), ("StringAnimatedImageName", "动画", "Animation", new[] { "gif" }) })
        {
            var category = new InputCategory(L.Translate(key, chinese, english));
            foreach (var extension in extensions)
            {
                var item = new InputExtension(extension, Formats.CanConvert(extension, PresetOutput), () => {
                    if (synchronizingInputs || SelectedPreset is null) return;
                    var selected = InputCategories.SelectMany(c => c.Extensions).Where(e => e.IsChecked && e.IsEnabled).Select(e => e.Name);
                    SelectedPreset.InputTypes = selected.Concat(SelectedPreset.InputTypes.Where(e => !Formats.Inputs.Contains(e))).Distinct().ToList();
                    PresetInputs = string.Join(' ', SelectedPreset.InputTypes); category.Refresh();
                }) { IsChecked = SelectedPreset?.InputTypes.Contains(extension) == true };
                category.Extensions.Add(item);
            }
            category.Refresh(); InputCategories.Add(category);
        }
        synchronizingInputs = false;
        if (SelectedPreset is not null) PresetInputs = string.Join(' ', SelectedPreset.InputTypes);
    }
    private void RefreshPreview()
    {
        try { PreviewOutput = OutputTemplate.Resolve(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Example.png"), PresetOutput, PresetTemplate); }
        catch (Exception e) { PreviewOutput = e.Message; }
    }
    public void AddFiles(IEnumerable<string> paths)
    {
        if (SelectedPreset is null) return;
        foreach (var path in paths)
            if (File.Exists(path))
            {
                var job = new JobViewModel(Path.GetFullPath(path), SelectedPreset);
                job.PropertyChanged += (_, _) => UpdateTaskbar(); Jobs.Add(job);
            }
            else Log("不存在：" + path);
        Status = $"{Jobs.Count} 个任务";
    }
    private void UpdateTaskbar()
    {
        var active = Jobs.Any(j => j.State is JobState.Queued or JobState.Running);
        var state = Jobs.Any(j => j.State == JobState.Failed) ? 4u : active ? 2u : 0u;
        TaskbarChanged?.Invoke((ulong)Jobs.Sum(j => j.Progress * 100), (ulong)Math.Max(1, Jobs.Count) * 10000, state);
    }
    [RelayCommand] private void ClearFinished()
    {
        foreach (var j in Jobs.Where(j => j.State is JobState.Done or JobState.Failed or JobState.Cancelled).ToArray()) Jobs.Remove(j);
    }
    [RelayCommand] private void CancelAll()
    {
        CancelAutoExit(); batchCancellation?.Cancel(); foreach (var j in Jobs) j.CancelCommand.Execute(null);
    }
    [RelayCommand] private void CancelAutoExit() { exitCancellation?.Cancel(); Status = "已取消自动退出"; }
    [RelayCommand] private async Task StartAsync()
    {
        if (IsRunning) return;
        var queued = Jobs.Where(j => j.State == JobState.Queued).ToArray(); if (queued.Length == 0) return;
        IsRunning = true; batchCancellation = new(); CancelAutoExit();
        try
        {
            PersistSettings();
            var worker = conversionWorkerPath ?? Path.Combine(AppContext.BaseDirectory, "worker", "FileConverterNext.Worker.exe");
            if (!File.Exists(worker)) throw new FileNotFoundException("找不到转换工作进程，请构建或安装完整发行包。", worker);
            using var limit = new SemaphoreSlim(Settings.MaximumNumberOfSimultaneousConversions);
            var client = new ConversionClient(worker);
            await Task.WhenAll(queued.Select(async job =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.Cancellation.Token, batchCancellation.Token);
                var entered = false;
                try
                {
                    await limit.WaitAsync(linked.Token); entered = true; job.State = JobState.Running; job.Detail = "转换中";
                    job.Outputs = await client.ConvertAsync(job.Input, job.Preset, Settings,
                        new Progress<WorkerMessage>(m => { job.Progress = m.Progress * 100; job.Detail = m.Message ?? ""; }), linked.Token);
                    job.Progress = 100; job.State = JobState.Done; job.Detail = string.Join(Environment.NewLine, job.Outputs); Log("完成：" + job.Input + "\n" + job.Detail);
                }
                catch (OperationCanceledException) { job.State = JobState.Cancelled; job.Detail = "已取消，源文件保留"; }
                catch (Exception error) { job.State = JobState.Failed; job.Detail = error.Message; Log(error.ToString()); }
                finally { if (entered) limit.Release(); }
            }));
            var outputs = queued.SelectMany(j => j.Outputs).ToArray();
            if (Settings.CopyFilesInClipboardAfterConversion && outputs.Length > 0 && CopyFilesToClipboard is not null) await CopyFilesToClipboard(outputs);
            Status = $"完成 {queued.Count(j => j.State == JobState.Done)} / {queued.Length}";
            if (Settings.ExitApplicationWhenConversionsFinished && queued.All(j => j.State == JobState.Done) && Jobs.All(j => j.State != JobState.Queued)) await AutoExitAsync();
        }
        catch (Exception error) { Error(error); }
        finally { IsRunning = false; batchCancellation.Dispose(); batchCancellation = null; }
    }
    private async Task AutoExitAsync()
    {
        exitCancellation = new();
        try
        {
            for (var n = Settings.DurationBetweenEndOfConversionsAndApplicationExit; n > 0; n -= .1)
            { Status = $"{n:0.0} 秒后退出"; await Task.Delay(100, exitCancellation.Token); }
            RequestClose?.Invoke();
        }
        catch (OperationCanceledException) { }
        finally { exitCancellation.Dispose(); exitCancellation = null; }
    }
    [RelayCommand] private void SavePreset()
    {
        try
        {
            if (SelectedPreset is null) return;
            var parameters = Parameters.ToDictionary(p => p.Key, p => p.Value);
            var original = SelectedPreset; var candidate = original.Clone(); candidate.Id = original.Id;
            candidate.Name = PresetName.Trim(); candidate.OutputType = PresetOutput;
            candidate.InputTypes = PresetInputs.Split([' ', ',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(e => e.TrimStart('.').ToLowerInvariant()).Distinct().ToList();
            candidate.InputTypes.RemoveAll(extension => !Formats.CanConvert(extension, candidate.OutputType));
            candidate.Settings = parameters; candidate.OutputFileNameTemplate = PresetTemplate; candidate.InputPostConversionAction = PresetPostAction; candidate.ConversionArchiveFolderName = PresetArchive;
            var index = Presets.IndexOf(original); Presets[index] = candidate;
            try { PersistSettings(); } catch { Presets[index] = original; throw; }
            SelectedPreset = candidate; RebuildTree(); Status = "预设已保存";
        }
        catch (Exception error) { Error(error); }
    }
    [RelayCommand] private void NewPreset()
    {
        AddPreset(false);
    }
    [RelayCommand] private void DuplicatePreset()
    {
        if (!IsPresetSelected) return;
        AddPreset(true);
    }
    private void AddPreset(bool duplicate)
    {
        var parent = IsFolderSelected ? SelectedTreeNode!.FullName : SelectedPreset?.Name.Contains('/') == true ? SelectedPreset.Name[..SelectedPreset.Name.LastIndexOf('/')] : "";
        var label = L["DefaultPresetName"]; var name = string.IsNullOrEmpty(parent) ? label : parent + "/" + label;
        var baseName = name; var number = 2;
        while (Presets.Any(p => p.Name == name)) name = baseName + " (" + number++ + ")";
        var created = duplicate && SelectedPreset is not null ? SelectedPreset.Clone() : new Preset { OutputType = OutputType.Mkv, Settings = PresetDefaults.For(OutputType.Mkv) };
        created.Name = name;
        var selected = SelectedPreset; var folderSelected = IsFolderSelected;
        var index = !folderSelected && selected is not null ? Presets.IndexOf(selected) + 1
            : Presets.Select((p, i) => (p, i)).Where(pair => pair.p.Name.StartsWith(parent + "/", StringComparison.Ordinal)).Select(pair => pair.i + 1).DefaultIfEmpty(Presets.Count).Max();
        Presets.Insert(Math.Clamp(index, 0, Presets.Count), created);
        SelectedTreeNode = null; SelectedPreset = created; RebuildTree(); SelectedTab = 0;
    }
    [RelayCommand] private void RemovePreset()
    {
        if (SelectedPreset is null) return; Presets.Remove(SelectedPreset); SelectedPreset = Presets.FirstOrDefault(); RebuildTree();
    }
    [RelayCommand] private void MovePreset(string? direction)
    {
        if (SelectedPreset is null) return;
        var from = Presets.IndexOf(SelectedPreset); var to = from + (direction == "up" ? -1 : 1);
        if (to >= 0 && to < Presets.Count) Presets.Move(from, to); RebuildTree();
    }
    [RelayCommand] private void CreateFolder()
    {
        if (string.IsNullOrWhiteSpace(FolderName)) return;
        if (!Settings.Folders.Contains(FolderName)) Settings.Folders.Add(FolderName.Replace('\\', '/')); RebuildTree();
    }
    public void CreateNewFolder()
    {
        var parent = IsFolderSelected ? SelectedTreeNode!.FullName : SelectedPreset?.Name.Contains('/') == true ? SelectedPreset.Name[..SelectedPreset.Name.LastIndexOf('/')] : "";
        var name = L.Translate("DefaultFolderName", "新文件夹", "New folder");
        var basePath = string.IsNullOrEmpty(parent) ? name : parent + "/" + name;
        var path = basePath; var number = 2;
        while (Settings.Folders.Contains(path)) path = basePath + " (" + number++ + ")";
        FolderName = path; FolderNewName = path; Settings.Folders.Add(path); RebuildTree();
        SelectedTreeNode = FlattenTree(PresetTree).First(n => n.IsFolder && n.FullName == path);
    }
    public void DeleteSelectedFolder()
    {
        if (!IsFolderSelected) return;
        var path = SelectedTreeNode!.FullName; var prefix = path + "/";
        foreach (var preset in Presets.Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) Presets.Remove(preset);
        Settings.Folders.RemoveAll(f => f == path || f.StartsWith(prefix, StringComparison.Ordinal));
        SelectedPreset = null; SelectedTreeNode = null; RebuildTree();
    }
    private static IEnumerable<PresetTreeNode> FlattenTree(IEnumerable<PresetTreeNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(FlattenTree(n.Children)));
    public void MoveTreeItem(PresetTreeNode source, PresetTreeNode? target)
    {
        if (source == target || target is not null && FlattenTree(source.Children).Contains(target)) return;
        ObservableCollection<PresetTreeNode> ParentOf(PresetTreeNode node) =>
            FlattenTree(PresetTree).FirstOrDefault(n => n.Children.Contains(node))?.Children ?? PresetTree;
        var destination = target?.IsFolder == true ? target.Children : target is null ? PresetTree : ParentOf(target);
        if (destination.Any(n => n != source && n.Name == source.Name)) { Status = L.Translate("DuplicateName", "目标中存在同名项目。", "The destination already contains an item with this name."); return; }
        ParentOf(source).Remove(source);
        destination.Insert(target is not null && !target.IsFolder ? destination.IndexOf(target) : destination.Count, source);
        var ordered = new List<Preset>(); Settings.Folders.Clear();
        void Walk(IEnumerable<PresetTreeNode> nodes, string parent)
        {
            foreach (var node in nodes)
            {
                var path = string.IsNullOrEmpty(parent) ? node.Name : parent + "/" + node.Name;
                if (node.Preset is { } preset) { preset.Name = path; ordered.Add(preset); }
                else { Settings.Folders.Add(path); Walk(node.Children, path); }
            }
        }
        Walk(PresetTree, "");
        Presets.Clear(); foreach (var preset in ordered) Presets.Add(preset);
        SelectedTreeNode = null;
        RebuildTree();
    }
    [RelayCommand] private void RenameFolder()
    {
        if (string.IsNullOrWhiteSpace(FolderName) || string.IsNullOrWhiteSpace(FolderNewName)) return;
        var old = FolderName.TrimEnd('/') + "/"; var next = FolderNewName.TrimEnd('/') + "/";
        foreach (var p in Presets.Where(p => p.Name.StartsWith(old, StringComparison.Ordinal))) p.Name = next + p.Name[old.Length..];
        Settings.Folders = Settings.Folders.Select(f => f == FolderName ? FolderNewName : f.StartsWith(old) ? next + f[old.Length..] : f).ToList();
        FolderName = FolderNewName; RebuildTree();
        SelectedTreeNode = FlattenTree(PresetTree).FirstOrDefault(n => n.IsFolder && n.FullName == FolderName);
    }
    [RelayCommand] private void DeleteFolder()
    {
        if (string.IsNullOrWhiteSpace(FolderName)) return;
        var prefix = FolderName.TrimEnd('/') + "/"; var parent = FolderName.Contains('/') ? FolderName[..(FolderName.LastIndexOf('/') + 1)] : "";
        foreach (var p in Presets.Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal))) p.Name = parent + p.Name[prefix.Length..];
        Settings.Folders.RemoveAll(f => f == FolderName || f.StartsWith(prefix)); RebuildTree();
    }
    public void Import(string path)
    {
        try { foreach (var p in SettingsStore.ReadLegacy(path).Presets) { p.Id = Guid.NewGuid(); if (Presets.Any(x => x.Name == p.Name)) p.Name += " (导入)"; Presets.Add(p); } RebuildTree(); Status = "导入完成，请保存设置"; }
        catch (Exception e) { Error(e); }
    }
    public void Export(string path, bool all)
    {
        try
        {
            IEnumerable<Preset> selection = all ? Presets : SelectedTreeNode?.IsFolder == true
                ? Presets.Where(p => p.Name.StartsWith(SelectedTreeNode.FullName.TrimEnd('/') + "/", StringComparison.Ordinal))
                : SelectedPreset is null ? [] : [SelectedPreset];
            SettingsStore.ExportLegacy(path, selection); Status = "导出完成";
        }
        catch (Exception e) { Error(e); }
    }
    public void RebuildTree()
    {
        var selectedFolder = SelectedTreeNode?.IsFolder == true ? SelectedTreeNode.FullName : null;
        PresetTree.Clear(); var folders = new Dictionary<string, PresetTreeNode>(StringComparer.Ordinal);
        ObservableCollection<PresetTreeNode> Ensure(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return PresetTree;
            if (folders.TryGetValue(path, out var found)) return found.Children;
            var i = path.LastIndexOf('/'); var parent = Ensure(i < 0 ? "" : path[..i]);
            var node = new PresetTreeNode { Name = i < 0 ? path : path[(i + 1)..], FullName = path };
            parent.Add(node); folders[path] = node; return node.Children;
        }
        foreach (var p in Presets) { var path = p.Name.Replace('\\', '/'); var i = path.LastIndexOf('/'); Ensure(i < 0 ? "" : path[..i]).Add(new() { Name = i < 0 ? path : path[(i + 1)..], FullName = path, Preset = p }); }
        foreach (var folder in Settings.Folders) Ensure(folder.Replace('\\', '/'));
        SelectedTreeNode = FlattenTree(PresetTree).FirstOrDefault(n => selectedFolder is not null ? n.IsFolder && n.FullName == selectedFolder : SelectedPreset is not null && n.Preset == SelectedPreset);
    }
    [RelayCommand] private void SaveSettings()
    {
        try
        {
            if (settingsReadFailed && File.Exists(store.FilePath))
                File.Copy(store.FilePath, store.FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak", false);
            settingsReadFailed = false;
            if (IsFolderSelected) RenameFolder();
            PersistSettings();
            SettingsSaved?.Invoke();
        }
        catch (Exception error) { Error(error); }
    }
    private void PersistSettings(bool updateMenu = true)
    {
        if (settingsReadFailed) throw new InvalidDataException("原设置文件无法读取。请修复文件，或点击保存设置以备份原文件并保存当前设置。");
        Settings.Presets = Presets.ToList(); store.Save(Settings); L.Use(Settings.Language); ApplyAppearance(); OnPropertyChanged(nameof(L));
        try { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Settings.Language); } catch (CultureNotFoundException) { }
        if (updateMenu && store.FilePath == AppPaths.SettingsFile) ShellRegistration.WriteSnapshot(Settings, ShellRegistration.RegisteredExecutableOr(Environment.ProcessPath!), L["Settings"]);
        Status = "已保存：" + SettingsLocation;
    }
    private void ApplyAppearance()
    {
        if (Application.Current is not null) Application.Current.RequestedThemeVariant = ThemeVariant.Light;
    }
    [RelayCommand] private void ApplyShell()
    {
        try
        {
            ShellRegistration.ValidateRegistration(Settings, AppContext.BaseDirectory);
            PersistSettings(false);
            ShellRegistration.Register(Settings, AppContext.BaseDirectory, Environment.ProcessPath!, L["Settings"]);
            Status = "菜单模式已应用。";
        }
        catch (Exception e) { Error(e); }
    }
    [RelayCommand] private void UnregisterShell()
    {
        try { ShellRegistration.Unregister(); Status = "菜单已注销。已加载 DLL 将在宿主退出后释放。"; } catch (Exception e) { Error(e); }
    }
    [RelayCommand] private async Task CheckUpdatesAsync()
    {
        if (IsUpdating) return; IsUpdating = true; updateCancellation = new();
        try
        {
            release = await updater.CheckAsync(Settings.Updates.Repository, updateCancellation.Token);
            if (release is null) { UpdateStatus = string.IsNullOrWhiteSpace(Settings.Updates.Repository) ? "请在 Updates.Repository 填写 owner/repo。" : "当前版本已是最新版本"; return; }
            ReleaseNotes = release.Notes; OnPropertyChanged(nameof(CanDownloadUpdate)); UpgradeAvailable?.Invoke();
            UpdateStatus = "发现版本：" + release.Tag;
        }
        catch (OperationCanceledException) { UpdateStatus = "已取消"; }
        catch (Exception e) { UpdateStatus = e.Message; Log(e.ToString()); }
        finally { IsUpdating = false; updateCancellation.Dispose(); updateCancellation = null; }
    }
    [RelayCommand] private async Task DownloadUpdateAsync()
    {
        if (release is null || IsUpdating) return;
        IsUpdating = IsUpdateDownloading = true; InstallUpdateOnExit = true; updateCancellation = new();
        CloseUpgradeRequested?.Invoke();
        try
        {
            downloadedInstaller = await updater.DownloadAsync(release, Path.Combine(AppPaths.DataDirectory, "Updates"), new Progress<double>(p => DownloadProgress = p * 100), updateCancellation.Token);
            IsUpdateReady = true; UpdateStatus = L["InstallButtonDescription"];
        }
        catch (OperationCanceledException) { InstallUpdateOnExit = false; UpdateStatus = "下载已取消"; }
        catch (Exception e) { InstallUpdateOnExit = false; UpdateStatus = e.Message; Log(e.ToString()); }
        finally { IsUpdating = IsUpdateDownloading = false; updateCancellation.Dispose(); updateCancellation = null; }
    }
    [RelayCommand] private void CancelUpdate() { InstallUpdateOnExit = false; updateCancellation?.Cancel(); }
    [RelayCommand] private void InstallUpdate()
    {
        if (downloadedInstaller is null) return;
        InstallUpdateOnExit = true; CloseUpgradeRequested?.Invoke();
    }
    [RelayCommand] private void OpenHelp() => OpenUrl(AppIdentity.RepositoryUrl);
    [RelayCommand] private void OpenTemplateHelp() => OpenUrl(AppIdentity.RepositoryUrl);
    [RelayCommand] private void OpenProject() => OpenUrl(AppIdentity.RepositoryUrl);
    private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); } catch (Exception e) { Error(e); } }
    public void Error(Exception error) { Status = error.Message; Log(error.ToString()); }
    private void Log(string text)
    {
        var line = DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine; Diagnostics += line;
        try { Directory.CreateDirectory(AppPaths.LogsDirectory); File.AppendAllText(Path.Combine(AppPaths.LogsDirectory, DateTime.Now.ToString("yyyyMMdd") + ".log"), line); } catch (IOException) { }
    }
    public void Dispose()
    {
        batchCancellation?.Cancel(); exitCancellation?.Cancel(); updateCancellation?.Cancel(); foreach (var j in Jobs) j.Cancellation.Cancel(); updater.Dispose();
    }
}
