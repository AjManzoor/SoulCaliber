# Phase 5 manual test matrix

Run these in Windows after installing Studio and the two Godot code changes:

| Test | Expected |
|---|---|
| Open existing Phase 4 master DB in Phase 5 WPF | Studio makes backup; `NodeIntroductions` table appears once; other tables unchanged |
| Add generic node text only | Old generic introduction displays for all host/visitor pairs without an override |
| Add `NOBUNOGI` intro with visitor blank | All visitors see Nobunogi's intro, except pairs with a more specific intro |
| Add `NOBUNOGI` with visitor `ARK` intro | Ark sees the pair intro; Youri still sees Nobunogi's character intro |
| Add `YOURI` intro | Saving it does not replace Nobunogi's intro |
| Edit existing speaker / visitor fields | Identity controls are locked; adding a different identity requires New |
| Try to create duplicate exact pair | Save blocked by editor + SQLite unique index |
| Delete a pair intro | Falls back to host-specific intro, then generic when none |
| Preview actor+host choice | Shows correct actor/host phrases on both SUCCESS and FAILURE |
| Create pair-specific ANY phrase plus speaker-only SUCCESS phrase | Pair-specific ANY wins for this pair; speaker-only SUCCESS remains for other visitors |
| Delete node with intro override | Blocked until dependent introductions are deleted |
| Delete character referenced in intro override | Blocked |
| Publish v0.2.0 | Publisher validates `NodeIntroductions`, new package includes rows, missing artwork remains warnings |
| Export Godot v0.2.0 + install v0.2.0 content | New introductions work in standalone release |
| Load older content.db with patched Godot | No `NodeIntroductions` table → generic introduction still works |

**Important:** Keep master content and savegame separate. Do not test deletion against your only master DB without a backup.
