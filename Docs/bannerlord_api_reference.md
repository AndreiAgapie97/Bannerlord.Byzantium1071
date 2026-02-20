# Bannerlord 1.3.14 — Decompiled API Reference

> Generated via PowerShell reflection against DLLs in:
> `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\`
>
> Date: 2026-02-20

---

## Table of Contents

1. [Campaign.Current](#campaigncurrent)
2. [Campaign.Models](#campaignmodels)
3. [GetCampaignBehavior\<T\>()](#getcampaignbehaviort)
4. [Settlement](#settlement)
5. [Settlement.Village (Field)](#settlementvillage-field)
6. [Village](#village)
7. [Kingdom](#kingdom)
8. [MBObjectBase.StringId](#mbobjectbasestringid)
9. [MBRandom](#mbrandom)
10. [MapEventParty](#mapeventparty)
11. [MBBindingList\<T\>](#mbbindinglistt)
12. [RecruitmentVM Collections](#recruitmentvm-collections)
13. [CampaignTime](#campaigntime)
14. [MakePeaceAction](#makepeaceaction)
15. [MBSubModuleBase Lifecycle](#mbsubmodulebase-lifecycle)
16. [TroopRoster](#trooproster)
17. [Clan](#clan)
18. [DiplomacyModel](#diplomacymodel)
19. [InformationMessage](#informationmessage)
20. [Colors](#colors)
21. [GameModels (vNext Planning Surfaces)](#gamemodels-vnext-planning-surfaces)
22. [CampaignEvents (vNext Planning Hooks)](#campaignevents-vnext-planning-hooks)
23. [Town (vNext State Surfaces)](#town-vnext-state-surfaces)
24. [Hero (vNext State Surfaces)](#hero-vnext-state-surfaces)
25. [vNext Work Package API Mapping](#vnext-work-package-api-mapping)
26. [All-Kingdom Iteration and Normalization](#all-kingdom-iteration-and-normalization)
27. [Deep DLL Sweep Findings](#deep-dll-sweep-findings)

---

## Campaign.Current

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Campaign`

```csharp
// Static property — CAN return null
public static Campaign? Current { get; }
```

**Key facts:**
- Returns `null` before campaign is fully initialized (e.g. main menu, loading screen).
- Returns `null` after campaign teardown.
- **Always null-check before use.**

---

## Campaign.Models

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Campaign`

```csharp
public GameModels Models { get; }
```

**Return type:** `TaleWorlds.CampaignSystem.GameModels`

**Key facts:**
- Returns the `GameModels` container holding all registered game models (e.g. `DiplomacyModel`, `VolunteerModel`, etc.).
- Can be `null` if `Campaign.Current` is null or campaign is not fully initialized.
- **Always access via null-safe chain:** `Campaign.Current?.Models?.DiplomacyModel`.
- The `GameModels` type provides typed properties for each model category.

---

## GetCampaignBehavior\<T\>()

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Campaign`

```csharp
public T GetCampaignBehavior<T>() where T : CampaignBehaviorBase
```

**Key facts:**
- Returns `default(T)` = `null` when the behavior type is not registered.
- Safe: does not throw. But the return value **must be null-checked**.
- Registration happens during `OnSessionLaunched` via `CampaignEvents`.

---

