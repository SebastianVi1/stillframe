using System.Windows.Media.Imaging;

namespace Stillframe.Services;

internal sealed class ImageLoader
{
    private readonly object gate = new();
    private readonly Dictionary<(string Path, int Width, int Height, ImageFit Fit), BitmapSource> cache = new();
    private readonly Queue<(string Path, int Width, int Height, ImageFit Fit)> order = new();
    private int generation;
    public void Clear() { lock (gate) { generation++; cache.Clear(); order.Clear(); } }

    public Task<BitmapSource> LoadAsync(string path, int decodeWidth, int decodeHeight, ImageFit fit, CancellationToken token)
    {
        int version;
        var key = (Path: path.ToUpperInvariant(), Width: Math.Clamp(decodeWidth, 360, 8192), Height: Math.Clamp(decodeHeight, 280, 8192), Fit: fit);
        lock (gate)
        {
            version = generation;
            if (cache.TryGetValue(key, out var existing)) return Task.FromResult(existing);
        }
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            var pixelWidth = Math.Max(1, header.PixelWidth);
            var pixelHeight = Math.Max(1, header.PixelHeight);
            var orientation = 1;
            try
            {
                if (header.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("/app1/ifd/{ushort=274}"))
                    orientation = Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}"));
            }
            catch (Exception e) when (e is NotSupportedException or IOException or ArgumentException) { }
            var rotated = orientation is >= 5 and <= 8;
            var widthRatio = (double)key.Width / (rotated ? pixelHeight : pixelWidth);
            var heightRatio = (double)key.Height / (rotated ? pixelWidth : pixelHeight);
            var requested = fit == ImageFit.Fill ? Math.Max(widthRatio, heightRatio) : Math.Min(widthRatio, heightRatio);
            var factor = Math.Min(1.0, Math.Min(requested,
                Math.Min(8192.0 / Math.Max(pixelWidth, pixelHeight), Math.Sqrt(16_000_000.0 / pixelWidth / pixelHeight))));
            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = Math.Max(1, (int)Math.Floor(pixelWidth * factor));
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            token.ThrowIfCancellationRequested();
            BitmapSource result = bitmap;
            // Windows phone/camera JPEGs may store orientation rather than rotated pixels.
            try
            {
                stream.Position = 0;
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames[0].Metadata is BitmapMetadata metadata && metadata.ContainsQuery("/app1/ifd/{ushort=274}"))
                {
                    Transform? transform = orientation switch
                    {
                        2 => new ScaleTransform(-1, 1),
                        3 => new RotateTransform(180),
                        4 => new ScaleTransform(1, -1),
                        5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
                        6 => new RotateTransform(90),
                        7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
                        8 => new RotateTransform(270),
                        _ => null
                    };
                    if (transform != null) { var oriented = new TransformedBitmap(bitmap, transform); oriented.Freeze(); result = oriented; }
                }
            }
            catch (Exception e) when (e is NotSupportedException or IOException or ArgumentException) { }
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (generation != version) return result;
                if (!cache.ContainsKey(key)) order.Enqueue(key);
                cache[key] = result;
                while (order.Count > 3) cache.Remove(order.Dequeue());
            }
            return result;
        }, token);
    }
}
