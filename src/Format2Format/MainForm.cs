using System.Collections.Concurrent;
using System.ComponentModel;
using Format2Format.Core;

namespace Format2Format;

/// <summary>
/// The single window: menu, toolbar, file list, and a bottom bar with the target format and Convert button.
/// Built in code (no designer file) and kept to stock controls and system colors.
/// </summary>
internal sealed partial class MainForm : Form
{
    private const string AppName = "Format2Format";

    private const string OpenFileFilter =
        "Images|*.jpg;*.jpeg;*.jfif;*.png;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.heic;*.heif;*.avif|All files|*.*";

    private readonly ConversionService _converter = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string[]> _externalFiles = new();
    private readonly string[] _startupFiles;

    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _toolbar = new();
    private readonly HintListView _list = new();
    private readonly ColumnHeader _nameColumn = new() { Text = "Name", Width = 230 };
    private readonly ColumnHeader _formatColumn = new() { Text = "Format", Width = 60 };
    private readonly ColumnHeader _sizeColumn = new() { Text = "Size", Width = 80, TextAlign = HorizontalAlignment.Right };
    private readonly ColumnHeader _statusColumn = new() { Text = "Status", Width = 220 };
    private readonly ComboBox _formatBox = new();
    private readonly Label _qualityLabel = new();
    private readonly NumericUpDown _quality = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _convertButton = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _countLabel = new();
    private readonly ToolStripStatusLabel _outputLabel = new();
    private readonly ToolTip _toolTip = new();

    private ToolStripMenuItem _addMenuItem = null!;
    private ToolStripMenuItem _removeMenuItem = null!;
    private ToolStripMenuItem _clearMenuItem = null!;
    private ToolStripMenuItem _convertMenuItem = null!;
    private ToolStripMenuItem _shellMenuItem = null!;
    private ToolStripButton _removeButton = null!;
    private ToolStripButton _clearButton = null!;
    private ToolStripMenuItem _listRemoveItem = null!;

    private CancellationTokenSource? _conversion;
    private bool _closeWhenConversionEnds;
    private bool _columnsScaled;

    public MainForm(IEnumerable<string> startupFiles)
    {
        _startupFiles = startupFiles.ToArray();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppName;
        ClientSize = new Size(660, 400);
        MinimumSize = new Size(480, 300);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not load the application icon", ex);
        }

        BuildMenu();
        BuildToolbar();
        BuildList();
        var bottomBar = BuildBottomBar();
        BuildStatusStrip();
        InitializeDragDrop(bottomBar);

        // Docking is applied from the last-added control to the first, so the order here is
        // outermost last: menu > toolbar at the top, status strip > bottom bar at the bottom, list fills the rest.
        Controls.Add(_list);
        Controls.Add(bottomBar);
        Controls.Add(_toolbar);
        Controls.Add(_statusStrip);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        AcceptButton = _convertButton;

        ResumeLayout(false);
        PerformLayout();

