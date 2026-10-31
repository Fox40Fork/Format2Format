using Format2Format.Core;

namespace Format2Format;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Log.Info($"Format2Format {Application.ProductVersion} starting with {args.Length} argument(s)");

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
    }

    private static void ReportCrash(Exception ex)
    {
        Log.Error("Unhandled UI exception", ex);
        MessageBox.Show($"Something went wrong:\n\n{ex.Message}\n\nDetails were written to {Log.FilePath}",
            "Format2Format", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
