using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using static SoulCaliberStudio.DialogueDesignerStore;

namespace SoulCaliberStudio;

public partial class CheckProfilesWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private CheckProfileRow? _selectedProfile;
    private CheckProfileTermRow? _selectedTerm;
    private bool _loading;

    public CheckProfilesWindow(DialogueDesignerStore store)
    {
        InitializeComponent();
        _store = store;
        DirectionBox.ItemsSource = Directions;
        AttributeBox.ItemsSource = RelationshipAttributes;
        DirectionBox.SelectedItem = "PARTNER_TO_ACTOR";
        AttributeBox.SelectedItem = "Trust";
        FactorBox.Text = "0.10";
        ReloadProfiles("STANDARD");
    }

    private void ReloadProfiles(string? key = null)
    {
        _loading = true;
        try
        {
            var profiles = _store.CheckProfiles();
            ProfileList.ItemsSource = profiles;
            ProfileList.SelectedItem = profiles.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? profiles.FirstOrDefault();
            Populate(ProfileList.SelectedItem as CheckProfileRow);
        }
        finally { _loading = false; }
    }
    private void Populate(CheckProfileRow? row)
    {
        _selectedProfile = row;
        KeyBox.Text = row?.Key ?? "";
        KeyBox.IsReadOnly = row is not null;
        NameBox.Text = row?.Name ?? "";
        DescriptionBox.Text = row?.Description ?? "";
        ReloadTerms();
    }
    private void ReloadTerms()
    {
        _loading = true;
        try
        {
            var rows = _selectedProfile is null ? new List<CheckProfileTermRow>()
                : _store.CheckProfileTerms(_selectedProfile.Key);
            TermList.ItemsSource = rows;
            TermList.SelectedItem = null;
            _selectedTerm = null;
            FormulaText.Text = _selectedProfile is null ? "Save a profile first."
                : $"Relationship bonus = {_store.CheckProfileFormula(_selectedProfile.Key)}";
        }
        finally { _loading = false; }
    }
    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _loading = true;
        try { Populate(ProfileList.SelectedItem as CheckProfileRow); }
        finally { _loading = false; }
    }
    private void TermList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _selectedTerm = TermList.SelectedItem as CheckProfileTermRow;
        if (_selectedTerm is null) return;
        DirectionBox.SelectedItem = _selectedTerm.Direction;
        AttributeBox.SelectedItem = _selectedTerm.Attribute;
        FactorBox.Text = _selectedTerm.Factor.ToString("0.###", CultureInfo.CurrentCulture);
    }
    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try { ProfileList.SelectedItem = null; Populate(null); }
        finally { _loading = false; }
        StatusText.Text = "New profile: choose an uppercase key and name, then save it before adding terms.";
    }
    private void SaveProfile_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var row = new CheckProfileRow(KeyBox.Text.Trim(), NameBox.Text.Trim(), DescriptionBox.Text);
        _store.SaveCheckProfile(row);
        ReloadProfiles(row.Key);
    }, "Profile saved. Changes to an existing profile affect every choice using it.");
    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null) return;
        if (MessageBox.Show(this, $"Delete profile '{_selectedProfile.Key}' and its formula?", "Delete profile",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Run(() => { _store.DeleteCheckProfile(_selectedProfile.Key); ReloadProfiles(); }, "Profile deleted.");
    }
    private void NewTerm_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            TermList.SelectedItem = null;
            _selectedTerm = null;
            DirectionBox.SelectedItem = "PARTNER_TO_ACTOR";
            AttributeBox.SelectedItem = "Trust";
            FactorBox.Text = "0.10";
        }
        finally { _loading = false; }
        StatusText.Text = "New term: choose a direction, attribute and factor, then save.";
    }

    private void SaveTerm_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_selectedProfile is null) throw new InvalidOperationException("Save or select a profile first.");
        if (!double.TryParse(FactorBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var factor))
            throw new InvalidOperationException("Enter a numeric factor, e.g. -0.05 or 0.20.");
        string direction = DirectionBox.SelectedItem as string ?? "";
        string attribute = AttributeBox.SelectedItem as string ?? "";
        _store.SaveCheckProfileTerm(new CheckProfileTermRow(_selectedProfile.Key, direction, attribute, factor),
            _selectedTerm);
        ReloadTerms();
    }, "Formula term saved.");
    private void RemoveTerm_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTerm is null) return;
        Run(() => { _store.DeleteCheckProfileTerm(_selectedTerm); ReloadTerms(); }, "Formula term removed.");
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Run(Action work, string message)
    {
        try { work(); StatusText.Text = message; }
        catch(Exception ex) { MessageBox.Show(this, ex.Message, "Check Profiles", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
