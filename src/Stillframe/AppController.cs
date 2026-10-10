using System.Drawing;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Stillframe.Native;
using Stillframe.Services;

namespace Stillframe;

public sealed class AppController : IDisposable
{
    private readonly App app;
    private readonly PreferencesStore store;
    private readonly Playlist playlist = new();
    private readonly ImageLoader loader = new();
    private readonly SemaphoreSlim displayGate = new(1);
    private CancellationTokenSource loadCancellation = new(), scanCancellation = new();
    private readonly DispatcherTimer slideshow, health, reconciliation;
    private readonly FolderMonitor monitor;
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly System.Windows.Forms.ToolStripMenuItem playMenu;
    private DesktopHost host = new();
    private WidgetWindow widget = null!;
    private SettingsWindow? settings;
    private BitmapSource? currentImage;
    private bool disposed, scanning, suspended;
    private int refreshRevision, failures;
    private ImageOrder? catalogOrder;
    private DateTime nextRecovery;
    private string? watcherFolder;
    public Preferences Preferences { get; private set; }
    public string Status { get; private set; } = "Choose a folder to start your slideshow.";
    public bool HasError { get; private set; }
    public bool Editing { get; private set; }
    public int Count => playlist.Count;
    public int Position => playlist.Position;
    public string? CurrentPath => playlist.Current;
    public string ConnectionStatus => Editing ? "Editing layout — lock when finished" : host.Status;

