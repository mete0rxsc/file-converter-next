using FileConverter.Core;
using FileConverter.Shell.Protocol;
using Microsoft.Win32;

namespace FileConverter.Windows;

public static class ShellRegistration
{
    public const string AotClassId = "{61699489-9EEA-43D6-BF5B-32DB32B7A101}";
    public const string CppClassId = "{61699489-9EEA-43D6-BF5B-32DB32B7A102}";
    public const string LegacyClassId = "{AF9B72B5-F4E4-44B0-A3D9-B55B748EFE90}";
    private static RegistryKey OpenUserRoot(string? userSid)
    {
        if (userSid is null) return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        if (!System.Text.RegularExpressions.Regex.IsMatch(userSid, @"^S-1-5-21-\d+-\d+-\d+-\d+$")) throw new ArgumentException("无效的安装用户 SID。");
        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        return users.OpenSubKey(userSid, true) ?? throw new InvalidOperationException("安装用户注册表配置单元未加载。");
    }
    public static bool LegacyInstalled(string? userSid = null)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = hive == RegistryHive.CurrentUser && userSid is not null ? OpenUserRoot(userSid) : RegistryKey.OpenBaseKey(hive, view);
            using var oldClass = root.OpenSubKey(@"Software\Classes\CLSID\" + LegacyClassId);
            if (oldClass is not null) return true;
            using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var item = uninstall.OpenSubKey(name);
                var title = item?.GetValue("DisplayName") as string;
                var publisher = item?.GetValue("Publisher") as string;
                if (title?.StartsWith("File Converter", StringComparison.OrdinalIgnoreCase) == true &&
                    (publisher?.Contains("Allard", StringComparison.OrdinalIgnoreCase) == true ||
                     publisher?.Contains("Tichau", StringComparison.OrdinalIgnoreCase) == true)) return true;
            }
        }
        return false;
    }
    public static void WriteSnapshot(AppSettings settings, string executable, string settingsLabel, string? dataDirectory = null)
    {
        var snapshot = new MenuSnapshot(Path.GetFullPath(executable), settingsLabel, settings.Shell.DisplayPresetIcons,
            settings.Presets.Select(p => new MenuPreset(p.Id.ToString("D"), p.Name, p.InputTypes.ToArray())).ToArray());
        SettingsStore.AtomicWrite(dataDirectory is null ? AppPaths.MenuFile : Path.Combine(dataDirectory, "menu.bin"), snapshot.Encode());
    }
    public static void Apply(AppSettings settings, string baseDirectory, string? userSid = null)
    {
        if (LegacyInstalled(userSid)) throw new InvalidOperationException("检测到旧版 FileConverter 或旧 SharpShell 菜单扩展。请先卸载旧版，再启用新版菜单。");
        var file = Path.Combine(baseDirectory, "shell", settings.Shell.Mode == ShellMode.NativeAot ? "FileConverter.Shell.NativeAot.dll" : "FileConverter.Shell.NativeCpp.dll");
        if (!File.Exists(file)) throw new FileNotFoundException("找不到所选 Shell DLL；请使用完整发行包。", file);
        var classId = settings.Shell.Mode == ShellMode.NativeAot ? AotClassId : CppClassId;
        using var root = OpenUserRoot(userSid);
        // Prepare the selected server before switching the single handler.
        using (var server = root.CreateSubKey(@"Software\Classes\CLSID\" + classId + @"\InprocServer32"))
        { server.SetValue("", Path.GetFullPath(file)); server.SetValue("ThreadingModel", "Apartment"); }
        using (var handler = root.CreateSubKey(@"Software\Classes\*\shellex\ContextMenuHandlers\FileConverter.Modern"))
            handler.SetValue("", classId);
        root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + (classId == AotClassId ? CppClassId : AotClassId), false);
    }
    public static void Unregister(string? userSid = null)
    {
        using var root = OpenUserRoot(userSid);
        root.DeleteSubKeyTree(@"Software\Classes\*\shellex\ContextMenuHandlers\FileConverter.Modern", false);
        root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + AotClassId, false);
        root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + CppClassId, false);
    }
    public static bool IsRegistered(string? userSid = null)
    {
        using var root = OpenUserRoot(userSid);
        using var key = root.OpenSubKey(@"Software\Classes\*\shellex\ContextMenuHandlers\FileConverter.Modern");
        return key is not null;
    }
}
