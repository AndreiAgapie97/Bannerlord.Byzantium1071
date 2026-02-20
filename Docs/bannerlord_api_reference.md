# Bannerlord 1.3.14 — Decompiled API Reference

> Generated via PowerShell reflection against DLLs in:
> `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\`
>
> Date: 2026-02-20

---

## Table of Contents

1. [Campaign.Current](#campaigncurrent)
2. [GetCampaignBehavior\<T\>()](#getcampaignbehaviort)
3. [Settlement](#settlement)
4. [Settlement.Village (Field)](#settlementvillage-field)
5. [Village](#village)
6. [Kingdom](#kingdom)
7. [MBObjectBase.StringId](#mbobjectbasestringid)
8. [MBRandom](#mbrandom)
9. [MapEventParty](#mapeventparty)
10. [MBBindingList\<T\>](#mbbindinglistt)
11. [RecruitmentVM Collections](#recruitmentvm-collections)
12. [CampaignTime](#campaigntime)

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
| `FactionsAtWarWith` | `IReadOnlyList<IFaction>` | Current enemies. |

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

## General Safety Rules (Derived from Audit)

1. **Always null-check `Campaign.Current`** before accessing any campaign subsystem.
2. **Always null-check return value of `GetCampaignBehavior<T>()`**.
3. **Use `TryGetValue` over direct dict indexer** for any dictionary keyed by `StringId`.
4. **Guard `StringId` with `string.IsNullOrEmpty()`** before using as dictionary key.
5. **Clamp variance percentages** to [0, 100] before computing spread for `MBRandom.RandomFloatRanged`.
6. **Guard NaN/Infinity** on any float that passes through division or ratio computation before display.
7. **Null-check VM collections** (`VolunteerList`, `TroopsInCart`, `Troops`, `DoneHint`) before iterating or accessing members.
8. **`Settlement.Village` is a field**, not a property — use `GetField()` in reflection, not `GetProperty()`.
