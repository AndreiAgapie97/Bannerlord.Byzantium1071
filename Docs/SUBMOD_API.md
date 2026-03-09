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

**Intended use:** Query castle recruitment state for overlay/reporting, prison processing logic, or allied settlement analysis

**Off-limits:**
- Prison roster mutation (use Bannerlord's prisoner actions)
- `_elitePool`, `_prisonerDaysHeld`, `_depositorTracking` dicts (internal state)
- `AutoEnslaveLowTierPrisoners`, `RegenerateElitePool` (called from daily tick)

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
- `RecruitmentCampaignBehavior.ApplyInternal` (v1.3.15 specific, no nameof)
- `RecruitmentVM.RefreshScreen` / `RefreshPartyProperties` (v1.3.15 specific)
- Several private DefaultSettlementGarrisonModel overloads

**If you patch private methods, pin to the Bannerlord version via `Campaign++/SubModule.xml` compatibility tag and re-test after each game update.**

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

**Last updated:** v0.2.7.2 (March 2026)
