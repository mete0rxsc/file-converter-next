using System.Collections.ObjectModel;
using FileConverter.Core;
using CommunityToolkit.Mvvm.ComponentModel;
namespace File_Conventer.ViewModels;
public partial class PresetTreeNode : ObservableObject
{
    [ObservableProperty] private string name = "";
    public string FullName { get; init; } = "";
    public Preset? Preset { get; init; }
    public ObservableCollection<PresetTreeNode> Children { get; } = [];
    public bool IsFolder => Preset is null;
}