## Settlement

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Settlements.Settlement`
**Inherits:** `MBObjectBase`

### Key Properties

| Name | Type | Notes |
|------|------|-------|
| `StringId` | `string` | Inherited from `MBObjectBase`. **CAN be null** (see below). |
| `Name` | `TextObject` | Display name. Can be null for destroyed/corrupt settlements. |
| `IsVillage` | `bool` | Property. |
| `IsTown` | `bool` | Property. |
| `IsCastle` | `bool` | Property. |
| `IsHideout` | `bool` | Property. |
| `Town` | `Town?` | Property. Null for villages/hideouts. |
| `SettlementComponent` | `SettlementComponent` | Property. Base component. |
| `BoundVillages` | `MBReadOnlyList<Village>` | Property. Villages bound to this town/castle. |
| `OwnerClan` | `Clan?` | Property. |
| `Village` | `Village` | **PUBLIC FIELD** (not a property!). See below. |

### Key Methods

| Name | Signature | Notes |
|------|-----------|-------|
| `CurrentSettlement` | `static Settlement? CurrentSettlement` | Null when not in a settlement menu. |

---

## Settlement.Village (Field)

**IMPORTANT: This is a PUBLIC FIELD, not a property.**

```
Field: Village
Type: TaleWorlds.CampaignSystem.Settlements.Village
IsPublic: True
IsPrivate: False
```

- **Not discoverable via `GetProperty()` reflection** — must use `GetField()`.
- Null for non-village settlements.
- For village settlements, contains the `Village` component with `Bound`, `Hearth`, etc.
- Access pattern: `settlement.Village?.Bound` is safe via null-conditional on the field value.

---

## Village

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Settlements.Village`

### Key Properties

| Name | Type | Notes |
|------|------|-------|
| `Bound` | `Settlement` | The parent town/castle this village is bound to. |
| `Hearth` | `float` | Village hearth value (population proxy). |
| `VillageState` | `Village.VillageStates` | Current state (Normal, BeingRaided, Looted, etc.). |

---

## Kingdom

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Kingdom`
**Inherits:** `MBObjectBase`

### Key Properties

| Name | Type | Notes |
|------|------|-------|
| `StringId` | `string` | Inherited from `MBObjectBase`. **CAN be null.** |
| `Name` | `TextObject` | Kingdom display name. |
| `IsEliminated` | `bool` | True if kingdom has been destroyed. |
| `Clans` | `MBReadOnlyList<Clan>` | Member clans. |
| `FactionsAtWarWith` | `MBReadOnlyList<IFaction>` | Current enemies. **Can be null** for eliminated kingdoms. |

### Key Static Members

| Name | Type | Notes |
|------|------|-------|
| `All` | `MBReadOnlyList<Kingdom>` | All kingdoms including eliminated. **Filter with IsEliminated.** |

---

## MBObjectBase.StringId

**Assembly:** `TaleWorlds.Core.dll`
**Type:** `TaleWorlds.ObjectSystem.MBObjectBase`

```csharp
public string StringId { get; set; }
```

**Key facts:**
- **CAN be null.** Not guaranteed to be populated for all objects at all times.
- Used as dictionary keys throughout the mod — always guard with `string.IsNullOrEmpty()` before using as a key.
- Inherited by `Settlement`, `Kingdom`, `Clan`, `CharacterObject`, etc.

---

## MBRandom

**Assembly:** `TaleWorlds.Core.dll`
**Type:** `TaleWorlds.Core.MBRandom`

### RandomFloatRanged

```csharp
// Decompiled implementation:
public static float RandomFloatRanged(float minValue, float maxValue)
{
    return minValue + RandomFloat * (maxValue - minValue);
}
```

**Key facts:**
- `RandomFloat` is a property returning `[0, 1)`.
- If `minValue > maxValue` (inverted range): **does NOT throw**. Returns values in `[maxValue, minValue)` — silently produces unexpected results.
- **Always ensure `min ≤ max`** before calling.
- Deterministic within Bannerlord's RNG seed context.

### RandomFloat

```csharp
public static float RandomFloat { get; } // [0, 1)
```

### RandomInt

```csharp
public static int RandomInt(int maxValue); // [0, maxValue)
```

---

## MapEventParty

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.MapEvents.MapEventParty`

**Key facts:**
- **Is a CLASS** (reference type), not a struct.
- Entries in `MapEvent.AttackerSide.Parties` / `MapEvent.DefenderSide.Parties` could theoretically be null.
- Always null-check individual entries when iterating.

### Key Properties

