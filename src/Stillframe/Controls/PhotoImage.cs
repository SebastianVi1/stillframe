namespace Stillframe.Controls;

// Keep the complete decoded photo and move its Fill viewport without creating another bitmap.
public sealed class PhotoImage : Image
{
    public static readonly DependencyProperty FocalPointProperty = DependencyProperty.Register(nameof(FocalPoint), typeof(Point),
        typeof(PhotoImage), new FrameworkPropertyMetadata(new Point(.5, .5), FrameworkPropertyMetadataOptions.AffectsRender));
    public Point FocalPoint { get => (Point)GetValue(FocalPointProperty); set => SetValue(FocalPointProperty, value); }
    public PhotoImage() => ClipToBounds = true;

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? Source?.Width ?? 0 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? Source?.Height ?? 0 : availableSize.Height);
    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    protected override void OnRender(DrawingContext drawing)
    {
        if (Source == null || Source.Width <= 0 || Source.Height <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var scale = Stretch == Stretch.Uniform
            ? Math.Min(ActualWidth / Source.Width, ActualHeight / Source.Height)
            : Math.Max(ActualWidth / Source.Width, ActualHeight / Source.Height);
        var width = Source.Width * scale;
        var height = Source.Height * scale;
        var x = Stretch == Stretch.Uniform ? (ActualWidth - width) / 2
            : Math.Clamp(ActualWidth / 2 - width * FocalPoint.X, Math.Min(0, ActualWidth - width), 0);
        var y = Stretch == Stretch.Uniform ? (ActualHeight - height) / 2
            : Math.Clamp(ActualHeight / 2 - height * FocalPoint.Y, Math.Min(0, ActualHeight - height), 0);
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        drawing.DrawImage(Source, new Rect(x, y, width, height));
        drawing.Pop();
    }
}
