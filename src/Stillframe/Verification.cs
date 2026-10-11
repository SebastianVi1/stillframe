using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Stillframe.Native;

namespace Stillframe;

// Application-owned integration checks run against an isolated profile and synthetic images.
// No input is sent to other applications or the user's desktop session.
internal static class Verification
{
    public static async Task RunAsync(AppController controller, string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<object>();
        void Check(string name, bool passed, string? detail = null)
        {
            results.Add(new { Name = name, Passed = passed, Detail = detail });
            if (!passed) throw new InvalidOperationException("Verification failed: " + name + " " + detail);
        }
        try
        {
            var pictures = Path.Combine(directory, "photos");
            Directory.CreateDirectory(pictures);
            for (var i = 0; i < 3; i++) CreatePhoto(Path.Combine(pictures, $"0{i + 1}.png"), i);
            File.WriteAllText(Path.Combine(pictures, "04-corrupt.jpg"), "This is intentionally not an image.");
            var p = controller.Preferences with { Folder = pictures, IncludeSubfolders = false, IntervalSeconds = 2, Playing = false, Order = ImageOrder.Filename };
            await controller.ApplyAsync(p);
            Check("Folder selection loads supported files", controller.Count == 4);
            Check("First photo decoded", Path.GetFileName(controller.CurrentPath) == "01.png");
            Check("Native desktop attachment", controller.ConnectionStatus == "Attached to desktop", controller.ConnectionStatus);
            await controller.AdvanceAsync(1);
            Check("Next photo", Path.GetFileName(controller.CurrentPath) == "02.png");
            await controller.AdvanceAsync(-1);
            Check("Previous photo", Path.GetFileName(controller.CurrentPath) == "01.png");
            await controller.AdvanceAsync(1); await controller.AdvanceAsync(1); await controller.AdvanceAsync(1);
            Check("Corrupt image skipped and playback continues", Path.GetFileName(controller.CurrentPath) == "01.png" && controller.Status.Contains("Skipped"));
            controller.TogglePlayback(); Check("Play control", controller.Preferences.Playing);
            await Task.Delay(2500);
            Check("Automatic slideshow advances", Path.GetFileName(controller.CurrentPath) == "02.png");
            controller.TogglePlayback(); var paused = controller.CurrentPath;
            await Task.Delay(2400);
            Check("Pause stops automatic advance", controller.CurrentPath == paused);
            controller.BeginEdit(); Check("Layout edit detaches safely", controller.Editing);
            controller.EndEdit(); Check("Layout locks back to desktop", !controller.Editing && controller.ConnectionStatus == "Attached to desktop");
            await Task.Delay(150);
            Check("Locked frame contains opaque photo pixels", controller.DesktopFrameCenterAlpha == 255, $"Center alpha: {controller.DesktopFrameCenterAlpha}");
            var widget = Application.Current.Windows.OfType<WidgetWindow>().First();
            var widgetHandle = new System.Windows.Interop.WindowInteropHelper(widget).Handle;
            var surface = NativeMethods.GetParent(widgetHandle);
            var wallpaperHost = NativeMethods.GetParent(surface);
            Check("Alpha frame is hosted inside the wallpaper layer", NativeMethods.ClassName(wallpaperHost) == "WorkerW");
            var desktop = NativeMethods.GetParent(wallpaperHost);
            if (desktop != 0 && ((long)NativeMethods.GetWindowLongPtr(desktop, NativeMethods.GwlExStyle) & NativeMethods.WsExNoRedirectionBitmap) != 0)
            {
                Check("Locked frame publishes an independent per-pixel alpha surface", !widget.AllowsTransparency &&
                    ((long)NativeMethods.GetWindowLongPtr(surface, NativeMethods.GwlExStyle) & NativeMethods.WsExLayered) != 0 && controller.HasRenderedDesktopFrame);
                var icons = NativeMethods.FindWindowEx(desktop, 0, "SHELLDLL_DefView", null);
                var above = NativeMethods.GetWindow(wallpaperHost, 3);
                while (above != 0 && above != icons) above = NativeMethods.GetWindow(above, 3);
                Check("Desktop icons stay above the wallpaper-hosted frame", icons != 0 && above == icons);
            }
            for (var cycle = 0; cycle < 3; cycle++)
            {
                controller.BeginEdit(); controller.EndEdit();
                await Task.Delay(150);
                Check($"Lock/unlock cycle {cycle + 1} retains native photo pixels", controller.HasRenderedDesktopFrame && controller.DesktopFrameCenterAlpha == 255);
            }
            CreatePhoto(Path.Combine(pictures, "05-added.png"), 0);
            await Task.Delay(1800);
            Check("File watcher discovers new photo", controller.Count == 5);
            File.Delete(Path.Combine(pictures, "05-added.png"));
            await Task.Delay(1800);
            Check("File watcher removes deleted photo", controller.Count == 4);
            var empty = Path.Combine(directory, "empty"); Directory.CreateDirectory(empty);
            await controller.ApplyAsync(controller.Preferences with { Folder = empty });
            Check("Empty folder has recovery guidance", controller.Count == 0 && controller.Status.Contains("No supported photos"));
            CaptureSettings(controller, directory, "empty.png");
            await controller.ApplyAsync(controller.Preferences with { Folder = pictures, Order = ImageOrder.Shuffle, Fit = ImageFit.Fit, ReducedMotion = true });
            Check("Fit and reduced motion settings", controller.Preferences.Fit == ImageFit.Fit && controller.Preferences.ReducedMotion);
            await controller.ApplyAsync(controller.Preferences with { Order = ImageOrder.ModifiedDate });
            Check("Changing order applies to existing collection", controller.Preferences.Order == ImageOrder.ModifiedDate);
            await controller.ApplyAsync(controller.Preferences with { Order = ImageOrder.Filename, Fit = ImageFit.Fill });
            CaptureSettings(controller, directory, "settings.png");
            var window = Application.Current.Windows.OfType<SettingsWindow>().First();
            var interval = (TextBox)window.FindName("IntervalBox"); interval.Text = "invalid";
            ((Button)window.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("Invalid interval is rejected with focus", interval.IsKeyboardFocused && ((TextBlock)window.FindName("StatusText")).Text.Contains("interval"));
            interval.Text = "10";
            CaptureSettings(controller, directory, "validation.png");
            window.Width = 700; window.Height = 580; window.UpdateLayout();
            CaptureWindow(window, Path.Combine(directory, "compact.png"));
            Check("Compact window retains visible Apply action", ((Button)window.FindName("ApplyButton")).IsVisible);
            await VerifyMotionAsync(Check);
            await VerifyRenderingAsync(directory, Check);
            controller.WriteDiagnostics();
            Check("No unsupported foreground fallback", Application.Current.Windows.OfType<WidgetWindow>().All(w => NativeMethods.GetParent(new System.Windows.Interop.WindowInteropHelper(w).Handle) != 0));
        }
        catch (Exception e) { results.Add(new { Name = "Unhandled verification error", Passed = false, Detail = e.ToString() }); }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "integration-results.json"), System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            controller.Dispose(); Application.Current.Shutdown();
        }
    }

    private static async Task VerifyMotionAsync(Action<string, bool, string?> check)
    {
        var pixels = Enumerable.Repeat((byte)255, 8 * 8 * 4).ToArray();
        var white = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 32);
        white.Freeze();
        var frame = new WidgetWindow { Width = 180, Height = 140, ShowActivated = false };
        try
        {
            frame.Show(); frame.UpdateLayout();
            var preferences = new Preferences { Playing = false, ReducedMotion = true };
            var immediate = frame.ShowPhotoAsync(white, preferences, true);
            check("Reduced motion presents photos immediately", immediate.IsCompletedSuccessfully, null);
            await immediate;
            preferences.ReducedMotion = false;
            for (var direction = 0; direction < 2; direction++)
            {
                var transition = frame.ShowPhotoAsync(white, preferences, true);
                await Task.Delay(800);
                var capture = new RenderTargetBitmap((int)frame.ActualWidth, (int)frame.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                capture.Render(frame);
                var center = new byte[4];
                capture.CopyPixels(new Int32Rect((int)frame.ActualWidth / 2, (int)frame.ActualHeight / 2, 1, 1), center, 4, 0);
                // Two identical white photos must stay white throughout a fade.
                // Simultaneous fade-out/fade-in would expose the dark canvas.
                check($"Photo fade preserves brightness in layer direction {direction + 1}",
                    center[0] >= 245 && center[1] >= 245 && center[2] >= 245,
                    $"Midpoint RGB: {center[2]}, {center[1]}, {center[0]}");
                await transition;
            }
        }
        finally { frame.Close(); }
    }

    private static async Task VerifyRenderingAsync(string directory, Action<string, bool, string?> check)
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var width = (int)(360 * scale); var height = (int)(240 * scale);
            var alpha = AlphaSurface.RoundedCoverage(width, height, 16 * scale);
            check($"Rounded corners have fractional alpha at {scale * 100}% scale",
                alpha[0] == 0 && alpha[(height / 2) * width + width / 2] == 255 && alpha.Any(a => a > 0 && a < 255), null);
            check($"Rounded corners remain symmetric at {scale * 100}% scale",
                Enumerable.Range(0, width).All(x => alpha[x] == alpha[width - 1 - x] && alpha[x] == alpha[(height - 1) * width + x]), null);
        }
        var frame = new WidgetWindow { Width = 360, Height = 240, AllowsTransparency = true, ShowActivated = false };
        try
        {
            frame.Show(); frame.UpdateLayout();
            var pixels = Enumerable.Repeat((byte)255, 8 * 8 * 4).ToArray();
            var white = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 32);
            await frame.ShowPhotoAsync(white, new Preferences { ReducedMotion = true }, false);
            frame.UpdateLayout();
            var capture = new RenderTargetBitmap(360, 240, 96, 96, PixelFormats.Pbgra32);
            capture.Render(frame);
            var rendered = new byte[360 * 240 * 4]; capture.CopyPixels(rendered, 360 * 4, 0);
            check("Edit frame blends curved edges into desktop", rendered[3] == 0 &&
                Enumerable.Range(0, 360 * 16).Any(i => rendered[i * 4 + 3] > 0 && rendered[i * 4 + 3] < 255), null);
            CaptureWindow(frame, Path.Combine(directory, "smooth-corners.png"));
        }
        finally { frame.Close(); }
        var largePath = Path.Combine(directory, "high-resolution.png");
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 3200, 1800));
        var large = new RenderTargetBitmap(3200, 1800, 96, 96, PixelFormats.Pbgra32); large.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(large));
        using (var stream = File.Create(largePath)) encoder.Save(stream);
        var loader = new Services.ImageLoader();
        var small = await loader.LoadAsync(largePath, 720, 480, ImageFit.Fill, CancellationToken.None);
        var big = await loader.LoadAsync(largePath, 2160, 1440, ImageFit.Fill, CancellationToken.None);
        check("High-DPI resize replaces lower-resolution cached photo", big.PixelWidth > small.PixelWidth && big.PixelWidth >= 2160 && big.PixelHeight >= 1440, $"{small.PixelWidth}x{small.PixelHeight} to {big.PixelWidth}x{big.PixelHeight}");
        var original = await loader.LoadAsync(largePath, 8192, 8192, ImageFit.Fill, CancellationToken.None);
        check("Photo decoding preserves original resolution without artificial upscaling", original.PixelWidth == 3200 && original.PixelHeight == 1800, null);
    }

    private static void CaptureSettings(AppController controller, string directory, string name)
    {
        controller.OpenSettings();
        var window = Application.Current.Windows.OfType<SettingsWindow>().First();
        window.LoadPreferences();
        window.UpdateLayout(); CaptureWindow(window, Path.Combine(directory, name));
    }
    private static void CaptureWindow(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void CreatePhoto(string path, int variant)
    {
        // Deterministic vector landscapes exercise actual PNG decoding without external assets.
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var skies = new[] { "#A7D0DE", "#E8C5AD", "#BDC7D5" };
            var hills = new[] { "#416B65", "#9B7161", "#53667E" };
            drawing.DrawRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString(skies[variant])), null, new Rect(0, 0, 1200, 800));
            drawing.DrawEllipse(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF1CF")), null, new Point(850, 200), 95, 95);
            var mountain = new StreamGeometry(); using (var context = mountain.Open())
            {
                context.BeginFigure(new Point(0, 650), true, true);
                context.LineTo(new Point(350, 260), true, false); context.LineTo(new Point(610, 580), true, false);
                context.LineTo(new Point(870, 370), true, false); context.LineTo(new Point(1200, 680), true, false);
                context.LineTo(new Point(1200, 800), true, false); context.LineTo(new Point(0, 800), true, false);
            }
            drawing.DrawGeometry(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hills[variant])), null, mountain);
            drawing.DrawRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#293C46")), null, new Rect(0, 720, 1200, 80));
        }
        var bitmap = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
