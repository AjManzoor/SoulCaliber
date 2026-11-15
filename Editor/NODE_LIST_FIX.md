# SoulCaliber Studio — Dialogue Node List Visibility Fix

## Problem

In Phase 6D, the **ROUNDS / NODES** list was inside a `DockPanel` whose bottom-docked editing form consumed all available height. On typical displays, the node list (including `REBUFFED`) became invisible even though the database records were loaded.

## Fix

- The middle column uses a 3-row grid rather than a bottom-docked editing form.
- **New node**, **Save node**, and **Delete** buttons are pinned at the top.
- The selectable node list stays visible underneath the buttons.
- The node editing form scrolls independently below the list.
- No C#, SQLite schema, dialogue mechanics or Godot files were changed.

## How to apply

Copy `Studio/MainWindow.xaml` over the corresponding file in your working SoulCaliber Studio project and rebuild. Or open the complete project in this ZIP.

## How to use

1. Open Confrontation in **Dialogue Designer**.
2. Click `REBUFFED` in **ROUNDS / NODES** to edit that node's introduction and choices.
3. Click **New node** to create a new node in the currently selected interaction.
4. Edit the opening choice's **Next node on failure** dropdown to connect it to the node key.
5. Save the choice. A link to an existing node does *not* automatically create it.

Keep a database backup as usual before content editing. This update only affects the WPF UI layout and requires no data migration.

Validation performed: XAML parsed and all named controls/event handlers preserved. WPF cannot be run here on Windows, so verify the new layout visually after rebuilding.
