using System.Windows;

namespace SoulCaliberStudio;

public partial class MainWindow
{
    private string? _testingDatabasePath;

    private void InitTesting(string path)
    {
        // Phase 12A: migrate the authoring catalogue on master DB open.
        // Keep this hook outside DialogueDesignerStore.cs so the previous
        // no-automatic-backups patch is not overwritten by this ZIP.
        _designer?.EnsureRelationshipStatusSchema();
        _designer?.EnsureCharacterDefaultStatusesSchema();
        _designer?.EnsureOptionStatusEffectsSchema();
        _designer?.EnsureOptionStatusRequirementsSchema();
        _designer?.EnsureChoiceRequirementVisibilitySchema();
        _designer?.EnsureMemoryCatalogueSchema();
        _designer?.EnsureOptionMemoryEffectsSchema();
        _designer?.EnsureOptionMemoryRequirementsSchema();
        _designer?.EnsureSpecialEncountersSchema();
        _designer?.EnsureConversationAvailabilitySchema();
        _testingDatabasePath = path;
        TestingDatabaseText.Text = path;
    }

    private void ManageRelationshipAttributes_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null)
        { MessageBox.Show(this,"Open the master database first.","Relationship attributes"); return; }
        if (!DesignerCanSwitch() || !CharactersCanSwitch() || !CanLeaveTable()) return;
        var window = new RelationshipAttributesWindow(_designer) { Owner = this };
        window.ShowDialog();
    }

    private void ManageRelationshipStatuses_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null)
        { MessageBox.Show(this, "Open the master database first.", "Relationship statuses"); return; }
        if (!DesignerCanSwitch() || !CharactersCanSwitch() || !CanLeaveTable()) return;
        try
        {
            var window = new RelationshipStatusesWindow(_designer) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Relationship statuses", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditDefaultCharacterRelationships_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null)
        { MessageBox.Show(this, "Open the master database first.", "Starting relationships"); return; }
        if (!DesignerCanSwitch() || !CharactersCanSwitch() || !CanLeaveTable()) return;
        try
        {
            var window = new DefaultCharacterRelationshipsWindow(_designer, _activeCharacter?.Id)
                { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Starting relationships", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ManageMemoryCatalogue_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null)
        { MessageBox.Show(this, "Open the master database first.", "Memory catalogue"); return; }
        if (!DesignerCanSwitch() || !CharactersCanSwitch() || !CanLeaveTable()) return;
        try
        {
            var window = new MemoryCatalogueWindow(_designer) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Memory catalogue", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ManageSpecialEncounters_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null)
        { MessageBox.Show(this, "Open the master database first.", "Special encounters"); return; }
        if (!DesignerCanSwitch() || !CharactersCanSwitch() || !CanLeaveTable()) return;
        try
        {
            var window = new SpecialEncountersWindow(_designer) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Special encounters", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ManageConversationAvailability_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null)
        { MessageBox.Show(this, "Open the master database first.", "Normal conversations"); return; }
        if (!DesignerCanSwitch() || !CharactersCanSwitch() || !CanLeaveTable()) return;
        try
        {
            var window = new ConversationAvailabilityWindow(_designer) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Normal conversations", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
