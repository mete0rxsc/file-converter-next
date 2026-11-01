using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using File_Conventer.ViewModels;
namespace File_Conventer.Views;
public partial class UpgradeWindow : Window
{
    private MainViewModel? model;
    private bool programmaticClose;
    public UpgradeWindow()
    {
        InitializeComponent();
        Opened += (_, _) => { model = DataContext as MainViewModel; if (model is not null) model.CloseUpgradeRequested += CloseForUpgrade; };
        Closing += (_, _) =>
        {
            if (!programmaticClose && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                !desktop.Windows.Any(w => w != this && w.IsVisible)) model?.CancelUpdateCommand.Execute(null);
        };
        Closed += (_, _) => { if (model is not null) model.CloseUpgradeRequested -= CloseForUpgrade; };
    }
    private void CloseForUpgrade() { programmaticClose = true; Close(); }
}
