using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FileConverterNext.Core;
using Microsoft.Win32.SafeHandles;

namespace FileConverterNext.Worker;

public static partial class AudioCd
{
    private const uint ReadToc = 0x24000, RawRead = 0x2403E;
    public static void Extract(string cdaPath, string output, Action<double> progress, CancellationToken token)
    {
        var trackName = Path.GetFileNameWithoutExtension(cdaPath);
        if (!trackName.StartsWith("Track", StringComparison.OrdinalIgnoreCase) || !int.TryParse(trackName.AsSpan(5), out var track))
            throw new InvalidDataException("无法从 CDA 文件名识别音轨。");
        var drive = Path.GetPathRoot(cdaPath) ?? throw new InvalidDataException("无效光驱路径。");
        using var device = CreateFileW(@"\\.\" + drive.TrimEnd('\\'), 0x80000000, 3, 0, 3, 0, 0);
        if (device.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开音频 CD 光驱。");
        var toc = new byte[804];
        if (!DeviceIoControl(device, ReadToc, null, 0, toc, toc.Length, out var returned, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (returned < 12) throw new InvalidDataException("光盘 TOC 数据不完整。");
        var first = toc[2]; var last = toc[3];
        if (track < first || track > last) throw new InvalidDataException("光盘中不存在所选音轨。");
        int Lba(int offset)
        {
            if (offset + 8 > returned) throw new InvalidDataException("音轨地址超出 TOC。");
            return (toc[offset + 5] * 60 + toc[offset + 6]) * 75 + toc[offset + 7] - 150;
        }
        var entry = 4 + (track - first) * 8;
        if ((toc[entry + 1] & 4) != 0) throw new InvalidDataException("所选音轨是数据轨道。");
        var start = Lba(entry); var end = Lba(entry + 8);
        if (start < 0 || end <= start) throw new InvalidDataException("音轨扇区范围无效。");
        var length = checked((end - start) * 2352);
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(file);
        writer.Write("RIFF"u8); writer.Write(checked(length + 36)); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(44100); writer.Write(176400);
        writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(length);
        var rawRequest = new byte[16]; var buffer = new byte[2352 * 16];
        for (var sector = start; sector < end;)
        {
            token.ThrowIfCancellationRequested();
            var count = Math.Min(16, end - sector);
            BinaryPrimitives.WriteInt64LittleEndian(rawRequest, (long)sector * 2048);
            BinaryPrimitives.WriteInt32LittleEndian(rawRequest.AsSpan(8), count);
            BinaryPrimitives.WriteInt32LittleEndian(rawRequest.AsSpan(12), 2); // TRACK_MODE_TYPE.CDDA
            if (!DeviceIoControl(device, RawRead, rawRequest, rawRequest.Length, buffer, count * 2352, out returned, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "读取音轨失败。源 CD 不会被修改。");
            if (returned != count * 2352) throw new IOException("音频 CD 返回不完整扇区。");
            writer.Write(buffer, 0, returned); sector += count;
            progress((sector - start) / (double)(end - start));
        }
        file.Flush(true);
    }
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string file, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint control, byte[]? input, int inputSize, [Out] byte[] output, int outputSize, out int returned, nint overlapped);
}
