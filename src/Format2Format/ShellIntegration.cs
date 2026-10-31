using System.Runtime.InteropServices;
using Format2Format.Core;
using Microsoft.Win32;

namespace Format2Format;

/// <summary>
/// The "Convert with Format2Format" entry in Explorer's right-click menu for image files.
/// </summary>
/// <remarks>
/// Written per user under HKCU\Software\Classes\SystemFileAssociations\&lt;ext&gt;\shell\Format2Format, so no
/// admin rights are needed and it doesn't replace whatever app opens the file type. On Windows 11 the entry
/// appears under "Show more options" (classic menu), as for all static verbs.
/// </remarks>
internal static class ShellIntegration
{
    private const string VerbName = "Format2Format";
    private const string MenuText = "Convert with Format2Format";

    public static IReadOnlyList<string> Extensions => ImageFormats.InputExtensions;

    private static string AssociationKey(string extension) => $@"Software\Classes\SystemFileAssociations\{extension}";

    private static string VerbKey(string extension) => $@"{AssociationKey(extension)}\shell\{VerbName}";

    public static bool IsRegistered()
    {
        foreach (string extension in Extensions)
        {
            using var key = Registry.CurrentUser.OpenSubKey(VerbKey(extension));
            if (key != null)
                return true;
        }

        return false;
    }

    public static void Register(string exePath)
    {
        foreach (string extension in Extensions)
        {
            using var verb = Registry.CurrentUser.CreateSubKey(VerbKey(extension));
            verb.SetValue("", MenuText);
            verb.SetValue("MUIVerb", MenuText);
            verb.SetValue("Icon", $"\"{exePath}\",0");
            // Without this, Explorer hides static verbs when more than 15 files are selected.
            verb.SetValue("MultiSelectModel", "Player");

            using var command = verb.CreateSubKey("command");
            command.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        NotifyShell();
        Log.Info($"Registered the right-click menu entry for \"{exePath}\"");
    }

    public static void Unregister()
    {
        foreach (string extension in Extensions)
        {
            Registry.CurrentUser.DeleteSubKeyTree(VerbKey(extension), throwOnMissingSubKey: false);

            // Tidy up the parent keys too, but only if nothing else lives there.
            DeleteIfEmpty($@"{AssociationKey(extension)}\shell");
            DeleteIfEmpty(AssociationKey(extension));
        }

        NotifyShell();
        Log.Info("Removed the right-click menu entry");
    }

    private static void DeleteIfEmpty(string path)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(path))
        {
            if (key is null || key.SubKeyCount > 0 || key.ValueCount > 0)
                return;
        }

        Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false);
    }

    /// <summary>Tells Explorer that file associations changed, so the menu updates without signing out.</summary>
    private static void NotifyShell() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
