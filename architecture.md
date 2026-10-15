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

People framing uses the Windows FaceDetector on a grayscale preview after EXIF orientation. PeopleCrop selects a Fill viewport around all detected faces; PhotoImage draws the original bitmap with that focus point. Fill never switches automatically to Fit and no extra cropped bitmap is created. Groups wider than the viewport are centered as closely as possible. The widget and Settings preview share PhotoImage.

Each photo layer caches its scaled rendering when it fits within the software renderer's 2048-pixel limit. The incoming layer resets its zoom before becoming visible; the outgoing layer retains its placement and zoom until hidden. Fade completion follows the animation event rather than a wall-clock delay. Desktop attachment stays on the working constant-opacity WPF path.

WidgetWindow owns the hover navigation overlay and sends direction requests to AppController.AdvanceAsync, sharing the existing playback gate and timer reset. Navigation is hidden for fewer than two photos and disabled during loading/fades. Buttons sit outside the cached photo layers and have no effect on the Explorer attachment method.
