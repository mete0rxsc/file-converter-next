using System.Collections.ObjectModel;
using FileConverterNext.Core;
using CommunityToolkit.Mvvm.ComponentModel;
namespace FileConverterNext.Desktop.ViewModels;
public partial class PresetTreeNode : ObservableObject
{
    [ObservableProperty] private string name = "";
    public string FullName { get; init; } = "";
    public Preset? Preset { get; init; }
    public ObservableCollection<PresetTreeNode> Children { get; } = [];
    public bool IsFolder => Preset is null;
}
