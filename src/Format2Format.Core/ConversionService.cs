using ImageMagick;

namespace Format2Format.Core;

/// <summary>
/// Converts one image file to another format. Stateless and thread-safe; contains no UI code.
/// </summary>
public sealed class ConversionService
{
    /// <summary>The ICO format can't store images larger than 256×256.</summary>
    public const int MaxIconSize = 256;

    private static readonly uint[] IconSizes = [256, 128, 64, 48, 32, 16];

    /// <summary>
    /// Converts <paramref name="inputPath"/> to <paramref name="target"/> and writes the result into
    /// <paramref name="outputDirectory"/> as "original name + new extension" (" (1)", " (2)", ... if taken).
    /// Never throws for bad input; failures are returned as a result with a short <see cref="ConversionResult.Error"/>.
    /// </summary>
    public ConversionResult Convert(string inputPath, OutputFormat target, string outputDirectory, ConversionOptions? options = null)
    {
        options ??= ConversionOptions.Default;

        try
        {
            if (!File.Exists(inputPath))
                return ConversionResult.Fail(inputPath, "File not found");

            if (!ImageFormats.HasSupportedExtension(inputPath))
                return ConversionResult.Fail(inputPath, $"Unsupported file type ({DescribeExtension(inputPath)})");

            var sourceFormat = FormatDetector.DetectFile(inputPath);
            if (sourceFormat == InputFormat.Unknown)
                return ConversionResult.Fail(inputPath, "Not a supported image (the file content isn't a recognized image format)");

            using var source = Read(inputPath, sourceFormat);
            if (source.Count == 0)
                return ConversionResult.Fail(inputPath, "The file contains no image data");

            var notes = new List<string>();
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            string extension = ImageFormats.GetExtension(target);
            var magickFormat = ImageFormats.ToMagickFormat(target);
            string outputPath;

            if (ShouldKeepAnimation(source, sourceFormat, target))
            {
                PrepareAnimation(source, target, options);
                notes.Add($"animation kept ({source.Count} frames)");
                outputPath = WriteNewFile(outputDirectory, baseName, extension, s => source.Write(s, magickFormat));
            }
            else
            {
                using var image = TakeSingleImage(source, sourceFormat, notes);
                PrepareSingleImage(image, target, options, notes);

                if (target == OutputFormat.Ico)
                {
                    using var icon = BuildIcon(image, notes);
                    outputPath = WriteNewFile(outputDirectory, baseName, extension, s => icon.Write(s, magickFormat));
                }
                else
                {
                    outputPath = WriteNewFile(outputDirectory, baseName, extension, s => image.Write(s, magickFormat));
                }
            }

            string? note = notes.Count > 0 ? Capitalize(string.Join("; ", notes)) : null;
            Log.Info($"Converted \"{inputPath}\" ({sourceFormat}) -> \"{outputPath}\"{(note is null ? "" : $" [{note}]")}");
            return ConversionResult.Ok(inputPath, outputPath, note);
        }
        catch (Exception ex)
        {
            string reason = DescribeError(ex);
            Log.Error($"Failed to convert \"{inputPath}\" to {target}: {reason}", ex);
            return ConversionResult.Fail(inputPath, reason);
        }
    }

    private static MagickImageCollection Read(string path, InputFormat format)
    {
        // Read through a stream rather than a path: ImageMagick treats characters like [ ] * ? in file names
        // as frame selectors/wildcards, so "photo[1].jpg" would otherwise fail or read the wrong thing.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var images = new MagickImageCollection();
        try
        {
            images.Read(stream, new MagickReadSettings { Format = ImageFormats.ToMagickFormat(format) });
            return images;
        }
        catch
        {
            images.Dispose();
            throw;
        }
    }

    /// <summary>Animated GIF/WEBP → GIF/WEBP is simple enough to keep; everything else becomes a single image.</summary>
    private static bool ShouldKeepAnimation(MagickImageCollection source, InputFormat sourceFormat, OutputFormat target) =>
        source.Count > 1 &&
        sourceFormat is InputFormat.Gif or InputFormat.Webp &&
        ImageFormats.SupportsAnimation(target);

    private static void PrepareAnimation(MagickImageCollection frames, OutputFormat target, ConversionOptions options)
    {
        // Turn delta frames into full frames so each one survives re-encoding with a different format.
        frames.Coalesce();

        if (target == OutputFormat.Webp)
        {
            foreach (var frame in frames)
                ApplyWebpQuality(frame, options.Quality);
        }
    }

    private static IMagickImage<byte> TakeSingleImage(MagickImageCollection source, InputFormat sourceFormat, List<string> notes)
    {
        if (source.Count == 1)
            return source[0].Clone();

        switch (sourceFormat)
        {
            case InputFormat.Ico:
                // Icons hold the same picture at several sizes; the largest is the useful one.
                return source.OrderByDescending(i => (long)i.Width * i.Height).ThenByDescending(i => i.Depth).First().Clone();

            case InputFormat.Gif:
            case InputFormat.Webp:
                notes.Add("animated image: only the first frame was converted");
                var first = source[0];
                if (first.Page.X != 0 || first.Page.Y != 0 ||
                    (first.Page.Width != 0 && (first.Page.Width != first.Width || first.Page.Height != first.Height)))
                {
                    // The first frame only covers part of the canvas; rebuild the full frame.
                    source.Coalesce();
                }

                return source[0].Clone();

            case InputFormat.Tiff:
                notes.Add($"multi-page TIFF ({source.Count} pages): only the first page was converted");
                return source[0].Clone();

            default:
                notes.Add($"file contains {source.Count} images: only the first was converted");
                return source[0].Clone();
        }
    }

