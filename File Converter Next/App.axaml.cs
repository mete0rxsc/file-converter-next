using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FileConverterNext.Desktop.ViewModels;
using FileConverterNext.Desktop.Views;
using FileConverterNext.Core;

namespace FileConverterNext.Desktop;

public partial class App : Application
{
    public static CommandLine StartupCommand { get; set; } = CommandLine.Parse([]);
    public static Window CreateStartupWindow(CommandLine command) => command.Settings ? new SettingsWindow()
        : command.RequestFile is not null || command.Preset is not null ? new MainWindow() : new HelpWindow();
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var model = new MainViewModel();
            var window = CreateStartupWindow(StartupCommand);
            window.DataContext = model;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = window;
            void TrackWindow(Window opened)
            {
                opened.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    if (desktop.Windows.Any(w => w.IsVisible)) return;
                    if (model.InstallUpdateOnExit && model.IsUpdateDownloading)
                    {
                        var waiting = new UpgradeWindow { DataContext = model };
                        TrackWindow(waiting); waiting.Show();
                    }
                    else desktop.Shutdown();
                });
            }
            TrackWindow(window);
            WindowNavigation.WindowCreated += TrackWindow;
            window.Opened += async (_, _) => await model.InitializeAsync(StartupCommand);
            model.UpgradeAvailable += () => { if (window.IsVisible) WindowNavigation.Show<UpgradeWindow>(window); };
            // Our normal exit uses Shutdown(); external/session shutdown must not launch MSI.
            desktop.ShutdownRequested += (_, _) => model.CancelUpdateCommand.Execute(null);
            desktop.Exit += (_, _) =>
            {
                WindowNavigation.WindowCreated -= TrackWindow;
                var installer = model.InstallerOnExit;
                model.Dispose();
                if (installer is not null)
                    try { UpdateService.Install(installer); } catch (Exception error) { model.Error(error); }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
