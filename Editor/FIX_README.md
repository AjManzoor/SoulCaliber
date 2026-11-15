# Phase 7 — Conditional dialogue editor usability fix

This is an update to the **WPF editor only**, based on the Phase 7 project.
No changes to the Godot files, migrations, database tables or publisher.

## Cause
The old editor displays conditions in a list but cannot edit the selected row:
clicking a condition never loads its Min/Max into the input fields, and there is no Update action.
The input controls also appear below the conditions list, which makes them easy to miss in a short window.

## Changes
- Condition fields and action buttons now appear **above** the list.
- Select a condition to populate Direction, Attribute, Min, and Max.
- Click **Update selected condition** to apply an edit locally.
- Click **Save variant and conditions** to persist edits to the master SQLite DB.
- Add, update, and remove operations validate the relationship bounds. A duplicate direction/attribute cannot be added.
- When switching variants, existing conditions auto-select the first row, and New clears the old condition fields.
- Explanatory text clarifies the relationship direction for host introductions.

## Installation
Copy the two updated files from `Studio/` into your existing Phase 7 WPF project:
1. `ConditionalDialogueWindow.xaml`
2. `ConditionalDialogueWindow.xaml.cs`
Rebuild SoulCaliber Studio in Visual Studio, then reopen the master DB.

## Test: Nobunogi greeting when Trust >= 70
1. Select the OPENING node and open the Conditional Dialogue window, choose **Introduction**.
2. Select or create the variant with Speaker `NOBUNOGI`, Other `ARK`.
3. Set dialogue text to a greeting (not the word `Trust`).
4. Select the existing `PARTNER_TO_ACTOR: Trust >= 0` condition.
5. Set **Min** to `70` and **Max** blank; click **Update selected condition**.
6. Click **Save variant and conditions**.
7. Under Preview, choose Actor `ARK`, Partner `NOBUNOGI`. With PARTNER_TO_ACTOR Trust 0 you should get fallback; with 80 the conditional greeting should be selected.
8. If the saved variant is for Speaker `ARK` or Other blank, create a *new* variant: those identifiers are immutable for existing records.

The update is source-checked and the XAML was parsed successfully, but the WPF UI has not been run on Windows here.
