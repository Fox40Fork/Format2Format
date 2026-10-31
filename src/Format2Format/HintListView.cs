using System.Runtime.InteropServices;

namespace Format2Format;

/// <summary>
/// A standard ListView that shows a centered hint (e.g. "Drag images here") while it's empty, using the
/// list view control's own empty-markup feature (LVN_GETEMPTYMARKUP), so it looks native.
/// </summary>
internal sealed class HintListView : ListView
{
    private const int WM_REFLECT_NOTIFY = 0x2000 + 0x004E;
    private const int LVN_GETEMPTYMARKUP = -100 - 87;
    private const int EMF_CENTERED = 0x1;

    public HintListView()
    {
        DoubleBuffered = true; // less flicker while statuses update
    }

    public string EmptyText { get; set; } = "";

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_REFLECT_NOTIFY && EmptyText.Length > 0)
        {
            var header = Marshal.PtrToStructure<NMHDR>(m.LParam);
            if (header.code == LVN_GETEMPTYMARKUP)
            {
                var markup = new NMLVEMPTYMARKUP { hdr = header, dwFlags = EMF_CENTERED, szMarkup = EmptyText };
                Marshal.StructureToPtr(markup, m.LParam, fDeleteOld: false);
                m.Result = 1;
                return;
            }
        }

        base.WndProc(ref m);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NMHDR
    {
        public IntPtr hwndFrom;
        public IntPtr idFrom;
        public int code;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NMLVEMPTYMARKUP
    {
        public NMHDR hdr;
        public int dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2084)] // L_MAX_URL_LENGTH
        public string szMarkup;
    }
}
