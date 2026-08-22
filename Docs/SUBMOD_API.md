# Campaign++ (Byzantium1071) — Public API for Submods

This document defines the **stable public API** that third-party submods can safely depend on, and outlines integration best practices.

---

## Core Principles

- **Stable public surface** = public classes, public methods, and documented Instance accessors
- **Private/internal** = implementation details that may change between minor versions
- **Semantic versioning** = breaking changes only on major version bumps (1.x → 2.x)
- **Harmony safety** = public class targets only; private method patches versioned
- **Settings via MCM** = never directly mutate behavior dicts; use settings provider

---

## Public Behavior Instances

All Campaign++ system behaviors expose a static `Instance` property for read-only queries. These are initialized during campaign load and cleared on campaign end.

### B1071_ManpowerBehavior

**Location:** `Byzantium1071.Campaign.Behaviors`  
**Access:** `B1071_ManpowerBehavior.Instance`

**Public read-only methods:**
- `GetManpowerPool(Settlement settlement, out int current, out int maxDaily, out int maxTotal)` — retrieve the settlement's manpower state
- `GetWarExhaustion(string kingdomStringId)` → `float` — retrieve current exhaustion for a kingdom
- `IsMultiFrontCrisis(Kingdom kingdom)` → `bool` — check if a kingdom meets multi-front crisis conditions
- `CanRecruitCountForPlayer(Settlement settlement, MobileParty party, CharacterObject troop, int amount, out int available, out int costPer, out Settlement? pool)` → `bool` — query whether manpower availability permits recruitment

**Intended use:** Submods can query manpower state and war exhaustion to make AI decisions, detect crisis conditions, or adjust recruitment logic

**Off-limits:**
- Direct mutation of `_manpowerPools`, `_warExhaustion`, `_casualtiesByPair` dicts (use settings/behavior provided methods)
- Private helper methods like `ApplyWarEffectsToKingdom`, `TrackWallVaultTrustAndPraseodymium`

**Example:**
```csharp
var behavior = B1071_ManpowerBehavior.Instance;
if (behavior != null && behavior.GetManpowerPool(mySettlement, out int cur, out _, out _))
{
    if (cur < 100 && behavior.GetWarExhaustion(mySettlement.OwnerClan.Kingdom.StringId) > 50f)
    {
        // Settlement is in crisis: low manpower + high exhaustion
    }
}
```

---

### B1071_CastleRecruitmentBehavior

**Location:** `Byzantium1071.Campaign.Behaviors`  
**Access:** `B1071_CastleRecruitmentBehavior.Instance`

**Public read-only methods:**
- `GetRecruitablePrisoners(Settlement castle)` → `List<(CharacterObject troop, int count, int days_held, int gold_cost)>` — list all ready-to-recruit prisoners at a castle
- `GetElitePoolCount(Settlement castle, CharacterObject troop)` → `int` — retrieve elite pool stock for one troop at a castle
- `IsLowTierEnslavementAvailable(Settlement castle)` → `bool` — whether low-tier prisoners held at this castle can be processed at all: the Slave Economy is enabled **and** the castle's faction owns a town to sell to. Deliberately does not test the current slave price, which is a temporary condition. Returns `false` for a null settlement. *(v1.0.2.4)*

**Intended use:** Query castle recruitment state for overlay/reporting, prison processing logic, or allied settlement analysis

