using System.Windows.Threading;

namespace Stillframe.Services;

internal sealed class FolderMonitor : IDisposable
{
    private FileSystemWatcher? watcher;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer debounce;
    private readonly Action refresh;
    public FolderMonitor(Dispatcher dispatcher, Action refresh)
    {
        this.dispatcher = dispatcher; this.refresh = refresh;
        debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(700) };
        debounce.Tick += (_, _) => { debounce.Stop(); this.refresh(); };
    }
    public void Watch(string folder, bool recursive)
    {
        watcher?.Dispose(); watcher = null; debounce.Stop();
        if (!Directory.Exists(folder)) return;
        try
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
                InternalBufferSize = 32 * 1024
            };
            watcher.Created += Changed; watcher.Deleted += Changed; watcher.Changed += Changed; watcher.Renamed += Renamed;
            watcher.Error += (_, _) => Schedule(); watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Log.Write("Folder watch unavailable: " + e.Message); }
    }
    private void Changed(object sender, FileSystemEventArgs e) => Schedule();
    private void Renamed(object sender, RenamedEventArgs e) => Schedule();
    private void Schedule() => dispatcher.BeginInvoke(() => { debounce.Stop(); debounce.Start(); });
    public void Dispose() { watcher?.Dispose(); debounce.Stop(); }
}
