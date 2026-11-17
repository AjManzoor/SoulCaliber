# A Warrior’s Loyalty — full authoring script

**Interaction key:** `NOBUNOGI_WARRIORS_LOYALTY` · **Host:** Nobunogi · **Visitor:** Law · **Start:** `OPENING`

A training-ground conversation about honour, respect, recruitment and fear. The same entrance yields different branches depending on directed status, Fear, Trust, and Respect.

**How to read the rules:** `PARTNER_TO_ACTOR` means Nobunogi → Law; `ACTOR_TO_PARTNER` means Law → Nobunogi. Every gate must pass. `HIDE` removes an unmet choice; `LOCK` leaves it disabled. Percentages are base chances, before active profile/skill modifiers.

> **Navigation note:** The current ChanceFormula caps results at 95% even with a 100% base chance. All simple navigation choices deliberately use identical success and failure dialogue, destinations and no effects, so the 5% failed roll does not alter the path.

## Design routes

- **Neutral / recruitment:** OPENING → PROPOSAL → OATH → ALLY_COUNCIL.
- **Enemy / truce:** OPENING → TRUCE → COMMON_GROUND.
- **Ally / trust:** OPENING → ALLY_COUNCIL → WARNING.
- **Ally / high fear:** OPENING → FEAR_COUNCIL → COERCED or REPRISAL.
- **Ally / high respect, low fear:** OPENING → RESPECT_COUNCIL → WARNING.

## Complete nodes and choices

### 01. The edge of a conversation (`OPENING` · round 1)

**Nobunogi (introduction):** Nobunogi runs a cloth along her blade without looking up. "If you came to watch, find a better seat. If you came to speak, speak."

**1. Ask about her training** — `OPEN_TALK`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** You have been at this since dawn.
- **Success:** Nobunogi: "And I shall still be at it when you are asleep."  
  Reaction `RECEPTIVE` · Next `MEASURE` · Effects: none
- **Failure:** Nobunogi: "And I shall still be at it when you are asleep."  
  Reaction `RECEPTIVE` · Next `MEASURE` · Effects: none

**2. Ask her to stand with you** — `OPEN_RECRUIT`

- **Gate:** PARTNER_TO_ACTOR status == NEUTRAL · **Visibility:** HIDE
- **Chance:** base 70% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** I could use a warrior of your ability beside me.
- **Success:** Nobunogi: "Then tell me why I should lend you my blade."  
  Reaction `INTERESTED` · Next `PROPOSAL` · Effects: PARTNER_TO_ACTOR.RESPECT +4
- **Failure:** Nobunogi: "You ask as though I were waiting for an invitation."  
  Reaction `DISMISSIVE` · Next `REBUFF` · Effects: PARTNER_TO_ACTOR.RESPECT -3

**3. Discuss your alliance** — `OPEN_ALLY`

- **Gate:** PARTNER_TO_ACTOR status == ALLY · **Visibility:** HIDE
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** There is something we should discuss as allies.
- **Success:** Nobunogi: "We are allies. That buys you my attention, not my agreement."  
  Reaction `RECEPTIVE` · Next `ALLY_COUNCIL` · Effects: none
- **Failure:** Nobunogi: "We are allies. That buys you my attention, not my agreement."  
  Reaction `RECEPTIVE` · Next `ALLY_COUNCIL` · Effects: none

**4. Offer to settle the hostility** — `OPEN_ENEMY`

- **Gate:** PARTNER_TO_ACTOR status == ENEMY · **Visibility:** HIDE
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** I did not come to reopen our quarrel.
- **Success:** Nobunogi: "Then be careful which wound you touch."  
  Reaction `RECEPTIVE` · Next `TRUCE` · Effects: none
- **Failure:** Nobunogi: "Then be careful which wound you touch."  
  Reaction `RECEPTIVE` · Next `TRUCE` · Effects: none

**5. Ask why she hesitates around you** — `OPEN_FEARED_ALLY`

- **Gate:** PARTNER_TO_ACTOR status == ALLY AND PARTNER_TO_ACTOR.FEAR >= 90 · **Visibility:** HIDE
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** You watch your words around me. Why?
- **Success:** Nobunogi: "You notice more than I thought."  
  Reaction `RECEPTIVE` · Next `FEAR_COUNCIL` · Effects: none
