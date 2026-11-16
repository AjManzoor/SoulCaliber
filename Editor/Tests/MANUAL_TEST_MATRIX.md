# Phase 11 manual test matrix

| ID | Action | Expected outcome |
|---|---|---|
| T01 | Start ARK -> NOBUNOGI, select an unlocked choice | Six stages appear in the live mechanics pane |
| T02 | Select a different choice | Audit and displayed chance update to the selected choice |
| T03 | Change a relationship value and press Re-evaluate | Profile terms and final chance recalculate; no other data changes |
| T04 | Toggle BRAVE on for recipient with FEAR +20 | Success preview becomes +10, assuming no clamp |
| T05 | Toggle TIMID instead | Success preview becomes +35 |
| T06 | Toggle BRAVE and TIMID simultaneously | Success preview becomes +18 |
| T07 | Force success | Applied relationship value exactly matches success preview |
| T08 | Force failure | Applied relationship value exactly matches failure preview |
| T09 | Trait affects GAIN only, effect is negative | Trait not applied to negative delta |
| T10 | Trait applies to FAILURE only, force SUCCESS | No failure-only adjustment applied |
| T11 | Multiple effects on same direction and attribute | Effects preview and apply in authored order; successive values correct |
| T12 | Current relationship near max or min | Audit shows adjusted vs applied deltas and [CLAMPED] warning |
| T13 | End round after forcing outcome | Old audit retained until Next; next-node audit reflects new values |
| T14 | Click Restore original character traits | Sandbox reflects master catalogue assignments, without writes |
| T15 | Switch initiator and recipient and restart | Original traits for new pair loaded, receiver's traits apply appropriately |
| T16 | Click Copy audit | A readable six-stage report is copied to clipboard |
| T17 | Open Dialogue inspector and Validate tree | Existing dialogue selection inspector and validation still work |
| T18 | Test a choice with no skill check or no effects | Audit reports zero skill points or no effects without crashing |
| T19 | Close Studio, open master content in another session | Personality trait assignments and content unchanged |
| T20 | Use a deliberately oversized profile factor | Stage 4 shows raw chance and capped 5–95% with warning |

No Godot patch or new master database migration is needed for Phase 11.