| Name | Type | Notes |
|------|------|-------|
| `Party` | `PartyBase` | The party involved. Can be null for disbanded parties. |
| `DiedInBattle` | `TroopRoster` | Roster of troops killed. **Can theoretically be null.** |
| `WoundedInBattle` | `TroopRoster` | Roster of troops wounded. **Can theoretically be null.** |

**Safe access pattern:**
```csharp
int dead = mep?.DiedInBattle?.TotalManCount ?? 0;
int wounded = mep?.WoundedInBattle?.TotalManCount ?? 0;
```

---

## MBBindingList\<T\>

**Assembly:** `TaleWorlds.Library.dll`
**Type:** `TaleWorlds.Library.MBBindingList<T>`

**Key facts:**
- Bannerlord's observable collection for UI data binding.
- **NOT thread-safe.** Do not modify from background threads.
- **No null guards on items.** Individual elements can be null.
- Used by `RecruitmentVM.VolunteerList`, `TroopsInCart`, etc.
- The collection itself (the property returning it) can also be null if the VM hasn't been initialized.

---

## RecruitmentVM Collections

**Assembly:** `TaleWorlds.CampaignSystem.ViewModelCollection.dll`
**Type:** `TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitmentVM`

| Property | Type | Can be null? |
|----------|------|-------------|
| `VolunteerList` | `MBBindingList<RecruitVolunteerVM>` | **Yes** — before VM init. |
| `TroopsInCart` | `MBBindingList<RecruitVolunteerTroopVM>` | **Yes** — before VM init. |
| `DoneHint` | `HintViewModel` | **Yes** — before VM init. |
| `CanRecruitAll` | `bool` | Safe. |
| `IsDoneEnabled` | `bool` | Safe. |

### RecruitVolunteerVM

| Property | Type | Can be null? |
|----------|------|-------------|
| `Troops` | `MBBindingList<RecruitVolunteerTroopVM>` | **Yes.** |

### RecruitVolunteerTroopVM

| Property | Type | Can be null? |
|----------|------|-------------|
| `Character` | `CharacterObject` | **Yes** — for empty slots. |
| `IsTroopEmpty` | `bool` | Safe. |
| `IsInCart` | `bool` | Safe. |
| `PlayerHasEnoughRelation` | `bool` | Safe. |
| `CanBeRecruited` | `bool` | Safe (settable). |
| `Cost` | `int` | Safe — recruitment gold cost. |
| `Level` | `string` | **Yes** — tier display string. |
| `NameText` | `string` | **Yes** — display name. |
| `TierIconData` | `string` | **Yes** — tier icon. |
| `TypeIconData` | `string` | **Yes** — type icon (infantry/cavalry). |

**Methods:**
- `ExecuteRecruit()` — Triggers recruitment action.
- `ExecuteBeginHint()` / `ExecuteEndHint()` — Tooltip hover lifecycle.
- `RefreshValues()` — Re-reads source data.

> **Limitation:** No `Hint` or `Tooltip` property exists on this VM. Per-troop manpower tooltips cannot be added without custom Gauntlet XML widget extensions.

---

## InformationMessage

**Assembly:** `TaleWorlds.Library.dll`
**Type:** `TaleWorlds.Library.InformationMessage`

**Constructors:**

| Signature | Notes |
|-----------|-------|
| `InformationMessage()` | Default — empty message. |
| `InformationMessage(string information)` | White text. |
| `InformationMessage(string information, Color color)` | Colored text — use `Colors.Yellow`, `Colors.Red`, etc. |
| `InformationMessage(string information, string soundEventPath)` | With SFX. |
| `InformationMessage(string information, Color color, string soundEventPath)` | Colored + SFX. |

> **Usage:** `InformationManager.DisplayMessage(new InformationMessage("text", Colors.Yellow));`

---

## Colors

**Assembly:** `TaleWorlds.Library.dll`
**Type:** `TaleWorlds.Library.Colors`