- **Failure:** Nobunogi: "You notice more than I thought."  
  Reaction `RECEPTIVE` · Next `FEAR_COUNCIL` · Effects: none

**6. Ask her what she really thinks of you** — `OPEN_HONOURED_ALLY`

- **Gate:** PARTNER_TO_ACTOR status == ALLY AND PARTNER_TO_ACTOR.RESPECT >= 75 AND PARTNER_TO_ACTOR.FEAR <= 30 · **Visibility:** HIDE
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** I would hear your judgement, without flattery.
- **Success:** Nobunogi: "You have earned something better than flattery."  
  Reaction `RECEPTIVE` · Next `RESPECT_COUNCIL` · Effects: none
- **Failure:** Nobunogi: "You have earned something better than flattery."  
  Reaction `RECEPTIVE` · Next `RESPECT_COUNCIL` · Effects: none

**7. Leave her to her training** — `OPEN_LEAVE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** I will not interrupt you.
- **Success:** Nobunogi: "Good."  
  Reaction `RECEPTIVE` · Next `END` · Effects: none
- **Failure:** Nobunogi: "Good."  
  Reaction `RECEPTIVE` · Next `END` · Effects: none

### 02. The measure of strength (`MEASURE` · round 2)

**Nobunogi (introduction):** She tests the edge with her thumb. "Some people sharpen steel. Others sharpen excuses."

**1. Praise her discipline** — `MEASURE_PRAISE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 65% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** Few would train this long without an audience.
- **Success:** Nobunogi: "An audience is for performers. But you have noticed the work."  
  Reaction `PLEASED` · Next `COMMON_GROUND` · Effects: PARTNER_TO_ACTOR.RESPECT +8
- **Failure:** Nobunogi: "That sounded perilously close to flattery."  
  Reaction `SUSPICIOUS` · Next `REBUFF` · Effects: PARTNER_TO_ACTOR.RESENTMENT +4

**2. Question whether training is enough** — `MEASURE_CHALLENGE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 55% · MIGHT vs FINESSE (×0.4/×0.4) · profile `STANDARD`
- **Law:** Training means little until someone tests it.
- **Success:** Nobunogi: "At last, a sensible question. What would you test?"  
  Reaction `INTERESTED` · Next `PROOF` · Effects: PARTNER_TO_ACTOR.RIVALRY +10
- **Failure:** Nobunogi: "You mistake noise for a challenge."  
  Reaction `UNIMPRESSED` · Next `REBUFF` · Effects: PARTNER_TO_ACTOR.RESPECT -4

**3. Ask what she is preparing for** — `MEASURE_LISTEN`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** Who are you preparing to face?
- **Success:** Nobunogi: "Whoever proves stronger than the last. There must always be someone."  
  Reaction `RECEPTIVE` · Next `COMMON_GROUND` · Effects: none
- **Failure:** Nobunogi: "Whoever proves stronger than the last. There must always be someone."  
  Reaction `RECEPTIVE` · Next `COMMON_GROUND` · Effects: none

**4. Change the subject** — `MEASURE_BACK`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** I had something else to ask.
- **Success:** Nobunogi: "Then ask it."  
  Reaction `RECEPTIVE` · Next `OPENING` · Effects: none
- **Failure:** Nobunogi: "Then ask it."  
  Reaction `RECEPTIVE` · Next `OPENING` · Effects: none

### 03. The price of an alliance (`PROPOSAL` · round 2)

**Nobunogi (introduction):** Nobunogi sheaths the blade. "I will not follow a name, a banner, or a frightened promise. Give me a reason."

**1. Say you intend to confront Mizuri** — `PROPOSAL_MIZURI`

- **Gate:** PARTNER_TO_ACTOR status == NEUTRAL · **Visibility:** LOCK
- **Chance:** base 62% · WILLPOWER vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** Mizuri stands above every other warrior. I intend to face her.
- **Success:** Nobunogi: "Then our roads may run together. Do not expect me to kneel along the way."  
  Reaction `IMPRESSED` · Next `OATH` · Effects: PARTNER_TO_ACTOR.RESPECT +10, PARTNER_TO_ACTOR.LOYALTY +8, PARTNER_TO_ACTOR status -> ALLY
- **Failure:** Nobunogi: "You speak of the summit. I do not yet believe you can climb."  
  Reaction `UNIMPRESSED` · Next `REBUFF` · Effects: PARTNER_TO_ACTOR.RESPECT -5

**2. Offer an alliance of equals** — `PROPOSAL_HONOUR`

- **Gate:** PARTNER_TO_ACTOR status == NEUTRAL AND PARTNER_TO_ACTOR.RESPECT >= 60 · **Visibility:** HIDE
- **Chance:** base 75% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `COOPERATIVE`
- **Law:** No obedience. We choose the same fight, and each keeps their honour.
- **Success:** Nobunogi: "An equal pact, then. Break it and you answer to me."  
  Reaction `ACCEPTING` · Next `OATH` · Effects: PARTNER_TO_ACTOR.TRUST +12, PARTNER_TO_ACTOR.RESPECT +5, PARTNER_TO_ACTOR status -> ALLY
- **Failure:** Nobunogi: "Equality is easily promised when nothing has been risked."  
  Reaction `WARY` · Next `REBUFF` · Effects: PARTNER_TO_ACTOR.TRUST -5

**3. Demand her loyalty through fear** — `PROPOSAL_THREAT`

- **Gate:** PARTNER_TO_ACTOR status == NEUTRAL AND PARTNER_TO_ACTOR.FEAR >= 70 · **Visibility:** HIDE
- **Chance:** base 70% · MIGHT vs WILLPOWER (×0.4/×0.4) · profile `INTIMIDATION`
- **Law:** You know what I can do. Do not make me prove it.
- **Success:** Nobunogi: "I will come. Remember that fear is not fealty."  
  Reaction `INTIMIDATED` · Next `COERCED` · Effects: PARTNER_TO_ACTOR.FEAR +6, PARTNER_TO_ACTOR.TRUST -15, PARTNER_TO_ACTOR.RESENTMENT +12, PARTNER_TO_ACTOR status -> ALLY
- **Failure:** Nobunogi: "A threat is not a victory. You have made an enemy."  
  Reaction `ANGRY` · Next `REPRISAL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +20, PARTNER_TO_ACTOR status -> ENEMY

