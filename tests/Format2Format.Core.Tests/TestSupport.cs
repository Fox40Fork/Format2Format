using Format2Format.Core;
using ImageMagick;

namespace Format2Format.Core.Tests;

/// <summary>Creates an isolated input/output folder per test and points the log at it.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "f2f-tests", Guid.NewGuid().ToString("N"));
        Input = Directory.CreateDirectory(Path.Combine(Root, "in")).FullName;
        Output = Path.Combine(Root, "out"); // not created on purpose: the service must create it
        Log.Configure(Path.Combine(Root, "log.txt"));
    }

    public string Root { get; }

    public string Input { get; }

    public string Output { get; }

    public string InputFile(string name) => Path.Combine(Input, name);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

internal static class TestImages
{
    /// <summary>Opaque image, left half red and right half blue.</summary>
    public static string WriteOpaque(string path, MagickFormat format, uint width = 40, uint height = 30)
    {
        using var image = new MagickImage(MagickColors.Red, width, height);
        using var blue = new MagickImage(MagickColors.Blue, width / 2, height);
        image.Composite(blue, (int)(width / 2), 0, CompositeOperator.Over);
        image.Write(path, format);
        return path;
    }

    /// <summary>Fully transparent image with an opaque green square in the middle.</summary>
    public static string WriteTransparentPng(string path, uint size = 20)
    {
        using var image = new MagickImage(MagickColors.Transparent, size, size);
        using var green = new MagickImage(MagickColors.Lime, size / 2, size / 2);
        image.Composite(green, (int)(size / 4), (int)(size / 4), CompositeOperator.Over);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    public static string WriteAnimatedGif(string path, int frames = 3)
    {
        using var collection = new MagickImageCollection();
        var colors = new[] { MagickColors.Red, MagickColors.Lime, MagickColors.Blue, MagickColors.Yellow };
        for (int i = 0; i < frames; i++)
        {
            var frame = new MagickImage(colors[i % colors.Length], 16, 16) { AnimationDelay = 10 };
            collection.Add(frame);
        }

        collection.Write(path, MagickFormat.Gif);
        return path;
    }

    public static MagickFormat FormatOf(string path) => new MagickImageInfo(path).Format;
}