Static readonly `Color` properties commonly used:

| Property | Notes |
|----------|-------|
| `Colors.Red` | Error / block messages. |
| `Colors.Yellow` | Warning / manpower gate messages. |
| `Colors.Green` | Success / confirmation. |
| `Colors.White` | Default text. |
| `Colors.Cyan` | Information highlight. |
| `Colors.Magenta` | Diagnostic/debug. |

> Returns `TaleWorlds.Library.Color` struct with RGBA float fields.

---

## CampaignTime

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.CampaignTime`

**Key facts:**
- Value type (struct).
- Members like `GetSeasonOfYear`, `GetDayOfSeason`, `GetYear`, `GetHourOfDay` are **methods** (not properties).
- Must be accessed via reflection if not part of public API surface.
- `CampaignTime.Now` returns current campaign time.
- `CampaignTime.Days(float)` creates a CampaignTime from absolute day count.
- `.ToDays` property returns `double` absolute day value.

---

## MakePeaceAction

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Actions.MakePeaceAction`

### ApplyByKingdomDecision

```csharp
public static void ApplyByKingdomDecision(
    IFaction faction1,
    IFaction faction2,
    int dailyTributeFrom1To2,
    int dailyTributeDuration)
```

**Key facts:**
- **Can throw** if factions are already at peace, eliminated, or in an invalid state mid-tick.
- Parameters 3–4 control tribute terms; pass `0, 0` for unconditional peace.
- **Always wrap in try-catch** when called in a loop (e.g., daily diplomacy tick) so one failure doesn't abort remaining iterations.

**Safe pattern:**
```csharp
try
{
    MakePeaceAction.ApplyByKingdomDecision(faction1, faction2, 0, 0);
}
catch (Exception ex)
{
    // Log and continue
}
```

---

## MBSubModuleBase Lifecycle

**Assembly:** `TaleWorlds.MountAndBlade.dll`
**Type:** `TaleWorlds.MountAndBlade.MBSubModuleBase`

### Key Virtual Methods (Override Order)

| Method | When Called | Notes |
|--------|-----------|-------|
| `OnSubModuleLoad()` | Module DLL loads | `protected override`. Init Harmony, UIExtender here. |
| `OnBeforeInitialModuleScreenSetAsRoot()` | Before main menu appears | `protected override`. One-time UI setup. |
| `OnGameStart(Game, IGameStarter)` | Campaign/game session begins | `protected override`. Register behaviors/models here. |
| `OnGameEnd(Game)` | Campaign ends (return to menu) | `public override`. **Module stays loaded.** Clean up stale singletons here. |
| `OnSubModuleUnloaded()` | Module DLL unloads (game exit) | `protected override`. Final cleanup, unpatch Harmony. |
| `OnApplicationTick(float dt)` | Every frame | `protected override`. Use for overlay ticks. |

**Key facts:**
- `OnGameEnd(Game game)` is `public virtual` (not protected). Fires when player exits campaign to main menu.
- The module instance persists between campaigns — **stale static references** from a previous campaign will cause crashes if not nulled in `OnGameEnd`.
- `OnSubModuleUnloaded` only fires on full game exit, not between campaigns.

---

## TroopRoster

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Roster.TroopRoster`

### Key Properties

| Name | Type | Notes |
|------|------|-------|
| `TotalManCount` | `int` | Total number of troops in the roster. |
| `TotalRegulars` | `int` | Non-hero troop count. |
| `TotalHeroes` | `int` | Hero count. |
| `Count` | `int` | Number of distinct troop types (roster element count). |

**Key facts:**
- Used by `MapEventParty.DiedInBattle` and `MapEventParty.WoundedInBattle` to track battle casualties.
- The roster instance itself **can theoretically be null** — always null-check before accessing `TotalManCount`.
- Iterating elements: `foreach (TroopRosterElement el in roster.GetTroopRoster())`.

---

## Clan

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Clan`
**Inherits:** `MBObjectBase`