**4. Let the question rest** — `PROPOSAL_WITHDRAW`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** You need not answer now.
- **Success:** Nobunogi: "Good. Better a question left standing than a promise that falls."  
  Reaction `RECEPTIVE` · Next `MEASURE` · Effects: none
- **Failure:** Nobunogi: "Good. Better a question left standing than a promise that falls."  
  Reaction `RECEPTIVE` · Next `MEASURE` · Effects: none

### 04. A badly chosen word (`REBUFF` · round 2)

**Nobunogi (introduction):** Her expression closes. "If that was your best argument, keep the second one to yourself."

**1. Admit you misjudged her** — `REBUFF_APOLOGISE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 65% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** I spoke poorly. That was not what I meant.
- **Success:** Nobunogi: "Then say what you mean, and I may listen."  
  Reaction `RECEPTIVE` · Next `COMMON_GROUND` · Effects: PARTNER_TO_ACTOR.TRUST +5
- **Failure:** Nobunogi: "You are more interested in escaping blame than speaking plainly."  
  Reaction `DISMISSIVE` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +5

**2. Press her harder** — `REBUFF_INSIST`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 40% · MIGHT vs WILLPOWER (×0.4/×0.4) · profile `INTIMIDATION`
- **Law:** You heard me. I will not beg you to understand.
- **Success:** Nobunogi: "A stubborn answer. I almost respect it."  
  Reaction `INTERESTED` · Next `PROPOSAL` · Effects: PARTNER_TO_ACTOR.FEAR +8
- **Failure:** Nobunogi: "You mistake stubbornness for courage."  
  Reaction `ANGRY` · Next `REPRISAL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +10

**3. End the conversation** — `REBUFF_LEAVE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** We have nothing more to say today.
- **Success:** Nobunogi: "Then we agree on something."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "Then we agree on something."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

### 05. A moment without posturing (`COMMON_GROUND` · round 3)

**Nobunogi (introduction):** For a moment, she looks past you towards the open ground. "Most people ask me who I have beaten. Few ask who I want to fight."

**1. Ask what she knows about Mizuri** — `GROUND_ASK_MIZURI`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 72% · INTELLECT vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** Have you ever seen Mizuri fight?
- **Success:** Nobunogi: "No. But I have heard enough to know rumours are not victories. Listen."  
  Reaction `REFLECTIVE` · Next `WARNING` · Effects: PARTNER_TO_ACTOR.TRUST +6
- **Failure:** Nobunogi: "And I have heard enough people talk as though they have seen the impossible."  
  Reaction `WARY` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +3

**2. Say what you are fighting for** — `GROUND_SHARE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** I do not want to win merely to be called strongest.
- **Success:** Nobunogi: "Then decide what you do want before the fight begins."  
  Reaction `RECEPTIVE` · Next `PROPOSAL` · Effects: none
