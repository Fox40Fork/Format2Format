using System.Text;
using Format2Format.Core;
using ImageMagick;

namespace Format2Format.Core.Tests;

public sealed class FormatDetectorTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(MagickFormat.Jpeg, InputFormat.Jpeg)]
    [InlineData(MagickFormat.Png, InputFormat.Png)]
    [InlineData(MagickFormat.Gif, InputFormat.Gif)]
    [InlineData(MagickFormat.WebP, InputFormat.Webp)]
    [InlineData(MagickFormat.Bmp, InputFormat.Bmp)]
    [InlineData(MagickFormat.Tiff, InputFormat.Tiff)]
    [InlineData(MagickFormat.Ico, InputFormat.Ico)]
    public void DetectsRealFiles(MagickFormat written, InputFormat expected)
    {
        string path = TestImages.WriteOpaque(_temp.InputFile("img.bin"), written, 32, 32);

        Assert.Equal(expected, FormatDetector.DetectFile(path));
    }

    [Theory]
    [InlineData("heic", "mif1heic", InputFormat.Heic)]
    [InlineData("mif1", "mif1heic", InputFormat.Heic)]
    [InlineData("avif", "mif1miaf", InputFormat.Avif)]
    [InlineData("mif1", "avifmiaf", InputFormat.Avif)]
    [InlineData("isom", "mp41", InputFormat.Unknown)] // MP4 video, not an image
    public void DetectsHeifFamily(string major, string compatible, InputFormat expected)
    {
        byte[] header = FtypBox(major, compatible);

        Assert.Equal(expected, FormatDetector.Detect(header));
    }

    [Fact]
    public void RejectsNonImages()
    {
        Assert.Equal(InputFormat.Unknown, FormatDetector.Detect("hello, this is text"u8));
        Assert.Equal(InputFormat.Unknown, FormatDetector.Detect("%PDF-1.7"u8));
        Assert.Equal(InputFormat.Unknown, FormatDetector.Detect(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void MissingFileIsUnknown()
    {
        Assert.Equal(InputFormat.Unknown, FormatDetector.DetectFile(_temp.InputFile("nope.png")));
    }

    private static byte[] FtypBox(string major, string compatible)
    {
        var body = Encoding.ASCII.GetBytes("ftyp" + major + "\0\0\0\0" + compatible);
        int size = body.Length + 4;
        return [(byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size, .. body, .. new byte[8]];
    }
}
