using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FileConverter.Shell.Protocol;

namespace FileConverter.Shell.NativeAot;

public static unsafe class Exports
{
    private const int Ok = 0, False = 1, NoInterface = unchecked((int)0x80004002), Invalid = unchecked((int)0x80070057), Fail = unchecked((int)0x80004005);
    private static readonly Guid Clsid = new("61699489-9EEA-43D6-BF5B-32DB32B7A101");
    private static readonly Guid Unknown = new("00000000-0000-0000-C000-000000000046"), FactoryId = new("00000001-0000-0000-C000-000000000046");
    private static readonly Guid InitId = new("000214E8-0000-0000-C000-000000000046"), MenuId = new("000214E4-0000-0000-C000-000000000046");
    [StructLayout(LayoutKind.Sequential)] private struct Face { public void** Vtable; public nint Handle; }
    private sealed class ObjectState
    {
        public int References = 1;
        public bool Factory;
        public Face* First;
        public Face* Second;
        public GCHandle Handle;
        public List<string> Files = [];
        public MenuSnapshot? Snapshot;
        public List<(MenuPreset? Preset, bool Enabled)> Commands = [];
    }
    private static readonly void** FactoryVtable = CreateVtable(true, false);
    private static readonly void** InitVtable = CreateVtable(false, false);
    private static readonly void** MenuVtable = CreateVtable(false, true);
    private static nint module;
    private static nint presetBitmap, folderBitmap;
    private static readonly object BitmapLock = new();
    private static nint Bitmap(bool folder)
    {
        lock (BitmapLock)
        {
            ref var cached = ref (folder ? ref folderBitmap : ref presetBitmap);
            if (cached != 0) return cached;
            var chars = new char[32768];
            fixed (char* buffer = chars)
            {
                var count = Native.GetModuleFileNameW(module, buffer, (uint)chars.Length);
                if (count == 0 || count >= chars.Length) return 0;
                var path = Path.Combine(Path.GetDirectoryName(new string(buffer, 0, (int)count))!, folder ? "folder.bmp" : "preset.bmp");
                // NativeAOT is process-pinned, so the tiny shared bitmaps share its lifetime.
                cached = Native.LoadImageW(0, path, 0, 16, 16, 0x2010); return cached;
            }
        }
    }
    private static void SetIcon(nint menu, uint position, bool folder)
    {
        var bitmap = Bitmap(folder); if (bitmap == 0) return;
        var info = new Native.MenuItemInfo { Size = (uint)sizeof(Native.MenuItemInfo), Mask = 0x80, Bitmap = bitmap };
        Native.SetMenuItemInfoW(menu, position, true, ref info);
    }
    private static ObjectState State(void* self) => (ObjectState)GCHandle.FromIntPtr(((Face*)self)->Handle).Target!;
    private static void** CreateVtable(bool factory, bool menu)
    {
        var table = (void**)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(Exports), (factory ? 5 : menu ? 6 : 4) * sizeof(void*));
        table[0] = (delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged[Stdcall]<void*, uint>)&AddRef;
        table[2] = (delegate* unmanaged[Stdcall]<void*, uint>)&Release;
        if (factory)
        {
            table[3] = (delegate* unmanaged[Stdcall]<void*, void*, Guid*, void**, int>)&CreateInstance;
            table[4] = (delegate* unmanaged[Stdcall]<void*, int, int>)&LockServer;
        }
        else if (menu)
        {
            table[3] = (delegate* unmanaged[Stdcall]<void*, nint, uint, uint, uint, uint, int>)&QueryContextMenu;
            table[4] = (delegate* unmanaged[Stdcall]<void*, Native.CommandInfo*, int>)&InvokeCommand;
            table[5] = (delegate* unmanaged[Stdcall]<void*, nuint, uint, uint*, byte*, uint, int>)&GetCommandString;
        }
        else table[3] = (delegate* unmanaged[Stdcall]<void*, nint, void*, nint, int>)&Initialize;
        return table;
    }
    private static ObjectState Create(bool factory)
    {
        var state = new ObjectState { Factory = factory };
        state.Handle = GCHandle.Alloc(state);
        state.First = (Face*)NativeMemory.AllocZeroed((nuint)sizeof(Face));
        state.First->Vtable = factory ? FactoryVtable : InitVtable;
        state.First->Handle = GCHandle.ToIntPtr(state.Handle);
        if (!factory)
        {
            state.Second = (Face*)NativeMemory.AllocZeroed((nuint)sizeof(Face));
            state.Second->Vtable = MenuVtable; state.Second->Handle = state.First->Handle;
        }
        return state;
    }
    private static int Query(ObjectState state, Guid* id, void** result)
    {
        if (result == null) return Invalid;
        *result = null;
        if (id == null) return Invalid;
        if (*id == Unknown || state.Factory && *id == FactoryId || !state.Factory && *id == InitId) *result = state.First;
        else if (!state.Factory && *id == MenuId) *result = state.Second;
        else return NoInterface;
        Interlocked.Increment(ref state.References);
        return Ok;
    }
    private static uint Drop(ObjectState state)
    {
        var refs = Interlocked.Decrement(ref state.References);
        if (refs == 0)
        { NativeMemory.Free(state.First); NativeMemory.Free(state.Second); state.Handle.Free(); }
        return (uint)refs;
    }
    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject", CallConvs = [typeof(CallConvStdcall)])]
    public static int DllGetClassObject(Guid* classId, Guid* interfaceId, void** result)
    {
        if (result == null) return Invalid;
        *result = null;
        try
        {
            // NativeAOT libraries cannot be unloaded. Pin only after loader initialization.
            Native.GetModuleHandleExW(0x5, (nint)(delegate* unmanaged[Stdcall]<Guid*, Guid*, void**, int>)&DllGetClassObject, out module);
            if (classId == null || *classId != Clsid) return unchecked((int)0x80040111);
            var state = Create(true); var hr = Query(state, interfaceId, result); Drop(state); return hr;
        }
        catch { return Fail; }
    }
    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow", CallConvs = [typeof(CallConvStdcall)])]
    public static int DllCanUnloadNow() => False;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(void* self, Guid* id, void** result) { try { return Query(State(self), id, result); } catch { if (result != null) *result = null; return Fail; } }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(void* self) { try { return (uint)Interlocked.Increment(ref State(self).References); } catch { return 0; } }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(void* self) { try { return Drop(State(self)); } catch { return 0; } }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int LockServer(void* self, int locked) => Ok; // pinned for process lifetime
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CreateInstance(void* self, void* outer, Guid* id, void** result)
    {
        if (result == null) return Invalid;
        *result = null;
        if (outer != null) return unchecked((int)0x80040110);
        try { var state = Create(false); var hr = Query(state, id, result); Drop(state); return hr; }
        catch { return Fail; }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Initialize(void* self, nint folder, void* data, nint key)
    {
        try
        {
            var state = State(self); state.Files.Clear(); state.Commands.Clear(); state.Snapshot = null;
            if (data == null) return Invalid;
            var format = new Native.FormatEtc { Format = 15, Aspect = 1, Index = -1, Medium = 1 };
            var medium = new Native.Medium();
            var hr = ((delegate* unmanaged[Stdcall]<void*, Native.FormatEtc*, Native.Medium*, int>)(*(void***)data)[3])(data, &format, &medium);
            if (hr < 0) return hr;
            try
            {
                var count = Native.DragQueryFileW(medium.Handle, uint.MaxValue, null, 0);
                if (count > 100000) return Invalid;
                for (uint i = 0; i < count; i++)
                {
                    var size = Native.DragQueryFileW(medium.Handle, i, null, 0);
                    if (size == 0 || size > 32767) continue;
                    var chars = new char[size + 1];
                    fixed (char* buffer = chars)
                    { Native.DragQueryFileW(medium.Handle, i, buffer, size + 1); state.Files.Add(new string(buffer, 0, (int)size)); }
                }
            }
            finally { Native.ReleaseStgMedium(ref medium); }
            var snapshotFile = new FileInfo(ShellPaths.Snapshot);
            if (!snapshotFile.Exists || snapshotFile.Length > MenuSnapshot.MaxSize) return Fail;
            state.Snapshot = MenuSnapshot.Decode(File.ReadAllBytes(snapshotFile.FullName));
            return Ok;
        }
        catch { return Fail; }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryContextMenu(void* self, nint parent, uint position, uint first, uint last, uint flags)
    {
        try { return QueryMenu(State(self), parent, position, first, last, flags); }
        catch { return Fail; }
    }
    private static int QueryMenu(ObjectState state, nint parent, uint position, uint first, uint last, uint flags)
    {
        nint root = 0;
        try
        {
            state.Commands.Clear();
            if ((flags & 1) != 0 || state.Snapshot is null) return Ok;
            var selected = state.Snapshot.Select(state.Files);
            if (selected.Count == 0) return Ok;
            if (last < first || (ulong)selected.Count + 1 > (ulong)last - first + 1) return Ok;
            root = Native.CreatePopupMenu(); if (root == 0) return Fail;
            var folders = new Dictionary<string, nint> { [""] = root };
            foreach (var (preset, enabled) in selected)
            {
                var parts = preset.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var path = ""; var menu = root;
                foreach (var part in parts.Take(Math.Max(0, parts.Length - 1)))
                {
                    var next = path.Length == 0 ? part : path + "/" + part;
                    if (!folders.TryGetValue(next, out var child))
                    {
                        child = Native.CreatePopupMenu(); if (child == 0) throw new InvalidOperationException();
                        if (!Native.AppendMenuW(menu, 0x10, (nuint)child, Escape(part))) { Native.DestroyMenu(child); throw new InvalidOperationException(); }
                        if (state.Snapshot.Icons) SetIcon(menu, (uint)(Native.GetMenuItemCount(menu) - 1), true);
                        folders[next] = child;
                    }
                    path = next; menu = child;
                }
                if (!Native.AppendMenuW(menu, enabled ? 0u : 1u, first + (uint)state.Commands.Count, Escape(parts.LastOrDefault() ?? preset.Name))) throw new InvalidOperationException();
                if (state.Snapshot.Icons)
                {
                    var item = new Native.MenuItemInfo { Size = (uint)sizeof(Native.MenuItemInfo), Mask = 0x80, Bitmap = Bitmap(false) };
                    Native.SetMenuItemInfoW(menu, first + (uint)state.Commands.Count, false, ref item);
                }
                state.Commands.Add((preset, enabled));
            }
            Native.AppendMenuW(root, 0x800, 0, null);
            if (!Native.AppendMenuW(root, 0, first + (uint)state.Commands.Count, Escape(state.Snapshot.SettingsLabel))) throw new InvalidOperationException();
            state.Commands.Add((null, true));
            if (!Native.InsertMenuW(parent, position, 0x410, (nuint)root, "File Converter")) throw new InvalidOperationException();
            root = 0; return state.Commands.Count;
        }
        catch { state.Commands.Clear(); return Fail; }
        finally { if (root != 0) Native.DestroyMenu(root); }
    }
    private static string Escape(string label) => label.Replace("&", "&&");
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int InvokeCommand(void* self, Native.CommandInfo* info)
    {
        try
        {
            if (info == null || info->Size < sizeof(Native.CommandInfo)) return Invalid;
            var state = State(self);
            var unicode = (info->Mask & 0x4000) != 0 && info->Size >= sizeof(Native.CommandInfoEx);
            int index;
            var wideVerb = unicode ? ((Native.CommandInfoEx*)info)->VerbW : 0;
            // Unicode canonical strings use lpVerbW. Numeric offsets always use
            // lpVerb, including when Explorer sets CMIC_MASK_UNICODE and leaves lpVerbW null.
            if (unicode && (nuint)wideVerb > ushort.MaxValue)
                index = state.Commands.FindIndex(c => string.Equals(Canonical(c.Preset), Marshal.PtrToStringUni(wideVerb), StringComparison.OrdinalIgnoreCase));
            else if ((nuint)info->Verb <= ushort.MaxValue) index = (int)(nuint)info->Verb;
            else
            {
                if (unicode) return Invalid;
                var text = Marshal.PtrToStringAnsi(info->Verb);
                index = state.Commands.FindIndex(c => string.Equals(Canonical(c.Preset), text, StringComparison.OrdinalIgnoreCase));
            }
            if (index < 0 || index >= state.Commands.Count || !state.Commands[index].Enabled || state.Snapshot is null) return Invalid;
            ShellPaths.Invoke(state.Snapshot, state.Commands[index].Preset, state.Files); return Ok;
        }
        catch { return Fail; }
    }
    private static string Canonical(MenuPreset? preset) => preset is null ? "fileconverter.settings" : "fileconverter." + preset.Id;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetCommandString(void* self, nuint id, uint type, uint* reserved, byte* buffer, uint size)
    {
        try
        {
            var state = State(self);
            if (id >= (nuint)state.Commands.Count) return Invalid;
            if (type is 2 or 6) return Ok;
            if (buffer == null || size == 0) return Invalid;
            var text = type is 0 or 4 ? Canonical(state.Commands[(int)id].Preset) : state.Commands[(int)id].Preset?.Name ?? "File Converter";
            if ((type & 4) != 0)
            {
                var count = Math.Min(text.Length, checked((int)size) - 1);
                text.AsSpan(0, count).CopyTo(new Span<char>((char*)buffer, count)); ((char*)buffer)[count] = '\0';
            }
            else
            {
                var bytes = Encoding.ASCII.GetBytes(text); var count = Math.Min(bytes.Length, checked((int)size) - 1);
                bytes.AsSpan(0, count).CopyTo(new Span<byte>(buffer, count)); buffer[count] = 0;
            }
            return Ok;
        }
        catch { return Fail; }
    }
}
