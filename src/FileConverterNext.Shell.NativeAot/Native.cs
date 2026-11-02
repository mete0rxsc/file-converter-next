using System.Runtime.InteropServices;

namespace FileConverterNext.Shell.NativeAot;

internal static unsafe partial class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct FormatEtc { public ushort Format; public nint Target; public uint Aspect; public int Index; public uint Medium; }
    [StructLayout(LayoutKind.Sequential)] internal struct Medium { public uint Type; public nint Handle; public nint Release; }
    [StructLayout(LayoutKind.Sequential)] internal struct CommandInfo
    {
        public uint Size, Mask; public nint Window, Verb, Parameters, Directory; public int Show; public uint HotKey; public nint Icon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct CommandInfoEx
    {
        public CommandInfo Base; public nint Title, VerbW, ParametersW, DirectoryW, TitleW; public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id; public nint SubMenu, CheckedBitmap, UncheckedBitmap; public nuint Data;
        public nint Text; public uint TextLength; public nint Bitmap;
    }
    [LibraryImport("ole32.dll")] internal static partial void ReleaseStgMedium(ref Medium medium);
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)] internal static partial uint DragQueryFileW(nint drop, uint index, char* path, uint size);
    [LibraryImport("user32.dll")] internal static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll")] internal static partial int GetMenuItemCount(nint menu);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? label);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool InsertMenuW(nint menu, uint position, uint flags, nuint id, string label);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool DestroyMenu(nint menu);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool SetMenuItemInfoW(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItemInfo info);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] internal static partial uint GetModuleFileNameW(nint module, char* buffer, uint size);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool GetModuleHandleExW(uint flags, nint address, out nint module);
    [LibraryImport("kernel32.dll")] internal static partial int WideCharToMultiByte(uint codePage, uint flags, char* input, int inputLength, byte* output, int outputLength, nint defaultCharacter, nint usedDefaultCharacter);
}
