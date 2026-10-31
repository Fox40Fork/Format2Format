using Format2Format.Core;

namespace Format2Format;

internal enum FileState
{
    Pending,
    Converting,
    Done,
    Failed,
    Skipped,
}

/// <summary>One row in the file list.</summary>
internal sealed class FileEntry(string path, InputFormat format, long size)
{
    public string Path { get; } = path;

    public string Name => System.IO.Path.GetFileName(Path);

    public InputFormat Format { get; } = format;

    public long Size { get; } = size;

    public FileState State { get; set; } = FileState.Pending;

    /// <summary>Failure/skip reason, or a note for a successful conversion.</summary>
    public string? Message { get; set; }

    public string? OutputPath { get; set; }

    /// <summary>Target format of the last successful conversion, so pressing Convert again doesn't redo it.</summary>
    public OutputFormat? ConvertedTo { get; set; }

    public bool CanConvert => State != FileState.Skipped;

    public string StatusText => State switch
    {
        FileState.Pending => "Pending",
        FileState.Converting => "Converting…",
        FileState.Done => $"Done → {System.IO.Path.GetFileName(OutputPath)}" + (Message is null ? "" : $". {Message}"),
        FileState.Failed => $"Failed: {Message}",
        FileState.Skipped => $"Skipped: {Message}",
        _ => State.ToString(),
    };

    public static FileEntry Inspect(string path)
    {
        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Skip(path, InputFormat.Unknown, 0, "Can't read the file");
        }

        // Same rule as the converter: the content decides, the extension only shapes the message.
        var format = FormatDetector.DetectFile(path);
        return format != InputFormat.Unknown
            ? new FileEntry(path, format, size)
            : Skip(path, format, size, ConversionService.DescribeUnrecognized(path));
    }

    private static FileEntry Skip(string path, InputFormat format, long size, string reason) =>
        new(path, format, size) { State = FileState.Skipped, Message = reason };
}
