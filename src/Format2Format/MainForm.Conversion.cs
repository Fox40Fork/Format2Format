using Format2Format.Core;

namespace Format2Format;

// Running conversions on a background thread and reporting the results.
internal sealed partial class MainForm
{
    private readonly record struct ProgressUpdate(ListViewItem Item, ConversionResult? Result, int Completed);

    private async void StartConversion()
    {
        if (IsConverting)
            return;

        var target = SelectedFormat;
        var options = new ConversionOptions { Quality = (int)_quality.Value };

        // Skip unsupported files and files already converted to this format (so pressing Convert twice
        // doesn't make duplicates, but switching the format and pressing it again does convert them again).
        var items = _list.Items.Cast<ListViewItem>()
            .Where(i => i.Tag is FileEntry { CanConvert: true } e && !(e.State == FileState.Done && e.ConvertedTo == target))
            .ToList();

        if (items.Count == 0)
        {
            string message = _list.Items.Count == 0
                ? "Add some images first: drag them onto the window or click Add Files."
                : _list.Items.Cast<ListViewItem>().Any(i => ((FileEntry)i.Tag!).CanConvert)
                    ? $"All files have already been converted to {ImageFormats.GetDisplayName(target)}."
                    : "None of the files in the list can be converted.";
            MessageBox.Show(this, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string outputFolder;
        try
        {
            outputFolder = KnownFolders.GetDownloadsPath();
        }
        catch (Exception ex)
        {
            Log.Error("Could not resolve the Downloads folder", ex);
            MessageBox.Show(this, "The Downloads folder could not be found, so there's nowhere to save the files.\n\n" + ex.Message,
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        foreach (var item in items)
        {
            var entry = (FileEntry)item.Tag!;
            entry.State = FileState.Pending;
            entry.Message = null;
            RefreshItem(item);
        }

        _conversion = new CancellationTokenSource();
        var token = _conversion.Token;
        _progress.Maximum = items.Count;
        _progress.Value = 0;
        UpdateCommandStates();
        Log.Info($"Converting {items.Count} file(s) to {target} (quality {options.Quality}) into \"{outputFolder}\"");

        var progress = new Progress<ProgressUpdate>(update => ApplyUpdate(update, target));
        var results = new List<(ListViewItem Item, ConversionResult Result)>();

        try
        {
            await Task.Run(() =>
            {
                IProgress<ProgressUpdate> report = progress;
                foreach (var item in items)
                {
                    if (token.IsCancellationRequested)
                        break;

                    var entry = (FileEntry)item.Tag!;
                    report.Report(new ProgressUpdate(item, null, results.Count));
                    var result = _converter.Convert(entry.Path, target, outputFolder, options);
                    results.Add((item, result));
                    report.Report(new ProgressUpdate(item, result, results.Count));
                }
            });
        }
        catch (Exception ex)
        {
            // ConversionService doesn't throw for bad files, so this is unexpected.
            Log.Error("Conversion loop failed", ex);
            MessageBox.Show(this, "Something went wrong while converting:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        bool cancelled = token.IsCancellationRequested;

        // Progress<T> posts to the UI queue before this continuation runs, so the list is already up to date.
        // Re-applying is cheap and keeps it correct even if that ever changes.
        foreach (var (item, result) in results)
            ApplyUpdate(new ProgressUpdate(item, result, results.Count), target);
        _progress.Value = Math.Min(_progress.Maximum, results.Count);

        _conversion.Dispose();
        _conversion = null;
        UpdateCommandStates();

        int succeeded = results.Count(r => r.Result.Success);
        Log.Info($"Finished: {succeeded} converted, {results.Count - succeeded} failed{(cancelled ? ", stopped by user" : "")}");

        if (_closeWhenConversionEnds)
        {
            Close();
            return;
        }

        ShowSummary(results.Select(r => r.Result).ToList(), items.Count, target, outputFolder, cancelled);
    }

    private void StopConversion()
    {
        if (_conversion is null || _conversion.IsCancellationRequested)
            return;

        _conversion.Cancel();
        _convertButton.Enabled = false; // re-enabled when the current file finishes
        Log.Info("Stop requested; finishing the current file");
    }

    private void ApplyUpdate(ProgressUpdate update, OutputFormat target)
    {
        var entry = (FileEntry)update.Item.Tag!;
        var result = update.Result;

        if (result is null)
        {
            entry.State = FileState.Converting;
        }
        else if (result.Success)
        {
            entry.State = FileState.Done;
            entry.OutputPath = result.OutputPath;
            entry.ConvertedTo = target;
            entry.Message = result.Note;
        }
        else
        {
            entry.State = FileState.Failed;
            entry.Message = result.Error;
        }

        _progress.Value = Math.Clamp(update.Completed, 0, _progress.Maximum);

        if (update.Item.ListView is not null)
            RefreshItem(update.Item);
    }

    private void ShowSummary(List<ConversionResult> results, int requested, OutputFormat target, string outputFolder, bool cancelled)
    {
        var outputs = results.Where(r => r.Success).Select(r => r.OutputPath!).ToList();
        int failed = results.Count - outputs.Count;
        string formatName = ImageFormats.GetDisplayName(target);

        var details = new List<string>();
        if (outputs.Count > 0)
            details.Add($"Saved to {outputFolder}");
        if (failed > 0)
            details.Add($"{failed} {(failed == 1 ? "file" : "files")} failed. See the Status column for the reason.");
        if (cancelled)
            details.Add($"Stopped before the remaining {requested - results.Count} {(requested - results.Count == 1 ? "file" : "files")}.");

        var page = new TaskDialogPage
        {
            Caption = AppName,
            Heading = outputs.Count == 0
                ? "No files were converted"
                : outputs.Count == 1 && requested == 1
                    ? $"Converted to {formatName}"
                    : $"Converted {outputs.Count} of {requested} files to {formatName}",
            Text = string.Join("\n", details),
            Icon = outputs.Count == 0 ? TaskDialogIcon.Error : failed > 0 ? TaskDialogIcon.Warning : TaskDialogIcon.Information,
            AllowCancel = true,
        };

        var openFolder = new TaskDialogButton("&Open folder");
        if (outputs.Count > 0)
        {
            page.Buttons.Add(openFolder);
            page.DefaultButton = openFolder;
        }

        page.Buttons.Add(TaskDialogButton.OK);

        if (TaskDialog.ShowDialog(this, page) == openFolder)
            ShellExplorer.OpenFolderAndSelect(outputFolder, outputs);
    }
}
