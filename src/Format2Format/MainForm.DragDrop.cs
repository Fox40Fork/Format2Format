namespace Format2Format;

// Drag and drop: files or folders dropped anywhere on the window are added to the list.
internal sealed partial class MainForm
{
    private void InitializeDragDrop(Control bottomBar)
    {
        // AllowDrop doesn't propagate to child controls, so wire up every control that can be under the cursor.
        foreach (var control in new[] { this, _list, _toolbar, _menu, _statusStrip, bottomBar })
            EnableDrop(control);
    }

    private void EnableDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += OnDragEnterOrOver;
        control.DragOver += OnDragEnterOrOver;
        control.DragDrop += OnDragDrop;

        foreach (Control child in control.Controls)
        {
            if (!child.AllowDrop)
                EnableDrop(child);
        }
    }

    private static void OnDragEnterOrOver(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            return;

        // Return right away so Explorer (the drag source) isn't blocked while we inspect the files.
        BeginInvoke(() =>
        {
            AddFiles(paths);
            Activate();
        });
    }
}
