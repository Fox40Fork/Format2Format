using ImageMagick;

namespace Format2Format.Core;

/// <summary>Formats offered in the "Convert to" dropdown.</summary>
public enum OutputFormat
{
    Png,
    Jpg,
    Webp,
    Bmp,
    Gif,
    Tiff,
    Ico,
}

/// <summary>Input formats, detected from the file's content (not its extension).</summary>
public enum InputFormat
{
    Unknown,
    Jpeg,
    Png,
    Webp,
    Bmp,
    Gif,
    Tiff,
    Ico,
    Heic,
    Avif,
}

/// <summary>Static facts about the formats Format2Format reads and writes.</summary>
public static class ImageFormats
{
    /// <summary>File extensions accepted as input (lower case, with leading dot).</summary>
    public static IReadOnlyList<string> InputExtensions { get; } =
    [
        ".jpg", ".jpeg", ".jfif", ".png", ".webp", ".bmp", ".gif",
        ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif",
    ];

    public static IReadOnlyList<OutputFormat> OutputFormats { get; } = Enum.GetValues<OutputFormat>();

    public static bool HasSupportedExtension(string path) =>
        InputExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string GetExtension(OutputFormat format) => format switch
    {
        OutputFormat.Png => ".png",
        OutputFormat.Jpg => ".jpg",
        OutputFormat.Webp => ".webp",
        OutputFormat.Bmp => ".bmp",
        OutputFormat.Gif => ".gif",
        OutputFormat.Tiff => ".tiff",
        OutputFormat.Ico => ".ico",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static string GetDisplayName(OutputFormat format) => format.ToString().ToUpperInvariant();

    public static bool TryParseOutputFormat(string? text, out OutputFormat format) =>
        Enum.TryParse(text, ignoreCase: true, out format) && Enum.IsDefined(format);

    /// <summary>JPG and WEBP are the only lossy targets, so only they take a quality value.</summary>
    public static bool SupportsQuality(OutputFormat format) => format is OutputFormat.Jpg or OutputFormat.Webp;

    public static bool SupportsTransparency(OutputFormat format) => format is not (OutputFormat.Jpg or OutputFormat.Bmp);

    public static bool SupportsAnimation(OutputFormat format) => format is OutputFormat.Gif or OutputFormat.Webp;

    /// <summary>Text for the Format column. JPEG files named .jfif are shown as JFIF since that's what users know them as.</summary>
    public static string GetDisplayName(InputFormat format, string? path = null)
    {
        if (format == InputFormat.Jpeg && path != null &&
            Path.GetExtension(path).Equals(".jfif", StringComparison.OrdinalIgnoreCase))
        {
            return "JFIF";
        }

        return format switch
        {
            InputFormat.Unknown => path is null ? "?" : Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
            _ => format.ToString().ToUpperInvariant(),
        };
    }

    internal static MagickFormat ToMagickFormat(OutputFormat format) => format switch
    {
        OutputFormat.Png => MagickFormat.Png,
        OutputFormat.Jpg => MagickFormat.Jpeg,
        OutputFormat.Webp => MagickFormat.WebP,
        OutputFormat.Bmp => MagickFormat.Bmp,
        OutputFormat.Gif => MagickFormat.Gif,
        OutputFormat.Tiff => MagickFormat.Tiff,
        OutputFormat.Ico => MagickFormat.Ico,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    internal static MagickFormat ToMagickFormat(InputFormat format) => format switch
    {
        InputFormat.Jpeg => MagickFormat.Jpeg,
        InputFormat.Png => MagickFormat.Png,
        InputFormat.Webp => MagickFormat.WebP,
        InputFormat.Bmp => MagickFormat.Bmp,
        InputFormat.Gif => MagickFormat.Gif,
        InputFormat.Tiff => MagickFormat.Tiff,
        InputFormat.Ico => MagickFormat.Ico,
        InputFormat.Heic => MagickFormat.Heic,
        InputFormat.Avif => MagickFormat.Avif,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
