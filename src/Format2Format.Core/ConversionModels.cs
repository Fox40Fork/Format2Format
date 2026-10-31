namespace Format2Format.Core;

public sealed record ConversionOptions
{
    public const int DefaultQuality = 90;

    /// <summary>1-100. Used for JPG and WEBP only. For WEBP, 100 means lossless.</summary>
    public int Quality { get; init; } = DefaultQuality;

    public static ConversionOptions Default { get; } = new();
}

public sealed record ConversionResult
{
    public required string InputPath { get; init; }

    public bool Success { get; init; }

    /// <summary>Full path of the written file when <see cref="Success"/> is true.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Optional extra information for a successful conversion, e.g. "only the first frame was converted".</summary>
    public string? Note { get; init; }

    /// <summary>Short, user-facing reason when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

    public static ConversionResult Ok(string inputPath, string outputPath, string? note = null) =>
        new() { InputPath = inputPath, Success = true, OutputPath = outputPath, Note = note };

    public static ConversionResult Fail(string inputPath, string error) =>
        new() { InputPath = inputPath, Success = false, Error = error };
}
