using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Stillframe.Services;

namespace Stillframe.Native;

internal sealed class DesktopHost
{
    private nint parent, shellView, hwnd, container;
    private long originalStyle, originalExStyle;
    private readonly AlphaSurface surface = new();
    private WidgetWindow? widget;
    private bool dirty;
    private int pixelWidth, pixelHeight;
    private double scale = 1;
    private TimeSpan lastFrame;
    public bool HasRenderedFrame => surface.HasFrame;
    public bool Attached => hwnd != 0 && NativeMethods.IsWindow(hwnd) && parent != 0 && NativeMethods.IsWindow(parent) &&
        NativeMethods.GetParent(hwnd) == (container != 0 ? container : parent) &&
        (container == 0 || NativeMethods.IsWindow(container) && NativeMethods.GetParent(container) == parent);
    public bool ModernDesktop { get; private set; }
    public string Status { get; private set; } = "Not connected";
    public nint Handle => container != 0 ? container : hwnd;

    public bool Attach(Window window, Preferences preferences)
    {
        if (Attached) { Place(preferences, window); return true; }
        if (container != 0) Detach();
        hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return false;
        if (originalStyle == 0)
        {
            originalStyle = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle);
            originalExStyle = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        }
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman == 0) return Fail("Windows Explorer is unavailable. Desktop connection will retry.");
        NativeMethods.SendMessageTimeout(progman, 0x052C, 0xD, 1, 2, 1000, out _);
        ModernDesktop = ((long)NativeMethods.GetWindowLongPtr(progman, NativeMethods.GwlExStyle) & NativeMethods.WsExNoRedirectionBitmap) != 0;
        shellView = NativeMethods.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        nint worker = NativeMethods.FindWindowEx(progman, 0, "WorkerW", null);
        if (!ModernDesktop)
        {
            NativeMethods.EnumWindows((top, _) =>
            {
                var view = NativeMethods.FindWindowEx(top, 0, "SHELLDLL_DefView", null);
                if (view != 0)
                {
                    shellView = view;
                    worker = NativeMethods.FindWindowEx(0, top, "WorkerW", null);
                    return false;
                }
                return true;
            }, 0);
        }
        var target = ModernDesktop ? progman : worker;
        if (target == 0 || shellView == 0) return Fail("Desktop layer is unavailable. Try reconnecting from Settings.");
        if (window is not WidgetWindow photoWindow) return Fail("A photo frame is required.");
        widget = photoWindow;
        // The native surface owns per-pixel transparency on both Explorer layouts.
        container = NativeMethods.CreateWindowEx((uint)(NativeMethods.WsExLayered | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow),
            "STATIC", "Stillframe desktop surface", 0x56000000, 0, 0, 1, 1, target, 0, 0, 0);
        if (container == 0) return Fail($"Desktop surface creation failed ({Marshal.GetLastPInvokeError()}).");
        var style = (originalStyle & ~NativeMethods.WsPopup) | NativeMethods.WsChild;
        var extended = (originalExStyle & ~NativeMethods.WsExAppWindow) | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        // The WPF child uses ordinary painting; the native parent owns layering.
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, (nint)style);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, (nint)extended);
        Marshal.SetLastPInvokeError(0);
        var contentParent = container != 0 ? container : target;
        NativeMethods.SetParent(hwnd, contentParent);
        var error = Marshal.GetLastPInvokeError();
        if (NativeMethods.GetParent(hwnd) != contentParent)
        {
            RestoreStyles();
            if (container != 0) { NativeMethods.DestroyWindow(container); container = 0; }
            return Fail($"Desktop attachment failed ({error}: {new Win32Exception(error).Message}).");
        }
        parent = target;
        widget.FrameChanged += Invalidate;
        CompositionTarget.Rendering += RenderFrame;
        Place(preferences, window);
        Status = "Attached to desktop";
        Log.Write($"Desktop attached: hwnd={hwnd:X}, surface={container:X}, parent={parent:X}, shell={shellView:X}, modern={ModernDesktop}, build={Environment.OSVersion.Version.Build}, error={error}");
        return true;
    }

    public void Place(Preferences preferences, Window window)
    {
        if (!Attached) return;
        var point = new NativeMethods.Point(preferences.X, preferences.Y);
        NativeMethods.MapWindowPoints(0, parent, ref point, 1);
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        scale = dpi == 0 ? 1 : dpi / 96.0;
        var width = (int)Math.Round(window.Width * scale);
        var height = (int)Math.Round(window.Height * scale);
        var resized = pixelWidth != width || pixelHeight != height;
        pixelWidth = width; pixelHeight = height;
        NativeMethods.SetWindowPos(Handle, ModernDesktop ? shellView : 0, point.X, point.Y, width, height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged | NativeMethods.SwpShowWindow);
        if (container != 0)
            // Keep the WPF render source outside the visible native surface.
            // An opaque child painted over it would erase its fractional alpha.
            NativeMethods.SetWindowPos(hwnd, 0, -width - 1, 0, width, height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged | NativeMethods.SwpShowWindow);
        if (resized || !surface.HasFrame) Invalidate();
        if (ModernDesktop)
        {
            var wallpaper = NativeMethods.FindWindowEx(parent, 0, "WorkerW", null);
            if (wallpaper != 0)
                NativeMethods.SetWindowPos(wallpaper, new nint(1), 0, 0, 0, 0,
                    NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }
    }

    private void Invalidate() => dirty = true;

    private void RenderFrame(object? sender, EventArgs e)
    {
        if (!Attached || widget == null || (!dirty && !widget.IsAnimating)) return;
        var time = ((RenderingEventArgs)e).RenderingTime;
        var interval = pixelWidth * (long)pixelHeight > 1_000_000 ? 33 : 16;
        if (!dirty && (time - lastFrame).TotalMilliseconds < interval) return;
        try
        {
            widget.UpdateLayout();
            surface.Present(container, widget.PhotoVisual, pixelWidth, pixelHeight, scale);
            dirty = false; lastFrame = time;
        }
        catch (Exception error) when (error is Win32Exception or ArgumentException or InvalidOperationException)
        {
            Log.Write("Desktop frame rendering failed: " + error.Message);
            Detach(); Status = "Desktop rendering failed. Reconnect from Settings.";
            widget?.Hide();
        }
    }

    public void Detach()
    {
        CompositionTarget.Rendering -= RenderFrame;
        if (widget != null) widget.FrameChanged -= Invalidate;
        surface.Dispose();
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
        {
            if (container != 0 && NativeMethods.IsWindow(container)) NativeMethods.DestroyWindow(container);
            container = parent = 0; return;
        }
        NativeMethods.GetWindowRect(Handle, out var bounds);
        NativeMethods.SetParent(hwnd, 0);
        RestoreStyles();
        if (container != 0) { NativeMethods.DestroyWindow(container); container = 0; }
        parent = 0;
        NativeMethods.SetWindowPos(hwnd, 0, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
            NativeMethods.SwpFrameChanged | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        Status = "Editing layout";
    }

    private void RestoreStyles()
    {
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, (nint)originalStyle);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, (nint)originalExStyle);
    }
    private bool Fail(string text) { Status = text; Log.Write(text); return false; }
}
