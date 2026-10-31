using Format2Format.Core;

namespace Format2Format;

// Settings > Add/Remove right-click menu entry.
internal sealed partial class MainForm
{
    private void UpdateShellMenuItem()
    {
        bool registered = SafeIsRegistered();
        _shellMenuItem.Text = registered ? "Remove right-click menu entry" : "Add right-click menu entry";
        _shellMenuItem.ToolTipText = registered
            ? "Remove \"Convert with Format2Format\" from Explorer's right-click menu"
            : "Add \"Convert with Format2Format\" to Explorer's right-click menu for image files";
    }

    private void ToggleShellIntegration()
    {
        bool registered = SafeIsRegistered();
        try
        {
            if (registered)
            {
                ShellIntegration.Unregister();
                MessageBox.Show(this, "The \"Convert with Format2Format\" entry was removed from Explorer's right-click menu.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                ShellIntegration.Register(Application.ExecutablePath);
                MessageBox.Show(this,
                    "Right-click an image in Explorer and choose \"Convert with Format2Format\".\n\n" +
                    "On Windows 11, it's under \"Show more options\".",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Changing the right-click menu entry failed", ex);
            MessageBox.Show(this, "The right-click menu entry could not be changed.\n\n" + ex.Message,
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        UpdateShellMenuItem();
    }

    private static bool SafeIsRegistered()
    {
        try
        {
            return ShellIntegration.IsRegistered();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the right-click menu registration", ex);
            return false;
        }
    }
}
