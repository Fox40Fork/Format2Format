using System.Text;

namespace Format2Format.Core;

/// <summary>
/// Identifies an image format from its first bytes ("magic numbers").
/// </summary>
/// <remarks>
/// We tell ImageMagick the exact decoder to use instead of letting it guess. That way a file with the
/// wrong extension still converts, and files that aren't one of our supported image types are never
/// handed to ImageMagick's many other decoders (PDF, SVG, MVG, text, ...).
/// </remarks>
public static class FormatDetector
{
    /// <summary>Number of leading bytes needed for detection.</summary>
    public const int HeaderLength = 64;

    private static readonly string[] AvifBrands = ["avif", "avis"];
    private static readonly string[] HeicBrands = ["heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs"];
    private static readonly string[] GenericHeifBrands = ["mif1", "msf1"];

    /// <summary>Reads the start of a file and detects its format. Returns Unknown if the file can't be read.</summary>
    public static InputFormat DetectFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> buffer = stackalloc byte[HeaderLength];
            int read = stream.ReadAtLeast(buffer, HeaderLength, throwOnEndOfStream: false);
            return Detect(buffer[..read]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return InputFormat.Unknown;
        }
    }

    public static InputFormat Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(stackalloc byte[] { 0xFF, 0xD8, 0xFF }))
            return InputFormat.Jpeg;

        if (header.StartsWith(stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return InputFormat.Png;

        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
            return InputFormat.Gif;

        if (header.Length >= 12 && header.StartsWith("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8))
            return InputFormat.Webp;

        if (header.StartsWith("II*\0"u8) || header.StartsWith("MM\0*"u8) ||   // classic TIFF
            header.StartsWith("II+\0"u8) || header.StartsWith("MM\0+"u8))     // BigTIFF
            return InputFormat.Tiff;

        // ICONDIR: reserved=0, type=1 (icon), count>0
        if (header.Length >= 6 && header.StartsWith(stackalloc byte[] { 0x00, 0x00, 0x01, 0x00 }) &&
            (header[4] | header[5]) != 0)
            return InputFormat.Ico;

        if (header.Length >= 14 && header.StartsWith("BM"u8))
            return InputFormat.Bmp;

        return DetectHeif(header);
    }

    /// <summary>HEIC and AVIF are both ISO-BMFF ("ftyp" box) containers; the brand list tells them apart.</summary>
    private static InputFormat DetectHeif(ReadOnlySpan<byte> header)
    {
        if (header.Length < 16 || !header.Slice(4, 4).SequenceEqual("ftyp"u8))
            return InputFormat.Unknown;

        int boxSize = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        int end = Math.Min(header.Length, boxSize < 16 ? header.Length : boxSize);

        var brands = new List<string> { Encoding.ASCII.GetString(header.Slice(8, 4)) }; // major brand
        for (int i = 16; i + 4 <= end; i += 4)                                           // compatible brands
            brands.Add(Encoding.ASCII.GetString(header.Slice(i, 4)));

        string major = brands[0];
        if (AvifBrands.Contains(major)) return InputFormat.Avif;
        if (HeicBrands.Contains(major)) return InputFormat.Heic;
        if (brands.Any(b => AvifBrands.Contains(b))) return InputFormat.Avif;
        if (brands.Any(b => HeicBrands.Contains(b) || GenericHeifBrands.Contains(b))) return InputFormat.Heic;
        return InputFormat.Unknown;
    }
}
