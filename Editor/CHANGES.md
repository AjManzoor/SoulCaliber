# Patch contents

Studio changes:
- `DialogueDesignerStore.cs`: interaction record and saving no longer take HostCharacterId; inserts use an empty compatibility value.
- `MainWindow.xaml`, `.Designer.cs`, `.Characters.cs`: remove host selection and improve preview defaults.
- `ContentPublisher.cs`: ignore HostCharacterId and validate nodes/choices normally.
- `CharacterStudioStore.cs`, `DatabaseEditor.cs`: remove legacy host from deletion checks.

Godot:
- `GodotPatch/Dialogue/DialogueEngine.cs`: require actor and partner keys explicitly; keep Phase 5 introductions.
- `GodotPatch/InteractionLauncher.cs`: optional helper for instantiating a configured InteractionScene before its _Ready.

No database migration and no savegame changes.