    private static void PrepareSingleImage(IMagickImage<byte> image, OutputFormat target, ConversionOptions options, List<string> notes)
    {
        // Phone photos (JPEG/JFIF/HEIC) often store rotation as an EXIF flag that many apps ignore. Bake it in.
        image.AutoOrient();
        image.ResetPage();

        // PNG, GIF, WEBP etc. can't hold CMYK, and CMYK JPEGs display badly in many apps. Use sRGB everywhere.
        if (image.ColorSpace == ColorSpace.CMYK)
        {
            if (image.GetColorProfile() != null)
                image.TransformColorSpace(ColorProfiles.SRGB);
            else
                image.ColorSpace = ColorSpace.sRGB;
        }

        if (!ImageFormats.SupportsTransparency(target) && image.HasAlpha)
        {
            if (!image.IsOpaque)
                notes.Add("transparency flattened onto white");

            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            image.Alpha(AlphaOption.Off);
        }

        switch (target)
        {
            case OutputFormat.Jpg:
                image.Quality = (uint)ClampQuality(options.Quality);
                break;

            case OutputFormat.Webp:
                ApplyWebpQuality(image, options.Quality);
                break;

            case OutputFormat.Tiff:
                // Otherwise ImageMagick may reuse the source's compression (e.g. JPEG-in-TIFF, which many apps can't open).
                image.Settings.Compression = CompressionMethod.LZW;
                break;
        }
    }

    private static void ApplyWebpQuality(IMagickImage<byte> image, int quality)
    {
        quality = ClampQuality(quality);
        image.Quality = (uint)quality;
        if (quality >= 100)
            image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
    }

    /// <summary>
    /// Builds a multi-resolution icon (256, 128, 64, 48, 32, 16 px, up to the source size). Non-square images
    /// are padded with transparency so they aren't stretched; anything over 256 px is scaled down.
    /// </summary>
    private static MagickImageCollection BuildIcon(IMagickImage<byte> image, List<string> notes)
    {
        uint side = Math.Max(image.Width, image.Height);

        image.Alpha(AlphaOption.Set);
        if (image.Width != image.Height)
        {
            image.BackgroundColor = MagickColors.Transparent;
            image.Extent(side, side, Gravity.Center);
            notes.Add("padded to a square");
        }

        if (side > MaxIconSize)
        {
            notes.Add($"scaled down from {side}×{side} to {MaxIconSize}×{MaxIconSize} (largest icon size)");
            side = MaxIconSize;
        }

        var sizes = IconSizes.Where(s => s <= side).ToList();
        if (sizes.Count == 0)
            sizes.Add(side); // tiny image (< 16 px): keep as-is

        var icon = new MagickImageCollection();
        foreach (uint size in sizes)
        {
            var frame = image.Clone();
            if (frame.Width != size)
                frame.Resize(size, size);
            icon.Add(frame);
        }

        return icon;
    }

    /// <summary>Encodes into a freshly created file; deletes the file again if encoding fails.</summary>
    private static string WriteNewFile(string directory, string baseName, string extension, Action<Stream> write)
    {
        string path;
        using (var stream = OutputPathResolver.CreateUniqueFile(directory, baseName, extension, out path))
        {
            try
            {
                write(stream);
                stream.Flush();
                return path;
            }
            catch
            {
                stream.Dispose();
                TryDelete(path);
                throw;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not delete incomplete output \"{path}\"", ex);
        }
    }

    private static int ClampQuality(int quality) => Math.Clamp(quality, 1, 100);

    private static string DescribeExtension(string path)
    {
        string ext = Path.GetExtension(path);
        return string.IsNullOrEmpty(ext) ? "no extension" : ext.ToLowerInvariant();
    }

    internal static string DescribeError(Exception ex) => ex switch
    {
        MagickCorruptImageErrorException => "The file is damaged or incomplete",
        MagickMissingDelegateErrorException => "This image's encoding isn't supported",
        MagickResourceLimitErrorException => "The image is too large to process",
        MagickException magick => CleanMagickMessage(magick.Message),
        UnauthorizedAccessException => "Access denied",
        FileNotFoundException => "File not found",
        DirectoryNotFoundException => "Folder not found",
        IOException io => io.Message,
        _ => ex.Message,
    };

    /// <summary>
    /// ImageMagick messages look like "Not a JPEG file: starts with 0x89 0x50 `C:\x.jpg' @ error/jpeg.c/ReadJPEGImage/1234".
    /// Keep just the human part.
    /// </summary>
    internal static string CleanMagickMessage(string message)
    {
        int at = message.IndexOf(" @ ", StringComparison.Ordinal);
        if (at >= 0)
            message = message[..at];

        int quote = message.IndexOf(" `", StringComparison.Ordinal);
        if (quote >= 0)
            message = message[..quote];

        message = message.Trim().TrimEnd('.', ':', ' ');
        return message.Length == 0 ? "Conversion failed" : Capitalize(message);
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
