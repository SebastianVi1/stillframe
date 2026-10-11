using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stillframe.Core;

public enum ImageOrder { Filename, ModifiedDate, Shuffle }
public enum ImageFit { Fit, Fill }

public sealed record Preferences
{
    public string Folder { get; set; } = "";
    public bool IncludeSubfolders { get; set; }
    public double IntervalSeconds { get; set; } = 10;
    public ImageOrder Order { get; set; } = ImageOrder.Filename;
    public ImageFit Fit { get; set; } = ImageFit.Fill;
    public bool CenterPeople { get; set; } = true;
    public bool SlowZoom { get; set; }
    public bool ReducedMotion { get; set; }
    public bool Playing { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 240;
    // Position uses physical screen pixels, including negative coordinates on secondary displays.
    public int X { get; set; } = 80;
    public int Y { get; set; } = 100;
    public string Monitor { get; set; } = "";
    public string LastImage { get; set; } = "";

    public void Normalize()
    {
        Folder ??= "";
        Monitor ??= "";
        LastImage ??= "";
        IntervalSeconds = double.IsFinite(IntervalSeconds) ? Math.Clamp(IntervalSeconds, 2, 3600) : 10;
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 180, 1200) : 360;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 140, 1000) : 240;
        if (!Enum.IsDefined(Order)) Order = ImageOrder.Filename;
        if (!Enum.IsDefined(Fit)) Fit = ImageFit.Fill;
    }
}

public sealed class PreferencesStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string DirectoryPath => directory;
    public string FilePath => Path.Combine(directory, "settings.json");
    public string? LoadWarning { get; private set; }

    public Preferences Load()
    {
        try
        {
            var value = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath), Options) ?? new()
                : new Preferences();
            value.Normalize();
            return value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadWarning = "Your saved settings could not be read. Default settings are being used.";
            return new();
        }
    }

    public void Save(Preferences value)
    {
        value.Normalize();
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