- **Failure:** Nobunogi: "Then decide what you do want before the fight begins."  
  Reaction `RECEPTIVE` · Next `PROPOSAL` · Effects: none

**3. Leave on better terms** — `GROUND_PART`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** I will think on what you said.
- **Success:** Nobunogi: "Thinking is useful. Eventually you must act."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "Thinking is useful. Eventually you must act."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

### 06. A test without drawing steel (`PROOF` · round 3)

**Nobunogi (introduction):** She steps close enough to study your stance. "Everyone claims to want a worthy opponent. Few recognise one."

**1. Point out a weakness in her stance** — `PROOF_OBSERVE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 67% · INTELLECT vs CUNNING (×0.4/×0.4) · profile `STANDARD`
- **Law:** You favour your forward foot. A feint could draw you out.
- **Success:** Nobunogi: "You have eyes after all. I corrected that habit this morning."  
  Reaction `IMPRESSED` · Next `COMMON_GROUND` · Effects: PARTNER_TO_ACTOR.RESPECT +9
- **Failure:** Nobunogi: "You saw something. You did not understand it."  
  Reaction `UNIMPRESSED` · Next `REBUFF` · Effects: PARTNER_TO_ACTOR.RESPECT -4

**2. Claim you could defeat her** — `PROOF_BOAST`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 45% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** Give me one exchange and I will show you the difference.
- **Success:** Nobunogi: "Perhaps you will. That is why I am listening."  
  Reaction `AMUSED` · Next `PROPOSAL` · Effects: PARTNER_TO_ACTOR.RIVALRY +12
- **Failure:** Nobunogi: "Your mouth is faster than your feet."  
  Reaction `DISMISSIVE` · Next `REPRISAL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +7

**3. Refuse to turn talk into a contest** — `PROOF_DECLINE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** We need not make every conversation a contest.
- **Success:** Nobunogi: "No. Only the ones that matter."  
  Reaction `RECEPTIVE` · Next `MEASURE` · Effects: none
- **Failure:** Nobunogi: "No. Only the ones that matter."  
  Reaction `RECEPTIVE` · Next `MEASURE` · Effects: none

### 07. What allies owe one another (`ALLY_COUNCIL` · round 3)

**Nobunogi (introduction):** She folds her arms. "If you are about to order me somewhere, choose your words carefully."

**1. Ask her to plan the fight with you** — `ALLY_PLAN`

- **Gate:** PARTNER_TO_ACTOR status == ALLY AND PARTNER_TO_ACTOR.TRUST >= 40 · **Visibility:** HIDE
- **Chance:** base 74% · INTELLECT vs INTELLECT (×0.4/×0.4) · profile `COOPERATIVE`
- **Law:** I want your judgement, not your obedience. How should we approach Mizuri?
- **Success:** Nobunogi: "Then you shall have it. First, abandon the idea of a glorious charge."  
  Reaction `IMPRESSED` · Next `WARNING` · Effects: PARTNER_TO_ACTOR.TRUST +7
- **Failure:** Nobunogi: "You need an answer. I need more than a question."  
  Reaction `UNMOVED` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.RESPECT -3

**2. Ask her to take a risk for you** — `ALLY_REQUEST`

- **Gate:** PARTNER_TO_ACTOR status == ALLY · **Visibility:** LOCK
- **Chance:** base 55% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `COOPERATIVE`
- **Law:** When the time comes, will you stand in the more dangerous position?
- **Success:** Nobunogi: "If it is the right position. Not simply because you asked."  
  Reaction `ACCEPTING` · Next `WARNING` · Effects: PARTNER_TO_ACTOR.LOYALTY +6
- **Failure:** Nobunogi: "You have confused an ally with a shield."  
  Reaction `OFFENDED` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.TRUST -8

**3. Ask for her private assessment** — `ALLY_RESPECT`

- **Gate:** PARTNER_TO_ACTOR status == ALLY AND PARTNER_TO_ACTOR.RESPECT >= 75 AND PARTNER_TO_ACTOR.FEAR <= 30 · **Visibility:** HIDE
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** What do you think of the way I lead?
- **Success:** Nobunogi: "I have an answer. Whether you like it is another matter."  
  Reaction `RECEPTIVE` · Next `RESPECT_COUNCIL` · Effects: none
- **Failure:** Nobunogi: "I have an answer. Whether you like it is another matter."  
  Reaction `RECEPTIVE` · Next `RESPECT_COUNCIL` · Effects: none

**4. Ask why she flinches at your orders** — `ALLY_FEAR`

- **Gate:** PARTNER_TO_ACTOR status == ALLY AND PARTNER_TO_ACTOR.FEAR >= 90 · **Visibility:** HIDE
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** You obey, but you go quiet when I speak.
- **Success:** Nobunogi: "Some things are easier to say when one is not afraid."  
  Reaction `RECEPTIVE` · Next `FEAR_COUNCIL` · Effects: none
- **Failure:** Nobunogi: "Some things are easier to say when one is not afraid."  
  Reaction `RECEPTIVE` · Next `FEAR_COUNCIL` · Effects: none

**5. Leave the matter for now** — `ALLY_LEAVE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** We will speak of this again.
- **Success:** Nobunogi: "See that we do."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "See that we do."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

