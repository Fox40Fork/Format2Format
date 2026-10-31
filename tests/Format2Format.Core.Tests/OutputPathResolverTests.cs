using Format2Format.Core;

namespace Format2Format.Core.Tests;

public sealed class OutputPathResolverTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void UsesPlainNameWhenFree()
    {
        using var stream = OutputPathResolver.CreateUniqueFile(_temp.Output, "photo", ".png", out string path);

        Assert.Equal(Path.Combine(_temp.Output, "photo.png"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void AppendsCounterOnCollision()
    {
        Directory.CreateDirectory(_temp.Output);
        File.WriteAllText(Path.Combine(_temp.Output, "photo.png"), "existing");
        File.WriteAllText(Path.Combine(_temp.Output, "photo (1).png"), "existing");

        using var stream = OutputPathResolver.CreateUniqueFile(_temp.Output, "photo", ".png", out string path);

        Assert.Equal(Path.Combine(_temp.Output, "photo (2).png"), path);
    }

    [Fact]
    public void FillsFirstGap()
    {
        Directory.CreateDirectory(_temp.Output);
        File.WriteAllText(Path.Combine(_temp.Output, "a.jpg"), "x");
        File.WriteAllText(Path.Combine(_temp.Output, "a (2).jpg"), "x");

        Assert.Equal(Path.Combine(_temp.Output, "a (1).jpg"), OutputPathResolver.GetAvailablePath(_temp.Output, "a", ".jpg"));
    }

    [Fact]
    public void TreatsFolderWithSameNameAsTaken()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Output, "photo.png"));

        using var stream = OutputPathResolver.CreateUniqueFile(_temp.Output, "photo", ".png", out string path);

        Assert.Equal(Path.Combine(_temp.Output, "photo (1).png"), path);
    }

    [Fact]
    public void ConcurrentCallersNeverGetTheSameFile()
    {
        var paths = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 20, _ =>
        {
            using var stream = OutputPathResolver.CreateUniqueFile(_temp.Output, "img", ".gif", out string path);
            paths.Add(path);
        });

        Assert.Equal(20, paths.Distinct().Count());
    }

    [Theory]
    [InlineData(0, "x.webp")]
    [InlineData(1, "x (1).webp")]
    [InlineData(12, "x (12).webp")]
    public void CandidateNames(int index, string expected)
    {
        Assert.Equal(Path.Combine("dir", expected), OutputPathResolver.GetCandidatePath("dir", "x", ".webp", index));
    }
}
