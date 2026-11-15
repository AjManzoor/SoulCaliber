# SoulCaliber Studio — Phase 7B: Universal conditional dialogue

This update uses the **Phase 7 condition-editor fix** as the baseline. It supports optional character identifiers for **conditional introductions and replies**.

## What a blank means

- **Speaking character blank**: any speaker (for introductions, the partner/right-hand participant).
- **Other character blank**: any counterpart (for introductions, the actor/left-hand participant).
- **Both blank**: this conditional variant applies to any two different participants **when every condition passes**.
- The **interaction and dialogue node or choice remain specific**. This does *not* cause a line from Confrontation to appear in Information Gathering.
- **Direction remains relative to the actual actor and partner**, even when IDs are blank: `PARTNER_TO_ACTOR` means the partner's feelings towards the actor.

## Priority among eligible candidates

1. Conditional variants whose conditions pass take precedence over standard introduction/line overrides.
2. More specific conditional variants win: **pair > speaker-only > counterpart-only > both blank**.
3. An exact outcome beats `ANY`, then configured priority (0–1000), then number of conditions, then oldest ID.
4. If no conditional matches, existing specific lines are tried before the generic node/choice fallback.

A universal rule *can override* a standard character-specific phrase when its condition matches, which is the intended meaning of conditional variants taking precedence. An explicit conditional pair-specific variant overrides a universal one regardless of priority.

## Installation

1. Back up your working Studio folder and `Content/soulcaliber_master.db` (close Godot, Studio and DB Browser beforehand).
2. Replace Studio with `Studio/` from the full ZIP, or copy **only changed files** from the patch ZIP. Keep `Migrations/004_UniversalConditionalDialogue.sql` alongside the Studio project in the supplied relative location: the csproj embeds `../Migrations/004_UniversalConditionalDialogue.sql`.
3. Build and launch WPF Studio, open your master database. It will take a SQLite snapshot backup and upgrade `ConditionalDialogueVariants.CharacterId` from `NOT NULL` to nullable (one time). Existing IDs and condition rows are preserved. It does not touch player saves or ordinary introductions/lines.
4. In Godot replace `Dialogue/ConditionalDialogueRules.cs` and `Dialogue/DialogueRepository.cs` with the files from `GodotPatch/Dialogue/`. **Keep your existing `DialogueEngine.cs` and `InteractionScene.cs`**; they are unchanged by this patch.
5. Restart the game (to reload content) and publish a new content version before the next standalone export.

## Test

1. In Studio select **Information Gathering → OPENING** and open the conditional introduction editor.
2. Create a **new** variant with **both character dropdowns blank**. Enter text `I can tell you something, but you must earn my trust.`
3. Add `PARTNER_TO_ACTOR / Trust / Min 70 / Max blank`, save the variant.
4. Preview `ARK` (actor) and `NOBUNOGI` (partner): Trust 0 -> fallback; Trust 80 -> universal variant.
5. Preview another pair with Trust 80 -> universal variant again.
6. Add a Nobunogi-specific conditional variant for same node and same Trust threshold -> Nobunogi-specific variant should win for Nobunogi, universal for others.
7. Make sure the same rule does not apply in a different interaction or dialogue node.
8. In Godot the trust check uses the **actual saved** relationships, not preview values. Check both actor/partner orderings and relationship direction.

## Notes

- No new game mechanics or changes to the savegame schema.
- This is **not** a global greeting library: each universal rule remains attached to its selected interaction node/choice.
- You cannot change an existing variant's speaker/other identity in place. Create a new variant instead; the editor protects existing identities.
- Windows WPF build and Godot runtime tests still need to be performed on your machine.
