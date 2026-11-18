using Godot;

namespace SoulCaliber.Dialogue;

/// <summary>Small, non-destructive startup failure notice used by both scenes.</summary>
internal static class SaveGameStartupDialog
{
    public static void Show(Node owner, string title, string message)
    {
        var dialog = new AcceptDialog
        {
            Title = title,
            DialogText = message
        };
        owner.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(720, 300));
    }
}