    public AppController(App app, PreferencesStore store)
    {
        this.app = app; this.store = store; Preferences = store.Load();
        monitor = new(app.Dispatcher, () => _ = RefreshAsync());
        slideshow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Preferences.IntervalSeconds) };
        slideshow.Tick += async (_, _) => { if (!scanning && !suspended && Preferences.Playing) await AdvanceAsync(1); };
        health = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        health.Tick += (_, _) => CheckHealth();
        reconciliation = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        reconciliation.Tick += async (_, _) => { if (!suspended && !scanning && Preferences.Folder.Length > 0) await RefreshAsync(invalidateImages: false); };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Settings", null, (_, _) => app.Dispatcher.Invoke(OpenSettings));
        playMenu = new("Pause slideshow", null, (_, _) => app.Dispatcher.Invoke(TogglePlayback)); menu.Items.Add(playMenu);
        menu.Items.Add("Previous photo", null, (_, _) => app.Dispatcher.InvokeAsync(() => AdvanceAsync(-1)));
        menu.Items.Add("Next photo", null, (_, _) => app.Dispatcher.InvokeAsync(() => AdvanceAsync(1)));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Edit layout", null, (_, _) => app.Dispatcher.Invoke(BeginEdit));
        menu.Items.Add("Lock to desktop", null, (_, _) => app.Dispatcher.Invoke(EndEdit));
        menu.Items.Add("Reconnect to desktop", null, (_, _) => app.Dispatcher.Invoke(Reconnect));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit Stillframe", null, (_, _) => app.Dispatcher.Invoke(() => { Save(); Dispose(); app.Shutdown(); }));
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Stillframe.ico"));
        using var iconStream = resource!.Stream;
        tray = new() { Text = "Stillframe — desktop photos", Icon = new Icon(iconStream), ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => app.Dispatcher.Invoke(OpenSettings);
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }

    public async Task InitializeAsync(string[] args)
    {
        var folderIndex = Array.IndexOf(args, "--folder");
        if (folderIndex >= 0 && folderIndex + 1 < args.Length) Preferences.Folder = Path.GetFullPath(args[folderIndex + 1]);
        EnsureOnScreen(); CreateWidget();
        await RefreshAsync(); health.Start(); reconciliation.Start(); ResetTimer();
        if (!args.Contains("--background") || Preferences.Folder.Length == 0) OpenSettings();
        if (store.LoadWarning != null) SetStatus(store.LoadWarning, true);
        if (args.Contains("--diagnostics")) WriteDiagnostics();
    }

    private void CreateWidget()
    {
        host = new DesktopHost();
        widget = new WidgetWindow { Width = Preferences.Width, Height = Preferences.Height };
        widget.LockRequested += EndEdit;
        widget.LayoutChanged += CaptureLayout;
        widget.Show();
        if (!host.Attach(widget, Preferences)) widget.Hide();
        settings?.UpdateStatus();
    }

    public void OpenSettings()
    {
        if (disposed) return;
        if (settings == null) settings = new(this);
        settings.UpdatePhoto(currentImage);
        if (!settings.IsVisible) settings.Show();
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Activate();
    }
    public void SettingsClosed(SettingsWindow window) { if (settings == window) settings = null; }

    public async Task ApplyAsync(Preferences next)
    {
        try
        {
            next.Normalize();
            var changedFolder = !StringComparer.OrdinalIgnoreCase.Equals(next.Folder, Preferences.Folder) || next.IncludeSubfolders != Preferences.IncludeSubfolders;
            if (changedFolder) { loadCancellation.Cancel(); loadCancellation.Dispose(); loadCancellation = new(); }
            if (next.StartWithWindows != Preferences.StartWithWindows) ConfigureStartup(next.StartWithWindows);
            Preferences = next;
            EnsureOnScreen(); widget.Width = Preferences.Width; widget.Height = Preferences.Height;
            if (Editing) PlaceEditingWindow(); else host.Place(Preferences, widget);
            Save();
            await RefreshAsync();
            ResetTimer();
            if (!HasError && Count > 0) SetStatus($"Settings saved. {Count} {(Count == 1 ? "photo" : "photos")} ready.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { Log.Write("Apply failed: " + e.Message); SetStatus("Settings could not be saved. Check folder access and try again.", true); }
    }

    public async Task RefreshAsync(bool invalidateImages = true)
    {
        if (disposed) return;
        var revision = ++refreshRevision;
        scanCancellation.Cancel(); scanCancellation.Dispose(); scanCancellation = new();
        var token = scanCancellation.Token;
        scanning = true;
        if (Count == 0) SetStatus("Looking for photos…");
        try
        {
            var folder = Preferences.Folder;
            var recursive = Preferences.IncludeSubfolders;
            var result = await Task.Run(() => ImageCatalog.Scan(folder, recursive, token), token);
            if (disposed || revision != refreshRevision) return;
            await displayGate.WaitAsync(token);
            try
            {
                if (disposed || revision != refreshRevision) return;
                if (invalidateImages) loader.Clear();
                var old = playlist.Current ?? Preferences.LastImage;
                var unchanged = result.Files.Count == playlist.Count && result.Files.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(playlist.Items);
                // Preserve the shuffle sequence during ordinary folder notifications.
                if (!unchanged || catalogOrder != Preferences.Order || Preferences.Order != ImageOrder.Shuffle || playlist.Count == 0)
                    playlist.Replace(result.Files, Preferences.Order, old);
                catalogOrder = Preferences.Order;
            }
            finally { displayGate.Release(); }
            if (watcherFolder != folder || invalidateImages)
            {
                monitor.Watch(folder, recursive); watcherFolder = folder;
            }
            if (Count == 0)
            {
                currentImage = null;
                var empty = result.Warning ?? (folder.Length == 0 ? "Choose a folder from Stillframe Settings." : "No supported photos yet. Add JPG, PNG or BMP images to this folder.");
                widget.ShowEmpty(empty); settings?.UpdatePhoto(null);
                SetStatus(empty, result.Warning != null); return;
            }
            if (playlist.Current == null) await AdvanceAsync(1);
            else if (invalidateImages) await RenderCurrentAsync(false);
            if (result.Warning != null) SetStatus(result.Warning, true);
            else if (!HasError) SetStatus($"{Count} {(Count == 1 ? "photo" : "photos")} ready. Your originals stay untouched.");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { SetStatus("Photos could not be read. Choose another folder or try Refresh photos.", true); Log.Write("Catalog failure: " + e.Message); }
        finally { if (revision == refreshRevision) { scanning = false; ResetTimer(); settings?.UpdateStatus(); } }
    }

    public async Task AdvanceAsync(int direction)
    {
        if (disposed || Count == 0 || !await displayGate.WaitAsync(0)) return;
        slideshow.Stop();
        try { await LoadAndShowAsync(direction, moveFirst: true, animate: true); }
        finally { displayGate.Release(); ResetTimer(); }
    }
    private async Task RenderCurrentAsync(bool animate)
    {
        await displayGate.WaitAsync();
        try { if (!disposed) await LoadAndShowAsync(1, moveFirst: false, animate: animate); }
        finally { displayGate.Release(); }
    }
    private async Task LoadAndShowAsync(int direction, bool moveFirst, bool animate)
    {
        var token = loadCancellation.Token;
        var skipped = 0;
        for (var attempt = 0; attempt < Count; attempt++)
        {
            if (moveFirst || attempt > 0) playlist.Move(direction);
            var path = playlist.Current;
            if (path == null) return;
            try
            {
                var image = await loader.LoadAsync(path, DecodeWidth(), token);
                if (disposed || token.IsCancellationRequested) return;
                currentImage = image;
                await widget.ShowPhotoAsync(image, Preferences, animate);
                Preferences.LastImage = path; Save(); settings?.UpdatePhoto(image);
                SetStatus(skipped > 0 ? $"Skipped {skipped} unreadable {(skipped == 1 ? "photo" : "photos")}. Slideshow continues." : $"{Count} {(Count == 1 ? "photo" : "photos")} ready. Your originals stay untouched.", skipped > 0);
                _ = PreloadAsync(playlist.Next, token);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or System.Runtime.InteropServices.COMException)
            { skipped++; Log.Write("Image skipped: " + e.GetType().Name); }
        }
        currentImage = null; widget.ShowEmpty("These images could not be opened. Choose another folder or refresh after fixing the files.");
        settings?.UpdatePhoto(null); SetStatus("No readable images were found. Choose another folder or fix the files and refresh.", true);
    }
    private int DecodeWidth() => (int)Math.Clamp(Preferences.Width * 2, 720, 2560);
    private async Task PreloadAsync(string? path, CancellationToken token)
    {
        if (path == null || disposed) return;
        try { await loader.LoadAsync(path, DecodeWidth(), token); }
        catch (Exception e) when (e is OperationCanceledException or IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or System.Runtime.InteropServices.COMException) { }
    }

    public void TogglePlayback()
    {
        Preferences.Playing = !Preferences.Playing; Save(); ResetTimer(); settings?.UpdateStatus();
    }
    private void ResetTimer()
    {
        slideshow.Stop(); slideshow.Interval = TimeSpan.FromSeconds(Preferences.IntervalSeconds);
        if (!disposed && !suspended && Preferences.Playing && Count > 1) slideshow.Start();
        playMenu.Text = Preferences.Playing ? "Pause slideshow" : "Play slideshow";
    }

    public void BeginEdit()
    {
        if (disposed || Editing) return;
        // Reparenting changes WPF's native visibility state. Give edit mode a
        // fresh top-level surface rather than reusing a desktop child HWND.
        host.Detach(); widget.Close();
        Editing = true; host = new DesktopHost();
        widget = new WidgetWindow { Width = Preferences.Width, Height = Preferences.Height,
            Left = Preferences.X, Top = Preferences.Y, ShowInTaskbar = true, ShowActivated = true };
        widget.LockRequested += EndEdit;
        widget.LayoutChanged += CaptureLayout;
        widget.Show(); PlaceEditingWindow(); widget.SetEditMode(true);
        if (currentImage != null) _ = widget.ShowPhotoAsync(currentImage, Preferences, false);
        SetStatus("Move or resize the frame, then choose Lock to desktop. You can also enter exact coordinates in Settings.");
        settings?.UpdateStatus();
    }
    private void PlaceEditingWindow()
    {
        var hwnd = new WindowInteropHelper(widget).Handle;
        var dpi = Math.Max(96, NativeMethods.GetDpiForWindow(hwnd)) / 96.0;
        NativeMethods.SetWindowPos(hwnd, 0, Preferences.X, Preferences.Y, (int)(Preferences.Width * dpi), (int)(Preferences.Height * dpi), NativeMethods.SwpNoActivate);
    }
    public void EndEdit()
    {
        if (disposed) return;
        if (Editing) CaptureLayout();
        if (Editing)
        {
            widget.Close(); Editing = false; CreateWidget();
            if (currentImage != null) _ = widget.ShowPhotoAsync(currentImage, Preferences, false);
        }
        else Reconnect();
        settings?.UpdateLayoutFields(); settings?.UpdateStatus(); WriteDiagnostics();
    }
    private void CaptureLayout()
    {
        if (!Editing) return;
        var handle = new WindowInteropHelper(widget).Handle;
        NativeMethods.GetWindowRect(handle, out var rect);
        Preferences.X = rect.Left; Preferences.Y = rect.Top;
        Preferences.Width = widget.Width; Preferences.Height = widget.Height;
        Preferences.Monitor = System.Windows.Forms.Screen.FromHandle(handle).DeviceName;
        Save(); settings?.UpdateLayoutFields();
    }
    public void Reconnect()
    {
        if (disposed || Editing) return;
        if (host.Handle == 0 || !NativeMethods.IsWindow(host.Handle)) CreateWidget();
        else
        {
            widget.Show();
            if (!host.Attach(widget, Preferences)) widget.Hide();
        }
        if (host.Attached) { failures = 0; SetStatus("Connected to the desktop. Applications appear above your photos."); }
        else { failures++; nextRecovery = DateTime.UtcNow.AddSeconds(Math.Min(60, 4 * Math.Pow(2, Math.Min(4, failures)))); SetStatus(host.Status, true); }
        settings?.UpdateStatus(); WriteDiagnostics();
    }
    private void CheckHealth()
    {
        if (disposed || suspended) return;
        var request = Path.Combine(store.DirectoryPath, "open-settings.request");
        if (File.Exists(request)) { try { File.Delete(request); OpenSettings(); } catch (IOException) { } }
        if (!Editing && !host.Attached && DateTime.UtcNow >= nextRecovery) Reconnect();
        if (host.Attached) host.Place(Preferences, widget);
    }
    private void EnsureOnScreen()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var selected = screens.FirstOrDefault(s => s.DeviceName == Preferences.Monitor)
            ?? screens.FirstOrDefault(s => s.WorkingArea.Contains(Preferences.X, Preferences.Y))
            ?? System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
        var bounds = selected.WorkingArea;
        Preferences.Monitor = selected.DeviceName;
        Preferences.X = Math.Clamp(Preferences.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - (int)Preferences.Width - 12));
        Preferences.Y = Math.Clamp(Preferences.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - (int)Preferences.Height - 12));
    }

    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => app.Dispatcher.BeginInvoke(async () =>
    {
        suspended = e.Mode == PowerModes.Suspend;
        if (e.Mode == PowerModes.Resume) { suspended = false; EnsureOnScreen(); Reconnect(); await RefreshAsync(); }
        ResetTimer();
    });
    private void DisplayChanged(object? sender, EventArgs e) => app.Dispatcher.BeginInvoke(() =>
    { EnsureOnScreen(); if (Editing) PlaceEditingWindow(); else host.Place(Preferences, widget); Save(); settings?.UpdateLayoutFields(); });

    private static void ConfigureStartup(bool enabled)
    {
        var executable = Environment.ProcessPath ?? throw new IOException("Executable path is unavailable.");
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("Stillframe", $"\"{executable}\" --background"); else key.DeleteValue("Stillframe", false);
    }
    private void SetStatus(string message, bool error = false) { Status = message; HasError = error; settings?.UpdateStatus(); }
    private void Save()
    {
        try { store.Save(Preferences); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Log.Write("Settings save failed: " + e.Message); SetStatus("Settings could not be saved. Check storage access and try again.", true); }
    }
    public void WriteDiagnostics()
    {
        try
        {
            NativeMethods.GetWindowRect(host.Handle, out var rect);
            var siblings = new List<string>();
            for (var current = NativeMethods.GetWindow(host.Handle, 3); current != 0 && siblings.Count < 16; current = NativeMethods.GetWindow(current, 3))
                siblings.Add(NativeMethods.ClassName(current));
            var diagnostic = new
            {
                AppVersion = typeof(App).Assembly.GetName().Version?.ToString(),
                WindowsBuild = Environment.OSVersion.Version.Build,
                Attached = host.Attached,
                ModernDesktop = host.ModernDesktop,
                Editing,
                ParentClass = NativeMethods.ClassName(NativeMethods.GetParent(host.Handle)),
                Visible = NativeMethods.IsWindowVisible(host.Handle),
                SiblingsAbove = siblings,
                Bounds = new { rect.Left, rect.Top, rect.Right, rect.Bottom },
                Width = widget.Width,
                Height = widget.Height,
                Dpi = NativeMethods.GetDpiForWindow(host.Handle),
                PhotoCount = Count,
                SlideshowPlaying = Preferences.Playing,
                MemoryMegabytes = Math.Round(Environment.WorkingSet / 1048576.0, 1),
                Connection = ConnectionStatus
            };
            Directory.CreateDirectory(store.DirectoryPath);
            File.WriteAllText(Path.Combine(store.DirectoryPath, "diagnostics.json"), System.Text.Json.JsonSerializer.Serialize(diagnostic, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Diagnostics unavailable: " + e.Message); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; slideshow.Stop(); health.Stop(); reconciliation.Stop();
        scanCancellation.Cancel(); loadCancellation.Cancel(); monitor.Dispose();
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Icon?.Dispose(); tray.Dispose();
        if (widget != null) { host.Detach(); widget.Close(); }
        settings?.Close();
    }
}
