# SoulCaliber Studio — Phase 8: Interaction Test Lab

**Based on the current Phase 7B universal conditional-dialogue editor.** All previous authoring tools, reaction catalogue, portrait-path generator, and publisher remain in the project.

## What changed

A new **Testing** tab launches a modal **Interaction Test Lab**. Use it to choose two characters and an interaction, supply temporary relationship values, walk through dialogue rounds, and examine the results of choices.

Features:

- Choose the **initiator (left)**, **recipient (right)**, and **interaction**.
- Set all six directional relationship attributes in both directions. `Fear` and `Romance` are 0–100; the others are -100–100.
- Start at the interaction's actual `StartNodeKey`, display the **resolved introduction**, and list available/locked choices with calculated probabilities.
- **Force success**, **force failure**, or generate a **random roll** for an unlocked choice.
- Display both speakers' selected dialogue, source (`conditional`, `standard`, or `generic`), target reaction, next node, and before/after relationship values.
- Advance through subsequent nodes. Later introductions see relationship changes from earlier simulated choices.
- Re-evaluate dialogue and choices after changing test values, without saving those values to disk.
- **Inspector**: candidate-by-candidate explanation of identity/outcome filtering and relationship-condition failures; uses the **existing shared `ConditionalDialogueRules.cs`** used by Godot.
- **Validate tree**: warns about missing branch targets, unreachable nodes, nodes with no choices, contradictory conditional bounds, duplicate/equivalent conditional variants, and impossible relationship requirements.

## Critical safety properties

- The simulator uses its own `Microsoft.Data.Sqlite` connection with `Mode=ReadOnly`, `Pooling=false`, and `PRAGMA query_only=ON`.
- It never opens `soulcaliber_savegame.db`. The simulated relationship values exist **only in memory**; effects change only the test window's values.
- No schema migration, database writing, or Godot code patch is required.
- The WPF authoring tools remain writable as before. **The simulator itself is read-only.**

## Installation

1. Back up your working **Phase 7B** WPF editor and master content database.
2. Extract this complete source project into a new folder; open `Studio/SoulCaliberStudio.csproj` in Visual Studio on Windows and build it.
3. Launch Studio and open your existing `Content/soulcaliber_master.db`.
4. Choose the **Testing** tab and click **Launch interaction simulator...**.
5. No Godot patch is needed for Phase 8; the game remains unchanged.

If you prefer a file-by-file merge, copy these **six** files into your existing WPF project's `Studio/` folder (two are existing files, four are new):

- `MainWindow.xaml` — updated, adds Testing tab
- `MainWindow.xaml.cs` — updated, initialises Testing when a master DB is selected
- `MainWindow.Testing.cs` — new
- `InteractionSimulatorWindow.xaml` — new
- `InteractionSimulatorWindow.xaml.cs` — new
- `SimulationData.cs` — new

## First test — Information Gathering

1. Select **ARK** as initiator and **NOBUNOGI** as recipient, interaction **INFORMATION_GATHERING**.
2. Set `Trust` in the **B → A** column (Nobunogi's Trust towards Ark) to `0`. Click **Start / restart**. Confirm the default introduction (unless you have another eligible rule at 0).
3. Set that same value to `80` and click **Re-evaluate dialogue and choices**. For a `PARTNER_TO_ACTOR Trust ≥ 70` rule, you should now see its conditional introduction.
4. Look at **WHY WAS THIS DIALOGUE SELECTED?**. The inspector should show which rules were eligible and which were skipped.
5. Select one of your new choices. Click **Force success**; check the resulting player phrase, recipient reply, relationship changes, and next node.
6. Click **Next round / finish**. If the success path points to another node, the next introduction should load with the **newly modified simulated relationships**.
7. Change the numbers and replay with **Force failure**. Verify that the failure branch and effects differ as authored.
8. Close the testing window and reopen it: the temporary values are gone. The master content and actual Godot savegame were not modified.

## Comparison with Godot

The probability formula and relationship-clamp logic in the Test Lab are intentionally modelled on the current Godot `DialogueEngine.cs`. **The shared conditional selection algorithm is directly reused** via `ConditionalDialogueRules.cs`. Unlike a Godot scene, the simulator does not render portraits, run the game engine, or write interaction history.

For important interaction types, compare **one probability, one conditional greeting, and one success/failure effect in Godot** to confirm the simulator still agrees with the game's rules. If future Godot math changes, update the simulator as well; ultimately extracting the chance/effect rules into a Godot-independent shared .NET assembly would remove that duplication.

## Validation scope and limitations

Static reachability considers only explicit success/failure branches. It is **not** a proof that a path can be reached under a particular combination of personality/relationship requirements. Duplicate warnings are for identical conditions/scope/priority, not all logically equivalent predicates. Missing artwork is still handled by the existing Publisher as a warning.

This source package passed XML/XAML, event-handler, read-only-connection, and SQLite schema smoke checks. **It was not compiled or visually tested on Windows in this environment**, so the first Visual Studio build and runtime check are required.

Optional Python smoke check (standard library only):

```sh
python Tests/phase8_static_smoke.py
```
