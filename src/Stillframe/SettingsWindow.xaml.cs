using System.Globalization;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Stillframe.Native;
using Stillframe.Services;

namespace Stillframe;

public partial class SettingsWindow : Window
{
    private readonly AppController controller;
    private bool loading;
    public SettingsWindow(AppController controller)
    {
        this.controller = controller; InitializeComponent(); LoadPreferences();
        SourceInitialized += (_, _) => { var corner = 2; NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref corner, 4); };
        Closing += (_, _) => controller.SettingsClosed(this);
    }
    public void LoadPreferences()
    {
        loading = true;
        var p = controller.Preferences;
        FolderBox.Text = p.Folder; SubfoldersBox.IsChecked = p.IncludeSubfolders;
        IntervalBox.Text = p.IntervalSeconds.ToString(CultureInfo.InvariantCulture);
        OrderBox.SelectedIndex = (int)p.Order; FitBox.SelectedIndex = (int)p.Fit;
        WidthBox.Text = p.Width.ToString(CultureInfo.InvariantCulture); HeightBox.Text = p.Height.ToString(CultureInfo.InvariantCulture);
        XBox.Text = p.X.ToString(CultureInfo.InvariantCulture); YBox.Text = p.Y.ToString(CultureInfo.InvariantCulture);
        ShapeBox.SelectedIndex = Math.Abs(p.Width - p.Height) < 1 ? 1 : p.Width > p.Height ? 0 : 2;
        MonitorBox.Items.Clear();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens) MonitorBox.Items.Add(new ComboBoxItem { Content = $"Display {MonitorBox.Items.Count + 1}", Tag = screen.DeviceName });
        MonitorBox.SelectedIndex = Math.Max(0, System.Windows.Forms.Screen.AllScreens.ToList().FindIndex(s => s.DeviceName == p.Monitor));
        CenterPeopleBox.IsChecked = p.CenterPeople; ZoomBox.IsChecked = p.SlowZoom; ReducedBox.IsChecked = p.ReducedMotion; StartupBox.IsChecked = p.StartWithWindows;
        loading = false; UpdateStatus();
    }
    public void UpdateStatus()
    {
        ConnectionText.Text = controller.ConnectionStatus;
        PhotoCount.Text = controller.Count == 0 ? "0 photos" : $"Photo {controller.Position} of {controller.Count}";
        PhotoName.Text = string.IsNullOrEmpty(controller.CurrentPath) ? "No photo selected" : Path.GetFileName(controller.CurrentPath);
        PlayButton.Content = controller.Preferences.Playing ? "Pause" : "Play";
        AutomationProperties.SetName(PlayButton, controller.Preferences.Playing ? "Pause slideshow" : "Play slideshow");
        LockButton.Visibility = controller.Editing ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.IsEnabled = controller.Count > 0;
        StatusText.Text = controller.Status;
        StatusText.Foreground = (Brush)FindResource(controller.HasError ? "Error" : "Muted");
    }
    internal void UpdatePhoto(LoadedPhoto? photo)
    {
        PreviewImage.Source = photo?.Image; PreviewImage.Stretch = controller.Preferences.Fit == ImageFit.Fill && photo?.ShowWholePhoto != true && photo?.ProtectPeople != true ? Stretch.UniformToFill : Stretch.Uniform;
        PreviewEmpty.Visibility = photo == null ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }
    public void UpdateLayoutFields()
    {
        WidthBox.Text = Math.Round(controller.Preferences.Width).ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = Math.Round(controller.Preferences.Height).ToString(CultureInfo.InvariantCulture);
        XBox.Text = controller.Preferences.X.ToString(CultureInfo.InvariantCulture);
        YBox.Text = controller.Preferences.Y.ToString(CultureInfo.InvariantCulture);
        UpdateStatus();
    }
    private void BrowseClicked(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose your photo folder", Multiselect = false };
        if (Directory.Exists(FolderBox.Text)) picker.InitialDirectory = FolderBox.Text;
        if (picker.ShowDialog(this) == true) { FolderBox.Text = picker.FolderName; ApplyClicked(sender, e); }
    }
    private void SamplesClicked(object sender, RoutedEventArgs e)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper");
        if (!Directory.Exists(root)) { ShowValidation("Windows sample photos are unavailable. Choose your own folder.", FolderBox); return; }
        var preferred = new[] { "ThemeA", "Spotlight", "Windows" }.Select(d => Path.Combine(root, d)).FirstOrDefault(Directory.Exists) ?? root;
        FolderBox.Text = preferred; SubfoldersBox.IsChecked = true; ApplyClicked(sender, e);
    }
    private async void ApplyClicked(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim().Trim('"');
        if (folder.Length > 0 && !Directory.Exists(folder)) { ShowValidation("That folder cannot be opened. Check its path or choose another folder.", FolderBox); return; }
        if (!TryNumber(IntervalBox, 2, 3600, "Enter an interval from 2 to 3,600 seconds.", out var interval)) return;
        if (!TryNumber(WidthBox, 180, 1200, "Enter a width from 180 to 1,200.", out var width)) return;
        if (!TryNumber(HeightBox, 140, 1000, "Enter a height from 140 to 1,000.", out var height)) return;
        if (!int.TryParse(XBox.Text, out var x)) { ShowValidation("Enter a whole number for the left position.", XBox); return; }
        if (!int.TryParse(YBox.Text, out var y)) { ShowValidation("Enter a whole number for the top position.", YBox); return; }
        var monitor = (MonitorBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var p = controller.Preferences with
        {
            Folder = folder,
            IncludeSubfolders = SubfoldersBox.IsChecked == true,
            IntervalSeconds = interval,
            Width = width,
            Height = height,
            X = x,
            Y = y,
            Monitor = monitor,
            Order = (ImageOrder)Math.Max(0, OrderBox.SelectedIndex),
            Fit = (ImageFit)Math.Max(0, FitBox.SelectedIndex),
            CenterPeople = CenterPeopleBox.IsChecked == true,
            SlowZoom = ZoomBox.IsChecked == true,
            ReducedMotion = ReducedBox.IsChecked == true,
            StartWithWindows = StartupBox.IsChecked == true
        };
        ApplyButton.IsEnabled = false;
        try { await controller.ApplyAsync(p); UpdateLayoutFields(); }
        finally { ApplyButton.IsEnabled = true; }
    }
    private bool TryNumber(TextBox box, double minimum, double maximum, string message, out double value)
    {
        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value >= minimum && value <= maximum) return true;
        ShowValidation(message, box); return false;
    }
    private void ShowValidation(string message, TextBox focus)
    {
        StatusText.Text = message; StatusText.Foreground = (Brush)FindResource("Error"); focus.Focus(); focus.SelectAll();
    }
    private void ShapeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || WidthBox == null) return;
        var shape = ShapeBox.SelectedIndex;
        if (shape == 3) return;
        WidthBox.Text = shape == 2 ? "280" : "360";
        HeightBox.Text = shape == 0 ? "240" : shape == 1 ? "360" : "400";
    }
    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e) => PreviewSurface.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);
    private async void PreviousClicked(object sender, RoutedEventArgs e) => await controller.AdvanceAsync(-1);
    private async void NextClicked(object sender, RoutedEventArgs e) => await controller.AdvanceAsync(1);
    private void PlayClicked(object sender, RoutedEventArgs e) => controller.TogglePlayback();
    private void EditClicked(object sender, RoutedEventArgs e) => controller.BeginEdit();
    private void LockClicked(object sender, RoutedEventArgs e) => controller.EndEdit();
    private async void RefreshClicked(object sender, RoutedEventArgs e) => await controller.RefreshAsync();
    private void ReconnectClicked(object sender, RoutedEventArgs e) => controller.Reconnect();
}
