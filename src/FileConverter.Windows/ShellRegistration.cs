using FileConverter.Core;
using FileConverter.Shell.Protocol;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace FileConverter.Windows;

public static class ShellRegistration
{
    // UI-only releases retain the validated shell payload and its installation path.
    // Keep this in sync with scripts/Build.ps1 and scripts/Test-Shell.ps1.
    private const string ShellPayloadVersion = "3.0.2";
    public const string AotClassId = "{61699489-9EEA-43D6-BF5B-32DB32B7A201}";
    public const string CppClassId = "{61699489-9EEA-43D6-BF5B-32DB32B7A202}";
    private static readonly string[] SupersededClassIds = ["{61699489-9EEA-43D6-BF5B-32DB32B7A101}", "{61699489-9EEA-43D6-BF5B-32DB32B7A102}"];
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
    public const string LegacyClassId = "{AF9B72B5-F4E4-44B0-A3D9-B55B748EFE90}";
    private static RegistryKey OpenUserRoot(string? userSid)
    {
        if (userSid is null) return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        userSid = new System.Security.Principal.SecurityIdentifier(userSid).Value;
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
        var file = ValidateRegistration(settings, baseDirectory, userSid);
        var classId = settings.Shell.Mode == ShellMode.NativeAot ? AotClassId : CppClassId;
        using var root = OpenUserRoot(userSid);
        RegisterKeys(root, classId, Path.GetFullPath(file), Path.GetFullPath(Path.Combine(baseDirectory, "FileConverter.exe")));
        SHChangeNotify(0x08000000, 0, 0, 0);
    }
    private sealed record ValueBackup(string Name, object Value, RegistryValueKind Kind);
    private sealed record KeyBackup(ValueBackup[] Values, Dictionary<string, KeyBackup> Children);
    private static KeyBackup? ReadKey(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        if (key is null) return null;
        return new(key.GetValueNames().Select(n => new ValueBackup(n, key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!, key.GetValueKind(n))).ToArray(),
            key.GetSubKeyNames().ToDictionary(n => n, n => ReadKey(key, n)!));
    }
    private static void RestoreKey(RegistryKey root, string path, KeyBackup? backup)
    {
        root.DeleteSubKeyTree(path, false);
        if (backup is null) return;
        using var key = root.CreateSubKey(path);
        foreach (var value in backup.Values) key.SetValue(value.Name, value.Value, value.Kind);
        foreach (var child in backup.Children) RestoreKey(key, child.Key, child.Value);
    }
    internal static void RegisterKeys(RegistryKey root, string classId, string file, string executable, Action? afterWrites = null)
    {
        const string handlerPath = @"Software\Classes\*\shellex\ContextMenuHandlers\FileConverter.Modern";
        const string markerPath = @"Software\FileConverter.Modern";
        var paths = new[] { AotClassId, CppClassId }.Concat(SupersededClassIds).Select(id => @"Software\Classes\CLSID\" + id).Append(handlerPath).ToArray();
        var backups = paths.ToDictionary(p => p, p => ReadKey(root, p));
        using var markerBefore = root.OpenSubKey(markerPath);
        var oldExecutable = markerBefore?.GetValue("Executable", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var oldKind = oldExecutable is null ? RegistryValueKind.String : markerBefore!.GetValueKind("Executable");
        try
        {
            using (var server = root.CreateSubKey(@"Software\Classes\CLSID\" + classId + @"\InprocServer32"))
            { server.SetValue("", file); server.SetValue("ThreadingModel", "Apartment"); }
            using (var marker = root.CreateSubKey(markerPath)) marker.SetValue("Executable", executable);
            using (var handler = root.CreateSubKey(handlerPath)) handler.SetValue("", classId);
            root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + (classId == AotClassId ? CppClassId : AotClassId), false);
            foreach (var obsolete in SupersededClassIds) root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + obsolete, false);
            afterWrites?.Invoke();
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var path in paths.Reverse())
                try { RestoreKey(root, path, backups[path]); } catch (Exception restore) { errors.Add(restore); }
            try
            {
                using var marker = root.CreateSubKey(markerPath);
                if (oldExecutable is null) marker.DeleteValue("Executable", false);
                else marker.SetValue("Executable", oldExecutable, oldKind);
            }
            catch (Exception restore) { errors.Add(restore); }
            if (errors.Count > 1) throw new AggregateException("菜单注册失败，部分注册信息无法恢复。", errors);
            throw;
        }
    }
    public static string ValidateRegistration(AppSettings settings, string baseDirectory, string? userSid = null)
    {
        if (LegacyInstalled(userSid)) throw new InvalidOperationException("检测到旧版 FileConverter 或旧 SharpShell 菜单扩展。请先卸载旧版，再启用新版菜单。");
        var executable = Path.Combine(baseDirectory, "FileConverter.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("找不到主程序；请使用完整发行包。", executable);
        var file = Path.Combine(baseDirectory, "shell", ShellPayloadVersion, settings.Shell.Mode == ShellMode.NativeAot ? "FileConverter.Shell.NativeAot.dll" : "FileConverter.Shell.NativeCpp.dll");
        if (!File.Exists(file)) throw new FileNotFoundException("找不到所选 Shell DLL；请使用完整发行包。", file);
        using var root = OpenUserRoot(userSid);
        return file;
    }
    public static void Register(AppSettings settings, string baseDirectory, string executable, string label, string? dataDirectory = null, string? userSid = null)
    {
        ValidateRegistration(settings, baseDirectory, userSid);
        var menu = dataDirectory is null ? AppPaths.MenuFile : Path.Combine(dataDirectory, "menu.bin");
        var previous = File.Exists(menu) ? File.ReadAllBytes(menu) : null;
        WriteSnapshot(settings, executable, label, dataDirectory);
        try { Apply(settings, baseDirectory, userSid); }
        catch
        {
            if (previous is not null) SettingsStore.AtomicWrite(menu, previous);
            else File.Delete(menu);
            throw;
        }
    }
    public static void Unregister(string? userSid = null)
    {
        using var root = OpenUserRoot(userSid);
        root.DeleteSubKeyTree(@"Software\Classes\*\shellex\ContextMenuHandlers\FileConverter.Modern", false);
        root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + AotClassId, false);
        root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + CppClassId, false);
        foreach (var obsolete in SupersededClassIds) root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + obsolete, false);
        using (var registration = root.OpenSubKey(@"Software\FileConverter.Modern", true)) registration?.DeleteValue("Executable", false);
        SHChangeNotify(0x08000000, 0, 0, 0);
    }
    public static string RegisteredExecutableOr(string fallback)
    {
        using var root = OpenUserRoot(null);
        using var registration = root.OpenSubKey(@"Software\FileConverter.Modern");
        return registration?.GetValue("Executable") is string path && File.Exists(path) ? path : fallback;
    }
    public static bool IsRegistered(string? userSid = null)
    {
        using var root = OpenUserRoot(userSid);
        using var key = root.OpenSubKey(@"Software\Classes\*\shellex\ContextMenuHandlers\FileConverter.Modern");
        return key is not null;
    }
}
