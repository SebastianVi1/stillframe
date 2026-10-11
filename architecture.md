# Architecture

Stillframe is a .NET 10 Windows application. WPF provides the interface; Win32 connects the photo frame to Explorer. Windows Forms supplies the tray icon and display inventory.

## Components

| Component | Responsibility |
|---|---|
| `src/Stillframe.Core` | Preferences, file discovery and playlist ordering; independent of the UI. |
| `AppController.cs` | Coordinates playback, image loading, settings, folder monitoring and desktop recovery. |
| `WidgetWindow.xaml` / `.cs` | Renders photos, fade/zoom animations and layout controls. |
| `SettingsWindow.xaml` / `.cs` | Folder selection, playback controls and validated settings. |
| `Native/DesktopHost.cs` / `AlphaSurface.cs` | Attaches behind icons/windows; presents DPI-sized pixels with antialiased corner transparency. |
| `Services/ImageLoader.cs` | Asynchronous image decoding, EXIF orientation, bounded caching and preloading. |
| `Services/FolderMonitor.cs` | Debounces folder changes; periodic scans recover missed events. |
| `tests/Stillframe.Tests` / `Verification.cs` | Core tests and isolated application/rendering checks. |

## Flow

Folder selection → file scan → playlist → image decoder/cache → photo frame. The controller serializes photo changes, saves preferences and updates the Settings preview.

Explorer hosts an ordinary WPF photo window. Newer Windows 11 layouts use a native constant-opacity container under Progman; older layouts use WorkerW. The visible WPF child stays at the container origin. Native rounded regions clip the locked frame; edit mode uses WPF transparency for antialiased corners. Edit/lock creates a fresh frame. This compatibility path avoids the per-pixel desktop surface that disappeared on Windows build 26200.

Two image layers provide the fade: the next photo fades in above the fully opaque previous photo. Software rendering supports the desktop surface; reduced-motion and Windows animation preferences disable animation.

Photo decoding follows the frame's physical size and Fit/Fill crop, with 2× sampling headroom. Cache keys include size and fit; decoding is bounded to 16 megapixels and an 8192-pixel edge, without enlarging originals.

## Persistence and packaging

Preferences and logs stay in `%LOCALAPPDATA%\Stillframe`; photos are never modified. Each profile has one running instance. A per-user Inno Setup installer bundles the runtime. The app has no accounts, telemetry or network services.
