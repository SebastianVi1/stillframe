namespace Stillframe.Services;

internal static class Log
{
    private static readonly object Gate = new();
    public static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stillframe");
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