### Key Properties

| Name | Type | Notes |
|------|------|-------|
| `WarPartyComponents` | `MBReadOnlyList<WarPartyComponent>` | Active war parties. **Can theoretically be null** during dissolution. |
| `Kingdom` | `Kingdom?` | The kingdom this clan belongs to. Null for clanless. |
| `StringId` | `string` | Inherited. **CAN be null.** |

**Safe access pattern for war parties:**
```csharp
if (clan.WarPartyComponents == null) continue;
foreach (var component in clan.WarPartyComponents) { ... }
```

---

## DiplomacyModel

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.GameComponents.DiplomacyModel`
**Access:** `Campaign.Current.Models.DiplomacyModel`

### Key Methods

| Name | Signature | Notes |
|------|-----------|-------|
| `GetScoreOfDeclaringWar` | `(IFaction, IFaction, IFaction, out TextObject) → float` | AI war scoring. |
| `GetScoreOfDeclaringPeace` | `(IFaction, IFaction, IFaction, out TextObject) → float` | AI peace scoring. |

**Key facts:**
- Accessed via `Campaign.Current.Models.DiplomacyModel`.
- The entire chain can be null: `Campaign.Current` → `Models` → `DiplomacyModel`.
- **Always use null-safe chain:** `Campaign.Current?.Models?.DiplomacyModel`.
- Calling methods on a null model will throw `NullReferenceException`.

---

## General Safety Rules (Derived from Audit)

1. **Always null-check `Campaign.Current`** before accessing any campaign subsystem.
2. **Always null-check return value of `GetCampaignBehavior<T>()`**.
3. **Use `TryGetValue` over direct dict indexer** for any dictionary keyed by `StringId`.
4. **Guard `StringId` with `string.IsNullOrEmpty()`** before using as dictionary key.
5. **Clamp variance percentages** to [0, 100] before computing spread for `MBRandom.RandomFloatRanged`.
6. **Guard NaN/Infinity** on any float that passes through division or ratio computation before display.
7. **Null-check VM collections** (`VolunteerList`, `TroopsInCart`, `Troops`, `DoneHint`) before iterating or accessing members.
8. **`Settlement.Village` is a field**, not a property — use `GetField()` in reflection, not `GetProperty()`.
9. **Null-safe chain for `Campaign.Current?.Models?.DiplomacyModel`** — all three can independently be null.
10. **Wrap `MakePeaceAction` in try-catch** when called in loops — one failure must not abort remaining iterations.
11. **Null-check `FactionsAtWarWith`** before `.Count` — can be null for eliminated kingdoms.
12. **Null-check `MapEventParty` roster properties** (`DiedInBattle`, `WoundedInBattle`) — use `?.TotalManCount ?? 0`.
13. **Clean up statics in `OnGameEnd`**, not just `OnSubModuleUnloaded` — module persists between campaigns.
14. **Cache reflection results** (`PropertyInfo`, `FieldInfo`) in static fields — reflection per-call is a hot-path perf issue.
15. **Use `InformationMessage(string, Color)` for player-facing warnings** — colored messages improve visibility. Gate messages use `Colors.Yellow`.
16. **`RecruitVolunteerTroopVM` has no tooltip property** — per-troop hints require Gauntlet widget XML extensions, which is outside mod scope.

---

## GameModels (vNext Planning Surfaces)

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.GameModels`
**Access:** `Campaign.Current?.Models`

### Relevant Model Properties (confirmed)

