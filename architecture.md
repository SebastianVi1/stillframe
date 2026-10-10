# Architecture

Stillframe is a .NET 10 Windows application. WPF provides the interface; Win32 connects the photo frame to Explorer. Windows Forms supplies the tray icon and display inventory.

## Components

| Component | Responsibility |
|---|---|
| `src/Stillframe.Core` | Preferences, file discovery and playlist ordering; independent of the UI. |
| `AppController.cs` | Coordinates playback, image loading, settings, folder monitoring and desktop recovery. |
| `WidgetWindow.xaml` / `.cs` | Renders photos, fade/zoom animations and layout controls. |
| `SettingsWindow.xaml` / `.cs` | Folder selection, playback controls and validated settings. |
| `Native/DesktopHost.cs` | Attaches the frame behind icons/windows; manages placement and rounded regions. |
| `Services/ImageLoader.cs` | Asynchronous image decoding, EXIF orientation, bounded caching and preloading. |
| `Services/FolderMonitor.cs` | Debounces folder changes; periodic scans recover missed events. |
| `tests/Stillframe.Tests` / `Verification.cs` | Core tests and isolated application/rendering checks. |

## Flow

Folder selection → file scan → playlist → image decoder/cache → photo frame. The controller serializes photo changes, saves preferences and updates the Settings preview.

On newer Windows 11 desktops, Explorer's Progman hosts a native layered surface beneath the icon layer and above the wallpaper. An ordinary WPF child renders inside it. Older layouts attach to WorkerW. Edit/lock creates a fresh frame for each mode to avoid stale WPF window state.

Two image layers provide the fade: the next photo fades in above the fully opaque previous photo. Software rendering supports the desktop surface; reduced-motion and Windows animation preferences disable animation.

## Persistence and packaging

Preferences and logs stay in `%LOCALAPPDATA%\Stillframe`; photos are never modified. Each profile has one running instance. A per-user Inno Setup installer bundles the runtime. The app has no accounts, telemetry or network services.