### 08. A frightened ally (`FEAR_COUNCIL` · round 4)

**Nobunogi (introduction):** Nobunogi does not step back, but her grip tightens around the scabbard. "I can follow you. That is not the same as being at ease beside you."

**1. Promise she will not be punished for honesty** — `FEAR_RELENT`

- **Gate:** PARTNER_TO_ACTOR status == ALLY AND PARTNER_TO_ACTOR.FEAR >= 90 · **Visibility:** LOCK
- **Chance:** base 62% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** Say what you truly think. You have my word there will be no punishment.
- **Success:** Nobunogi: "Then hear this: I will fight beside you, but I will not become your shadow."  
  Reaction `RELIEVED` · Next `COMMON_GROUND` · Effects: PARTNER_TO_ACTOR.FEAR -25, PARTNER_TO_ACTOR.TRUST +10
- **Failure:** Nobunogi: "Words cost you nothing. I have learned to count deeds instead."  
  Reaction `WARY` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +7

**2. Tell her that obedience is enough** — `FEAR_PRESS`

- **Gate:** PARTNER_TO_ACTOR status == ALLY AND PARTNER_TO_ACTOR.FEAR >= 90 · **Visibility:** LOCK
- **Chance:** base 72% · MIGHT vs WILLPOWER (×0.4/×0.4) · profile `INTIMIDATION`
- **Law:** You need not like me. You need only obey.
- **Success:** Nobunogi: "As you wish. But do not mistake silence for peace."  
  Reaction `INTIMIDATED` · Next `COERCED` · Effects: PARTNER_TO_ACTOR.TRUST -15, PARTNER_TO_ACTOR.RESENTMENT +12
- **Failure:** Nobunogi: "Then you have made it impossible for us to stand together."  
  Reaction `ANGRY` · Next `REPRISAL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +20, PARTNER_TO_ACTOR status -> ENEMY

**3. Let the subject drop** — `FEAR_STOP`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** You have said enough.
- **Success:** Nobunogi: "I hope so."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "I hope so."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

### 09. An enemy at arm’s length (`TRUCE` · round 2)

**Nobunogi (introduction):** Her hand is near the hilt. "You have my attention. Do not waste it trying to pretend we are friends."

**1. Admit you were wrong** — `TRUCE_ADMIT`

- **Gate:** PARTNER_TO_ACTOR status == ENEMY · **Visibility:** LOCK
- **Chance:** base 63% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** I gave you reason to despise me. I know that.
- **Success:** Nobunogi: "Then let us try beginning with honesty, not friendship."  
  Reaction `RECEPTIVE` · Next `COMMON_GROUND` · Effects: PARTNER_TO_ACTOR.TRUST +8, PARTNER_TO_ACTOR status -> NEUTRAL
- **Failure:** Nobunogi: "A confession means little when you still expect a reward."  
  Reaction `COLD` · Next `REPRISAL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +8

