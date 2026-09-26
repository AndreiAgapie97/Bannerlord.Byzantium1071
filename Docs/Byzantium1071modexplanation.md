# Byzantium 1071 — Complete Mod Explanation

**Version:** 1.0.4.0
**Target Game:** Mount & Blade II: Bannerlord v1.5.3 beta (installed target; Warsails/NavalDLC v1.3.3 verified)<br>
**Mod ID:** `Byzantium1071`

---

## Table of Contents

1. [Overview — What is this mod?](#1-overview)
2. [Manpower Pools — The Core System](#2-manpower-pools)
3. [Pool Regeneration — How pools refill](#3-pool-regeneration)
4. [Volunteer Production — How troops appear for recruitment](#4-volunteer-production)
5. [Militia Link — How manpower affects garrison militia growth](#5-militia-link)
6. [Player Recruitment Gate — What you must pay to hire troops](#6-player-recruitment-gate)
7. [AI Recruitment Gate — The same rules apply to AI](#7-ai-recruitment-gate)
8. [Garrison Auto-Recruitment Cap — Garrison limits](#8-garrison-auto-recruitment)
9. [Castle Recruitment — Elite pools, prisoner conversion, and AI recruitment](#9-castle-recruitment)
9A. [Troop Service — Demobilization and soldier rotation](#9a-troop-service)
10. [War Effects — How wars drain pools](#10-war-effects)
11. [Delayed Recovery — Post-war recovery penalties](#11-delayed-recovery)
12. [War Exhaustion — The fatigue of prolonged conflict](#12-war-exhaustion)
13. [Diplomacy Pressure — How exhaustion shapes AI diplomacy](#13-diplomacy-pressure)
14. [Pressure Bands — Tiered war-weariness states](#14-pressure-bands)
15. [Forced Peace — When AI kingdoms are compelled to end wars](#15-forced-peace)
16. [Truce Enforcement — Post-peace cooldowns](#16-truce-enforcement)
17. [Combat Realism — Troop survivability and autoresolve armor](#17-combat-realism)
18. [Army Economics — Tier-exponential recruiting and wage costs](#18-army-economics)
19. [The Overlay — The in-game intelligence panel](#19-the-overlay)
19A. [Settlement Intelligence Tooltips](#19a-settlement-intelligence-tooltips)
20. [Configuration — MCM Settings Reference](#20-configuration)
21. [Mod Architecture Summary — For technical readers](#21-architecture)
22. [Verbose Debug Logging — rgl_log instrumentation](#22-verbose-debug-logging)
23. [Settings Migration — Version-gated defaults update](#23-settings-migration)
24. [Compatibility Notes](#24-compatibility)
25. [Known Limitations and Design Decisions](#25-limitations)
26. [Slave Economy — Raiding, enslavement, and market bonuses](#26-slave-economy)
27. [Minor Faction Economy — Frontier Revenue](#27-minor-faction-economy)
28. [Provincial Governance — Governance Strain](#28-provincial-governance)
29. [Frontier Devastation — Persistent raid damage](#29-frontier-devastation)
30. [Village Investment (Patronage) — Gold-sink village development](#30-village-investment)
31. [Town Investment (Civic Patronage) — Gold-sink town development](#31-town-investment)
32. [Mod Compatibility System — Runtime load-order scan and report](#32-mod-compatibility-system)
33. [AI Recovery Routing — Steering under-strength lords to Campaign++ recruits](#33-ai-recovery-routing)
34. [Settlement Revenue Tuning — Tapering tax, town tariffs, and village income](#34-settlement-revenue-tuning)

---

## 1. Overview

Byzantium 1071 is a **realism and strategic depth** mod for Bannerlord. It introduces a **manpower economy** that makes every recruitment decision meaningful, links war and diplomacy to population fatigue, slows down AI snowballing, and provides a rich intelligence overlay so you always know the state of the world.

The mod is designed to be **fully symmetric**: the same rules that limit *your* recruitment limit the AI's recruitment. No asymmetric player protection or difficulty scaling — just a realistic economy applied to everyone.

Everything is configurable via the Mod Configuration Menu (MCM). If a feature is not to your taste, almost every system can be individually toggled.

Localization uses Bannerlord's standard keyed `TextObject` workflow (`{=key}` + `SetTextVariable`) backed by language dictionaries in `_Module/ModuleData/Languages/` (English, Simplified Chinese, French, and German), so text follows the player's selected game language.

---

## 2. Manpower Pools

### What they are

Every town and castle in the game maintains a **manpower pool** — a finite reservoir of fighting-age adults willing to serve as troops. Villages contribute to their bound town/castle's pool through hearth (population) bonuses; they do not have independent pools.

### Pool size

A pool's maximum size is calculated from:
- **Base value**: configurable per settlement type (town, castle, "other")
- **Prosperity scaling**: richer settlements support larger pools (linear scale between min and max prosperity scale)
- **Security scaling**: safer settlements attract more volunteers (multiplied on top of prosperity)
- **Hearth contribution**: each bound village adds `hearth × multiplier` as flat bonus
- **Governor bonus**: a governor's Leadership skill increases max pool by up to +100%
- **Tiny pools mode**: optional testing override that divides all pools by a configurable divisor

### What the pool represents

The pool represents the *current available* manpower. Recruiting from it draws down the pool. Raiding, sieges, battles, and conquest drain it further. The pool regenerates daily to represent natural population recovery, birth cycles, and returning veterans.

### Where to see your pool

Open the overlay (default hotkey: **M**) → **Current** tab for the settlement you have selected. Or use the **Nearby**, **Castles**, or **Towns** tabs for a full ledger.

---

## 3. Pool Regeneration

### How it works

Each in-game day, every pool receives a daily regen calculated from many factors applied as a chain of multipliers:

| Stage | Factor |
|-------|--------|
| Base rate | Settlement type + prosperity interpolation |
| Hearth bonus | Adds a flat percentage based on village hearths |
| Security modifier | Low security → slower regen |
| Food modifier | Starvation → much slower regen |
| Loyalty modifier | Disloyal towns → fewer people volunteer |
| Siege penalty | Besieged settlements regen much less |
| Seasonal modifier | Spring/Summer: bonus; Winter: penalty (if enabled) |
| Peace dividend | Kingdom at peace → regen multiplier (if enabled) |
| Governor bonus | Town governor's Steward skill adds flat regen |
| War exhaustion penalty | Severely exhausted kingdoms → regen penalty |
| Delayed recovery | Post-war recovery penalty decays over time |
| Soft cap | As pool approaches full, regen slows (prevents instant refill) |
| Stochastic variance | Optional random variance ±N% per day |
| Stress floor | Final safety: regen never falls below a minimum threshold |

The result is capped to a configurable maximum percentage-of-pool per day to prevent absurd refill rates.

### Castle minimum regen floor

Castles did not generate manpower through births — garrisons were rotated from towns by the regional commander (*strategos* or *doux*). The castle's own civilian community (~50–500 people) produced almost no military-age males. Castles use a separate minimum regen floor (`CastleMinimumDailyRegen`, default: 1) instead of the global `MinimumDailyRegen` (1). This represents slow peasant levies from bound villages — the castle's tiny rural hinterland providing a trickle of recruits.

### Castle supply chain (v0.1.8.3)

The full daily regen value is still computed for castles via the standard formula (`GetDailyRegen`), but when `EnableCastleSupplyChain` is enabled (default: true), only the **local trickle** (capped at `CastleMinimumDailyRegen`, 1/day) is created organically. Everything above that trickle is **transferred from the nearest same-faction town's pool** — draining the town rather than materialising manpower from nothing.

**Algorithm:**
1. Compute `regen` via the standard regen formula (hearths, prosperity, governor, exhaustion, etc.)
2. `localTrickle = min(regen, CastleMinimumDailyRegen)` — free, represents peasant levy from bound villages
3. `supplyRequest = regen - localTrickle` — the amount the castle needs from a town
4. Find the nearest town belonging to the same faction (by map distance)
5. `supplyTransfer = min(supplyRequest, supplyTown.currentMP)` — castle takes what it can
6. If **no** same-faction town exists anywhere the castle is *cut off*: `villageLevy = supplyRequest × CastleVillageLevyPercent%` (default 100), and the result is floored at `CastleCutOffDailyRegen` (default 4) — see below
7. Castle receives `localTrickle + supplyTransfer` when supplied, or `max(CastleCutOffDailyRegen, localTrickle + villageLevy)` when cut off; supply town loses `supplyTransfer`

   The town lookup runs every tick rather than only when `supplyRequest > 0`. Cut-off status must be known on days the castle requests nothing, since the floor applies regardless, and a `null` lookup *is* the definition of cut off.

**Edge cases:**
- **No same-faction town exists** (a realm made only of castles): the castle raises a levy from its own bound villages, taking `CastleVillageLevyPercent` (default 100) of `supplyRequest`, and the day's total is floored at `CastleCutOffDailyRegen` (default 4). Fixed in v1.0.2.6 — previously such a castle received only the trickle (1/day) *forever*, which starved it permanently.

  `FindNearestSameFactionTown` scans `Town.AllTowns` with no distance cutoff, so a `null` return means the faction owns **zero** towns, not that the nearest one is far. The levy is not manpower from nowhere: `GetDailyRegen` has already weighted `regen` by the bound villages' hearths, so a share of it is a village-derived figure. A castle with no bound villages still gets the floor, which represents the castle's own community rather than its hinterland. The normal supply chain resumes the moment the faction takes a town.

  > **Why the levy alone was not enough, and why the default is 100 rather than 50.** `supplyRequest` is `regen - localTrickle`, and for a castle `regen` is almost always exactly the floor: measured across all 67 vanilla castles with shipped defaults, *every one* produces a percentage-based regen that truncates to zero, so `regen` comes entirely from `max(minDailyRegen, 0)` and `supplyRequest` is zero. The single exception is a pool below `DepletedRegenThresholdPercent` (15%), where `GetDailyRegen` adds `DepletedRegenBonusAtZero` (+2/day, tapering) *after* the floor. That bonus therefore lands in `supplyRequest` — and for a cut-off castle it was being discarded, since no town existed to honour the request. The practical effect was that a cut-off castle was denied the emergency recovery every other settlement receives: 1/day against the 3/day the same castle would get inside a kingdom that owns towns. A levy of 100% recovers that bonus in full and is a bug fix rather than a buff; 50% would have restored only half of what the emergency system already grants everywhere else.

  > **Why a separate floor was still required.** Because the levy only ever operates on the emergency band, it stops contributing once the pool passes 15% fill, after which the castle is back on 1/day. Against a median castle pool of 221 that is roughly four days saved out of a two-hundred-day recovery — the leak is fixed but the castle is still uninhabitable as a base of operations. `CastleCutOffDailyRegen` addresses the underlying rate: 4/day refills that pool in ~55 days, which makes an independence start viable without touching any established kingdom. The floor is applied in the daily tick (after `GetDailyRegen`) rather than inside the regen formula, so the soft cap, hard cap and stress floor do not erode it; the pool maximum still bounds it via `newCur = min(max, cur + actualRegen)`.

  > **Why this cannot inflate the AI kingdoms.** The branch is gated on the faction owning zero towns. All six starting kingdoms own towns, so none of the game's 67 castles reaches it at campaign start, and a kingdom only reaches it after being reduced to castles alone — at which point a faster rebuild is the intended behaviour, not an exploit. Simulating the shipped formulas across every settlement: a typical kingdom holds ~10,000 manpower across ~7 towns and ~8 castles and regains ~25/day, and this change leaves both figures untouched.

  This is the exact position of a player who declares independence holding a single castle: before the fix their lords could never refill their parties while every established kingdom recruited normally. Same edge-case class as the v1.0.2.4 slave-economy fix — a path that assumed every faction owns a town.
- **Supply town is depleted** (pool at 0): castle receives only the local trickle.
- **Multiple castles share one supply town**: towns near many castles drain faster. This is intentional — historically, frontier provinces with dense castle networks placed heavy demands on their administrative capital.
- **Castle already at max**: the early-exit `cur >= max` check fires before the supply chain code. No transfer occurs.
- **Toggle off** (`EnableCastleSupplyChain = false`): reverts to legacy behavior where castles regen independently.

**Strategic implication:** Raiding a town now starves its dependent castles. Players must defend their towns to keep frontier castles garrisoned.

### Depleted emergency regen

When a pool drops below a configurable threshold (`DepletedRegenThresholdPercent`, default: 15% of max), an additive flat bonus is applied that scales inversely with fill ratio:

- At 0% fill: +`DepletedRegenBonusAtZero` (default 2) per day
- At 7.5% fill: +1/day
- At threshold (15%): +0 (normal regen takes over)

This models limited Crown frontier investment. Historically, devastated frontier provinces (e.g., post-Manzikert eastern Anatolia) took decades to recover — refugees fled *away* from the frontier, not toward it. The emergency bonus intentionally bypasses the normal hard cap and can be toggled via `EnableDepletedEmergencyRegen`.

**Combined impact:** A castle at 0% with a healthy supply town regens ~3/day (1 local trickle + 2 emergency, both drawn from the supply town above the trickle). Without a supply town, ~3/day (1 trickle + 2 emergency from the depleted bonus). At 7.5% fill, ~2/day. At 15%+, reverts to normal formula (≥1).

### Regen is cached per day

The heavy regen calculation is cached once per in-game day to avoid recalculating the same formula thousands of times (volunteers, garrison patch, overlay, militia all read from it). This keeps the daily tick performant.

---

## 4. Volunteer Production

Volunteers are the troops that appear at notables' recruitment lists. The mod overrides the daily probability that a new tier-N troop slot appears:

```
modifiedProbability = base_vanilla_probability × manpowerRatio
```

Where `manpowerRatio = currentPool / maxPool` (clamped to [0.0, 1.0]).

**Effect**: A pool at 100% fills volunteer slots just as fast as vanilla. A pool at 50% fills them at half speed. An empty pool produces no new volunteers.

Optional **stochastic variance** (WP4) multiplies the result by a random factor of `[1 - spread, 1 + spread]` where `spread = VolunteerVariancePercent / 100`. This adds realistic day-to-day noise — some days more volunteers appear, some days fewer. The spread is clamped to 100% maximum to prevent negative probabilities.

---

## 5. Militia Link

The mod overrides the settlement militia growth model. In addition to vanilla militia growth factors, it applies a manpower-ratio scale:

```
additionalFactor = lerp(MinScale, MaxScale, manpowerRatio) - 1
```

A settlement with full manpower grows militia at `MaxScale × vanilla` rate. An empty settlement grows militia at `MinScale × vanilla` rate. If the computed factor is negligible (< 0.001), no modification is applied.

This means settlements drained of manpower by war also see weakened militia — a city that lost its fighting-age population cannot readily defend itself.

---

## 6. Player Recruitment Gate

When you open the recruitment menu at a settlement, the mod intercepts your hiring actions:

### Settlement-type tier gate
Before manpower is even checked, the mod resolves the source settlement type of the volunteer board:
- Villages use `VillageVolunteerTierMax` (default T2)
- Towns use `TownVolunteerTierMax` (default T4)
- Castles are ignored here because castle recruitment is handled by the separate castle recruitment system

If a troop is above the relevant cap, the volunteer roster is sanitized back down to a legal troop. This prevents over-cap troops from occupying notable slots forever. The recruit-time gates still remain as a defensive fallback for injected or stale UI entries.

**Why sanitizing must always succeed:** vanilla's `UpdateVolunteersOfNotablesInSettlement` only ever *upgrades* a non-null volunteer slot — it never clears or downgrades one. So an over-cap troop that the recruit-time gate refuses can never leave the board on its own. If the sanitizer gives up on a slot, that slot is dead permanently and the settlement slowly fills with volunteers nobody can recruit.

**Resolution order (v1.0.2.4).** `FindHighestAllowedAncestor` walks *forward* from `culture.BasicTroop` / `culture.EliteBasicTroop`, so it only sees troops on a path from the culture roots. Troop overhaul mods routinely ship branches that are not rooted there — De Re Militari has branches running T4→T6, for instance — and the forward search can never reach them. `FindFallbackVolunteer` therefore continues:

1. **Reverse parent index.** A child→parent index built over `CharacterObject.All` lets the sanitizer walk *up* from the over-cap troop, reaching branches disconnected from the culture roots. The index is cached per session and reset at session launch, so a different module set never reuses a stale tree.
2. **The troop's own culture root**, when it is itself within the cap.
3. **The settlement's culture root** — covers troops whose `Culture` is not the settlement's, which some overhauls do by parking troops on shared or neutral cultures.
4. **Clear the slot.** Safe and self-healing: vanilla refills any null slot with `GetBasicVolunteer(notable)` on its next daily roll.

Existing saves recover on load, since the sanitizer already runs across all settlements at session launch. **Rebuild Recruitment Sources** in the Compatibility tab forces the same pass mid-session.

### Tavern mercenaries (exempt)
Hiring mercenaries from a tavern is exempt from the manpower pool entirely. They are wandering soldiers being hired, not levies raised from the local population, so they neither consume the pool nor are blocked by it.

This is checked in `B1071_AiRecruitmentManpowerGatePatch` **before** the manpower gate, on `RecruitingDetail.MercenaryFromTavern`. Gating them made the tavern hire dialogue silently no-op — no gold taken, no troops added — once a town's pool ran dry, which is exactly what a long war does. It also removes an inconsistency: the `town_backstreet` "Recruit N mercenaries" menu option bypasses `ApplyInternal` and was never gated at all.

Because `ApplyInternal` fires `OnTroopRecruited` on its way out, the patch also raises an `IsProcessingTavernMercenary` flag so `B1071_ManpowerBehavior` skips *consumption* for AI parties too — otherwise the pool would still be drained by the very hires just declared exempt. A Harmony `Finalizer` clears the flag even if `ApplyInternal` throws, so it can never leak into the next recruitment. The tier gate already exempted this path implicitly, since tavern hires pass `individual == null`.

### War gate (v1.0.2.6)
`B1071_RecruitmentTierGateHelper.IsBlockedByWar` refuses recruitment outright in any settlement belonging to a faction the recruiter's realm is at war with. It runs ahead of both the tier gate and the manpower gate in all three player paths (single recruit, Recruit All, and the Done confirmation) and in the AI path.

In practice this closes the enemy **village** route — hostile towns and castles cannot be entered at all. Before the fix, touring enemy villages was free manpower: the player refilled from the very pools their war was draining while their own settlements stayed untouched.

Exempt only when the clan has **no kingdom and no fief** — the early-game player, for whom every realm at war would otherwise be closed. A landless **vassal or mercenary is not exempt**: swearing to a kingdom makes that kingdom your realm and its wars your wars. Exempting them would have left the gate trivially bypassable by simply never accepting a fief. Tavern mercenaries return before this check and are unaffected. AI lords are gated on the same terms but silently — they have no UI to message. Toggle: `Block recruiting in enemy realms` (default ON).

This is the **access** half of the recruiting-abroad gate; the gold premium in §18 is the price half, for realms merely at peace. Manpower cost is left alone in both — see §18 for why.

### Per-troop gate (single recruit)
Before each troop hire, the mod checks:
- How much manpower the pool has
- What that troop costs (flat `BaseManpowerCostPerTroop`, default 1 per troop)
- Whether the pool has enough to cover the cost

If not, a yellow message shows and the hire is blocked.

### Recruit-all gate
Before the entire "Recruit All" action, the mod checks the full sequence of all troops in the recruitment list. If any troop in sequence would exceed available manpower, the whole batch is blocked.

### Cart (confirm/done) gate
When you confirm a batch of troops you put in the "cart", the same sequence check is run again against what you've accumulated.

### UI feedback
- Over-cap troops should normally not appear on the board at all; the roster is sanitized before daily use and before the recruitment VM refreshes
- Unaffordable troops show greyed-out (their `CanBeRecruited` flag is set false by AND-ing with vanilla's gate — never re-enabling what vanilla disabled for other reasons)
- The Recruit All button is disabled if the tier cap or manpower sequence would fail (AND-ed with vanilla's gate)
- The Done button shows a tooltip with the blocker troop and either tier-cap or manpower details

### Culture discount
If your party leader shares culture with the recruitment settlement, a configurable discount (CultureCostPercent) applies.

---

## 7. AI Recruitment Gate

The mod patches the AI recruitment pipeline (`RecruitmentCampaignBehavior.ApplyInternal`, a private internal method). Before AI parties recruit at a settlement, the same settlement-type tier-cap check and `CanRecruitCountForPlayer` manpower check are applied.

If the recruit is blocked:
- **Player context**: yellow on-screen message
- **AI**: debug log (visible when AI logging is enabled in MCM)

The AI sees the same resource constraints you do. A kingdom that has fought many battles and drained its pools will struggle to recruit new troops, and villages/towns will stop feeding it troops above their configured cap — a natural attrition mechanic.

---

## 8. Garrison Auto-Recruitment Cap

The vanilla game automatically recruits troops into town garrisons daily via `DefaultSettlementGarrisonModel.GetMaximumDailyAutoRecruitmentCount`. This mod wraps that:

- If manpower is zero → garrison auto-recruitment is blocked entirely
- Otherwise → auto-recruitment is capped to `min(vanilla_cap, available_manpower)`

This ensures garrison auto-fill also draws from (and is limited by) the manpower pool, preventing garrisons from growing unchallenged even as the settlement's population is depleted.

---

## 9. Castle Recruitment

### What it does

Castles have their own dedicated recruitment system, separate from the normal village-notable volunteer system. This system provides **three sources of troops** at every castle:

1. **Elite Troop Pool** (culture-based)
2. **Converted Prisoners** (ready for recruitment)
3. **Pending Prisoners** (still earning conformity)

Players access this via the castle game menu option "🏰 Recruit troops". AI lords recruit automatically during their daily visit.

### Source 1: Elite Troop Pool

Each castle generates T4/T5/T6 troops matching the settlement's culture (or T2–T6 with diversified pool enabled). The elite pool:

- Regenerates daily from the castle's manpower pool (1–2 troops/day based on prosperity)
- Each regenerated troop costs `CastleEliteManpowerCost` (default: 1) manpower
- Is capped per castle by `CastleElitePoolMax` (or dynamic formula when `EnableDynamicPoolCapacity` is ON)
- Consists of a random distribution of culture-specific troop types from both the basic and noble troop trees, plus any additional troop roots currently present on same-culture volunteer boards
- Access can be restricted via `CastleRecruitmentAccess`: 0 = open, 1 = same faction only (default), 2 = owner clan + ruling clan only — applied identically to player and AI

#### Diversified Pool (optional)

When `EnableDiversifiedCastlePool` is ON, the pool regenerates a weighted mix of tiers instead of only T4–T6:
- **T2 Levy** (default 45%) — basic infantry/ranged of the castle's culture
- **T2 Noble** (default 35%) — noble-line T2 troops
- **T3–T4** (default 15%) — mid-tier troops
- **T5+** (default 5%) — elite and champion troops

#### Dynamic Pool Capacity (optional)

When `EnableDynamicPoolCapacity` is ON, pool cap = `CastleElitePoolBaseCapDynamic` + (`prosperity` × `CastleElitePoolProsperityScaling`) + (`wallLevel` × `CastleElitePoolWallBonus`). Defaults: 5 + 0.005×prosperity + 3×walls.

#### Recruitment Source Rebuild (v1.0.0.1)

Because volunteer-board troop trees can be altered by other mods after a save already exists, Campaign++ now exposes a manual **Rebuild Recruitment Sources** action in the Compatibility tab. It re-sanitizes volunteer boards, clears the castle culture caches, and re-discovers supplemental troop roots from live same-culture settlements without wiping existing elite-pool stock.

### Source 2: Converted Prisoners

Prisoners above `CastlePrisonerAutoEnslaveTierMax` earn conformity from one shared castle budget each daily settlement tick, after low-tier processing and before AI recruitment and garrison absorption.

- Daily budget: `24 × (10 + 0.05 × governor Leadership)` = `240 + 1.2 × Leadership`. Without a governor, Leadership is zero. Party recruitment perks are deliberately excluded.
- Requirement: `CharacterObject.ConformityNeededToRecruitPrisoner`, from the active game model; vanilla uses `(level + 6)² − 10`.
- Points live in the native prison roster's XP, already persisted by Bannerlord. Read them directly from the roster because the cached troop-list copies can contain stale XP.
- A stable ordinal troop-ID rotation distributes points equally among unfinished types, skipping fully ready types. A fifth-of-a-point remainder and next rotation index persist as three parallel lists (`b1071_cr_conformityCastles`, `b1071_cr_conformityRemainders`, `b1071_cr_conformityCursors`). Unspent budget and fractional remainder are discarded when every type is ready or the eligible roster is empty.
- Ready count is `min(prisoner count, points / requirement)`. Pending count is the remainder of the prisoner count. More arrivals do not multiply the daily budget or mark the whole type ready.
- Each player recruit, AI recruit, and garrison absorption removes one prisoner and one requirement of native XP. Gold distribution, FIFO depositor records and zero-manpower prisoner recruitment remain unchanged.
- AI castle deposits use `TransferCastlePrisoners` to carry native conformity. As with native partial deposits, the source retains points up to its remaining prisoners' capacity; excess points move with the deposit. The source XP is explicitly debited because native count removal alone does not reliably clamp it. Full deposits carry all valid points; wounded counts are preserved.
- `B1071_CastlePrisonerWithdrawalPatch` and its command-validation companion restrict native dungeon withdrawals for eligible troop types with any outstanding player recruitment cost. Otherwise, native withdrawal would carry conformity into the party and allow recruitment without paying the depositor. Both the transferability check and actual command count are guarded; a mixed type remains protected even when its first FIFO entry is free. Checks use the original castle count reconstructed from the native screen's net transfer history. Negative history permits undoing only the newly deposited count. No gold or FIFO state changes inside the reversible screen; ordinary free transfers, deposits and cancellation remain native.
- On session launch, old per-type day counters are consumed once: completed timers credit the existing stack's full requirement; incomplete timers credit their fraction toward one recruit. Preserve greater existing native XP, cap to roster capacity, and clear legacy timers so save/reload cannot regrant migration credit. Missing troops receive nothing.

| Tier | Gold Cost (default) |
|------|---------------------|
| T4   | 1,200g              |
| T5   | 2,500g              |
| T6+  | 5,000g              |

### Source 3: Pending Prisoners

Prisoners without sufficient conformity remain visible in the recruitment screen. The same type can appear in both lists with separate counts. The pending column displays `points toward next recruit / requirement`. Its ownership tooltip explicitly describes the whole troop type; the next fee still follows the actual FIFO order, including prisoners already ready. No fixed completion date is promised because the number of pending types and governor Leadership can change.

### Low-Tier Prisoner Auto-Enslavement

T1–T3 prisoners at castles are automatically enslaved to the nearest town's slave market each day (requires Slave Economy enabled). This keeps the prison roster clean and feeds into the town-level slave economy system.

**Enslavement income:** The castle owner receives the dynamic slave market price per enslaved prisoner. The price is obtained from `nearestTown.Town.GetItemPrice(_slaveItem)` — the same town that receives the slave goods. Income is distributed through `DistributeIncome`:
- **Same-clan / untracked prisoners:** 100% goes to the castle owner.
- **Cross-clan depositor:** Split by the holding fee — depositor receives (100 − fee)%, owner receives fee%.
- **Hostile depositor:** 100% goes to the castle owner (wartime forfeit).

**Affordability gate:** Processing is per-unit. Before each enslavement, the nearest town's gold is checked against the slave price. If the town cannot afford the slave, ALL remaining enslavement stops for that day — prisoners stay in the dungeon. Towns with low gold may take several in-game days to process a large batch.

**Roguery XP parity (v1.0.0.1):** Enslavement now grants the same Roguery XP Bannerlord awards for prisoner sales. Immediate town enslavement uses `SkillLevelingManager.OnPrisonerSell` whenever the original party still exists. Delayed castle processing stores the original depositor hero and party in a dedicated FIFO queue, then falls back to `Hero.AddSkillXp` with the exact vanilla-equivalent tier-sum formula if that party is gone.

**Notifications (v1.0.0.1):**
- **Owner income:** When the player's own castle enslaves prisoners, a green notification shows: "[Castle] enslaved X prisoners — sold to [Town], treasury income +Yg."
- **Depositor income:** When a cross-clan castle processes the player's deposited prisoners, a blue notification shows the consignment share.
- **Deposit confirmation:** When the player deposits prisoners at their own castle, a confirmation message appears: "Deposited X prisoners at [Castle]. Low-tier will be auto-enslaved; elites held for conversion."

**Verbose logging:** Each castle's daily enslavement is logged with: candidate count, processed count, target town, slave price, town gold remaining, and a TOWN BROKE flag when the affordability gate halts processing.

**Availability gate (v1.0.2.4):** `IsLowTierEnslavementAvailable(castle)` is the single source of truth for whether the enslavement pipeline can run at a given castle. It requires two *permanent* conditions: the Slave Economy is enabled, and `FindNearestTown` resolves a same-faction town to sell to. It deliberately does **not** test the slave price — a broke town, or a price that dipped after a large sale, is a temporary state the affordability gate above already handles by retrying tomorrow.

**Stranded-prisoner safety net (v1.0.2.4):** `DrainStrandedLowTierPrisoners` runs immediately after `AutoEnslaveLowTierPrisoners` in the daily tick. When `IsLowTierEnslavementAvailable` returns false, T1–T3 prisoners have no exit from a castle dungeon at all — `AdvancePrisonerConformity` skips them, `IsReadyForRecruitment` refuses them, and `B1071_CastlePrisonerRetentionPatch` blocks vanilla's daily sale — so the dungeon saturates at `PrisonerSizeLimit` and every deposit route refuses on `room <= 0`, killing castle recruitment at that settlement. The safety net restores vanilla's suppressed sale for exactly those prisoners:
- Same call as vanilla: `SellPrisonersAction.ApplyForSelectedPrisoners(settlement.Party, null, roster)` — null buyer, ransom into the castle treasury.
- Same rate as vanilla: `MBRandom.RoundRandomized(count * 0.1f)`, which floors and then adds 1 with probability equal to the fraction, so even a single stranded prisoner drains eventually without needing an artificial floor.
- Applied to the **stranded low-tier subset only**, not `TotalRegulars` — T4+ prisoners awaiting conversion are never touched, making this strictly gentler than the vanilla call being suppressed.
- Depositor and Roguery-XP FIFO entries for the released prisoners are consumed, since the enslavement path that would normally drain them is exactly what is missing.
- **Player-owned castles are excluded.** Vanilla sells only the overflow above `PrisonerSizeLimit` at player settlements; below the cap the player escorts prisoners to a town and ransoms them there. That route is unaffected by the Slave Economy setting, so there is nothing to restore and auto-ransoming the player's prisoners would remove a vanilla choice.

### Access Rules

| Actor | Recruitment | Deposit |
|-------|-------------|---------|
| Player | Own + Allied + Neutral | Own (Manage) + Same-faction (vanilla Donate) + Neutral (our menu option) |
| AI lords | Same-faction + Neutral | Same-faction + Neutral |
| Garrison | Same castle's prisoners (auto-absorption) | N/A |
| Hostile | Blocked | Blocked |

### AI Castle Recruitment

AI lord parties currently at a non-hostile castle (same-faction or neutral) auto-recruit from **both** the elite pool and converted prisoners during the daily tick. Hostile parties are skipped:

- Lords recruit up to their party size limit (no daily cap)
- **Same-clan lords recruit elite-pool troops for 50% gold cost** — lords whose clan matches the castle's `OwnerClan` pay half price as a recruitment expense, including when the recruiter owns the castle. The payment leaves circulation rather than returning to the household
- **Cross-clan lords pay gold** routed to the castle owner via `GiveGoldAction.ApplyBetweenCharacters(party.LeaderHero, settlement.Owner, cost)`
- **Per-unit processing (v0.1.7.1):** AI prisoner recruitment processes one unit at a time, peeking the current depositor and checking affordability before each recruit. This ensures correct depositor attribution when a troop type has mixed depositors in the FIFO queue
- **Treasury reserve (v1.0.2.9):** every gold decision on this path runs through `GetAiBufferedAffordableCount(gold, costPerUnit, maxUnits)`, which allows a batch of `n` only while `gold > n x costPerUnit x CastleAiGoldBufferMultiplier` (default **3**). It is `CanAiAfford` generalised from one hero's fee to a batch, so what a lord keeps back grows with the wage bill he is taking on instead of sitting at a flat floor. The elite pool and the prisoner loop share it. Set the multiplier to 1 for the old spend-to-the-last-coin behaviour.
- Elite recruitment drains manpower (if `CastleRecruitDrainsManpower` is enabled, default: yes). **Since v1.0.2.9 the AI reads the same two manpower values the player does:** the affordability gate is `GetRecruitCostForParty`, which carries the culture discount, and the charge is `GetManpowerChargePerTroop`, which does not. Previously the AI used the flat base cost for both, so a lord recruiting at a castle of his own culture was gated harder than the player standing beside him
- Prisoner recruitment costs **zero manpower**
- After modifying a party's roster, `SetMoveGoToSettlement` + `RecalculateShortTermBehavior` is called to re-anchor the party at the castle, preventing the "flickering" bug where roster changes invalidate the AI's cached settlement target

### Player Castle Recruitment

The 820-unit-wide recruitment popup sizes itself before movie loading to `min(1000, logicalViewportHeight - 48)`. After reserving 318 units for headers, dividers and padding, it divides whole 29-unit row slots across the three independent scroll areas (8 levy, 8 ready and 7 pending at the maximum height). Empty placeholders reserve the same space as their filled section. Recruiting does not resize the window; pricing and batch actions are unchanged.

Player recruitment follows the same clan-based rules:

- **Recruiting elite-pool troops from own clan's castle:** 50% gold discount, with the discounted amount spent as a recruitment expense
- **Recruiting from another clan's castle:** Full gold cost, paid to the castle owner
- Elite payments use `PayEliteRecruitment` on both player and AI paths: `GiveGoldAction.ApplyBetweenCharacters` receives a null recipient for same-clan expenses and the castle owner for cross-clan transfers
- The recruit message shows effective cost (50% for same-clan elites, full cost for cross-clan)
- **Batch recruit:** Each section (Elite and Ready) has a "Recruit All" button that loops through all available troops using snapshot iteration (avoids collection modification during loop). Single `RefreshLists()` call at the end for efficiency.

### Player Prisoner Deposit

Players can deposit prisoners at castles for processing through the consignment model:

- **Own castle:** Use vanilla's "Manage prisoners" option (no consignment split — you already own the castle). A deposit confirmation is shown: "Deposited X prisoners at [Castle]."
- **Same-faction castle:** Use vanilla's "Donate prisoners" option in the dungeon menu. Our `OnPrisonerDonatedToSettlement` event hook records depositor tracking automatically.
- **Neutral castle:** Vanilla's "Donate prisoners" doesn't show at neutral castles (requires same-faction). We add a custom dungeon menu option "⚔️ Deposit prisoners" that appears only at neutral castles. Uses the same vanilla `PartyScreenHelper.OpenScreenAsDonatePrisoners()` API — identical party screen and done handler.
- **Hostile castle:** Blocked (player cannot access hostile castles)

The deposit menu option shows the prisoner count and holding fee: "⚔️ Deposit prisoners (12 available, 30% holding fee)". It is disabled with a tooltip when the player has no prisoners or the prison is full.

### Garrison Prisoner Absorption

Each day, the garrison at a castle can absorb 1 ready prisoner directly into its ranks:

- Only fires if garrison auto-recruitment is enabled (vanilla toggle) and food change is positive
- Respects the garrison size limit (`PartySizeLimit` or `CalculateGarrisonPartySizeLimit`)
- Creates a garrison party if one doesn't exist yet
- Costs zero manpower (prisoner-sourced, not population-sourced)
- The absorption rate is hardcoded to 1/day (matching the vanilla `DefaultSettlementGarrisonModel.GetMaximumDailyAutoRecruitmentCount` constant) and intentionally bypasses the B1071 manpower postfix that caps regular garrison auto-recruitment — since prisoner absorption is population-free, it should not be gated by manpower
- **Affordability gate (v0.1.7.1):** Processes prisoners one at a time. Before absorbing each prisoner, `GetGarrisonAbsorptionCost` pre-checks what the castle owner would owe the depositor. If the owner cannot afford the depositor's share, that prisoner is skipped entirely — it remains in the dungeon until the owner has enough gold. Untracked, same-clan, and dead-depositor prisoners cost the owner nothing and are always absorbed.

### Castle Economy — Consignment Model

The castle recruitment system uses a **consignment model** for prisoner income. When a lord deposits prisoners at another clan's castle, the depositor retains economic interest in those prisoners. Income is split between the depositor and castle owner when prisoners are processed.

**Depositor tracking:** The behavior maintains `_depositorTracking` — a per-castle, per-troop-type, per-depositor FIFO list recording who deposited which prisoners and how many. When prisoners are consumed (enslaved, recruited, or absorbed by garrison), the system looks up the depositor(s) and distributes income accordingly.

**Channel 1: Enslavement income (T1–T3)**
- Daily auto-enslavement converts low-tier prisoners to slave goods at the nearest town
- **The nearest town buys the slaves** — income is paid from the town's trade treasury via `GiveGoldAction.ApplyForSettlementToCharacter(nearestTown, recipient, amount)`. No gold is created from nothing; the town pays for the labor it receives.
- **Affordability gate (v0.1.7.1):** Prisoners are processed one unit at a time. If the town's gold runs out, remaining prisoners stay in the castle dungeon until the next day. Town wealth naturally throttles enslavement volume.
- Income is split per depositor entry:
  - Cross-clan depositor: depositor gets (100% - holding fee), castle owner gets holding fee
  - Same-clan depositor: castle owner gets 100% (family)
  - Untracked prisoners (siege conquest, pre-tracking saves): castle owner gets 100%
  - Dead/unavailable depositor: castle owner gets 100%
- Holding fee default: 30% (MCM configurable 5–50%)

**Channel 2: Recruitment fees (3-party independent clan-waiver)**
- When a lord recruits converted prisoners, gold flows through a 3-party model:
  - Each party (depositor and castle owner) independently determines whether their share is waived based on clan relationship with the recruiter
  - Recruiter same-clan as depositor: depositor's share waived
  - Recruiter same-clan as castle owner: owner's share waived
  - Both same-clan: free
  - No depositor: legacy 2-party rules (same-clan = free, cross-clan = owner gets 100%)
- **Gold transfer mechanism (v0.1.7.1):** All recruitment payments use `GiveGoldAction.ApplyBetweenCharacters(recruiter, recipient, amount)` — two direct transfers (recruiter→depositor, recruiter→owner) with proper clamping and campaign event integration. No gold is created from nothing and no gold is silently destroyed. If the castle owner is null (theoretical edge case), the owner's share is simply skipped rather than destroying gold via `ChangeHeroGold`.

**Channel 3: Garrison absorption compensation**
- When the garrison absorbs a cross-clan depositor's prisoner, the castle owner pays the depositor their share from the owner's own gold via `GiveGoldAction.ApplyBetweenCharacters(owner, depositor, share)`
- **Affordability gate (v0.1.7.1):** If the owner cannot afford the depositor's share, the prisoner is **not absorbed** — it stays in prison. This prevents the owner from getting free troops at the depositor's expense
- Same-clan depositor prisoners are absorbed for free (no compensation needed)

**Elite pool recruitment** is outside the depositor economy — elite troops are castle-generated, so there is no depositor to compensate. Same-clan recruitment spends 50% of the tier price through a null-recipient `GiveGoldAction`; cross-clan recruitment pays the full price to the castle owner. Paying the owner in both cases previously refunded an owner recruiting from their own castle and recycled clan-member payments within the household. `PayEliteRecruitment` now routes both player singles and AI batches consistently. Per-unit rounding, affordability gates, manpower charges and AI treasury reserves are unchanged. The existing missing-owner fallback for cross-clan transfers is preserved. Prisoner consignment fees and waivers are unaffected; no saved state or settings migration is needed.

**Net effect:** Both the capturing lord and the castle owner benefit from the prisoner pipeline. A lord who captures 50 prisoners and deposits them at an allied castle receives 70% of all enslavement and recruitment income, even though they moved on to fight elsewhere. The castle owner earns a 30% commission for housing, processing, and providing the infrastructure.

**Player deposit parity:** The behavior listens to `CampaignEvents.OnPrisonerDonatedToSettlementEvent` — the campaign event vanilla fires when the player uses the dungeon menu's "Donate prisoners" party screen. When the player deposits prisoners at another clan's castle, depositor tracking is established via `RecordDeposit`, giving the player their consignment share of all future income from those prisoners. Own-clan castles are skipped (same-clan economics make tracking unnecessary). An info message notifies the player of the consignment terms.

**Same-clan delayed XP tracking:** Own-clan deposits still bypass economic FIFO, but they are now tracked in a separate delayed-enslavement XP queue so the original captor keeps the Roguery XP when those prisoners are auto-enslaved later.

| Deposit Path | Who | Tracking |
|---|---|---|
| AI lord enters same-faction castle | AI | `B1071_CastlePrisonerDepositPatch` → `RecordDeposit` |
| Player "Donate prisoners" at ally castle | Player | `OnPrisonerDonatedToSettlement` event → `RecordDeposit` |
| Player "Manage prisoners" at own castle | Player | Skipped (same-clan, no economic impact) |

### Vanilla Prisoner Handling — Two Patches at Castles

Two vanilla code paths would otherwise destroy prisoners before the castle recruitment system can use them:

**Patch 1: Daily tick blocked** (`B1071_CastlePrisonerRetentionPatch`)
The vanilla `PartiesSellPrisonerCampaignBehavior.DailyTickSettlement` sells ~10% of settlement prisoners daily at AI castles. This is **blocked** when castle recruitment is enabled. Without this patch, T4+ prisoners would vanish before completing their training period.

**Patch 2: Settlement entry redirected** (`B1071_CastlePrisonerDepositPatch`)
The vanilla `OnSettlementEntered` handler fires when any non-player, friendly party enters a fortification. It collects ALL non-hero regular prisoners from the party and calls `SellPrisonersAction.ApplyForSelectedPrisoners`. Critical discovery: that action ONLY removes prisoners from the party's roster and pays gold — it **never adds them to the settlement's prison roster**. The prisoners simply vanish.

**Patch 3: Cross-faction influence blocked (v0.1.8.8)** (`B1071_PrisonerDonationInfluencePatch`)
Vanilla's `InfluenceGainCampaignBehavior.OnPrisonerDonatedToSettlement` awards influence for ALL prisoner donations regardless of faction match. A Harmony Prefix blocks the influence grant when the donating party's `MapFaction` does not match the settlement's `MapFaction`. This prevents mercenaries from earning influence (which converts to gold) by depositing prisoners at neutral castles. Same-faction deposits work as vanilla.

Our Harmony Prefix intercepts this for **both castles and towns** (v0.1.7.8):

**Castle branch:** Non-hero regular prisoners are moved from the party's prison roster directly into the castle's prison roster (free deposit — lords deliver prisoners as duty, no gold paid). A **prison capacity check** enforces the vanilla `PrisonerSizeLimit` — when the castle prison is full, excess prisoners are not deposited and fall through to vanilla sell behavior at the next town. **When slave economy is disabled**, T1–T3 prisoners are skipped entirely (they have no processing pipeline at castles without auto-enslavement — the retention patch would trap them forever). They stay with the lord and vanilla sells them for ransom. Depositor tracking is recorded for the consignment income model. After the prefix, the party's roster contains only heroes (+ overflow/skipped regulars); vanilla's handler runs and handles what remains.

T1–3 prisoners deposited this way will be auto-enslaved on the next daily tick (requires slave economy ON). T4+ prisoners begin their conversion day-count immediately.

**Town branch (v0.1.7.8 — race condition fix):** Non-hero prisoners at or below `CastlePrisonerAutoEnslaveTierMax` (default T3) are converted to `b1071_slave` trade goods and added directly to the town's market `ItemRoster`. The town **buys** each slave at the current market price — gold is deducted from `Town.Gold` and paid to the AI lord via `GiveGoldAction.ApplyForSettlementToCharacter`. If the town runs out of gold mid-batch, remaining T1–T3 prisoners stay with the lord for vanilla to sell/ransom. T4+ prisoners are left for vanilla normally. This branch was added to fix a critical race condition: vanilla's `OnSettlementEntered` handler registered before our `SlaveEconomyBehavior.OnSettlementEntered` (MbEvent fires FIFO), so vanilla would vaporize ALL prisoners before our mod could enslave them. Moving enslavement into the Harmony Prefix guarantees it runs first.

### Daily Tick Order

The castle daily tick runs 5 steps in sequence:

1. **AutoEnslave** — T1-T3 prisoners → slave market
2. **AdvancePrisonerConformity** — Share the castle budget among unfinished prisoner types
3. **RegenerateElitePool** — Add culture troops from manpower
4. **AiAutoRecruit** — AI lords take from elite pool + ready prisoners
5. **GarrisonAbsorbPrisoners** — Garrison absorbs ready prisoners (1/day)

### Persistence

All castle recruitment data is saved with the campaign:

- Native prison roster XP: conformity points, saved by Bannerlord
- `_conformityRemainders` / `_conformityCursors`: per-castle fractional budget and rotation, saved in parallel lists
- `_prisonerDaysHeld`: legacy day counters consumed once on session launch
- `_elitePool`: per-castle per-troop stock counts (culture elite pool)
- `_enslavementXpTracking`: per-castle per-troop FIFO of original depositor hero + party for delayed Roguery XP attribution

The mod dictionaries are flattened into parallel lists for `SyncData` serialization. Native roster XP is saved by the game. Empty prisoner entries lose their points, and stale depositor tracking is cleaned when prisoners are recruited or removed.

### MCM Settings (Castle Recruitment Group)

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableCastleRecruitment` | true | Master toggle for the entire system |
| `CastlePrisonerAutoEnslaveTierMax` | 3 | Unified enslavement tier cap (player + AI). Controls all enslavement paths: player town menu, AI town entry, castle auto-enslave. T4+ must go to castle or ransom. |
| `CastleRecruitT4Days` / `T5Days` / `T6Days` | 10 / 21 / 35 | Legacy group: old-save timer migration only; no effect on ongoing conformity |
| `CastleRecruitGoldT4` / `T5` / `T6` | 1200 / 2500 / 5000 | Gold cost per recruit by tier |
| `CastleElitePoolMax` | 20 | Maximum elite troops per castle |
| `CastleEliteRegenMin` / `RegenMax` | 1 / 3 | Daily regen range (scales with prosperity) |
| `CastleEliteManpowerCost` | 1 | Manpower cost per elite troop regenerated |
| `CastleRecruitDrainsManpower` | true | Whether elite recruitment drains manpower |
| `CastleEliteAiRecruits` | true | Whether AI lords can recruit from castles |
| `CastleHoldingFeePercent` | 30 | Commission % the castle owner receives when another clan's prisoners are processed |


### v0.1.7.2 Audit Fixes

**UI clan-waiver display fix:** The castle recruitment screen now correctly reflects clan waivers.
- At your own clan's castle: elite troops show "Elite (50%)" and the button is gated on the half price rather than the full one.
- Same-clan depositor prisoners show "Ready (Free)" with the button enabled.
- Hint text: "Recruit one {name} (same clan — 50% cost: {COST} gold)" for elites, "(clan waivers — free)" for waived prisoners.
- Previously, the UI checked raw gold cost without waivers, graying out buttons even when recruitment was free.

**FIFO depositor tracking fix:** RecordDeposit now always appends new entries instead of consolidating same-hero entries. This preserves strict FIFO ordering when the same lord deposits the same troop type at the same castle with interleaved deposits from other lords. Prevents over-crediting earlier depositors when processing stops midway (e.g., town runs out of gold).

**Removed CastleEliteAiMaxPerDay MCM setting:** This legacy setting was never used in code — AI lords were already uncapped, recruiting up to their party limit. Setting removed from MCM entirely.

---

## 9A. Troop Service

Troop Service adds a campaign-level rotation pressure to field armies. Soldiers are tracked as individual FIFO service records by party, troop type, and join day. When a soldier reaches his configured service threshold, he can leave during the daily tick.

**Per-man accounting (v1.0.3.4).** Every current service, reserve, veteran, and recall record now carries the clan that originally raised the soldier and the clan employing him for the current term. Origin survives upgrades and re-enlistment; employer changes when a veteran is hired again. This keeps short transfer restoration, remote recall cancellation, and player/AI veteran access scoped to the right clan.

Short troop transfers preserve service age through a bounded transfer reserve, so moving soldiers between field parties or briefly parking them outside the party does not reset their service term. The reserve expires after the greater of 14 days, the warning lead, or the paid-extension duration to avoid permanent save growth from genuine battle losses. Direct castle recruitment paths explicitly register their added soldiers as fresh service entries, so newly recruited castle elites and converted prisoners start at age 0 instead of being matched against old reserve records.

This is intentionally tied to Bannerlord's calendar, not real-world days. A Bannerlord year is about 84 days, so service thresholds are tuned in raw in-game days and exposed in MCM presets: Light, Moderate, Harsh, and Custom.

### Scope

- Player main party: fully tracked and visible in the Troop Service screen.
- AI lord and mercenary field parties: same departure and paid-extension logic for parity.
- Excluded: garrisons, militia, caravans, villagers, bandits, and prisoners.

### Player visibility

The player can open the Troop Service screen from the campaign map with the configured hotkey (default: F9). The screen lists each active main-party soldier record, showing troop, tier, service age, days remaining, warning status, and extension cost.

The system warns the player before departures. Soldiers within 14 days of leaving trigger a notification, throttled to one every `DemobilizationNotifyIntervalDays` (default 3) rather than one every campaign day, and the popup appears on the same interval when the earliest soldier reaches the warning lead day or day 0.

### Paid extension

Each visible soldier can be extended individually, up to `DemobilizationMaxExtensions` times (default 3). AI lord field parties buy extensions on the same rule and at the same prices during daily service processing, if the lord has enough spare gold after the configured treasury buffer. The default extension length is 21 Bannerlord days.

Genuine troop upgrades preserve service history but subtract the configured promotion bonus from service age, defaulting to 5 days. This rewards promotion without allowing upgrade loops to reset a veteran to age 0, and the soldier's accumulated extension count travels with him.

The cost formula is:

```text
troop tier x extension days x gold-per-tier-day x (2 + extensions already taken) / 2
```

The trailing factor is +50% per repeat, so holding on to a man gets progressively harder instead of being a single all-or-nothing purchase.

Extension is a gold sink; the manpower side of a soldier's life is settled at discharge instead, when he walks home and returns his manpower to the pool that raised him (see below). The intent is to make long campaigns demand reserves and timing, while still giving field commanders a lever when an army must stay in the field a little longer. The default balance favors predictability: warnings every 3 days, an 8% daily troop-type departure cap, 5 maximum departures per party per day, a 5x AI extension treasury buffer, and season/crisis pressure disabled unless enabled in MCM. Garrisons and militia are intentionally outside the feature and never demobilize or buy extensions through this system.

---

### Veteran Return and the Veteran Register (v1.0.2.7)

**The defect.** `RetireOverdueCohorts` used to end at `RemoveTroopsFromRoster(party, troop, 1)`. The roster shrank, the `CohortEntry` count shrank, and the soldier ceased to exist. Recruitment had already debited the settlement's manpower pool to raise him, and nothing ever credited it back — so every completed service term was a permanent withdrawal from the map's recruitment economy. This compounds across every AI party in the campaign: a long-running save has strictly less manpower in it than a fresh one, for no modelled reason. The unrealism the player noticed ("they disappear forever") and the economic leak are the same bug.

**Why origin tracking had to come first.** `OnTroopRecruited(Hero, Settlement recruitmentSettlement, Hero, CharacterObject, int)` already receives the settlement; the old code discarded the argument. Without it there is no answer to "where does he go back to", so the return model is not implementable at all. `CohortEntry` and `TransferReserveEntry` therefore both carry a `HomeId` string, and it must survive three transformations that the service system already performs on entries:

1. **Roster reconciliation** — `NormalizeIndividualEntries` splits multi-man entries into one-man entries; `AddIndividualEntries` takes `homeId` and `extensionCount` so the split is lossless.
2. **Tier upgrades** — the carryover path passes `source.HomeId`, so a promoted recruit keeps the village that raised him rather than being re-homed to wherever he happened to be promoted.
3. **The transfer reserve** — soldiers parked outside a party for a few days round-trip through `TransferReserveEntry`, which stores and restores both fields.

Soldiers the system first sees through reconciliation rather than a recruitment event have no event settlement. `ResolveHomeIdForParty` supplies a best guess: the settlement the party is standing in, else the leader's clan seat, else empty. Empty is tolerated — `SendVeteranHome` re-runs the party fallback at discharge time.

**Discharge.** `SendVeteranHome(party, troop, cohort, count, today)` resolves the home settlement, then rolls `DemobilizationManpowerReturnPercent` **per man**. Per-man rather than per-batch is not a stylistic choice: `RetireOverdueCohorts` discharges one soldier at a time, so `count * percent / 100` floors to zero for every setting below 100 and the option would appear to do nothing at all. Men who arrive are credited to the pool through `B1071_ManpowerBehavior.ReturnManpowerForTroops`, which already maps a village to its bound town pool the same way recruitment drew from it, and are added to the register. That method exists so the credit is priced by the same `GetManpowerCostPerTroop` lookup the charge uses: crediting one point per man is only correct while `Base manpower cost per troop` is left at its default of 1, and quietly short-changes every settlement above it. A man who did not make it home is not registered either — he is simply gone, which is the correct reading of "did not survive the journey".

**Register shape.** `_veteranRegister` is `settlementId → troopId → List<VeteranEntry>`, where each entry carries `{ DischargeDay, Count, OriginClanId, EmployerClanId }`. Batching by discharge day and provenance rather than storing one entry per man keeps the structure small while still supporting per-batch ageing and FIFO removal. `FromPlayer` remains only to interpret pre-1.0.3.4 blank-employer saves for the player; new records use employer identity.

**Settling (v1.0.2.8).** A batch is not hireable until `today - DischargeDay >= DemobilizationVeteranSettlingDays` (default **7**). Without it a term of service was optional: `SendVeteranHome` stamps `DischargeDay = today` and nothing gated hireability on elapsed days, so a soldier released this morning was recallable this afternoon for the price of a bounty. `IsHireable(entry, claim, today)` is the single gate — `entry.Count > 0 && Matches(entry, claim) && IsSettled(entry, today)` — and both `CountVeterans` and `RemoveVeteransFromRegister` take a `today` argument so the player path and the AI path cannot diverge. The value is clamped to `retentionDays - 1`: the two settings pull against each other, and unclamped a settling period at or above retention would age every batch off before it became hireable. Cancellation returns men with `DischargeDay = today - VeteranSettlingDays()`, already rested, because they had finished the rest once before the order went out.

**Decay.** `CleanupVeteranRegister(today)` runs once per daily tick and drops any batch older than `DemobilizationVeteranRetentionDays` (default 84 — one campaign year). It is also called defensively from `GetVeteransForUi` and `TryRecallVeterans` so the UI can never offer a man who has already aged off. Empty troop lists and empty settlements are pruned as they empty, so the dictionary does not grow without bound over a long campaign.

**Scatter.** `ScatterVeteransAt(settlement, reason)` removes `DemobilizationVeteranScatterPercent` of every batch. Two events feed it:

- `RaidCompletedEvent(BattleSideEnum, RaidEventComponent)` → the raided settlement.
- `OnSettlementOwnerChangedEvent(...)` → the settlement, **only when the owning realm actually changed**. A fief passed between clans of the same realm is not an event the men at home would flee from. A captured town also scatters its bound villages, because the villages changed realm with it.

  The realm comparison is on `Clan.MapFaction`, not `Clan.Kingdom`. A clan outside a kingdom — an independent lord, a rebel clan that has not yet declared itself, a minor faction — has a null `Kingdom`, so the original `Kingdom` test returned early and silently skipped every conquest involving one. That is not a rare corner: it covers a player who declared independence taking his first town, and every settlement a rebellion seizes. `MapFaction` returns the kingdom for a clan that has one and the clan itself otherwise, so kingdom-to-kingdom transfers compare exactly as before.

`entries[i].Count * percent / 100` floors to zero for a batch of one, so a zero result is re-rolled against the same percentage. Without that, a register holding single-man batches would be perfectly raid-proof.

**Recall.** `TryRecallVeterans(settlement, troop, requested)` trims the request successively rather than failing on the first constraint, so a player asking for "All" gets as many as the world can actually supply:

1. Register stock at that settlement.
2. Party room — `PartySizeLimit - TotalManCount`.
3. Gold — `Hero.MainHero.Gold / goldPerMan`, where `goldPerMan = tier × DemobilizationRecallGoldPerTier`.
4. Manpower — loops on `CanRecruitCountForPlayer` and steps `wanted` down to what the pool can cover.

Then it charges the bounty via `GiveGoldAction` with a `null` recipient — the money goes to the men, not to the fief holder, because paying the owner would net to nothing at the player's own settlements and the quoted price would never actually be charged where it matters most — calls `ConsumeManpowerPublic`, adds to the roster, removes from the register oldest-first, and calls `AddFreshCohort(..., "veteran_recall", settlement.StringId)`. That last call is the point: the recalled man's clock restarts and **this** settlement becomes his home, so a veteran repeatedly recalled from a town gradually becomes that town's man regardless of where he was originally levied.

**Why the recall costs manpower as well as gold.** Charging gold alone would make the register a way to convert money into troops without touching the manpower economy, which is precisely the constraint the whole mod exists to impose. Charging manpower alone would make recall free for a rich player and identical to fresh recruitment. Charging both prices the register correctly: the settlement gets its manpower back at discharge and spends it again at recall, so at a 100% return rate a unit cycled through home and back is manpower-neutral over the round trip — the credit and the charge run through the same price lookup, which is the whole reason `ReturnManpowerForTroops` exists rather than a raw head-count credit. Below 100% the round trip is deliberately lossy. The gold bounty is what the player pays for skipping the retraining from tier 1.

**Access.** The resolver is three-state for both player and AI: at war it denies the whole register; otherwise, with `Allow cross-clan veteran recruitment` **off** (the default), it returns own-employer-only access. A clan may therefore take batches whose current `EmployerClanId` matches its own clan and no others. With the option **on**, 0 = any non-hostile lord, 1 = same kingdom, and 2 = owning clan only (**the ladder default since v1.0.2.8**) grant the full register; callers that fail the ladder still receive their own-employer batches. The ladder governs veterans employed by other clans, never a player-only exception.

**Why the ladder default moved from kingdom to clan (v1.0.2.8).** At 1 the player could walk into any allied town and hire the men every other lord of his realm had discharged there — which, once a campaign has run a while, is most of the veterans on the map, available for a gold bounty and with no relationship to whether he had ever recruited or trained one of them. The register reads as a personal ledger and behaved as a kingdom-wide labour pool. Clan only keeps another lord's countrymen with his own clan. Profile migration **v23** moves an existing config from 1 to 2 **only if it is still on 1**; 0 and 2 are deliberate choices and are left alone. Since v1.0.3.4 this ladder matters only when the cross-clan option is enabled; the new option deliberately defaults off without a migration so existing profiles are protected too.

**Your own men are always yours, outside war (v1.0.2.7).** The access ladder alone produced the worst bug in the feature. `DemobilizationVeteranRecallAccess` defaults to 1, `VeteranMenuCondition` returned `false` when the check failed, and returning `false` from a menu condition *hides the option*. So a soldier recruited at a castle outside the player's realm — the converted-prisoner path recruits from exactly such castles — marched home on discharge, was written correctly to that castle's register, and then sat behind a door the player could not see. From the player's chair he had disappeared forever, which is the precise complaint the whole return model was built to answer. The origin was never wrong; the door was.

`TryGetPlayerRegisterAccess(settlement, out bool ownMenOnly)` replaces the single yes/no with three outcomes:

- **War with the owner** → `false`. The register is closed and the menu option stays hidden. A hostile town does not advertise its reserves.
- **Cross-clan recruitment off** → `true`, `ownMenOnly == true`. The player may collect only batches whose current employer is the player clan.
- **Cross-clan recruitment on and ladder satisfied** → `true`, `ownMenOnly == false`. The full register.
- **Anything else short of war** → `true`, `ownMenOnly == true`. The player may collect batches whose current employer is the player clan, and nobody else's. Pre-1.0.3.4 blank employers use `FromPlayer` only for this compatibility case.

Every read and every consume path takes the flag, not just the display: `GetVeteranCountAt(settlement, ownMenOnly)` for the headcount in the menu label, `GetVeteransForUi` for the rows, and `RemoveVeteransFromRegister(..., ownMenOnly)` for the draw — without the last one a recall on foreign ground would count the player's men and then quietly pocket the local lord's. On foreign ground the option is also hidden when the player has no men waiting, since an empty stranger's register would otherwise put a dead entry on every friendly settlement menu on the map.

This does not loosen the v1.0.2.6 foreign-recruitment balance. By default another lord's countrymen are unavailable to every other clan. Enabling cross-clan recruitment deliberately restores the ladder for both player and AI; what never changes is that a setting cannot confiscate the player's own soldiers outside war.

**Persistence.** The eleven provenance and pending-batch keys added in v1.0.3.4 are `b1071_demob_originClanIds`, `b1071_demob_employerClanIds`, `b1071_demob_reserveOriginClanIds`, `b1071_demob_reserveEmployerClanIds`, `b1071_demob_reserveSourcePartyIds`, `b1071_demob_vetOriginClanIds`, `b1071_demob_vetEmployerClanIds`, `b1071_demob_pendBatchesPerOrder`, `b1071_demob_pendBatchOriginClanIds`, `b1071_demob_pendBatchEmployerClanIds`, and `b1071_demob_pendBatchCounts`. Reserves are keyed `employerClanId → troopId`; their old global rows have no safe employer and are discarded on load rather than restored to a different clan. Pending batches are flattened only after their header and each header stores the number actually written, while raw-header indices stop a corrupt header from shifting a later order's batches. The legacy `extendedFlags` bool lists are still written for extension compatibility.

`vetFromPlayer` is deliberately **excluded** from the `Math.Min` row count that guards the register load, and read as `i < list.Count && list[i]`. A save written before the flag existed simply has no list, and every veteran in it loads as nobody's man in particular — still visible and hireable under the ordinary access ladder, but not carrying a personal claim. Inferring the flag from anything else on the entry would hand a returning player free veterans across the map, which is a worse failure than one campaign's worth of pre-existing men staying behind the normal rules. Legacy pending orders lazily reconstruct a player-employer batch from their saved player-owned count and leave the remainder unattributed.

---

### Remote Recall and the Pending-Arrival Ledger (v1.0.2.8)

**The problem the register created.** Discharge sends a man to the settlement that raised him, and a party recruits from wherever it happens to be, so after fifty campaign days a player's veterans are spread across a dozen fiefs in three kingdoms. Collection required standing in each of them. The feature was correct and unusable at the same time: the only way to act on it was to spend a season doing laps of the map, which is exactly the busywork the manpower economy is supposed to replace with decisions.

**Why in-transit men are not a `MobileParty`.** The obvious implementation — spawn an escort party and give it a `GoToParty` behaviour — is the most expensive option available. It needs a party template, a leader, food, an AI behaviour and a despawn path; it becomes an entity enemies can chase, capture and raise map events against; and every one of those is a state machine that has to stay consistent with a ledger the demobilization behaviour also owns. It is also, by a distance, the most likely thing in this release to corrupt a save — the same class of hazard `ClanSurvivalBehavior` was cut back for. A recall in flight is therefore a row, not an entity: `PendingRecallEntry` holds `OrderId`, `SettlementId`, `TroopId`, `Count`, `OrderDay`, `GoldPaid`, `ManpowerDrawn`, `PlayerOwnedCount`, `CourierRemaining`, and a `PosX`/`PosY` pair.

**Two phases, one day's budget.** `AdvancePendingRecalls(today)` runs on the daily tick with `float budget = 1f`:

1. **The order rides out.** `CourierRemaining` starts at the map distance from the player to the settlement and is drawn down at `DemobilizationCourierSpeed` (default **120**). Nothing moves during this phase — the men have not been told.
2. **The men march in.** Once the courier lands, the row's `Vec2` is walked toward `MobileParty.MainParty.GetPosition2D` at `DemobilizationMarchSpeed` (default **60**).

Leftover budget carries from phase one into phase two, so a courier that lands mid-day does not waste the rest of it.

**Why a live position rather than a remaining distance.** A scalar is half the code and models the wrong thing: it fixes the destination at order time, so a column marches to a field the player left a week ago, and a player who doubles back to meet his men gains nothing. Storing the position and re-reading the player's each day makes "they march to wherever you are" literally true, and it is what makes the ETA in the UI honest rather than a promise made once.

**Arrival is allowed to fail without losing anyone.** `TryDeliverPendingRecall(entry, party, today)` returns `false` — keeping the row alive for the next tick — while `party.MapEvent != null || party.SiegeEvent != null` or `room <= 0`. Adding men to a roster mid-battle is the same unsafe mutation early release already refuses. Partial delivery is supported: room for three of five signs three in via `AddToCounts` + `AddFreshCohort(..., "veteran_recall_remote", entry.SettlementId)` and leaves two on the ledger. The one case that returns `true` without delivering is `ResolveTroop` returning null — a submod removed the troop type between saves, there is nobody left to deliver, and retrying forever is worse than dropping the order with a log line.

**In-flight men count against the party cap.** `CountPendingRecallSoldiers()` is added to `TotalManCount` in both `HasPartyRoomForOne` and `TryRecallVeterans` — which is why the former stopped being static. Without it a player could place ten standing orders against a party with room for one and have nine columns arrive to a wall.

**Cancellation.** `TryCancelPendingRecall(orderId, settlementId, troopId)` finds the row by its stable handle, validates settlement and troop, credits `ManpowerDrawn` back, and returns every remaining ordered recall batch to its prior employer. **The gold is deliberately not refunded** — it was handed to the men when the order went out, and a free cancel turns a standing order into a costless option the player can hold open on every register on the map.

**Why the ledger records what was taken, not what was quoted.** `ManpowerDrawn` is the return value of `ConsumeManpowerPublic`, and cancellation credits that figure flat rather than recomputing the price. Recall provenance is stored as ordered `RecallBatch` rows rather than a player-owned count: partial arrivals consume batches FIFO, and scatter allocates its loss by largest remainder with oldest batches breaking ties. That preserves exactly which employer gets the survivors back.

**Scatter reaches only the courier phase.** `ScatterVeteransAt` calls `ScatterPendingRecallsAt(settlementId, percent)` **before** the early return for a settlement with no register, because an order can be outstanding against a register that has since emptied. Only rows with `CourierRemaining > 0f` are rolled. This narrows the obvious reading on purpose: men whose order already reached them are on the road and past the walls, and burning them for a raid on a town they have left punishes the player for a distance the model already charged him for. Men still sitting at home when the place is sacked take the same roll the register does.

**Map-wide view.** `GetAllVeteransForUi()` walks `_veteranRegister` and appends rows per settlement through the same `AppendVeteranRows(settlement, rows, mapWide)` the settlement-local `GetVeteransForUi` uses, so the two lists cannot drift. The one behavioural difference is `if (mapWide && !hasAccess) return;` — the local view explains why a register is shut, because the player deliberately walked into that settlement; the map-wide view drops it, because a screen whose main content is a wall of identical refusals is worse than a shorter screen. `SortVeteranRows(rows, byArrival)` orders by ETA first in map-wide mode, then tier descending, then days-until-gone, then name. `VeteranView` gained `Settlement`, `SettlementName`, `IsRemote` and `EtaDays`; `EstimateRecallDays` quotes `distance / courier + distance / march` and clamps to a minimum of 1, since an order placed today reading "0 days" reads as a bug.

**Hotkey.** `B1071_VeteranRecallScreen.Tick(float)` polls `Input.IsKeyPressed(GetConfiguredHotkey(...))` and is dispatched from `SubModule.OnApplicationTick` immediately after `B1071_DemobilizationScreen.Tick`. `EnableVeteranRecallHotkey` (default **true**) and `VeteranRecallHotkeyChoice` (default **0** = F8; 1/2/3 = F10/F11/F12) gate it. Polling rather than a `HotKeyManager` category because the troop service screen already establishes that pattern here, and a second category is a second thing to collide with a player's rebinds.

**Persistence.** Existing recall headers are followed by flattened origin/employer/count batch rows and a batch count per header, not an order-ID join. That remains correct when invalid legacy IDs are repaired on load. A legacy pending order lazily reconstructs a player-employer batch from `pendOwnCounts` plus an unattributed remainder; new saves write the derived legacy count only for backwards wire compatibility.

**Settings.** `EnableDemobilizationRemoteRecall` (default **true**). Off restores the previous behaviour exactly: `AppendVeteranRows` sets `b1071_recall_block_remote` as the per-row block reason, so the button is visibly disabled with an explanation rather than silently absent.

### AI Parity on the Register (v1.0.2.8)

**The gap.** Only the player could ever draw from a register. Every AI soldier who completed his term was written home, aged off after `DemobilizationVeteranRetentionDays`, and lost — which is precisely the leak v1.0.2.7 closed for the player and left open for everyone else. "The same rules apply to the AI" is the mod's first stated principle, and this was the largest place it was not true.

**What was built.** `CampaignEvents.SettlementEntered` → `OnSettlementEntered(party, settlement, hero)` → `TryAiHireVeterans(party, clan, settlement)`. A lord's field party that enters a settlement uses the same three-state access resolver as the player, removes only the batches it may claim, and starts those men on a new term under the hiring clan.

**Passive by design.** Nothing sends a lord looking for a register. No new pathing, no detours, no teleported men. The parity is in the rules he obeys when he happens to be standing somewhere, not in a new appetite. Giving the AI a courier system of its own would mean modelling a hundred simultaneous marches to targets that themselves move every day — the cost is real and the player would never see the difference, because he cannot watch another lord's reinforcements walk.

**He gets no own-men exception.** `TryAiHireVeterans` calls the plain `CanFactionAccessVeteranRegister(clan.MapFaction, clan, settlement)` and draws only `VeteranClaim.ExceptPlayer`. The player's discharged men are never on offer to anyone, at any access setting, anywhere. The register records whose men are his precisely so that this is answerable.

**The player's own clan is excluded outright** (`if (clan == Clan.PlayerClan) return;`). A companion's party quietly draining a register the player is saving for himself is the opposite of helpful, and his own lords' discharges already count as his men.

He keeps the reserve `CanAiAfford` enforces for service extensions, and `wanted` trims successively against party room, gold and manpower in the same order the player's request does — so a register can neither bankrupt a lord nor overfill his party. Gated by `EnableDemobilizationAiRecall` (default **true**).

**Every trim runs one way only.** The gold step computes the largest count the reserve still leaves room for — `(Gold - 1) / (goldPerMan × bufferMultiplier)` — and takes `Math.Min` of that and what was asked for, then re-tests `CanAiAfford` on the trimmed figure so the rule itself has the last word. Written as a loop that assigned its own estimate to `wanted`, it could **raise** the request: the estimate ignored the reserve multiplier, so a lord with a deep purse standing over a register holding three men could come away with seven, four of whom existed nowhere — past the register's stock, past his party's room, and past the reserve the check exists to defend. The manpower step is now written the same way for the same reason, shrinking strictly on each pass; a pool reporting room for more men than were asked for would mean the gate had already let the request through, so treating that as a larger request is reading a refusal as an offer.

---

**Service term retune.** All three presets and the Custom defaults were multiplied by roughly two and the tier spread widened, because a Bannerlord year is 84 days and the old Moderate T6 term of 112 days made an elite veteran a sixteen-week hire. Moderate is now 42/63/84/126/168/252 for T1–T6, Light 63/84/126/168/252/336, Harsh 28/42/56/84/112/168. The settings migration rewrites the Custom values **only if they still match the v17 defaults exactly**, so a player who tuned them keeps his numbers.

**Screen rows are groups, not records (v1.0.2.7).** Service records hold exactly one man each — `NormalizeIndividualEntries` splits every multi-man entry — and that is load-bearing for per-man service age, FIFO discharge order and the transfer reserve, so it cannot change. Printed literally it gives one line per soldier, and the release button on such a line can only ever release one man, which is what made the old **All** button look broken. Grouping therefore belongs in the display layer: `GetMainPartyCohortsForUi` collapses records that share a troop, a home, an enlistment day and an extension count into one `CohortView`, since men matching on all four are genuinely interchangeable. `ExtendCost` is quoted **per man**, because the buttons act on one, five or the whole row and a single total would be wrong for two of those three.

A row therefore cannot be addressed by slot index — indices shift the moment `RemoveEmptyEntries` prunes a drained record. `CollectGroupIndices(cohorts, homeId, joinDay, extensionCount)` re-resolves the records behind a row at the moment of the call, in slot order, and both mutators take the group key instead: `TryExtendCohortGroup(partyId, troopId, homeId, joinDay, extensionCount, requested)` (new) and `TryDischargeCohort`, re-signatured the same way. `TryExtendCohort(partyId, troopId, cohortIndex)` is kept unchanged for submods. A batch extension the player cannot fully afford shrinks to what his gold covers rather than failing outright, and it extends first and bills for what actually happened, so the two numbers cannot diverge.

The screen itself gained the rest of the group idiom: **click one, Shift+click five, Ctrl+click the whole row** on both buttons, replacing the fixed **1 / All** pair; column labels over the previously unlabelled Extend and Send Home columns; a Status column drawn in three fixed colours (three `TextWidget`s gated by `IsVisible`, rather than binding a colour string, whose Gauntlet conversion on a bound property is unverified); alternating row shading, decided in the parent VM while row order is still known because a `ListPanel` item template has no index of its own; and a widened window (1060 → 1200px) to give the Troop column room for the long names that were being clipped.

The veteran register screen follows the same idiom for the same reason. Its **1 / 5 / All** buttons stood immediately right of `Gold Each` and `Manpower Each` and read as a third row of numbers rather than as controls; they are now a single labelled **Recall** button under a **Recall** header, on the same modifier convention, with the same striping. `TryRecallVeterans` already trims a request against stock, party room, gold and manpower in that order, so Ctrl+click needs no separate guard. Its header row also had the summary and the gold total sharing one full-width widget, which the longer register summary overran: the summary is now clamped clear of the gold block and cut to one sentence, with the foreign-ground explanation moved to a second line that appears only when it applies.

**Extension retune.** `HasBeenExtended` (bool) became `ExtensionCount` (int) capped by `DemobilizationMaxExtensions` (default 3). `GetExtensionCost` multiplies the base by `(2 + alreadyExtended) / 2`, i.e. +50% per extension already taken. Combined with the `DemobilizationExtensionGoldPerTierDay` default drop from 5 to 2, one T5 soldier costs 210g / 315g / 420g across his three extensions where the old single extension cost 525g. The old price exceeded the cost of simply recruiting replacements, which is why the lever was never rational to pull.

**Notification throttle.** `ShowMainPartyWarningIfNeeded` previously reprinted the standing warning every campaign day for the entire lead window. It now uses three separate day markers: `_lastWarningEvalDay` guards re-entrancy within a single day, `_lastWarningDay` throttles the scrolling message by `DemobilizationNotifyIntervalDays` (default 3), and `_lastPopupDay` throttles the inquiry on the same interval. Splitting the re-entrancy guard from the interval marker is what allows the interval to be honoured without losing the once-per-day guarantee — a single field cannot do both.

The popup carries one exemption: a row at `RemainingDays <= 0` bypasses `_lastPopupDay` entirely. Under a pure interval the popup that fires on the lead day silences the one that would fire on the day the men actually walk, which is the only warning that still leaves the player time to buy an extension. The scrolling message stays on the plain interval, so the exemption costs at most one extra inquiry per departure day.

**Early release (v1.0.2.7).** `TryDischargeCohort(partyId, troopId, cohortIndex, requested)` ends a term at a moment the player chooses instead of one the clock chooses. It resolves the entry, trims `requested` to the entry's count, calls `RemoveTroopsFromRoster`, and then hands off to the same `SendVeteranHome` a completed term uses — so the home resolution, the per-man `DemobilizationManpowerReturnPercent` roll, the manpower credit and the register entry are byte-for-byte the same code path. There is deliberately no separate early-discharge accounting to keep in sync with the normal one.

Three properties are deliberate:

- **Free.** No gold, no manpower penalty. Recruit-then-release is already manpower-neutral by construction, and every recall costs gold, so there is nothing to farm by cycling men through the register. Pricing it would only tax the player for tidying his own army.
- **Exempt from the daily departure caps.** `DemobilizationMaxDeparturesPerDay` and the per-troop-type percentage exist to stop a party bleeding a squad a day behind the player's back. This is his own decision; throttling it would only make him click the same button again tomorrow.
- **Refused during a `MapEvent` or `SiegeEvent`.** Pulling men out of a roster while a battle is resolving is the same class of mid-action state change that corrupts a party elsewhere in the codebase. The player is told to disengage first rather than being silently ignored.

Main party only — the guard compares `GetPartyId(MobileParty.MainParty)` against the passed `partyId`. AI parties have no reason to want it, and letting a submodule discharge someone else's army through this entry point would bypass the caps that keep AI attrition legible.

The Troop Service screen exposes it as **1** and **All** buttons per row, alongside a new **Count** column — a release button on a row whose size the player cannot see is a trap, and recall creates multi-man entries routinely. `CohortView` gained `ReturnsHome` so the button hint can tell the truth about where the men go when `EnableDemobilizationVeteranReturn` is off: they simply leave, and their manpower is lost.

**All** asks for confirmation above `ConfirmReleaseThreshold` (10 men) and stays a single click below it. The asymmetry is the point: the action is cheap to undo only in principle — buying the men back costs the re-enlistment bounty and the settlement's manpower a second time, and only works while they are still on the register — so the prompt is priced to the size of the mistake rather than shown every time.

`SendVeteranHome` gained an `out int arrived`. It always rolled survival per man, but previously returned only the `Settlement`, so `TryDischargeCohort` had no way to know how many actually made it and reported everyone who marched off. At the default `DemobilizationManpowerReturnPercent` of 100 the two numbers are identical; below it they are not, and the release message now quotes the arrivals. `RetireOverdueCohorts` discards the value deliberately — its message says the men "set off home", which stays true either way.

---

## 10. War Effects

When the war effects system is enabled, the following events drain manpower:

### Raids
When a village raid completes successfully (village state = Looted):
- The bound town/castle pool is drained by `RaidManpowerDrainPercent` of its current value
- A daily cap (`RaidDailyPoolDrainCapPercent`) prevents very active raid columns from emptying a pool in a single day
- Deduplicated by village+day so the same village can only trigger one drain per day
- Also applies a delayed recovery penalty (see §10)
- Also adds war exhaustion to the defending kingdom (see §11)

### Siege aftermath
When a settlement is sieged and taken, depending on aftermath type:
- **Devastate**: pool set to `SiegeDevastateRetainPercent`% of maximum
- **Pillage**: pool set to `SiegePillageRetainPercent`% of maximum
- **Show Mercy**: pool set to `SiegeMercyRetainPercent`% of maximum

The pool can only decrease from siege (not increase). Delayed recovery and war exhaustion are also applied.

### Battle casualties
After every field battle and outside-the-walls siege skirmish, each party's casualty count (dead + wounded) is multiplied by `BattleCasualtyDrainMultiplier` and drained from that party's home settlement pool. Village-related raid map events are excluded.

### Conquest
When a settlement changes hands across kingdom lines:
- Pool is reduced to `ConquestPoolRetainPercent`% of its current value
- Delayed recovery and war exhaustion are applied to the losing kingdom
- Internal fief grants (same kingdom) do NOT trigger this effect

### Dynamic conquest protection (ping-pong defense)

When `EnableDynamicConquestProtection` is ON and the pool's fill ratio is at or below `ConquestDepletedThresholdPercent` (default: 25%), the retain percentage is linearly interpolated from `ConquestDepletedRetainPercent` (default: 85%) at 0% fill up to the normal `ConquestPoolRetainPercent` (50%) at the threshold. Pools above 25% fill use the normal 50% retain.

**Impact:** A castle at 8/400 manpower (2% fill) retains ~84% (≈7) instead of 50% (≈4). Combined with depleted emergency regen, border castles repeatedly conquered in quick succession can recover between conquests instead of spiraling to permanent zero.

---

## 11. Delayed Recovery

After major war events (raids, sieges, conquests), settlements suffer a **recovery penalty** on regen rate that decays gradually over time:

- A penalty percentage (e.g., 40%) is applied to regen for a configurable number of days (e.g., 60 days)
- The penalty decays linearly from 40% → 0% over those days
- Multiple events stack by taking the worst (most severe) pending penalty but always re-starting the recovery timer
- A floor ensures regen never falls below 10% of normal even with severe stacked penalties

### Recovery penalty reduction when depleted

When `ReduceRecoveryPenaltyWhenDepleted` is ON and a pool's fill ratio is below `RecoveryDepletedThresholdPercent` (default: 25%), the recovery penalty is halved. A 25% regen penalty becomes 12.5%. This prevents the penalty from compounding the crisis at settlements that need faster regen most.

**Impact:** Depleted settlements recover from conquest penalties 50% faster, working synergistically with depleted emergency regen to pull settlements out of the death spiral.

Effect: Even after a war ends, previously raided settlements continue to regenerate slowly for weeks in-game, simulating realistic economic recovery.

---

## 12. War Exhaustion

Each kingdom maintains a **war exhaustion** value (0 to configurable max, default 100). It rises from:
- Battles: each casualty (dead + wounded) × `BattleExhaustionPerCasualty`
- Raid events: `RaidExhaustionGain` per completed raid on the kingdom's villages
- Siege events (defender): `SiegeExhaustionDefender` per settlement sacked
- Siege events (attacker): `SiegeExhaustionAttacker` per siege victory
- Settlement conquest: `ConquestExhaustionGain` per settlement lost

Exhaustion decays daily by `ExhaustionDailyDecay` (default: 0.65). War exhaustion additionally penalizes pool regen: a heavily exhausted kingdom regenerates its manpower slower (by up to 90% penalty, floored at 10% of normal).

### Historically-calibrated tuning (v0.1.8.2)

The exhaustion and diplomacy settings were retuned using mathematical modeling against a 212 in-game day playtest and historical calibration against the 1071–1081 Byzantine-Seljuk period:

- **Decay rate 0.65**: Recovery from catastrophic exhaustion (100→0) takes ~154 days (1.8 in-game years), matching post-Manzikert partial stabilization timelines.
- **Band thresholds (Rising=35, Crisis=65)**: Rising requires ~70 days of moderate war; Crisis requires ~130 days (~1.5 years). Only genuinely protracted wars escalate to Crisis.
- **Forced peace at 80**: ~43 days from Crisis entry to forced peace (~5 months in-game), matching historical political pressure timelines.
- **Multi-war reduction 5/war**: Each additional war front reduces the forced-peace threshold (80→75→70). Multi-front wars collapse faster.

---

## 13. Diplomacy Pressure

When the Diplomacy Pressure system is enabled, war exhaustion influences AI kingdom decisions:

### Declare War support modifier
When an AI clan votes on a war declaration, their support is penalized/boosted based on the kingdom's exhaustion. Higher exhaustion → less support for more wars.

### Make Peace support modifier  
When voting on peace proposals, exhaustion boosts peace support. Higher exhaustion → more support for peace.

### Kingdom-level war declaration block
Before AI kingdoms even add a `DeclareWarDecision` to their election queue, the mod can block it entirely (in the Crisis band or above the legacy threshold).

### Multi-war pressure
If a kingdom is already fighting multiple major wars, each additional war beyond the configured threshold adds extra peace bias, simulating strategic overextension.

---

## 14. Pressure Bands

To add realistic **hysteresis** (delayed response with memory), the diplomacy pressure system uses three bands instead of raw thresholds:

| Band | Meaning | War Support | Peace Support |
|------|---------|-------------|---------------|
| **Low** | Manageable exhaustion | Light penalty | Minimal bonus |
| **Rising** | Mounting war fatigue | Moderate penalty | Growing bonus |
| **Crisis** | Critical war weariness | Strong capped penalty | Strong bonus; war declarations blocked |

Bands use hysteresis: transitioning **up** (Low→Rising→Crisis) is immediate when thresholds are crossed. Transitioning **down** requires exhaustion to fall `hysteresis` points below the threshold — preventing rapid oscillation when a kingdom hovers near a boundary.

Bands enabled/disabled via MCM. Legacy mode uses raw numeric thresholds without hysteresis.

---

## 15. Forced Peace

When a kingdom reaches Crisis level exhaustion and other conditions are met, the mod can **forcibly trigger a peace** via `MakePeaceAction.ApplyByKingdomDecision`:

Conditions checked each day:
1. Kingdom is in Crisis band (or above legacy threshold)
2. Cooldown period since last forced peace has elapsed
3. Kingdom has more active wars than the `ForcedPeaceMaxActiveWars` minimum
4. Each candidate war has been ongoing for at least `MinWarDurationDaysBeforeForcedPeace`
5. Optional: skip if enemy is currently besieging one of the kingdom's core settlements
6. Skip if the kingdom is actively besieging the candidate faction's settlements (v0.1.8.10) — don't waste siege progress by suing for peace mid-operation

The peace is applied to the war with the best diplomatic score (vanilla's `GetScoreOfDeclaringPeace`). With `DiplomacyEnforcePlayerParity` ON (default), forced peace applies to the player's kingdom too — full AI parity. Disable it in MCM to exempt the player.

### Siege-Aware Peace Voting (v0.1.8.10)

The mod's `MakePeaceDecisionExhaustionSupportPatch` now checks for active siege operations before applying peace bonuses. When the voting kingdom has any `WarPartyComponent` whose `MobileParty.BesiegedSettlement` belongs to the peace target faction, **all** mod-added peace bonuses (exhaustion bands, manpower depletion, major-war bias) are fully suppressed. Vanilla's own scoring still applies — the mod simply stops amplifying it while sieges are in progress.

This prevents the observed scenario where a kingdom would vote for peace while having two armies besieging an enemy castle, caused by exhaustion/manpower bonuses overwhelming vanilla's base peace scoring.

### Early-War Peace Vote Penalty (v0.2.7.2)

Council peace votes for wars younger than `MinWarDurationDaysBeforeForcedPeace` (default 40 days) receive a large soft penalty in the `DetermineSupport` postfix. The penalty scales linearly from `EarlyWarPeacePenaltyStrength` (default 300) at day 0 down to zero at the minimum war duration threshold.

- **Mechanism:** For peace-supporting outcomes, `__result -= penalty`. For war-supporting outcomes, `__result += penalty`. This makes peace votes virtually impossible to pass in the first days of a war.
- **Not a hard block:** Extreme exhaustion or overwhelming council consensus can still override the penalty, but in practice the default 300-point penalty ensures no council votes for peace before ~30+ days.
- **Player parity:** Inherits the existing `DiplomacyEnforcePlayerParity` gating — when on, player kingdoms receive the same penalty.
- **War age source:** Uses `kingdom.GetStanceWith(peaceTarget).WarStartDate.ElapsedDaysUntilNow` for accurate per-war timing.
- **Diplomacy mod compatible:** Complements Bannerlord.Diplomacy's `MinimumWarDurationInDays` (default 21) which blocks peace *proposals*. B1071's penalty discourages peace *votes* — they address different stages of the peace pipeline.
- **MCM:** `EarlyWarPeacePenaltyStrength` (float, 0–1000, default 300) in the "Diplomacy (War Exhaustion)" group.

### Multi-Front War Relief (v0.2.7.2)

The base rule remains unchanged: forced peace cannot end a war younger than `MinWarDurationDaysBeforeForcedPeace` (default 40 days). However, a kingdom trapped in a genuine systemic collapse can now use an emergency minimum instead of being locked into a hopeless spiral.

Emergency relief activates only when **all** of the following are true:

1. The kingdom is fighting at least `EmergencyWarCountThreshold` major kingdom wars (default 2).
2. The kingdom is in the **Crisis** pressure band, or above the forced-peace exhaustion threshold in legacy mode.
3. The kingdom's average settlement manpower fill is at or below `EmergencyManpowerThresholdPercent` (default 25%).

When active:

- Forced peace uses `EmergencyMinWarDays` (default 15) instead of the normal 40-day minimum.
- The early-war council peace-vote penalty fades out on the same emergency schedule, so both peace paths use the same war-age window.
- If the emergency minimum is greater than or equal to the normal minimum, the relief path is effectively disabled.

This preserves the default anti-whiplash war-duration rule for ordinary conflicts while giving shattered kingdoms a way to escape multi-front death spirals.

---

## 16. Truce Enforcement

After any peace is made (via kingdom decision or forced peace), both kingdoms enter a **truce period** lasting `ForcedPeaceTruceDays` days. During this truce:

- AI cannot add new `DeclareWarDecision` against the former enemy
- If they try, it is blocked at `Kingdom.AddDecision` level
- Support scores for war declarations are hard-set against the war outcome

The truce is registered via **three mechanisms** for belt-and-suspenders reliability:
1. Harmony Postfix on `MakePeaceAction.Apply`
2. Harmony Postfix on `MakePeaceAction.ApplyByKingdomDecision`
3. Native `CampaignEvents.MakePeace` listener

If any one mechanism is skipped (e.g., another mod's Prefix short-circuits the pipeline), the others still record the truce.

Active truces are visible in the overlay's **Wars** tab.

## 17. Combat Realism

Two patches improve autoresolve and wound fidelity for higher-tier troops. They are **not** independent — they multiply, so both read their curve from one place, `B1071_CombatRealismTuning`, selected by the `Combat Realism → Elite survivability preset` setting (0–3, default **1 — Light**).

### Why one preset

- **Tier Armor Simulation** (`B1071_TierArmorSimulationPatch`, Postfix on `DefaultCombatSimulationModel.SimulateHit`) reduces simulated hit damage, which lowers how **often** the fatal-hit gate `MBRandom.RandomInt(MaxHitPoints) < damage` fires.
- **Tier Survivability** (`B1071_FatalityPatch`, Postfix on `DefaultPartyHealingModel.GetSurvivalChance`) raises survival chance, which decides **kill vs. wound** once the gate has fired.

Tuned separately they double-dip: a T6 troop took a quarter fewer casualty checks *and* survived far more of the ones it got, which is what produced the "elite troops are unkillable" reports before v1.0.2.5. Sharing a preset makes that impossible.

### Preset curves

| Preset | Damage reduction (T3/T4/T5/T6+) | Survival bonus (T3/T4/T5/T6+) |
|--------|--------------------------------|-------------------------------|
| 0 — Vanilla | untouched | untouched |
| 1 — Light *(default)* | −3 / −6 / −9 / −12% | +2 / +4 / +6 / +8pp |
| 2 — Moderate | −5 / −10 / −15 / −20% | +3 / +6 / +9 / +12pp |
| 3 — Strong | −6 / −12 / −18 / −24% | +5 / +10 / +15 / +20pp |

Preset 3 reproduces the pre-v1.0.2.5 values. T1–T2 receive nothing at any preset — both lookups return `0f` and the patch returns early. The survival bonus is additive on top of vanilla's Medicine-based base rate and capped at 100%.

Heroes are unaffected by both systems: their damage path uses `AddHeroDamage` rather than the single-hit gate, and `AddFactor(50f)` already puts their base survival near 100%. Both patches activate only in autoresolve simulation (not live battles), and both apply symmetrically to player and AI.

### Teaching the AI what the curves are worth (v1.0.3.7)

Applying both curves to player and AI alike is parity of **effect**. Until v1.0.3.7 there was no parity of **knowledge**: the AI still priced troops at vanilla worth.

Vanilla values a troop purely by tier. `DefaultMilitaryPowerModel.GetDefaultTroopPower` returns `(2 + tier) * (10 + tier) * 0.02f` — a fixed curve where a T6 is worth 3.9× a T1 — and Campaign++ never changes a troop's tier, only how much punishment that tier absorbs. So a lord fielding elite veterans that the two curves above make markedly harder to kill was still being scored as though they were vanilla troops: declining engagements he would now win, and attacking elite garrisons he should have avoided.

`B1071_TroopPowerValuationPatch` (Postfix on `DefaultMilitaryPowerModel.GetDefaultTroopPower`) closes that gap.

**Why this method and not `GetPowerOfParty`.** Every strength comparison in the campaign funnels through `GetDefaultTroopPower`: `PartyBase.EstimatedStrength`, `PartyBase.GetCustomStrength`, `MobileParty.GetTotalLandStrengthWithFollowers`, `Army.EstimatedStrength` and `Kingdom.CurrentTotalStrength` all reach `MilitaryPowerModel.GetPowerOfParty`, which calls `GetTroopPower`, which calls `GetDefaultTroopPower`. Correcting the single innermost method fixes engage-vs-avoid, siege target scoring, army formation and the diplomacy war calculus at once, with no risk of double counting. Patching `GetPowerOfParty` instead would miss `GetTroopPower`'s other callers and would have to re-derive the per-troop tier the roster loop already holds. `PartyBase` caches the result against `MemberRoster.VersionNo`, so the postfix runs only when a roster actually changes.

**Why the multiplier is derived, not tabled.** `B1071_EconomyMath.PowerFactor` computes from the same two preset curves that make the troop durable, so the valuation cannot drift away from the behaviour it describes. A troop enters the fatal-hit gate less often as its damage taken falls (`ArmorFactor`) and survives more of the gates it does enter (`SurvivalBonus`), so the two compound into a death rate of `(1 + armor) × (1 - survival)`; the reciprocal is how much longer the troop lasts. Only half of that gain becomes visible power — tougher troops live longer but do not hit harder — which is the `PowerDamping` constant.

**Why the curve is centred rather than inflationary.** This is the subtle part. A handful of vanilla decisions compare power against *hard-coded absolute constants* calibrated to the vanilla scale: `DefaultArmyManagementCalculationModel.CanLordCreateArmy` requires a summed `GetCustomStrength` of **1000**, and `DefaultDiplomacyModel` refuses to declare war below a `CurrentTotalStrength` of **500**. The AI's combat logic only needs the *ratio* between tiers to be right, but those gates read the *absolute* value — so scaling every tier upward would quietly change how often kingdoms raise armies and declare war, a far larger behavioural change than the one intended. `PowerFactor` therefore holds tier 3, the usual mid-point of a lord's roster, at exactly vanilla and moves the other tiers around it.

| Preset | T1 | T2 | T3 | T4 | T5 | T6+ | Total power of a representative roster |
|--------|----|----|----|----|----|-----|-----------------------------------------|
| 0 — Vanilla | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | unchanged |
| 1 — Light *(default)* | 0.975 | 0.975 | 1.000 | 1.027 | 1.057 | 1.089 | +0.9% |
| 2 — Moderate | 0.959 | 0.959 | 1.000 | 1.046 | 1.100 | 1.161 | +1.6% |
| 3 — Strong | 0.943 | 0.943 | 1.000 | 1.067 | 1.149 | 1.248 | +2.5% |

The final column is measured against a 100-man roster of 20/30/25/15/8/2 across T1–T6 — a lord's usual low-heavy shape. `TroopPowerMathTests.TotalPartyPowerStaysNearVanillaForARepresentativeRoster` asserts that drift stays inside ±5% at every preset; widening that band would silently move the army and war thresholds, and nothing else in either suite would catch it.

Preset 0 returns exactly `1f` for every tier, so a player on Vanilla survivability gets bit-identical AI behaviour. Heroes are skipped: `GetDefaultTroopPower` prices them off `Hero.Level` rather than `Tier`, and neither combat curve moves a hero's survivability.

The retired `EnableTierSurvivability` / `EnableTierArmorSimulation` booleans still exist on `B1071_McmSettings` under the **Legacy** group so older configs deserialize, but nothing reads them.

---

## 18. Army Economics

Historically, recruiting and maintaining veteran soldiers was far more expensive than vanilla Bannerlord reflects. This system scales gold costs exponentially by tier.

### Hire & Upgrade Costs

Four presets control how much more expensive higher-tier troops are to recruit (one-time fee):

| Tier | Vanilla  | Light (+%) | Moderate (+%) | Severe (+%) |
|------|----------|------------|---------------|-------------|
| T1   | 10–20g   | ±0%        | +10%           | +25%         |
| T2   | 50g      | +15%       | +30%           | +75%         |
| T3   | 100–200g | +35%       | +75%           | +175%        |
| T4   | 200–400g | +65%       | +150%          | +350%        |
| T5   | 400–600g | +100%      | +250%          | +600%        |
| T6   | 600–1500g| +150%      | +400%          | +1000%       |

Default: **Moderate**. Hire cost scaling automatically cascades into upgrade gold cost (vanilla computes upgrade cost as the difference in recruit costs divided by 2).

**Foreign recruitment premium (v1.0.2.6):** recruiting in a settlement whose owner's faction is not the buyer's own faction applies a further `AddFactor` on top of the tier factor, driven by `ForeignRecruitCostPreset` (0 = off, 1 = 1.5×, 2 = 2× *(default)*, 3 = 3×). You are outbidding the local lord for men who owe him service and owe you nothing.

Those multipliers are stated against the **base** game price, not against what the player already pays. `ExplainedNumber` sums its factors rather than compounding them, so the tier factor and the foreign factor land on the same number: `final = base × (1 + tierFactor + foreignFactor)`. The tier factor climbs steeply while the foreign factor is flat, so the *effective* premium falls away as tier rises. At the shipped defaults (Moderate hire, foreign preset 2):

| Tier | Base | At home | Abroad | Effective |
|------|------|---------|--------|-----------|
| T1   | 10g  | 11g     | 21g    | ×1.91 |
| T2   | 50g  | 65g     | 115g   | ×1.77 |
| T3   | 100g | 175g    | 275g   | ×1.57 |
| T4   | 200g | 500g    | 700g   | ×1.40 |
| T5   | 400g | 1400g   | 1800g  | ×1.29 |
| T6   | 600g | 3000g   | 3600g  | ×1.20 |

That shape is intended rather than an accident of the arithmetic. Foreign recruiting in practice means village volunteers, which is exactly where the premium bites hardest. An elite troop is already priced out of casual mass-hiring by the tier factor alone, and stacking a full doubling on top of it would put a T6 hire abroad at 6000g — which removes the option instead of pricing it. A player who wants the flat doubling to hold at the top end can raise the preset to 3.

The **manpower** cost is deliberately unchanged. Manpower is a property of the settlement, not of the recruiter, so charging a foreigner extra manpower would drain the host settlement faster and punish that kingdom for the visitor's behaviour — the wrong party pays. Gold is a property of the recruiter, so gold is the correct lever, and it also bites the "player is too rich by the midgame" complaint.

**Tavern mercenaries and caravan guards are exempt everywhere**, on `CharacterObject.Occupation` (`Mercenary` / `CaravanGuard`; every settlement volunteer is `Soldier`). The premium models outbidding a settlement's own lord for men who owe him service — a wandering mercenary owes nobody anything, so there is no lord to outbid and no reason his price should depend on whose town he is in. Testing the *troop* rather than a call-path flag is deliberate: vanilla prices a tavern hire at three separate call sites (the recruitment UI / dialogue, the AI's affordability pre-check in `CheckRecruiting`, and the charge inside `ApplyInternal`) and only the last passes through the existing `IsProcessingTavernMercenary` flag, so a flag-based exemption would have shown the player one price and taken another. This sits alongside the occupation normalisation below, which already cancels vanilla's 3× occupation surcharge for the same troop types. Caravan parties are excluded a second time on `MobileParty.IsCaravan` — a caravan buys nothing but tavern guards, so the occupation test already covers it, but a caravan trades abroad by design and must never be taxed for being where it belongs.

Exempt only when the clan has no kingdom *and* no fief, so the early game is untouched. A landless vassal or mercenary pays the premium like anyone else — `MapFaction` already resolves to the kingdom they serve, so a Vlandian vassal recruiting in Sturgia is correctly foreign whether or not he holds a fief. The buyer's location is resolved as `buyerHero.CurrentSettlement ?? buyerHero.PartyBelongedTo?.CurrentSettlement`; if neither resolves, no premium is applied. Applies to AI and player alike. Settlements of factions you are **at war with** are blocked entirely rather than priced — see §6.

**Occupation normalisation:** Vanilla makes Mercenaries, Gangsters, and CaravanGuards 3× more expensive to hire than regular soldiers. This mod cancels that premium exactly so all troop types cost the same as a regular soldier of the same tier. The tier-scaling still applies; only the occupation surcharge is removed.

### Daily Wages

Four presets control how much more veterans demand per day:

| Tier | Vanilla | Light  | Moderate | Severe |
|------|---------|--------|----------|--------|
| T1   | 2d/day  | 2d     | 2d       | 2d     |
| T2   | 3d/day  | 3d     | 3d       | 4d     |
| T3   | 5d/day  | 6d     | 8d       | 9d     |
| T4   | 8d/day  | 11d    | 16d      | 20d    |
| T5   | 12d/day | 20d    | 31d      | 42d    |
| T6   | 17d/day | 34d    | 60d      | 85d    |

Default: **Severe**. 100 T6 troops at Severe cost 8500 denars/day. Heroes and companions are excluded.

**Caravan Guards wage exemption:** CaravanGuards are excluded from wage inflation entirely — they keep vanilla wages so caravan businesses remain profitable at all presets.

**Mercenary wage:** Mercs retain vanilla's 1.5× daily wage on top of the tier factor — expensive to maintain. Their hire cost is normalised to match regular soldiers (occupation premium cancelled). Cheap to recruit, expensive to keep.

### Garrison Wage Discount

Garrison parties pay a configurable percentage of field troop wages. Default **80%** (20% discount). Historically, garrison soldiers received reduced coin pay supplemented by shelter, rations, and equipment from the local lord.

The discount applies to the aggregate party wage total and stacks on top of vanilla's existing perk- and building-based garrison reductions. Togglable in MCM; the percentage is adjustable (10–100%).

### Parity and AI interaction

Both patches apply to ALL parties equally — player and AI. The AI does not recruit blindly:
- Party gold must exceed `50 + partySize × 20g` before AI starts recruiting at all
- Each volunteer hire is individually gated: party gold must exceed hire cost AND daily wage budget must have room for the new daily wage
- At Moderate/Severe, the AI will sustain significantly fewer T5–T6 troops because their combined wage bill exhausts the party’s wage budget

---

## 19. The Overlay

Press **M** (or configured hotkey) to toggle the Byzantium 1071 Overlay — a tactical intelligence panel embedded into the campaign map bar.

### Tabs

All 14 tabs use a 5-column layout. Click any column header to sort; click again to reverse.

| Tab | What it shows |
|-----|---------------|
| **Current** | Manpower pool, regen, war pressure band, and recovery status for the selected/nearest settlement |
| **Nearby** | All towns/castles sorted by distance, with manpower and % fill |
| **Castles** | All castles, sorted by manpower, prosperity, or regen |
| **Towns** | All towns, same |
| **Villages** | All villages with hearth, faction, and bound pool |
| **Factions** | Faction \| Ruler \| Treasury \| Manpower \| Prosperity (CK3-style order) |
| **Armies** | Per-faction military power (Σ tier²×count), troop count, and war exhaustion |
| **Wars** | Active wars with exhaustion, peace pressure, territory counts (e.g., "9 vs 15"), and active truces |
| **Rebellion** | All towns sorted by rebellion risk score, with L/S/Food stats, time-to-rebel estimate, and culture mismatch |
| **Prisoners** | All imprisoned nobles — captor, holder, location |
| **Clans** | Clan loyalty/defection risk and recruitment opportunity scores |
| **Characters** | All live heroes and wanderers with approximate locations and relation symbol (♥ ▲ ● ▼ †) |
| **Search** | Free-text search across heroes, settlements, armies, clans, kingdoms, and **trade good / food prices** at every town |
| **Casualties** | Cumulative battlefield deaths by kingdom pair since the ledger became active, with total losses and kill ratio |

### Performance

- Settlement row cache rebuilds once per in-game day (not every 2 seconds)
- Distance calculations use tiered updates: top 30 closest settlements update every 2s, full recalc every 30 real seconds
- Character caches rebuild on daily tick
- Armies cache rebuild on daily tick
- All list operations use scratch lists to avoid GC allocation per frame
- Tab labels are resolved once during initialization and cached (eliminates ~26 TextObject allocations per 2-second sync cycle)

### Visual Polish

- **Zebra striping**: Alternating overlay rows display a subtle white overlay (5% alpha) for readability.
- **Player-faction highlight**: Rows belonging to the player's faction are highlighted in gold (12% alpha).
- **Tooltips**: Truncated cell text shows full content on hover via Bannerlord's native `HintWidget`.
- **Dynamic panel height**: Panel height adapts to the MCM `OverlayLedgerRowsPerPage` setting — formula: `200 + (rows × 20)`.
- **Keyboard navigation**: Left/Right arrow keys cycle tabs (wraps around). Disabled on Search tab to allow text editing.
- **Font-safe glyphs**: All text uses glyphs verified to exist in Bannerlord's embedded font (dagger † instead of ⚔, em-dash — instead of box-drawing ──).

### Troop Service and prisoner selection presentation

The Troop Service counter remains `max(0, today - JoinDay)`. The UI labels it **Service Days** and explains that extensions and promotion credits can reduce it, so it is neither biological age nor lifetime service. The header and row reserve 110 units for this column. A fixed 20-unit header bounds its stretching HintWidget; using CoverChildren here lets the tooltip expand the header and displace the 410-unit roster viewport. The viewport retains 13 complete 31-unit row slots and clipping. Prisoner selection assigns alternate-row flags before binding and uses matching gold headings without changing quantity controls or conversion rules.

### Ledger layout and submitted search

`B1071_FullScreenLedger` adds a focused Gauntlet layer using the existing MapBarVM. Its layout derives from the compact table markup, uses 20-unit row text and 28-unit rows, and leaves a 16-unit screen margin on each edge. Logical dimensions account for the UI context's inverse scale. Pagination reserves 248 units of chrome plus 42 for Search; a 1080-unit viewport therefore displays 28 ordinary rows or 27 search results. Widened column profiles share spare width, with Current keeping related values together and separating its gated diagnostics. Compact mode retains its configured row count. Switching modes preserves the tab, sort, submitted search and the page containing the previous first entry.

Open/close requests are processed from the overlay tick, outside Gauntlet button dispatch. Escape closes the full-screen layer after its press frame. The movie releases before layer removal, without finalizing the map-owned VM; castle recruitment uses the same release-before-remove order for its owned movie. Search receives normal space input in the focused full-screen layer. Both modes expose Close, and keyboard navigation follows the rendered tab order. Numeric comparisons consistently obey the sort direction; Characters starts nearest first, the Casualties indicator maps to its actual comparator, and truce time uses the Duration column. Search market details label price and stock, and its summary names the remaining categories as Others.

In compact mode, the ledger retains its 1200-unit outer width, configured offsets, `200 + rows × 20` panel height, and 20-unit rows. The inner stack runs top to bottom: navigation, title, optional search toolbar, table, totals, and footer. Row collections remain reversed for the bottom-to-top row list. Current inserts its settlement row first visually and suppresses the duplicate totals; telemetry remains under its existing gate.

`B1071_LedgerColumns` assigns widths and alignment per tab within 1152 units, including four 24-unit gutters. Heading, data, and additive total columns share those widths. Mixed summaries use one full-width, explicitly labeled strip: combined casualty deaths are not placed under Kills A, and army exhaustion is labeled as an arithmetic average out of 100 rather than a pressure band. Current status and telemetry rows use a separate 180-unit label and the remaining width for details, retaining the existing telemetry gate and row height. Long row values remain intact in the controller and in `HintViewModel`; displayed text uses a conservative width-based character budget and clips to its cell. Row and totals tooltips expose the full values. Header tooltips explain abbreviated concepts and faction-side ordering. Pagination tracks actual page count, disables endpoints, and clamps after results shrink; ordinary ledgers show an entry range, while Wars retains its mixed war/truce pagination.

Alignment deliberately does not use enum data-source properties. In the installed Bannerlord binding API, unsupported value types fall through to a generic notification method constrained to reference types. Registering a `TextHorizontalAlignment` data source can therefore fail during map UI creation despite a successful C# build. Boolean visibility selects text widgets with literal Left/Right XML alignment instead. `LedgerUiContractTests` checks supported binding types, XML bindings, column budgets, search state, and pagination boundaries.

`B1071_LedgerSearchState` separates draft text from the submitted query. Enter or Search submits; typing only updates the draft. Category selection filters the cached match list without rescanning the world. Sorting and paging retain the submitted query. Clear resets both query and category. Full matches refresh on explicit submission and daily cache invalidation; displayed positions and prices are a snapshot between refreshes. Search no longer rebuilds its bound rows on every periodic overlay refresh. This reduces work during text editing; it is not a claim that the historical native Search crash has been reproduced or solved.

Rebellion outlook describes the existing estimate without implying a scheduled revolt. `FormatRebellionOutlook` displays Rebellious for the game's unrest state, Low loyalty at or below 25, No decline when the current trend projects no crossing, and an approximate day count otherwise. The calculation and rebellion mechanics are unchanged. Ledger hints retain the native tooltip appearance and fade behavior; a faint screenshot alone does not establish an opacity defect.

### Hotkey note

Letter hotkeys (M, N, K) are suppressed while the Search tab is active to prevent search query input from triggering the toggle.

---

## 19A. Settlement Intelligence Tooltips

Campaign++ reports settlement manpower and devastation on two campaign-map surfaces: the Sandbox nameplate, and the full settlement tooltip the game already shows on map hover. `B1071_SettlementNameplateVMMixin` targets `SettlementNameplateVM.RefreshBindValues`, and the three `SettlementNameplateItemSmall/Medium/Large` prefabs receive `Command.HoverBegin` and `Command.HoverEnd` on their existing `SettlementNameplateCapsuleWidget`. The vanilla camera, alternate encyclopedia click, and nested tracker button therefore retain their original commands.

Both surfaces take their rows from `B1071_SettlementTooltipContent.Build`. They are visible side by side on the map, so any disagreement between them reads as a bug; one shared builder is what makes that disagreement impossible rather than merely unlikely.

### Tooltip data

The mixin creates one `BasicTooltipViewModel` per nameplate. Its callback calls the shared builder only when the hover begins, so the map does not scan every settlement to keep tooltip text current.

- Towns call `B1071_ManpowerBehavior.GetManpowerPool` for current/max values and use `GetDailyRegen` for the effective daily recovery. They also query `B1071_DevastationBehavior.GetAverageBoundVillageDevastation`.
- Castles show current/max and average devastation. The recovery row is omitted rather than printing `GetDailyRegen`, because the daily tick converts that raw request into `localTrickle + supplyTransfer`, clamps the transfer to the supply town's available pool, and has a separate cut-off branch.
- Villages call the existing pool resolver through `GetManpowerPool`, so `Village.Bound` supplies the displayed regional pool. The label includes the resolved pool's name to make the shared value explicit. A null pool is accepted for orphan villages, while `GetDevastation(Village)` still supplies the village row.
- A pool at or above its maximum is labelled **Full** and does not receive a recovery-rate row.

Every row is built with `textHeight` 0, and that value is load-bearing rather than incidental. `TooltipPropertyWidget` sets its `IsTwoColumn` flag only when the definition and value are both non-empty **and** `TextHeight == 0`; any other height sends the row down the `!IsTwoColumn` branch of `RefreshSize`, which gives both label containers the same width while `SetBattleScope` leaves the definition right-aligned and the value left-aligned. The two labels then meet in the middle and render with no gap at all — `Manpower1685/2499` rather than `Manpower    1685/2499`. Native passes 0 for ordinary rows too: `PropertyBasedTooltipVM.AddProperty` defaults `textHeight` to 0 and reserves non-zero heights for multi-line and description rows. Nothing in Campaign++ references `IsTwoColumn`, so a rename on the TaleWorlds side would still compile and silently revert the layout; `TooltipTwoColumnLayoutContractStillHolds` in the game-backed suite pins the two native members the assumption rests on.

The `EnableSettlementNameplateTooltips` MCM property gates all three outputs: the nameplate tooltip, the vanilla-tooltip section, and the marker. It is a new setting key with a default of `true`; no migration block is needed because MCM cannot deserialize the retired setting's key into the new property. Its player-facing label reads **Enable settlement tooltips** rather than naming the nameplate, because the toggle no longer drives only that surface; the property name is deliberately left alone so existing MCM configs and submods built against `IB1071Settings` keep working. Devastation rows and markers also respect `EnableFrontierDevastation`.

### Vanilla settlement tooltip

`B1071_SettlementTooltipRefresher` adds the section by wrapping the `Settlement` tooltip registration rather than by patching a refresher method. `InformationManager` keeps one refresher delegate per registered type in a plain `Dictionary<Type, TooltipRegistry>`, and `RegisterTooltip` is a bare assignment into it, so the last module to call it owns that type outright. `SandBox.View` registers `TooltipRefresherCollection.RefreshSettlementTooltip` for `Settlement` at module load; the War Sails view module then overwrites that entry with `NavalTooltipRefresherCollection.RefreshSettlementTooltip`, a verbatim copy of vanilla's method that never calls vanilla's. A Harmony postfix on vanilla's method was the first attempt here, and it is the failure this design exists to avoid: the postfix attached, the launch check reported it attached, nothing threw, and no section ever appeared, because the game had stopped calling the method it was attached to. That is the hardest failure shape to read from a log, since every diagnostic agrees the patch is fine.

`Install()` reads the current entry, keeps its `OnRefreshData` as the inner call, and re-registers itself with the same `MovieName` and tooltip type. What it wraps is whatever it finds: vanilla's refresher on an install without War Sails, War Sails' copy where that DLC is present, another mod's if one registered later still. No reference to any DLC or mod assembly is involved and there is nothing to detect — the wrapper does not care who wrote the entry it wraps, which is the property a detection-based approach could never have. It logs the wrapped refresher's declaring type, because which module owns the registration is the whole question and is invisible from outside.

It is installed from `SubModule.OnGameStart`, which runs long after every module's `OnSubModuleLoad`, so the entry being wrapped is the one the game will actually use. The wrapper is a single static delegate instance for the life of the process, so a second call recognises its own registration by reference and returns instead of wrapping the wrapper and adding the section twice. `Uninstall()` in `OnSubModuleUnloaded` puts the inner refresher back, and only while the registration is still the wrapper's: if another mod has registered over it since, that entry is theirs and restoring the old inner would silently drop it. `InstallWrapsWhicheverRefresherIsRegisteredAndUninstallPutsItBack` in the game-backed suite pins the whole round trip.

The settlement arrives as `args[0]` and is taken with a type-pattern test rather than an unchecked cast, because the vanilla body's own `args[0] as Settlement` shows the array is untyped by contract. The inner refresher is called outside the try/catch that guards the Campaign++ rows: swallowing an exception from the game's own tooltip there would turn a game-side failure into a half-built tooltip that reports nothing and says nothing.

The rows are preceded by vanilla's own section break — an empty spacer row at height `-1`, a header row whose value is a single space, then a `RundownSeperator` — which is the same three-row idiom vanilla uses between Owner, Information, and Defenders. The header text is the literal `{=!}Campaign++`: `{=!}` is the marker vanilla uses for text no language pack translates, and the mod's name is identical in every pack, so this deliberately carries no `b1071_` text ID for translators to chase.

The section is inserted above the trailing spacer rather than appended, so it reads as the last block of settlement data instead of sitting below **Hold 'Alt' for more info.** and the parley hint. The wrapper runs after the inner refresher has built the list, but nothing obliges it to add at the end: `TooltipPropertyList` is an `MBBindingList<TooltipProperty>`, whose `InsertItem` override raises `ItemAdded` with the index, so an insert notifies the bound widget exactly as an append does.

`FindFooterIndex` finds the insertion point by row shape rather than by text. Vanilla closes `RefreshSettlementTooltip` with `AddProperty(string.Empty, string.Empty, -1)` and then adds the **Hold 'Alt'** hint and the parley hint, each with an empty definition but a non-empty value. A row with an empty definition, an empty value and a negative `TextHeight` is therefore the spacer and nothing else. The scan runs backwards because vanilla uses the same spacer between sections, and only the last one is the footer. Finding the hint by its localised text was the alternative and is what kept the section at the bottom before: it fails in every language the mod does not ship. If the footer is ever restructured away the scan finds nothing and returns the end of the list, which is where the section used to go — a worse position, not a broken one.

The first settlement tooltip built after installing logs one line through `LogFirstOutcome` naming what the wrapper did: rows inserted and where, the toggle switched off, no settlement in the arguments, or nothing for the builder to report. A section that fails to appear looks identical from the outside in all of those cases, and the try/catch around the rows only speaks when something throws — which is precisely how the retired postfix stayed invisible.

The retired patch also carried a hazard worth keeping on record, independent of the registration problem. Harmony JIT-compiles a target method the moment it patches it, and the JIT runs the `beforefieldinit` type initializers of every type that method references. `RefreshSettlementTooltip` reads `CampaignUIHelper.MobilePartyPrecedenceComparerInstance` and calls `CampaignUIHelper.IsSettlementInformationHidden`, and `CampaignUIHelper` initializes its `_partyMoraleStr` field with `GameTexts.FindText("str_party_morale")`. At module load no `Game` exists, `GameTexts`'s private `_gameTextManager` is still null, and that call throws a `NullReferenceException` inside `TaleWorlds.Core`. The CLR caches a failed type initializer for the life of the process, so every later use of `CampaignUIHelper` — the map's own settlement tooltips among them — threw `TypeInitializationException`, and the campaign crashed a few seconds after the save finished loading. A patch's own try/catch is no defence against this, because the failure happens at patch time rather than inside the patch. Wrapping a registration JIT-compiles nothing and cannot reach it. The general rule survives the patch that taught it: a target whose types reach game text, campaign state, or any other post-`Game.Initialize()` singleton from a static initializer must not be patched in `OnSubModuleLoad`.

### Ambient marker and event safety

Each size inserts a small `▲` marker into the existing `TopSideIcons` list at a fixed devastation threshold of 50. The parent already has `DoNotAcceptEvents="true"` and `DoNotPassEventsToChildren="true"`; the marker also declares `DoNotAcceptEvents="true"`.

Each size registers exactly one movie name. UIExtenderEx resolves movies by bare file name — `WidgetPrefabPatch.ProcessMovie` calls `Path.GetFileNameWithoutExtension` before looking the movie up — so a directory-qualified name such as `Nameplate/SettlementNameplateItemSmall` can never match and registering both forms would be dead weight. Because only one key resolves and every processing pass starts from the pristine prefab XML, duplicate insertion is not reachable and no injection guard is needed.

The marker markup is therefore a constant. `PrefabComponent.ProcessMovieIfNeeded` re-reads an insert patch's content on **every** prefab load, not once at registration, so a getter that returned different markup on a second read would inject an empty widget and silently drop the marker for the rest of the session after any prefab reload.

No new save keys, persistent scene objects, map artwork, settlement-menu property tooltip changes, encyclopedia tooltip changes, or ledger changes are part of this feature. The vanilla settlement tooltip is added to, never rewritten — no row vanilla builds is edited, reordered, or removed.

---

## 20. Configuration

All settings are in the Mod Configuration Menu. Key groups:

| Group | Key Settings |
|-------|-------------|
| Pool Sizes | Base max for town/castle/other; tiny pool testing mode |
| Pool Scaling | Prosperity normalizer, min/max prosperity scale, hearth multiplier |
| Regen | Per-type daily regen %, soft cap, stress floor, hard cap, min daily, castle min daily, castle supply chain toggle |
| Regen Modifiers | Security/food/loyalty/siege/governor scales |
| Depleted Emergency Regen | Enable toggle, threshold %, bonus at zero |
| Recruitment Cost | Base cost per troop, culture discount, village volunteer tier max, town volunteer tier max |
| Castle Recruitment | Enable/disable; prisoner tier threshold; T4/T5/T6 days and gold costs; elite pool max, regen range, manpower cost; AI recruit toggle |
| Troop Service - Veterans | Veterans return home; manpower returned on discharge; recall gold per tier; retention days; scatter on raid or conquest; allow cross-clan veteran recruitment (default off); recall access when cross-clan recruitment is on (0=any non-hostile, 1=kingdom, 2=clan only, default 2); allow recall from a distance; register hotkey enable and key (F8/F10/F11/F12); courier speed; veteran march speed; AI lords hire veterans |
| Combat Realism | Elite survivability preset (0–3, default 1 — sets autoresolve damage reduction and wound-vs-kill bonus together) |
| Army Economics | Hire & upgrade cost preset (0–3); daily wage preset (0–3); garrison wage % of field (default 80); garrison discount toggle |
| Overlay | Hotkey (M/N/K/F9-F12), panel position, default tab, rows per page |
| War Effects | Enable/disable; raid/battle/siege/conquest drain %; conquest retain % |
| Dynamic Conquest Protection | Enable toggle; depleted retain %, depleted threshold % |
| Delayed Recovery | Recovery days and penalty % for raid/siege/conquest; depleted penalty reduction |
| Bounded Stochasticity | Enable variance; volunteer variance %; recovery variance % |
| Immersion Modifiers | Seasonal regen; peace dividend; culture discount; governor bonus |
| Alerts & Militia | Manpower alert thresholds; militia-manpower link min/max scales |
| War Exhaustion | Enable/disable; decay rate; per-event gain values; regen penalty divisor |
| Diplomacy | War/peace support scaling, pressure bands, cooldowns, truces, early-war voting pressure, and multi-front crisis relief |
| Developer Tools | Debug message toggles, AI logging, telemetry display, verbose mod log, settings profile version |
| Tooltips | Settlement manpower tooltip enable/disable |
| Village Investment | Enable/disable; 3-tier costs, durations, hearth/relation/influence/power bonuses; power cap; cross-clan relation; AI toggle |
| Town Investment | Enable/disable; 3-tier costs, durations, prosperity/relation/influence/power bonuses; power cap; cross-clan relation; AI toggle |

### Quick Settings Tab (v0.2.6.0)

A separate MCM tab (**Campaign++ - Quick Settings**) mirrors all 28 system master toggles in 5 groups:

| Group | Toggles |
|-------|---------|
| Core Systems | War Effects, War Exhaustion, Diplomacy Pressure, Forced Peace at Crisis, Truce Enforcement, Delayed Recovery, Militia Link |
| Economy & Investment | Slave Economy, Village Investment, Town Investment, Minor Faction Economy, Garrison Wage Discount |
| Recruitment & Military | Castle Recruitment, Open Castle Access, Elite Survivability (0–3), AI Lords Seek Campaign++ Recruits, Clan Survival, Troop Service |
| Province & Governance | Governance Strain, Provincial Stabilization, Frontier Devastation, Castle Supply Chain |
| Immersion & Modifiers | Seasonal Regen, Peace Dividend, Culture Discount, Governor Bonus, Overlay (M key), Manpower Alerts |

All entries use `ProxyRef<T>` wrappers around `B1071_McmSettings.Instance` — changing a Quick Settings toggle changes the corresponding full-tab setting and vice versa. Built by `B1071_QuickSettingsFluentSettings` using the same FluentGlobalSettings pattern as the Compatibility tab.

---

## 21. Architecture

### Files

| File | Role |
|------|------|
| `SubModule.cs` | Entry point; Harmony setup; UIExtenderEx; overlay tick; Quick Settings + Compatibility tab registration |
| `B1071_ManpowerBehavior.cs` | ~2600 lines; all gameplay logic: pools, regen, recruitment, war events, exhaustion, diplomacy, truces |
| `B1071_ManpowerMilitiaModel.cs` | Overrides vanilla militia growth model with manpower-ratio scale |
| `B1071_ManpowerVolunteerPatch.cs` | Harmony Postfix on `GetDailyVolunteerProductionProbability` — manpower-gated volunteer production (replaced AddModel for EO compat) |
| `B1071_CastleRecruitmentBehavior.cs` | ~1776 lines; castle recruitment: elite pool + prisoner conversion + AI recruitment + garrison absorption + consignment income + hostile depositor forfeit |
| `B1071_CastleRecruitmentScreen.cs` | Gauntlet screen wrapper for the castle recruitment UI |
| `B1071_CastleRecruitmentVM.cs` | ViewModel for the castle recruitment screen (3-list layout: elite, ready, pending) |
| `B1071_CastleRecruitTroopVM.cs` | ViewModel for a single troop entry in the castle recruitment UI |
| `B1071_CastlePrisonerRetentionPatch.cs` | Harmony Prefix blocks vanilla daily prisoner selling at castles |
| `B1071_CastlePrisonerDepositPatch.cs` | Harmony Prefix on `OnSettlementEntered`; castle branch deposits prisoners into prison (with capacity check); town branch enslaves T1–T3 into slave goods (race condition fix v0.1.7.8) |
| `B1071_PrisonerDonationInfluencePatch.cs` | Harmony Prefix on `InfluenceGainCampaignBehavior.OnPrisonerDonatedToSettlement`; blocks influence when donating party's faction ≠ settlement's faction (v0.1.8.8) |
| `B1071_SlaveEconomyBehavior.cs` | Slave economy: acquisition, market bonuses, decay, game menus; `OnSettlementEntered` transfers slave goods only (prisoner enslavement moved to Prefix in v0.1.7.8) |
| `B1071_SlaveFoodPatch.cs` | Harmony Postfix — slave food consumption ("Slave Upkeep" in food tooltip) |
| `B1071_SlaveConstructionPatch.cs` | Harmony Postfix — slave construction bonus ("Slave Labor" in construction tooltip) |
| `B1071_SlaveProsperityPatch.cs` | Harmony Postfix — slave prosperity bonus ("Slave Labor" in prosperity tooltip) |
| `B1071_GovernanceBehavior.cs` | Provincial governance strain tracking and decay |
| `B1071_GovernanceLoyaltyPatch.cs` | Harmony Postfix — governance strain loyalty penalty |
| `B1071_GovernanceProsperityPatch.cs` | Harmony Postfix — governance strain prosperity penalty |
| `B1071_GovernanceSecurityPatch.cs` | Harmony Postfix — governance strain security penalty |
| `B1071_VillageInvestmentBehavior.cs` | Village patronage: 3-tier investment, hearth bonus state, AI investment, game menus, cross-clan diplomacy |
| `B1071_VillageInvestmentHearthPatch.cs` | Harmony Postfix — patronage hearth growth bonus ("Patronage" tooltip line) |
| `B1071_TownInvestmentBehavior.cs` | Town civic patronage: 3-tier investment, prosperity bonus state, AI investment, game menus, cross-clan diplomacy |
| `B1071_TownInvestmentProsperityPatch.cs` | Harmony Postfix — civic patronage prosperity growth bonus ("Civic Patronage" tooltip line) |
| `B1071_DevastationBehavior.cs` | Frontier devastation tracking, decay, and dynamic EO food model compat |
| `B1071_DevastationFoodPatch.cs` | Harmony Postfix — devastation food penalty |
| `B1071_DevastationHearthPatch.cs` | Harmony Postfix — devastation hearth growth penalty |
| `B1071_DevastationProsperityPatch.cs` | Harmony Postfix — devastation prosperity penalty |
| `B1071_DevastationSecurityPatch.cs` | Harmony Postfix — devastation security penalty |
| `B1071_MinorFactionIncomePatch.cs` | Harmony Postfix — minor faction frontier revenue |
| `B1071_AiRecruitmentManpowerGatePatch.cs` | Harmony Prefix on AI recruitment |
| `B1071_GarrisonAutoRecruitManpowerPatch.cs` | Harmony Postfix caps garrison auto-recruit |
| `B1071_PlayerRecruitmentManpowerGatePatch.cs` | Harmony Prefixes on player recruit single/all/done |
| `B1071_PlayerRecruitmentUiStatePatch.cs` | Harmony Postfixes refresh UI availability flags |
| `B1071_TierArmorSimulationPatch.cs` | Harmony Postfix on `SimulateHit` for tier armor damage reduction |
| `B1071_ArmyEconomicsPatch.cs` | Harmony Postfixes on `GetTroopRecruitmentCost` and `GetCharacterWage` for tier-exponential costs |
| `B1071_FatalityPatch.cs` | Harmony Postfix on `GetSurvivalChance` for tier survivability |
| `B1071_ExhaustionDiplomacyPatch.cs` | 5 Harmony patches for war/peace decisions + truce registration |
| `B1071_VerboseLog.cs` | Static helper class — centralized `[Byzantium1071][Subsystem] message` logging to rgl_log |
| `B1071_McmSettings.cs` | MCM settings definitions (~200+ settings) + version-gated settings migration system |
| `B1071_OverlayController.cs` | ~3700 lines; all overlay tab logic, caching, pagination, sorting, search, 5-column layout |
| `B1071_MapBarPanelUIExtender.cs` | UIExtenderEx prefab injection + ViewModel Mixin (5 column TextWidgets) |
| `B1071_LedgerRowVM.cs` | ViewModel for a single 5-column overlay row |

### Key design principles

1. **Null-safe throughout**: every external game object access uses null-conditional operators or explicit null guards
2. **Fail-open**: if the mod's behavior instance is unavailable (e.g., not in campaign), all patches return `true` (allow vanilla) or skip
3. **No asymmetric cheating**: the same manpower rules apply to player and AI equally
4. **Serialization-safe**: all mod state is serialized via `SyncData` to campaign save/load
5. **Exception dedup in tick**: the overlay tick wraps `B1071_OverlayController.Tick` in per-type exception deduplication to avoid log spam if an edge case fires every frame
6. **Settings migration**: version-gated hard migration ensures balance retuning reaches existing users, not just fresh installs

---

## 22. Verbose Debug Logging

**MCM setting: `Enable verbose mod log`** (Developer Tools group) — a master switch that logs all mod activity to Bannerlord's `rgl_log` file. Superset of every individual debug toggle (`LogAiManpowerConsumption`, `TelemetryDebugLogs`, `DiplomacyDebugLogs`). Performance cost — disable for normal play.

When enabled, the following is logged:

| System | Events logged |
|---|---|
| **Manpower** | Session start, pool seeding, daily regen per settlement, consumption (player + AI with full context), recovery penalties |
| **War exhaustion** | Every gain (raid/siege/battle/conquest/noble capture with amount + source), daily decay per kingdom, pressure band transitions |
| **Diplomacy** | Forced peace attempts (all skip reasons + successful), truce registration, peace events, war-gate blocks, DeclareWar/MakePeace support bias |
| **Slave economy** | Raid captures (all parties), daily bonuses per town, AI deposits, town enslavement (AI lords), player enslavement, slave decay/attrition, manumission (cap overflow → MP), initial stock seeding, daily price snapshots, caravan trade events |
| **Prisoners** | Castle deposits (party, count, room remaining) |
| **Garrison** | Auto-recruit capping (when manpower < vanilla recruit count) |
| **Devastation** | Village loot devastation changes |
| **AI recruitment** | Manpower gate blocks (when verbose OR `LogAiManpowerConsumption` is ON) |
| **Clan Survival** | Rescue attempts, commits and aborts; eligibility skips; rebel normalization; kingdom detach; war cleanup; tracking start/stop; destruction-pipeline diagnostics — logged to both rgl_log and session file |
| **AI telemetry** | One digest per campaign day per subsystem — troop power, AI recovery routing, settlement visits, demobilization extensions — plus a CSV row; also emitted when `TelemetryDebugLogs` alone is on (see below) |

All logging is centralized through the `B1071_VerboseLog` static helper class with format `[Byzantium1071][Subsystem] message`. ClanSurvival events additionally write to the session file log via `B1071_SessionFileLog.WriteTagged` for post-session analysis.

### Daily AI telemetry (v1.0.3.8)

**MCM setting: `Telemetry debug logs`** (Developer Tools group), or `Enable verbose mod log`, which is a superset. With both off, none of the work below is done.

Releases 1.0.3.4-1.0.3.7 changed AI decisions that only become visible over dozens of campaign days and across hundreds of parties. `B1071_TroopPowerValuationPatch` logged nothing; `B1071_AiRecoveryBehavior` logged one line per session. This subsystem exists to make those changes checkable in one play session instead of several.

**Why aggregate and not trace.** `B1071_AiRecoveryBehavior.OnAiHourlyTick` fires once per lord party per campaign hour — roughly 200 x 24 on a mature map — and `GetDefaultTroopPower` runs once per troop per party per strength query. Per-event lines would cost more than the systems they measure and would bury the signal. So the measured sites only increment an int in `B1071_TelemetryCounters` (the same shape as `B1071_SessionAudit`), and `B1071_TelemetryBehavior` emits once on `DailyTickEvent`.

**What the daily snapshot walks.** Every AI lord party once, summing non-hero roster power twice: at vanilla tier pricing (`B1071_TelemetryMath.VanillaTroopPower`, a deliberate second copy of `(2 + tier) * (10 + tier) * 0.02f`) and with `B1071_CombatRealismTuning.GetPowerFactor` applied. The pair is the point — the game exposes no "what would this have been" value to read back, and either number alone says nothing. Beside them go the count of armies in the field and the number of wars declared that day. Those two are not decoration: `DefaultArmyManagementCalculationModel.CanLordCreateArmy` gates on a summed strength of **1000** and `DefaultDiplomacyModel` refuses war below a `CurrentTotalStrength` of **500**, both absolute constants on the scale §17's multiplier moves. `TroopPowerMathTests` bounds the arithmetic; only a running campaign shows the consequence, and these are the two columns where it would appear.

**The wasted-trip classification.** `SettlementEntered` records a party's member count and size limit; `OnSettlementLeftEvent` classifies the visit. A party that arrived at or above its limit is `NotSeeking` and excluded — counting garrison rotations and patrol stops as failed recruitment would bury the signal. A party that had room and left no fuller is `NoGain`, a wasted recruitment trip, and is bucketed by **the same 60% threshold that gates recovery routing** (`B1071_AiRecoveryMath.ShouldStart`, called through `B1071_TelemetryMath.IsWeakEnoughForRecovery`). That is the whole design of the bucket: recovery routing only ever sees parties below that line, so wasted trips landing mostly in **weak** mean the existing system can be taught to avoid them, while wasted trips landing mostly in **healthy** mean it structurally cannot and the fix belongs elsewhere. The split is judged on arrival strength, because that is what the party was when it chose to come — the decision under examination, not the state it left in. Visit state is a session-only instance dictionary and is never written to `SyncData`.

**Arrival strength cannot be read on arrival, which is why `FieldSample` exists.** Vanilla hires the notable board on the way in — `RecruitmentCampaignBehavior.OnBeforeSettlementEntered` → `CheckRecruiting` → `RecruitVolunteersFromNotable` — and `BeforeSettlementEnteredEvent` fires ahead of `SettlementEntered`. So a count taken in `OnSettlementEntered` already includes the recruits, the lord departs with the same total he was first observed holding, and every successful recruiting trip is classified `NoGain`. That is not a small bias; it is close to an inversion of the one number the metric was built for, and it accounts for the 76% figure recorded before v1.0.3.9. Listening to the earlier event does not help: `MBCampaignEvent.AddHandler` appends to a list that `RunHandlers` walks by index, so dispatch is strict registration order and vanilla's behaviors are always registered first. The only ordering-independent answer is to sample before the party arrives anywhere. `OnHourlyTickParty` therefore records the member count and a `CampaignTime` stamp for every AI lord party with a null `CurrentSettlement`, and `OnSettlementEntered` prefers that sample when it is for the same party and under two campaign hours old — one hourly tick either side of the arrival — consuming it as it is used. Absent or stale, it falls back to the live count, which is the old behaviour rather than a guess: a stale sample could invent a gain that never happened, while the fallback merely under-reports. Only the count is sampled: `PartySizeLimit` is derived from clan tier and perks rather than from the roster, so the hire cannot move it and sampling it would add a staleness path for no gain -- it is still read live at entry. Samples are swept for inactive parties on the same daily pass that clears abandoned visits, so neither dictionary grows across a campaign.

**Routed trips are counted separately, because the figure above cannot answer the question on
its own.** `IsTrackedVisit` admits every AI lord party entering any village, town or castle —
garrison rotations, loot sales, waiting out a pursuer — so a high no-gain share is a property
of the campaign, not evidence about `B1071_AiRecoveryBehavior`. In the first run carrying this
classification, 76% of visits ended with no gain and 74% of those were by weak lords, and
neither number could be attributed to routing. So `VisitState` now also records whether the
settlement was this system's destination for that party, through the read-only
`B1071_AiRecoveryBehavior.IsRecoveryDestination`, and `visitsRouted` / `visitsRoutedNoGain`
carry it into the digest and the CSV. Two details make it honest. It reports **confirmed
intent, not a proposal** — a proposal is a score written into `PartyThinkParams` that the
native AI is free to ignore, and counting those would credit the system with trips it did not
cause. And it samples on **entry, not exit** — intent is cleared the moment the lord reaches
his stop line, so reading it on the way out would report every successful trip as unrouted and
invert the measurement. The pair runs beside the existing counters rather than splitting them,
so days logged before v1.0.3.9 stay comparable.

**The rejection histogram, and why the eligible count needs it.** `RecoveryDigest` reports how many party-hours *entered* a recovery pass. That figure is unreadable alone: a low number means either that few lords need help or that the eligibility filter is rejecting the ones who do, and those call for opposite fixes. So `OnAiHourlyTick` resolves `GetBlockReasons` once and reuses it — `IsPartyEligible` is exactly `IsEligible(GetBlockReasons(party))`, so nothing extra is computed — and a rejected party is passed to `RecordBlockedWeakParty`. That helper counts the rejection only if the lord was **below `B1071_AiRecoveryMath.ShouldStart`'s 60% line**: a healthy lord being turned away is not a missed recovery, and counting those would bury the signal under every full-strength party on the map. It also drops
any party that was never a candidate at all: caravans, villagers, militia and bandits come
through the same hook and fail on `InvalidLeader` and `ExcludedPartyType` every hour of every
day, and in the first run carrying this histogram they were 86% of every rejection recorded,
holding the top two buckets between them. Each of the fourteen `B1071_AiRecoveryBlockReason` flags gets its own bucket, indexed by bit position; the advisory `UrgentFood` flag keeps its column but no longer fills it, since it stopped rejecting anyone in v1.0.3.9 — the `recoveryFoodShort` counter recorded beside `recoveryEligible` is where that share is now read; `B1071_TelemetryMath.BlockReasonNames` is the only thing tying a bucket to its flag, so its order is load-bearing and `BlockReasonNamesMatchTheFlagOrder` fails if the enum ever gains a member out of order. One rejection sets every flag that applied, so the buckets **sum to more than the party-hour total** — a lord can be in an army *and* on a protected objective, and both gates are worth seeing. `BlockDigest` prints the dominant reason first and omits buckets that never fired.

**Why the volunteer board is counted three ways.** `recoveryZeroQuote` says a settlement supplied nothing; it never says why. A board can come back empty because it is empty, because the lord is over his wage limit, or because he is under vanilla's recruiting money floor — and the last of those is a rule Campaign++ *mirrors* rather than owns, which makes it the one most able to drift from the game without any other figure moving. The v1.0.3.9 floor shifted `recoveryZeroQuote` by under a point in its first campaign, equally consistent with "almost never fires" and with "fires constantly on settlements whose quote stays positive on veterans and castle stock". So `QuoteNotableVolunteers` now counts `volQuotes` at the line where structural gates end and affordability begins, and `volWageBlock` and `volGoldBlock` at the two returns below it. The wage limit is bucketed separately because vanilla tests it first: folded together, the gold share would be measured against a denominator containing lords who never reached the gold test. Both money clauses — the personal floor and the clan-purse-with-generosity clause — share `volGoldBlock`, because they are one vanilla decision written as two tests and splitting them would imply a distinction `CheckRecruiting` does not make.

**Two outputs, and why both.** Digest lines go to `rgl_log` and the session file, tagged `[Byzantium1071][Telemetry][Power|AiRecovery|AiRecoveryBlocks|Visits|Demob]`; they answer "what happened just now" and are read top to bottom. `B1071_TelemetryCsvLog` writes `b1071_telemetry_{timestamp}_{pid}.csv` into the same `<ModuleRoot>/Logs` folder, one row of forty-one numeric columns per campaign day; it answers "what has been happening" and is read in a spreadsheet. Interleaving them would mean hand-extracting rows from a log full of unrelated subsystem chatter before any trend could be plotted. The CSV shares `B1071_SessionFileLog.ResolveModuleLogsRoot`, its lock-and-never-throw discipline, and its 30-file prune; every field is invariant-formatted so no quoting is needed and the file opens identically in every locale, and `TelemetryMathTests` fails if the header and row column counts ever diverge.

**Failure posture.** Every handler is wrapped; five faults disable the behavior for the session rather than logging once per tick forever. Nothing here is read by any gameplay path, and `SyncData` is deliberately empty.

---

## 23. Settings Migration

### Problem

MCM (`AttributeGlobalSettings`) persists all user-modified values in a JSON file on disk. When code defaults are changed in an update, existing users don't see the new values — MCM loads the saved (stale) values instead.

### Solution

Version-gated hard migration with notification:

- `SettingsProfileVersion` (integer property in Developer Tools) tracks the user's current settings version
- `MigrateToLatestProfile()` checks if `SettingsProfileVersion < LATEST_PROFILE_VERSION` and applies all missing migration blocks
- On first mod load after update, rebalanced settings are force-overwritten to new defaults
- Non-rebalanced settings (pool sizes, slave economy, castle recruitment, toggles, etc.) are preserved
- A green in-game notification confirms: *"Campaign++ v0.1.8.2: Balance settings updated to new defaults."*
- Migration is logged to `rgl_log` for diagnostics
- Future rebalances simply bump `LATEST_PROFILE_VERSION` and add a new migration block

---

## 24. Compatibility

**Game version:** targeted at the installed Bannerlord **v1.5.3 beta** (and Warsails/NavalDLC **v1.3.3**). API and prefab references were re-resolved against the installed binaries; manual nameplate smoke testing remains a separate acceptance step.

**Required dependencies** (must load before this mod):
- `Bannerlord.Harmony` ≥ v2.4.2
- `Bannerlord.ButterLib` ≥ v2.10.4
- `Bannerlord.UIExtenderEx` ≥ v2.13.2
- `Bannerlord.MBOptionScreen` (MCM) ≥ v5.11.4

All four are declared as `DependedModules` in `SubModule.xml`. If any is missing from `Modules/`, Campaign++ will not load at all — game updates sometimes clear third-party modules, so re-check this list after every Bannerlord patch.

**Confirmed compatible mods:**
- **Bannerlord.EconomyOverhaul** (v1.1.6) — Full compatibility. 23/23 B1071 systems work. EO’s food model replacement is handled via runtime dynamic patching; volunteer model converted to Harmony Postfix to avoid AddModel collision. EO’s village-level slave system and B1071’s town-level slave system are completely independent and complementary.
- **CavalryLogisticsOverhaul** (v1.2.0) — Fully compatible. No overlapping patch targets. Cavalry wage costs may compound (intentional — both mods make cavalry more expensive).
- **Bannerlord.Diplomacy** (v1.0.1.50) — Compatible with caveats. See interaction map below.

**Compatibility utility (v1.0.0.1):** The Campaign++ Compatibility tab now includes **Rebuild Recruitment Sources**, a safe manual refresh for volunteer-board sanitization and castle recruitment culture caches after troop-tree mods are added, removed, or updated mid-save.

### Bannerlord.Diplomacy Interaction Map (v0.2.7.2)

Diplomacy adds its own war exhaustion system and peace proposal pipeline. Five code paths can end a war:

| # | Path | Trigger | B1071 Intercepts? | Notes |
|---|------|---------|--------------------|-------|
| 1 | **Forced peace (max exhaustion)** | `ProcessWarExhaustion` → `ConsiderPeaceActions` → `KingdomPeaceAction.ApplyPeace()` | **No** | Fires when Diplomacy exhaustion = 100 + Loss/Tie. Bypasses all proposal conditions. No war-age check. |
| 2 | **AI council proposal** | `ConsiderPeacePrefix` → `MakePeaceConditions.CanApply()` | **Partially** | Diplomacy checks `MinimumWarDurationInDays` (default 21). B1071's DetermineSupport postfix adds early-war penalty but doesn't block the proposal itself. |
| 3 | **Council vote outcome** | `ApplyChosenOutcomePrefix` → `KingdomPeaceAction.ApplyPeace()` | **Yes (v0.2.7.2)** | B1071's early-war penalty makes peace votes fail for young wars. Once a vote passes, peace happens. |
| 4 | **Player diplomacy UI** | Diplomacy's peace proposal panel | **Partially** | Same `MakePeaceConditions` gate as Path 2. |
| 5 | **Player Diplomacy Control** | `PlayerDiplomacyControl` setting | **No** | Player bypasses all AI checks entirely. |

**MCM alignment recommendation:** Set Diplomacy's `MinimumWarDurationInDays` from 21 → 40 to match B1071's `MinWarDurationDaysBeforeForcedPeace`. This synchronizes the proposal gate (Diplomacy) with the vote penalty (B1071).

**No overlapping patch targets:** Diplomacy patches `KingdomDecisionProposalBehavior.ConsiderPeace` (Prefix) and `MakePeaceKingdomDecision.ApplyChosenOutcome` (Prefix). B1071 patches `MakePeaceKingdomDecision.DetermineSupport` (Postfix). These are different methods — no conflict.

### Warsails (NavalDLC) Interaction Map (v1.3.3)

Warsails registers roughly seventy campaign models of its own, ten of which sit on systems Campaign++ patches:

| Model | Warsails replacement | Campaign++ still applies? |
|---|---|---|
| `SettlementProsperityModel` | `NavalDLCSettlementProsperityModel` | **Yes** |
| `SettlementSecurityModel` | `NavalDLCSettlementSecurityModel` | **Yes** |
| `SettlementGarrisonModel` | `NavalDLCSettlementGarrisonModel` | **Yes** |
| `SettlementMilitiaModel` | `NavalDLCSettlementMilitiaModel` | **Yes** (see caveat) |
| `PartyWageModel` | `NavalDLCPartyWageModel` | **Yes** |
| `SettlementAccessModel` | `NavalDLCSettlementAccessModel` | **Yes** |
| `CombatSimulationModel` | `NavalDLCCombatSimulationModel` | **Yes** |
| `PartyHealingModel` | `NavalDLCPartyHealingModel` | **Yes** |
| `ClanFinanceModel` | `NavalDLCClanFinanceModel` | **Yes** |
| `BuildingConstructionModel` | `NavalDLCBuildingConstructionModel` | **Yes** |

These derive from the abstract base rather than the vanilla `Default*` type, which would normally bypass our patches. They do not: each is a **thin decorator** that forwards through `((MBGameModel<T>)this).BaseModel.Method(...)`, so execution still reaches the `Default*` implementation where Campaign++'s Harmony patches are attached.

Models Warsails does **not** replace (still plain vanilla `Default*`): `SettlementFoodModel`, `SettlementLoyaltyModel`, `VolunteerModel`, `TradeItemPriceFactorModel`.

`CombatSimulationModel` gained a naval ship-vs-ship `SimulateHit` overload in Warsails v1.2.8: `(Ship, Ship, PartyBase, PartyBase, SiegeEngineType, float, MapEvent, out int)`. Campaign++ does not patch it, and its parameter types stay distinct enough that the explicit `argumentTypes` array still selects the troop overload unambiguously. Note that the troop overload itself **did** change in game v1.5.0 — see the tier armor note below.

**Known caveat — militia perk bonus:** `B1071_ManpowerMilitiaModel` derives from `DefaultSettlementMilitiaModel` and calls `base.CalculateMilitiaChange()`. Because official modules load first, Campaign++'s model is registered last and therefore sits outermost in the chain, so that `base.` call goes straight to the vanilla model and skips `NavalDLCSettlementMilitiaModel`'s port-town Boatswain perk bonus. Gameplay-only, no crash; would be resolved by converting the model to a Harmony postfix or resolving `BaseModel` at runtime.

**Patches that touch private methods** (fragile on game updates):
- `RecruitmentCampaignBehavior.ApplyInternal` — private, string name
- `RecruitmentVM.OnDone` — private, string name
- `RecruitmentVM.RefreshScreen` — private, string name
- `RecruitmentVM.RefreshPartyProperties` — private, string name
- `DefaultClanFinanceModel.CalculateClanIncomeInternal` — private, string name
- `PartiesSellPrisonerCampaignBehavior.OnSettlementEntered` / `DailyTickSettlement` — private, string name
- `InfluenceGainCampaignBehavior.OnPrisonerDonatedToSettlement` — private, string name

If Bannerlord renames any of these methods, the corresponding patch will silently not apply. `VerifyCriticalPatches()` runs after patching at startup and logs any target that failed to attach, so this surfaces at launch rather than mid-campaign. Patches on public methods use `nameof()` and have compile-time safety.

### Game Version Verification

Each game update is verified by resolving every patch target and reflection path against the new assemblies, not just by recompiling — Harmony binds prefix/postfix parameters **by name**, so a renamed parameter compiles fine and then throws at patch time.

The checklist per update:

1. Release build against the new game DLLs.
2. Resolve all `[HarmonyPatch]` targets — signature match, overload ambiguity, and parameter names.
3. Resolve all reflection paths (`Clan.IsMinorFaction` / `IsRebelClan`, `RebellionsCampaignBehavior._rebelClansAndDaysPassedAfterCreation`, `ItemObject.ItemCategory` and its backing field, `MapEvent.IsRaid` / `MapEventSettlement` / `EventType`, the `CampaignTime` members, `ModuleHelper.GetModules`).
4. Confirm the MapBar UIExtender XPath anchors still exist in `SandBox/GUI/Prefabs/Map/MapBar.xml`.
5. Confirm the `town` / `castle` / `village` game menu IDs are unchanged.
6. Confirm Gauntlet vertical-stack layout direction has not flipped again (see the v1.4.5 fix).
7. Check whether any newly shipped first-party model replaces a patched `Default*` type, and whether it delegates via `BaseModel`.

**v1.4.8 / Warsails v1.2.8 result:** 44 Harmony patch methods and 16 reflection paths resolved with no breaking changes; UI anchors, menu IDs, and layout direction unchanged. No code changes were required.

**v1.5.0 / Warsails v1.3.0 result:** exactly one breaking change. `DefaultCombatSimulationModel.SimulateHit` gained a `TaleWorlds.Core.BattleEnvironment` parameter in position 7 (8 → 9 params), which stopped `B1071_TierArmorSimulationPatch`'s explicit `argumentTypes` array from resolving. `PatchAssemblySafely` caught and logged it per-class, so there was no crash and no save damage — the feature simply stopped working. Fixed in v1.0.2.5. Every other patch target and reflection path resolved with unchanged signatures.

**v1.5.2 / Warsails v1.3.2 result:** no breaking changes. All 34 `[HarmonyPatch]` targets, 10 reflection paths and 8 prefab XPath anchors resolved with unchanged signatures and unchanged parameter names — including the nine-type `SimulateHit` array pinned in v1.0.2.5. The ten Warsails decorators were re-read from decompiled source, and every one still forwards through `((MBGameModel<T>)this).BaseModel`, so no patched `Default*` implementation is bypassed; the four undecorated models remain undecorated. `SettlementNameplateVM`, `TooltipRefresherCollection`, `PropertyBasedTooltipVM`, `CampaignUIHelper` and `SettlementNameplateItemWidget` decompile byte-identical to their v1.5.0/v1.5.1 form, so the v1.0.3.3 settlement tooltips are unaffected. No game enum is persisted or compared by ordinal, so a reordered enum cannot reach save state. Build is clean with no `BHA0001`; the fast suite and the game-backed suite both pass. **Steps 5 and 6 were not re-run** — both need a running campaign. No code changes were required. Note that v1.5.2 is a Steam `beta`-branch build, so the "beta" in the target-game string is still accurate.

**v1.5.3 / Warsails v1.3.3 result:** no breaking changes. All 39 `[HarmonyPatch]` targets and 11 reflection paths resolved with unchanged signatures and unchanged parameter names. The target count grew from 34 because the sweep manifest had drifted behind the code: the three settlement-revenue taper targets (`CalculateTownIncomeFromTariffs`, `CalculateVillageIncome`, `CalculateTownTax`), `GarrisonRecruitmentCampaignBehavior.TickAutoRecruitmentGarrisonChange` and `DefaultMilitaryPowerModel.GetDefaultTroopPower` were in the source but not the manifest, and all five were verified present and name-identical in the v1.5.3 binaries before being added. `RevenueSmoothenFraction()` still returns `5f`, and the tariff and village originals still debit `TradeTaxAccumulated` from the untampered pool inside the method body, so the revenue taper's payout-only scaling remains sound. The ten Warsails decorators that override a patched method all still forward through `base.BaseModel`; the four undecorated models remain undecorated (105 first-party `*Model` subclasses seen); the ship-vs-ship `SimulateHit` overload still leaves the pinned nine-type troop array unambiguous. The `town` / `castle` / `village` / `castle_dungeon` menu IDs were confirmed statically in decompiled `AddGameMenu` calls — the first update where step 5's ID check did not need a running campaign. Build against the v1.5.3 assemblies reports no `BHA0001`; the fast suite (573 tests) and the game-backed suite (201 tests) both pass. **Step 6 was not re-run** — visual, needs the game on screen. No code changes were required. v1.5.3 is still a Steam `beta`-branch install (`BetaKey: beta` in the app manifest), so the "beta" in the target-game string is still accurate.

**Lesson for future updates:** a patch pinned by an explicit `argumentTypes` array fails *silently* when the game inserts a parameter, and `VerifyCriticalPatches` will not catch it because that list only covers private methods resolved by string name. Build with the BUTR Harmony Analyzer enabled and treat any `BHA0001` warning as a release blocker — it caught this one at compile time.

**Save compatibility**: All mod state is stored in campaign saves. Loading a save from a version of the mod without certain features (e.g., an older save without war exhaustion data) will gracefully default to empty dictionaries and zero values. Upgrading from 0.1.5.x to 0.1.6.0 requires no new campaign.

---

## 25. Known Limitations and Design Decisions

### Orphan villages
Villages with no bound town or castle (broken map data from mods) return `null` as their pool and are skipped silently.

### Player kingdom and forced peace
With `DiplomacyEnforcePlayerParity` ON (default), the player's kingdom is subject to the same forced peace and diplomacy pressure as AI kingdoms. This can be disabled in MCM if you prefer to control your own war/peace decisions entirely.

### Truce duplicate registration
Peace events can trigger truce registration 2-3 times (two Harmony Postfixes + CampaignEvent). The redundancy ensures truces are captured even if a third-party mod skips part of the Harmony chain. An idempotency guard (v0.2.7.0) deduplicates registered entries when the same pair+expiry arrives within 0.01 campaign days, eliminating redundant log messages while preserving the multi-source capture mechanism.

### UI override priority
The `CanBeRecruited` flag on individual troop slots is always ANDed with vanilla's value: the mod can restrict but never re-enable troops vanilla disabled (e.g., party full, insufficient gold). The `CanRecruitAll` button follows the same rule.

### Stochastic variance is bounded
All random multipliers are clamped: volunteer variance is capped at ±100% (never produces a negative probability), recovery variance is capped the same way.

### No simulation of supply lines or treasury costs
The mod models population willingness to serve (manpower) but does not model gold costs for siege packs, supply wagon attrition, or command capacity beyond vanilla party size limits. These could be future additions.

### Performance
The heaviest operation is the overlay cache rebuild (`RebuildCache`), which iterates all settlements and calls `GetDailyRegen` once per unique pool. On a typical world (~300 settlements, ~100 unique pools), this runs in < 2ms on modern hardware and is run at most once per in-game day.

---

## 26. Slave Economy

### Historical context

In the 1071-era Near East, the capture and sale of prisoners was a primary mechanism through which warfare fed economic reconstruction. Byzantine estates rebuilt after Seljuk raids using enslaved labour; Seljuk amirs sold Greek captives into Anatolian markets. This system models that loop.

### The Slave trade good

Slaves are a dedicated `Goods`-type `ItemObject` (`id="b1071_slave"`) in the civilian market. They behave exactly like grain, clay, or spice — visible in your party inventory, tradeable on the Trade screen, and purchasable by AI caravans through normal trade routes. They have their own item category (`b1071_slaves`) so trade notifications read "Slaves" rather than a vanilla category name.

### Acquiring slaves

**Village raids** — Each time the game delivers a loot batch during a village raid (the same moment grain or clay appears in the raid log), Slave goods are added to the raiding party. Count per loot event:
```
slaves = floor(village.Hearth / SlaveHearthDivisor)
```
Default divisor is 300: a 300-hearth village yields 1 slave per event; 600 hearths = 2. Villages below the divisor yield 0. This applies to **both the player and AI lords** — every faction benefits from raiding.

**Prisoner enslavement (player)** — When in a town, the `⛓ Enslave prisoners` option appears in the town menu if you have non-hero prisoners. Selecting it converts all non-hero prisoners at **Tier 3 or below** to Slave goods (1:1). Heroes and T4+ prisoners cannot be enslaved — T4+ must be taken to a castle for recruitment conversion or ransomed at the tavern. The tier cap is controlled by the unified MCM setting `CastlePrisonerAutoEnslaveTierMax` (default 3), ensuring complete player/AI parity.

**Roguery XP parity** — Successful enslavement now grants Roguery XP equivalent to Bannerlord's vanilla prisoner-sale formula. The feature is controlled by `EnableEnslavementRogueryXp` and `EnslavementRogueryXpMultiplier` in MCM → Slave Economy.

**Slave Conversion Selection UI (optional)** — When `EnableSlaveConversionSelection` is ON (MCM → Slave Economy), clicking enslave opens a selection screen instead of bulk-converting. Players see a list of eligible prisoners with +/− controls and Select All / Deselect All buttons, then confirm which prisoners to convert. This is particularly useful with mods like Lowborn that add valuable low-tier troops. The popup resets any stale instance before opening and is also reset at campaign end and module unload, preventing an abandoned screen from silently blocking later clicks. When the toggle is OFF, the legacy bulk behavior is used.
The selector computes float-backed ListHeight as min(260, max(62, troop-type count × 31)) and WindowHeight as 240 + ListHeight at initialization. The 240-unit allowance preserves the original 500-minus-260 non-list geometry. Quantity changes do not resize the popup. The module-local B1071.QuietButton brush in GUI/Brushes/B1071_Buttons.xml styles secondary management actions and ledger controls, referencing existing native sprites without replacing a global brush. Both canvas and frame layers extend 5 units left and right for label clearance; widget layout and hit areas remain unchanged. Resting canvas/frame ColorFactor values are 0.75/0.55, hover 1.15/1.0, pressed 0.55/0.70, and disabled 0.30/0.30. The prisoner confirmation retains native ButtonBrush; close controls remain borderless. Ledger active-tab gold labels use existing selection bindings, while sorted-heading overlays follow the existing arrow suffix and receive notifications from the header setters. The quiet-button styling itself does not change dimensions, sorting, or refresh scheduling. Recruitment, Demobilization, and VeteranRecall add an event-transparent clipped canvas body beginning at y=104 (y=96 for Demobilization to cover the remaining native bevel). The body is inset 5 units and the divider 20 units. Its canvas uses a -200 top margin and -20 side/bottom margins to crop the scaled header and built-in edge trim. A one-unit divider marks the boundary. B1071.PanelFrame in GUI/Brushes/B1071_Panels.xml draws only the native outer frame after the canvas layers, preventing backgrounds from covering it or introducing a second inner frame. The underlying backing is an opaque dark BlankWhiteSquare_9 inset 5 units on all sides instead of the full-size brown StdAssets popup. The prisoner selector also uses the inset dark backing and B1071.PanelFrame above its existing canvas, replacing Encyclopedia.Frame without changing its bounded sizing or content layout. These decorative siblings do not participate in the content ListPanel layout.

**Prisoner enslavement (AI)** — When an AI lord party enters a town, **Tier 1–3** non-hero prisoners in their prison roster are automatically enslaved via the Harmony Prefix in `B1071_CastlePrisonerDepositPatch`. The town **buys** each slave at the current market price — gold is deducted from `Town.Gold` and paid to the lord via `GiveGoldAction.ApplyForSettlementToCharacter` (properly clamped, fires campaign events). If the town runs out of gold mid-batch, remaining T1–T3 prisoners stay with the lord and fall through to vanilla sell behavior (ransom gold). **Tier 4+ prisoners are not enslaved** — they are left for the vanilla ransom/release pipeline or deposited at castles for recruitment conversion. Both player and AI use the same `CastlePrisonerAutoEnslaveTierMax` setting, and both now receive the same Roguery XP treatment as the player path. AI lords also deposit any slave items already in their inventory (from raids) into the town market on arrival.

### Starting slave stocks

On new game creation, each town is seeded with a random number of slaves (0–30) already in its market. This makes the construction and prosperity bonuses visible from the first campaign day. The seeding runs exactly once per save and is not repeated on load.

### Selling slaves

Sell Slave goods via the standard town **Trade** screen. Price is set by the market like any commodity.

**AI caravan trade** — The `b1071_slaves` category uses `luxury_demand=1.0`, the same tier as wine or jewelry. AI caravans actively route for luxury goods, so they will seek out towns where slaves are available and transport them to towns with unmet demand. This creates a realistic redistribution mechanic: slaves sold in a raided border town may eventually appear in a wealthy interior city. If your goal is sustained bonuses in a specific town, you need to keep selling there.

**Price behaviour** — Because slave supply depends entirely on player raids and prisoner conversions (no production building produces slaves), the price at any given market primarily reflects local stock. A custom Harmony postfix (`B1071_SlavePricePatch`) replaces the vanilla price formula for the `b1071_slaves` category with an exponential decay curve:

```
priceFactor = max(0.1, decayRate ^ stock)
```

Where `stock = inStoreValue / 300` (integer count of slaves) and `decayRate` is an MCM setting (default 0.98). This produces a smooth, gradual price decline with meaningful differentials across stock levels:

| Stock | Price (at base 300d) |
|-------|-----------------------|
| 0     | 300d                 |
| 10    | 245d                 |
| 20    | 200d                 |
| 30    | 164d                 |
| 50    | 109d                 |
| 80    | 60d                  |
| 114+  | ~30d (floor)         |

The base value was reduced from 1500d to 300d in v0.1.8.5 after mathematical analysis showed the 1500d base made enslaving 10–13× more profitable than ransoming T1–T3 prisoners (avg ransom ~45d). At 300d, enslaving yields ~2–2.5× ransom — worthwhile but not exploitative. The decay rate default was raised from 0.925 to 0.98 in v0.1.8.4 after playtest data showed 96.7% of price snapshots stuck at floor with the steeper curve.

This replaces the vanilla formula `priceFactor = (demand / (0.1×supply + inStoreValue×0.04 + 2))^0.6`, which was designed for cheap bulk goods. With a 300d base value, the vanilla formula's denominator terms are better scaled, but the custom curve provides smoother, more predictable price behavior for a item that has no production buildings.

**IsTradeGood fix (v0.1.8.3)** — Bannerlord's XML deserialization silently ignores the `is_trade_good="true"` attribute on custom `ItemCategory` objects loaded from XML. Without intervention, the slave category defaults to `IsTradeGood=false`, which applies the non-trade clamp of [0.8–1.3×] — making prices almost completely supply-insensitive (e.g. 569 slaves in a town still produces ~1200 denars). `InitializeSlaveMarketData()` now force-sets `IsTradeGood=true` via reflection on the auto-property backing field (`<IsTradeGood>k__BackingField`) at session launch. This restores the full [0.1–10.0×] trade-good price range. Note: the custom price patch now bypasses the vanilla formula entirely for slaves, but the IsTradeGood fix remains necessary to ensure the correct clamp range is applied if the patch ever fails to fire.

**Supply EMA correction (v0.1.8.3)** — Bannerlord's `TownMarketData` tracks supply as an exponential moving average: `Supply_new = Supply_old × 0.85 + InStoreValue × 0.15` (updated daily by `ItemConsumptionBehavior`). Half-life is ~4.3 days. `CorrectSlaveSupplyEma()` caps the Supply EMA at `2 × InStoreValue + 3000`. Note: with the custom price patch (v0.1.8.4), this correction is less critical for pricing (our postfix ignores supply/demand and uses only inStoreValue), but it remains useful for caravan AI supply/demand evaluation and as a safety net if the patch fails to fire.

### Daily market bonuses

While Slave goods are stocked in a town's civilian market, two bonuses apply each campaign day — all proportional to current stock:

| Bonus | Formula | Default (at 1.5× effectiveness) |
|-------|---------|----------------------------------|
| **Construction** | `min(cap, count × acceleration × eff)` | 100 slaves → 75 progress/day |
| **Prosperity** | `count × prosperityPerUnit × eff` | 100 slaves → ~1/day |

> **Note (v0.1.8.3):** Manpower injection was removed. Slaves are labor — historically they were NOT a source of military recruitment.

Construction and prosperity bonuses integrate into the game's `ExplainedNumber` pipeline:
- **Construction tooltip** (Manage screen): shows `"Slave Labor +XX"`
- **Prosperity tooltip** (hover over town): shows `"Slave Labor +X.XX"` in Expected Change

Bonuses end automatically when market stock reaches zero.

### MCM settings (Slave Economy group)

| Setting | Default | Effect |
|---------|---------|--------|
| Enable slave economy | On | Master toggle |
| Hearths per slave (raid) | 300 | slaves = floor(hearths / divisor) per loot event |
| Slave effectiveness multiplier | 1.5× | Scales all daily bonuses |
| Prosperity per slave per day | 0.0067 | ×1.5 eff ≈ 0.01/slave (100 slaves ≈ 1/day) |
| Construction bonus per slave/day | 0.5 | ×1.5 eff = 0.75/slave (100 slaves = 75/day) |
| Construction bonus cap | 150 | Maximum daily construction bonus regardless of stock |
| Food consumption per slave/day | 0.05 | 100 slaves = -5 food/day. Creates natural economic cap on hoarding. |
| Daily slave decay % | 1.0% | % of slave population lost per day (deaths, escapes, manumission). Creates equilibrium. |
| Slave cap per prosperity | 0.02 | Max slaves per point of prosperity. Excess manumitted daily → MP. 0 = no cap. |
| Slave cap minimum | 10 | Minimum slave cap regardless of prosperity. |
| Slave price decay rate | 0.98 | Exponential decay rate for custom slave price curve. Price = baseValue × decayRate^stock. Lower = steeper drop. |

### Slave stock cap & manumission (v0.1.8.4)

Playtest analysis revealed global slave oversaturation (~61,000 slaves across the map, ~119/town average) pinning nearly all towns at the price floor (~150 denars). AI enslavement inflow far exceeded the 1%/day decay, eliminating price differentials and caravan trade.

Each town now has a prosperity-based slave cap: `max(SlaveCapMinimum, prosperity × SlaveCapPerProsperity)`. At defaults (0.02), a 3000-prosperity town can hold ~60 slaves; a 1000-prosperity town ~20.

Each daily tick, if stock exceeds the cap, excess slaves are **manumitted (freed)** and converted **1:1 into the town's manpower pool** via `B1071_ManpowerBehavior.AddManpowerToSettlement()`. This creates a gameplay loop:

1. War → prisoners → slaves → construction/prosperity bonuses while stock is under cap
2. Overflow → freed → MP returned to town pool (recruitable population)
3. Towns stabilize at their cap, creating supply differentials that drive caravan trade

The manumission fires after decay in `OnDailyTickSettlement`, so decay reduces stock first, then the cap check removes any remaining excess. Verbose log: `"Manumission at [Town]: X slave(s) freed (cap=Y, stock was Z, now W). +X MP to town pool."` Player notification (green ⚔) shown if at current settlement.

### Slave food consumption (new in 0.1.6.0)

Each slave in a town market, settlement stash, or food-consuming mobile party consumes food daily. This is visible in the food tooltip as **"Slave Upkeep -X.XX"**. At default settings (0.05 food/slave/day):

| Slaves | Food drain |
|--------|-----------|
| 50 | -2.5/day (noticeable) |
| 100 | -5.0/day (significant — roughly a village's output) |
| 200 | -10.0/day (severe — multiple villages' output) |

Historically, enslaved labourers received subsistence rations comparable to garrison troops (~0.04–0.06 food units/day). This creates a natural economic cap on slave hoarding: at some point, the food cost of maintaining a large slave population outweighs the prosperity and construction benefits, forcing strategic balance.

### Carried slaves, stashes and shared captive capacity

- **Food:** `B1071_SlaveFoodPatch` sums market and stash counts; the existing `GetSlaveCountForTown` remains market-only so construction and prosperity cannot benefit from stashes. Town/castle stash upkeep is paid by settlement food stocks. `B1071_SlavePartyFoodPatch` adds upkeep to the base party ration calculation, using `DoesPartyConsumeFood` to preserve vanilla exemptions. Native food perks and War Sails modifiers then apply normally, without flattening or reinterpreting their additive factors. Native daily consumption, fractional rations, army food sharing and starvation still handle the actual party supplies. Settlement upkeep is added to the resolved settlement breakdown without reapplying its existing factors. No new settings or saved accumulators are needed.
- **Capacity:** While Slave Economy is enabled and initialized, including at zero slaves, `B1071_SlavePrisonerCapacityPatch` queries the current size-limit model at the `PartyBase.PrisonerSizeLimit` getter, subtracts carried slave goods and clamps the remaining ordinary-prisoner limit to zero. Vanilla's prisoner-roster cache can miss troop-count changes as well as inventory transfers; bypassing it keeps the displayed reservation consistent with escapes and speed. Removing the last slave therefore cannot restore a stale native limit. The explainer shows occupied slots. The underlying size-limit model remains unchanged and supplies total shared capacity. Converting one prisoner into one slave therefore preserves total occupied capacity; all other acquisition paths are covered by live roster queries.
- **Screen capacity:** `B1071_SlavePartyScreenCapacityPatch` reads the current ordinary-prisoner limit from the player party when the screen uses that party's live member roster. This keeps native labels, transfer allowances and mixed-captive warnings current after troop transfers and undo. Detached previews and other parties keep their supplied limits. The public getter is a trivial auto-property with no game-dependent static initialization; binding is tested before a campaign exists.
- **Capacity warning:** `B1071_SlavePrisonerWarningPatch` augments the native `PartyVM.IsMainPrisonersLimitWarningEnabled` setter when slaves alone exceed total capacity. Otherwise the clamped zero ordinary-prisoner limit compared with zero ordinary prisoners cannot warn. It applies only to the player's right-hand party when prisoners are relevant, preserves native warnings, and uses the setter to notify Gauntlet of the corrected value. The recursive setter call stops immediately once the flag is true. The native setter has no game-dependent static initialization (verified against 1.5.3); tests bind the patch before a campaign exists and verify the notification value and warning clearing.
- **Land speed:** `B1071_SlaveEscortSpeedPatch` adds only the difference between the native escort curves for prisoners plus slaves and prisoners alone: `(10 + men) / (10 + men + captives)`, raised to `0.33`, minus one. Counts include attached parties, matching the native escort aggregation. Caravans have no native ordinary-prisoner speed contribution, so their extra escort term uses slave counts only. Existing cargo weight stays intact. The native overcapacity contribution is read from the same capacity getter used by vanilla, then replaced with a single combined captive/capacity ratio using the current model. As in vanilla, the overcapacity ratio is per leading party, while the regular escort term includes attached parties. Native speed floors remain; sea speed is untouched.
- **Overcapacity escapes:** `B1071_SlaveSharedEscapePatch` replaces private `PrisonerReleaseCampaignBehavior.HourlyPartyTick` only while the enabled system has carried slaves. The number of hourly attempts is combined captives minus total capacity. Each successful chance samples slaves and regular prisoners uniformly by headcount, falling back to heroes only when neither remains; the player hero is excluded. Base chance is native 10%, modified by Athletics Stamina and the leader's Valor trait. Map events, sieges, garrisons and militia retain their native exclusions. Slave goods are removed through the item roster; regular prisoners through the troop roster; heroes through `EndCaptivityAction`. No slave-only stock is immune, and no ordinary-prisoner-only escape penalty is introduced. Partial failures never run a second native escape pass.
- **Compatibility:** Private target and native formula verified against Bannerlord 1.5.3; registered in launch verification and game-backed patch-binding tests. The installed War Sails decorators forward party food and land speed through their base models. Slave goods, prices, save formats, market-only decay/manumission and FIFO castle consignment are unchanged. Disabling Slave Economy restores native capacity, food and speed behavior.

### Slave attrition (new in 0.1.6.0)

1% of the slave population is lost each day (deaths, escapes, manumission). Without decay, slave populations would grow indefinitely once established; with it, you need continuous inflow (raids, prisoner enslavement) to maintain a slave workforce. This creates a natural equilibrium where the rate of acquisition must match the rate of decay.

The decay uses a **fractional accumulator** (persisted in save data) to prevent rounding loss when the decay rate produces less than 1 whole slave per day. For example, 10 slaves at 1% decay = 0.1 loss/day. After 10 days, the accumulator reaches 1.0 and one slave is removed.

---

## 27. Minor Faction Economy

### Historical context

In the 1071 Byzantine-Seljuk era, minor warbands — Turkmen raiders, Norman mercenary companies, Armenian frontier lords — sustained themselves through a combination of raiding income, toll collection, protection fees, and tribute extraction. They had no permanent settlement tax base like major factions.

### The problem

Minor factions in Bannerlord have no settlement income. Their only revenue is merc pay (if hired), a tiny base income, and occasional trade. Under the mod's tier-exponential wage system, a Tier 4 minor faction with 50 average-T3 troops pays ~400d/day in wages but earns only ~160–320d/day. This forces troop dismissal and combat irrelevance.

### The fix

Each non-bandit minor faction clan receives a daily **"Frontier Revenue"** stipend:
- **Mercenary clans** (under contract): `clanTier × 250 denars/day`
- **Unaligned clans**: `clanTier × 400 denars/day` (higher because they have no employer subsidising them)

This is visible in the clan finance tooltip as "Frontier Revenue". Bandit factions are excluded (they skip DailyTickClan entirely). The **player clan is explicitly excluded** — players have settlement income and this is an AI economic balancer only.

**Rescued rebel clans** (v0.2.7.0) also qualify for Frontier Revenue after normalization — but since v1.0.2.6 the rebel rescue is off by default, so in a default install no new clans enter this path. When the Clan Survival system rescues a rebel clan, it sets `IsMinorFaction = true` via reflection, making the rescued clan eligible for the unaligned stipend (`clanTier × 400 denars/day`) while it remains independent. If that clan later joins a kingdom as a normal vassal, Frontier Revenue now stops so it does not double-dip on top of settlement income. Mercenary-service clans still qualify for the mercenary stipend path.

### MCM settings (Minor Faction Economy group)

| Setting | Default | Effect |
|---------|---------|--------|
| Enable minor faction income boost | On | Master toggle |
| Mercenary stipend per tier | 250 | denars/day per clan tier for merc-contracted factions |
| Unaligned stipend per tier | 400 | denars/day per clan tier for independent factions |

---

## 28. Provincial Governance

### Historical context

Prolonged conflict strained Byzantine provincial administration. Raids disrupted tax collection, sieges displaced populations, and conquests shattered administrative continuity. Rebuilding governance after violence took months or years.

### How it works

Every town and castle accumulates **governance strain** from war events (raids, sieges, battles, conquests). High strain penalises:
- **Loyalty**: up to -3.0/day at maximum strain (visible in loyalty tooltip as "Governance Strain")
- **Security**: up to -2.0/day at maximum strain (visible in security tooltip)
- **Prosperity**: up to -1.0/day at maximum strain (visible in prosperity tooltip)

Strain decays at 0.3/day during peacetime. A +10 raid strain takes ~33 days to fully decay. Penalties scale linearly from 0 at strain 0 to the configured maximum at the strain cap (default 100).

### Provincial Stabilization

Same-faction lords can actively stabilize strained towns and castles. The action is available from the settlement menu when governance strain is present and the settlement is not under siege.

| Tier | Cost | Immediate effect | Temporary recovery |
|------|------|------------------|--------------------|
| Emergency Relief | 1,500g | -10 strain | +0.6 loyalty/day, +0.4 security/day for 5 days |
| Placate Local Elites | 4,000g | -20 strain | +1.0 loyalty/day, +0.7 security/day for 7 days |
| Grant Amnesty | 9,000g | -35 strain | +1.5 loyalty/day, +1.0 security/day for 10 days |

Each tier also adds extra governance strain decay while active. Duration doubles as cooldown. AI lords use the same rules when strain is high and they can afford the action safely.

### MCM settings (Provincial Governance group)

| Setting | Default | Effect |
|---------|---------|--------|
| Enable governance strain | On | Master toggle |
| Strain decay per day | 0.3 | How fast strain decays toward 0 |
| Max loyalty penalty | 3.0 | Loyalty penalty/day at full strain |
| Max security penalty | 2.0 | Security penalty/day at full strain |
| Max prosperity penalty | 1.0 | Prosperity penalty/day at full strain |
| Strain cap | 100 | Maximum strain a settlement can accumulate |
| Enable provincial stabilization | On | Gold-funded strain reduction plus temporary loyalty/security recovery |

---

## 29. Frontier Devastation

### Historical context

The Seljuk frontier raids of the 1060s–1070s systematically devastated Anatolian borderlands. Unlike a single raid that destroys and moves on, repeated frontier raiding creates persistent regional degradation — depopulation, abandoned farmland, broken irrigation, collapsed trade routes. Recovery takes years, not days.

### How it works

Each village tracks a **devastation score** (0–100) that:
- **Increases** by +25 per completed raid (configurable)
- **Decays** at -0.5/day, but **only during Normal state** (frozen while Looted or being raided)
- A single raid takes 50 days to fully heal. Two rapid raids = 50 devastation before any decay.

### Effects (via Harmony patches, visible in game tooltips)

| Patch | Effect at devastation 50 | Effect at devastation 100 |
|-------|--------------------------|---------------------------|
| Hearth penalty (village) | -1.0 hearth/day | -2.0 hearth/day |
| Prosperity penalty (bound town) | -1.0 pros/day | -2.0 pros/day |
| Security penalty (bound town) | -0.75 sec/day | -1.5 sec/day |
| Food penalty (per village) | -50% food contribution | -100% food contribution |

Prosperity and security penalties are applied to the **bound town/castle** and averaged across all bound villages' devastation values. Food penalty is summed per village.

### MCM settings (Frontier Devastation group)

| Setting | Default | Effect |
|---------|---------|--------|
| Enable frontier devastation | On | Master toggle |
| Devastation per raid | 25 | Devastation added per village loot event |
| Decay per day | 0.5 | Daily decay during Normal state |
| Max hearth penalty | 2.0 | Hearth growth penalty/day at devastation 100 |
| Max prosperity penalty | 2.0 | Prosperity penalty/day at avg devastation 100 |
| Max security penalty | 1.5 | Security penalty/day at avg devastation 100 |
| Max food penalty per village | 1.5 | Food penalty at devastation 100 per village |

---

## 30. Village Investment (Patronage)

### Historical context

Byzantine frontier lords and strategos invested in rural communities as a means of securing loyalty, improving agricultural output, and maintaining population in vulnerable borderlands. Patronage of villages — through donations, construction, and gifting — was a pragmatic mechanism that strengthened the social contract between the military aristocracy and the farming class. A well-supported village produced more recruits, more food, and more loyal subjects.

### How it works

Lords (player and AI) can invest gold at any non-hostile, non-looted village through the village menu. Three tiers of patronage are available, each with increasing cost, duration, and bonuses:

| Tier | Cost | Duration | Hearth/day | Relation | Influence | Power |
|------|------|----------|-----------|----------|-----------|-------|
| Modest | 2,000d | 20 days | +0.3 | +3 | +0.5 | +5 |
| Generous | 5,000d | 30 days | +0.6 | +6 | +1.0 | +10 |
| Grand | 12,000d | 45 days | +1.0 | +10 | +2.0 | +20 |

- **Hearth bonus** lasts for the full investment duration, visible in the hearth tooltip as "Patronage" (Harmony postfix).
- **Relation** is applied immediately to all village notables.
- **Power** is added immediately to notables (capped at 200 to prevent absurd volunteer tiers).
- **Influence** is granted only if the village belongs to the investor's kingdom.
- **Cross-clan diplomacy:** investing in another clan's village grants +2 relation with that clan's leader.
- **Duration = cooldown:** no re-investment at the same village until the previous patronage expires.
- **Gold is destroyed** — the investment acts as a pure gold sink (null recipient via GiveGoldAction).

### AI behavior

AI lords invest when entering their own faction's villages, picking the highest affordable tier with a conservative gold gate (`hero.Gold > cost × 3`). This prevents AI gold starvation while ensuring meaningful economic participation.

### Save/load and mod removal safety

State is persisted as two dictionaries (`_investDaysRemaining`, `_investHearthBonus`) via `SyncData`, keyed by `{settlementId}_{heroId}`. Mid-campaign install is safe (empty dictionaries). Mod removal is safe: only accumulated hearth persists (within normal range), and relation/influence/power changes are vanilla-native.

### MCM settings (Village Investment group)

| Setting | Default | Effect |
|---------|---------|--------|
| Enable village investment | On | Master toggle |
| Modest/Generous/Grand cost | 2000/5000/12000 | Gold cost per tier |
| Modest/Generous/Grand duration | 20/30/45 | Days of hearth bonus (and cooldown) |
| Modest/Generous/Grand hearth | 0.3/0.6/1.0 | Daily hearth growth bonus |
| Modest/Generous/Grand relation | 3/6/10 | Notable relation gain |
| Modest/Generous/Grand influence | 0.5/1.0/2.0 | Influence gain (same kingdom only) |
| Modest/Generous/Grand power | 5/10/20 | Notable power gain |
| Power cap | 200 | Maximum notable power from investment |
| Cross-clan relation | 2 | Relation with owner clan leader |
| AI enabled | On | Whether AI lords invest |

---

## 31. Town Investment (Civic Patronage)

### Historical context

Byzantine towns served as the economic and administrative backbone of the empire. Wealthy benefactors — whether military commanders, provincial governors, or the emperor himself — invested in urban centres through construction projects, market improvements, and civic endowments. This patronage drove prosperity, attracted merchants, and strengthened the tax base. Towns that received investment grew wealthier, while neglected towns stagnated.

### How it works

Lords (player and AI) can invest gold at any non-hostile, non-besieged town through the town menu. Three tiers of civic patronage are available, each with increasing cost, duration, and bonuses:

| Tier | Cost | Duration | Prosperity/day | Relation | Influence | Power |
|------|------|----------|---------------|----------|-----------|-------|
| Modest | 5,000d | 20 days | +0.5 | +3 | +2.0 | +5 |
| Generous | 15,000d | 40 days | +1.0 | +6 | +5.0 | +10 |
| Grand | 40,000d | 60 days | +2.0 | +10 | +10.0 | +20 |

- **Prosperity bonus** lasts for the full investment duration, visible in the prosperity tooltip as "Civic Patronage" (Harmony postfix on `CalculateProsperityChange`).
- **Relation** is applied immediately to all town notables.
- **Power** is added immediately to notables (capped at 200).
- **Influence** is granted only if the town belongs to the investor's kingdom.
- **Cross-clan diplomacy:** investing in another clan's town grants +2 relation with that clan's leader.
- **Duration = cooldown:** no re-investment at the same town until the previous patronage expires.
- **Siege block:** investment is unavailable while the town is under siege (player menu hidden, AI path blocked).
- **Gold is destroyed** — the investment acts as a pure gold sink (null recipient via GiveGoldAction).

### Differences from Village Investment

| Aspect | Village | Town |
|--------|---------|------|
| Target stat | Hearth | Prosperity |
| Cost range | 2,000–12,000d | 5,000–40,000d |
| Duration range | 20–45 days | 20–60 days |
| Influence range | 0.5–2.0 | 2.0–10.0 |
| Siege restriction | N/A (villages) | Blocked during siege |
| AI gold multiplier | ×15 | ×15 |
| Tooltip label | "Patronage" | "Civic Patronage" |
| Notification color | Green | Blue |

### AI behavior

AI lords invest when entering their own faction's towns, subject to:
- Gold safety multiplier (default ×15): `hero.Gold > cost × 15`
- Random chance gate (default 30%)
- Random tier selection from affordable tiers
- Per-hero cooldown (default 5 days between any town investment)
- Prosperity ceiling (default 5,000 — skip wealthy towns)
- Siege check — no investment during sieges

### Save/load and mod removal safety

State is persisted as two dictionaries (`_investDaysRemaining`, `_investProsperityBonus`) via `SyncData`, keyed by `{settlementId}_{heroId}`. Mid-campaign install is safe (empty dictionaries). Mod removal is safe: only accumulated prosperity persists (within normal range), and relation/influence/power changes are vanilla-native.

### Verbose logging

All investment events are logged to rgl_log when verbose logging is enabled:
- `[TownInvestment] Player invested tier 3 (40000d) at Epicrotea — prosperity +2/day for 60d, relation +10, power +20, influence +10.0.`
- `[TownInvestment] Investment expired: key=town_ES1_lord_1_3 at Epicrotea.`
- `[TownInvestment] Prosperity bonus for Epicrotea: +3.00/day from 2 active patron(s).` (logged once per day from the daily tick — not on every model query)

### MCM settings (Town Investment group)

| Setting | Default | Effect |
|---------|---------|--------|
| Enable town investment | On | Master toggle |
| Modest/Generous/Grand cost | 5000/15000/40000 | Gold cost per tier |
| Modest/Generous/Grand duration | 20/40/60 | Days of prosperity bonus (and cooldown) |
| Modest/Generous/Grand prosperity | 0.5/1.0/2.0 | Daily prosperity growth bonus |
| Modest/Generous/Grand relation | 3/6/10 | Notable relation gain |
| Modest/Generous/Grand influence | 2.0/5.0/10.0 | Influence gain (same kingdom only) |
| Modest/Generous/Grand power | 5/10/20 | Notable power gain |
| Power cap | 200 | Maximum notable power from investment |
| Cross-clan relation | 2 | Relation with owner clan leader |
| AI enabled | On | Whether AI lords invest |
| AI gold multiplier | 15 | AI must have gold > cost × this |
| AI chance | 30% | Chance per eligible town visit |
| AI random tier | On | Random vs. always-highest tier |
| AI hero cooldown | 5 days | Min days between any two town investments |
| AI prosperity ceiling | 5000 | Skip towns at or above this prosperity |
| Notify player | On | Show message when AI invests in your towns |

---

## 32. Clan Survival (Kingdom Destruction Rescue)

### Problem

Bannerlord reaches kingdom extinction through more than one lifecycle. The settlement-loss path removes member clans from the dying kingdom and can leave them independent with inherited wars. The leader-death path can call `DestroyClanAction`, killing heroes, disbanding parties, transferring fiefs, and eliminating clans. Without one policy spanning both paths, outcomes depend on which vanilla route happened to fire.

### Solution

The primary `OnClanChangedKingdom` listener tracks eligible clans detached by the settlement-loss path. Two Harmony prefixes cover the destructive path **before** heroes are killed:

1. **`DestroyClanAction.Apply`** — the default path used when `DestroyKingdomAction.Apply(kingdom)` iterates member clans.
2. **`DestroyClanAction.ApplyByClanLeaderDeath`** — the leader-death path used when `DestroyKingdomAction.ApplyByKingdomLeaderDeath(kingdom)` is called after the kingdom leader dies with no successor clans.

`ApplyByFailedRebellion` is deliberately not patched. `ApplyByClanLeaderDeath` is observed, but a tracked clan is protected only when vanilla has already produced a valid living successor; a dead, missing, or detached leader is released to the current vanilla destruction call.

### Rescue Flow

```
DestroyClanAction.Apply/ApplyByClanLeaderDeath called
  └── HandleDestroyClan prefix
        ├── Already tracked?
        │     ├── Independent with living leader → suppress destruction
        │     └── Dead/missing/detached leader, eliminated, or joined kingdom
        │           → clear tracking and let this vanilla call finish
        ├── New rescue eligibility: enabled, non-player, non-bandit,
        │     living adult hero, and living leader after vanilla succession
        ├── Rebel rescue additionally requires RescueRebelClans
        ├── Detach from dying kingdom via the public Clan.Kingdom setter
        ├── Register and verify persistent tracking
        │     └── Registration unavailable/fails → let vanilla destruction finish
        ├── Mark the committed rescue in the session guard
        └── Suppress vanilla destruction
```

**Operation order is critical:**
- No succession, diplomacy, fief-transfer, or destruction action is started from inside the prefix. Vanilla owns succession; inherited wars are cleared on a later daily tick.
- A new rescue suppresses vanilla only after the tracking dictionary contains the clan. If the behavior is unavailable, kingdom detach fails, or registration cannot be verified, the current vanilla destruction call proceeds.
- Clans rescued through the kingdom-destruction path normally have no fiefs. If an unusual path still leaves fiefs, they remain with the independent clan rather than triggering a nested ownership action.

### Independent Tracking (v0.2.0.1)

After rescue, clans enter a tracked independent state:

- The clan becomes an independent faction (`IsMapFaction = true`, `Kingdom == null`)
- Heroes continue normal world behavior under vanilla AI
- The behavior tracks rescued clans via `Dictionary<string, float>` (clan StringId → rescue campaign day)

Daily tick responsibilities in the current implementation:

- Drop invalid/eliminated clans from tracking
- If a tracked clan has already joined a kingdom (vanilla behavior, diplomacy, or another mod), stop tracking it
- For still-independent tracked clans, keep inherited-war cleanup enforced so they remain neutral
- Continue this maintenance for existing tracked clans even when the master toggle is disabled; the toggle prevents new rescues rather than abandoning committed ones
- If a tracked clan has a dead, missing, or detached leader on a daily tick, issue no campaign actions and log `TRACKED_INVALID_LEADER`; this is a legacy/mod-conflict state, not a signal to launch delayed destruction

No scripted kingdom scoring or forced mercenary placement is executed in the v0.2.0.1 rescue flow.

### Edge Cases

| Scenario | Handling |
|----------|----------|
| Player clan in destroyed kingdom | Skipped — vanilla handles player-death separately |
| No living adult heroes | Let vanilla destroy — no one to carry on |
| Leader dead, missing, or no longer belongs to the clan at a rescue boundary | Do not start nested succession or destruction actions; vanilla succession should already have completed, so skip the rescue |
| Clan has fiefs | Keep the fiefs with the rescued independent clan; do not start a nested ownership action |
| No eligible kingdom after grace | Not applicable in v0.2.0.1 (no scripted placement pass) |
| Rescued clan's leader dies while independent | Vanilla succession runs first. If destruction is still requested with a valid living leader, the rescue remains protected; with a dead, missing, or detached leader, Campaign++ clears tracking and lets that current vanilla destruction call finish. |
| Clan already joined a kingdom | Stop tracking (another mod or player action placed them) |
| Failed rebellion destruction | Not patched — legitimate destruction proceeds |
| Rebel clan loses last settlement | **v1.0.2.6:** destroyed, as in vanilla. Only rescued and normalized (IsRebelClan→false, IsMinorFaction→true) if `RescueRebelClans` is enabled |
| Rebel clan leader dies | Vanilla succession runs first. Campaign++ never promotes an heir from the destruction prefix; if the clan remains leaderless, vanilla destruction proceeds |
| Homeless rebel clan on session load | Startup scan normalizes before vanilla's DailyTickClan can kill heroes |

### Rebel Clan Rescue (v0.2.7.0, opt-in since v1.0.2.6)

> **Off by default since v1.0.2.6.** A crushed rebellion now dies exactly as it does in vanilla. Everything described below runs only when `RescueRebelClans` is enabled; the code path is kept intact rather than removed so the behaviour can be restored in one click. Noble clans of a *destroyed kingdom* are still rescued either way — this toggle governs rebel-origin clans only.
>
> **Why it was turned off.** Rescuing a rebel clan requires `IsMinorFaction = true`, because that flag is what makes it eligible for Frontier Revenue. But `IsMinorFaction` is Bannerlord's *mercenary company* category, so every crushed rebellion left a permanent hireable company behind, with no cap and no exit. Players reported 40+ of them by day 800.
>
> **Cleanup for existing saves.** `PurgeLeftoverRebelClans` (default OFF) removes the accumulated companies from a campaign already in progress.
>
> **Consent is asked for explicitly, not inferred from the setting.** Enabling a checkbox in a long settings list is not agreement to an irreversible mass deletion. On the next daily tick the player is shown an inquiry carrying the real candidate count from their own save; nothing is destroyed until they answer yes. The count comes from the same `EnumerateLeftoverRebelClans` iterator that does the removing, so what the player is told and what is acted on cannot drift apart.
>
> **Removal is a drip, not a sweep** — `RebelPurgePerDay` (3) clans per daily tick. A long campaign holds 40–100 of these; disbanding them all on one day removes a large slice of the map's parties simultaneously, which reads to the player as an unexplained mass extinction and shocks army, caravan and war-party state all at once. A progress message names the remaining count each day, and a completion message tells the player they can switch the setting off. Both the ask and the confirmation are per-session and unserialised, so a save/reload part-way through re-asks with the *remaining* count rather than silently resuming.
>
> **`IsPurgingRebelClans` is raised around the `DestroyClanAction.Apply` call** and checked at the very top of `HandleDestroyClan`. Without it the mod intercepts its own destruction call, the rebel branch rescues the clan straight back, and the cleanup increments a counter for a clan that still exists — a silent no-op reporting success. Fixing the `RescueRebelClans` gate covers the common case, but not a player running the cleanup *with the rescue still on*, which is a coherent thing to want. The flag is cleared in a `finally` and again in the outer `catch`, so a throw mid-sweep cannot leave the rescue disabled for the rest of the session.
>
> Selection requires *all three* of: `IsRebelClanOrigin` (a `StringId` containing `rebel_clan` — what still identifies these clans after normalisation cleared `IsRebelClan`, and which no vanilla clan carries), `IsMinorFaction` set (so only clans this mod flagged), zero settlements, and no `Kingdom` — a company under an active mercenary contract holds its employer there, and destroying it mid-contract risks leaving that kingdom with a dangling mercenary entry. It is not leftover in any meaningful sense either; it becomes a candidate again when the contract lapses. Hand-authored minor factions are structurally unreachable, and a rebel clan that took a fief is spared. It runs on the daily tick, never at session launch: `DestroyClanAction` touches kingdoms, parties and diplomacy, and driving that during load — or from inside another action's event callback — is the failure mode that corrupted saves before.
>
> **`NotifyLeftoverRebelClans`** is the passive counterpart: once per session, a save holding leftover companies with `RescueRebelClans` off and the cleanup off gets one message stating that the rescue is off, that the existing companies are being left alone, and where the cleanup lives. The default flip is silent otherwise — a returning player would find their behaviour changed with no in-game signal and no way to discover the cleanup except by reading a changelog. It suppresses itself when the cleanup is enabled, since the inquiry says all of this already.

Rebel clans — spawned when a town rebels — face destruction through different pathways than regular kingdom clans:

1. **When a rebel clan loses its last settlement** (reconquered by another faction), vanilla's `RebellionsCampaignBehavior` daily tick calls `DestroyClanAction.Apply` to destroy the homeless rebel.
2. **When a rebel clan's leader dies**, vanilla calls `DestroyClanAction.ApplyByClanLeaderDeath`.

Neither path goes through the kingdom-destruction pipeline (rebel clans have `Kingdom == null` and no kingdom ever "falls"). The v0.2.6.1 rescue system didn't cover this — all rebel clans were destroyed.

**Two-layer rescue architecture:**

- **Primary**: `OnSettlementOwnerChanged` event listener in `B1071_ClanSurvivalBehavior`. When a settlement changes hands and the previous owner's clan is a rebel-origin clan (`IsRebelClan == true` OR StringId contains `"rebel_clan"`) with zero remaining settlements, rescue fires proactively — before vanilla's daily tick can destroy the clan. No inline Campaign actions (TimeLord/BetterTime safe).
- **Safety net**: `HandleDestroyClan` prefix in `B1071_ClanSurvivalPatch`. When `DestroyClanAction.Apply` or `ApplyByClanLeaderDeath` fires for a rebel-origin clan with `Kingdom == null`, the prefix rescues it only if it has living adults and a living leader after vanilla succession. **Gated on `RescueRebelClans` since v1.0.2.6.** A leaderless clan is released to the current vanilla destruction call; Campaign++ does not start nested succession.

**Normalization** (`NormalizeRebelClan`):
1. Sets `IsRebelClan = false` (public setter) — prevents vanilla from re-targeting the clan
2. Sets `IsMinorFaction = true` (private setter, via reflection) — enables Frontier Revenue eligibility
3. Removes from vanilla's `RebellionsCampaignBehavior._rebelClansAndDaysPassedAfterCreation` dictionary (via reflection) — prevents vanilla's daily tick from managing the clan as a rebel
4. Renames clan from settlement-based rebel name (e.g., "Pen Cannoc rebels") to leader-derived warband name (e.g., "Borun's Warband") via `clan.ChangeClanName()` — prevents duplicate names when the same settlement rebels twice. Skips gracefully if the leader is null. (v0.2.7.2)

The critical normalization invariant is verified before a rescue commits: `IsRebelClan == false` and `IsMinorFaction == true`. If either flag cannot be established, the rescue is aborted (and the rebel flag is rolled back when possible). Removing the clan from vanilla's private rebellion dictionary and renaming it remain logged, best-effort cleanup steps.

**Detection**: `IsRebelClanOrigin()` returns true for clans with `IsRebelClan == true` OR StringId containing `"rebel_clan"`. This catches both freshly spawned rebels and "normalized" former-rebels whose StringId retains the marker.

### Startup Scan (v0.2.7.0)

Rebel clans that lost their settlement in a **prior save session** (or before the mod was installed) are not caught by `OnSettlementOwnerChanged` — that event only fires during live gameplay. Vanilla's `RebellionsCampaignBehavior.DailyTickClan` Part B kills homeless rebel clan heroes on the very first daily tick after session load, before any rescue path can fire.

When both `EnableClanSurvival` and `RescueRebelClans` are enabled, **`ScanAndRescueHomelessRebelClans()`** runs once at `OnSessionLaunched`:

1. Iterates all clans. For each: checks `IsRebelClanOrigin`, no settlements, not already tracked, not eliminated, not player, not bandit.
2. Filters to clans with living adult heroes (`IsAlive && !IsChild && (IsLord || IsMinorFactionHero)`).
3. Requires a living leader after vanilla succession; a dead/missing leader is logged and skipped without starting campaign actions.
4. Normalizes the clan (`IsRebelClan→false`, `IsMinorFaction→true`, removes from rebellion tracking).
5. Registers in the tracking dictionary and marks `_alreadyRescued`.

By clearing `IsRebelClan` before the first `DailyTickClan`, vanilla's "kill all heroes in homeless rebel clan" logic no longer targets these clans. They survive as independent minor factions.

### Persistence & Safety

- **Save/load:** Tracking dictionary persisted via `SyncData` with clan StringIds
- **Mod removal:** Rescued clans have `_isEliminated = false` and exist as normal independent factions. Without the mod they remain independent indefinitely — vanilla only re-destroys on specific triggers (leader death of old age, etc.)
- **Mid-campaign install:** Safe — startup scan covers existing homeless rebel clans; kingdom destruction rescue triggers on future events

### MCM Settings (Clan Survival, GroupOrder 25)

| Setting | Default | Description |
|---------|---------|-------------|
| Enable clan survival | On | Enables new rescues. Existing tracked clans continue safe maintenance until they join a kingdom or reach vanilla leader-death destruction |
| Grace period (days) | 30 | Currently unused and reserved for a possible future auto-placement flow. Changing it has no effect. |
| Culture match weight | 2.0 | Currently unused and reserved for possible future placement scoring. Changing it has no effect. |

---

## 32. Mod Compatibility System

### What it does

At game launch, Campaign++ automatically scans all active Harmony patches and game model replacements to build a complete picture of how every mod in the load order interacts with it. The result is presented as a brief per-mod report at the title screen and as a full MCM tab that remains available for the entire session.

There is no hardcoded mod list. Everything is inferred at runtime.

### Stage 1 — Harmony scan (title screen + campaign load)

First scan triggered by `OnBeforeInitialModuleScreenSetAsRoot`, after all Harmony patches from standard startup have been applied. A **second scan runs at `OnSessionLaunched`** (via `B1071_CompatibilityBehavior`) to catch mods that apply their patches lazily on campaign load rather than at startup — without this second pass, such mods (e.g. RBM) do not appear in the report.

1. Iterates `Harmony.GetAllPatchedMethods()` to get every patched method in the process.
2. For each method, calls `Harmony.GetPatchInfo()` and collects the patch owners.
3. Filters out infrastructure mods via `IsFrameworkId()`: Harmony, ButterLib, MCM, MBOptionScreen, UIExtenderEx, ModLib, BetterException, DebugMode, NativeModule, UnpatchAll, TaleWorlds core, BLSE, LauncherEx.
4. All surviving owners are added to `_allGameplayOwners` regardless of whether they overlap with Campaign++. This ensures every gameplay mod appears in the report.
5. For methods where Campaign++ also has a patch (`B1071HarmonyId`), co-patchers are evaluated for risk:
   - **Warning**: co-patcher has a bool-return prefix (can short-circuit), or a transpiler (rewrites IL), or Campaign++ has a transpiler on the same method.
   - **Caution**: both mods have postfixes on the same daily-calculation method (additive stacking, order-sensitive).
   - **Safe**: everything else (independent postfixes on non-sensitive methods).
6. Results are stored in `_harmonyConflicts` (one entry per mod per method) and `_allGameplayOwners` (one ID per mod). Both are stable for the session.

### Stage 2 — Model scan (campaign load)

Triggered by `OnGameLoaded` / `OnNewGameCreated` via `B1071_CompatibilityBehavior`. Checks whether other mods have replaced the game model classes that Campaign++ patches:

- Food / volunteer production / militia / prosperity / other campaign models
- For each model, the actual runtime type is compared against the expected type
- `IsDynamicallyHandled = true` if Campaign++'s Harmony patches target the base class and fire via `base()` automatically (no action needed)
- `IsSubclassOfExpected = true` if the replacement extends the expected type (patches typically still fire)
- `IsNativeAssembly = true` if the replacement comes from a first-party assembly (`TaleWorlds*`, `SandBox*`, `NavalDLC*`, `BirthAndDeath*`, etc.) — these generate no warning regardless of type mismatch. This covers the Naval DLC's thin decorator models which replace vanilla systems but delegate all logic back through `BaseModel`.
- Otherwise, `Risk = Warning` and the relevant feature may be inactive

Results stored in `_modelIssues`. Model check state is cleared and re-run each campaign session.

### Deduplication

Mods that register multiple Harmony IDs (e.g. Diplomacy shipping several sub-IDs) are grouped by `FriendlyModName()` before display. All IDs in the group are evaluated together so a single mod's combined risk is shown on one line.

### Risk labels and display

`GetModPopupStatus()` collapses a mod's full conflict list into one string:

| Output | Meaning |
|--------|---------|
| `Compatible` | No overlap, or all overlaps are Safe |
| `Minor overlap: Area - very likely fine` | Caution-level overlap in one area |
| `Minor overlap in N areas - very likely fine` | Caution-level in multiple areas |
| `Worth checking: Area` | Warning-level overlap in one area |
| `Worth checking in N areas` | Warning-level in multiple areas |

### Startup popup

`BuildPopupText()` generates the popup text:
1. Overall verdict line ("All mods running smoothly" or "One or more areas worth checking")
2. "Your mods:" section — one line per mod, name + status
3. Core game systems section (only shown if any model has Risk > Safe)
4. Footer pointing to the MCM tab

The popup is shown via `InformationManager.ShowInquiry` with two buttons: **OK** (dismiss) and **Copy Report** (copies text to clipboard via STA thread to satisfy WinForms clipboard requirements). A green in-game message confirms the copy. The popup can be suppressed via MCM toggle.

### MCM tab — Campaign++ Compatibility

Built by `B1071_CompatibilityFluentSettings`. Structure:

- **Summary group** (top of tab): "Report status" row (`"Partial — load any campaign"` until model checks run, then `"Up to date"`), "Don't show this popup at startup" toggle, "Running alongside" row (e.g. "AI Influence - runs fine"), "Core game systems" row, "Tip" row (`"Load a campaign - full report pops-up"`), "Open Full Report" button.
- **Per-mod groups** (one per detected gameplay mod): row per overlapping method with player-facing label and hover hint. Hint includes in-game effect, risk reason text, and optional MCM action bullets.
- **Core Game Systems group** (visible only after campaign load): one row per model check with short status text and full hover explanation.

All row values and hint texts are generated by helpers in `B1071_CompatibilityChecker` and are re-read every MCM redraw, so the tab reflects the current session state.

### Framework filter heuristic

`IsFrameworkId()` uses substring matching on the lowercased Harmony ID. The filter intentionally uses `lc == "0harmony" || lc.StartsWith("0harmony.")` rather than `lc.Contains("harmony")` to avoid false-positives on gameplay mods whose ID happens to contain the substring.

Currently filtered identifiers: `taleworlds`, `butterlib`, `butlib`, `.mcm`, `modlib`, `uiextender`, `mboptionscreen`, `betterexception`, `debugmode`, `nativemodule`, `unpatch`, `blse`, `launcherex`, `0harmony` (exact or prefix-match).

### Code files

| File | Purpose |
|------|---------|
| `B1071_CompatibilityChecker.cs` | Core scanner, risk scoring, text helpers, popup builder |
| `B1071_CompatibilityFluentSettings.cs` | MCM tab builder, clipboard helper |
| `B1071_CompatibilityBehavior.cs` | CampaignBehaviorBase bridge for model scan |
| `B1071_QuickSettingsFluentSettings.cs` | Quick Settings MCM tab builder — 28 ProxyRef-backed system toggles in 5 groups |

---

## 33. AI Recovery Routing

### Problem

Bannerlord already sends an under-strength lord toward a settlement to rebuild. Its
`AiVisitSettlementBehavior` scoring weighs volunteer availability, party size, wounded troops,
food, wages, distance, route availability, and settlement crowding — but it can only see
**vanilla volunteers**. Campaign++ veterans sitting in a settlement's register, castle elite
pools, and converted castle prisoners are invisible to it. A beaten lord would therefore walk
past a castle holding forty of his own veterans to reach a village with three volunteers.

### Solution

`B1071_AiRecoveryBehavior` **extends** the native scores rather than replacing the decision. It
never creates a travel route, issues a direct movement order, or invents a destination: it only
re-weights `GoToSettlement` entries Bannerlord has already produced and scored. An eligible lord
already inside a settlement uses the existing recruitment systems immediately instead of scoring
a meaningless visit to his current location.

### Registration order — why insertion, not `AddBehavior`

Bannerlord registers campaign behaviors forward, but `MbEvent` **prepends** listeners, so they
are invoked in reverse registration order. A behavior appended in the normal way would run
*before* the native AI scorers and observe an empty score set.

`SubModule.TryInsertAiRecoveryBehavior` therefore inserts the behavior into
`CampaignGameStarter.CampaignBehaviors` immediately **before**
`AiArmyMemberBehavior`, the first native scorer. Reverse invocation then places Campaign++
last, with every native settlement score already complete.

If the list is not an ordered `IList<CampaignBehaviorBase>`, or the expected native behavior is
absent, the feature **does not register at all** and logs one diagnostic. Vanilla behavior is
preserved; there is no partial mode.

### Recovery band

| Boundary | Rule |
|---|---|
| Start | `NumberOfAllMembers / PartySizeLimit < 0.60` (exclusive) |
| Stop | ratio reaches `0.80` (inclusive) |

Wounded soldiers count toward both, because Bannerlord's own `PartySizeRatio` includes them.
Using the same headcount avoids a party oscillating in and out of recovery as men are wounded
and healed. `B1071_AiRecoveryMath.MissingToStop` rounds the 80% target **up**, so a small party
is never left one man short of its own stop line.

### Protected states

A party is eligible only when it is an AI-led lord party and **every disqualifying** block
reason in `B1071_AiRecoveryBlockReason` is clear: army membership, map event, siege event,
transition, disbanding, retreat, starvation, a besieged current settlement, quest use, an
excluded party type, and any objective other than `Hold`, `None`, `PatrolAroundPoint`, or an
ordinary `GoToSettlement`. Engage/chase, escort, raid, besiege, assault, defend, and flee are
all excluded.

**`UrgentFood` is the one exception, and has been since v1.0.3.9.** `IsEligible` is
`(reasons & ~AdvisoryReasons) == None`, and `AdvisoryReasons` holds that flag alone: it is
computed, recorded and acted on, but it disqualifies nobody. The reason is that the threshold
behind it is not a hunger warning. `GetBlockReasons` raises it below
`MobilePartyAIModel.NeededFoodsInDaysThresholdForSiege`, which is `12f` — the stock vanilla
wants in hand *before committing to a siege*. A lord with eleven days of food is in no
difficulty at all, and vanilla never blocks anything on that number. It does the reverse:
`AiVisitSettlementBehavior` reads the same constant to **raise** the score of towns and
villages that sell food, so treating it as a disqualification had Campaign++ working against
the game with the game's own figure. In a 27-day run it was 24.3% of every rejection recorded
— the second-largest gate — while `PartyBase.IsStarving`, which is the game's only genuine
emergency signal and a separate flag, fired zero times. Starvation still blocks; running low
on rations does not.

Being food-short still changes the pass, in two places, and both are restraints rather than
permissions. The `DrainedCandidateScore` penalty is **skipped** for such a lord: that penalty
halves the score of a settlement that can supply no recruits, and for a hungry lord that may
be exactly the town vanilla has just scored up to sell him grain — recruiting is worth less
than eating. Because no penalty is written on that path, the scoring bar read before it is
still current and is not re-read. And **Recovery Takes Priority is forced off** for him, so
the chosen stop competes on merit instead of replacing the winning score outright. Vanilla's
food bonus is already inside the native score `CandidateScore` multiplies, so a settlement
offering both food and men still comes out ahead — but a castle full of elites can no longer
outrank the town that would have fed him. The system may suggest a destination to a hungry
lord; it may not overrule one.

**A lord below vanilla's recruiting money floor skips that same penalty**, and for a related
reason. The penalty means *this settlement is empty*; for a lord who cannot afford to hire
anywhere it would instead mean *this lord is poor*, which is a party condition written onto
the map. Nor would it fall evenly — the veteran and castle quoters apply no money floor, so
his castles would keep their full scores while every town and village was halved, and the
towns are precisely where vanilla is steering him to sell loot and raise the gold the floor
demands. The test is the same `IsBelowVanillaRecruitingMoneyFloor` the volunteer quoter uses,
so the rule has one definition rather than two that can drift; and as with the food case no
penalty is written, so the scoring bar read before it is still current.

`PatrolAroundPoint` is deliberately **not**, and was until v1.0.3.9. It is the AI's idle
state — a lord with nothing to do circles a point until something needs him — so treating it
as a commitment turned away precisely the lords this system exists for: weak, unoccupied, and
free to go and recruit. Player-clan companion parties remain eligible, but existing recruitment ownership
rules still apply — they cannot take veterans reserved away from player-clan AI use.

### Honest resource accounting

The scoring is only worth anything if the troops it counts can actually be bought on arrival.
Each candidate is quoted through read-only methods that share the **real** recruitment paths'
rules, spending one `B1071_AiRecoveryBudget` (party room, gold, manpower) in true arrival
order — **veterans, then castle elites, then converted prisoners, then the vanilla notable
board** — so no coin, no slot, and no point of manpower is counted twice:

- **Veterans** respect settling time, employer/access rules, the treasury reserve, party room, and manpower.
- **Castle elites** respect castle access, the same-clan 50% discount, the treasury reserve, party room, and manpower.
- **Converted prisoners** respect access, FIFO depositor costs, the treasury reserve, and party room. They continue to **cost zero manpower**, matching the real deposit path.
- **Vanilla notable volunteers** are quoted last, and are the only source Campaign++ **counts but never takes**. `QuoteNotableVolunteers` reads each notable's board through `VolunteerModel.MaximumIndexHeroCanRecruitFromHero` — relation decides how far down a board a given lord may reach, and slots past that index are visible on it but not his — then applies the settlement volunteer tier cap, `IsBlockedByWar`, the manpower pool, and `PartyWageModel.GetTroopRecruitmentCost` against the lord's purse. Its gold buffer is **1**, not the configured multiplier, because vanilla itself hires on a bare `PartyTradeGold > cost` and for a lord party `PartyTradeGold` *is* `LeaderHero.Gold`. It counts **one man per notable per vanilla pass, and vanilla makes seven of them**. `RecruitVolunteersFromNotable` does break out of its slot loop on the first successful hire, so one call yields at most one man from a notable — but `OnBeforeSettlementEntered` does not call it once. It computes a pass count (`num`) and loops `CheckRecruiting` that many times: 1 for a caravan, 1–3 for a party inside the player's army, and **7** for every ordinary AI lord party, which is the only population quoted here since `B1071_AiRecoveryBlockReason.Army` disqualifies the rest. v1.0.3.9's first cut read the inner `break` as the whole rule and quoted a single man per notable, undercounting a full board sevenfold and collapsing `CandidateScore`'s `usefulShare` so that recovery lost races it should have won. The cap is now `B1071_AiRecoveryMath.VanillaRecruitPassesPerArrival`. Because `Hero.VolunteerTypes` is a six-slot array, seven passes always outrun the board and the cap never fires in practice — what the quoter reports is every reachable slot, which is the true ceiling. It is written as the pass count rather than as the board size because that is the rule making it correct, and it is what would bind first if either number moved. Wages gate it the same way they gate vanilla: a party already over its payment limit is quoted zero, and each prospective recruit must fit inside the wage budget left after the ones quoted before him. Gold gates it twice over, and the second gate is the one that matters: `CheckRecruiting` refuses to hire at all below `HeroHelper.StartRecruitingMoneyLimit`, which is `50 + min(150, manCount) × 20` — a sixty-man lord needs 1,250 denars before vanilla will buy him a single recruit, and the floor **rises as he fills up**. Omitting it quoted a full board to a lord who could not take one man from it, and routing then sent him across the map to come back with nothing, which the wasted-trip telemetry recorded as a routed no-gain. The clan-purse clause is mirrored with it: a lord who does not lead his clan may draw on `StartRecruitingMoneyLimitForClanLeader` instead, and a generous one (`GenerosityMercenaryRecruitmentEffect`) is past the whole test — dropping that clause would have under-quoted every non-leader with thin coffers and a rich clan, which is most of them. Both clauses live in one helper, `IsBelowVanillaRecruitingMoneyFloor`, because the routing penalty above asks the same question and a rule mirrored from the game must not be mirrored twice.

**Which gold that floor is tested against is a real decision, and the first cut got it wrong.** It is the leader's live gold, not `B1071_AiRecoveryBudget.Gold`. The budget is spent down in Campaign++ arrival order — veterans, then castle elites, then volunteers — which is the right accounting for *how many men the purse stretches to*, and the wrong accounting for *whether vanilla will begin at all*. Vanilla hires the board from `OnBeforeSettlementEntered`, at the instant the party crosses the gate, whereas every Campaign++ transfer happens on a later hourly tick once the lord is already inside; the gold the game weighs against this floor is therefore the gold he rode in with. Testing the drained budget instead under-quoted every settlement holding both a veteran register entry and a board, and inflated `volGoldBlock` with lords who were never refused anything. Per-recruit affordability below still spends the shared budget, which is the constraint that budget exists to enforce.

**Quoting six slots per notable costs six times the model calls**, and this quoter runs per candidate settlement, per recovering lord, every campaign hour. `GetCharacterWage`, `GetTroopRecruitmentCost` — which builds an `ExplainedNumber` on every call — `GetRecruitCostForParty` and `GetManpowerChargePerTroop` are resolved once per troop into a `VolunteerCost` held in a small dictionary allocated per `QuoteNotableVolunteers` call, then reused across notables and slots; boards repeat heavily, because every notable of a culture offers largely the same basic tree. The cache is keyed by troop alone, which is only sound because it does not outlive the call: leader, settlement and party are fixed within one, and the four figures depend on nothing else. A static cache reused across calls would be keyed by too little and would be wrong the first time a second lord quoted the same town.

`B1071_AiRecoveryMath.AffordableUnits` applies the gold-buffer multiplier and leaves the lord at
least one coin, so a quote can never bankrupt a party that acts on it.

**Where a lord should go and what he can be handed are two different totals.** A quote reports
both. `Total` counts every source including the volunteer board, and that is what routing
scores on — all of the supply he will find is a reason to send him there, whoever hands it
over. `Actionable` is `Total` minus the board: the men Campaign++ can transfer itself.
`OnAiHourlyTick` recruits a lord standing in a settlement only when `Actionable` is positive,
because the board is hired by vanilla on arrival and acting on it here is a guaranteed no-op —
previously a lord parked in a town whose only supply was that board re-entered two no-op paths
every campaign hour for as long as he stayed.

Volunteer *recruitment* is still left entirely to Bannerlord: `RecruitVolunteersFromNotable`
takes the men when the lord arrives, and taking them here as well would take them twice. What
changed in v1.0.3.9 is that Campaign++ now **counts** them when deciding where to send him.
Until then it supplied only the awareness of its own troop sources — and since vanilla keeps
volunteer boards only in towns and villages while the castle quoter returns nothing for
anything that is not a castle, and the veteran register is sparse, every town and village in
the world quoted zero. Routing then had nothing to say about the settlements lords actually
recruit from, which is why the zero-quote share sat above 90% in the runs that motivated this
telemetry.

### Ranking and the winning score

```
adjusted = nativeScore × (1 + min(recruitable, missing) / missing)
```

A settlement that closes the whole gap doubles its native score; one that closes half adds 50%;
surplus beyond the gap adds nothing.

**And a settlement that can supply nobody is pushed down.** The formula above is a multiplier
of at least 1, so on its own it can only ever recommend — the emptiest village on the map keeps
its full native score and can still win. `B1071_AiRecoveryMath.DrainedCandidateScore` halves the
native score of every candidate that quoted zero, but **only when some other candidate quoted
men**. That condition is the whole design: pushing a lord away from every settlement at once
would leave him wandering the map, and a settlement is worth visiting for food, healing and
safety even when it has nobody to recruit. A negative native score is returned untouched —
halving −100 gives −50, which ranks *higher*, so penalising an already unattractive settlement
would make it look better. The penalty is applied before the **Recovery Takes Priority** gate,
deliberately: whether our own pick beats the native best is a separate question from whether an
empty village should be preferred to a full one, and a lord who ignores the suggestion should
still not be drawn to the emptiest settlement in reach.

The bar that gate measures against is then **re-read**. It was first taken before the penalties
were written, so if the settlement holding the top native score is one just pushed down, the gate
would have weighed our pick against a score that no longer exists and refused it — and an empty
settlement outscoring everything is exactly the case the penalty was written for, so the fix
would have cancelled itself. Re-reading is cheap and cannot misbehave:
`PartyThinkParams.SetBehaviorScore` updates an existing entry in place and never appends, and
the penalty only ever lowers, so the second pass walks the same list and can only move the bar
down. The current recovery target is kept while it stays within
10% of the best candidate (`IsWithinStickiness`), so a lord does not thrash between two nearly
equal castles as pools fluctuate.

With **Recovery Takes Priority** enabled, only the selected tuple is raised to
`highestCompletedNativeScore + max(0.1, 5% of that score)`. The flat floor matters at small
scores where 5% would not clear the gap. This beats *starting a new task*. Protected active tasks
are excluded before scoring ever runs. With the setting disabled, the selected tuple keeps its
adjusted score and competes normally against Bannerlord's other newly proposed tasks.

### Reservations

A winning score first creates a session-only proposal with a **12-campaign-hour soft destination
claim**. Another recovering lord will not *route* to the same settlement while that claim is
current, but no troops are removed or locked: the player, a lord already standing there, and every
otherwise eligible AI lord remain free to recruit from it.

What a claim does carry is a quoted figure, and `GetReservedSupply` subtracts it from every other
lord's quote on two independent grounds. Veterans, castle elites, and converted prisoners are the
stock of one settlement, so only a claim naming **this** settlement spends them — that is what
keeps the current-settlement pass honest now that it ignores the destination claim entirely.
Quoted volunteers are the exception and are **not** reserved as a headcount: they are vanilla's
stock and vanilla hands them to whichever lord arrives, so holding them for one lord would
promise something this system cannot deliver. The manpower those hires will cost the settlement
is another matter, and `QuoteSettlement` folds it into the quote's `Manpower` so the claim
carries it — without that, two recovering lords were quoted the same village pool twice over.
Manpower is pooled, so a claim on a bound village spends the same points as its town, and any claim
on the **same pool** is deducted whether or not it names the same settlement. A claim can therefore
apply on both grounds, on one, or on neither. Subtracting only the pooled half was enough to promise
the same forty veterans to two lords, because an orphan village or a same-settlement claim would
have gone uncounted.

On the next AI tick, the proposal becomes confirmed
intent only if Bannerlord is actually travelling to, or has reached, the exact target. Rejected
proposals release their claim. Claims are also dropped when they expire, become invalid, are
fulfilled, the party becomes ineligible, or the feature is turned off.

Only confirmed recovery intent is saved through `SyncData`: parallel lists of party IDs, target
settlement IDs, and expiry days. Reservations, proposals, quotes, and resource counts are not saved. On load,
`OnAfterSessionLaunchedEvent` restores a recorded intent only if the party and settlement still
exist, the party remains eligible and below the 80% stop line, the deadline remains current, the
target remains friendly and usable, and the party is still travelling to or already at it. The next
AI tick recalculates the best destination and a fresh claim from current stock.
An ordinary 60–79% settlement journey that was never selected by Campaign++ is therefore never
misclassified as recovery after loading.

Intents are swept for destroyed parties on the same pass that expires reservations. A party that
dies mid-recovery never thinks again, so the per-party cleanup on its own tick would never fire
and the entry would pin a dead `MobileParty` for the rest of the session.

Actual recruitment stays in the existing systems and pays every gold and manpower cost. An eligible
recovering lord already inside a settlement recruits immediately, and does so *before* any lord
still travelling there: a claim steers routing and holds no stock, so it must not turn a lord away
from the castle he is standing in. What that other lord has claimed here is still subtracted from
his quote, which is the part that stops the same men being promised twice. Every otherwise eligible
AI lord also receives an immediate castle recruitment pass on entry. Both paths use the real order:
veterans first, then castle elites, then converted prisoners. The daily castle pass remains as a
safety net.

Parties are re-anchored after roster changes to prevent settlement-exit flicker. The recovery path
calls that recruitment from inside the hourly think event, where the party's next behavior is chosen
moments later from the same scores, so its anchor either stands, because nothing was applied, or is
replaced by that fresh decision — neither is the stale-cache exit the anchor guards against.

The arrival and recovery passes do defer one thing the daily pass owns: the player's consignment
notice. They serve one lord at a time and can run several times an hour, so each banks the player's
depositor share in a session-only per-castle tally and only that castle's own daily pass prints the
total. The player still reads one consignment line per castle per day, matching the enslavement and
garrison paths; his gold is paid the moment the recruitment happens either way, so the tally never
holds money and is deliberately not saved.

### Failing safe

The handler runs once per AI lord party per campaign hour, which makes it the worst place in the
mod for an unguarded fault: anything systemic — a game update moving a member the pass reads —
throws hundreds of times a day. The whole handler is wrapped and every caught fault is counted.
After five, `_disabledThisSession` stops the pass for the rest of the session and drops its intents
and reservations, so every lord reverts to Bannerlord's own settlement scoring, which is exactly the
behavior with the setting switched off. The same count bounds the log at five lines instead of one
per party per hour. Nothing about the fuse is saved, so reloading retries.

`_inRecoveryPass` skips a nested entry into the handler. Recruitment writes to the party roster, and
the recruitment behaviors re-anchor the party with `RecalculateShortTermBehavior`, which drives the
party AI this handler is running inside; if that ever fed back into the think event, the alternative
to skipping is recursion inside a native callback. The cost of a skip is one hour of scoring for
that party.

`NotifyRecruitment` is wrapped for the mirror-image reason. The recruitment behaviors call it from
their own passes, and one of those — the daily castle tick — carries no try/catch, so an
escaping fault would land in a code path that predates this feature and was previously safe.

The save half of `SyncData` is wrapped for a third reason: it runs inside the player's save, and
deciding which intents to persist means reading live parties. A fault escaping there would abort the
save. Aborting the walk instead is cheap because the three parallel lists are appended together and
nothing between the three `Add` calls can throw — stopping early shortens them equally, and the
worst case is a save that carries no journeys, which loads exactly like a save written before this
version.

### Setting

`EnableAiRecoveryRouting` — *AI Lords Seek Campaign++ Recruits*, MCM group **AI Recovery**,
default **on**, mirrored in Quick Settings. Migration profile **v25** enables it for existing
profiles.

`AiRecoveryTakesPriorityOverNewTasks` — *Recovery Takes Priority*, default **on**. Migration
profile **v26** enables it for existing profiles. Turn it off when Campaign++ recruitment stops
should influence, but not automatically beat, Bannerlord's other new tasks.

`AiRecoveryIntentDurationDays` — *Recovery intent days*, default **1**, range **1–30**. A
confirmed recovery journey expires after this many campaign days if the party has not reached 80%.
Migration profile **v27** sets it to one day for existing profiles.

## 34. Settlement Revenue Tuning

### Problem

Vanilla pays a fief's owner a fixed share of a settlement's revenue with **no curvature anywhere
in the chain**. Three independent lines, each linear in a quantity the settlement's own growth
drives:

| Line | Vanilla formula | Scales with |
|---|---|---|
| Town tax | `Prosperity × 0.35`, then policy, loyalty, security, building factors | prosperity, no ceiling |
| Town tariff | `town.TradeTaxAccumulated / RevenueSmoothenFraction()` | daily trade commission |
| Village income | `village.TradeTaxAccumulated / RevenueSmoothenFraction()` | daily trade commission |

The tax side is stateless — recomputed from prosperity every call. The tariff side is a **stored
pool**, and this is the part that is easy to misread. `Town.OnInit` seeds it at
`1000 + RandomInt(1000)`; castles and villages start at zero, and
`CalculateInitialAccumulatedTaxes` gives each village `Σ(itemValue × dailyProduction) × (0.6 + 0.3
× rand) × RevenueSmoothenFraction()`. It then grows from the commission the settlement earns when
it **sells** goods: `SellItemsAction` computes `MBRandom.RoundRandomized(price ×
GetTownTaxRatio(town))` — 0.7 for a town, 1.0 for a village — scales it by
`GetTownCommissionChangeBasedOnSecurity`, and adds it to the pool
(`TaleWorlds.CampaignSystem.Actions.SellItemsAction:70,90`). Villages also accrue from their
villager parties trading, via `CalculateVillageTaxFromIncome`
(`CampaignBehaviors\VillagerCampaignBehavior.cs:334-336`). The only perk trickles are
`Trade.Tollgates` and `Trade.TravelingRumors`, a few denars per arrival.

**The pool is never reset, but it does reach a steady state — and saying otherwise would be
wrong.** Four drains act on it:

| Drain | Effect | Where |
|---|---|---|
| Tariff payout | `pool / 5` (+ perks + `TariffIncome` buildings); withdraws the **pre-perk** `num` | `CalculateTownIncomeFromTariffs` |
| `CrownDuty` policy | −5% of the pool per fief, to the ruling clan | `AddRulingClanIncome` |
| `RoadTolls` policy | −`pool / 30` per town | `AddRulingClanIncome` |
| Village tariff | −5% of the village basis | `CalculateVillageIncome` |

The 1/5 withdrawal is a proportional drain, so with it alone a town pool converges on five days of
its daily commission inflow; with `CrownDuty` and `RoadTolls` active it converges on roughly
**4.2 days** and stays there. Villages have no payout-side decay of this kind and only accrue, so
their pool is closer to a pure running total, though `LandTax` takes 5% of the basis from
non-owner villages when that policy is in force.

That correction matters for how the problem is stated. Late-game settlement income is large
because the **inflow** grows — a mature town's daily trade is many times a young one's — not
because the pool runs away without limit. The pool tracks the inflow, which is why the reference
constants below can be read as "days of trade throughput" at all. The defect is unchanged either
way: neither model has any curvature, so a town whose trade doubles pays its owner twice as much,
and nothing in the chain asks whether that town is already the largest in the world.

### Solution

`B1071_SettlementRevenueTuningPatch` adds three postfixes — one per line — that scale the amount
**the clan receives**, through `B1071_RevenueMath`:

```
taperCurve(ratio, curve) = 1 / (1 + ratio / curve) ^ (1 / curve)
excess                   = max(0, basis − knee)
ratio                    = excess / reference
retainedBasis            = strength × (min(basis, knee) + excess × taperCurve(ratio, curve))
scale                    = retainedBasis / basis
```

| Base | Reference | Basis | Knee range |
|---|---|---|---|
| Town tariff | 12,000 denars/day | pool / 5 | 0–30,000 |
| Village tariff | 2,500 denars/day | pool / 5 | 0–10,000 |
| Town tax | 3,960 above the fixed 40 floor | prosperity − 40 | 0–4,000, effective cap 3,960 |

The two pool references are the "five days of throughput" reading made concrete: a reference basis
of 12,000 corresponds to a town whose trade earns about 12,000 a day. They are derived, not
measured, which is why the telemetry below exists. The enabled preset still needs long-campaign balance validation.

The reference itself (ratio 1) is the legible point on the curve: it yields (1 + 1/curve)^(−1/curve)
of the strength — about 0.817 at curve 2. That figure is not fixed across the slider
(0.500 at curve 1, 0.946 at curve 4, 0.991 at curve 10), which is why the hints quote worked numbers
at specific curve values. Low values spread the reduction evenly across every settlement; high
values leave ordinary ones almost untouched and bend hard past the reference. The half-of-cut point
sits at r = curve × (2^curve − 1) ratios out — 1, 6, 21, 155, 10,230 for curves 1 through 10 — so
larger curves reduce income less; the sliders allow values from 1 to 10.

### Why a knee is not optional

The retained basis blends the strength share below the knee with the tapered excess above it.
Because the curve is at most 1, **strength is the maximum share retained, not a minimum.** A strength of 70 keeps every settlement at or
below 70% of its revenue: ordinary settlements sit just under that, and larger ones keep
progressively less without a floor — at curve 2 the spread between the smallest and largest
observed town was about 13 percentage points, and a settlement many times its reference approaches
0%. A player who reaches for the strength slider therefore cuts every fief they own, not just the
runaway ones.

`curve` cannot fix this. It controls how sharply the cut varies with size, but the plateau is still
the strength, so raising it to spare ordinary settlements also flattens the taper into a uniform
cut. The knee is what decouples the two: at or below it `ratio` is exactly 0, so the curve
contributes nothing and the settlement keeps exactly its strength share, while the deeper cut above
the knee lands only on the settlements that have outgrown it.

The transition is a ramp, not a cliff, and that is deliberate — a hard threshold would be a line
for a player to sit just underneath. At knee 6,000, curve 2, strength 70:

| Town tariff basis | 3,000 | 6,000 | 6,500 | 8,000 | 20,000 | 40,000 |
|---|---|---|---|---|---|---|
| Share kept | 70.0% | 70.0% | 69.9% | 69.3% | 59.9% | 48.8% |

Three properties hold and are tested: a knee of 0 reproduces the no-knee taper exactly, a negative
knee is read as 0 (a negative one would bite *harder* than leaving the slider alone), and the tax
knee is capped at `TaxReferenceProsperity − TaxKneeProsperity` = 3,960 — past that the exemption has
reached the 4,000 reference, so the slider would only be offering travel that behaves identically.
The tax slider therefore stops at 4,000.

The extra taper applies only to the portion above the knee. Applying its multiplier to the
whole basis instead can make larger settlements pay less total income when the knee exceeds
the reference. With village strength 70, curve 1 and knee 10,000, the corrected payout grows
from 7,000 at basis 10,000 to about 8,166 at basis 15,000. Curves above 1 grow sublinearly;
curve 1 approaches a finite total. Regression checks sweep the allowed curve and knee ranges.

### Why the payout and not the drain

The tariff classes use observation-only prefixes to capture the daily basis in Harmony
__state before the original methods withdraw gold. They use the original model's
RevenueSmoothenFraction(), without changing it. Postfixes use that captured basis for
both preview and payment, and for withdrawal-only telemetry. This prevents previews from
using a larger pool than actual payments.

The drain stays at its vanilla value. The settlement pays exactly what it paid before, and the clan
receives what is left after the taper. That asymmetry can only ever **remove** gold from the
campaign. The reverse — cutting the drain while paying the full amount — would create gold from
nothing, and it is why this is a postfix on the returned amount rather than a prefix on the pool.
The pool's trajectory is exactly vanilla — the drain and the inflow are untouched — so nothing
downstream of `TradeTaxAccumulated` sees any difference; only the owner's payout is smaller.

`RevenueSmoothenFraction` is **not** patched. It is shared with `CalculateOwnerIncomeFromCaravan`,
`CalculateOwnerIncomeFromWorkshop` and `AddMercenaryIncome`, so patching it would silently nerf
caravans, workshops and mercenary pay along with tariffs.

### Edge cases the code actually handles

These are not defensive padding. Each one is a case where the naive implementation is wrong:

- **Negative tariff results.** Town tax ends in `result.Clamp(0f, float.MaxValue)`, so it can never
  be negative. The tariff result is clamped **nowhere**, and it sums `AddPerkBonusForTown` and
  `AddEffectOfBuildings(TariffIncome)` contributions that can be negative. For a negative value,
  `value × (scale − 1)` is *positive*, so an unguarded taper would **raise** income. Every postfix
  returns early on a non-positive result.
- **Existing factors must not multiply the adjustment again.** ExplainedNumber.Add changes
  BaseNumber, which existing factors multiply. Apply first copies the resolved value and
  explanation lines into a fresh number with AddFromExplainedNumber, then adds one negative
  Revenue Taper line. Positive results are scaled once and rounded down; zero and negative
  results are left unchanged. This also preserves clamp contributions in the breakdown.
- **NaN curves.** `TaperCurve` tests `!(curve > 0f)` rather than `curve <= 0f`, because the latter
  is false for `NaN`. A corrupted settings file would otherwise produce a `NaN` scale and a broken
  income line.
- **Low loyalty.** Vanilla zeroes town tax with `AddFactor(-1)` below 25 loyalty. The result is
  already zero by the time the postfix runs, and the non-positive guard leaves it alone.
- **One denar.** Village income of 1 is left alone: an integer currency cannot taper it, and
  truncating to zero would make the steepest settings a no-op on exactly the settlements they exist
  to bite.
- **Perk and building immunity is not preserved.** Scaling the returned `ExplainedNumber` shrinks
  perks and building bonuses proportionally, so a governor's tax perks no longer fully offset the
  taper. Scaling only the base and leaving perks intact was the alternative; it was rejected because
  it breaks the relationship between a settlement's size and its owner's income, which is the one
  property this feature exists to restore.
- **`OnPlayerEarnedGoldFromAsset` over-reports.** Vanilla raises it with the *untapered*
  `bonuses.ResultNumber` while the drain uses the pre-perk `num`. Tapering the result without
  touching that call means asset-income tracking sees a larger figure than the player receives. It
  is cosmetic — no gold moves on it — and it is recorded here rather than silently fixed, because
  the mod uses `CampaignEventDispatcher` nowhere else and adding a second call site to correct a
  statistic is a larger change than this one.

### Telemetry

The end-of-session summary carries a basis range, followed by two distribution lines:

```
tariffBasis(town=min-max(n),village=min-max(n),refs=12000/2500)
TariffBasisSpread town: atleast2048=141,atleast4096=196,atleast8192=241,...
TariffBasisSpread village: atleast512=903,atleast1024=1488,atleast2048=2103,...
```

Recorded from the two tariff postfixes on the `applyWithdrawals` path, which is the daily tick — the
finance panel calls the same method with `applyWithdrawals` false, so recording unconditionally
would have logged every settlement several times per UI refresh. Towns and villages are tracked
apart because their pools differ by roughly a factor of five. The village postfix records the basis
**before** its one-denary early return, so a village too small to taper is still counted: that is
precisely the settlement a knee is chosen from.

The counts are per power of two, rendered from the lowest to the highest non-empty bucket with the
empty ones between them printed as zero, so a bimodal spread stays visible. Memory is a fixed
seventeen integers per series however long the session runs, which is why the distribution is kept
as octaves rather than as a sorted list of every sample.

**A range alone cannot calibrate anything.** A 46-day session reported `town=94-13035`, which is
equally consistent with a median of 500 and with a median of 9,000 — and the second would mean the
reference constant was four times too low. The octave counts are what answer that, and they are
what the knee settings are read against. **Nothing in the mod reported a tariff figure before
this**, so the defaults are a reasoned estimate and a session log is what corrects them.

The observed range from that first play-test also shows the town reference is set high: the largest
town of the session reached basis 13,035 against a 12,000 reference, so the taper never got out of
first gear. The village reference of 2,500 is the more aggressive of the two, since villages were
observed up to 5,618 — 2.2× their reference, and 43% of the town maximum, where the "roughly a
fifth" assumption in the code comments holds only for the middle of the distribution.

### Settings

MCM group **Settlement Revenue**, `GroupOrder = 27`. The master toggle defaults **on**.
Town tariffs use strength **90**, curve **1**, knee **2,000**; villages use **90 / 1 / 500**.
Tax remains **100 / 2 / 0**, so the tax taper is bypassed. Profile **v28** applies all ten
values once to older profiles, preserving unrelated settings. Once current, migration leaves
player customization untouched, including an explicit opt-out.

| Setting | Range | Default |
|---|---|---|
| `EnableSettlementRevenueTuning` | — | on |
| `SettlementTariffStrengthTown` | 5–100 % | 90 |
| `SettlementTariffCurveTown` | 1.00–10.00 | 1.00 |
| `SettlementTariffKneeTown` | 0–30,000 denars/day | 2,000 |
| `SettlementTariffStrengthVillage` | 5–100 % | 90 |
| `SettlementTariffCurveVillage` | 1.00–10.00 | 1.00 |
| `SettlementTariffKneeVillage` | 0–10,000 denars/day | 500 |
| `SettlementTaxStrength` | 5–100 % | 100 |
| `SettlementTaxCurve` | 1.00–10.00 | 2.00 |
| `SettlementTaxKnee` | 0–4,000 prosperity | 0 |

Strength is a percentage because the curve multiplies a share in `[0, 1]`; a raw 0..1 float slider
would spend most of its travel in a range no player would ever pick. The knee is an integer
denar-per-day figure for the two tariff bases and a prosperity figure for tax, because prosperity is
the one of the three a player can read straight off the settlement screen. The master toggle is
mirrored in Quick Settings.

Optional comparison figures at strength 60, curve 2, knee 0 (not the shipped preset):

| Town tariff, daily basis | 3,000 | 12,000 | 48,000 |
|---|---|---|---|
| Share kept | 57% | 49% | 35% |

| Town tax, prosperity | 2,000 | 6,000 | 20,000 |
|---|---|---|---|
| Share kept | 54% | 45% | 32% |

Anything at or below 40 prosperity is never touched, which keeps villages out of a tax setting aimed
at runaway towns. These comparison values are covered by `RevenueMathTests`. The enabled default preset
approaches 12,600/2,700 denars for town/village trade bases before additional payout modifiers.
The ceilings follow strength × (knee + reference) at curve 1; they are not caps on total fief income.

### Compatibility

`DefaultSettlementTaxModel` and `DefaultClanFinanceModel` are registered by
`Campaign.Current.Models`, both as `AddModel(new Default…())`. A mod that replaces either model
outright with its own `AddModel` shadows these postfixes entirely — they attach to the vanilla type
and would simply never run. The startup compatibility snapshot reports `modelOverrides`, so that is
visible in the log rather than assumed. EconomyOverhaul v1.1.6 declares neither of the three patched
methods, so it does not collide.

### Verification

RevenueTuningRegressionTests exercises the installed ExplainedNumber with positive and
negative factors, fractional amounts, clamped totals and values above int.MaxValue.
It also calls the real prefix/postfix pairs around a simulated vanilla withdrawal to verify
identical previews/payments and unchanged pools. This is headless validation, not a live campaign.

`RevenueMathTests` (fast suite) pins the curve against its own definition written out independently,
the bound that a scale can never exceed the strength asked for, monotonicity, sub-linear growth, the
knee, the edge cases above, and the hint figures. `RevenueTuningPatchBindingTests` (game suite)
applies all three patch classes to the installed game the way `SubModule` does and asserts the
postfix attached, because `PatchAssemblySafely` logs a patch-time `HarmonyException` and carries on:
**a postfix that fails to bind is a skipped patch with a quiet log line, and nothing else in the
suite would notice**, since `DeclarativeHarmonyPatchTargetStillExists` resolves the target by
reflection and never asks whether the postfix attached. Harmony binds parameters by name, so that
test is what turns a renamed parameter from a silent no-op into a failure. It also asserts that each
knee setting is read by the patch that owns it and passed to the math call following its own curve —
a slider that compiled but was never consumed would look like a working toggle and do nothing. Town
tax is the only one of the three targets this mod has never patched, and its parameter names are
asserted directly there.

All three targets are public overrides named with nameof(). They are also registered in
VerifyCriticalPatches and the matching CriticalTargets test list. Tariff binding tests verify
both the observation prefix and the payout postfix.

The release snapshot with the enabled preset and profile v28 migration passed 578 fast
tests and 219 game-backed tests. Migration coverage checks the one-time preset, unrelated
setting preservation and subsequent player customization. In-game validation remains outstanding.
