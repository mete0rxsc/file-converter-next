using System.Runtime.InteropServices;

namespace FileConverter.Windows;
public sealed class TaskbarProgress : IDisposable
{
    private ITaskbarList3? taskbar;
    public TaskbarProgress()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new("56FDF344-FD6D-11D0-958A-006097C9A090"));
            taskbar = (ITaskbarList3)Activator.CreateInstance(type!)!; taskbar.HrInit();
        }
        catch (COMException) { }
    }
    public void Set(nint window, ulong completed, ulong total, uint state)
    {
        try { taskbar?.SetProgressState(window, state); if (state != 0) taskbar?.SetProgressValue(window, completed, Math.Max(1, total)); }
        catch (COMException) { }
    }
    public void Dispose() { if (taskbar is not null) Marshal.FinalReleaseComObject(taskbar); taskbar = null; }
    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit(); void AddTab(nint hwnd); void DeleteTab(nint hwnd); void ActivateTab(nint hwnd); void SetActiveAlt(nint hwnd);
        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(nint hwnd, ulong completed, ulong total);
        void SetProgressState(nint hwnd, uint state);
    }
}