| Property | Type | vNext utility |
|----------|------|---------------|
| `SettlementFoodModel` | `SettlementFoodModel` | Food-stock pressure and recovery pacing. |
| `SettlementProsperityModel` | `SettlementProsperityModel` | Prosperity/hearth degradation and recovery curves. |
| `SettlementSecurityModel` | `SettlementSecurityModel` | Governance/security stress modeling. |
| `SettlementLoyaltyModel` | `SettlementLoyaltyModel` | Stability and post-conquest friction effects. |
| `SettlementMilitiaModel` | `SettlementMilitiaModel` | Local defense resilience/rebuild. |
| `VillageProductionCalculatorModel` | `VillageProductionCalculatorModel` | Village food-output disruption effects. |
| `MobilePartyFoodConsumptionModel` | `MobilePartyFoodConsumptionModel` | Campaign endurance and supply pressure. |
| `PartyMoraleModel` | `PartyMoraleModel` | Starvation/arrears-style cohesion effects. |
| `RaidModel` | `RaidModel` | Raiding cadence and impact intensity tuning. |
| `DiplomacyModel` | `DiplomacyModel` | War/peace sustainability scoring. |
| `MilitaryPowerModel` | `MilitaryPowerModel` | Strategic overextension pressure checks. |
| `NotablePowerModel` | `NotablePowerModel` | Local elite influence drift under crisis. |
| `IssueModel` | `IssueModel` | Crisis-linked issue pressure injection. |

### Method Surfaces (confirmed)

| Type | Method | Notes |
|------|--------|-------|
| `SettlementFoodModel` | `CalculateTownFoodStocksChange(...)` | Town food delta driver. |
| `SettlementProsperityModel` | `CalculateProsperityChange(...)` | Prosperity drift under stress/recovery. |
| `SettlementProsperityModel` | `CalculateHearthChange(...)` | Village hearth change coupling. |
| `SettlementSecurityModel` | `CalculateSecurityChange(...)` | Security pressure accumulation. |
| `SettlementLoyaltyModel` | `CalculateLoyaltyChange(...)` | Governance fracture proxy support. |
| `VillageProductionCalculatorModel` | `CalculateDailyFoodProductionAmount(...)` | Village production penalties. |
| `PartyMoraleModel` | `GetDailyStarvationMoralePenalty(...)` | Starvation-driven cohesion penalty. |
| `MobilePartyFoodConsumptionModel` | `CalculateDailyFoodConsumptionf(...)` | Party supply burden baseline. |
| `DiplomacyModel` | `GetScoreOfDeclaringPeace(...)` / `GetScoreOfDeclaringWar(...)` | Strategic fatigue/continuation pressure. |

**Key facts:**
- This model set supports interconnected crisis simulation rather than isolated manpower scalars.
- Preserve null-safe access for the full chain: `Campaign.Current?.Models?.<ModelProperty>`.

---

## CampaignEvents (vNext Planning Hooks)

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.CampaignEvents`

### Relevant Events (confirmed names)

| Category | Events |
|----------|--------|
| Daily lifecycle | `DailyTickSettlementEvent`, `DailyTickPartyEvent`, `DailyTickClanEvent` |
| Raid lifecycle | `RaidCompletedEvent`, `VillageBeingRaided`, `VillageLooted`, `VillageBecomeNormal` |
| Siege lifecycle | `OnSiegeAftermathAppliedEvent`, `AfterSiegeCompletedEvent`, `SiegeCompletedEvent` |
| Diplomacy lifecycle | `WarDeclared`, `MakePeace`, `KingdomDecisionAdded`, `KingdomDecisionConcluded` |
| Ownership/state changes | `OnSettlementOwnerChangedEvent` |
| Provisioning stress | `OnPartyConsumedFoodEvent`, `OnMainPartyStarvingEvent` |
| Recruitment signals | `OnTroopRecruitedEvent`, `OnUnitRecruitedEvent` |

**Key facts:**
- Event-driven updates are preferred for low-risk incremental realism systems.
- Register listeners in campaign lifecycle-safe locations and guard against stale static state.

---

## Town (vNext State Surfaces)

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Settlements.Town`

### Relevant Properties (confirmed)

