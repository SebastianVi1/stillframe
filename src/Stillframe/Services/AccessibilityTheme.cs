using System.ComponentModel;

namespace Stillframe.Services;

internal static class AccessibilityTheme
{
    public static void Initialize()
    {
        Apply();
        SystemParameters.StaticPropertyChanged += OnSystemChange;
    }

    private static void OnSystemChange(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Application.Current.Dispatcher.BeginInvoke(Apply);
    }

    private static void Apply()
    {
        var resources = Application.Current.Resources;
        if (SystemParameters.HighContrast)
        {
            resources["Canvas"] = SystemColors.WindowBrush;
            resources["Surface"] = SystemColors.WindowBrush;
            resources["Elevated"] = SystemColors.ControlBrush;
            resources["Text"] = SystemColors.WindowTextBrush;
            resources["Muted"] = SystemColors.WindowTextBrush;
            resources["Accent"] = SystemColors.HighlightBrush;
            resources["OnAccent"] = SystemColors.HighlightTextBrush;
            resources["Line"] = SystemColors.WindowTextBrush;
            resources["Error"] = SystemColors.WindowTextBrush;
        }
        else
        {
            foreach (var (brush, color) in new[] { ("Canvas", "CanvasColor"), ("Surface", "SurfaceColor"), ("Elevated", "ElevatedColor"), ("Text", "TextColor"), ("Muted", "MutedColor"), ("Accent", "AccentColor"), ("OnAccent", "OnAccentColor"), ("Line", "BorderColor"), ("Error", "ErrorColor") })
                resources[brush] = new SolidColorBrush((Color)Application.Current.FindResource(color));
        }
    }
}
