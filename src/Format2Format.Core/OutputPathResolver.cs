namespace Format2Format.Core;

/// <summary>
/// Picks output file names: "name.ext", then "name (1).ext", "name (2).ext", ... Never overwrites.
/// </summary>
public static class OutputPathResolver
{
    public const int MaxAttempts = 10_000;

    public static string GetCandidatePath(string directory, string baseName, string extension, int index) =>
        Path.Combine(directory, index == 0 ? baseName + extension : $"{baseName} ({index}){extension}");

    /// <summary>Returns the first candidate path that doesn't exist yet. (Not atomic; see <see cref="CreateUniqueFile"/>.)</summary>
    public static string GetAvailablePath(string directory, string baseName, string extension)
    {
        for (int i = 0; i < MaxAttempts; i++)
        {
            string candidate = GetCandidatePath(directory, baseName, extension, i);
            if (!Exists(candidate))
                return candidate;
        }

        throw new IOException($"No free file name for \"{baseName}{extension}\" after {MaxAttempts} attempts.");
    }

    /// <summary>
    /// Atomically creates a new, empty file with the first free name and returns it open for writing.
    /// Uses <see cref="FileMode.CreateNew"/>, so even another process racing for the same name can't
    /// make us overwrite a file.
    /// </summary>
    public static FileStream CreateUniqueFile(string directory, string baseName, string extension, out string path)
    {
        Directory.CreateDirectory(directory);

        for (int i = 0; i < MaxAttempts; i++)
        {
            string candidate = GetCandidatePath(directory, baseName, extension, i);
            if (Exists(candidate))
                continue;

            try
            {
                var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                path = candidate;
                return stream;
            }
            catch (IOException) when (Exists(candidate))
            {
                // Someone else created it between our check and CreateNew; try the next name.
            }
        }

        throw new IOException($"No free file name for \"{baseName}{extension}\" after {MaxAttempts} attempts.");
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