        UpdateQualityVisibility();
        UpdateCommandStates();
    }

    private bool IsConverting => _conversion != null;

    private OutputFormat SelectedFormat => ImageFormats.OutputFormats[Math.Max(0, _formatBox.SelectedIndex)];

    /// <summary>Adds files sent by another Format2Format process (Explorer right-click). Safe to call from any thread.</summary>
    public void AcceptExternalFiles(string[] files)
    {
        _externalFiles.Enqueue(files);
        if (!IsHandleCreated)
            return; // OnLoad drains the queue

        try
        {
            BeginInvoke(DrainExternalFiles);
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    // ───────────────────────────── UI construction ─────────────────────────────

    private void BuildMenu()
    {
        _menu.RenderMode = ToolStripRenderMode.System;

        _addMenuItem = new ToolStripMenuItem("&Add Files...", null, (_, _) => ShowAddFilesDialog()) { ShortcutKeys = Keys.Control | Keys.O };
        // The Delete key is handled by the list itself, so typing in other controls isn't hijacked.
        _removeMenuItem = new ToolStripMenuItem("&Remove", null, (_, _) => RemoveSelected()) { ShortcutKeyDisplayString = "Del" };
        _clearMenuItem = new ToolStripMenuItem("C&lear List", null, (_, _) => ClearList());
        _convertMenuItem = new ToolStripMenuItem("&Convert", null, (_, _) => StartConversion()) { ShortcutKeys = Keys.F5 };

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange(
        [
            _addMenuItem,
            _removeMenuItem,
            _clearMenuItem,
            new ToolStripSeparator(),
            _convertMenuItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Open &Downloads Folder", null, (_, _) => OpenDownloadsFolder()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("E&xit", null, (_, _) => Close()),
        ]);

        _shellMenuItem = new ToolStripMenuItem("Add right-click menu entry", null, (_, _) => ToggleShellIntegration());
        var settings = new ToolStripMenuItem("&Settings");
        settings.DropDownItems.Add(_shellMenuItem);
        settings.DropDownOpening += (_, _) => UpdateShellMenuItem();

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.AddRange(
        [
            new ToolStripMenuItem("View &Log File", null, (_, _) => ViewLog()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("&About Format2Format", null, (_, _) => ShowAbout()),
        ]);

        _menu.Items.AddRange([file, settings, help]);
    }

    private void BuildToolbar()
    {
        _toolbar.RenderMode = ToolStripRenderMode.System;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Padding = new Padding(4, 2, 4, 2);

        var add = CreateToolButton("Add Files", StockIconId.ImageFiles, "Add image files to the list (Ctrl+O)", (_, _) => ShowAddFilesDialog());
        _removeButton = CreateToolButton("Remove", StockIconId.Delete, "Remove the selected files from the list (Del)", (_, _) => RemoveSelected());
        _clearButton = CreateToolButton("Clear", StockIconId.Recycler, "Remove all files from the list (files on disk are not touched)", (_, _) => ClearList());

        _toolbar.Items.AddRange([add, _removeButton, _clearButton]);
    }

    private static ToolStripButton CreateToolButton(string text, StockIconId icon, string toolTip, EventHandler onClick)
    {
        var button = new ToolStripButton(text, null, onClick)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            ToolTipText = toolTip,
            Margin = new Padding(0, 1, 2, 2),
        };

        try
        {
            using var systemIcon = SystemIcons.GetStockIcon(icon, StockIconOptions.SmallIcon);
            button.Image = systemIcon.ToBitmap();
        }
        catch (Exception ex)
        {
            Log.Warn($"Stock icon {icon} unavailable: {ex.Message}");
        }

        return button;
    }

    private void BuildList()
    {
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = true;
        _list.ShowItemToolTips = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.EmptyText = "Drag images here, or click Add Files.";
        _list.Columns.AddRange([_nameColumn, _formatColumn, _sizeColumn, _statusColumn]);

        _list.SelectedIndexChanged += (_, _) => UpdateCommandStates();
        _list.ClientSizeChanged += (_, _) => FitStatusColumn();
        _list.KeyDown += OnListKeyDown;

        _listRemoveItem = new ToolStripMenuItem("&Remove", null, (_, _) => RemoveSelected()) { ShortcutKeyDisplayString = "Del" };
        var contextMenu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        contextMenu.Items.AddRange(
        [
            new ToolStripMenuItem("&Add Files...", null, (_, _) => ShowAddFilesDialog()),
            _listRemoveItem,
            new ToolStripMenuItem("C&lear List", null, (_, _) => ClearList()),
        ]);
        contextMenu.Opening += (_, _) =>
        {
            _listRemoveItem.Enabled = _removeButton.Enabled;
            contextMenu.Items[2].Enabled = _clearButton.Enabled;
        };
        _list.ContextMenuStrip = contextMenu;
    }

    private TableLayoutPanel BuildBottomBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 6,
            RowCount = 1,
            Padding = new Padding(6, 6, 6, 4),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var convertToLabel = new Label { Text = "Convert &to:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 3, 3) };

        _formatBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _formatBox.Width = 72;
        _formatBox.Anchor = AnchorStyles.Left;
        _formatBox.Margin = new Padding(0, 3, 12, 3);
        foreach (var format in ImageFormats.OutputFormats)
            _formatBox.Items.Add(ImageFormats.GetDisplayName(format));
        _formatBox.SelectedIndex = ImageFormats.TryParseOutputFormat(_settings.Format, out var saved)
            ? ImageFormats.OutputFormats.ToList().IndexOf(saved)
            : 0;
        _formatBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateQualityVisibility();
            _settings.Format = SelectedFormat.ToString();
            _settings.Save();
        };

        _qualityLabel.Text = "&Quality:";
        _qualityLabel.AutoSize = true;
        _qualityLabel.Anchor = AnchorStyles.Left;
        _qualityLabel.Margin = new Padding(0, 3, 3, 3);

        _quality.Minimum = 1;
        _quality.Maximum = 100;
        _quality.Width = 52;
        _quality.Anchor = AnchorStyles.Left;
        _quality.Margin = new Padding(0, 3, 12, 3);
        _quality.Value = Math.Clamp(_settings.Quality, 1, 100);
        _quality.ValueChanged += (_, _) =>
        {
            _settings.Quality = (int)_quality.Value;
            _settings.Save();
        };
        const string qualityTip = "1–100. Higher means better quality and bigger files. For WEBP, 100 means lossless.";
        _toolTip.SetToolTip(_quality, qualityTip);
        _toolTip.SetToolTip(_qualityLabel, qualityTip);

        _progress.Dock = DockStyle.Fill;
        _progress.Margin = new Padding(0, 4, 8, 4);

        _convertButton.Text = "Convert";
        _convertButton.Size = new Size(88, 25);
        _convertButton.Anchor = AnchorStyles.Right;
        _convertButton.Margin = new Padding(0, 2, 0, 2);
        _convertButton.UseVisualStyleBackColor = true;
        _convertButton.Click += (_, _) =>
        {
            if (IsConverting)
                StopConversion();
            else
                StartConversion();
        };

        bar.Controls.Add(convertToLabel, 0, 0);
        bar.Controls.Add(_formatBox, 1, 0);
        bar.Controls.Add(_qualityLabel, 2, 0);
        bar.Controls.Add(_quality, 3, 0);
        bar.Controls.Add(_progress, 4, 0);
        bar.Controls.Add(_convertButton, 5, 0);
        return bar;
    }

    private void BuildStatusStrip()
    {
        _statusStrip.RenderMode = ToolStripRenderMode.System;
        _statusStrip.SizingGrip = true;
        _countLabel.Spring = true;
        _countLabel.TextAlign = ContentAlignment.MiddleLeft;
        _outputLabel.TextAlign = ContentAlignment.MiddleRight;
        _statusStrip.Items.AddRange([_countLabel, _outputLabel]);
    }

    // ───────────────────────────── Lifecycle ─────────────────────────────

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            _outputLabel.Text = "Saving to: " + KnownFolders.GetDownloadsPath();
        }
        catch (Exception ex)
        {
            Log.Error("Could not resolve the Downloads folder", ex);
            _outputLabel.Text = "Downloads folder not available";
        }

        AddFiles(_startupFiles);
        DrainExternalFiles();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (IsConverting && e.CloseReason != CloseReason.WindowsShutDown)
        {
            e.Cancel = true;
            if (MessageBox.Show(this, "A conversion is still running. Stop it and exit?", AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _closeWhenConversionEnds = true;
                StopConversion();
            }

            return;
        }

        _conversion?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        // Column widths aren't scaled automatically when the window moves to a monitor with a different DPI.
        float factor = (float)e.DeviceDpiNew / e.DeviceDpiOld;
        foreach (var column in new[] { _nameColumn, _formatColumn, _sizeColumn })
            column.Width = (int)Math.Round(column.Width * factor);
        FitStatusColumn();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Initial column widths are in 96-DPI units; scale them for the current monitor once.
        // (ListView doesn't scale column widths itself, and the handle can be recreated later.)
        if (!_columnsScaled && DeviceDpi != 96)
        {
            float factor = DeviceDpi / 96f;
            foreach (var column in new[] { _nameColumn, _formatColumn, _sizeColumn })
                column.Width = (int)Math.Round(column.Width * factor);
        }

        _columnsScaled = true;
    }

    // ───────────────────────────── File list ─────────────────────────────

    private void ShowAddFilesDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add Files",
            Filter = OpenFileFilter,
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            AddFiles(dialog.FileNames);
    }

    /// <summary>Adds files (and the images directly inside dropped folders). Duplicates are ignored.</summary>
    private void AddFiles(IEnumerable<string> paths)
    {
        var added = new List<ListViewItem>();

        foreach (string raw in paths)
        {
            string path;
            try
            {
                path = Path.GetFullPath(raw);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Log.Warn($"Ignoring invalid path \"{raw}\"");
                continue;
            }

            if (Directory.Exists(path))
            {
                foreach (string file in EnumerateImagesInFolder(path))
                    TryAdd(file, added);
            }
            else if (File.Exists(path))
            {
                TryAdd(path, added);
            }
            else
            {
                Log.Warn($"Ignoring missing file \"{path}\"");
            }
        }

        if (added.Count == 0)
            return;

        _list.BeginUpdate();
        _list.Items.AddRange(added.ToArray());
        _list.EndUpdate();
        added[^1].EnsureVisible();

        Log.Info($"Added {added.Count} file(s) to the list");
        UpdateCommandStates();
    }

    private static IEnumerable<string> EnumerateImagesInFolder(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(ImageFormats.HasSupportedExtension)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not list \"{folder}\"", ex);
            return [];
        }
    }

    private void TryAdd(string path, List<ListViewItem> added)
    {
        if (!_paths.Add(path))
            return;

        var entry = FileEntry.Inspect(path);
        var item = new ListViewItem(entry.Name) { Tag = entry };
        item.SubItems.Add(ImageFormats.GetDisplayName(entry.Format, entry.Path));
        item.SubItems.Add(FormatSize(entry.Size));
        item.SubItems.Add(entry.StatusText);
        RefreshItem(item);
        added.Add(item);
    }

    private void RemoveSelected()
    {
        if (IsConverting || _list.SelectedItems.Count == 0)
            return;

        int firstIndex = _list.SelectedIndices[0];
        _list.BeginUpdate();
        foreach (ListViewItem item in _list.SelectedItems.Cast<ListViewItem>().ToList())
        {
            _paths.Remove(((FileEntry)item.Tag!).Path);
            _list.Items.Remove(item);
        }

        _list.EndUpdate();

        // Keep the keyboard flow: select the row that took the removed one's place.
        if (_list.Items.Count > 0)
        {
            var next = _list.Items[Math.Min(firstIndex, _list.Items.Count - 1)];
            next.Selected = true;
            next.Focused = true;
        }

        UpdateCommandStates();
    }

    private void ClearList()
    {
        if (IsConverting)
            return;

        _list.Items.Clear();
        _paths.Clear();
        _progress.Value = 0;
        UpdateCommandStates();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None)
        {
            RemoveSelected();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.A && e.Modifiers == Keys.Control)
        {
            _list.BeginUpdate();
            foreach (ListViewItem item in _list.Items)
                item.Selected = true;
            _list.EndUpdate();
            e.Handled = true;
        }
    }

    private void DrainExternalFiles()
    {
        bool any = false;
        while (_externalFiles.TryDequeue(out var files))
        {
            AddFiles(files);
            any = true;
        }

        if (any)
            BringToForeground();
    }

    private void BringToForeground()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
    }

    private static void RefreshItem(ListViewItem item)
    {
        var entry = (FileEntry)item.Tag!;
        item.SubItems[3].Text = entry.StatusText;
        item.ToolTipText = entry.OutputPath is null ? entry.Path : $"{entry.Path}\n→ {entry.OutputPath}";
        item.ForeColor = entry.State == FileState.Skipped ? SystemColors.GrayText : SystemColors.WindowText;
    }

    private void FitStatusColumn()
    {
        int others = _nameColumn.Width + _formatColumn.Width + _sizeColumn.Width;
        int minimum = LogicalToDeviceUnits(120);
        _statusColumn.Width = Math.Max(minimum, _list.ClientSize.Width - others);
    }

    // ───────────────────────────── Commands & state ─────────────────────────────

    private void UpdateQualityVisibility()
    {
        bool show = ImageFormats.SupportsQuality(SelectedFormat);
        _qualityLabel.Visible = show;
        _quality.Visible = show;
    }

    private void UpdateCommandStates()
    {
        bool busy = IsConverting;
        bool hasItems = _list.Items.Count > 0;
        bool hasSelection = _list.SelectedItems.Count > 0;

        _removeButton.Enabled = _removeMenuItem.Enabled = !busy && hasSelection;
        _clearButton.Enabled = _clearMenuItem.Enabled = !busy && hasItems;
        _convertMenuItem.Enabled = !busy && hasItems;
        _formatBox.Enabled = !busy;
        _quality.Enabled = !busy;

        _convertButton.Text = busy ? "Stop" : "Convert";
        _convertButton.Enabled = busy ? !_conversion!.IsCancellationRequested : hasItems;

        UpdateCountLabel();
    }

    private void UpdateCountLabel()
    {
        int count = _list.Items.Count;
        long total = _list.Items.Cast<ListViewItem>().Sum(i => ((FileEntry)i.Tag!).Size);
        _countLabel.Text = count switch
        {
            0 => "No files",
            1 => $"1 file ({FormatTotalSize(total)})",
            _ => $"{count} files ({FormatTotalSize(total)})",
        };
    }

    private void OpenDownloadsFolder()
    {
        try
        {
            ShellExplorer.Open(KnownFolders.GetDownloadsPath());
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the Downloads folder", ex);
            MessageBox.Show(this, "The Downloads folder could not be found.\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ViewLog()
    {
        if (File.Exists(Log.FilePath))
            ShellExplorer.Open(Log.FilePath);
        else
            MessageBox.Show(this, $"Nothing has been logged yet.\n\nThe log will be written to:\n{Log.FilePath}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        string version = Application.ProductVersion;
        MessageBox.Show(this,
            $"{AppName} {version}\n" +
            "Converts images between formats. Works offline.\n\n" +
            $"Image engine: {ConversionService.EngineVersion}\n\n" +
            $"Converted files are saved to your Downloads folder.\nLog file: {Log.FilePath}",
            $"About {AppName}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    /// <summary>Same style as Explorer's Size column: whole kilobytes, rounded up.</summary>
    internal static string FormatSize(long bytes) =>
        $"{(long)Math.Ceiling(bytes / 1024.0):N0} KB";

    private static string FormatTotalSize(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{Math.Ceiling(bytes / 1024.0):N0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):N1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):N2} GB",
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _conversion?.Dispose();
        }

        base.Dispose(disposing);
    }
}
