using System.Windows;

namespace SoulCaliberStudio;

public partial class MainWindow
{
    private void EditConditionalDialogue_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeNode is null)
        {
            MessageBox.Show(this, "Select an existing dialogue node first.", "Conditional dialogue");
            return;
        }
        if (!DesignerCanSwitch()) return;
        var editor = new ConditionalDialogueWindow(_designer, _activeNode, _activeOption) { Owner = this };
        editor.ShowDialog();
        DesignerStatus.Text = "Conditional dialogue updated. Reopen the Godot scene to load new content.";
    }
}
