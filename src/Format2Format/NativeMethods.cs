using System.Diagnostics;
using System.Runtime.InteropServices;
using Format2Format.Core;

namespace Format2Format;

/// <summary>Resolves Windows known folders through the Known Folder API.</summary>
internal static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
    private const uint KF_FLAG_CREATE = 0x00008000;

    /// <summary>The user's Downloads folder, wherever they've moved it (e.g. D:\Downloads or OneDrive).</summary>
    public static string GetDownloadsPath()
    {
        int hr = SHGetKnownFolderPath(DownloadsId, KF_FLAG_CREATE, IntPtr.Zero, out IntPtr pathPtr);
        try
        {
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);
            return Marshal.PtrToStringUni(pathPtr)!;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}

/// <summary>Explorer helpers.</summary>
internal static class ShellExplorer
{
    /// <summary>Opens <paramref name="folder"/> in Explorer with <paramref name="files"/> selected.</summary>
    public static void OpenFolderAndSelect(string folder, IReadOnlyList<string> files)
    {
        try
        {
            if (files.Count > 0 && TryOpenAndSelect(folder, files))
                return;
        }
        catch (Exception ex)
        {
            Log.Warn("SHOpenFolderAndSelectItems failed; falling back to explorer.exe", ex);
        }

        try
        {
            string args = files.Count == 1 ? $"/select,\"{files[0]}\"" : $"\"{folder}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open \"{folder}\" in Explorer", ex);
        }
    }

    public static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open \"{path}\"", ex);
            MessageBox.Show($"Could not open \"{path}\".\n\n{ex.Message}", "Format2Format", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool TryOpenAndSelect(string folder, IReadOnlyList<string> files)
    {
        IntPtr folderPidl = IntPtr.Zero;
        var filePidls = new List<IntPtr>();
        try
        {
            if (SHParseDisplayName(folder, IntPtr.Zero, out folderPidl, 0, out _) != 0)
                return false;

            foreach (string file in files)
            {
                if (SHParseDisplayName(file, IntPtr.Zero, out IntPtr pidl, 0, out _) == 0)
                    filePidls.Add(pidl);
            }

            if (filePidls.Count == 0)
                return false;

            // The items must be given relative to the folder: the last ID of each absolute PIDL.
            IntPtr[] children = filePidls.Select(ILFindLastID).ToArray();
            return SHOpenFolderAndSelectItems(folderPidl, (uint)children.Length, children, 0) == 0;
        }
        finally
        {
            foreach (IntPtr pidl in filePidls)
                Marshal.FreeCoTaskMem(pidl);
            if (folderPidl != IntPtr.Zero)
                Marshal.FreeCoTaskMem(folderPidl);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, uint dwFlags);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern IntPtr ILFindLastID(IntPtr pidl);
}
