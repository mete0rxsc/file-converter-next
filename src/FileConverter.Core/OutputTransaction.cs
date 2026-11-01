namespace FileConverter.Core;

/// <summary>Owns only its staging files and destinations created with CreateNew; never renames an input before success.</summary>
public sealed class OutputTransaction : IDisposable
{
    public string Workspace { get; } = Path.Combine(Path.GetTempPath(), "FileConverter", Guid.NewGuid().ToString("N"));
    public OutputTransaction() => Directory.CreateDirectory(Workspace);
    public string Stage(string file) => Path.Combine(Workspace, file);
    public static string ReservePath(string desired, out FileStream reservation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(desired))!);
        for (var i = 0; ; i++)
        {
            var candidate = i == 0 ? desired : Path.Combine(Path.GetDirectoryName(desired)!, Path.GetFileNameWithoutExtension(desired) + $" ({i + 1})" + Path.GetExtension(desired));
            try { reservation = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None); return candidate; }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
        }
    }
    public IReadOnlyList<string> Commit(IReadOnlyList<(string Staged, string Destination)> files, string input, Preset preset, CancellationToken cancellationToken)
    {
        if (files.Count == 0 || files.Any(f => !File.Exists(f.Staged) || new FileInfo(f.Staged).Length == 0))
            throw new InvalidDataException("转换未生成有效输出，源文件已保留。");
        var created = new List<string>();
        try
        {
            foreach (var (staged, desired) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ReservePath(desired, out var target);
                created.Add(destination);
                using (target)
                using (var source = File.OpenRead(staged))
                { source.CopyTo(target); target.Flush(true); }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            foreach (var path in created) File.Delete(path);
            throw;
        }
        // Once committed, cancellation may not interrupt the final source action.
        // If a post action fails, keep the verified outputs and the original input.
        if (preset.InputPostConversionAction == PostAction.Delete) File.Delete(input);
        else if (preset.InputPostConversionAction == PostAction.MoveInArchiveFolder)
        {
            if (string.IsNullOrWhiteSpace(preset.ConversionArchiveFolderName) || Path.IsPathRooted(preset.ConversionArchiveFolderName) ||
                preset.ConversionArchiveFolderName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
                throw new InvalidDataException("归档目录必须位于输入目录内；转换结果已保留。");
            var archive = Path.Combine(Path.GetDirectoryName(input)!, preset.ConversionArchiveFolderName, Path.GetFileName(input));
            var reserved = ReservePath(archive, out var handle);
            handle.Dispose();
            try { File.Move(input, reserved, true); }
            catch { File.Delete(reserved); throw; }
        }
        return created;
    }
    public void Dispose()
    {
        try { Directory.Delete(Workspace, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
