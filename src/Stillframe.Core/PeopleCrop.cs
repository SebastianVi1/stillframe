namespace Stillframe.Core;

// All coordinates are normalized against the correctly oriented photo.
public readonly record struct PhotoBounds(double X, double Y, double Width, double Height);

public static class PeopleCrop
{
    public static PhotoBounds Choose(double imageAspect, double frameAspect, IReadOnlyList<PhotoBounds> faces)
    {
        var full = new PhotoBounds(0, 0, 1, 1);
        if (faces.Count == 0 || !double.IsFinite(imageAspect) || !double.IsFinite(frameAspect) || imageAspect <= 0 || frameAspect <= 0)
            return full;
        var width = Math.Min(1, frameAspect / imageAspect);
        var height = Math.Min(1, imageAspect / frameAspect);
        // Include headroom and shoulders rather than treating the face box as the entire person.
        var left = faces.Min(f => Math.Max(0, f.X - f.Width * .4));
        var right = faces.Max(f => Math.Min(1, f.X + f.Width * 1.4));
        var top = faces.Min(f => Math.Max(0, f.Y - f.Height * .8));
        var bottom = faces.Max(f => Math.Min(1, f.Y + f.Height * 2));
        // Fill always retains its original scale. Center the group as closely as the frame permits.
        var x = Math.Clamp((left + right - width) / 2, 0, 1 - width);
        var y = Math.Clamp((top + bottom - height) / 2, 0, 1 - height);
        var faceLeft = faces.Min(f => f.X); var faceRight = faces.Max(f => f.X + f.Width);
        var faceTop = faces.Min(f => f.Y); var faceBottom = faces.Max(f => f.Y + f.Height);
        if (faceRight - faceLeft <= width)
            x = Math.Clamp(x, Math.Max(0, faceRight - width), Math.Min(faceLeft, 1 - width));
        if (faceBottom - faceTop <= height)
            y = Math.Clamp(y, Math.Max(0, faceBottom - height), Math.Min(faceTop, 1 - height));
        return new(x, y, width, height);
    }
}
