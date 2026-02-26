# Campaign++ — Changelog

---

## [0.1.7.8] — 2026-02-26

### Fix — AI town enslavement race condition + prison capacity enforcement

**Critical bug: AI town enslavement was non-functional (race condition)**

When an AI lord entered a town, two `OnSettlementEntered` listeners fired in registration order: vanilla's `PartiesSellPrisonerCampaignBehavior` and our `B1071_SlaveEconomyBehavior`. Because vanilla behaviors register before mod behaviors (`MbEvent` fires FIFO), vanilla would collect and sell ALL prisoners before our handler could enslave them. Result: zero slaves were ever produced from AI lord town visits — the feature existed in code but never actually executed.

**Fix — Expanded Harmony Prefix (`B1071_CastlePrisonerDepositPatch`)**

The existing castle deposit Harmony Prefix (which runs BEFORE vanilla's handler) now handles both castles AND towns:

- **Castle branch (unchanged in behavior):** All non-hero regular prisoners are moved from the party's prison roster into the castle's prison roster. Depositor tracking recorded for consignment income. T1–T3 auto-enslaved on next daily tick; T4+ begin conversion tracking.
- **Town branch (new):** Non-hero prisoners at or below `CastlePrisonerAutoEnslaveTierMax` (default T3) are converted to `b1071_slave` trade goods and added directly to the town's market `ItemRoster`. T4+ prisoners are left in the party roster for vanilla to sell/ransom normally.

After the prefix runs, vanilla's handler only sees heroes (at castles) or heroes + T4+ regulars (at towns) — it processes those normally.

**Prison capacity enforcement (new)**

AI castle deposits now enforce the vanilla `PrisonerSizeLimit`. When a castle's prison is full, excess prisoners are not deposited — they remain with the lord's party and fall through to vanilla sell behavior at the next town visited. Partial deposits use proportional wounded calculations to avoid stranding more wounded than total count.

**Dead code removal (`B1071_SlaveEconomyBehavior`)**

The `OnSettlementEntered` handler in `SlaveEconomyBehavior` previously attempted to enslave prisoners but was always pre-empted by vanilla (the race condition above). That dead prisoner-enslavement code has been removed. The handler now only transfers slave trade goods already in the AI lord's inventory into the town market — which works correctly because item rosters are not affected by vanilla's prisoner sell handler.

**Save/load safety:** No new `SyncData` keys. No serialized state changes. Existing campaigns will immediately benefit from working AI town enslavement on next load. Fully compatible with saves from v0.1.7.7 or earlier.

**Mod removal safety:** No campaign data changes. If the mod is removed, vanilla resumes normal prisoner sell behavior at all settlements.

---

## [0.1.7.7] — 2026-02-26

### Balance — T3 enslavement cap + slave price 200d → 300d

**Enslavement tier cap (player parity fix)**

The player's "Enslave prisoners" town menu now respects the same `CastlePrisonerAutoEnslaveTierMax` MCM setting (default 3) that already controlled AI town enslavement and castle auto-enslavement. Previously, the player could enslave ALL non-hero prisoners regardless of tier — a parity gap that made the prisoner path disproportionately powerful for the player compared to AI behavior.

**What changed:**
- `ConvertPrisonersToSlaves`: tier filter added — only prisoners at `Tier <= CastlePrisonerAutoEnslaveTierMax` (default T1–T3) are converted. T4+ prisoners stay in the player's prison roster.
- `SlaveTradeEnterCondition`: now counts only enslaveable (T1–T3) prisoners. When the player has only T4+ prisoners, the menu option is visible but **disabled** with tooltip: *"Only Tier 1–3 prisoners can be enslaved. Take T4+ to a castle for recruitment conversion or ransom at the tavern."*
- `EnslaveCondition`: text updated to show T1–T3 eligible count and how many T4+ are kept.
- `EnslaveConsequence`: notification message reports how many T4+ prisoners were kept and what to do with them.
- `RefreshSlaveMenuBody`: submenu body now shows enslaveable vs. non-enslaveable prisoner counts separately with guidance for T4+ disposal.
- New helpers: `CountEnslavablePrisoners()` (T1–T3 only) and `CountAllNonHeroPrisoners()` (all tiers) replace the former `CountNonHeroPrisoners()`.

**MCM setting update:**
- `CastlePrisonerAutoEnslaveTierMax` HintText updated to reflect its role as a **unified** enslavement tier cap across all three paths: (1) player town menu, (2) AI town auto-enslave, (3) castle auto-enslave. Variable name and default unchanged — fully save compatible.

**Slave base price: 200d → 300d** (`items.xml`)

Historically, 300 denars places slaves between spice and fur — reasonable for unskilled field labour in the 1071 Near East context. This is the minimal change that moves toward historical accuracy without disrupting the existing economy.

**Economic impact (with T3 cap):**
| Scenario | Before (200d, all tiers) | After (300d, T1–T3 only) |
|----------|--------------------------|--------------------------|
| Village raid (600h, 18 events) | 2 slaves/event × 18 = 36 × ~100d sell = 3,600d | 2 slaves/event × 18 = 36 × ~150d sell = 5,400d |
| Enslave 20 T1–T3 prisoners | 20 × ~100d sell = 2,000d | 20 × ~150d sell = 3,000d |
| Enslave 20 mixed (10 T1–T3 + 10 T4–T5) | 20 × ~100d sell = 2,000d | 10 × ~150d sell = 1,500d (T4+ kept) |
| Ransom 20 T4–T5 at tavern | N/A (would have been enslaved) | ~2,000–3,000d total ransom |

**Key behavioral changes:**
- Post-battle with mixed prisoners: player now naturally routes T4+ to castles (for elite recruitment) or tavern (for ransom), while T1–T3 go to enslavement. This creates a meaningful strategic decision instead of "enslave everything."
- AI already had this behavior — now player matches exactly.
- Raid income slightly increases per-slave due to higher price, but total income per raid is unchanged (slaves from raids are hearth-based, not prisoner-based).
- No new serialized data. Price change in XML re-reads every session. T3 cap is a runtime logic filter.

**Save/load safety:** No new `SyncData` keys. `items.xml` `value` is re-read on session launch. Existing slave items in inventories/markets retain their identity; the engine applies the new base price on next market interaction. Compatible with existing campaigns including mid-game mod addition.

**Mod removal safety:** If the mod is removed, slave items become orphaned trade goods (vanilla handles unknown items gracefully). No campaign corruption regardless of when the mod is added or removed.

---

## [0.1.7.6] — 2026-02-26

### Rebrand — Byzantium 1071 → Campaign++

Mod renamed from "Byzantium 1071" to "Campaign++" in the launcher (`SubModule.xml` `<Name>`) and MCM settings (`DisplayName`). The name "Byzantium 1071" was causing confusion — players expected a Byzantine total conversion or troop overhaul, not a faction-agnostic campaign systems mod.

- `SubModule.xml` `<Name>`: `Byzantium 1071` → `Campaign++`
- `B1071_McmSettings.DisplayName`: `Byzantium 1071` → `Campaign++`
- Nexus mod page title, description, and all in-game references updated
- Module `<Id>` (`Byzantium1071`), folder name, assembly name, and `.csproj` unchanged — **fully save compatible**

---

## [0.1.7.5] — 2026-02-25

### Balance — Diplomacy default tuning (wars ending too soon)

Wars were ending prematurely due to three independent peace-pressure systems stacking additively: exhaustion-driven diplomacy bias, manpower-depletion diplomacy pressure, and multi-war pressure. Analysis showed a kingdom at moderate exhaustion (~50) with depleted manpower could accumulate ~490+ peace support bias per clan vote — overwhelming any vanilla war motivation.

**MCM default changes:**
- `DiplomacyEnforcePlayerParity`: `false` → `true` — player kingdom now follows the same truce/no-war diplomacy gates as AI by default.
- `DiplomacyForcedPeaceThreshold`: `75` → `85` — forced peace requires higher exhaustion before activating.
- `DiplomacyForcedPeaceCooldownDays`: `3` → `10` — forced peace cascade slowed significantly.
- `MinWarDurationDaysBeforeForcedPeace`: `20` → `40` — wars are protected from forced peace for twice as long.

All values remain configurable via MCM.

---

## [0.1.7.4] — 2026-02-26

### Audit Fixes — Comprehensive 26-finding code audit

### New — Castle open access patch (`B1071_CastleAccessPatch`)

**Problem:** Vanilla `DefaultSettlementAccessModel` blocks early-game players from interacting with the castle recruitment system. Two restrictions compound:

1. **Castle settlement entry** (`CanMainHeroEnterCastle`): Neutral factions with owner relation < 0 → `NoAccess` — player can't even enter the castle to auto-deposit prisoners.
2. **Lord's hall / keep** (`CanMainHeroEnterKeepInternal`): Neutral factions with clan tier < 3 → `LimitedAccess` (bribe ~800 denars) — player can't access the recruitment UI overlay.

**Fix:** Two Harmony Postfixes that relax castle-specific restrictions for non-hostile factions:

- `B1071_CastleAccessPatch_Settlement` — Postfix on `CanMainHeroEnterSettlement`. If the castle blocked entry due to `RelationshipWithOwner` (neutral, owner dislikes you), upgrades to `FullAccess`. Crime-based and hostile blocks are preserved.
- `B1071_CastleAccessPatch_LordsHall` — Postfix on `CanMainHeroEnterLordsHall`. If the lord's hall required a bribe due to `ClanTier` (neutral, tier < 3), upgrades to `FullAccess`, waiving the ~800g bribe. Crime-based bribe and hostile disguise blocks are preserved.

**Scope:** Castles only — town access model is untouched. Hostile castles (at war) remain fully restricted. MCM toggle: `CastleOpenAccess` (default: true).

**Critical (2)**
- **B-1**: Wartime gold exploit — depositor at war with castle owner now forfeits their share to the castle owner. Applied to `DistributeIncome`, `HandleRecruitmentGold`, `GetPlayerDepositorShare`, `GetPlayerRecruitmentShare`, `GetEffectiveGoldCost`, and `GetGarrisonAbsorptionCost`.
- **D-4**: `SlaveEconomyBehavior.Instance` now properly nulled in `OnSubModuleUnloaded` (was missing from cleanup).

**High (5)**
- **E-1**: Forced peace now applies to the player's kingdom when `DiplomacyEnforcePlayerParity` is enabled.
- **A-1**: AI recruit dedup now tracks `_lastAIRecruitPartyId` to prevent false positive dedup when multiple AI parties recruit the same troop+amount from the same settlement.
- **B-4/E-3**: Town AI slave auto-enslavement now uses the `CastlePrisonerAutoEnslaveTierMax` MCM setting instead of hardcoded `Tier <= 2`.
- **D-3**: `_dynamicFoodPatchApplied` static flag now properly reset in `OnSubModuleUnloaded` via `ResetDynamicPatchFlag()`.
- **A-3**: Soft-cap defaults to `current = 0` (instead of `current = max`) when `Instance` is null, preventing regen from being crushed at startup.

**Medium (8)**
- **G-1**: New combined prosperity penalty cap (`MaxCombinedModProsperityPenalty`, default -8.0/day) prevents runaway settlement death spirals. Implemented via `B1071_ProsperityPenaltyCapPatch` shared tracker with HarmonyPrefix reset.
- **B-3**: Same-clan elite recruitment now costs 50% of normal price (was free). UI updated to show "Elite (50%)" with correct cost display.
- **G-4**: New `SlaveManpowerCapPerTown` MCM setting (default 20/day) caps daily slave manpower injection per town.
- **G-6**: `FindAliveHero` now uses a `Dictionary<string, Hero>` cache rebuilt once per game day instead of O(n) linear scan over `Hero.AllAliveHeroes`.
- **G-7**: `IsEnemyBesiegingCoreSettlement` now iterates `kingdom.Settlements` instead of `Settlement.All` for better performance.
- **A-5/A-6/A-7**: Dead MCM settings (`TiersPerExtraCost`, `CostMultiplierPercent`, `SlaveConstructionBonusDays`) moved to "Legacy" group with `[LEGACY — NOT USED]` labels (kept for save compatibility).
- **F-1**: `PayHero` now logs a warning when `payingTown` is null (gold created from thin air fallback).
- **A-2**: Daily regen skipped entirely when pool is already at max capacity.

**Low (4)**
- **A-4**: Raid dedup maps no longer cleared daily in `OnDailyTick` — uses periodic prune of entries older than 2 days instead, preventing mid-day dedup gaps.
- **C-1**: `_seeded` flag now persisted in `SyncData` to prevent re-seeding manpower pools on save/load.
- **C-2**: Culture troop cache tree traversal replaced with safe BFS using visited-set guard to prevent infinite loops from circular `UpgradeTargets` in modded troop trees.

---

## [0.1.7.3] — 2026-02-25

### UX — Batched player consignment notifications

Replaced per-prisoner notification spam with one aggregate summary per castle per daily tick for each income path.

**Enslavement path** — `AutoEnslaveLowTierPrisoners` accumulates player depositor income across all enslaved prisoners, emits a single message: "⚔️ Consignment from {castle}: +{gold}g ({count} prisoners enslaved at {town}, your {share}% depositor share)."

**AI recruitment path** — `AiAutoRecruit` accumulates player depositor income across all AI lord recruitments, emits a single message: "⚔️ Consignment from {castle}: +{gold}g ({count} of your prisoners recruited by AI lords, your {share}% depositor share)."

**Garrison absorption path** — `GarrisonAbsorbPrisoners` accumulates player depositor income, emits a single message: "⚔️ Consignment from {castle}: +{gold}g ({count} prisoners absorbed into garrison, your {share}% depositor share)."

**New helpers:**
- `GetPlayerDepositorShare(castle, heroId, income)` — mirrors `DistributeIncome` logic but only returns the player's share without transferring gold. Used for enslavement pre-calculation.
- `GetPlayerRecruitmentShare(castle, recruiter, heroId, costPerTroop, count)` — mirrors `HandleRecruitmentGold` logic (including family waiver check) but only returns the player's share. Used for AI recruitment pre-calculation.

### Balance — Critical slave economy demand calibration fix

**Root cause:** XML-loaded `ItemCategory` demand values are set as raw floats directly on the `BaseDemand`/`LuxuryDemand` properties. Code-registered vanilla categories pass integers to `InitializeObject()` which internally multiplies by `0.001f`. Our XML values were **not** scaled — `base_demand="0.8"` was interpreted as `BaseDemand=0.8f`, while vanilla Wine uses `BaseDemand=0.015f` (integer 15 × 0.001). Our slave category had **53× higher** demand than vanilla luxury goods.

**Symptoms:**
- Slave prices spiked to ~350g in zero-supply towns (vs ~200g base value) due to extreme demand signal
- Selling the first slave crashed the price by ~100g (350→250) — 29% drop from a single unit
- Rapid convergence to floor (~154g) after just 8-10 units sold
- Town stock depleted far too fast (consumption rate 53× normal), wiping out slave-based bonuses (construction, prosperity, manpower)

**Fix:** Aligned with decompiled vanilla `DefaultItemCategories.InitializeAll()` values:

| Category | BaseDemand | LuxuryDemand |
|----------|-----------|-------------|
| Wine (vanilla) | 0.015 | 0.030 |
| Velvet (vanilla) | 0.015 | 0.032 |
| Jewelry (vanilla) | 0.015 | 0.032 |
| Fur (vanilla) | 0.010 | 0.038 |
| **Slaves (old)** | **0.800** | **1.000** |
| **Slaves (new)** | **0.015** | **0.032** |

**Expected behavior after fix:**
- Slave prices in zero-supply towns: moderate premium over base value (~220-250g), not extreme spike
- Price drops gradually per unit sold, similar to selling wine or jewelry
- Town slave stocks persist longer (consumption = vanilla luxury rate), sustaining construction/prosperity/manpower bonuses
- AI caravans still actively route for slaves (luxury_demand 0.032 = jewelry tier)

---

## [0.1.7.2] — 2026-02-25

### Comprehensive Audit — 89-Scenario Financial & Safety Verification

Full audit of the castle recruitment system covering 89 financial scenarios, save/load safety, mod removal safety, exploit analysis, and AI/player parity.

**Bug Fix — UI clan-waiver display (B1071_CastleRecruitTroopVM)**
- The recruit button was checking `Hero.MainHero.Gold >= goldCost` (raw cost) without accounting for clan waivers.
- At your own castle: elite troops and same-clan depositor prisoners showed "Not enough gold" even though they're free.
- Fix: Elite constructor now checks `Clan.PlayerClan == castle.OwnerClan` → effective cost = 0 (free).
- Fix: Prisoner constructor now calls `behavior.GetPlayerEffectivePrisonerCost()` for the effective cost after waivers.
- Added `GetPlayerEffectivePrisonerCost()` public helper to `B1071_CastleRecruitmentBehavior`.
- Status text now shows "Elite (Free)" / "Ready (Free)" when clan waivers eliminate cost.
- Hint text updated: "Recruit one {name} (same clan — free)" or "(clan waivers — free)".

**Bug Fix — FIFO depositor tracking (RecordDeposit)**
- `RecordDeposit` previously consolidated same-hero entries: if Hero B deposited 3, Hero C deposited 2, then Hero B deposited 4 more, the list became [(B,7), (C,2)] instead of strict FIFO [(B,3), (C,2), (B,4)].
- This broke strict FIFO ordering when interleaved deposits occurred, potentially over-crediting earlier depositors if processing stopped midway (e.g., town out of gold).
- Fix: Always append new entries, never consolidate. Strict FIFO ordering preserved.

**Removed — CastleEliteAiMaxPerDay MCM setting**
- The `CastleEliteAiMaxPerDay` setting (default: 3) was never used in code. AI was already uncapped.
- Setting removed from MCM entirely. No save compatibility impact (MCM handles missing keys gracefully).

### Audit Results Summary (89 scenarios)

- **Enslavement path (A):** 10 scenarios — all pass ✅
- **Player recruit prisoner (B):** 10 scenarios — all pass ✅
- **AI recruit prisoner (C):** 10 scenarios — all pass ✅
- **AI recruit elite (D):** 10 scenarios — all pass ✅
- **Player recruit elite (E):** 6 scenarios — all pass ✅
- **Garrison absorb (F):** 10 scenarios — all pass ✅
- **Cross-cutting:** 16 scenarios — all pass ✅
- **Exploits:** 7 scenarios — all mitigated ✅
- **Save/load safety:** Verified all 13 save keys, null-safe loading, old save compatibility ✅
- **Mod removal safety:** Verified clean Harmony unpatch, orphaned data inert, no state corruption ✅
- **AI/Player parity:** Full parity across all 6 gold flow paths ✅

---

## [0.1.7.1] — 2026-02-25

### Neutral Castle Access (Full-Audit Fix)

**All castle recruitment features now work at neutral castles (for both player and AI).**

Previously, several faction checks restricted castle deposit and AI recruitment to same-faction castles only. This was overly restrictive — neutral (non-hostile, non-allied) castles were blocked. The user's design intent is that only hostile castles should be blocked.

**AI deposit patch — neutral castles allowed** (fixed)
- `B1071_CastlePrisonerDepositPatch`: Changed faction check from `mobileParty.MapFaction != settlement.MapFaction` (same-faction only) to `FactionManager.IsAtWarAgainstFaction(...)` (blocks hostile only). AI lords can now deposit prisoners at any non-hostile castle they enter, including neutral ones.

**AI auto-recruitment — neutral castles allowed** (fixed)
- `AiAutoRecruit`: Changed faction check from `party.MapFaction != faction` (same-faction only) to `FactionManager.IsAtWarAgainstFaction(...)` (blocks hostile only). AI lord parties at neutral castles can now recruit from the elite pool and converted prisoners. Cross-clan gold rules still apply (neutral lords pay full price).

**Player deposit at neutral castles — new menu option** (new)
- Vanilla's "Donate prisoners" dungeon menu option requires same-faction (`MapFaction == MainHero.MapFaction`). This blocks player deposits at neutral castles.
- Added a new dungeon menu option "⚔️ Deposit prisoners" (`b1071_castle_deposit_prisoners`) that appears only at neutral castles where vanilla's option doesn't.
- Uses the same vanilla `PartyScreenHelper.OpenScreenAsDonatePrisoners()` API — identical party screen, identical done handler.
- The existing `OnPrisonerDonatedToSettlement` event hook fires automatically, recording depositor tracking for the consignment model.
- Shows prisoner count and holding fee percentage in the label: "⚔️ Deposit prisoners (12 available, 30% holding fee)".
- Disabled with tooltip when player has no prisoners or prison is full.

**Access rule summary (after this fix):**
| Actor  | Recruitment           | Deposit                    |
|--------|-----------------------|----------------------------|
| Player | Own + Allied + Neutral ✅ | Own (Manage) + Same-faction (vanilla Donate) + Neutral (our option) ✅ |
| AI     | Same-faction + Neutral ✅ | Same-faction + Neutral ✅ |
| Both   | Hostile ❌              | Hostile ❌                  |

**Minor fixes from audit:**
- Added null check for `donatedPrisoners` parameter in `OnPrisonerDonatedToSettlement` handler (defensive safety).
- Updated all doc comments to reflect neutral castle access rules.

### Gold Transaction Bug Fixes (4 bugs found via financial audit)

**Enslavement Economy Fix: Town Pays for Slaves** (fixed)
- **Problem:** When castles auto-enslaved T1-T3 prisoners, slave items were added to the nearest town's market **free of charge**, and income sent to depositor/owner was **created from nothing** via `GiveGoldAction(null, recipient, amount)`. This caused double-income: the depositor/owner received gold (created), AND the town got free inventory to sell to caravans. Net result: gold inflation.
- **Root cause:** `AutoEnslaveLowTierPrisoners` treated the enslavement as a gift → reward, not as a sale. No entity was paying for the slaves.
- **Fix:** The nearest town now **buys** the slaves from the castle. Income is paid via `GiveGoldAction.ApplyForSettlementToCharacter(nearestTown, recipient, amount)`, which deducts from `Town.Gold` (the town's trade treasury), properly clamped by vanilla's `SettlementComponent.ChangeGold`. The slave items are still added to the town's `ItemRoster` (the town receives the labor).
- **Affordability gate:** Prisoners are processed one unit at a time. If the town cannot afford the next slave at the current market price, enslavement **stops** — remaining prisoners stay in the castle dungeon until next day (when the town may have earned more gold or the price dropped).
- **Vanilla API used:** `GiveGoldAction.ApplyForSettlementToCharacter(Settlement, Hero, int, bool)` → internally calls `ApplyInternal` with `giverParty = settlement.Party` → `SettlementComponent.ChangeGold(-clampedAmount)`. Properly fires campaign events.
- **Economics:** Castle enslavement is now a proper market transaction. Towns with low gold buy fewer slaves. Towns with high gold buy more. Self-correcting — no infinite gold injection.

**BUG 1 (Medium): Garrison absorption stiffing depositors when owner is broke** (fixed)
- **Problem:** When the castle garrison absorbed a cross-clan deposited prisoner and the castle owner couldn't afford the depositor's share, the prisoner was consumed, the depositor tracking was consumed, but the depositor received **nothing**. The owner got a free garrison troop at the depositor's expense.
- **Fix:** Garrison now processes prisoners one at a time. Before absorbing each prisoner, we pre-check (`GetGarrisonAbsorptionCost`) whether the owner can afford the depositor's share. If they can't, that prisoner is **skipped** — it stays in the prison roster. The garrison can still absorb untracked or same-clan prisoners (which are free).
- **New helper:** `GetGarrisonAbsorptionCost(castle, depositorHeroId, goldCostPerTroop)` — returns 0 for untracked, dead, or same-clan depositors; otherwise returns `goldCostPerTroop * (1 - feePercent)`.

**BUG 2 (Low): PeekDepositor mismatch causing unexpected charges** (fixed)
- **Problem:** AI prisoner recruitment used `PeekDepositor` (checks first depositor only) for a bulk affordability check, then consumed N prisoners via `ConsumeDepositorEntries` which iterates FIFO across potentially different depositors. If depositors had different clan relationships, the recruiter could be charged more than the affordability check predicted.
- **Example:** 5 T5 prisoners — first 3 deposited by same-clan (free to recruit), last 2 by cross-clan (300g each). Peek says "free" → recruiter takes all 5 → gets charged 600g unexpectedly.
- **Fix:** AI prisoner recruitment now processes **one unit at a time** in a `for` loop. Each iteration peeks the current first depositor, checks affordability for that specific depositor, and only proceeds if the recruiter can afford it.

**BUG 3 (Medium — was classified Low, upgraded after decompilation): Gold inflation in HandleRecruitmentGold** (fixed)
- **Problem:** `HandleRecruitmentGold` used `ChangeHeroGold(-X)` to deduct from recruiter (gold destroyed, clamped at 0 by `set_Gold` → `Math.Max(0, value)`), then `GiveGoldAction(null, recipient, share)` to create gold from nothing for depositor and owner. If recruiter had less than X gold, they lost only what they had (e.g., 100g), but depositor+owner received the full amount (e.g., 300g total). **Net: 200g created from nothing. Gold inflation.**
- **Root cause:** Two different mechanisms — `ChangeHeroGold` (raw field modification, no campaign events) vs. `GiveGoldAction` (proper API with events). `GiveGoldAction.ApplyInternal` clamps transfers to `MathF.Min(giverHero.Gold, goldAmount)`, but `ChangeHeroGold` does not.
- **Fix:** Replaced with two direct `GiveGoldAction.ApplyBetweenCharacters` calls: `(recruiter, depositor, depositorShare)` + `(recruiter, owner, ownerShare)`. Benefits: clamped to available gold (no inflation), proper campaign events fired, no gold creation from nothing, waived shares correctly skip the transfer.

**BUG 4 (Cosmetic): Null owner fallback silently destroys gold** (fixed)
- **Problem:** In `TryRecruitElite` and `AiAutoRecruit` elite section, when `owner == null`, the fallback was `recruiterHero.ChangeHeroGold(-totalCost)` — gold deducted from recruiter but paid to nobody. Gold simply vanished.
- **Fix:** Removed the `else ChangeHeroGold(-cost)` fallback from both locations. If owner is null (shouldn't happen — castles always have owners), the transaction is silently skipped instead of destroying gold.

### Vanilla API findings (from decompilation)

Documented for future reference — these apply to Bannerlord v1.3.15:

| API | Behavior |
|-----|----------|
| `GiveGoldAction.ApplyBetweenCharacters(giver, recipient, amount)` | `ApplyInternal` clamps amount to `MathF.Min(giver.Gold, amount)` before deducting. Recipient gets only what was actually deducted. Safe. Fires `OnHeroOrPartyTradedGold`. |
| `GiveGoldAction.ApplyBetweenCharacters(null, recipient, amount)` | Giver block skipped entirely (no deduction). Recipient gets full amount. **Gold created from nothing.** |
| `GiveGoldAction.ApplyForSettlementToCharacter(settlement, hero, amount)` | Calls `ApplyInternal` with `giverParty = settlement.Party`. Deducts from `SettlementComponent.Gold` via `ChangeGold(-clamped)`. Recipient hero gets clamped amount. Fires campaign events. **Proper town→hero transfer.** |
| `Town.ChangeGold(int changeAmount)` | `Gold = Gold + changeAmount`. If result < 0, clamps to 0 (same pattern as `Hero.set_Gold`). |
| `Hero.ChangeHeroGold(amount)` | Adds `amount` to `_gold`. `set_Gold` calls `Math.Max(0, value)` — gold clamped at 0, never negative. But if deducting more than available, excess is silently discarded. **No campaign events fired.** |

### Compatibility
- **Save-safe:** No new serialized data. Existing saves load normally.
- **Backward compatible:** Old saves without depositor data treat all prisoners as untracked (castle owner gets 100%).
- **Mod removal safe:** All data uses flat `SyncData` lists. Removing the mod loses tracking data but doesn't corrupt saves.

---

## [0.1.7.0] — 2026-02-25

### New Systems

**Castle Recruitment System — Three-source troop recruitment at castles** (new)

Castles now offer a dedicated recruitment screen with three independent troop sources, accessible via the "🏰 Recruit troops" option in the castle game menu. This system runs in parallel with the normal village-notable volunteer system.

**Source 1 — Elite Troop Pool (culture-based)**
- Each castle generates T4/T5/T6 troops matching the settlement's culture, drawn from both the basic (infantry/common) and noble (elite/cavalry) troop trees.
- The pool regenerates daily from the castle's manpower pool. Regen rate scales with prosperity: `regenMin` at 0 prosperity, `regenMax` at `ProsperityNormalizer` prosperity (defaults: 1–3 troops/day).
- Each regenerated troop costs `CastleEliteManpowerCost` (default: 10) manpower — elite troops are expensive to raise.
- The pool is capped per castle at `CastleElitePoolMax` (default: 10). Multiple troop types share this cap.
- Troops are distributed randomly among the culture's eligible T4–T6 troop types each day.

**Source 2 — Converted Prisoners (ready for recruitment)**
- T4+ prisoners held at a castle become recruitable after serving a tier-based waiting period:
  | Tier | Required Days | Gold Cost |
  |------|--------------|-----------|
  | T4   | 5 days       | 150g      |
  | T5   | 7 days       | 300g      |
  | T6   | 10 days      | 500g      |
- Day tracking is per troop *type* per castle — when the threshold is met, all prisoners of that type become ready simultaneously.
- Prisoner recruitment costs **zero manpower**. These are already captured troops, not drawn from the settlement's population.

**Source 3 — Pending Prisoners (not yet ready)**
- T4+ prisoners still serving their waiting period. Visible in the recruitment screen with a "X/Y days" progress indicator. Not yet recruitable.

**Low-tier prisoner auto-enslavement**
- T1–T3 prisoners at castles are automatically enslaved daily to the nearest town's slave market (requires Slave Economy enabled). This keeps castle prisons focused on high-value T4+ prisoners and feeds into the town-level slave economy.
- The castle owner receives the dynamic slave market price per enslaved prisoner. Income is generated from the nearest town’s market price for the `b1071_slave` item (e.g., if slaves trade at 154 denars, 20 enslaved prisoners = 3,080 denars).

**Access rules**
- Player can recruit from any castle NOT hostile to them (own, allied, or neutral faction).
- Player can deposit prisoners at neutral castles via a dedicated dungeon menu option ("⚔️ Deposit prisoners"). Vanilla's "Donate prisoners" covers same-faction; our option extends to neutral castles.
- AI lord parties recruit from any non-hostile castle (own faction or neutral). Hostile castles are skipped.
- AI lord parties deposit prisoners at any non-hostile castle they enter (own faction or neutral).
- Hostile castles are hidden from the game menu entirely (no greyed-out option, just not shown).
- The menu shows availability at a glance: "🏰 Recruit troops (5 available, 3 pending)".

**Player recruitment UI**
- Full Gauntlet screen with three scrollable lists: Elite Pool (culture troops), Ready Prisoners (converted), and Pending Prisoners (still waiting).
- Each troop entry shows: troop name, tier indicator, count available, gold cost, and a recruit button.
- Stats bar at top shows current gold (left) and castle manpower (right).
- Lists auto-refresh after each recruitment. Empty lists show a placeholder message.

**Vanilla prisoner handling — patched at castles (two patches)**
- **Daily tick blocked** (`B1071_CastlePrisonerRetentionPatch`): The vanilla `PartiesSellPrisonerCampaignBehavior.DailyTickSettlement` sells ~10% of settlement prisoners daily at AI castles. This is blocked when castle recruitment is enabled. Without this, T4+ prisoners would vanish before finishing their waiting period.
- **Settlement entry redirected** (`B1071_CastlePrisonerDepositPatch`): The vanilla `OnSettlementEntered` handler sells ALL non-hero regular prisoners when a lord enters any fortification — removing them from the party's prison roster, paying gold, and **never adding them to the settlement's prison roster** (they simply vanish). At castles, our prefix intercepts this: all regular prisoners are moved from the party's roster directly into the castle's prison roster (free deposit, no gold). Vanilla then runs, finds no regulars left, and handles hero prisoners normally. T1–T3 prisoners deposited will be auto-enslaved on the next daily tick; T4+ begin conversion tracking immediately.

**Persistence**
- `_prisonerDaysHeld`: per-castle per-troop day counters (prisoner conversion tracking). Survives save/load via `SyncData`.
- `_elitePool`: per-castle per-troop stock counts (culture elite pool). Survives save/load via `SyncData`.
- Both dictionaries use a flattened 3-parallel-list serialization scheme. Stale entries are cleaned up when prisoners are fully recruited or removed from the prison roster.

**Daily tick order (5 steps, sequential)**
1. **AutoEnslave** — T1–T3 prisoners → nearest town slave market
2. **TrackDays** — Increment day counters for all T4+ prisoners
3. **RegenerateElitePool** — Add culture troops from manpower (prosperity-scaled, capped)
4. **AiAutoRecruit** — AI lords take from elite pool + ready prisoners (gold cost, no daily cap)
5. **GarrisonAbsorbPrisoners** — Garrison absorbs ready prisoners (1/day, zero manpower)

**MCM Settings — Castle Recruitment group**
| Setting | Default | Description |
|---------|---------|-------------|
| `EnableCastleRecruitment` | true | Master toggle for the entire castle recruitment system |
| `CastlePrisonerAutoEnslaveTierMax` | 3 | Maximum tier for auto-enslavement (T1–3 by default) |
| `CastleRecruitT4Days` / `T5Days` / `T6Days` | 5 / 7 / 10 | Days before prisoners of each tier become recruitable |
| `CastleRecruitGoldT4` / `T5` / `T6` | 150 / 300 / 500 | Gold cost per recruit by tier |
| `CastleElitePoolMax` | 10 | Maximum elite troops per castle |
| `CastleEliteRegenMin` / `RegenMax` | 1 / 3 | Daily regen range (scales with prosperity) |
| `CastleEliteManpowerCost` | 10 | Manpower cost per elite troop regenerated |
| `CastleRecruitDrainsManpower` | true | Whether elite recruitment drains manpower |
| `CastleEliteAiRecruits` | true | Whether AI lords recruit from castles |

### Castle Recruitment — AI & Garrison Overhaul

**AI lord parties now recruit converted prisoners at castles** (new)
- AI lords visiting their own faction's castles now recruit from **both** the elite troop pool **and** converted (ready) prisoners.
- **Same-clan lords recruit for free**; cross-clan lords (different clan, same faction) pay gold per troop, which is credited to the castle owner.
- Previously, AI could only recruit from the elite pool. Converted prisoners (T4+) sat idle indefinitely because no code path existed for AI or garrison to claim them.
- Gold transfer: cross-clan recruitment uses `GiveGoldAction.ApplyBetweenCharacters(party.LeaderHero, settlement.Owner, cost)`. Same-clan lords skip the gold check entirely (unlimited budget from own castle).
- Prisoner recruitment costs **zero manpower** (by design — prisoners are already captured, not drawn from the population). Elite recruitment still drains manpower when `CastleRecruitDrainsManpower` is enabled.

**AI recruitment daily cap removed** (changed)
- The old `CastleEliteAiMaxPerDay` cap (default 3) has been removed. AI lords now recruit up to their party size limit from both sources in a single visit.
- Both pools (elite + prisoners) are available simultaneously with no priority — lords take what they can afford from both.
- MCM setting `CastleEliteAiMaxPerDay` kept for save compatibility but marked as legacy/unused.

**Garrison absorbs ready prisoners at auto-recruit rate** (new)
- Castles with garrison auto-recruitment enabled (vanilla setting + positive food) now transfer converted prisoners into the garrison at the same daily rate as vanilla auto-recruitment (`GetMaximumDailyAutoRecruitmentCount`, typically 1/day + building bonuses).
- Our existing manpower postfix (`B1071_GarrisonAutoRecruitManpowerPatch`) still caps the total daily garrison growth.
- Garrison absorption costs zero manpower (prisoner-sourced, not population-sourced).
- Respects garrison size limit and food requirements.
- Step 5 in the castle daily tick, after AI recruitment.

### Castle Economy — Enslavement & Recruitment Income

**Castle Economy — Consignment Model (depositor tracking & income splitting)** (new)
- Lords who deposit prisoners at another clan's castle now receive their fair share of the income when those prisoners are processed (enslaved or recruited). This is the **consignment model** — depositors "consign" prisoners; the castle takes a commission.
- **New data structure:** `_depositorTracking` — per-castle, per-troop-type, per-depositor FIFO list tracking who deposited which prisoners and how many. Survives save/load via 4-parallel-list serialization in `SyncData`.

**Enslavement income split (T1–T3)**
- When low-tier prisoners are auto-enslaved, the slave market income is now split between the depositor and castle owner based on the **Castle Holding Fee %** (MCM, default 30%). 
- Cross-clan depositor: depositor gets 70%, castle owner gets 30%.
- Same-clan depositor (depositing at own clan's castle): castle owner gets 100% (family).
- Untracked prisoners (siege conquest, pre-tracking saves): castle owner gets 100%.
- If depositor hero is dead/unavailable: castle owner gets 100%.

**Recruitment income split (T4+ converted prisoners)**
- 3-party **independent clan-waiver** model: recruiter, depositor, and castle owner each have separate clan relationships that independently determine which shares are paid.
- Recruiter same-clan as depositor → depositor's share (70%) waived.
- Recruiter same-clan as castle owner → owner's share (30%) waived.
- Recruiter same-clan as both → fully free.
- No depositor (untracked) → current behavior: same-clan free, cross-clan pays owner 100%.

| Recruiter vs Depositor | Recruiter vs Owner | Recruiter Pays | Depositor Gets | Owner Gets |
|---|---|---|---|---|
| Cross-clan | Cross-clan | Full cost | 70% | 30% |
| Same-clan | Cross-clan | 30% only | — | 30% |
| Cross-clan | Same-clan | 70% only | 70% | — |
| Same-clan | Same-clan | Free | — | — |
| No depositor | Cross-clan | Full cost | — | 100% |
| No depositor | Same-clan | Free | — | — |

**Garrison absorption now compensates depositors** (changed)
- When the garrison absorbs a cross-clan depositor's prisoner, the castle owner pays the depositor their share (70%) from the owner's own gold.
- Affordability check: absorption only pays if the castle owner has enough gold.
- Same-clan depositor's prisoners: free (family).
- Untracked prisoners: free (castle owner's own).

**Elite pool recruitment unchanged**
- Elite pool troops are castle-generated, not deposited. No depositor involvement.
- Current rules preserved: same-clan free, cross-clan pays castle owner.

**MCM Settings — new**
| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| `CastleHoldingFeePercent` | 30 | 5–50 | Castle owner's commission on prisoner processing |

**Deposit patch — faction check fixed** (fixed)
- The deposit patch previously checked `!IsAtWarWith(settlement.MapFaction)`, which was broader than vanilla (allowed neutral/unaffiliated parties to deposit). Now uses `mobileParty.MapFaction != settlement.MapFaction` — only same-faction parties deposit, matching vanilla's `OnSettlementEntered` condition.
- Deposit patch now calls `RecordDeposit` after moving prisoners, recording the depositing lord's hero ID for each troop type and count.

**Player prisoner deposits now tracked (consignment model parity)** (new)
- The behavior now listens to `CampaignEvents.OnPrisonerDonatedToSettlementEvent` — the campaign event vanilla fires when the player donates prisoners via the dungeon menu's "Donate prisoners" party screen.
- When the player deposits prisoners at another clan's castle, `RecordDeposit` is called for each non-hero troop type, establishing the player as the depositor in the consignment model.
- The player now receives their share (default 70%) of all enslavement income and recruitment fees from their deposited prisoners, just like AI lords.
- Own-clan castles are skipped (no economic difference — same-clan deposits → owner gets 100%).
- An information message is shown: "⚔️ Deposited 15 prisoners at [castle]. You receive 70% of processing income (holding fee: 30%)."
- Uses vanilla's existing dungeon → "Donate prisoners" flow (`PartyScreenHelper.OpenScreenAsDonatePrisoners`) — no new UI or menu options needed. This matches AI/Player parity: both deposit at same-faction castles, both get tracked.

**Persistence — depositor tracking added**
- `_depositorTracking`: `Dictionary<castleId, Dictionary<troopId, List<(heroId, count)>>>` — FIFO depositor entries per castle per troop type.
- Serialized via 4 parallel save/load lists: `b1071_cr_depCastles`, `b1071_cr_depTroops`, `b1071_cr_depHeroes`, `b1071_cr_depCounts`.
- Stale entries cleaned during `CleanupStalePrisonerEntries` (integrated into existing cleanup).
- Backward compatible: loading an old save with no depositor data treats all existing prisoners as untracked (castle owner gets 100%).

**Enslavement income (T1–T3 prisoners)** (previous implementation, now superseded by consignment split above)
- When T1–T3 prisoners are auto-enslaved at a castle, the castle owner (`settlement.Owner`) receives the dynamic slave market price per prisoner. Price is obtained from `nearestTown.Town.GetItemPrice(_slaveItem)` — the same town that receives the slave goods.
- Gold is created via `GiveGoldAction.ApplyBetweenCharacters(null, owner, totalIncome, true)` (same mechanism vanilla uses for prisoner ransoming). This makes prisoner management a genuine income source for lords — even T1 prisoners (ransom value ~30g) become worth the full slave market price (e.g., 154g).

**Recruitment income (elite pool & converted prisoners)** (new)
- When any party recruits from a castle’s elite pool or converted prisoner list, the gold cost is now routed to the castle owner.
- **Same-clan lords recruit for free** — lords from the same clan as the castle owner (including the owner themselves) pay no gold cost. This models the clan’s internal resource sharing (same household, shared holdings).
- **Cross-clan lords pay the castle owner** — lords from different clans (even if same kingdom/faction) pay the listed gold cost. Gold transfers via `GiveGoldAction.ApplyBetweenCharacters(recruiter, owner, cost, true)`.
- **Player follows the same rules** — recruiting from your own clan’s castle is free; recruiting from an allied/neutral castle pays the owner.
- Garrison absorption remains free (internal castle operation, no external buyer).

**Economic impact**
- Castles now generate meaningful income for their owners through two channels: enslaved prisoners (market-price-driven, potentially lucrative) and recruitment fees from visiting lords of other clans.
- This creates a positive feedback loop: lords who invest prisoners in their castles are rewarded with income, and well-stocked castles attract recruitment from allies (generating further income).
- The income is dynasty-level: `settlement.Owner` returns the clan leader, so all castle income flows to the clan head.


**Prisoners from visiting lords never appeared in castle pending/ready lists** (fixed)
- **Symptom:** AI lords visiting castles deposited prisoners, but none of them ever appeared in the Pending or Ready prisoner lists. Only prisoners originating from siege conquests or game start were tracked. No matter how many parties visited, the prisoner counts never grew.
- **Root cause:** Vanilla's `PartiesSellPrisonerCampaignBehavior.OnSettlementEntered` creates a roster of ALL non-hero regular prisoners from the visiting party and calls `SellPrisonersAction.ApplyForSelectedPrisoners`. Inside that action, for regular prisoners, the code executes `sellerParty.PrisonRoster.AddToCounts(character, -count)` (removes from party) and calculates gold — but **never calls** `buyerParty.PrisonRoster.AddToCounts(character, count)`. The prisoners are removed from the party's roster and gold is given; they are never transferred to the settlement's prison roster. They simply vanish.
- **Impact:** Our castle recruitment system reads from `settlement.Party.PrisonRoster` to track T4+ prisoner conversion. Since vanilla never puts visiting lords' prisoners there, the entire prisoner conversion pipeline was starved of input — it could only work with prisoners originating from siege conquests (the only source that writes directly to the settlement roster).
- **Fix:** Added `B1071_CastlePrisonerDepositPatch` — a Harmony Prefix on the private `OnSettlementEntered` method. For castles with castle recruitment enabled, ALL non-hero regular prisoners are moved from the party's prison roster directly into the castle's prison roster (free deposit, no gold paid to the lord). After this prefix, the party's roster contains only heroes; vanilla's handler still runs, finds no regulars, and handles hero prisoners normally. T1–T3 prisoners deposited will be auto-enslaved on the next daily tick; T4+ prisoners begin their conversion day-count immediately.

**AI lord flickering at castles** (fixed)
- **Symptom:** Lords entered a castle, exited for a split-second, re-entered, exited again, repeating several times before stabilizing. Only occurred after the castle recruitment system was added.
- **Root cause:** `AiAutoRecruit` called `party.MemberRoster.AddToCounts()` which fires `CampaignEventDispatcher.OnPartySizeChanged` and bumps `MobileParty.VersionNo`, invalidating cached AI decisions. On the next engine tick, `MobilePartyTickData.CheckExitingSettlementParallel` checks `ShortTermTargetSettlement == CurrentSettlement` — the version bump caused AI to re-evaluate its target, briefly picking a different destination, triggering an exit → immediate re-entry loop.
- **Fix:** After modifying any party's roster at a castle, `SetMoveGoToSettlement(settlement)` + `RecalculateShortTermBehavior()` is called, re-anchoring the party so that `ShortTermTargetSettlement` remains equal to `CurrentSettlement` through the next tick check. Applied to both elite and prisoner recruitment paths.

**Manpower cost formula changed to flat 1 per troop** (changed)
- `GetManpowerCostPerTroop` now returns a flat `BaseManpowerCostPerTroop` (default 1) regardless of tier.
- Old formula `baseCost + ((tier-1) / tiersPerStep)` scaled T1=1, T2=1, T3=2, T4=2, T5=3. Now all tiers cost 1.
- MCM settings `TiersPerExtraCost` and `CostMultiplierPercent` are no longer used (kept for save compatibility).

**Prisoner recruitment costs zero manpower** (changed)
- `TryRecruitPrisoner` (player path) no longer checks or consumes manpower. Prisoner recruitment still costs gold.
- AI prisoner recruitment also costs zero manpower.
- Rationale: prisoners are already captured — recruiting them does not drain the settlement's population.

**Elite pool regen manpower mismatch** (fixed)
- **Bug:** `RegenerateElitePool` used `CastleEliteManpowerCost=10` for the affordability check (how many troops can we afford?) but then called `ConsumeManpowerPublic` per-troop using `GetManpowerCostPerTroop` (which was tier-based, 1-3). The actual drain was far less than what the affordability check budgeted.
- **Fix:** Added `ConsumeManpowerFlat(settlement, totalCost)` method to `B1071_ManpowerBehavior` that drains an exact flat amount. Regen now consumes exactly `toAdd × CastleEliteManpowerCost` — matching the affordability check precisely.

**AI elite manpower affordability check off by factor of `BaseManpowerCostPerTroop`** (fixed)
- **Bug:** `AiAutoRecruit` assumed 1 manpower per troop (`take = Math.Min(take, curMp)`), but `ConsumeManpowerPublic` internally used `GetManpowerCostPerTroop` which reads `Settings.BaseManpowerCostPerTroop`. If the MCM setting was > 1, the check would allow recruiting more troops than the manpower pool could actually cover (consumption is capped to available, so manpower couldn't go negative, but the cost-per-troop contract was violated).
- **Fix:** Affordability check now uses `Math.Max(1, Settings.BaseManpowerCostPerTroop)` and the consumption switched to `ConsumeManpowerFlat(settlement, take × mpCostPer)` for explicit alignment.

**Garrison prisoner absorption bypassed when manpower was zero** (fixed)
- **Bug:** `GarrisonAbsorbPrisoners` called `GetMaximumDailyAutoRecruitmentCount(town)` to get the daily absorption rate. This call goes through our `B1071_GarrisonAutoRecruitManpowerPatch` Harmony postfix, which caps the result to current manpower. Since prisoner absorption costs zero manpower, a castle with 0 manpower would get `dailyCap = 0` — the garrison could never absorb prisoners even though absorption is population-free.
- **Fix:** Hardcoded `dailyCap = 1` (matching the vanilla model's constant return value) instead of calling the patched model. The rate is now independent of manpower state.

### UI Changes

**Gold / Manpower stats row layout** (changed)
- "Gold" stays pinned left, "Manpower" moved to right side of the stats row with 120px right margin for visual centering.
- Replaced single `HorizontalLeftToRight` ListPanel with a `Widget` container holding two independently aligned child groups.

### Internal Changes

- New files: `B1071_CastleRecruitmentBehavior.cs` (~960 lines), `B1071_CastleRecruitmentScreen.cs`, `B1071_CastleRecruitmentVM.cs`, `B1071_CastleRecruitTroopVM.cs`, `B1071_CastlePrisonerRetentionPatch.cs`, `B1071_CastlePrisonerDepositPatch.cs`, `B1071_CastleRecruitment.xml` (Gauntlet prefab)
- `B1071_CastleRecruitmentBehavior.AiAutoRecruit()`: Complete rewrite — now recruits from both elite pool and converted prisoners, same-clan free / cross-clan pays owner, no daily cap, includes flickering fix.
- `B1071_CastleRecruitmentBehavior.AutoEnslaveLowTierPrisoners()`: Now pays castle owner the dynamic slave market price per enslaved prisoner via `GiveGoldAction`.
- `B1071_CastleRecruitmentBehavior.TryRecruitPrisoner()` / `TryRecruitElite()`: Same-clan recruitment is free; cross-clan gold routed to castle owner via `GiveGoldAction.ApplyBetweenCharacters`.
- `B1071_CastleRecruitmentBehavior.GarrisonAbsorbPrisoners()`: New method — daily garrison absorption of ready prisoners; hardcoded rate of 1/day, bypassing manpower-gated model.
- `B1071_ManpowerBehavior.GetManpowerCostPerTroop()`: Simplified to flat `BaseManpowerCostPerTroop`.
- `B1071_ManpowerBehavior.ConsumeManpowerFlat()`: New method — consumes exact flat manpower amount, ignoring troop tier.
- `B1071_CastleRecruitmentBehavior.TryRecruitPrisoner()`: Removed manpower check/consumption.
- `B1071_CastleRecruitmentBehavior.AiAutoRecruit()`: Elite manpower check now uses `BaseManpowerCostPerTroop` setting + `ConsumeManpowerFlat` for aligned affordability.
- `B1071_McmSettings.CastleEliteAiRecruits`: Updated hint text to reflect both pools + no cap.
- `B1071_McmSettings.CastleEliteAiMaxPerDay`: Marked as legacy (kept for save compatibility).
- Harmony patch count: 29 → 31 (+`B1071_CastlePrisonerRetentionPatch`, +`B1071_CastlePrisonerDepositPatch`)

### Save Compatibility

**100% save-compatible with 0.1.6.x saves.** No new campaign required.
- New `SyncData` keys (`b1071_cr_prisonerCastles`, `b1071_cr_prisonerTroops`, `b1071_cr_prisonerDays`, `b1071_cr_eliteCastles`, `b1071_cr_eliteTroops`, `b1071_cr_eliteCounts`) are absent from old saves — dictionaries initialize empty. Castle recruitment begins immediately on first daily tick (elite pool starts empty and fills over the first few days; prisoners start day-counting from load).
- `CastleEliteAiMaxPerDay` MCM setting kept in save data but ignored at runtime.
- Garrison absorption starts immediately on load — any existing ready prisoners will begin transferring to garrison at 1/day.

---

## [0.1.6.0] — 2026-02-24

### New Systems

**Minor Faction Economy — Frontier Revenue** (new)
- Non-bandit minor factions receive a daily "Frontier Revenue" stipend to prevent bankruptcy under tier-exponential wages.
- Revenue = `clanTier × stipendPerTier` (denars/day). Mercenary clans receive less (default 250/tier) than unaligned factions (default 400/tier) since mercs already get contract pay.
- **Player clan explicitly excluded** — this revenue is for AI minor factions only. `Clan.IsMinorFaction` returns `true` for an independent player, but frontier revenue is not appropriate for the player who already has settlement income.
- Applied via Harmony Postfix on `DefaultClanFinanceModel.CalculateClanIncomeInternal`. Visible as "Frontier Revenue" in the clan finance tooltip.
- MCM: **Minor Faction Economy** group (GroupOrder 19) with master toggle, mercenary stipend per tier, and unaligned stipend per tier.

**Provincial Governance — Governance Strain** (new)
- Wars, raids, sieges, and conquests accumulate governance strain on settlements. High strain reduces loyalty, security, and prosperity — modelling the administrative cost of prolonged conflict.
- Strain decays at a configurable rate (default 0.3/day — a +10 raid strain takes ~33 days to fully decay).
- Penalties scale linearly from 0 at strain 0 to configurable maximums at the strain cap (default 100): loyalty penalty up to -3.0/day, security -2.0/day, prosperity -1.0/day.
- Applied via three Harmony Postfixes: `B1071_GovernanceLoyaltyPatch`, `B1071_GovernanceProsperityPatch`, `B1071_GovernanceSecurityPatch`. All visible in vanilla tooltips as "Governance Strain".
- MCM: **Provincial Governance** group (GroupOrder 20) with master toggle, decay rate, penalty caps, and strain cap.

**Frontier Devastation** (new)
- Village raids accumulate persistent devastation (0–100) that decays slowly. Unlike vanilla's binary Looted/Normal state, this creates persistent regional degradation from repeated frontier raiding.
- +25 devastation per raid (configurable), -0.5/day decay during Normal state only (frozen while Looted/BeingRaided). A single raid takes 50 days to fully heal; two rapid raids = 50 devastation = severe penalties.
- Four effect patches: hearth growth penalty (village), prosperity penalty (bound town), security penalty (bound town), food supply penalty (per devastated village). All visible in vanilla tooltips as "Frontier Devastation".
- MCM: **Frontier Devastation** group (GroupOrder 21) with master toggle, devastation per raid, decay rate, and all penalty caps.

**Slave Economy Enhancements — Food Consumption & Decay** (new)
- **Slave food consumption** — Each slave in the town market now consumes 0.05 food/day (historically, enslaved labourers received subsistence rations comparable to garrison troops). Visible in the food tooltip as "Slave Upkeep". Creates a natural economic cap on slave hoarding: at some point, the food cost outweighs the prosperity/construction benefits.
  - 50 slaves → -2.5 food/day (noticeable)
  - 100 slaves → -5.0 food/day (significant — roughly a village's output)
  - 200 slaves → -10.0 food/day (severe)
- **Slave attrition (decay)** — 1% of the slave population is lost per day (deaths, escapes, manumission). Without decay, slave populations grow indefinitely; with it, inflow must match decay for equilibrium. Uses a fractional accumulator to prevent rounding loss (persisted via SyncData).
- MCM: `SlaveFoodConsumptionPerUnit` (default 0.05, range 0–0.1) and `SlaveDailyDecayPercent` (default 1.0%, range 0–10) in the **Slave Economy** group.

### Compatibility — EconomyOverhaul v1.1.6

**Full compatibility achieved with Bannerlord.EconomyOverhaul** (new)
- 23/23 Byzantium1071 systems now work correctly alongside EO. Verified via DLL decompilation (reflection + IL analysis).

**Volunteer model** — Converted from `AddModel(B1071_ManpowerVolunteerModel)` to a Harmony Postfix (`B1071_ManpowerVolunteerPatch`) on `DefaultVolunteerModel.GetDailyVolunteerProductionProbability`. This eliminates the "last AddModel wins" conflict — both B1071's manpower gating and EO's troop policy system coexist because EO overrides `GetBasicVolunteer` (a different method) and does not override `GetDailyVolunteerProductionProbability`.
- Deleted `B1071_ManpowerVolunteerModel.cs`. Removed `AddModel` call from `SubModule.cs`.

**Food model** — EO's `BLM_TownProductionModel` inherits from abstract `SettlementFoodModel` (not `DefaultSettlementFoodModel`), making B1071's static `[HarmonyPatch]` on `DefaultSettlementFoodModel` dead code when EO is loaded. Added runtime model detection in `B1071_DevastationBehavior.ApplyDynamicFoodPatchIfNeeded()`:
- Queries `Campaign.Current.Models.SettlementFoodModel` at session launch.
- If the model inherits from `DefaultSettlementFoodModel` → static patches work, nothing to do.
- If not (e.g. EO's model) → dynamically patches both `B1071_DevastationFoodPatch.Postfix` and `B1071_SlaveFoodPatch.Postfix` onto the actual model's `CalculateTownFoodStocksChange` via reflection-based Harmony patching.
- Logged at runtime: `"[Byzantium1071] Non-default food model detected (BLM_TownProductionModel), applied 2 dynamic compat food patches"`.

**Other EO systems confirmed compatible without changes:**
- Prosperity/Loyalty/Security models: EO calls `base` → B1071 Postfixes fire during the base call ✅
- Clan finance model: EO delegates to base → `CalculateClanIncomeInternal` still called → B1071 Postfix fires ✅
- Construction model: EO calls `base` → B1071 slave construction Postfix fires ✅
- Hearth model: EO inherits but doesn't override `CalculateHearthChange` → B1071 Postfix fires normally ✅
- All 14 non-overlapping patches (recruitment, diplomacy, combat, wages): zero EO interaction ✅

**EO Slave System** — No conflict. EO has village-level slaves (VillageAddonsBehavior: prisoner roster, land production); B1071 has town-level slaves (item-based market goods, construction + prosperity + manpower + food bonuses). Different locations, different storage, different effects. Complementary.

### Bug Fixes

**Player receiving Minor Faction frontier revenue** (fixed)
- `Clan.IsMinorFaction` returns `true` for an independent player clan (no kingdom). The frontier revenue patch was not excluding the player, granting 1600 denars/day (Tier 4 × 400/tier).
- Fix: Added `if (clan == Clan.PlayerClan) return;` after the `IsMinorFaction` check.

**Slave decay accumulator could grow unbounded** (fixed)
- If the player sold slaves between daily ticks, `wholeLoss` could exceed `slaveCount`, causing the removal to be skipped entirely while `accum` kept growing.
- Fix: `wholeLoss = Math.Min((int)accum, slaveCount)` — clamps to available stock.

### Internal Changes

- Harmony patch count: 27 → 29 (+`B1071_ManpowerVolunteerPatch`, +`B1071_SlaveFoodPatch`)
- Model count: 2 → 1 (deleted `B1071_ManpowerVolunteerModel`, kept `B1071_ManpowerMilitiaModel`)
- New files: `B1071_ManpowerVolunteerPatch.cs`, `B1071_SlaveFoodPatch.cs`, `B1071_GovernanceBehavior.cs`, `B1071_GovernanceLoyaltyPatch.cs`, `B1071_GovernanceProsperityPatch.cs`, `B1071_GovernanceSecurityPatch.cs`, `B1071_DevastationBehavior.cs`, `B1071_DevastationFoodPatch.cs`, `B1071_DevastationHearthPatch.cs`, `B1071_DevastationProsperityPatch.cs`, `B1071_DevastationSecurityPatch.cs`, `B1071_MinorFactionIncomePatch.cs`
- Deleted files: `B1071_ManpowerVolunteerModel.cs`
- MCM settings: +16 new settings across 3 new groups (Minor Faction Economy, Provincial Governance, Frontier Devastation) + 2 new settings in Slave Economy group
- Dynamic food patch system (`ApplyDynamicFoodPatchIfNeeded`) uses `_dynamicFoodPatchApplied` static flag — survives across game loads within the same application session

### Save Compatibility

**100% save-compatible with 0.1.5.x saves.** No new campaign required.

- All new `SyncData` fields use graceful null handling: `_decayAccumulator ??= new Dictionary<string, float>()` and `_devastationByVillage ??= new Dictionary<string, float>()`.
- New behaviors (`GovernanceBehavior`, `DevastationBehavior`) start with empty dictionaries on first load — strain and devastation begin at 0, identical to a fresh game.
- New MCM settings use `Defaults` fallback — missing JSON config keys resolve to defaults without errors.
- No `AddModel` changes for existing models (militia model unchanged). Volunteer model was an `AddModel` in 0.1.5.x → now a Harmony Postfix, which is additive (no model slot conflict on load).
- Slave decay accumulator (`b1071_slaveDecayAccum`) is a new `SyncData` key — absent from old saves, initialised as empty dictionary (no decay backlog).
- `_initialStockSeeded` persists from old saves — re-seeding is impossible.

### Documentation

- Changelog updated for all 0.1.6.0 changes.
- `Byzantium1071modexplanation.md` updated with new systems and architecture table.
- `Compatibility_Report.md` updated to reflect 29 patches, deleted model, and EO compat fixes.
- `EO_Compatibility_Plan.md` marked as implemented.
- Nexus mod description updated with new systems section.

---

## [0.1.5.1] — 2026-02-24

### Compatibility — Bannerlord 1.3.15

**Full 1.3.15 verification and crash-hardening pass**
- All 19 Harmony patches verified against v1.3.15 — all target types, method signatures, and parameter lists confirmed identical.
- Only API change detected: `RecruitmentVM.RefreshScreen` changed from private → public. Harmless for Harmony (patches work on both private and public methods). Comment updated in `B1071_PlayerRecruitmentUiStatePatch.cs`.
- All 19 Harmony patches wrapped in try-catch: Postfixes catch + log (vanilla result stands), Prefixes catch + log + `return true` (original method always runs). A mod error will **never crash the game**.
- Fixed `null!` in `ExplainedNumber.Add()` calls in `B1071_SlaveConstructionPatch` and `B1071_SlaveProsperityPatch` — replaced with 2-parameter `Add(value, description)` overload.
- Fixed missing `e.Character != null` null guard in `OnSettlementEntered` LINQ predicate (`B1071_SlaveEconomyBehavior`).
- Added warning log when `b1071_slave` item fails to resolve in `OnSessionLaunched`.
- Updated all version reference comments in source code from v1.3.14 → v1.3.15.
- **100% save-compatible** — no data structure changes, no new behaviors or models. Existing saves work without starting a new campaign.

### Documentation

**Updated: `API` (Nexus mod page)**
- Version reference updated from v1.3.14 → v1.3.15.
- Added save compatibility notice at top of page.
- Added crash-safety note in Requirements section (all 19 patches try-catch wrapped).
- Bold styling applied to key terms and section headers throughout.

**Updated: `bannerlord_api_reference.md`**
- Header and all version stamps updated from 1.3.14 → 1.3.15.
- `RecruitmentVM.RefreshScreen()` visibility updated from private → public with version note.

**Updated: `bannerlord_api_research_deep_2026-02-20.md`**
- Header updated from 1.3.14 → 1.3.15 with reverification note.

---

## [0.1.5.0] — 2026-02-23

### New Features

**Noble-capture war exhaustion**
- Capturing an enemy lord/lady (`CampaignEvents.HeroPrisonerTaken`) now inflicts configurable war exhaustion on the victim's kingdom (default +5.0 per noble).
- Controlled by MCM settings `EnableNobleCaptureExhaustion` and `NobleCaptureExhaustionGain` in the **War Exhaustion** group.
- Respects the manpower-depletion amplifier (below) if enabled.

**Tier-weighted battle casualty drain**
- Battle casualty pool drain (`DrainPoolFromSide`) now applies per-tier multipliers: T1×1.0, T2×1.1, T3×1.25, T4×1.5, T5×1.75, T6×2.0. Losing veteran troops costs proportionally more manpower than losing levies.
- Requires iterating `TroopRoster.GetTroopRoster()` entries; the new `CalcTierWeightedDrain` helper handles this.
- Controlled by `EnableTierWeightedCasualties` (War Exhaustion group). When disabled, falls back to flat `TotalManCount × multiplier` (vanilla behavior).

**Manpower-depletion exhaustion amplifier**
- Every exhaustion gain (`AddWarExhaustion`) is now optionally scaled by the generating kingdom's average pool fill: `amount *= 1 + (1 − avgRatio) × amplifier`. At the default amplifier of 1.0, a kingdom at 0% average manpower pays double exhaustion per event.
- New helper `GetKingdomAverageManpowerRatio(Kingdom)` iterates the kingdom's towns and castles. Exposed as `internal` for use by the diplomacy patch.
- Controlled by `EnableManpowerDepletionAmplifier` and `ManpowerDepletionAmplifier` (War Exhaustion group).

**Low-manpower AI diplomacy pressure (independent of exhaustion)**
- When a kingdom's average settlement manpower fill falls below the configured threshold (default 40%), AI clans gain an additional peace-support bonus in both `DeclareWarDecision.DetermineSupport` and `MakePeaceKingdomDecision.DetermineSupport`.
- Pressure scales linearly from 0 at the threshold to `ManpowerDiplomacyPressureStrength` (default 200) at 0% fill.
- Runs **before** the exhaustion gate, so it fires even when `EnableExhaustionDiplomacyPressure` is disabled.
- Controlled by `EnableManpowerDiplomacyPressure`, `ManpowerDiplomacyThresholdPercent`, and `ManpowerDiplomacyPressureStrength` (Diplomacy (War Exhaustion) group).

### UI / UX

**Search bar — space key fix**
- **Root cause:** `InputKey.Space` is bound to a Bannerlord campaign `GameKey` (time acceleration). The GameKey system marks the event consumed before the OS character-input pipeline fires — `EditableTextWidget.OnCharInput` never receives the space character even when the search field has focus.
- **Fix:** `Input.IsKeyPressed(InputKey.Space)` intercepted in `OverlayController.Tick()` when `_activeTab == Search`. Each intercepted press calls `SetSearchQuery(_searchQuery + " ")` directly.
- **Enter key:** `Input.IsKeyPressed(InputKey.Enter)` intercepted in the same block to trigger `ExecuteSearch()`.

**Search bar — auto-focus on tab switch**
- `IsAutoFocused="true"` added to the `EditableTextWidget` in the XAML layout string. When the Search controls become visible (tab switch to Search), the widget auto-gains focus — typing begins immediately without clicking the field. Backspace is absorbed by the focused widget instead of triggering Bannerlord's back-navigation.

**Overlay close — same-frame input release**
- **Problem:** `ToggleVisibility` and `HideOverlayIfVisible` set `_viewDirty = true`, but the mixin reads this via `ConsumeViewDirty()` on the *next* `OnRefresh` frame. For one frame the `EditableTextWidget` remained visible in Gauntlet, consuming keypresses intended for the campaign map.
- **Fix:** `static Action? _forceSyncCallback` added to `B1071_OverlayController`. The mixin constructor registers `() => SyncFromController(notifyAll: true)`. Both hide paths invoke it immediately after setting state, synchronising Gauntlet widget visibility in the same frame.

### Performance & Internal Fixes

**`GetKingdomAverageManpowerRatio` — one-day cache**
- `AddWarExhaustion` (called per-party in `AccumulateBattleExhaustion`) and `GetManpowerDiplomacyPeaceBias` (called per-clan in `DetermineSupport`) both triggered a full settlement iteration each time.
- Added `Dictionary<string, float> _manpowerRatioCacheById` and `float _manpowerRatioCacheExpiryDay`. Cache expires once per in-game day. All callers share the same day's result at zero additional allocation cost.

**`DrainPoolFromSide` — guard ordering fix**
- The early-exit guard (`if drainDied + drainWounded <= 0 continue`) was placed after the drain computation rather than before, building a zero-value drain entry unnecessarily. Guard moved before the computation.

**Slave economy v3 — full redesign (replaces v2)**

*v3 removes all custom sale UI for selling, replaces one-time bonuses with continuous daily market bonuses, and routes all economic effects through Harmony model patches so they are visible in vanilla tooltips.*

**Custom ItemCategory `b1071_slaves`** (new `_Module/ModuleData/item_categories.xml`):
- `is_trade_good="true"`, `base_demand="0.1"`, `luxury_demand="0.1"`. Registered via `<XmlNode id="ItemCategories" path="item_categories">` in `SubModule.xml`.
- Replaces the previous `item_category="fur"` assignment that caused trade notifications to read "Fur was bought" instead of "Slaves".

**Acquisition — two sources (v3):**
1. **Village raids:** `ItemsLooted` event (fires per loot batch, same cadence as grain/clay in the raid log) + `MapEvent.IsRaid` filter. Slave count = `floor(village.Hearth / SlaveHearthDivisor)`. Replaces the `VillageBeingRaided` tick-based approach.
2. **Prisoner enslavement at town:** unchanged from v2 (all non-hero prisoners → Slave goods 1:1 via the `⛓ Enslave prisoners` submenu).

**Market-based daily bonuses (v3):**
- Each campaign day, every town whose `Settlement.ItemRoster` contains Slave goods receives three bonuses, all proportional to stock.
- **Manpower:** `B1071_SlaveEconomyBehavior.OnDailyTickSettlement` calls `AddManpowerToSettlement`.
- **Prosperity:** `B1071_SlaveProsperityPatch` — Harmony Postfix on `DefaultSettlementProsperityModel.CalculateProsperityChange(Town, bool)`. Adds `slaveCount × SlaveProsperityPerUnit × effectiveness` to the returned `ExplainedNumber` (struct, `ref` required). Bonus appears as **"Slave Labor"** in the Prosperity tooltip Expected Change breakdown. The previous direct `Town.Prosperity +=` has been removed — only the patch drives prosperity, preventing double-counting.
- **Construction:** `B1071_SlaveConstructionPatch` — Harmony Postfix on `DefaultBuildingConstructionModel.CalculateDailyConstructionPower(Town, bool)`. Adds `min(cap, slaveCount × SlaveConstructionAcceleration × effectiveness)` to the returned `ExplainedNumber`. Bonus appears as **"Slave Labor"** in the Construction tooltip (town Manage screen). The previous direct `BuildingProgress +=` has been removed.

**Default balance (calibrated for default 1.5× effectiveness multiplier):**
- 100 slaves → 75 construction/day (`acc=0.5`, `cap=150`, ×1.5 = 75)
- 100 slaves → ~1 prosperity/day (`0.0067 × 1.5 ≈ 0.01/slave`)
- 1 slave → ~1 manpower/day (`0.67 × 1.5 ≈ 1.0`)

**Removed from v2:**
- "Sell all slaves" submenu option (sell via standard Trade screen instead)
- One-time construction bonus with expiry timer (`B1071_SlaveConsBonusDays`)
- `SyncData` persistence of bonus state (all bonuses are now derived live from `Settlement.ItemRoster`)

**MCM:** 8 settings in **Slave Economy** group (GroupOrder 15): master toggle, `SlaveHearthDivisor`, `SlaveRansomMultiplier`, `SlaveManpowerPerUnit`, `SlaveProsperityPerUnit`, `SlaveConstructionAcceleration`, `SlaveConstructionBonusCap`, legacy duration (unused).

**`b1071_slaves` ItemCategory demand calibration — caravan trade fix**
- **Problem:** Initial demand values (`base_demand="0.1"`, `luxury_demand="0.1"`) were ~10× lower than vanilla trade goods (which are code-registered via `DefaultItemCategories` with no XML counterpart). Vanilla categories like `Fur` or `Velvet` use `base_demand ≥ 0.5–1.0` and `luxury_demand ≥ 0.5–1.5`.
- **Effect of too-low values:** (a) AI caravan route calculation found near-zero price differential between slave-holding and slave-free towns → caravans found no profitable route → no purchases. (b) Silent 1/week stock disappearance = `TownMarketData.UpdateStores()` daily consumption at `BaseDemand × 0.1` rate — visible as stock decreasing with no logged purchase. (c) Price barely responded to 89-unit supply injection because demand signal was too weak.
- **Fix:** `base_demand="0.3"` (moderate daily town consumption, comparable to mid-tier goods), `luxury_demand="1.0"` (luxury tier — caravans actively route for luxury goods, this directly activates caravan interest). Slaves now behave economically like wine or jewelry in terms of trade desirability.

**Daily slave bonus notification — spam fix and default off**
- Narrowed from "every slave-holding town in the world, every day" to "only the specific settlement the player is currently inside" (`Settlement.CurrentSettlement == settlement` guard added).
- Changed `ShowPlayerDebugMessages` default from `true` → `false`. Debug notifications are opt-in for testing; disabled for normal play.

**Slave economy — AI parity, initial stocks, and name fix**

*Full AI parity: AI lords now participate in the slave economy symmetrically with the player.*

**Trade notification name fix:**
- `name="{=b1071_slave_name}Slaves{@Plural}..."` → `name="{=!}Slaves"` in `items.xml`. The non-hashed `{=b1071_slave_name}` key was not registered in any GameTexts file; the engine showed "unassigned" in trade notifications instead of "Slaves". The `{=!}` prefix is the canonical Bannerlord literal-string marker — no translation lookup, always shows the literal text.

**AI raid acquisition (parity):**
- Removed `if (mobileParty != MobileParty.MainParty) return;` guard from `OnItemsLooted`. All parties now receive slave goods when raiding villages, using the same `floor(hearth / SlaveHearthDivisor)` formula as the player.
- Raid notification message is unchanged but is now guarded `if (mobileParty == MobileParty.MainParty)` — only shows for the player's own raids.

**AI settlement entry — slave deposit and tier-based prisoner enslavement (parity):**
- New `OnSettlementEntered` listener (`CampaignEvents.SettlementEntered`). Fires for every party entering any settlement.
- For AI lord parties only (`party.IsLordParty && party != MobileParty.MainParty`) entering towns:
  1. Any slave items already carried in `party.ItemRoster` (accumulated from raids) are transferred directly to the town's `Settlement.ItemRoster` (market).
  2. Tier ≤ 2 non-hero prisoners in `party.PrisonRoster` are enslaved and added directly to the town market (`settlement.ItemRoster.AddToCounts(_slaveItem, count)`).
  3. Tier 3+ non-hero prisoners are left untouched — vanilla ransom/release logic applies. This mirrors the player's incentive structure (T3+ worth more as ransom).
- The player manages enslavement manually via the existing `⛓ Enslave prisoners` town menu (unchanged).

**Initial town slave stocks (new game seeding):**
- `OnNewGameCreatedPartialFollowUpEndEvent` listener seeds each town with `MBRandom.RandomInt(0, 31)` slaves (0–30 inclusive) on new game creation.
- Guards: runs only once (`_initialStockSeeded` bool persisted via `SyncData`) — save-load does not re-seed.
- Makes the slave economy immediately visible from campaign turn one without requiring the player to find and raid their first village first.


### Documentation

**Updated: Nexus mod page description**
- Full rewrite to reflect all changes from 0.1.3 through 0.1.5 (WIP): tier-weighted casualty drain table in War Effects, manpower amplifier formula in War Exhaustion, manpower depletion pressure in Diplomacy Pressure, search bar notes in Ledger section, updated MCM group list with new War Exhaustion and Diplomacy settings.

**Updated: Nexus mod page description — pre-release final (2026-02-23)**
- Updated Slave Economy section: four acquisition sources (added AI raid parity and AI prisoner enslavement), initial town stock note, version label changed from `0.1.5.0 (WIP)` to `0.1.5.0`.

**Updated: `Byzantium1071modexplanation.md`**
- Section 23 (Slave Economy): updated Acquiring slaves to cover AI raid parity and AI tier-based prisoner enslavement, added Starting slave stocks subsection.



---

## [0.1.4.0] — 2026-02-22

### New Features

**`B1071_TierArmorSimulationPatch` — Tier armor in autoresolve simulation**
- **Problem / Root cause:** In vanilla autoresolve, `DefaultCombatSimulationModel.SimulateHit` computes damage purely from the *striker's* stats. No armor reduction is applied based on the struck troop's tier or equipment. This means a T1 bandit deals the same simulated damage to a T6 knight as to a T1 recruit. The fatal-hit stochastic gate (`RandomInt(MaxHitPoints) < damage`) provides mild protection for T6 through higher MaxHP, but at typical bandit damage values it is often insufficient. Victim selection (`SelectRandomSimulationTroop`) uses `MBRandom.RandomInt(NumRemainingSimulationTroops)` — uniform random — so high-tier troops are not over-selected. The disproportionate T6 casualties come from the armor gap, not from biased selection.
- **Fix:** Added a Postfix on `DefaultCombatSimulationModel.SimulateHit` (8-param troop-vs-troop overload, `nameof`-verified, v1.3.14) that reduces the `ExplainedNumber` damage result based on the struck troop's tier before it is cast to an integer and passed to the fatal-hit gate:
  | Tier | Damage reduction |
  |------|-----------------|
  | T1 | 0% |
  | T2 | -6% |
  | T3 | -12% |
  | T4 | -18% |
  | T5 | -24% |
  | T6+ | -30% |
- **Combined effect with existing `B1071_FatalityPatch`:**
  - T6: gate fires ~30% less often (armor) *and* +25% survival when it does fire (survivability) — two independent layers of protection.
  - Heroes excluded from both patches (different death resolution path, near-invincible by baseline).
- **Scope — autoresolve only:** `DefaultCombatSimulationModel.SimulateHit` is not called during live battles. In live combat, damage uses actual weapon/armor stats from the Mission engine; the existing `GetSurvivalChance` Postfix (`FatalityPatch`) covers live-battle aftermath (wound vs. kill determination). Frequent T6 wounds in live battle against small bandit groups is a positioning effect (elite troops engage the most enemies, absorbing the most hits) and is not addressable without mission AI changes.
- **MCM:** Toggle `Combat Realism → Enable tier armor simulation` (default: on). Independent of `Enable tier survivability`.

**`B1071_ArmyEconomicsPatch` — Tier-exponential hire costs and daily wages**
- **Design intent:** Vanilla recruitment and wage costs are nearly flat across tiers, making it trivially cheap to maintain hundreds of T6 elite troops. Historically, raising and paying veteran soldiers required significantly more wealth than levying common fighters — a scarcity that shaped strategy. This patch reintroduces that economic pressure.
- **Implementation:** Two Postfixes targeting public, `nameof`-resolvable methods on `DefaultPartyWageModel` (verified v1.3.14):
  - **`GetTroopRecruitmentCost`** — `AddFactor` per struck tier on the returned `ExplainedNumber`. Because `GetGoldCostForUpgrade` calls `GetTroopRecruitmentCost` to compute its base (`(cost(target) − cost(current)) / 2`), upgrade gold costs automatically cascade from hire cost scaling — no separate upgrade patch needed, and no double-stacking is possible.
  - **`GetCharacterWage`** — int result multiplied by `(1 + wageFactor)` and rounded. Heroes/companions excluded (`character.IsHero` guard).
- **Scaling (tier-exponential, AddFactor values T1→T6):**

  | Preset | T1 | T2 | T3 | T4 | T5 | T6 |
  |--------|-----|-----|-----|-----|-----|-----|
  | 0 — Off | +0% | +0% | +0% | +0% | +0% | +0% |
  | 1 — Light | +0% | +15% | +35% | +65% | +100% | +150% |
  | 2 — Moderate (default) | +10% | +30% | +75% | +150% | +250% | +400% |
  | 3 — Severe | +25% | +75% | +175% | +350% | +600% | +1000% |

- **Hire gold reference at Moderate (vanilla → patched):** T1 20g→22g, T2 50g→65g, T3 100g→175g, T4 200g→500g, T5 400g→1400g, T6 600g→3000g.
- **Upgrade gold cascade at Moderate:** T4→T5 vanilla~50g → ~450g; T5→T6 vanilla~100g → ~800g.
- **Daily wage at Moderate (vanilla → patched):** T3 5d→8d, T4 8d→16d, T5 12d→31d, T6 17d→60d. Maintaining 100 T6 troops costs ~6000 denars/day.
- **Parity:** Applies identically to AI parties — lords cannot amass T6 armies cheaply either.
- **MCM:** `Army Economics → Hire & upgrade cost preset` (0-3, default 2) and `Army Economics → Daily wage preset` (0-3, default 2).

### Bug Fixes

**`B1071_PlayerRecruitmentUiStatePatch` — `vm.CanRecruitAll` unconditional override**
- **Problem:** The `Recruit All` button's enabled state was being set unconditionally to the mod's own calculation. This could re-enable the button in situations where vanilla had already disabled it for unrelated reasons (e.g. party size cap reached, no recruitable troops at all), allowing the button to appear available when it should not be.
- **Fix:** Changed the assignment to AND with the existing vanilla value:
  ```csharp
  // Before (bug):
  vm.CanRecruitAll = individualAvailableCount > 0 && allRecruitAllCandidatesAffordable;

  // After (correct):
  vm.CanRecruitAll = vm.CanRecruitAll && individualAvailableCount > 0 && allRecruitAllCandidatesAffordable;
  ```
- **Impact:** Manpower can now only further restrict the Recruit All button, never re-enable something vanilla disabled. Eliminates a potential silent exploit where a player could confirm a batch while the party was otherwise ineligible.

**`B1071_OverlayController.RebuildArmiesCache` — behavior lookup fix**
- **Problem:** The Armies tab cache rebuild was obtaining `B1071_ManpowerBehavior` via `Campaign.Current.GetCampaignBehavior<B1071_ManpowerBehavior>()`. This path can silently return `null` in edge cases (mid-load, game-end transitions, certain save/reload sequences) causing the Armies tab to display stale or empty war exhaustion values without any error.
- **Fix:** Replaced with direct access via the behavior's own static instance field:
  ```csharp
  // Before (fragile):
  var behavior = campaign.GetCampaignBehavior<B1071_ManpowerBehavior>();

  // After (correct):
  var behavior = B1071_ManpowerBehavior.Instance;
  ```
- **Impact:** Consistent with how all other overlay tab builders access the behavior. Eliminates the null-return risk during transitional game states.

### Code Quality

**String-literal Harmony patch fragility comments**
- Added version-pinned documentation comments to all four Harmony patch sites that target private game methods by string name:
  - `RecruitmentCampaignBehavior."ApplyInternal"` — AI recruitment gate
  - `RecruitmentVM."OnDone"` — player cart confirmation gate
  - `RecruitmentVM."RefreshScreen"` — UI state refresh
  - `RecruitmentVM."RefreshPartyProperties"` — party property refresh
- Each comment documents: the targeted type and method name, the game version it was verified against (v1.3.14), and a note to reverify after Bannerlord patches.

### Documentation

**New: `Docs/Byzantium1071modexplanation.md`**
- Comprehensive 21-section player-facing explanation of every mod system.
- Covers: manpower pools, regen chain, volunteer scaling, militia link, recruitment gate, culture discount, AI/garrison gates, war effects (raid/siege/battle/conquest), delayed recovery, war exhaustion, diplomacy pressure, pressure bands, forced peace, truce enforcement, combat realism, overlay (all 13 tabs), MCM configurability, compatibility notes, and design philosophy.



**Updated: Nexus mod page description**
- Full rewrite to accurately reflect all implemented systems.
- Removed factual error: recruitment cost was incorrectly described as scaling by "tier and available manpower" — cost scales by tier only; the pool is a depletable resource, not a price modifier.
- Added missing systems: Delayed Recovery, Pressure Bands with hysteresis, Forced Peace, Truce Enforcement, Combat Realism tier table, Volunteer Production Scale, Militia Link, Culture Discount.
- Removed "Future Vision" items that were already fully implemented (AI strategic reasoning, enhanced diplomacy pressure).
- Replaced vague compatibility note with specific identification of the 4 private-method string patches as fragility points.

---

## [0.1.3.0] — Initial release

- Manpower pool economy for all towns and castles
- 14-stage daily regen multiplier chain
- Volunteer production scaling by manpower ratio
- Militia growth link to manpower ratio
- Player recruitment gate (per-troop, Recruit All, Confirm/Done)
- UI state sync: greyed-out troops, yellow warning messages
- Culture discount on recruitment cost
- AI recruitment gate (symmetric with player)
- Garrison auto-recruit cap
- War effects: raid drain, siege aftermath, battle casualties, conquest retention
- Delayed recovery (post-war regen penalty with linear decay)
- War exhaustion per kingdom (accumulates from raids/sieges/battles/conquest, decays daily)
- War exhaustion regen penalty
- Diplomacy pressure: war declaration and peace vote modifiers
- Pressure bands with hysteresis (Low / Rising / Crisis)
- Forced peace at Crisis band
- Truce enforcement (3-mechanism registration)
- Combat realism: tier survivability bonus (T1–T6) on `GetSurvivalChance`
- Strategic intelligence overlay (13 tabs: Current, Nearby, Castles, Towns, Villages, Factions, Armies, Wars, Rebellion, Prisoners, Clans, Characters, Search)
- MCM full configuration (~80 settings across 10+ groups)
- Exception deduplication in `OnApplicationTick`
- Clean teardown on `OnGameEnd` and `OnSubModuleUnloaded`
