using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace SoulCaliberStudio;

public partial class MainWindow
{
    private string? _lastPublishedDirectory;
    private bool _publishingBusy;

    private void InitPublishing(string masterDb)
    {
        _lastPublishedDirectory = null;
        PublishInstallButton.IsEnabled = false;
        PublishMasterText.Text = masterDb;
        RefreshPublishGodotPath();
        string contentFolder = Path.GetDirectoryName(masterDb)!;
        string repositoryRoot = Directory.GetParent(contentFolder)?.FullName ?? contentFolder;
        PublishOutputBox.Text = Path.Combine(repositoryRoot, "Published");
        PublishVersionBox.Text = "0.1.0";
        PublishLogBox.Text = "Your original master database is never changed during validation or publishing.\n\n" +
                             "Start with Validate. Fix any errors before publishing a versioned, read-only content snapshot.";
    }

    private void RefreshPublishGodotPath()
    {
        if (PublishGodotText is not null)
            PublishGodotText.Text = _godotRoot ?? "No Godot project selected. Choose project.godot under Characters & Portraits.";
    }

    private bool CanPublishNow()
    {
        if (_publishingBusy) return false;
        if (_editor is null)
        {
            MessageBox.Show(this, "Open your soulcaliber_master.db first.", "No master database");
            return false;
        }
        // Prevent surprising results when a user has typed text in an editor but has not saved it.
        if (_designerDirty || _characterDirty || _reactionDirty || IsDirty())
        {
            MessageBox.Show(this,
                "You have unsaved edits. Save or discard those edits in their respective tabs before publishing. " +
                "The publisher only reads records already committed to SQLite.",
                "Unsaved content", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (string.IsNullOrWhiteSpace(_godotRoot))
        {
            MessageBox.Show(this, "Choose the Godot project first using 'Locate project.godot'.", "Game project required");
            return false;
        }
        return true;
    }

    private void SetPublishBusy(bool value)
    {
        _publishingBusy = value;
        PublishValidateButton.IsEnabled = !value;
        PublishCreateButton.IsEnabled = !value;
        PublishInstallButton.IsEnabled = !value && _lastPublishedDirectory is not null;
    }

    private void SelectPublishProject_Click(object sender, RoutedEventArgs e)
    {
        ChooseGodotProject_Click(sender, e);
        RefreshPublishGodotPath();
    }

    private void SelectPublishOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose the Published output folder" };
        if (Directory.Exists(PublishOutputBox.Text)) picker.FolderName = PublishOutputBox.Text;
        if (picker.ShowDialog(this) == true) PublishOutputBox.Text = picker.FolderName;
    }

    private async void PublishValidate_Click(object sender, RoutedEventArgs e)
    {
        if (!CanPublishNow()) return;
        string master = PublishMasterText.Text;
        string root = _godotRoot!;
        SetPublishBusy(true);
        PublishLogBox.Text = "Validating a consistent SQLite snapshot and checking resource paths...";
        try
        {
            PublishReport report = await Task.Run(() => ContentPublisher.Validate(master, root));
            PublishLogBox.Text = report.Describe();
        }
        catch (Exception ex)
        {
            PublishLogBox.Text = "Validation could not run:\n" + ex;
            MessageBox.Show(this, ex.Message, "Validation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetPublishBusy(false); }
    }

    private async void PublishCreate_Click(object sender, RoutedEventArgs e)
    {
        if (!CanPublishNow()) return;
        string master = PublishMasterText.Text;
        string root = _godotRoot!;
        string output = PublishOutputBox.Text.Trim();
        string version = PublishVersionBox.Text.Trim();
        if (output.Length == 0)
        {
            MessageBox.Show(this, "Choose an output directory.", "Output required");
            return;
        }
        if (MessageBox.Show(this,
            $"Publish an immutable v{version} content snapshot?\n\nMaster: {master}\nOutput root: {output}\n\n" +
            "This creates a standalone content package; it does not export Godot or edit your master database.",
            "Publish SoulCaliber content", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        SetPublishBusy(true);
        PublishLogBox.Text = "Creating a SQLite backup, validating content, checking artwork and generating a manifest...";
        try
        {
            PublishReport report = await Task.Run(() => ContentPublisher.Publish(master, root, output, version));
            PublishLogBox.Text = report.Describe();
            if (report.PublishedDirectory is not null)
            {
                _lastPublishedDirectory = report.PublishedDirectory;
                MessageBox.Show(this,
                    $"Content package created:\n{report.PublishedDirectory}\n\n" +
                    "Next: export your Godot project, then install this Content folder beside the exported .exe.",
                    "Publish successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, "Publishing stopped because validation found errors. See the report on the right.",
                    "Publishing blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            PublishLogBox.Text = "Publishing failed without changing the master DB:\n" + ex;
            MessageBox.Show(this, ex.Message, "Publishing failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetPublishBusy(false); }
    }

    private async void PublishInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_publishingBusy || _lastPublishedDirectory is null) return;
        var picker = new OpenFolderDialog
        {
            Title = "Select the exported Godot game folder (containing SoulCaliber.exe)"
        };
        if (picker.ShowDialog(this) != true) return;
        if (MessageBox.Show(this,
            $"Install the content package into:\n{picker.FolderName}\n\n" +
            "Close the exported game first. Existing content.db/manifest.json files will be backed up before replacement.",
            "Install into exported game?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        string package = _lastPublishedDirectory;
        string game = picker.FolderName;
        SetPublishBusy(true);
        try
        {
            string installed = await Task.Run(() => ContentPublisher.InstallIntoExport(package, game));
            PublishLogBox.Text += "\nINSTALLED INTO:\n" + installed + "\n\nRun the exported .exe to test offline.\n";
            MessageBox.Show(this,
                $"Content installed at:\n{installed}\n\n" +
                "This copies only the SQLite content snapshot and manifest. Godot's exported PCK must contain your portraits.",
                "Content installed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            PublishLogBox.Text += "\nINSTALL FAILED:\n" + ex + "\n";
            MessageBox.Show(this, ex.Message, "Install failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetPublishBusy(false); }
    }
}