**2. Suggest proving your intentions through action** — `TRUCE_TRIAL`

- **Gate:** PARTNER_TO_ACTOR status == ENEMY · **Visibility:** LOCK
- **Chance:** base 55% · WILLPOWER vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** Judge me by what I do next, not what I say now.
- **Success:** Nobunogi: "Then show me something worthy of judging."  
  Reaction `CONSIDERING` · Next `PROOF` · Effects: PARTNER_TO_ACTOR.RESPECT +5
- **Failure:** Nobunogi: "I already have. I was not impressed."  
  Reaction `DISMISSIVE` · Next `REPRISAL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +6

**3. Back away** — `TRUCE_END`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** Not today, then.
- **Success:** Nobunogi: "Not today."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "Not today."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

### 10. Words earned, not given (`RESPECT_COUNCIL` · round 4)

**Nobunogi (introduction):** For once, Nobunogi smiles without mockery. "You have fought hard enough to deserve the truth. Do not ask me to soften it."

**1. Ask whether she would follow you into danger** — `RESPECT_PROMISE`

- **Gate:** PARTNER_TO_ACTOR status == ALLY AND PARTNER_TO_ACTOR.RESPECT >= 75 AND PARTNER_TO_ACTOR.FEAR <= 30 · **Visibility:** LOCK
- **Chance:** base 80% · WILLPOWER vs WILLPOWER (×0.4/×0.4) · profile `COOPERATIVE`
- **Law:** If my judgement fails, will you still stand beside me?
- **Success:** Nobunogi: "I will challenge you first. Then, if you listen, I will stand beside you."  
  Reaction `WARM` · Next `WARNING` · Effects: PARTNER_TO_ACTOR.TRUST +10
- **Failure:** Nobunogi: "Do not ask me to excuse a foolish command."  
  Reaction `UNMOVED` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.RESPECT -5

**2. Invite her to dispute your battle plan** — `RESPECT_SHARE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 78% · INTELLECT vs INTELLECT (×0.4/×0.4) · profile `COOPERATIVE`
- **Law:** Tell me where I am wrong before we march.
- **Success:** Nobunogi: "Good. The first mistake is believing the strongest enemy will oblige your plan."  
  Reaction `IMPRESSED` · Next `WARNING` · Effects: PARTNER_TO_ACTOR.LOYALTY +9
- **Failure:** Nobunogi: "You wanted argument, but you were not ready to hear it."  
  Reaction `DISMISSIVE` · Next `COMMON_GROUND` · Effects: PARTNER_TO_ACTOR.TRUST -3

**3. Thank her for speaking plainly** — `RESPECT_PART`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** That is more useful than praise. Thank you.
- **Success:** Nobunogi: "Then make use of it."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "Then make use of it."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

### 11. An unfinished account (`FAREWELL` · round 5)

**Nobunogi (introduction):** She turns back to her training. "Whatever we are today, we will learn what that means when steel is drawn."

**1. Take your leave** — `FAREWELL_END`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** Until then.
- **Success:** Nobunogi: "Until then."  
  Reaction `RECEPTIVE` · Next `END` · Effects: none
- **Failure:** Nobunogi: "Until then."  
  Reaction `RECEPTIVE` · Next `END` · Effects: none

### 12. The oath that is not submission (`OATH` · round 3)

**Nobunogi (introduction):** She offers her forearm, not a bowed head. "Very well. I will fight beside you. I will not fight beneath you."

**1. Accept her terms** — `OATH_ACCEPT`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** That is precisely what I asked.
- **Success:** Nobunogi: "Then do not forget it."  
  Reaction `RECEPTIVE` · Next `ALLY_COUNCIL` · Effects: none
- **Failure:** Nobunogi: "Then do not forget it."  
  Reaction `RECEPTIVE` · Next `ALLY_COUNCIL` · Effects: none