| Name | Type | vNext utility |
|------|------|---------------|
| `FoodStocks` | `int` | Settlement provisioning pressure baseline. |
| `FoodChange` | `ExplainedNumber` | Daily food drift diagnostics. |
| `Prosperity` | `float` | Economic health and recruitment base proxy. |
| `ProsperityChange` | `ExplainedNumber` | Recovery/degradation pacing. |
| `Security` | `float` | Governance/control proxy. |
| `SecurityChange` | `ExplainedNumber` | Instability trend tracking. |
| `Loyalty` | `float` | Post-conquest/governance friction driver. |
| `LoyaltyChange` | `ExplainedNumber` | Recovery-speed limiter input. |
| `Militia` | `float` | Local defense resilience. |
| `MilitiaChange` | `ExplainedNumber` | Reconstitution pace. |

---

## Hero (vNext State Surfaces)

**Assembly:** `TaleWorlds.CampaignSystem.dll`
**Type:** `TaleWorlds.CampaignSystem.Hero`

### Relevant Properties (confirmed)

| Name | Type | vNext utility |
|------|------|---------------|
| `Power` | `float` | Political leverage proxy for crisis pressure logic. |
| `Clan` | `Clan` | Factional alignment anchor. |
| `Occupation` | `Occupation` | Role-based crisis behavior filtering. |
| `GovernorOf` | `Town` | Local governance coupling. |
| `CompanionOf` | `Clan` | Retinue/support network signal. |
| `IsPrisoner` | `bool` | Disruption marker for command continuity. |

---

## vNext Work Package API Mapping

Planning mapping for realism packages defined in `Docs/documentation.md`.

**Scope note:** apply RV1–RV5 across all major active kingdoms using shared formulas and normalization rules.

### RV1 — Provincial Governance Friction

| Target | API ties |
|--------|----------|
| Governance strain index | `SettlementLoyaltyModel`, `SettlementSecurityModel`, `Town.Loyalty`, `Town.Security` |
| Lifecycle inputs | `DailyTickSettlementEvent`, `OnSettlementOwnerChangedEvent` |

### RV2 — Frontier Devastation Ecology 2.0

| Target | API ties |
|--------|----------|
| Cumulative devastation | `VillageProductionCalculatorModel`, `SettlementProsperityModel`, `SettlementFoodModel` |
| Trigger/recovery cadence | `RaidCompletedEvent`, village raid-state events, `DailyTickSettlementEvent` |

### RV3 — Military Cohesion and Contract Dependence

| Target | API ties |
|--------|----------|
| Cohesion pressure | `PartyMoraleModel`, `MobilePartyFoodConsumptionModel` |
| Stress events | `OnMainPartyStarvingEvent`, `OnPartyConsumedFoodEvent`, `DailyTickPartyEvent` |

### RV4 — Crisis Diplomacy Phase II

| Target | API ties |
|--------|----------|
| Crisis-state diplomacy | `DiplomacyModel`, `WarDeclared`, `MakePeace`, `KingdomDecisionConcluded` |

### RV5 — Population Movement and Recruitment Geography

| Target | API ties |
|--------|----------|
| Migration pressure | `SettlementProsperityModel`, `Village.Hearth`, `Town.Prosperity` |
| Temporal progression | `DailyTickSettlementEvent` plus manpower behavior state update loops |

---

## All-Kingdom Iteration and Normalization

**Goal:** keep crisis pressure hard for all major kingdoms while avoiding single-faction over-penalization from map size or one bad war.

### Kingdom Iteration Surface

| Surface | Usage |
|--------|-------|
| `Kingdom.All` | Iterate all kingdoms and filter to active major kingdoms (`!IsEliminated`). |
| `Kingdom.FactionsAtWarWith` | Compute current war-load pressure per kingdom. |
| `Clan.WarPartyComponents` | Estimate active military burden/availability. |
| `Town` + `Village` state values | Aggregate settlement stress and recovery capacity. |

