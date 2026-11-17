# SoulCaliber Interaction Pack: A Warrior's Loyalty

**Status:** Fully authored **draft**, with an optional standalone Studio master-DB importer. **No changes have been made to your project or your database.**

**Intended characters:** Law visits Nobunogi.
**Interaction key:** `NOBUNOGI_WARRIORS_LOYALTY`
**Start node:** `OPENING`

This pack was authored using the SoulCaliber Studio dialogue schema and mechanics observed in the uploaded `Editor(1).zip` and the Phase 12A–12E status and visibility system developed in our conversation. The latest migrated **master SQLite database was not supplied**, so the importer's tests use a synthetic schema. The importer **checks your real database before writing** and refuses imports when required tables, columns, reaction types, attributes, profiles, or statuses are missing. You should still run Studio's validation and test the result after import.

## Contents

| File | Purpose |
| --- | --- |
| `A_Warriors_Loyalty_full_script.md` | Every node, choice, dialogue line, exact gate, probability profile and outcome |
| `nobunogi_warriors_loyalty.json` | Machine-readable complete interaction specification |
| `A_Warriors_Loyalty_flowchart.mmd` | Readable Mermaid overview of important branches |
| `import_into_studio.py` | Optional Python importer for a **compatible Studio master DB**; read-only by default |
| `selftest_importer.py` | Synthetic-schema import/rollback regression tests |

## Story outline

The same training-ground conversation responds to **Nobunogi → Law** status and numerical relationship values. A Neutral Nobunogi can be recruited by offering an equal alliance—or coerced through fear. An Enemy Nobunogi can be approached for a truce. A trusted Ally will discuss the threat posed by Mizuri. An Ally who respects Law may open up; an Ally who fears him may be barely concealing her resentment.

15 nodes, 50 choices, 11 hidden-on-unmet choices, 22 skill checks, 18 status gates, 14 numeric gates, 50 numeric outcome effects and 7 status outcome effects. All nodes are reachable, all branching targets resolve, all nodes have an unconditionally offered way forward, and status gates use the direction used by the Godot engine.

## Import into SoulCaliber Studio (optional)

1. **Close Studio and Godot.** Verify you are using the **master authoring database**, not the published content snapshot or the savegame database.
2. As a one-off manual precaution, make a copy of the master database if you want to be able to revert easily. The importer itself deliberately creates **no backups**.
3. Install Python 3.10+ if necessary, and open a terminal inside the unpacked interaction folder.
4. **Dry-run validation (no writes):**

   ```powershell
   py import_into_studio.py --db "C:\path\to\YOUR_MASTER_DATABASE.db"
   ```

5. If the script reports `DRY RUN PASSED`, carry out the import:

   ```powershell
   py import_into_studio.py --db "C:\path\to\YOUR_MASTER_DATABASE.db" --apply
   ```

6. Reopen SoulCaliber Studio. Find `A Warrior's Loyalty` in the Dialogue Designer. Review and tweak the dialogue or outcomes, validate the content, then use Studio's normal **Publish** workflow.
7. Run the Godot prototype using interaction key `NOBUNOGI_WARRIORS_LOYALTY`, with **Law** as initiator and **Nobunogi** as recipient.

The script imports all 15 nodes, 50 options, check profiles, skill checks, status and numeric requirements, outcome effects, and HIDE/LOCK settings in **one SQLite transaction**. It **refuses** to overwrite an interaction with the same key and rolls back everything on an error. It does **not** bind the interaction to specific character IDs; existing Studio/game participant selection determines who speaks the default lines. If the characters use different IDs, choose the right ones in the prototype.

## Implementation notes

- `PARTNER_TO_ACTOR` means **Nobunogi → Law** in the intended encounter; `ACTOR_TO_PARTNER` means **Law → Nobunogi**.
- Requirements **all** must be satisfied. `HIDE` removes a choice when any requirement fails; `LOCK` keeps it visible but disabled.
- Personality traits continue to modify the **magnitude of numerical effects** according to each character's currently authored traits. They do not change the skill roll just because they exist.
- The current ChanceFormula clamps final chances to **5–95%**. Choices marked `base_chance: 100` are navigation choices with the **same target and dialogue text on both success and failure**, and no outcome effect, so even an unlikely failed navigation roll doesn't change the story path.
- This sample contains **no actual duel event, memory event, recruitment-event trigger, or cutscene trigger**, because those systems have not yet been implemented. Ally status changes are supported; adding party membership/event changes is later work.
- `HostIntroduction`, `DefaultPlayerLine`, `DefaultHostSuccessLine`, `DefaultHostFailureLine` are used so the interaction can be played without extra phrase/image records. Reactions use keys from the seeded Studio reaction catalogue.
- Depending on character stats and your edited check-profile modifiers, actual success probabilities may differ from base chances; this is intentional.

## Test scenarios

**Recruitment:** Set Nobunogi → Law `NEUTRAL`; `OPENING` should reveal a recruitment option. Succeed at the equal-alliance proposal with enough Respect; she becomes an `ALLY`, leading to `OATH`, then `ALLY_COUNCIL`.

**Enemy reconciliation:** Set Nobunogi → Law `ENEMY`. `OPENING` reveals the truce branch. A successful admission returns her status to `NEUTRAL`; another recruitment attempt can then occur.

**Frightened ally:** Set Nobunogi → Law `ALLY`, Fear `90` or higher. A hidden fear branch appears. A forceful demand can increase resentment; failing can change her status to `ENEMY`.

**Respected ally:** Set Nobunogi → Law `ALLY`, Respect `75` or higher, Fear `30` or lower. A different, trust-based conversation appears.

**Visibility:** With unmet Ally status requirements, verify the Ally-specific choices are absent rather than visibly locked. The Studio Interaction Test Lab's Choice gates inspector should still report them as hidden.

## Validation performed here

- JSON node IDs, choice IDs, transitions, probability profile keys, reaction keys, attribute and skill keys, direction semantics, HIDE/LOCK values, and effect uniqueness.
- All 15 nodes are reachable ignoring requirement conditions, and every node has an unconditional exit/progression option.
- Synthetic SQLite schema: dry run does not write; successful apply inserts all expected record counts; a mid-import SQL error rolls back **all** changes; a second import is refused.

**Not verified here:** import into your *actual* latest master SQLite database, Studio content publishing, Godot runtime behavior, and your installed character-specific assets. The importer checks compatibility on your machine before it writes.
