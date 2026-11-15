using System.Windows;

namespace SoulCaliberStudio;

public partial class MainWindow
{
    private string? _testingDatabasePath;

    private void InitTesting(string path)
    {
        _testingDatabasePath = path;
        TestingDatabaseText.Text = path;
    }

    private void LaunchSimulator_Click(object sender, RoutedEventArgs e)
    {
        if (_testingDatabasePath is null)
        {
            MessageBox.Show(this, "Open the master database first.", "Test Lab");
            return;
        }
        if (!DesignerCanSwitch() || !CharactersCanSwitch() || !CanLeaveTable()) return;
        try
        {
            var window = new InteractionSimulatorWindow(_testingDatabasePath) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to start simulator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
