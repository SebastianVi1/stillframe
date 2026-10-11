using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Stillframe.Native;

// Explorer needs an independent layered HWND. Publish premultiplied pixels
// rather than a binary window region, which cannot antialias a curved edge.
internal sealed class AlphaSurface : IDisposable
{
    private nint dc, bitmap, previous, bits;
    private byte[] pixels = [], coverage = [];
    private RenderTargetBitmap? target;
    private int width, height;
    private double dpi;
    public bool HasFrame { get; private set; }
    public byte CenterAlpha { get; private set; }

    public void Present(nint handle, FrameworkElement visual, int pixelWidth, int pixelHeight, double scale)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || visual.ActualWidth <= 0 || visual.ActualHeight <= 0) return;
        if (width != pixelWidth || height != pixelHeight || dpi != scale * 96)
        {
            Dispose();
            width = pixelWidth; height = pixelHeight; dpi = scale * 96;
            dc = NativeMethods.CreateCompatibleDC(0);
            var info = new NativeMethods.BitmapInfo
            {
                Size = 40,
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                SizeImage = checked((uint)(width * height * 4))
            };
            bitmap = NativeMethods.CreateDIBSection(dc, ref info, 0, out bits, 0, 0);
            if (dc == 0 || bitmap == 0) { Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
            previous = NativeMethods.SelectObject(dc, bitmap);
            pixels = new byte[checked(width * height * 4)];
            coverage = RoundedCoverage(width, height, 16 * scale);
            target = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        }
        target!.Clear(); target.Render(visual);
        target.CopyPixels(pixels, width * 4, 0);
        CenterAlpha = pixels[((height / 2) * width + width / 2) * 4 + 3];
        if (!HasFrame) Services.Log.Write($"First desktop pixels: {width}x{height}, center alpha={CenterAlpha}, visual visible={visual.IsVisible}");
        for (var y = 0; y < coverage.Length; y++)
        {
            var alpha = coverage[y];
            if (alpha == 255) continue;
            var offset = y * 4;
            for (var channel = 0; channel < 4; channel++)
                pixels[offset + channel] = (byte)((pixels[offset + channel] * alpha + 127) / 255);
        }
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        var size = new NativeMethods.Size(width, height);
        var source = new NativeMethods.Point(0, 0);
        var blend = new NativeMethods.BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
        if (!NativeMethods.UpdateLayeredWindow(handle, 0, 0, ref size, dc, ref source, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        HasFrame = true;
    }

    internal static byte[] RoundedCoverage(int width, int height, double radius)
    {
        var result = Enumerable.Repeat((byte)255, checked(width * height)).ToArray();
        radius = Math.Min(radius, Math.Min(width, height) / 2.0);
        var corner = (int)Math.Ceiling(radius);
        // Eight samples per axis retain fractional coverage at every DPI.
        for (var y = 0; y < corner; y++)
            for (var x = 0; x < corner; x++)
            {
                var inside = 0;
                for (var sy = 0; sy < 8; sy++)
                    for (var sx = 0; sx < 8; sx++)
                    {
                        var dx = x + (sx + 0.5) / 8 - radius;
                        var dy = y + (sy + 0.5) / 8 - radius;
                        if (dx * dx + dy * dy <= radius * radius) inside++;
                    }
                var alpha = (byte)((inside * 255 + 32) / 64);
                result[y * width + x] = result[y * width + width - 1 - x] = alpha;
                result[(height - 1 - y) * width + x] = result[(height - 1 - y) * width + width - 1 - x] = alpha;
            }
        return result;
    }

    public void Dispose()
    {
        if (dc != 0 && previous != 0) NativeMethods.SelectObject(dc, previous);
        if (bitmap != 0) NativeMethods.DeleteObject(bitmap);
        if (dc != 0) NativeMethods.DeleteDC(dc);
        dc = bitmap = previous = bits = 0;
        target = null; pixels = coverage = []; width = height = 0; HasFrame = false; CenterAlpha = 0;
    }
}