**Off-limits:**
- Prison roster mutation (use Bannerlord's prisoner actions)
- `_elitePool`, `_prisonerDaysHeld`, `_depositorTracking` dicts (internal state)
- `AutoEnslaveLowTierPrisoners`, `DrainStrandedLowTierPrisoners`, `RegenerateElitePool` (called from daily tick)

**Example:**
```csharp
var behavior = B1071_CastleRecruitmentBehavior.Instance;
if (behavior != null)
{
    var readyPrisoners = behavior.GetRecruitablePrisoners(myCastle);
    foreach (var (troop, count, days, goldCost) in readyPrisoners)
    {
        // Process or analyze ready prisoners
    }
}
```

---

### B1071_DemobilizationBehavior

**Location:** `Byzantium1071.Campaign.Behaviors`  
**Access:** `B1071_DemobilizationBehavior.Instance`

**Public read-only methods:**
- `GetMainPartyCohortsForUi()` → `List<CohortView>` — one row per group of interchangeable soldiers in the player's main party. Records are stored one man apiece; this method collapses men who share a troop, a home, an enlistment day and an extension count into a single row, so `Count` is the size of the group and `CohortIndex` is the first record in it *(grouping added in v1.0.2.7)*
- `CanPlayerAccessVeteranRegister(Settlement? settlement)` → `bool` *(static)* — whether the player may hire **anyone** from this settlement's veteran register under the configured access level *(v1.0.2.7)*
- `TryGetPlayerRegisterAccess(Settlement? settlement, out bool ownMenOnly)` → `bool` *(static)* — whether the register opens at all, and on what terms. `false` when the player is at war with the owner. `true` with `ownMenOnly == false` is full access; `true` with `ownMenOnly == true` means he may take back only the men he discharged there himself. Prefer this over `CanPlayerAccessVeteranRegister`, which answers only the first half *(v1.0.2.7)*
- `GetVeteranCountAt(Settlement? settlement)` → `int` — how many discharged veterans are waiting at a settlement; `0` for null and for a settlement with no register. It reports the stock, not your ability to hire it: it does **not** consult `EnableDemobilizationVeteranReturn`, so pair it with `TryGetPlayerRegisterAccess` before offering the player anything *(v1.0.2.7)*
- `GetVeteranCountAt(Settlement? settlement, bool ownMenOnly)` → `int` — same, counting only the player's own discharged men when `ownMenOnly` is set. Feed it the flag from `TryGetPlayerRegisterAccess` *(v1.0.2.7)*

**Public mutating methods:**
- `GetVeteransForUi(Settlement? settlement)` → `List<VeteranView>` — one row per troop type on that settlement's register, with prices and the per-row block reason. Listed here rather than above because it runs `CleanupVeteranRegister` first: reading the register is what ages expired entries off it *(v1.0.2.7)*
- `RegisterDirectRecruitment(MobileParty party, CharacterObject troop, int amount, string source)` → `int` — register soldiers added outside a vanilla recruitment event so they start at service age 0
- `RegisterDirectRecruitment(MobileParty party, CharacterObject troop, int amount, string source, Settlement? homeSettlement)` → `int` — same, but records the settlement the men return to on discharge. The four-argument overload is kept and simply passes `null`. *(v1.0.2.7)*
- `TryExtendCohort(string partyId, string troopId, int cohortIndex)` → `bool` — buy one service extension for a single tracked record; fails if gold is short or the extension cap is reached. Records hold one man, so this extends one man
- `TryExtendCohortGroup(string partyId, string troopId, string homeId, int joinDay, int extensionCount, int requested)` → `int` — extend service for men on one `CohortView` row, identified by the row's `TroopId`, `HomeId`, `JoinDay` and `ExtensionCount`. Every man carries the same fee, so the bill is `CohortView.ExtendCost` times the number extended; a batch the player cannot fully afford shrinks to what his gold covers rather than failing. Returns how many were kept on *(v1.0.2.7)*
- `TryDischargeCohort(string partyId, string troopId, string homeId, int joinDay, int extensionCount, int requested)` → `int` — release men from one `CohortView` row before their term ends, main party only. Returns how many actually left, trimmed to the row's headcount. Free, and exempt from the daily departure caps, but refused outright while the party is in a `MapEvent` or `SiegeEvent`. Routes through the same `SendVeteranHome` as a completed term, so the manpower credit, the return roll and the register entry are identical *(v1.0.2.7)*
- `TryRecallVeterans(Settlement? settlement, CharacterObject? troop, int requested)` → `int` — hire veterans back. Returns how many actually joined: the request is trimmed successively by register stock, party room, the player's gold, and the settlement's manpower, so asking for more than is possible returns a smaller number rather than failing. *(v1.0.2.7)*

**View types:**

```csharp
public sealed class CohortView
{
    public string PartyId, TroopId;
    public int CohortIndex;
    public CharacterObject Troop;
    public int Count, JoinDay, AgeDays, ThresholdDays, RemainingDays;
    public int ExtendCost;                      // per man, not per row
    public bool IsWarning, IsOverdue;
    public int ExtensionCount, MaxExtensions;   // v1.0.2.7
    public bool ExtensionsExhausted;            // v1.0.2.7
    public bool CanExtend;
    public string HomeId;                       // v1.0.2.7 - group key
    public string HomeName;                     // v1.0.2.7
    public bool ReturnsHome;                    // v1.0.2.7
}

public sealed class VeteranView                 // v1.0.2.7
{
    public string SettlementId, TroopId;
    public CharacterObject Troop;
    public int Count, Tier;
    public int GoldCostPerMan, ManpowerCostPerMan;
    public int DaysUntilGone;
    public bool CanRecallOne;
    public string BlockReason;
}
```

**Breaking change in v1.0.2.7:** `CohortView.HasBeenExtended` (`bool`) is gone. Extensions are now repeatable, so use `ExtensionCount`, `MaxExtensions`, and `ExtensionsExhausted` instead. `ExtensionCount > 0` is the exact equivalent of the old flag.

**Row identity in v1.0.2.7:** a `CohortView` is a group, not a slot. `CohortIndex` still points at the first record behind the row and is safe to display, but do not use it to act on the row — slot indices shift whenever emptied records are pruned. Pass `TroopId` + `HomeId` + `JoinDay` + `ExtensionCount` to the group methods instead; they re-resolve the records at the moment of the call.

**Intended use:** Display service state in an overlay, add your own recruitment source that participates in the service clock, or offer veterans through a different UI

**Off-limits:**
- `_serviceCohorts`, `_transferReserve`, `_veteranRegister` dicts (internal state)
- `SendVeteranHome`, `ScatterVeteransAt`, `CleanupVeteranRegister`, `RetireOverdueCohorts` (daily-tick lifecycle)
- Removing troops from a roster yourself to "discharge" them — that is the manpower leak this system exists to close. Let the daily tick do it, or call `TryDischargeCohort` if you need it to happen now

**Example:**
```csharp
var behavior = B1071_DemobilizationBehavior.Instance;
if (behavior != null && B1071_DemobilizationBehavior.TryGetPlayerRegisterAccess(settlement, out bool ownMenOnly))
{
    // ownMenOnly == true on foreign ground: the rows below are the player's own
    // discharged men, and nobody else's, which is all he is entitled to take there.
    foreach (var row in behavior.GetVeteransForUi(settlement))
    {
        // row.Troop, row.Count, row.GoldCostPerMan, row.DaysUntilGone
    }

    int joined = behavior.TryRecallVeterans(settlement, someTroop, 5);
}
```

---

### B1071_SlaveEconomyBehavior

**Location:** `Byzantium1071.Campaign.Behaviors`  
**Access:** `B1071_SlaveEconomyBehavior.Instance`

**Public read-only methods:**
- `GetSlaveProductionRate(Settlement town)` → `float` — retrieve effective daily slave production (from market and garrison labor)

**Intended use:** Analyze settlement labor capacity, economic output, or AI decision-making

**Off-limits:**
- Town/garrison labor allocation logic (internal)
- Slave market pricing (use town market APIs directly)

---

### B1071_GovernanceBehavior

**Location:** `Byzantium1071.Campaign.Behaviors`  
**Access:** `B1071_GovernanceBehavior.Instance`

**Public read-only methods:**
- None currently exposed; limited submod use case

**Intended use:** Observe governance-related campaign events; do not depend on current state access

---

### B1071_ClanSurvivalBehavior

**Location:** `Byzantium1071.Campaign.Behaviors`  
**Access:** `B1071_ClanSurvivalBehavior.Instance`

**Public read-only methods:**
- `IsRebelClanOrigin(Clan clan)` → `bool` — check if a clan is (or originated from) a rebellion

**Intended use:** Identify rebel clans for custom event handling or diplomatic logic

**Off-limits:**
- `ScanAndRescueHomelessRebelClans`, `NormalizeRebelClan` (lifecycle methods)

---

### B1071_VillageInvestmentBehavior, B1071_TownInvestmentBehavior

**Location:** `Byzantium1071.Campaign.Behaviors`  
**Access:** `B1071_VillageInvestmentBehavior.Instance` and `B1071_TownInvestmentBehavior.Instance`

**Public read-only methods:**
- `GetActiveHearthBonus(Village village)` → `float` (village) or `GetActiveProsperityBonus(Town town)` → `float` (town) — retrieve the current per-day bonus from all active investments

**Intended use:** Add investment display/tooltip info, query growth bonuses for tooltips

**Off-limits:**
- `_investDaysRemaining`, `_investHearthBonus` dicts
- `ApplyInvestment`, `OnSettlementEntered` (lifecycle)

---

## Settings Access

### B1071_McmSettings

**Location:** `Byzantium1071.Campaign.Settings`  
**Access:** `B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults`

All mod settings are public properties on this singleton. **Read directly; never mutate.**

```csharp
var settings = B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;
bool castleRecruitmentEnabled = settings.EnableCastleRecruitment;
int manpowerMultiplier = settings.ManpowerPoolMultiplier;
```

**Safe operations:**
- Query any public property
- Check `SettingsProfileVersion` to detect latest applied migration

**Unsafe operations:**
- Direct property assignment (use MCM UI for persistence)
- Assumption that property values match save state (they may lag after MCM open)

**Note on migration:** Settings are versioned via `LATEST_PROFILE_VERSION`. Existing player profiles auto-migrate on first load; your submod code should assume current settings are **at least** the latest version.

**Retired properties.** Property names are never removed (see the stability guarantees below), but a property can stop being read. Retired properties stay declared so existing MCM configs still deserialize, and they appear in the **Legacy** settings group with a `[LEGACY — NOT USED]` hint. Do not branch on them.

| Property | Retired in | Read this instead |
|---|---|---|
| `TiersPerExtraCost` | 1.0.1.x | flat `BaseManpowerCostPerTroop` |
| `CostMultiplierPercent` | 1.0.1.x | flat `BaseManpowerCostPerTroop` |
| `EnableTierSurvivability` | 1.0.2.5 | `EliteSurvivabilityPreset` (0–3) |
| `EnableTierArmorSimulation` | 1.0.2.5 | `EliteSurvivabilityPreset` (0–3) |

`EliteSurvivabilityPreset` drives both autoresolve tier systems at once — damage reduction and the wound-vs-kill bonus. `0` disables both; `1` (default) through `3` scale them together. The curve itself lives in `B1071_CombatRealismTuning`, which is `internal` — read the preset, not the tables.

---

## Campaign Events

Subscribe to Bannerlord's standard `CampaignEvents` and Campaign++'s behaviors to react to gameplay changes. Campaign++ does **not** expose custom events; it only subscribes to vanilla campaign events internally.

**Safe to subscribe:**
```csharp
CampaignEvents.DailyTickEvent.AddNonSerializedListener(myBehavior, OnDailyTick);
CampaignEvents.OnSettlementOwnerChanged.AddNonSerializedListener(myBehavior, OnOwnerChanged);
```

**Campaign++ internal event subscriptions (for reference):**
- `OnSessionLaunchedEvent` → behavior initialization
- `DailyTickEvent`, `DailyTickSettlementEvent` → system updates
- `OnPrisonerDonatedToSettlementEvent` → castle recruitment tracking
- `SettlementEntered` → investment AI, compatibility checks
- All events registered as `NonSerializedListener` (do not persist across saves)

---

## Harmony Patching

Campaign++ uses Harmony to patch vanilla Bannerlord classes. If your submod also patches the same targets, follow these rules:

### Safe Patch Targets (Public Types)

- `TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementGarrisonModel` (public class, stable interface)
- `TaleWorlds.CampaignSystem.GameComponents.DefaultVolunteerModel` (public, used by both Campaign++ and EconomyOverhaul)
- `TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitmentVM` (UIExtenderEx hook point)

### Fragile Patch Targets (Private/internal)

Campaign++ patches these private methods. Document your dependency version:
- `RecruitmentCampaignBehavior.ApplyInternal` (v1.4.8 verified, no nameof)
- `RecruitmentVM.OnDone` / `RefreshScreen` / `RefreshPartyProperties` (v1.4.8 verified)
- `DefaultClanFinanceModel.CalculateClanIncomeInternal` (v1.4.8 verified)
- `PartiesSellPrisonerCampaignBehavior.OnSettlementEntered` / `DailyTickSettlement` (v1.4.8 verified)
- `InfluenceGainCampaignBehavior.OnPrisonerDonatedToSettlement` (v1.4.8 verified)
- Several private DefaultSettlementGarrisonModel overloads

**If you patch private methods, pin to the Bannerlord version via `Campaign++/SubModule.xml` compatibility tag and re-test after each game update.** Note that Harmony binds prefix/postfix parameters by **name**, so a game update that renames a parameter will compile cleanly and then throw at patch time — verify parameter names, not just signatures.

### Warsails (NavalDLC) and model decorators

Warsails replaces ten campaign models Campaign++ hooks (prosperity, security, garrison, militia, party wage, settlement access, combat simulation, party healing, clan finance, building construction). Each replacement is a thin decorator that forwards through `((MBGameModel<T>)this).BaseModel.Method(...)`, so patches attached to the vanilla `Default*` type still execute.

If your submod patches any of these models, patch the `Default*` type rather than the `NavalDLC*` type — that keeps the submod working whether or not Warsails is installed. Campaign++ references no Warsails assemblies and does not require the DLC.

### Best Practice: Postfix Over Prefix

```csharp
// ✓ GOOD: Postfix allows vanilla + other mods to run first
[HarmonyPatch(typeof(DefaultSettlementGarrisonModel), nameof(DefaultSettlementGarrisonModel.GetMaximumDailyAutoRecruitmentCount))]
public static class MyGarrisonPatch
{
    static void Postfix(ref int __result)
    {
        __result = Math.Min(__result, myCustomLimit);  // further restrict, never re-enable
    }
}

// ✗ AVOID: Prefix blocks vanilla entirely  
[HarmonyPatch(typeof(DefaultVolunteerModel), nameof(DefaultVolunteerModel.GetDailyVolunteerProductionBase))]
public static class MyVolunteerPatch
{
    static bool Prefix()
    {
        return false;  // blocks vanilla
    }
}
```

### Harmony ID

Use a unique, namespaced Harmony ID:
```csharp
var harmony = new Harmony("com.yourname.yourmod");
```

---

## Models (Read-Only Calculations)

Campaign++ does not expose custom models. All calculations are embedded in behavior daily-tick or recruitment-path logic.

**If you need to override settlement/party calculations:**
1. Use `AddModel` in your behavior's `OnGameStart`
2. Inject your model into the campaign's model collection
3. Test parity with Campaign++'s behavior

**Example: Custom manpower model**
```csharp
public class MyManpowerModel : DefaultSettlementGarrisonModel
{
    public override int GetDailyWageAmount(Settlement settlement) => /* custom */;
}

private void OnGameStart(CampaignGameStarter starter)
{
    starter.AddModel(new MyManpowerModel());  // added last, has highest priority
}
```

---

## Persistence (Save/Load Safety)

Campaign++ behaviors use `SyncData` to persist state. **Do not directly access serialization dicts.**

**Safe pattern for your submod:**
```csharp
public override void SyncData(IDataStore dataStore)
{
    // Your own dicts
    dataStore.SyncData("my_setting_key", ref myDict);
}
```

**Unsafe pattern:**
```csharp
// DON'T do this — internal state may reorganize
var campaignState = B1071_ManpowerBehavior.Instance._manpowerPools;
```

### Late-Install Safety

Campaign++ behaviors initialize from empty dicts if a key is missing (graceful degradation). Your submod should do the same:

```csharp
pool ??= new Dictionary<string, int>();  // null-coalesce on load
```

---

## Compatibility Checklist

Before releasing a submod, verify:

- [ ] **Behavior instances checked as null** — handle the case where Campaign++ is not loaded
- [ ] **Settings read via public properties** — no attempt to mutate MCM values
- [ ] **Harmony patches have unique IDs** — coordinate with other mod authors
- [ ] **Private method patches pinned to Bannerlord version** — document in your readme
- [ ] **SyncData uses own keys** — don't pollute Campaign++'s namespaces
- [ ] **Gold transfers use `GiveGoldAction`** — never direct assignment
- [ ] **Settlement/faction checks null-safe** — settlements destroyed, clans eliminated mid-game
- [ ] **Campaign events use `AddNonSerializedListener`** — not persisted across saves
- [ ] **Tested Save/Load/Save cycle** — lag or crashes indicate persistence issues

---

## Example: Population Growth Submod

```csharp
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace MyPopulationMod.Campaign
{
    public class PopulationGrowthBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailySettlement);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Persist our own population multiplier if configurable
        }

        private void OnDailySettlement(Settlement settlement)
        {
            if (settlement?.Village == null) return;

            // Query Campaign++'s manpower to avoid starvation
            var mpBehavior = B1071_ManpowerBehavior.Instance;
            if (mpBehavior == null) return;

            mpBehavior.GetManpowerPool(settlement, out int curManpower, out _, out _);

            // Only boost hearth growth if manpower is healthy
            var settings = B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;
            if (curManpower > settings.ManpowerPoolMultiplier * 50)
            {
                settlement.Village.Hearth += 0.5f;  // custom growth bonus
            }
        }
    }
}
```

---

## Support & Breaking Changes

**Version compatibility:**
- **v0.2.x** — current stable API surface (this document)
- **v0.3.x+** — may introduce breaking changes; check CHANGELOG.md

**Report issues:**
- Nexus Mods comments
- GitHub issues (if reachable)

**API stability guarantees:**
- Public behavior `Instance` accessors will not be removed (may be renamed on major version)
- `B1071_McmSettings` property names will not change (only new properties added)
- Public method signatures will not break (may add optional parameters)

---

## Forbidden Patterns

❌ **Do not:**
- Patch Campaign++ private methods without version pinning
- Mutate behavior state dicts directly
- Call private/internal methods (use public queries instead)
- Create gold from nothing or destroy it silently
- Persist references to Harmony-patched classes (violates MCM model)
- Assume save state matches MCM Instance before save point
- Intercept vanilla campaign events that Campaign++ listens to without re-throwing

✓ **Do:**
- Use `TryGetValue` with fallback defaults
- Null-check behavior instances
- Subscribe to vanilla events; let Campaign++ coexist
- Test on existing campaigns (mid-install safety)
- Document which Bannerlord version your Harmony patches target

---

**Last updated:** v1.0.2.7 (August 2026)
