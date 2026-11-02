using Avalonia.Controls;
using Avalonia.Interactivity;
namespace FileConverterNext.Desktop.Views;
public partial class HelpWindow : Window
{
    public HelpWindow() => InitializeComponent();
    private void CloseWindow(object? sender, RoutedEventArgs args) => Close();
}
