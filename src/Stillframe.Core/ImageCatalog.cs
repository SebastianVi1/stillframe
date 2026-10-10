namespace Stillframe.Core;

public sealed record PhotoFile(string Path, DateTime Modified);
public sealed record CatalogResult(IReadOnlyList<PhotoFile> Files, string? Warning);

public static class ImageCatalog
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".bmp" };

    public static bool IsSupported(string path) => Extensions.Contains(System.IO.Path.GetExtension(path));

    public static CatalogResult Scan(string folder, bool recursive, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(folder)) return new([], null);
        if (!Directory.Exists(folder)) return new([], "This folder is unavailable. Reconnect the drive or choose another folder.");
        var files = new List<PhotoFile>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(System.IO.Path.GetFullPath(folder));
        var skipped = false;
        while (pending.TryPop(out var directory))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!visited.Add(directory)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!IsSupported(file)) continue;
                    try { files.Add(new(file, File.GetLastWriteTimeUtc(file))); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped = true; }
                }
                if (!recursive) continue;
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    // Junctions and symlinks must not produce loops or escape the chosen tree.
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped = true; }
        }
        return new(files, skipped ? "Some files or subfolders could not be read." : null);
    }
}
