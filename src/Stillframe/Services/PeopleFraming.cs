using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;

namespace Stillframe.Services;

internal sealed record LoadedPhoto(BitmapSource Image, bool ShowWholePhoto = false, bool ProtectPeople = false);

internal static class PeopleFraming
{
    internal static async Task<LoadedPhoto> FrameAsync(BitmapSource image, double frameAspect, CancellationToken token)
    {
        try
        {
            if (!FaceDetector.IsSupported || !FaceDetector.IsBitmapPixelFormatSupported(BitmapPixelFormat.Gray8)) return new(image);
            token.ThrowIfCancellationRequested();
            var scale = Math.Min(1, 960.0 / Math.Max(image.PixelWidth, image.PixelHeight));
            var preview = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            var gray = new FormatConvertedBitmap(preview, PixelFormats.Gray8, null, 0);
            gray.Freeze();
            var pixels = new byte[gray.PixelWidth * gray.PixelHeight];
            gray.CopyPixels(pixels, gray.PixelWidth, 0);
            using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Gray8, gray.PixelWidth, gray.PixelHeight);
            bitmap.CopyFromBuffer(pixels.AsBuffer());
            var detector = await FaceDetector.CreateAsync().AsTask(token);
            var detected = await detector.DetectFacesAsync(bitmap).AsTask(token);
            token.ThrowIfCancellationRequested();
            var faces = detected.Select(f => new PhotoBounds((double)f.FaceBox.X / gray.PixelWidth,
                (double)f.FaceBox.Y / gray.PixelHeight, (double)f.FaceBox.Width / gray.PixelWidth,
                (double)f.FaceBox.Height / gray.PixelHeight)).ToArray();
            if (faces.Length == 0) return new(image);
            var decision = PeopleCrop.Choose((double)image.PixelWidth / image.PixelHeight, frameAspect, faces);
            if (decision.ShowWholePhoto) return new(image, true, true);
            var b = decision.Bounds;
            var x = Math.Clamp((int)Math.Floor(b.X * image.PixelWidth), 0, image.PixelWidth - 1);
            var y = Math.Clamp((int)Math.Floor(b.Y * image.PixelHeight), 0, image.PixelHeight - 1);
            var width = Math.Clamp((int)Math.Round(b.Width * image.PixelWidth), 1, image.PixelWidth - x);
            var height = Math.Clamp((int)Math.Round(b.Height * image.PixelHeight), 1, image.PixelHeight - y);
            var cropped = new CroppedBitmap(image, new Int32Rect(x, y, width, height));
            cropped.Freeze();
            return new(cropped, false, true);
        }
        catch (Exception error) when (error is COMException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            // A detector failure must never interrupt playback or discard a readable photo.
            Log.Write("People framing unavailable: " + error.GetType().Name);
            return new(image);
        }
    }
}
