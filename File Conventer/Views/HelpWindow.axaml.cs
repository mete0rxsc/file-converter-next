using Avalonia.Controls;
using Avalonia.Interactivity;
namespace File_Conventer.Views;
public partial class HelpWindow : Window
{
    public HelpWindow() => InitializeComponent();
    private void CloseWindow(object? sender, RoutedEventArgs args) => Close();
}
