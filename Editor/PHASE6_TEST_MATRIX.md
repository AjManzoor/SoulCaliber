# Phase 6A/6B — Manual acceptance tests

1. Launch Studio and open the same master content database used by Godot.
2. Select an interaction, a node, and a **saved** choice; open Choice Mechanics.
3. Effect CRUD: create a SUCCESS/recipient-to-initiator/Fear/+15 effect; save; close/reopen the window; verify it is there. Edit to +20; reopen; verify +20. Delete; reopen; verify gone.
4. Add SUCCESS/recipient-to-initiator/Fear/+15 and FAILURE/recipient-to-initiator/Respect/-10. Confirm both are retained. Add SUCCESS/initiator-to-recipient/Trust/-5. Verify independent directions and outcomes.
5. Try adding the same success+direction+attribute effect again: it should be rejected with an explanatory message, not overwritten.
6. Skill check: add Intimidation vs Resolve at 0.5/0.5, INTIMIDATION mode. Reopen; verify. Change to STANDARD; reopen; verify. Delete; reopen; verify no check.
7. Requirement: add recipient-to-initiator/Trust/minimum 40, no maximum. Verify after reopen; try minimum 80 + maximum 30 (reject), no bounds (reject), Fear minimum -10 (reject).
8. Modifier: add recipient-to-initiator/Fear/0.10; reopen; edit to -0.05; reopen; delete.
9. Change tabs/records with unsaved text: discard prompt should appear and allow cancellation.
10. Open Dialogue Designer for a different choice: previous mechanics should not have changed. The summary beneath the button should reflect the current selection.
11. Run the corresponding Godot interaction and verify the tooltip chances, option locking, win/loss effects, both directional relationship displays and savegame persistence. Restart the scene after editor changes.
12. Validate + Publish from Studio using a new release version. If content passes, package should include all rule rows in its SQLite snapshot. Missing optional images are warnings only.
13. Reopen a previously working Phase 5 character and introduction override and verify those features are intact.
