# Phase 8 — Interaction Test Lab test matrix

| Case | Steps | Expected |
|---|---|---|
| Open editor | Open master DB → Testing → Launch | Lab opens, lists characters and interactions |
| Invalid pair | Choose same character twice → Start | Warning, no test starts |
| Generic fallback | Start a pair with no matching conditional introduction | Original `Nodes.HostIntroduction` or ordinary `NodeIntroductions` is selected |
| Conditional threshold | Set partner→actor Trust=69, re-evaluate; then 70 | 69 fails Trust≥70; 70 passes if identity matches |
| Universal vs character-specific | Author both eligible rules, same node | The shared conditional selector chooses the more specific variant |
| Forced success | Choose unlocked option → Force success | Actor and recipient success lines, success reaction and effects, success branch |
| Forced failure | Replay same option → Force failure | Failure wording/reaction/effects and failure branch |
| Random roll | Start → Random roll | Roll 1..100, success iff roll ≤ displayed chance |
| Locked choice | Configure requirement unmet | Choice visibly marked LOCKED; cannot execute |
| Directional effect | Effect `PARTNER_TO_ACTOR Trust +10` | Only recipient→initiator Trust changes |
| Clamp | Start Trust=98, effect +10 | Trust becomes 100, not 108 |
| Negative / bounded stats | Trust=-100 is allowed; Fear=-1 rejected | Input validation follows relationship model |
| Multi-round | Choice targets another node → Continue | New introduction and choices load, using updated simulated values |
| End | Choice next key blank → Continue | Conversation ends cleanly; can restart |
| Inspector | Save several overlapping variants → Re-evaluate | Explains speaker/counterpart/outcome matches and condition failures |
| Branch validator | Create detached node then Validate tree | Warns node unreachable |
| Duplicate variants | Same scope, priority, conditions | Warning that one variant shadows another |
| Save isolation | Simulate several effects, close lab, relaunch | Defaults to neutral test relationships; real player saves unaffected |
| Persistence isolation | Run lab without starting Godot | Master DB is opened read-only; simulator does not touch `user://` save |

## First-day confidence checks

Compare with the real Godot scene for the **same** two IDs, node, choice, skills and relationship values:

- Initial introduction wording and conditional variant source
- Calculated probability (including rounding and 5–95% clamp)
- One success and one failure's relationship deltas and branches

If any differ, investigate the implementation mismatch; the simulator is a *test harness*, not the authoritative runtime.
