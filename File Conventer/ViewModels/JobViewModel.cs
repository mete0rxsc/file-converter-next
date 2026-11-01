using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileConverter.Core;
using Avalonia.Media;

namespace File_Conventer.ViewModels;

public partial class JobViewModel(string input, Preset preset) : ViewModelBase
{
    public string Input { get; } = input;
    public string FileName => Path.GetFileName(Input);
    public Preset Preset { get; } = preset.Clone();
    public string PresetName => Preset.Name;
    public CancellationTokenSource Cancellation { get; } = new();
    private DateTimeOffset? started;
    [ObservableProperty] private IReadOnlyList<string> outputs = [];
    public string OutputPath
    {
        get { try { return Outputs.FirstOrDefault() ?? OutputTemplate.Resolve(Input, Preset.OutputType, Preset.OutputFileNameTemplate); } catch { return FileName; } }
    }
    public bool CanCancel => State is JobState.Queued or JobState.Running;
    public bool IsDone => State == JobState.Done;
    public bool IsFailed => State is JobState.Failed or JobState.Cancelled;
    public string StateText => State switch { JobState.Queued => "等待转换", JobState.Running => "转换中", JobState.Done => "完成", JobState.Failed => "失败", _ => "已取消" };
    public IBrush StatusBackground => Brush.Parse(State switch { JobState.Done => "#BFFAE8", JobState.Failed => "#FFD4D1", _ => "#E0E0E0" });
    public IBrush StatusForeground => Brush.Parse(State switch { JobState.Done => "#0EAF80", JobState.Failed => "#E91101", _ => "#202020" });
    public string RemainingTime => State == JobState.Running && Progress > 0 && started.HasValue
        ? TimeSpan.FromSeconds(Math.Min(86400, (DateTimeOffset.Now - started.Value).TotalSeconds * (100 - Progress) / Progress)).ToString(@"hh\:mm\:ss") : "";
    partial void OnStateChanged(JobState value)
    {
        if (value == JobState.Running) started = DateTimeOffset.Now;
        foreach (var name in new[] { nameof(CanCancel), nameof(IsDone), nameof(IsFailed), nameof(StateText), nameof(StatusBackground), nameof(StatusForeground), nameof(RemainingTime) }) OnPropertyChanged(name);
    }
    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(RemainingTime));
    partial void OnOutputsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(OutputPath));
    [ObservableProperty] private JobState state = JobState.Queued;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string detail = "等待转换";
    [RelayCommand] private void Cancel()
    {
        if (State is not (JobState.Queued or JobState.Running)) return;
        Cancellation.Cancel();
        if (State == JobState.Queued) { State = JobState.Cancelled; Detail = "已取消"; }
    }
}
