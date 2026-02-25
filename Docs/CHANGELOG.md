# Byzantium 1071 — Changelog

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

**Access rules**
- Player can recruit from any castle NOT hostile to them (own, allied, or neutral faction).
- AI lord parties recruit from same-faction castles only — visiting enemy or neutral castles does not trigger AI recruitment.
- Hostile castles are hidden from the game menu entirely (no greyed-out option, just not shown).
- The menu shows availability at a glance: "🏰 Recruit troops (5 available, 3 pending)".

**Player recruitment UI**
- Full Gauntlet screen with three scrollable lists: Elite Pool (culture troops), Ready Prisoners (converted), and Pending Prisoners (still waiting).
- Each troop entry shows: troop name, tier indicator, count available, gold cost, and a recruit button.
- Stats bar at top shows current gold (left) and castle manpower (right).
- Lists auto-refresh after each recruitment. Empty lists show a placeholder message.

**Vanilla prisoner selling — blocked at castles**
- The vanilla `PartiesSellPrisonerCampaignBehavior.DailyTickSettlement` sells ~10% of settlement prisoners daily at AI castles. This is blocked by `B1071_CastlePrisonerRetentionPatch` when castle recruitment is enabled. Without this, T4+ prisoners would vanish before finishing their waiting period.
- The vanilla `OnSettlementEntered` handler (which transfers a *mobile party's* prisoners into the castle prison) is NOT blocked — this is how prisoners arrive at castles in the first place.

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
- AI lords visiting their own faction's castles now recruit from **both** the elite troop pool **and** converted (ready) prisoners, paying gold per troop (same cost as the player).
- Previously, AI could only recruit from the elite pool. Converted prisoners (T4+) sat idle indefinitely because no code path existed for AI or garrison to claim them.
- Gold is deducted from `party.LeaderHero.Gold` via `ChangeHeroGold(-cost)`. Lords who can't afford a troop skip it.
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

### Bug Fixes

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

- New files: `B1071_CastleRecruitmentBehavior.cs` (~960 lines), `B1071_CastleRecruitmentScreen.cs`, `B1071_CastleRecruitmentVM.cs`, `B1071_CastleRecruitTroopVM.cs`, `B1071_CastlePrisonerRetentionPatch.cs`, `B1071_CastleRecruitment.xml` (Gauntlet prefab)
- `B1071_CastleRecruitmentBehavior.AiAutoRecruit()`: Complete rewrite — now recruits from both elite pool and converted prisoners, pays gold, no daily cap, includes flickering fix.
- `B1071_CastleRecruitmentBehavior.GarrisonAbsorbPrisoners()`: New method — daily garrison absorption of ready prisoners; hardcoded rate of 1/day, bypassing manpower-gated model.
- `B1071_ManpowerBehavior.GetManpowerCostPerTroop()`: Simplified to flat `BaseManpowerCostPerTroop`.
- `B1071_ManpowerBehavior.ConsumeManpowerFlat()`: New method — consumes exact flat manpower amount, ignoring troop tier.
- `B1071_CastleRecruitmentBehavior.TryRecruitPrisoner()`: Removed manpower check/consumption.
- `B1071_CastleRecruitmentBehavior.AiAutoRecruit()`: Elite manpower check now uses `BaseManpowerCostPerTroop` setting + `ConsumeManpowerFlat` for aligned affordability.
- `B1071_McmSettings.CastleEliteAiRecruits`: Updated hint text to reflect both pools + no cap.
- `B1071_McmSettings.CastleEliteAiMaxPerDay`: Marked as legacy (kept for save compatibility).
- Harmony patch count: 29 → 30 (+`B1071_CastlePrisonerRetentionPatch`)

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
