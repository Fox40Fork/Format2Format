using System.Text;

namespace Format2Format.Core;

/// <summary>
/// Minimal thread-safe file logger. Writes to %LOCALAPPDATA%\Format2Format\log.txt by default and
/// keeps one previous file (log.old.txt) once the log passes 1 MB. Logging never throws.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Format2Format");

    public static string FilePath { get; private set; } = Path.Combine(DefaultDirectory, "log.txt");

    /// <summary>Redirects the log (used by tests).</summary>
    public static void Configure(string filePath)
    {
        lock (Gate)
            FilePath = filePath;
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level).Append("] [pid ").Append(Environment.ProcessId).Append("] ")
            .Append(message);
        if (ex != null)
            line.AppendLine().Append(ex);
        line.AppendLine();

        lock (Gate)
        {
            // Several processes may write at once (single-instance hand-off), so retry briefly on sharing violations.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    RollIfTooLarge();
                    File.AppendAllText(FilePath, line.ToString(), Encoding.UTF8);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(15);
                }
                catch
                {
                    return; // never let logging break the app
                }
            }
        }
    }

    private static void RollIfTooLarge()
    {
        var info = new FileInfo(FilePath);
        if (info.Exists && info.Length > MaxBytes)
            File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.txt"), overwrite: true);
    }
}
