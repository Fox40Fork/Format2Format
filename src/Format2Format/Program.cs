using System.Runtime.InteropServices;
using System.Security.Principal;
using Format2Format.Core;

namespace Format2Format;

internal static class Program
{
    private const string RegisterSwitch = "--register-shell";
    private const string UnregisterSwitch = "--unregister-shell";

    /// <returns>Process exit code (only meaningful for the --register-shell / --unregister-shell switches).</returns>
    [STAThread]
    private static int Main(string[] args)
    {
        Log.Info($"Format2Format {Application.ProductVersion.Split('+')[0]} starting with {args.Length} argument(s)");

        if (args.Length == 1 && args[0] is RegisterSwitch or UnregisterSwitch)
            return RunShellSwitch(args[0]);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        // The other process has a different working directory, so always pass absolute paths.
        string[] files = args.Select(ToFullPath).OfType<string>().ToArray();

        // Explorer starts one process per selected file. The first becomes the window; the rest hand over
        // their file through a named pipe and exit, so everything lands in one list.
        using var instance = new SingleInstance(GetInstanceId());
        if (!instance.TryBecomePrimary())
        {
            AllowSetForegroundWindow(ASFW_ANY); // lets the existing window come to the front
            if (instance.TrySendToPrimary(files))
            {
                Log.Info($"Handed {files.Length} file(s) to the running window");
                return 0;
            }

            // The running window didn't answer; it may be closing. Wait briefly, then open our own window.
            if (!instance.TryBecomePrimary(timeoutMs: 3000))
                Log.Warn("The running window didn't respond; opening a separate window");
        }

        ApplicationConfiguration.Initialize();
        var form = new MainForm(files);
        if (instance.IsPrimary)
        {
            instance.ArgumentsReceived += form.AcceptExternalFiles;
            instance.StartServer();
        }

        Application.Run(form);
        return 0;
    }

    private static int RunShellSwitch(string option)
    {
        try
        {
            if (option == RegisterSwitch)
                ShellIntegration.Register(Application.ExecutablePath);
            else
                ShellIntegration.Unregister();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error($"{option} failed", ex);
            return 1;
        }
    }

    /// <summary>One window per user per logon session.</summary>
    private static string GetInstanceId()
    {
        string user;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            user = identity.User?.Value ?? Environment.UserName;
        }
        catch (Exception)
        {
            user = Environment.UserName;
        }

        // Named pipes are machine-wide, so include the session too (same user signed in twice, e.g. over RDP).
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return $"Format2Format-{user}-{process.SessionId}";
    }

    private static string? ToFullPath(string arg)
    {
        try
        {
            return Path.GetFullPath(arg.Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Warn($"Ignoring invalid argument \"{arg}\"");
            return null;
        }
    }

    private static void ReportCrash(Exception ex)
    {
        Log.Error("Unhandled UI exception", ex);
        MessageBox.Show($"Something went wrong:\n\n{ex.Message}\n\nDetails were written to {Log.FilePath}",
            "Format2Format", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
