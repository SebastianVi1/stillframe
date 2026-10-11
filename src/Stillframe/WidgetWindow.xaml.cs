using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Stillframe.Native;
using Stillframe.Services;

namespace Stillframe;

public partial class WidgetWindow : Window
{
    private bool frontIsCurrent, editing, resizing;
    private System.Drawing.Point resizeOrigin;
    private double originalWidth, originalHeight;
    public event Action? LockRequested;
    public event Action? LayoutChanged;
    public WidgetWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            if (!AllowsTransparency) Background = (Brush)FindResource("Canvas");
            UpdateWindowRegion();
        };
        DpiChanged += (_, _) => { UpdateWindowRegion(); LayoutChanged?.Invoke(); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (!editing || resizing || e.OriginalSource is Button) return;
            try { DragMove(); LayoutChanged?.Invoke(); } catch (InvalidOperationException) { }
        };
        KeyDown += (_, e) => { if (editing && e.Key == Key.Escape) { LockRequested?.Invoke(); e.Handled = true; } };
    }

    public void SetEditMode(bool value)
    {
        editing = value;
        EditOverlay.Visibility = ResizeGrip.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        ShowActivated = value;
        if (value) { Activate(); Focus(); }
    }

    internal Task ShowPhotoAsync(LoadedPhoto photo, Preferences preferences, bool animate) =>
        ShowPhotoAsync(photo.Image, preferences with
        {
            Fit = photo.ShowWholePhoto ? ImageFit.Fit : preferences.Fit,
            SlowZoom = preferences.SlowZoom && !photo.ProtectPeople
        }, animate);

    public async Task ShowPhotoAsync(BitmapSource image, Preferences preferences, bool animate)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        var incoming = frontIsCurrent ? BackImage : FrontImage;
        var outgoing = frontIsCurrent ? FrontImage : BackImage;
        incoming.BeginAnimation(OpacityProperty, null);
        outgoing.BeginAnimation(OpacityProperty, null);
        incoming.Source = image;
        incoming.Stretch = outgoing.Stretch = preferences.Fit == ImageFit.Fill ? Stretch.UniformToFill : Stretch.Uniform;
        var reduced = preferences.ReducedMotion || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast;
        var duration = reduced ? 0 : outgoing.Source == null ? 1000 : animate ? 1600 : 0;
        // The new photo always fades over an opaque old photo. Fading both
        // layers together would expose the dark canvas halfway through.
        Panel.SetZIndex(incoming, 1);
        Panel.SetZIndex(outgoing, 0);
        if (duration > 0)
        {
            incoming.Opacity = 0;
            outgoing.Opacity = 1;
            incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            await Task.Delay(duration);
        }
        incoming.BeginAnimation(OpacityProperty, null);
        outgoing.BeginAnimation(OpacityProperty, null);
        incoming.Opacity = 1;
        outgoing.Opacity = 0;
        outgoing.Source = null;
        frontIsCurrent = !frontIsCurrent;
        foreach (var target in new[] { incoming, outgoing })
        {
            var scale = (ScaleTransform)target.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
        }
        if (preferences.SlowZoom && !reduced && preferences.Fit == ImageFit.Fill)
        {
            var scale = (ScaleTransform)incoming.RenderTransform;
            var zoom = new DoubleAnimation(1, 1.035, TimeSpan.FromSeconds(preferences.IntervalSeconds + duration / 1000.0));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        }
    }

    public void ShowEmpty(string caption)
    {
        foreach (var image in new[] { FrontImage, BackImage })
        {
            image.BeginAnimation(OpacityProperty, null); image.Opacity = 0; image.Source = null;
        }
        EmptyCaption.Text = caption;
        EmptyState.Visibility = Visibility.Visible;
    }

    private void SurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var bounds = new Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height));
        PhotoSurface.Clip = new RectangleGeometry(bounds);
        var mask = new DrawingBrush(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(bounds, 16, 16)));
        mask.Freeze(); FrameRoot.OpacityMask = AllowsTransparency ? mask : null;
        UpdateWindowRegion();
    }
    private void UpdateWindowRegion()
    {
        if (AllowsTransparency) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var dpi = NativeMethods.GetDpiForWindow(handle) / 96.0;
        DesktopHost.ApplyRoundedRegion(handle, (int)Math.Round(Width * dpi), (int)Math.Round(Height * dpi), dpi);
    }
    private void LockClicked(object sender, RoutedEventArgs e) => LockRequested?.Invoke();
    private void ResizeStart(object sender, MouseButtonEventArgs e)
    {
        if (!editing) return;
        resizing = true; resizeOrigin = System.Windows.Forms.Cursor.Position;
        originalWidth = Width; originalHeight = Height; ResizeGrip.CaptureMouse(); e.Handled = true;
    }
    private void ResizeMove(object sender, MouseEventArgs e)
    {
        if (!resizing) return;
        var current = System.Windows.Forms.Cursor.Position;
        var dpi = NativeMethods.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96.0;
        Width = Math.Clamp(originalWidth + (current.X - resizeOrigin.X) / Math.Max(dpi, 1), 180, 1200);
        Height = Math.Clamp(originalHeight + (current.Y - resizeOrigin.Y) / Math.Max(dpi, 1), 140, 1000);
    }
    private void ResizeEnd(object sender, MouseButtonEventArgs e)
    {
        if (!resizing) return;
        resizing = false; ResizeGrip.ReleaseMouseCapture(); LayoutChanged?.Invoke(); e.Handled = true;
    }
}
