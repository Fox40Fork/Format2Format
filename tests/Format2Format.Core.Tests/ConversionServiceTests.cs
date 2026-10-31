using Format2Format.Core;
using ImageMagick;

namespace Format2Format.Core.Tests;

public sealed class ConversionServiceTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ConversionService _service = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<OutputFormat, MagickFormat> AllTargets => new()
    {
        { OutputFormat.Png, MagickFormat.Png },
        { OutputFormat.Jpg, MagickFormat.Jpeg },
        { OutputFormat.Webp, MagickFormat.WebP },
        { OutputFormat.Bmp, MagickFormat.Bmp },
        { OutputFormat.Gif, MagickFormat.Gif },
        { OutputFormat.Tiff, MagickFormat.Tiff },
        { OutputFormat.Ico, MagickFormat.Ico },
    };

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void ConvertsJpegToEveryTarget(OutputFormat target, MagickFormat expected)
    {
        string input = TestImages.WriteOpaque(_temp.InputFile("photo.jpg"), MagickFormat.Jpeg);

        var result = _service.Convert(input, target, _temp.Output);

        Assert.True(result.Success, result.Error);
        Assert.Equal(Path.Combine(_temp.Output, "photo" + ImageFormats.GetExtension(target)), result.OutputPath);
        Assert.Equal(expected, TestImages.FormatOf(result.OutputPath!));
    }

    [Theory]
    [InlineData("picture.jfif", MagickFormat.Jpeg)]
    [InlineData("picture.webp", MagickFormat.WebP)]
    [InlineData("picture.bmp", MagickFormat.Bmp)]
    [InlineData("picture.tif", MagickFormat.Tiff)]
    [InlineData("picture.gif", MagickFormat.Gif)]
    [InlineData("picture.ico", MagickFormat.Ico)]
    public void ConvertsCommonInputsToPng(string fileName, MagickFormat inputFormat)
    {
        string input = TestImages.WriteOpaque(_temp.InputFile(fileName), inputFormat, 32, 32);

        var result = _service.Convert(input, OutputFormat.Png, _temp.Output);

        Assert.True(result.Success, result.Error);
        Assert.Equal("picture.png", Path.GetFileName(result.OutputPath));
        using var output = new MagickImage(result.OutputPath!);
        Assert.Equal(MagickFormat.Png, output.Format);
        Assert.Equal(32u, output.Width);
    }

    [Fact]
    public void ReadsByContentWhenExtensionIsWrong()
    {
        // A PNG that was saved with a .jpg name still converts.
        string input = TestImages.WriteTransparentPng(_temp.InputFile("actually-png.jpg"));

        var result = _service.Convert(input, OutputFormat.Webp, _temp.Output);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void NeverOverwritesExistingFiles()
    {
        string input = TestImages.WriteOpaque(_temp.InputFile("cat.png"), MagickFormat.Png);
        Directory.CreateDirectory(_temp.Output);
        string existing = Path.Combine(_temp.Output, "cat.jpg");
        File.WriteAllText(existing, "do not touch");

        var first = _service.Convert(input, OutputFormat.Jpg, _temp.Output);
        var second = _service.Convert(input, OutputFormat.Jpg, _temp.Output);

        Assert.Equal(Path.Combine(_temp.Output, "cat (1).jpg"), first.OutputPath);
        Assert.Equal(Path.Combine(_temp.Output, "cat (2).jpg"), second.OutputPath);
        Assert.Equal("do not touch", File.ReadAllText(existing));
    }

    [Theory]
    [InlineData(OutputFormat.Jpg)]
    [InlineData(OutputFormat.Bmp)]
    public void FlattensTransparencyOntoWhite(OutputFormat target)
    {
        // 64 px with the green square in the middle, so JPEG's 8×8 blocks at the corner are pure background.
        string input = TestImages.WriteTransparentPng(_temp.InputFile("logo.png"), size: 64);

        var result = _service.Convert(input, target, _temp.Output);

        Assert.True(result.Success, result.Error);
        Assert.Contains("white", result.Note);
        using var output = new MagickImage(result.OutputPath!);
        Assert.False(output.HasAlpha);
        var corner = output.GetPixels().GetPixel(0, 0).ToColor()!;
        Assert.True(corner.R > 245 && corner.G > 245 && corner.B > 245, $"corner should be white but was {corner}");
        var center = output.GetPixels().GetPixel(32, 32).ToColor()!;
        Assert.True(center.G > 200 && center.R < 60, $"center should stay green but was {center}");
    }

    [Fact]
    public void KeepsTransparencyForPngAndWebp()
    {
        string input = TestImages.WriteTransparentPng(_temp.InputFile("logo.png"));

        var result = _service.Convert(input, OutputFormat.Webp, _temp.Output);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Note);
        using var output = new MagickImage(result.OutputPath!);
        Assert.True(output.HasAlpha);
        Assert.Equal(0, output.GetPixels().GetPixel(0, 0).ToColor()!.A);
    }

    [Fact]
    public void QualityAffectsJpegSize()
    {
        string input = Path.Combine(_temp.Input, "noise.png");
        using (var noise = new MagickImage("plasma:", 200, 200))
            noise.Write(input, MagickFormat.Png);

        var low = _service.Convert(input, OutputFormat.Jpg, _temp.Output, new ConversionOptions { Quality = 10 });
        var high = _service.Convert(input, OutputFormat.Jpg, _temp.Output, new ConversionOptions { Quality = 95 });

        Assert.True(new FileInfo(low.OutputPath!).Length < new FileInfo(high.OutputPath!).Length);
    }

    [Fact]
    public void KeepsAnimationFromGifToWebp()
    {
        string input = TestImages.WriteAnimatedGif(_temp.InputFile("anim.gif"), frames: 3);

        var result = _service.Convert(input, OutputFormat.Webp, _temp.Output);

        Assert.True(result.Success, result.Error);
        Assert.Contains("animation kept", result.Note, StringComparison.OrdinalIgnoreCase);
        using var output = new MagickImageCollection(result.OutputPath!);
        Assert.Equal(3, output.Count);
    }

    [Fact]
    public void ConvertsFirstFrameOfAnimationToPng()
    {
        string input = TestImages.WriteAnimatedGif(_temp.InputFile("anim.gif"), frames: 3);

        var result = _service.Convert(input, OutputFormat.Png, _temp.Output);

        Assert.True(result.Success, result.Error);
        Assert.Contains("first frame", result.Note, StringComparison.OrdinalIgnoreCase);
        using var output = new MagickImageCollection(result.OutputPath!);
        Assert.Single(output);
        var pixel = output[0].GetPixels().GetPixel(0, 0).ToColor()!;
        Assert.True(pixel.R > 200 && pixel.G < 50, "first frame is red");
    }

    [Fact]
    public void LargeImageToIcoIsScaledToMultiSizeIcon()
    {
        string input = TestImages.WriteOpaque(_temp.InputFile("big.png"), MagickFormat.Png, 600, 400);

        var result = _service.Convert(input, OutputFormat.Ico, _temp.Output);

        Assert.True(result.Success, result.Error);
        Assert.Contains("256", result.Note);
        using var icon = new MagickImageCollection(result.OutputPath!);
        Assert.Equal([256u, 128u, 64u, 48u, 32u, 16u], icon.Select(i => i.Width).OrderDescending());
        Assert.All(icon, i => Assert.Equal(i.Width, i.Height));
    }

    [Fact]
    public void UnsupportedExtensionFailsWithClearMessage()
    {
        string input = _temp.InputFile("notes.txt");
        File.WriteAllText(input, "hello");

        var result = _service.Convert(input, OutputFormat.Png, _temp.Output);

        Assert.False(result.Success);
        Assert.Equal("Unsupported file type (.txt)", result.Error);
        Assert.False(Directory.Exists(_temp.Output) && Directory.EnumerateFiles(_temp.Output).Any());
    }

    [Fact]
    public void NonImageWithImageExtensionFails()
    {
        string input = _temp.InputFile("fake.png");
        File.WriteAllText(input, "this is not a png");

        var result = _service.Convert(input, OutputFormat.Jpg, _temp.Output);

        Assert.False(result.Success);
        Assert.StartsWith("Not a supported image", result.Error);
    }

    [Fact]
    public void TruncatedImageFailsAndLeavesNoOutputFile()
    {
        string good = TestImages.WriteOpaque(_temp.InputFile("good.png"), MagickFormat.Png, 300, 300);
        byte[] bytes = File.ReadAllBytes(good);
        string input = _temp.InputFile("broken.png");
        File.WriteAllBytes(input, bytes[..60]);

        var result = _service.Convert(input, OutputFormat.Jpg, _temp.Output);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.DoesNotContain("@ error/", result.Error);
        Assert.False(File.Exists(Path.Combine(_temp.Output, "broken.jpg")));
    }

    [Fact]
    public void MissingFileFails()
    {
        var result = _service.Convert(_temp.InputFile("gone.png"), OutputFormat.Jpg, _temp.Output);

        Assert.False(result.Success);
        Assert.Equal("File not found", result.Error);
    }

    [Fact]
    public void HandlesBracketsInFileNames()
    {
        // ImageMagick would read "[1]" as a frame selector if we passed the path to it directly.
        string input = TestImages.WriteOpaque(_temp.InputFile("scan [1].png"), MagickFormat.Png);

        var result = _service.Convert(input, OutputFormat.Jpg, _temp.Output);

        Assert.True(result.Success, result.Error);
        Assert.Equal("scan [1].jpg", Path.GetFileName(result.OutputPath));
    }

    [Fact]
    public void AvifRoundTripIsSupported()
    {
        string input = TestImages.WriteOpaque(_temp.InputFile("pic.avif"), MagickFormat.Avif, 32, 32);

        var result = _service.Convert(input, OutputFormat.Png, _temp.Output);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void HeicDecoderIsAvailable()
    {
        // We can't create HEIC files in tests (no encoder ships with Magick.NET), but the decoder must be there.
        var heic = MagickNET.SupportedFormats.FirstOrDefault(f => f.Format == MagickFormat.Heic);
        Assert.NotNull(heic);
        Assert.True(heic.SupportsReading);
    }

    [Theory]
    [InlineData("Not a JPEG file: starts with 0x89 0x50 `/tmp/x.jpg' @ error/jpeg.c/ReadJPEGImage/1234", "Not a JPEG file: starts with 0x89 0x50")]
    [InlineData("improper image header `x.bmp' @ error/bmp.c/ReadBMPImage/1", "Improper image header")]
    public void CleansImageMagickMessages(string raw, string expected)
    {
        Assert.Equal(expected, ConversionService.CleanMagickMessage(raw));
    }
}