### Recommended Normalized Inputs

| Input | Source examples | Normalization intent |
|------|------------------|----------------------|
| War-load index | enemy count, war age, concurrent fronts | avoid over-weighting large kingdoms by raw war count alone |
| Devastation exposure | raid/siege aftermath frequency, village disruption | compare sustained pressure fairly across kingdom sizes |
| Recovery capacity | food/security/loyalty/prosperity trend aggregates | estimate rebound opportunity instead of static strength |
| Cohesion stress | starvation/morale pressure events | translate supply strain to bounded kingdom pressure |

### Bounded Output Guidance

- clamp per-kingdom pressure to a stable bounded range before diplomacy effects;
- use hysteresis and cooldown windows to reduce war/peace flip-flop;
- include anti-snowball floor so one conflict does not create unrecoverable collapse.

### Fairness Objective

Primary objective is **recovery parity** across all 8 major kingdoms: similar rebound opportunity under similar stress, while preserving distinct campaign outcomes.

---

## Deep DLL Sweep Findings

Extended reflection sweep was completed and stored in:

- `Docs/bannerlord_api_research_deep_2026-02-20.md`

### Coverage summary

- `GameModels` property surface scanned: **123** model properties.
- `CampaignEvents` high-value event accessors scanned (daily/war/peace/raid/siege/recruitment/starvation clusters).
- `TaleWorlds.CampaignSystem.Actions` inventory scanned, including diplomacy/war/kingdom transition actions.
- Core state types scanned (`Kingdom`, `Clan`, `Town`, `Village`, `Hero`, `MobileParty`, `PartyBase`, `Army`, `MapEvent`, `IFaction`).

### Newly confirmed high-impact APIs for all-kingdom pressure systems

#### Faction and stance management

**Type:** `TaleWorlds.CampaignSystem.FactionManager`

- `DeclareWar(IFaction, IFaction)`
- `IsAtWarAgainstFaction(IFaction, IFaction)`
- `IsNeutralWithFaction(IFaction, IFaction)`
- `SetNeutral(IFaction, IFaction)`
- `GetRelationBetweenClans(Clan, Clan)`

`FactionManagerStancesData` also exposes stance-link add/remove/get surfaces for relation-state tracking.

#### War/peace and kingdom transition actions

**Type:** `TaleWorlds.CampaignSystem.Actions.DeclareWarAction`

- `ApplyByDefault(...)`
- `ApplyByKingdomDecision(...)`
- `ApplyByRebellion(...)`
- `ApplyByPlayerHostility(...)`

**Type:** `TaleWorlds.CampaignSystem.Actions.MakePeaceAction`

- `Apply(IFaction, IFaction)`
- `ApplyByKingdomDecision(IFaction, IFaction, int tribute, int duration)`

**Type:** `TaleWorlds.CampaignSystem.Actions.ChangeKingdomAction`

- clan join/leave/defection/mercenary transition apply methods.

**Type:** `TaleWorlds.CampaignSystem.Actions.DestroyKingdomAction`

- destruction pathways via `Apply(...)` variants.

#### Kingdom-level balancing surfaces

**Type:** `TaleWorlds.CampaignSystem.Kingdom`

Additional useful properties beyond earlier baseline:

- `AllParties`
- `Armies`
- `CurrentTotalStrength`
- `Aggressiveness`
- `CallToWarWallet`
- `DistanceToClosestNonAllyFortification`
- `AlliedKingdoms`

These are suitable as normalized inputs for kingdom burden/capacity scoring.

#### Additional model families worth considering in phase-2 tuning

- `ClanFinanceModel`
- `SettlementEconomyModel`
- `PartyWageModel`
- `PartyDesertionModel`
- `DefectionModel`
- `KingdomDecisionPermissionModel`
- `TradeAgreementModel`

These are not required for initial implementation, but they provide leverage for deeper military-fiscal and political-fragmentation realism.