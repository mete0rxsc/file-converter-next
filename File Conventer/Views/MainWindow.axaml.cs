using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using File_Conventer.ViewModels;
using FileConverter.Windows;

namespace File_Conventer.Views;

public partial class MainWindow : Window
{
    private TaskbarProgress? taskbar;
    private MainViewModel? model;
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            model = vm; vm.RequestClose += Close;
            if (TryGetPlatformHandle() is { Handle: not 0 } handle)
            {
                taskbar = new(); vm.TaskbarChanged += UpdateTaskbar;
            }
            vm.CopyFilesToClipboard = async paths =>
            {
                if (Clipboard is null) return;
                var data = new DataTransfer();
                foreach (var path in paths)
                    if (await StorageProvider.TryGetFileFromPathAsync(path) is { } file) data.Add(DataTransferItem.CreateFile(file));
                await Clipboard.SetDataAsync(data);
            };
        };
        Closing += (_, _) => model?.CancelAllCommand.Execute(null);
        Closed += (_, _) =>
        {
            if (model is not null) { model.RequestClose -= Close; model.TaskbarChanged -= UpdateTaskbar; }
            taskbar?.Dispose();
        };
    }
    private void UpdateTaskbar(ulong completed, ulong total, uint state)
    {
        if (TryGetPlatformHandle() is { Handle: not 0 } handle) taskbar?.Set(handle.Handle, completed, total, state);
    }
    private void ShowSettings(object? sender, RoutedEventArgs args) => WindowNavigation.Show<SettingsWindow>(this);
    private void ShowDiagnostics(object? sender, RoutedEventArgs args) => WindowNavigation.Show<DiagnosticsWindow>(this);
}
