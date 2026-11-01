using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using File_Conventer.ViewModels;

namespace File_Conventer.Views;

public partial class SettingsWindow : Window
{
    private static readonly DataFormat<string> PresetDragFormat = DataFormat.CreateStringApplicationFormat("FileConverter.Preset");
    private PresetTreeNode? dragNode;
    private Point dragStart;
    private bool dragging;
    private PointerPressedEventArgs? dragPress;
    private bool saved;
    private MainViewModel? editor;
    public SettingsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => { if (DataContext is MainViewModel vm) { editor = vm; vm.BeginSettingsEdit(); vm.SettingsSaved += Saved; } };
        Closed += (_, _) => { if (editor is not null) { editor.SettingsSaved -= Saved; editor.EndSettingsEdit(saved); } };
        PresetList.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(PresetList).Properties.IsLeftButtonPressed) return;
            dragStart = e.GetPosition(PresetList);
            dragPress = e;
            dragNode = NodeAt(e.Source);
        }, RoutingStrategies.Tunnel);
        PresetList.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            var at = e.GetPosition(PresetList);
            if (dragging || dragNode is null || !e.GetCurrentPoint(PresetList).Properties.IsLeftButtonPressed ||
                Math.Abs(at.X - dragStart.X) + Math.Abs(at.Y - dragStart.Y) < 8) return;
            dragging = true;
            try
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.Create(PresetDragFormat, dragNode.FullName));
                if (dragPress is not null) await DragDrop.DoDragDropAsync(dragPress, transfer, DragDropEffects.Move);
            }
            finally { dragging = false; dragNode = null; }
        });
        PresetList.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(PresetDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        });
        PresetList.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (DataContext is MainViewModel vm && dragNode is not null)
            {
                var target = NodeAt(e.Source);
                if (target != dragNode) vm.MoveTreeItem(dragNode, target);
            }
            e.Handled = true;
        });
        PresetList.KeyDown += (_, e) => { if (e.Key == Key.Delete) { DeleteSelected(null, new()); e.Handled = true; } };
    }
    private void Saved() { saved = true; Close(); }
    private static PresetTreeNode? NodeAt(object? source)
    {
        var control = source as Control;
        return (control as TreeViewItem ?? control?.FindAncestorOfType<TreeViewItem>())?.DataContext as PresetTreeNode;
    }
    private async void ImportPresets(object? sender, RoutedEventArgs args)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "File Converter", FileTypeFilter = [new("XML") { Patterns = ["*.xml"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path && DataContext is MainViewModel vm) vm.Import(path);
        }
        catch (Exception error) { (DataContext as MainViewModel)?.Error(error); }
    }
    private async void ExportPresets(object? sender, RoutedEventArgs args)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "File Converter", SuggestedFileName = "FileConverter-presets.xml", DefaultExtension = "xml" });
            if (file?.TryGetLocalPath() is { } path && DataContext is MainViewModel vm) vm.Export(path, false);
        }
        catch (Exception error) { (DataContext as MainViewModel)?.Error(error); }
    }
    private void NewFolder(object? sender, RoutedEventArgs args) { if (DataContext is MainViewModel vm) vm.CreateNewFolder(); }
    private void DeleteSelected(object? sender, RoutedEventArgs args) { if (DataContext is MainViewModel vm) { if (vm.IsFolderSelected) vm.DeleteSelectedFolder(); else vm.RemovePresetCommand.Execute(null); } }
    private void RenameFolder(object? sender, RoutedEventArgs args) { if (DataContext is MainViewModel vm) vm.RenameFolderCommand.Execute(null); }
    private void RefreshPresetTree(object? sender, RoutedEventArgs args) { if (DataContext is MainViewModel vm) vm.RebuildTree(); }
    private async void ShowUpgrade(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel vm) await vm.CheckUpdatesCommand.ExecuteAsync(null);
        WindowNavigation.Show<UpgradeWindow>(this);
    }
}
