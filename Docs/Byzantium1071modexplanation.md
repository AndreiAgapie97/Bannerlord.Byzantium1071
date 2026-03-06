# Byzantium 1071 — Complete Mod Explanation

**Version:** 0.2.6.1  
**Target Game:** Mount & Blade II: Bannerlord (tested on v1.3.15)  
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

---

## 1. Overview

Byzantium 1071 is a **realism and strategic depth** mod for Bannerlord. It introduces a **manpower economy** that makes every recruitment decision meaningful, links war and diplomacy to population fatigue, slows down AI snowballing, and provides a rich intelligence overlay so you always know the state of the world.

The mod is designed to be **fully symmetric**: the same rules that limit *your* recruitment limit the AI's recruitment. No asymmetric player protection or difficulty scaling — just a realistic economy applied to everyone.

Everything is configurable via the Mod Configuration Menu (MCM). If a feature is not to your taste, almost every system can be individually toggled.

Localization uses Bannerlord's standard keyed `TextObject` workflow (`{=key}` + `SetTextVariable`) backed by language dictionaries in `_Module/ModuleData/Languages/` (English, Simplified Chinese, and French), so text follows the player's selected game language.

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
6. Castle receives `localTrickle + supplyTransfer`; supply town loses `supplyTransfer`

**Edge cases:**
- **No same-faction town exists** (last faction holdout is a castle): castle receives only the local trickle (1/day).
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
- Unaffordable troops show greyed-out (their `CanBeRecruited` flag is set false by AND-ing with vanilla's gate — never re-enabling what vanilla disabled for other reasons)
- The Recruit All button is disabled if sequence would fail (AND-ed with vanilla's gate)
- The Done button shows a tooltip with the blocker troop/pool/cost details

### Culture discount
If your party leader shares culture with the recruitment settlement, a configurable discount (CultureCostPercent) applies.

---

## 7. AI Recruitment Gate

The mod patches the AI recruitment pipeline (`RecruitmentCampaignBehavior.ApplyInternal`, a private internal method). Before AI parties recruit at a settlement, the same `CanRecruitCountForPlayer` check is applied. 

If the pool lacks manpower:
- **Player context**: yellow on-screen message
- **AI**: debug log (visible when AI logging is enabled in MCM)

The AI sees the same resource constraints you do. A kingdom that has fought many battles and drained its pools will struggle to recruit new troops — a natural attrition mechanic.

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
3. **Pending Prisoners** (still serving their waiting period)

Players access this via the castle game menu option "🏰 Recruit troops". AI lords recruit automatically during their daily visit.

### Source 1: Elite Troop Pool

Each castle generates T4/T5/T6 troops matching the settlement's culture. The elite pool:

- Regenerates daily from the castle's manpower pool (1–3 troops/day based on prosperity)
- Each regenerated troop costs `CastleEliteManpowerCost` (default: 1) manpower
- Is capped per castle by `CastleElitePoolMax`
- Consists of a random distribution of culture-specific troop types from both the basic and noble troop trees
- Anyone visiting a non-hostile castle can recruit from the elite pool (both player and AI; only hostile castles are blocked)

### Source 2: Converted Prisoners

T4+ prisoners held at a castle for a configured number of days become recruitable:

| Tier | Required Days (default) | Gold Cost (default) |
|------|------------------------|---------------------|
| T4   | 7 days                 | 1,200g              |
| T5   | 14 days                | 2,500g              |
| T6   | 21 days                | 5,000g              |

- Day tracking is per troop *type* per castle (not per individual prisoner)
- Once the waiting period is met, all prisoners of that type at that castle become ready simultaneously
- Prisoner recruitment costs **zero manpower** — they are already captured, not drawn from the population

### Source 3: Pending Prisoners

T4+ prisoners still serving their waiting period. Visible in the recruitment screen but not yet recruitable.

### Low-Tier Prisoner Auto-Enslavement

T1–T3 prisoners at castles are automatically enslaved to the nearest town's slave market each day (requires Slave Economy enabled). This keeps the prison roster clean and feeds into the town-level slave economy system.

**Enslavement income:** The castle owner receives the dynamic slave market price per enslaved prisoner. The price is obtained from `nearestTown.Town.GetItemPrice(_slaveItem)` — the same town that receives the slave goods. Gold is created via `GiveGoldAction.ApplyBetweenCharacters(null, owner, totalIncome, true)`. This makes even T1 prisoners valuable (slave market price may far exceed their ransom value).

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
- **Same-clan lords recruit for 50% gold cost** — lords whose clan matches the castle's `OwnerClan` pay half price (same household, shared resources)
- **Cross-clan lords pay gold** routed to the castle owner via `GiveGoldAction.ApplyBetweenCharacters(party.LeaderHero, settlement.Owner, cost)`
- **Per-unit processing (v0.1.7.1):** AI prisoner recruitment processes one unit at a time, peeking the current depositor and checking affordability before each recruit. This ensures correct depositor attribution when a troop type has mixed depositors in the FIFO queue
- Elite recruitment drains manpower (if `CastleRecruitDrainsManpower` is enabled, default: yes)
- Prisoner recruitment costs **zero manpower**
- After modifying a party's roster, `SetMoveGoToSettlement` + `RecalculateShortTermBehavior` is called to re-anchor the party at the castle, preventing the "flickering" bug where roster changes invalidate the AI's cached settlement target

### Player Castle Recruitment

Player recruitment follows the same clan-based rules:

- **Recruiting from own clan's castle:** 50% gold discount (same-clan pricing)
- **Recruiting from another clan's castle:** Full gold cost, paid to the castle owner
- Gold transfers use `GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, castle.Owner, goldCost)` — direct hero-to-hero transfer
- The recruit message shows effective cost (50% for same-clan elites, full cost for cross-clan)
- **Batch recruit:** Each section (Elite and Ready) has a "Recruit All" button that loops through all available troops using snapshot iteration (avoids collection modification during loop). Single `RefreshLists()` call at the end for efficiency.

### Player Prisoner Deposit

Players can deposit prisoners at castles for processing through the consignment model:

- **Own castle:** Use vanilla's "Manage prisoners" option (no consignment split — you already own the castle)
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

**Elite pool recruitment** is unchanged — elite troops are castle-generated (no depositor). Same-clan free, cross-clan pays castle owner.

**Net effect:** Both the capturing lord and the castle owner benefit from the prisoner pipeline. A lord who captures 50 prisoners and deposits them at an allied castle receives 70% of all enslavement and recruitment income, even though they moved on to fight elsewhere. The castle owner earns a 30% commission for housing, processing, and providing the infrastructure.

**Player deposit parity:** The behavior listens to `CampaignEvents.OnPrisonerDonatedToSettlementEvent` — the campaign event vanilla fires when the player uses the dungeon menu's "Donate prisoners" party screen. When the player deposits prisoners at another clan's castle, depositor tracking is established via `RecordDeposit`, giving the player their consignment share of all future income from those prisoners. Own-clan castles are skipped (same-clan economics make tracking unnecessary). An info message notifies the player of the consignment terms.

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
2. **TrackDays** — Increment day counters for T4+ prisoners
3. **RegenerateElitePool** — Add culture troops from manpower
4. **AiAutoRecruit** — AI lords take from elite pool + ready prisoners
5. **GarrisonAbsorbPrisoners** — Garrison absorbs ready prisoners (1/day)

### Persistence

All castle recruitment data is saved with the campaign:

- `_prisonerDaysHeld`: per-castle per-troop day counters (prisoner conversion tracking)
- `_elitePool`: per-castle per-troop stock counts (culture elite pool)

Both dictionaries are flattened into parallel lists for `SyncData` serialization. Stale entries are cleaned up when prisoners are fully recruited or removed from the prison roster.

### MCM Settings (Castle Recruitment Group)

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableCastleRecruitment` | true | Master toggle for the entire system |
| `CastlePrisonerAutoEnslaveTierMax` | 3 | Unified enslavement tier cap (player + AI). Controls all enslavement paths: player town menu, AI town entry, castle auto-enslave. T4+ must go to castle or ransom. |
| `CastleRecruitT4Days` / `T5Days` / `T6Days` | 7 / 14 / 21 | Days before prisoners become recruitable |
| `CastleRecruitGoldT4` / `T5` / `T6` | 1200 / 2500 / 5000 | Gold cost per recruit by tier |
| `CastleElitePoolMax` | 20 | Maximum elite troops per castle |
| `CastleEliteRegenMin` / `RegenMax` | 1 / 3 | Daily regen range (scales with prosperity) |
| `CastleEliteManpowerCost` | 1 | Manpower cost per elite troop regenerated |
| `CastleRecruitDrainsManpower` | true | Whether elite recruitment drains manpower |
| `CastleEliteAiRecruits` | true | Whether AI lords can recruit from castles |
| `CastleHoldingFeePercent` | 30 | Commission % the castle owner receives when another clan's prisoners are processed |


### v0.1.7.2 Audit Fixes

**UI clan-waiver display fix:** The castle recruitment screen now correctly reflects clan waivers.
- At your own clan's castle: elite troops show "Elite (Free)" and the recruit button is enabled regardless of gold.
- Same-clan depositor prisoners show "Ready (Free)" with the button enabled.
- Hint text: "Recruit one {name} (same clan — free)" for elites, "(clan waivers — free)" for waived prisoners.
- Previously, the UI checked raw gold cost without waivers, graying out buttons even when recruitment was free.

**FIFO depositor tracking fix:** RecordDeposit now always appends new entries instead of consolidating same-hero entries. This preserves strict FIFO ordering when the same lord deposits the same troop type at the same castle with interleaved deposits from other lords. Prevents over-crediting earlier depositors when processing stops midway (e.g., town runs out of gold).

**Removed CastleEliteAiMaxPerDay MCM setting:** This legacy setting was never used in code — AI lords were already uncapped, recruiting up to their party limit. Setting removed from MCM entirely.

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

Two independent systems improve autoresolve and wound fidelity for higher-tier troops.

### Tier Survivability

When `DefaultPartyHealingModel.GetSurvivalChance` would return a value < 1.0 for a non-hero troop, a tier-based bonus is added to survival chance (wound vs. kill decision):

| Tier | Survival Bonus |
|------|---------------|
| T1   | +0%  |
| T2   | +5%  |
| T3   | +10% |
| T4   | +15% |
| T5   | +20% |
| T6+  | +25% |

**Effect**: Veterans are more likely to survive as wounded rather than die outright. Additive on top of vanilla base survival chance, capped at 100%.

### Tier Armor Simulation

In autoresolve, `DefaultCombatSimulationModel.SimulateHit` output is reduced for higher-tier struck troops, modelling superior armor and battle experience:

| Tier | Damage Reduction |
|------|----------------|
| T1   | −0%  |
| T2   | −6%  |
| T3   | −12% |
| T4   | −18% |
| T5   | −24% |
| T6+  | −30% |

**Effect**: Reduced damage lowers the probability of the fatal-hit gate firing per autoresolve tick. Combined with tier survivability: fewer casualty checks trigger AND better outcomes within each check.

Heroes are unaffected by both systems. Both patches activate only in autoresolve simulation (not live battles). Both apply symmetrically to player and AI.

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

Default: **Moderate**. 100 T6 troops at Moderate cost 6000 denars/day. Heroes and companions are excluded.

**Caravan Guards wage exemption:** CaravanGuards are excluded from wage inflation entirely — they keep vanilla wages so caravan businesses remain profitable at all presets.

**Mercenary wage:** Mercs retain vanilla's 1.5× daily wage on top of the tier factor — expensive to maintain. Their hire cost is normalised to match regular soldiers (occupation premium cancelled). Cheap to recruit, expensive to keep.

### Garrison Wage Discount

Garrison parties pay a configurable percentage of field troop wages. Default **60%** (40% discount). Historically, garrison soldiers received reduced coin pay supplemented by shelter, rations, and equipment from the local lord.

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

All 13 tabs use a 5-column layout. Click any column header to sort; click again to reverse.

| Tab | What it shows |
|-----|---------------|
| **Current** | Manpower pool, regen, war pressure band, and recovery status for the selected/nearest settlement |
| **Nearby** | All towns/castles sorted by distance, with manpower and % fill |
| **Castles** | All castles, sorted by manpower, prosperity, or regen |
| **Towns** | All towns, same |
| **Villages** | All villages with hearth, faction, and bound pool |
| **Factions** | Faction \| Ruler \| Treasury \| Manpower \| Prosperity (CK3-style order) |
| **Armies** | Per-faction military power (Σ tier²×count), troop count, and war exhaustion |
| **Wars** | Active wars with exhaustion, peace pressure, territory delta (e.g., "Empire +2 / Sturgia −1"), and active truces |
| **Rebellion** | All towns sorted by rebellion risk score, with L/S/Food stats, time-to-rebel estimate, and culture mismatch |
| **Prisoners** | All imprisoned nobles — captor, holder, location |
| **Clans** | Clan loyalty/defection risk and recruitment opportunity scores |
| **Characters** | All live heroes and wanderers with approximate locations and relation symbol (♥ ▲ ● ▼ †) |
| **Search** | Free-text search across heroes, settlements, armies, clans, kingdoms, and **trade good / food prices** at every town |

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

### Hotkey note

Letter hotkeys (M, N, K) are suppressed while the Search tab is active to prevent search query input from triggering the toggle.

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
| Recruitment Cost | Base cost per troop, culture discount |
| Castle Recruitment | Enable/disable; prisoner tier threshold; T4/T5/T6 days and gold costs; elite pool max, regen range, manpower cost; AI recruit toggle |
| Combat Realism | Enable/disable tier survivability; enable/disable tier armor simulation |
| Army Economics | Hire & upgrade cost preset (0–3); daily wage preset (0–3); garrison wage % of field (default 60); garrison discount toggle |
| Overlay | Hotkey (M/N/K/F9-F12), panel position, default tab, rows per page |
| War Effects | Enable/disable; raid/battle/siege/conquest drain %; conquest retain % |
| Dynamic Conquest Protection | Enable toggle; depleted retain %, depleted threshold % |
| Delayed Recovery | Recovery days and penalty % for raid/siege/conquest; depleted penalty reduction |
| Bounded Stochasticity | Enable variance; volunteer variance %; recovery variance % |
| Immersion Modifiers | Seasonal regen; peace dividend; culture discount; governor bonus |
| Alerts & Militia | Manpower alert thresholds; militia-manpower link min/max scales |
| War Exhaustion | Enable/disable; decay rate; per-event gain values; regen penalty divisor |
| Diplomacy | 27 settings for war/peace support scaling, bands, cooldowns, truces |
| Developer Tools | Debug message toggles, AI logging, telemetry display, verbose mod log, settings profile version |
| Tooltips | Settlement manpower tooltip enable/disable |
| Village Investment | Enable/disable; 3-tier costs, durations, hearth/relation/influence/power bonuses; power cap; cross-clan relation; AI toggle |
| Town Investment | Enable/disable; 3-tier costs, durations, prosperity/relation/influence/power bonuses; power cap; cross-clan relation; AI toggle |

### Quick Settings Tab (v0.2.6.0)

A separate MCM tab (**Campaign++ - Quick Settings**) mirrors all 21 system master toggles in 5 groups:

| Group | Toggles |
|-------|---------|
| Core Systems | War Effects, War Exhaustion, Diplomacy Pressure, Forced Peace, Delayed Recovery, Militia Link |
| Economy & Investment | Slave Economy, Village Investment, Town Investment, Minor Faction Economy, Garrison Wage Discount |
| Recruitment & Military | Castle Recruitment, Open Castle Access, Combat Tier Survivability, Combat Tier Armor Simulation, Clan Survival |
| Province & Governance | Governance Strain, Frontier Devastation, Castle Supply Chain |
| Immersion & Modifiers | Seasonal Regen, Peace Dividend, Culture Discount, Governor Bonus, Overlay, Manpower Alerts |

All toggles use `ProxyRef<bool>` wrappers around `B1071_McmSettings.Instance` — changing a Quick Settings toggle changes the corresponding full-tab setting and vice versa. Built by `B1071_QuickSettingsFluentSettings` using the same FluentGlobalSettings pattern as the Compatibility tab.

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
| **Clan Survival** | Rescue events, rescue skips (with reasons), heir promotions, rebel clan rescues, rebel normalization, kingdom eliminations, war clearing, tracking start/stop — logged to both rgl_log and session file |

All logging is centralized through the `B1071_VerboseLog` static helper class with format `[Byzantium1071][Subsystem] message`. ClanSurvival events additionally write to the session file log via `B1071_SessionFileLog.WriteTagged` for post-session analysis.

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

**Required dependencies** (must load before this mod):
- `Bannerlord.Harmony` ≥ v2.3.3
- `Bannerlord.ButterLib` ≥ v2.9.18
- `Bannerlord.UIExtenderEx` ≥ v2.12.0
- `Bannerlord.MBOptionScreen` (MCM) ≥ v5.10.2

**Confirmed compatible mods:**
- **Bannerlord.EconomyOverhaul** (v1.1.6) — Full compatibility. 23/23 B1071 systems work. EO’s food model replacement is handled via runtime dynamic patching; volunteer model converted to Harmony Postfix to avoid AddModel collision. EO’s village-level slave system and B1071’s town-level slave system are completely independent and complementary.
- **CavalryLogisticsOverhaul** (v1.2.0) — Fully compatible. No overlapping patch targets. Cavalry wage costs may compound (intentional — both mods make cavalry more expensive).

**Patches that touch private methods** (fragile on game updates):
- `RecruitmentCampaignBehavior.ApplyInternal` — private, string name
- `RecruitmentVM.OnDone` — private, string name
- `RecruitmentVM.RefreshScreen` — private, string name
- `RecruitmentVM.RefreshPartyProperties` — private, string name

If Bannerlord renames any of these methods, the corresponding patch will silently not apply. Patches on public methods use `nameof()` and have compile-time safety.

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

**Prisoner enslavement (AI)** — When an AI lord party enters a town, **Tier 1–3** non-hero prisoners in their prison roster are automatically enslaved via the Harmony Prefix in `B1071_CastlePrisonerDepositPatch`. The town **buys** each slave at the current market price — gold is deducted from `Town.Gold` and paid to the lord via `GiveGoldAction.ApplyForSettlementToCharacter` (properly clamped, fires campaign events). If the town runs out of gold mid-batch, remaining T1–T3 prisoners stay with the lord and fall through to vanilla sell behavior (ransom gold). **Tier 4+ prisoners are not enslaved** — they are left for the vanilla ransom/release pipeline or deposited at castles for recruitment conversion. Both player and AI use the same `CastlePrisonerAutoEnslaveTierMax` setting. AI lords also deposit any slave items already in their inventory (from raids) into the town market on arrival.

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

Each slave in the town market consumes food daily. This is visible in the food tooltip as **"Slave Upkeep -X.XX"**. At default settings (0.05 food/slave/day):

| Slaves | Food drain |
|--------|-----------|
| 50 | -2.5/day (noticeable) |
| 100 | -5.0/day (significant — roughly a village's output) |
| 200 | -10.0/day (severe — multiple villages' output) |

Historically, enslaved labourers received subsistence rations comparable to garrison troops (~0.04–0.06 food units/day). This creates a natural economic cap on slave hoarding: at some point, the food cost of maintaining a large slave population outweighs the prosperity and construction benefits, forcing strategic balance.

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

**Rescued rebel clans** (v0.2.7.0) also qualify for Frontier Revenue after normalization. When the Clan Survival system rescues a rebel clan, it sets `IsMinorFaction = true` via reflection, making the rescued clan eligible for the unaligned stipend (`clanTier × 400 denars/day`) until it enters mercenary service or joins a kingdom.

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

### MCM settings (Provincial Governance group)

| Setting | Default | Effect |
|---------|---------|--------|
| Enable governance strain | On | Master toggle |
| Strain decay per day | 0.3 | How fast strain decays toward 0 |
| Max loyalty penalty | 3.0 | Loyalty penalty/day at full strain |
| Max security penalty | 2.0 | Security penalty/day at full strain |
| Max prosperity penalty | 1.0 | Prosperity penalty/day at full strain |
| Strain cap | 100 | Maximum strain a settlement can accumulate |

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

In vanilla Bannerlord, when a kingdom is destroyed, **every member clan is immediately annihilated** — all heroes are killed via `KillCharacterAction.ApplyByRemove`, all parties disbanded, fiefs transferred, and the clan is marked as eliminated. This is historically unrealistic (noble families survived the fall of empires) and reduces late-game variety as powerful clans vanish forever.

### Solution

Two Harmony prefixes intercept the destruction chain **before** heroes are killed:

1. **`DestroyClanAction.Apply`** — the default path used when `DestroyKingdomAction.Apply(kingdom)` iterates member clans.
2. **`DestroyClanAction.ApplyByClanLeaderDeath`** — the leader-death path used when `DestroyKingdomAction.ApplyByKingdomLeaderDeath(kingdom)` is called after the kingdom leader dies with no successor clans.

Neither `ApplyByFailedRebellion` nor genuine single-clan leader-death (where no heirs exist) are intercepted — those are legitimate destructions.

### Rescue Flow

```
DestroyClanAction.Apply/ApplyByClanLeaderDeath called
  └── TryRescueClan prefix
        ├── Gate checks: enabled? not player? not bandit? has kingdom? kingdom eliminated?
        ├── Living adult lord check: any heroes alive, not children, not companions?
        ├── Leader promotion: if leader dead, promote heir via ChangeClanLeaderAction
        │     └── No valid heir? → let vanilla destroy (return true)
        ├── Step 1: Fief transfer (while clan.Kingdom is still set)
        │     └── ChooseHeirClanForFiefs → ChangeOwnerOfSettlementAction.ApplyByDestroyClan
        ├── Step 2: Kingdom detach via public setter (clan.Kingdom = null)
        │     └── SetKingdomInternal(null) → LeaveKingdomInternal():
        │           zeroes influence, removes from kingdom clan list,
        │           removes heroes/fiefs/warparties from tracking,
        │           disbands armies, updates banner colors,
        │           sets LastFactionChangeTime
        ├── Step 3: Clear inherited wars (MakePeaceAction.Apply for each)
        │     └── Skips constant-war factions (DiplomacyModel.IsAtConstantWar)
        ├── Step 4: Register in ClanSurvivalBehavior tracking dictionary
        └── Return false (skip vanilla destruction)
```

**Operation order is critical:**
- Fiefs transfer FIRST because `ChooseHeirClanForFiefs` needs `clan.Kingdom != null` to search among surviving kingdom clans.
- Kingdom detach SECOND via the public `Clan.Kingdom` setter (not reflection — the setter is public, not internal). The setter calls `SetKingdomInternal(null)` → `LeaveKingdomInternal()` which handles influence zeroing, army disbanding, banner color updates, faction tracking cleanup, and `LastFactionChangeTime`.
- Wars clear THIRD because after detach, `FactionsAtWarWith` returns the clan's own inherited stances. The `IsAtConstantWar` check (matching vanilla's pattern from `ChangeKingdomAction.LeaveByKingdomDestruction`) prevents attempting to make peace with bandit/permanent-war factions.
- After the prefix returns false, `DestroyKingdomAction` still calls `kingdom.RemoveClanInternal(clan)` — this is benign because `MBList.Remove()` is idempotent (the setter's `LeaveKingdomInternal` already removed the clan).

### Independent Tracking (v0.2.0.1)

After rescue, clans enter a tracked independent state:

- The clan becomes an independent faction (`IsMapFaction = true`, `Kingdom == null`)
- Heroes continue normal world behavior under vanilla AI
- The behavior tracks rescued clans via `Dictionary<string, float>` (clan StringId → rescue campaign day)

Daily tick responsibilities in the current implementation:

- Drop invalid/eliminated clans from tracking
- If a tracked clan has already joined a kingdom (vanilla behavior, diplomacy, or another mod), stop tracking it
- For still-independent tracked clans, keep inherited-war cleanup enforced so they remain neutral

No scripted kingdom scoring or forced mercenary placement is executed in the v0.2.0.1 rescue flow.

### Edge Cases

| Scenario | Handling |
|----------|----------|
| Player clan in destroyed kingdom | Skipped — vanilla handles player-death separately |
| No living adult heroes | Let vanilla destroy — no one to carry on |
| Leader dead, heirs exist | Promote best heir, then rescue |
| Leader dead, no heirs | Let vanilla destroy |
| Clan has fiefs | Transfer to heir clan before rescue |
| No eligible kingdom after grace | Not applicable in v0.2.0.1 (no scripted placement pass) |
| Rescued clan's leader dies while independent | Vanilla succession triggers; if final leader dies, `DestroyClanAction` fires and our prefix re-evaluates |
| Clan already joined a kingdom | Stop tracking (another mod or player action placed them) |
| Failed rebellion destruction | Not patched — legitimate destruction proceeds |
| Rebel clan loses last settlement | Rescued, normalized (IsRebelClan→false, IsMinorFaction→true), qualifies for Frontier Revenue |
| Rebel clan leader dies | Safety net promotes heir if available, then rescues |
| Homeless rebel clan on session load | Startup scan normalizes before vanilla's DailyTickClan can kill heroes |

### Rebel Clan Rescue (v0.2.7.0)

Rebel clans — spawned when a town rebels — face destruction through different pathways than regular kingdom clans:

1. **When a rebel clan loses its last settlement** (reconquered by another faction), vanilla's `RebellionsCampaignBehavior` daily tick calls `DestroyClanAction.Apply` to destroy the homeless rebel.
2. **When a rebel clan's leader dies**, vanilla calls `DestroyClanAction.ApplyByClanLeaderDeath`.

Neither path goes through the kingdom-destruction pipeline (rebel clans have `Kingdom == null` and no kingdom ever "falls"). The v0.2.6.1 rescue system didn't cover this — all rebel clans were destroyed.

**Two-layer rescue architecture:**

- **Primary**: `OnSettlementOwnerChanged` event listener in `B1071_ClanSurvivalBehavior`. When a settlement changes hands and the previous owner's clan is a rebel-origin clan (`IsRebelClan == true` OR StringId contains `"rebel_clan"`) with zero remaining settlements, rescue fires proactively — before vanilla's daily tick can destroy the clan. No inline Campaign actions (TimeLord/BetterTime safe).
- **Safety net**: `HandleDestroyClan` prefix in `B1071_ClanSurvivalPatch`. When `DestroyClanAction.Apply` or `ApplyByClanLeaderDeath` fires for a rebel-origin clan with `Kingdom == null`, the prefix intercepts and rescues the clan if it has living adults.

**Normalization** (`NormalizeRebelClan`):
1. Sets `IsRebelClan = false` (public setter) — prevents vanilla from re-targeting the clan
2. Sets `IsMinorFaction = true` (private setter, via reflection) — enables Frontier Revenue eligibility
3. Removes from vanilla's `RebellionsCampaignBehavior._rebelClansAndDaysPassedAfterCreation` dictionary (via reflection) — prevents vanilla's daily tick from managing the clan as a rebel

All three reflection operations fail gracefully with logged warnings if the game API changes.

**Detection**: `IsRebelClanOrigin()` returns true for clans with `IsRebelClan == true` OR StringId containing `"rebel_clan"`. This catches both freshly spawned rebels and "normalized" former-rebels whose StringId retains the marker.

### Startup Scan (v0.2.7.0)

Rebel clans that lost their settlement in a **prior save session** (or before the mod was installed) are not caught by `OnSettlementOwnerChanged` — that event only fires during live gameplay. Vanilla's `RebellionsCampaignBehavior.DailyTickClan` Part B kills homeless rebel clan heroes on the very first daily tick after session load, before any rescue path can fire.

**`ScanAndRescueHomelessRebelClans()`** runs once at `OnSessionLaunched`:

1. Iterates all clans. For each: checks `IsRebelClanOrigin`, no settlements, not already tracked, not eliminated, not player, not bandit.
2. Filters to clans with living adult heroes (`IsAlive && !IsChild && (IsLord || IsMinorFactionHero)`).
3. Promotes heir if leader is dead (via `ChangeClanLeaderAction.ApplyWithoutSelectedNewLeader`). Skips if no valid heir.
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
| Enable clan survival | On | Master toggle for the entire rescue system |
| Grace period (days) | 30 | Reserved for planned auto-placement flow (not consumed by current rescue tick) |
| Culture match weight | 2.0 | Reserved for planned culture scoring in auto-placement flow (not consumed by current rescue tick) |

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
| `B1071_QuickSettingsFluentSettings.cs` | Quick Settings MCM tab builder — 21 ProxyRef-backed system toggles in 5 groups |

