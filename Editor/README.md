# SoulCaliber Studio — Phase 6A + 6B: Choice Mechanics Editor

This is the **full WPF editor source project** built from the working Phase 5 Studio (including the high-contrast theme, character editor, unique introductions, phrase fix, publisher, artwork warnings and SQLite file-lock fix).

## What Phase 6 adds

Choose **Dialogue Designer → [interaction] → [round/node] → [choice] → Edit choice mechanics…**. A new window has four tabs:

1. **Relationship Effects:** add, edit and delete the consequence of a choice on SUCCESS or FAILURE. Choose a direction, one of the six relationship attributes, and a signed delta. You can add multiple effects to the same choice, including effects on *both* directional relationships. Duplicate effects for the same outcome + direction + attribute are prevented in the editor: edit the existing effect instead.
2. **Skill Check:** create, replace or remove the choice's single actor/defender skill check. Choose skills (Persuasion, Intimidation, Deception, Charm, Insight, Resolve), mode (STANDARD / INTIMIDATION), and nonnegative actor/defender factors. These fields already affect success chances in the Godot dialogue engine.
3. **Requirements:** add, edit and delete conditional choice locks based on any directional relationship. Optional minimum and/or maximum (Fear/Romance range 0–100; other attributes -100–100). **All requirements must pass for the choice to be available.**
4. **Chance Modifiers:** add, edit and delete additional relationship-derived bonuses or penalties to success probability. A `0.10` factor on Trust = 60 adds 6 percentage points. Existing engine relationship bonuses remain in effect.

Each save/delete runs in a SQLite transaction and first makes a safe backup of the master content database using the same Phase 5 backup mechanism. Other tabs, including **Advanced Tables**, remain available. The choice editor displays the configured number of effects, requirements and modifiers, and whether a skill check exists.

**No Godot C# changes or database migration are needed for this phase.** These are the existing `Effects`, `SkillChecks`, `Requirements` and `ChanceModifiers` tables, already loaded by `DialogueRepository` and evaluated by `DialogueEngine`. Restart the Godot interaction scene after making edits to reload its cached definitions.

## Installation

1. Make copies of your current Phase 5 WPF project and `Content/soulcaliber_master.db` before testing this version.
2. Extract this ZIP into a new editor folder; do not combine compiled `bin/obj` directories from the old project.
3. Open `Studio/SoulCaliberStudio.csproj` in **Visual Studio on Windows** (.NET 10 + WPF workload). Restore dependencies and rebuild.
4. Start the editor, open the **master content** database (not `soulcaliber_savegame.db` or `Published/v.../Content/content.db`).
5. Select an existing dialogue choice or create/save a new one, then click the new **Edit choice mechanics…** button.

## Suggested first end-to-end test

For the `INFORMATION_GATHERING` interaction (or any new choice, preferably in a test copy of the master DB):

**Choice: `INTIMIDATE`**

- Base success chance: 25% (configured in the existing Choice Editor).
- Skill Check: `INTIMIDATION`, `Intimidation` (initiator) vs `Resolve` (recipient), factors `0.5` / `0.5`.
- SUCCESS effect: `Recipient → Initiator`, `Fear`, `+15`.
- SUCCESS effect: `Recipient → Initiator`, `Trust`, `-3`.
- FAILURE effect: `Recipient → Initiator`, `Respect`, `-10`.
- FAILURE effect: `Recipient → Initiator`, `Trust`, `-5`.
- Optional requirement: `Recipient → Initiator`, `Respect`, minimum `-50` (locks the option when this character's respect falls below -50).
- Optional chance modifier: `Recipient → Initiator`, `Fear`, factor `0.05`.

Run in Godot with two characters. Observe the skill tooltip and success percentage; after choosing the action, check that the **correct direction** and **correct outcome** change their relationship values. Close and reopen the game: the changes should persist in `user://soulcaliber_savegame.db`.

**Be aware:** The engine's success chance is capped at 5–95%, and relationship values are clamped to their attribute ranges. If an attribute is already at its maximum/minimum, a configured effect may not change the displayed value. You can test with a clean save or characters who have neutral relationships.

## Scope and compatibility

- No new SQLite tables. All stable character and choice IDs are preserved.
- No overworld, duel, knowledge/rumour system, or conditional story greetings added in this phase.
- No requirement to set a specific host at runtime: Godot already accepts actor, partner and interaction IDs. The existing editor still has a **default host** field for defining interactions; making this optional is a separate, small future change (Phase 6C).
- Publishing validations now also check effect/requirement/modifier rows. Missing portrait files remain **warnings**, as in Phase 4.
- Existing Phase 5 Godot changes for node introductions must remain installed, but there are **no new Godot patch files** for Phase 6A/6B.

## Validation performed for this package

- All `.xaml` files parsed as XML and all referenced event handlers checked against code-behind.
- SQLite fixture smoke test passed for success/failure effects in both directions, requirements, chance modifiers, skill-check UPSERT and snapshot integrity.
- The full WPF app **could not be compiled or run in this Linux environment**; build and UI testing must be done in Visual Studio on Windows. If you see an error, send its file and line number.
