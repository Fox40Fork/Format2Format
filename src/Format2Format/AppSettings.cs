using System.Text.Json;
using Format2Format.Core;

namespace Format2Format;

/// <summary>Remembers the last chosen format and quality in %LOCALAPPDATA%\Format2Format\settings.json.</summary>
internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(Log.DefaultDirectory, "settings.json");

    public string Format { get; set; } = nameof(OutputFormat.Png);

    public int Quality { get; set; } = ConversionOptions.DefaultQuality;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read settings; using defaults", ex);
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn("Could not save settings", ex);
        }
    }
}
