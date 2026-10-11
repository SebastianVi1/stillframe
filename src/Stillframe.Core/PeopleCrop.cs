namespace Stillframe.Core;

// All coordinates are normalized against the correctly oriented photo.
public readonly record struct PhotoBounds(double X, double Y, double Width, double Height);
public readonly record struct CropDecision(PhotoBounds Bounds, bool ShowWholePhoto);

public static class PeopleCrop
{
    public static CropDecision Choose(double imageAspect, double frameAspect, IReadOnlyList<PhotoBounds> faces)
    {
        var full = new PhotoBounds(0, 0, 1, 1);
        if (faces.Count == 0 || !double.IsFinite(imageAspect) || !double.IsFinite(frameAspect) || imageAspect <= 0 || frameAspect <= 0)
            return new(full, false);
        var width = Math.Min(1, frameAspect / imageAspect);
        var height = Math.Min(1, imageAspect / frameAspect);
        // Include headroom and shoulders rather than treating the face box as the entire person.
        var left = faces.Min(f => Math.Max(0, f.X - f.Width * .4));
        var right = faces.Max(f => Math.Min(1, f.X + f.Width * 1.4));
        var top = faces.Min(f => Math.Max(0, f.Y - f.Height * .8));
        var bottom = faces.Max(f => Math.Min(1, f.Y + f.Height * 2));
        if (right - left > width || bottom - top > height) return new(full, true);
        var x = Math.Clamp((left + right - width) / 2, Math.Max(0, right - width), Math.Min(left, 1 - width));
        var y = Math.Clamp((top + bottom - height) / 2, Math.Max(0, bottom - height), Math.Min(top, 1 - height));
        return new(new(x, y, width, height), false);
    }
}
