using Avalonia.Controls;
namespace File_Conventer.Views;

public static class WindowNavigation
{
    public static event Action<Window>? WindowCreated;
    public static T Show<T>(Window owner) where T : Window, new()
    {
        var existing = owner.OwnedWindows.OfType<T>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return existing; }
        var window = new T { DataContext = owner.DataContext };
        WindowCreated?.Invoke(window);
        window.Show(owner);
        return window;
    }
}
