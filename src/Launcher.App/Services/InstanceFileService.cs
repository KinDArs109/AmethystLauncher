using System.IO;

namespace Launcher.App.Services;

public sealed record FileEntry(string Name, string FullPath, bool IsDirectory, long Size, int ItemCount, DateTimeOffset Modified)
{
    public string SizeLabel => IsDirectory
        ? $"{ItemCount} {RussianPlural.Of(ItemCount, "элемент", "элемента", "элементов")}"
        : FormatBytes(Size);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ"];
        double size = bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }
}

public interface IInstanceFileService
{
    IReadOnlyList<FileEntry> ListDirectory(string path);

    void Delete(FileEntry entry);

    void Rename(FileEntry entry, string newName);

    void CopyInto(string targetDirectory, IEnumerable<string> sourceFilePaths);
}

public sealed class InstanceFileService : IInstanceFileService
{
    public IReadOnlyList<FileEntry> ListDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        var entries = new List<FileEntry>();

        foreach (var dir in Directory.GetDirectories(path))
        {
            var info = new DirectoryInfo(dir);
            var itemCount = SafeCountEntries(dir);
            entries.Add(new FileEntry(info.Name, dir, true, 0, itemCount, info.LastWriteTimeUtc));
        }

        foreach (var file in Directory.GetFiles(path))
        {
            var info = new FileInfo(file);
            entries.Add(new FileEntry(info.Name, file, false, info.Length, 0, info.LastWriteTimeUtc));
        }

        return entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Delete(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            Directory.Delete(entry.FullPath, recursive: true);
        }
        else
        {
            File.Delete(entry.FullPath);
        }
    }

    public void Rename(FileEntry entry, string newName)
    {
        var parent = Path.GetDirectoryName(entry.FullPath)
            ?? throw new InvalidOperationException($"Не удалось определить родительскую папку для '{entry.FullPath}'.");
        var newPath = Path.Combine(parent, newName);

        if (entry.IsDirectory)
        {
            Directory.Move(entry.FullPath, newPath);
        }
        else
        {
            File.Move(entry.FullPath, newPath);
        }
    }

    public void CopyInto(string targetDirectory, IEnumerable<string> sourceFilePaths)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var source in sourceFilePaths)
        {
            var destination = Path.Combine(targetDirectory, Path.GetFileName(source));
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static int SafeCountEntries(string directory)
    {
        try
        {
            return Directory.GetFileSystemEntries(directory).Length;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
