using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
namespace File_Conventer.ViewModels;

public partial class InputExtension(string name, bool enabled, Action changed) : ObservableObject
{
    public string Name { get; } = name;
    public bool IsEnabled { get; } = enabled;
    [ObservableProperty] private bool isChecked;
    partial void OnIsCheckedChanged(bool value) => changed();
}

public partial class InputCategory(string name) : ObservableObject
{
    private bool synchronizing;
    public string Name { get; } = name;
    public ObservableCollection<InputExtension> Extensions { get; } = [];
    [ObservableProperty] private bool? isChecked;
    partial void OnIsCheckedChanged(bool? value)
    {
        if (synchronizing || value is null) return;
        synchronizing = true;
        foreach (var item in Extensions.Where(e => e.IsEnabled)) item.IsChecked = value.Value;
        synchronizing = false;
        Refresh();
    }
    public void Refresh()
    {
        if (synchronizing) return;
        synchronizing = true;
        var enabled = Extensions.Where(e => e.IsEnabled).ToArray();
        IsChecked = enabled.Length > 0 && enabled.All(e => e.IsChecked) ? true : enabled.Any(e => e.IsChecked) ? null : false;
        synchronizing = false;
    }
}

public sealed record PostActionOption(FileConverter.Core.PostAction Action, string Name)
{
    public override string ToString() => Name;
}
