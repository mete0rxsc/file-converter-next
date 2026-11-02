using Avalonia;
using System;
using FileConverterNext.Core;
using FileConverterNext.Windows;

namespace FileConverterNext.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var command = CommandLine.Parse(args);
            var dataDirectory = command.IntegrationLocalAppData is null ? AppPaths.DataDirectory : System.IO.Path.Combine(command.IntegrationLocalAppData, "FileConverterNext");
            void Trace(string message)
            {
                if (!command.Verbose) return;
                var directory = System.IO.Path.Combine(dataDirectory, "Logs");
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.AppendAllText(System.IO.Path.Combine(directory, "shell-integration.log"), DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            Trace("User=" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value + "; Session=" + System.Diagnostics.Process.GetCurrentProcess().SessionId + "; Target=" + command.IntegrationUserSid + "; Data=" + dataDirectory + "; Register=" + command.Register + "; Unregister=" + command.Unregister);
            if (command.Version) { Console.WriteLine("FileConverterNext " + UpdateService.CurrentVersion); return 0; }
            if (command.Unregister) { ShellRegistration.Unregister(command.IntegrationUserSid); Trace("Registered=" + ShellRegistration.IsRegistered(command.IntegrationUserSid)); return 0; }
            if (command.Register || command.ApplyShell)
            {
                // MSI service custom actions must target the installing user's
                // profile explicitly; HKCU in a service may point to SYSTEM.
                var previousDataDirectory = command.IntegrationLocalAppData is null ? AppPaths.PreviousDataDirectory : System.IO.Path.Combine(command.IntegrationLocalAppData, "FileConverter");
                var store = new SettingsStore(System.IO.Path.Combine(dataDirectory, "settings.json"), previousDataDirectory);
                var settings = store.Load();
                ShellRegistration.Register(settings, AppContext.BaseDirectory, Environment.ProcessPath!, "配置预设…", dataDirectory, command.IntegrationUserSid);
                store.Save(settings);
                if (!ShellRegistration.IsRegistered(command.IntegrationUserSid)) throw new InvalidOperationException("右键菜单注册检查失败。");
                Trace("Registered=True; Mode=" + settings.Shell.Mode);
                return 0;
            }
            App.StartupCommand = command;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            System.IO.Directory.CreateDirectory(AppPaths.LogsDirectory);
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.LogsDirectory, "startup.log"), error.ToString());
            return 1;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
