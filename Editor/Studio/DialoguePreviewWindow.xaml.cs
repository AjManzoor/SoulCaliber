using System.Windows;
using static SoulCaliberStudio.DialogueDesignerStore;

namespace SoulCaliberStudio;

public partial class DialoguePreviewWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly Node _node;
    private readonly Option _option;

    public DialoguePreviewWindow(DialogueDesignerStore store, Node node, Option option, string? defaultActor, string? defaultHost)
    {
        InitializeComponent();
        _store = store;
        _node = node;
        _option = option;
        HeadingText.Text = $"{node.Key} / {option.Label}";
        var chars = store.Characters();
        PreviewActorBox.ItemsSource = chars;
        PreviewHostBox.ItemsSource = chars;
        PreviewHostBox.SelectedItem = chars.Contains(defaultHost ?? "") ? defaultHost : chars.FirstOrDefault();
        PreviewActorBox.SelectedItem = chars.Contains(defaultActor ?? "") && defaultActor != defaultHost
            ? defaultActor : chars.FirstOrDefault(x => x != PreviewHostBox.SelectedItem as string);
        Render();
    }

    private void Resolve_Click(object sender, RoutedEventArgs e) => Render();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Render()
    {
        if (PreviewActorBox.SelectedItem is not string actor || PreviewHostBox.SelectedItem is not string host)
            return;
        if (actor == host)
        {
            MessageBox.Show(this, "Choose two different characters.", "Dialogue preview");
            return;
        }
        var intro = _store.ResolveIntroduction(_node.Id, host, actor);
        IntroductionSource.Text = $"{_store.CharacterName(host)} — {intro.Source}";
        IntroductionText.Text = Expand(intro.Text, actor, host);

        var ps = _store.ResolveLine(_option.Id, actor, host, true, _option.DefaultPlayerLine);
        var hs = _store.ResolveLine(_option.Id, host, actor, true, _option.DefaultHostSuccessLine);
        var pf = _store.ResolveLine(_option.Id, actor, host, false, _option.DefaultPlayerLine);
        var hf = _store.ResolveLine(_option.Id, host, actor, false, _option.DefaultHostFailureLine);

        SuccessPlayerSource.Text = $"{_store.CharacterName(actor)} — {ps.Source}";
        SuccessPlayerText.Text = ps.Text;
        SuccessHostSource.Text = $"{_store.CharacterName(host)} — {hs.Source}";
        SuccessHostText.Text = hs.Text;
        FailurePlayerSource.Text = $"{_store.CharacterName(actor)} — {pf.Source}";
        FailurePlayerText.Text = pf.Text;
        FailureHostSource.Text = $"{_store.CharacterName(host)} — {hf.Source}";
        FailureHostText.Text = hf.Text;
    }

    private string Expand(string text, string actor, string host) =>
        text.Replace("{actor}", _store.CharacterName(actor), StringComparison.OrdinalIgnoreCase)
            .Replace("{partner}", _store.CharacterName(host), StringComparison.OrdinalIgnoreCase);
}