**2. Ask about Mizuri** — `OATH_ASK_MORE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** Then tell me what you know of Mizuri.
- **Success:** Nobunogi: "I can tell you what I have heard. Whether it is true is another question."  
  Reaction `RECEPTIVE` · Next `WARNING` · Effects: none
- **Failure:** Nobunogi: "I can tell you what I have heard. Whether it is true is another question."  
  Reaction `RECEPTIVE` · Next `WARNING` · Effects: none

**3. Leave with your agreement** — `OATH_END`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** Then we have an agreement.
- **Success:** Nobunogi: "We have a beginning."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "We have a beginning."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

### 13. An ally made by force (`COERCED` · round 3)

**Nobunogi (introduction):** She nods once, with the stiff precision of someone counting the exits. "Call me your ally if it pleases you."

**1. Demand her opinion** — `COERCED_ASK`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** Then speak. What do you think?
- **Success:** Nobunogi: "I think the best place for a warrior is somewhere she may choose to stand."  
  Reaction `RECEPTIVE` · Next `FEAR_COUNCIL` · Effects: none
- **Failure:** Nobunogi: "I think the best place for a warrior is somewhere she may choose to stand."  
  Reaction `RECEPTIVE` · Next `FEAR_COUNCIL` · Effects: none

**2. End the exchange** — `COERCED_END`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** You have made your decision.
- **Success:** Nobunogi: "Have I?"  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "Have I?"  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

### 14. The line you crossed (`REPRISAL` · round 3)

**Nobunogi (introduction):** Nobunogi steps back and finally draws an inch of steel. "There. Now we understand each other."

**1. Withdraw the threat** — `REPRISAL_RETRACT`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 38% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** I went too far. Put the blade away.
- **Success:** Nobunogi: "You have one chance to show that you meant it."  
  Reaction `WARY` · Next `COMMON_GROUND` · Effects: PARTNER_TO_ACTOR.TRUST +3, PARTNER_TO_ACTOR status -> NEUTRAL
- **Failure:** Nobunogi: "Do not mistake my restraint for forgiveness."  
  Reaction `ANGRY` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.RESENTMENT +10

**2. Leave before it escalates** — `REPRISAL_WALK`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** Then this is where we stop.
- **Success:** Nobunogi: "Wise, for once."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "Wise, for once."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

### 15. What she knows of Mizuri (`WARNING` · round 4)

**Nobunogi (introduction):** She lowers her voice. "They say Mizuri has never needed to prove herself twice to the same challenger. I cannot tell you if that is true. I intend to find out."

**1. Ask her to continue** — `WARNING_LISTEN`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 70% · INTELLECT vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** What would you do if she were stronger than you?
- **Success:** Nobunogi: "Learn why. Then see whether that reason can be broken."  
  Reaction `REFLECTIVE` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.TRUST +5
- **Failure:** Nobunogi: "If you want certainty, ask a fortune teller."  
  Reaction `UNMOVED` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.RESPECT -2

**2. Say Mizuri will be easy to defeat** — `WARNING_BOAST`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 42% · CHARISMA vs WILLPOWER (×0.4/×0.4) · profile `STANDARD`
- **Law:** Then she will be disappointed when I meet her.
- **Success:** Nobunogi: "Confidence is a weapon. Keep its edge sharp."  
  Reaction `AMUSED` · Next `PROOF` · Effects: PARTNER_TO_ACTOR.RIVALRY +5
- **Failure:** Nobunogi: "That is not confidence. It is carelessness."  
  Reaction `UNIMPRESSED` · Next `FAREWELL` · Effects: PARTNER_TO_ACTOR.RESPECT -8

**3. Promise you will not underestimate her** — `WARNING_PROMISE`

- **Gate:** PARTNER_TO_ACTOR status == ALLY AND PARTNER_TO_ACTOR.RESPECT >= 70 · **Visibility:** HIDE
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** I will take her strength seriously. And yours.
- **Success:** Nobunogi: "Then perhaps we both live long enough to test her."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "Then perhaps we both live long enough to test her."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

**4. Thank her and go** — `WARNING_LEAVE`

- **Gate:** Always available · **Visibility:** LOCK
- **Chance:** base 100% · No skill check · profile `NEUTRAL`
- **Law:** That is enough to think about.
- **Success:** Nobunogi: "Then think quickly."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none
- **Failure:** Nobunogi: "Then think quickly."  
  Reaction `RECEPTIVE` · Next `FAREWELL` · Effects: none

## Validation summary

- 15 nodes, 50 authored choices.
- 11 hidden choices; 14 numeric gates, 18 status gates.
- 22 skill checks; 50 numeric effects, 7 status effects.
- Every node has at least one unconditional escape or progression choice, all node destinations resolve, and all nodes are reachable ignoring gates.
- No duel, event trigger or memory occurrence is authored; those features need their future systems.
