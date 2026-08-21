# Campaign++ — Player Guide

*Version 1.0.2.6 — Everything you need to know, step by step.*

**Game version:** Bannerlord **v1.5.0**. The Warsails (NavalDLC) expansion **v1.3.0** is supported but not required — Campaign++ works with or without it.

**Before you start**, make sure all four required mods are installed and enabled *above* Campaign++ in your load order. Campaign++ will not load if any of them is missing, and a Bannerlord update can sometimes clear them out:

- Harmony (2.4.2 or newer)
- ButterLib (2.10.4 or newer)
- UIExtenderEx (2.13.2 or newer)
- Mod Configuration Menu / MCM (5.11.4 or newer)

---

## Table of Contents

1. [First Launch — What Happens Automatically](#1-first-launch--what-happens-automatically)
2. [The Overlay Ledger (Press M)](#2-the-overlay-ledger-press-m)
3. [Manpower — The Core Resource](#3-manpower--the-core-resource)
4. [Recruiting Volunteers (Villages & Towns)](#4-recruiting-volunteers-villages--towns)
4A. [Troop Service & Demobilization](#4a-troop-service--demobilization)
5. [Castle Recruitment — Elite Troops & Prisoners](#5-castle-recruitment--elite-troops--prisoners)
6. [Depositing Prisoners at Castles](#6-depositing-prisoners-at-castles)
7. [Village Investment (Patronage)](#7-village-investment-patronage)
8. [Town Investment (Civic Patronage)](#8-town-investment-civic-patronage)
9. [The Slave Economy](#9-the-slave-economy)
10. [War Effects & Devastation](#10-war-effects--devastation)
11. [War Exhaustion & Diplomacy](#11-war-exhaustion--diplomacy)
12. [Army Economics](#12-army-economics)
13. [MCM Settings](#13-mcm-settings)
14. [Clan Survival](#14-clan-survival)
15. [Mod Compatibility Report](#15-mod-compatibility-report)
16. [Quick Reference Cheat Sheet](#16-quick-reference-cheat-sheet)

---

## 1. First Launch — What Happens Automatically

Once the mod is installed and you start a campaign, the following happens behind the scenes with **zero input from you**:

- Every town and castle gets a **manpower pool** (towns start at ~1500, castles at ~400, scaled by prosperity).
- Every town market is **seeded with 0–10 slaves** so the slave economy is visible from day one.
- Every castle begins generating an **elite troop pool** from its culture (up to 20 troops).
- **Tier-exponential army costs** kick in — hiring, upgrading, and paying troops costs more at higher tiers.
- **Garrison wages** are discounted to 80% of field rates automatically.
- Non-bandit minor factions receive a "**Frontier Revenue**" stipend so they don't go bankrupt.
- AI lords will automatically deposit prisoners at castles they visit.
- UI/localized strings follow your game language automatically (**English + Simplified Chinese + French + German** included).
- If upgrading from a previous version, a **green notification** confirms that balance settings have been auto-migrated to the latest defaults. Your non-balance settings (pool sizes, toggles, etc.) are preserved.

You don't need to press anything. Just start playing.

---

## 2. The Overlay Ledger (Press M)

### How to Open It

| Action | What Happens |
|--------|--------------|
| **Press M** on the campaign map | Toggles the overlay on/off |

> The hotkey is configurable in MCM: M (default), N, K, F9, F10, F11, or F12.

### The 14 Tabs

Once the overlay is open, you'll see **tab buttons** along the top. Click any tab to switch views:

| Tab | Name | What You See |
|-----|------|--------------|
| 1 | **Current** | Detailed breakdown of the settlement you last clicked on the map — manpower, regen rate, pool capacity, bound villages, governance strain, devastation, war exhaustion |
| 2 | **Nearby** | All settlements sorted by distance from your party — manpower headcount, fill %, distance |
| 3 | **Castles** | Every castle in the world — manpower, prosperity, daily regen |
| 4 | **Towns** | Every town — manpower, prosperity, daily regen |
| 5 | **Villages** | Every village — hearth count, faction, bound settlement |
| 6 | **Factions** | Kingdom-level view — Faction \| Ruler \| Treasury \| Manpower \| Prosperity |
| 7 | **Armies** | Active armies — power rating, troop count, kingdom exhaustion |
| 8 | **Wars** | Active wars — exhaustion levels, peace pressure, territory counts (e.g., "9 vs 15") |
| 9 | **Rebellion** | Settlements at risk of revolt — risk score, loyalty, time-to-rebellion, culture mismatch |
| 10 | **Prisoners** | Captured nobles — who captured them, where they're held |
| 11 | **Clans** | Clan loyalty/instability within kingdoms |
| 12 | **Characters** | All living characters — location, distance, relation symbol (♥ ▲ ● ▼ †) |
| 13 | **Search** | Free-text search across settlements, heroes, clans, and **trade good / food prices** at every town |
| 14 | **Casualties** | Cumulative battlefield deaths by kingdom pair since the feature became active — compare who has bled most across the campaign |

### Overlay Controls

| Control | What It Does |
|---------|--------------|
| **Click a column header** | Sorts the table by that column (click again to reverse) |
| **◀ / ▶ arrows** | Page through rows (default 9 rows per page, adjustable in MCM) |
| **← / → arrow keys** | Cycle between tabs (wraps around; disabled on Search tab to allow text editing) |
| **Hover a truncated cell** | Shows full text in a tooltip |
| **Type in Search tab** | Filters settlements, heroes, clans, and market prices by your search text. Try typing a trade good name (e.g., "Grain", "Slaves") to compare prices across all towns |
| **Left-click a settlement on the map** | Updates the "Current" tab with that settlement's details |

> **Visual cues:** Alternating rows have subtle zebra striping. Rows belonging to your faction are highlighted in gold.

---

## 3. Manpower — The Core Resource

Every town and castle has a **manpower pool**. This is the population available for military service.

### What Consumes Manpower

| Action | Manpower Cost |
|--------|---------------|
| **Recruiting a volunteer** from a village/town | 1 MP per troop (flat, all tiers) |
| **Garrison auto-recruiting** | Same — 1 MP per troop, zero MP = zero garrison growth |
| **Castle elite troop regeneration** | 1 MP per elite troop generated |
| **Village raids (enemy)** | Drains 15% of bound settlement's pool |
| **Sieges** | Sets pool to 10–70% of max depending on aftermath choice |
| **Battles** | Each casualty drains 0.5 MP from their home pool |
| **Settlement conquest** | Retains only 50% of the current pool |

> **Tavern mercenaries are free of manpower.** Hiring mercenaries in a tavern costs no manpower and is never blocked by an empty pool — they are wandering soldiers, not local levies. (Before v1.0.2.4 they were gated, which made the hire silently do nothing once a town's pool ran dry.)

### What Regenerates Manpower

Pools regenerate **daily**, scaled by:
- **Prosperity** (richer = faster regen)
- **Security** (safer = faster)
- **Food supply** (starving = slower)
- **Loyalty** (disloyal = slower)
- **Season** (spring/summer +15%, winter -25%)
- **Peace** (+25% regen when your kingdom is at peace)
- **Governor skills** (Steward boosts regen, Leadership boosts pool size)
- **Slaves in town market** (each slave adds construction/prosperity bonuses; excess above cap are manumitted into MP)
- **Castle minimum floor** — castles always regen at least 1/day (peasant levies from bound villages)
- **Castle supply chain** — castle regen above the local trickle floor (1/day) is **transferred from the nearest same-faction town**, not created from nothing. If the supply town is depleted, the castle starves. **Raiding a town starves its dependent castles.**
- **Castles in a kingdom with no town** — if your kingdom owns no town anywhere, its castles are cut off from the supply chain. They raise men from their own villages instead, and recover **4 a day** rather than the 1 a day a supplied castle falls back on. That refills a typical castle in under two months instead of most of a year, which is what makes declaring independence with a single castle survivable. It goes back to normal the moment you take a town, and kingdoms that already hold towns are unaffected.
- **Depleted emergency regen** — settlements below 15% fill get a bonus up to +2/day at 0%, scaling down to +0 at 15%. Devastated frontiers recover slowly — historically, post-Manzikert eastern Anatolia took decades.

### How to Check Manpower

- **Press M** → go to the **Nearby**, **Castles**, or **Towns** tab to see MP values at a glance.
- **Click any settlement on the map** → the **Current** tab shows a full breakdown of that settlement's manpower pool, regen rate, and all modifiers.

### What Happens When Manpower Runs Out

- **Volunteer slots at villages go empty** — no one to recruit.
- **Garrison stops growing** — no auto-recruits.
- A **yellow warning message** appears in the game log when a pool drops below 25%.
- **Militia growth slows** proportionally (0 MP = 0 militia growth).

---

## 4. Recruiting Volunteers (Villages & Towns)

Recruitment works exactly like vanilla, **but now it's gated by manpower and settlement-type volunteer tier caps**.

### Step by Step

1. **Move your party to a village or town** (as usual).
2. **Click "Recruit Troops"** in the settlement menu.
3. The recruitment screen appears — volunteers are shown as normal.
4. **Click a volunteer to recruit them.** Each recruitment deducts manpower from the settlement's pool.
5. Slots above that settlement type's volunteer cap are **normalized out of the board** before you recruit. Villages should only show village-legal tiers; towns should only show town-legal tiers.
6. If manpower is insufficient, the recruit button will be blocked and a **yellow message** explains why: *"Manpower: cannot recruit X — [Settlement] needs Y, only Z left."*

### What's Different from Vanilla

- **Depleted pools = empty recruitment boards.** If a village's bound town/castle has 0 manpower, you'll find no volunteers.
- **Village and town boards can have different tier caps.** By default, villages stop at T2 and towns stop at T4.
- **The cap uses the source settlement type only.** A village still draws manpower from its bound town/castle pool, but the troop-tier cap is determined by the village board itself.
- **Illegal slots are sanitized at the roster level.** Existing over-cap volunteers are downgraded to a legal troop of that culture, or the slot is emptied for the game to refill on its next daily roll, so they do not clog the board forever.
- **Troop overhaul mods are handled.** Overhauls such as **De Re Militari** contain troop branches that do not start at the culture's basic troop — a branch running T4→T6, for example. Campaign++ now walks *up* those branches as well as down from the roots, so an over-cap volunteer always gets replaced instead of sitting in the slot forever. If you were on an earlier version and have villages full of volunteers you cannot recruit, **they clear themselves when you load the save** — no new campaign needed.
- **Recruit All and Done are still checked too.** The button/path gates remain as a defensive fallback if another mod injects an over-cap troop into the screen.
- **Castle recruitment is separate.** Castle elite recruitment is not affected by these volunteer-board caps.
- **Matching culture gives a 25% manpower discount** (a recruit from a matching-culture settlement costs 0.75 MP instead of 1).
- **You cannot recruit in a kingdom you are at war with.** Their villages give you nothing. Their towns and castles you could never enter anyway.
- **Recruiting in another kingdom at peace costs extra gold** — you are outbidding the local lord. Roughly double for ordinary recruits by default, less for elite troops. The manpower cost is the same as anywhere else.
- **Both rules only apply once you have a kingdom or a fief.** A clan with neither recruits anywhere, so the early game is untouched. Swear to a kingdom as a vassal or a mercenary and that kingdom becomes your realm — everywhere else is foreign, even before you are granted a fief.
- **Enemy lords play by the same rules.**

---

## 4A. Troop Service & Demobilization

Field troops now have a service life. After enough Bannerlord days in the field, some soldiers finish their term and leave the party gradually instead of serving forever.

### What Counts

- Your **main party** is tracked in detail and shown in the service screen.
- AI lord and mercenary field parties follow the same rules.
- Garrisons, militia, caravans, villagers, bandits, and prisoners are not part of this system.

### How to Read It

- Press the configured **Troop Service hotkey** on the campaign map (default: F9) to open the service screen.
- The screen shows each tracked soldier, how long he has served, how many days remain, and the gold cost to extend him.
- When soldiers are close to leaving, the game can show a daily warning message and, by default, a popup with an **Open Service** button when the earliest soldier reaches the 14-day warning lead or day 0.

### How to Manage It

- Pay to extend a specific soldier if you need him for the next campaign season. Each soldier can only be extended once.
- AI lord field parties can also pay for one-time extensions, using the same cost formula, if the lord has enough spare gold. Garrisons and militia never use this system.
- Use the MCM preset to choose Light, Moderate, Harsh, or Custom service lengths.
- One Bannerlord year is about 84 days, so a 45-day service term is a little over half a year in game time.
- Moving troops between field parties or briefly parking them outside the party no longer resets their service age; short transfers keep the original service record.
- Newly recruited castle elites and converted prisoners start with age 0 when they join a field party.
- Promoting a soldier gives him a small service grace period by default, reducing his service age by 5 days without resetting him to age 0.
- The default balance is conservative: 14-day warnings, 15% daily troop-type departure cap, 10 maximum departures per party per day, and season/crisis pressure disabled unless you enable those realism toggles in MCM.

---

## 5. Castle Recruitment — Elite Troops & Prisoners

Castles now have their own recruitment system with **three sources of troops**.

### How to Recruit from a Castle

1. **Travel to any non-hostile castle** (your own, allied, or neutral).
2. **Enter the castle** (neutral castles no longer charge a bribe — see below).
3. In the castle menu, look for: **🏰 Recruit troops (X available, Y pending)**
4. **Click it** to open the castle recruitment screen.
5. You'll see two types of troops:
   - **Elite pool troops** — culture-matching troops generated daily from the castle's manpower. By default T4–T6; with **Diversified Pool** enabled, also includes T2 levy, T2 noble, and T3–T4 troops.
   - **Converted prisoners** — T4+ prisoners that have finished their holding period.
6. **Click individual troops to recruit them.** Each has a gold cost.
7. **"Recruit All" buttons** — each section (Elite and Ready) has a "Recruit All" button that recruits all available troops of that type in one click.

### Troop Costs

| Source | Gold Cost | Manpower Cost |
|--------|-----------|---------------|
| Elite pool T2 (diversified) | 150 | Drains castle MP |
| Elite pool T3 (diversified) | 400 | Drains castle MP |
| Elite pool T4 | 1,200 | Drains castle MP |
| Elite pool T5 | 2,500 | Drains castle MP |
| Elite pool T6 | 5,000 | Drains castle MP |
| Converted prisoner T4 | 1,200 | None |
| Converted prisoner T5 | 2,500 | None |
| Converted prisoner T6+ | 5,000 | None |

### Discount Rules

| Situation | Discount |
|-----------|----------|
| You own the castle | Castle owner's 30% fee is waived |
| Prisoner was deposited by your clan | Depositor's 70% share is waived |
| Both (your castle + your prisoner) | **Free** |
| Otherwise | Full price |

### Prisoner Holding Periods

Prisoners don't convert instantly. They must be held:
- **T4**: 10 days
- **T5**: 21 days
- **T6+**: 35 days

The menu shows pending prisoners with their progress. Check back later when they're ready.

### Neutral Castle Access

By default, the mod **removes the vanilla bribe/clan-tier restriction** for entering neutral castles. You can walk right in and recruit. This can be turned off in MCM (`Open castle access`).

### Access Restriction (New)

The `Castle recruitment access` MCM setting controls who can recruit from a castle:

| Value | Who Can Recruit |
|-------|----------------|
| 0 — Open | Anyone non-hostile |
| 1 — Same faction | Only lords in the castle owner's kingdom (default) |
| 2 — Clan + ruler | Only lords in the castle owner's clan or the ruling clan of the owner's kingdom |

This applies identically to **player and AI**. AI lords that fail the restriction check skip castle recruitment entirely.

### Diversified Pool (New)

When `Enable diversified castle pool` is ON, the elite pool regenerates a **weighted mix** of troop tiers instead of only T4–T6:

| Bucket | Default % | Contains |
|--------|-----------|----------|
| T2 Levy | 45% | Basic infantry/ranged of the castle's culture |
| T2 Noble | 35% | Noble-line T2 troops (cavalry path) |
| T3–T4 | 15% | Mid-tier troops |
| T5+ | 5% | Elite and champion troops |

All percentages are configurable in MCM. The pool header in the recruitment screen changes to **"Castle Levy (Diversified Pool)"** when active.

Castle pools also supplement the vanilla culture roots with any troop trees currently present on same-culture volunteer boards. If you add a troop-tree mod mid-save and castle pools look stale, open **Campaign++ Compatibility** and click **Rebuild Sources**.

### Dynamic Pool Capacity (New)

When `Enable dynamic pool capacity` is ON, the pool cap scales with the castle's development:

**Pool Cap = Base + (Prosperity × Scaling) + (Wall Level × Bonus)**

Defaults: Base 5, Prosperity scaling 0.005, Wall bonus 3. A castle with 2000 prosperity and level-3 walls would have cap = 5 + 10 + 9 = 24.

---

## 6. Depositing Prisoners at Castles

You can dump your prisoners at castles for processing — they'll be enslaved (T1–T3) or held for conversion (T4+).

### At Your Own or Allied Castles

1. **Enter the castle.**
2. **Go to the dungeon** (castle menu → Manage Prisoners).
3. **Use vanilla's "Donate prisoners"** option — it works as normal.
4. You'll see a confirmation: *"Deposited X prisoners at [Castle]. Low-tier will be auto-enslaved; elites held for conversion."*
5. At your own castle, you receive **100% of enslavement income** (no holding fee since you own it).

### At Neutral Castles

1. **Enter the castle** (no bribe needed with Castle Open Access on).
2. **Go to the dungeon.**
3. Look for: **⚔️ Deposit prisoners (X available, 30% holding fee)**
4. **Click it** to open the prisoner donation screen.
5. Select which prisoners to deposit.

### Influence Rule (v0.1.8.8)

You only receive **influence** for donating prisoners when the castle belongs to **your faction**. Depositing at a neutral or allied castle still earns consignment gold, but **no influence**. This prevents mercenaries from converting cross-faction influence into gold.

### What Happens to Your Deposited Prisoners

- **T1–T3 prisoners**: Auto-enslaved daily to the nearest town. At your own castle you receive **100% of the gold**. At another clan's castle, you receive **70%** (castle owner keeps 30% holding fee).
- **Roguery XP stays with the original captor**: When those low-tier prisoners are eventually auto-enslaved at a castle, Campaign++ credits Roguery XP to the depositor who captured them, not to the castle owner.
- **T4+ prisoners**: Held for conversion. Once ready, whoever recruits them pays gold. You receive your depositor's share.
- **Garrison absorption**: The castle garrison absorbs 1 ready prisoner/day automatically. The castle owner pays you your share.
- **Affordability gate**: The nearest town must have enough gold to buy each slave. If the town runs out, remaining prisoners stay in the dungeon until the next day.
- **When enslavement isn't possible at all**: If the Slave Economy is switched off, or the castle's kingdom no longer holds a town to sell to, T1–T3 prisoners are **left with you instead of being deposited** — there would be no way out of that dungeon for them. Ransom them at a town as normal. T4+ deposits are unaffected, and this never triggers in a normal campaign with the Slave Economy on.

You'll see notification messages in the game log:

- **Green (owner income):** *"[Castle] enslaved X prisoners — sold to [Town], treasury income +Yg."* — appears when your own castle processes prisoners.
- **Blue (depositor income):** *"Consignment from [Castle]: +Xg (Y prisoners enslaved at [Town], your 70% depositor share)."* — appears when another clan's castle processes your deposited prisoners.

---

## 7. Village Investment (Patronage)

Invest gold in friendly villages to boost their hearth growth, improve relations with notables, gain influence, and increase notable power — while removing gold from the world economy.

### How to Invest

1. **Travel to any non-hostile village** that isn't being raided or looted.
2. In the village menu, click **🏠 Invest in village**.
3. **Choose a tier:** Modest, Generous, or Grand.
4. Gold is deducted immediately; bonuses apply instantly (relation, power, influence) or over time (hearth).

### Investment Tiers

| Tier | Cost | Duration | Hearth/day | Relation | Influence | Power |
|------|------|----------|-----------|----------|-----------|-------|
| Modest gift | 2,000d | 20 days | +0.3 | +3 per notable | +0.5 | +5 per notable |
| Generous patronage | 5,000d | 30 days | +0.6 | +6 per notable | +1.0 | +10 per notable |
| Grand investment | 12,000d | 45 days | +1.0 | +10 per notable | +2.0 | +20 per notable |

### Key Rules

- **Duration = cooldown** — you cannot invest again at the same village until the current patronage expires.
- **Influence** is only granted if the village belongs to your kingdom.
- **Cross-clan bonus** — investing in another clan's village gives +2 relation with that clan's leader.
- **Notable power** is capped at 200 (controls volunteer tier quality).
- **Gold is destroyed** — this is a pure gold sink, reducing world inflation.
- **AI lords also invest** in their own faction's villages using the same tiers and costs.

### What You'll See

- A **green notification** confirming your investment with all bonuses applied.
- The hearth tooltip for the village shows a **"Patronage"** line during the investment.
- The menu option shows remaining days if you already have an active investment.

---

## 8. Town Investment (Civic Patronage)

Invest gold in friendly towns to boost their prosperity growth, improve relations with notables, gain influence, and increase notable power — a heavier gold sink than village patronage, targeting the urban economy.

### How to Invest

1. **Travel to any non-hostile town** that isn't under siege.
2. In the town menu, click **🏛 Invest in town**.
3. **Choose a tier:** Modest, Generous, or Grand.
4. Gold is deducted immediately; bonuses apply instantly (relation, power, influence) or over time (prosperity).

### Investment Tiers

| Tier | Cost | Duration | Prosperity/day | Relation | Influence | Power |
|------|------|----------|---------------|----------|-----------|-------|
| Modest gift | 5,000d | 20 days | +0.5 | +3 per notable | +2.0 | +5 per notable |
| Generous patronage | 15,000d | 40 days | +1.0 | +6 per notable | +5.0 | +10 per notable |
| Grand investment | 40,000d | 60 days | +2.0 | +10 per notable | +10.0 | +20 per notable |

### Key Rules

- **Duration = cooldown** — you cannot invest again at the same town until the current patronage expires.
- **Cannot invest during a siege** — menu option hidden while the town is besieged.
- **Influence** is only granted if the town belongs to your kingdom.
- **Cross-clan bonus** — investing in another clan's town gives +2 relation with that clan's leader.
- **Notable power** is capped at 200.
- **Gold is destroyed** — this is a pure gold sink, reducing world inflation.
- **AI lords also invest** in their own faction's towns using the same tiers and costs.

### What You'll See

- A **blue notification** confirming your investment with all bonuses applied.
- The prosperity tooltip for the town shows a **"Civic Patronage"** line during the investment.
- The menu option shows remaining days if you already have an active investment.

---

## 9. The Slave Economy

### How to Get Slaves

There are **three ways** to acquire slaves:

#### Method 1: Raid a Village
1. **Declare war** on a faction (or be at war).
2. **Attack and raid an enemy village** (vanilla raid mechanic).
3. During the raid, **slaves are automatically added to your party inventory** (1 slave per second if 300+ hearts and 2 slaves per second if 600+ hearts, no slaves if under 300 hearths).
4. A gold notification confirms the loot.

#### Method 2: Enslave Prisoners at a Town
1. **Win a battle** and take prisoners.
2. **Travel to any town.**
3. In the town menu, look for: **⛓ Enslave prisoners (X T1–3 eligible)**
4. **Click it** to enter the slave trade submenu.
5. Click **"Enslave prisoners"** — by default this converts all your non-hero prisoners at **Tier 3 or below** into Slave trade goods (1:1). Heroes and T4+ prisoners are never enslaved.
   - **Selection UI (new):** If `Enable slave conversion selection` is ON in MCM → Slave Economy, clicking enslave opens a **selection screen** where you pick exactly which prisoners to convert (useful with mods like Lowborn that add valuable low-tier troops). Use +/− buttons or Select All / Deselect All.
6. A gold notification confirms: *"⛓ Enslaved X T1–3 prisoners. Slave goods in inventory: Y. Open the Trade screen to sell them to the market."* By default, the action also grants **Roguery XP matching vanilla prisoner sales**.

> **What happens to T4+ prisoners?** They stay in your prison roster. Take them to a castle for recruitment conversion (they become elite recruits after a waiting period) or ransom them at the town tavern.

> **Tip:** The menu clearly shows how many prisoners are eligible (T1–3) and how many T4+ are kept. If you only have T4+ prisoners, the option appears greyed out with a tooltip explaining why.

#### Method 3: Automatic (AI Lords)
When AI lords enter a town, their T1–3 prisoners are automatically enslaved into the town market as slave goods via a Harmony Prefix that runs before vanilla's prisoner sell handler. The town pays the lord the current slave market price for each enslaved prisoner (gold is deducted from town treasury). If the town runs out of gold, remaining prisoners fall through to vanilla sell behavior. T4+ prisoners are left for vanilla behavior (ransomed at the tavern) or deposited at castles for recruitment conversion. This happens in the background — you'll see the slave count in town markets growing over time. The tier cap is the same for both player and AI (`CastlePrisonerAutoEnslaveTierMax` MCM setting, default 3), and the same Roguery XP rules apply there too.

### How to Sell Slaves

1. After enslaving prisoners, **Slave goods appear in your inventory** like any trade item.
2. **Open the Trade screen** at any town (the normal trade button).
3. **Sell the Slave goods** to the town market at market price.

### Town Bonuses from Slaves

While Slave goods remain in a town's market, the town gets **daily bonuses**:

| Bonus | Amount (per slave/day) |
|-------|----------------------|
| Prosperity | +0.01 |
| Construction speed | +0.75 progress |
| Food drain | -0.075 food |

Slaves also **decay at 1%/day** — they don’t last forever. A town with 100 slaves loses ~1 slave per day.

### Slave Stock Cap & Manumission (v0.1.8.4)

Each town has a **slave cap** based on prosperity: `max(10, prosperity × 0.02)`. A 3000-prosperity town can hold ~60 slaves; a 1000-prosperity town ~20.

- **Excess slaves are manumitted (freed) daily** and converted **1:1 into the town's manpower pool**.
- This prevents global oversaturation (where every town sits at floor price) and creates supply differentials that drive caravan trade.
- The gameplay loop: War → prisoners → slaves → construction/prosperity bonuses while under cap. Overflow → freed → MP returned to town pool.
- MCM settings: `Slave cap per prosperity` (default 0.02), `Slave cap minimum` (default 10), and `Slave price decay rate` (default 0.98).

> **Note (v0.1.8.3):** Slaves no longer provide direct manpower injection. Historically, enslaved populations were labor (construction, agriculture) — not a military recruitment source. However, excess slaves above the cap are now manumitted into MP (v0.1.8.4).

> **Note (v0.1.8.3):** Slave prices now respond properly to supply and demand. Previously, Bannerlord's engine silently ignored the `is_trade_good` flag on custom categories, clamping prices to a narrow band (~1200 denars regardless of stock). Additionally, the engine's supply tracking "remembered" old stock for weeks after it was bought — buying 100 slaves from a town wouldn't raise the price for 30+ days. Both issues are now fixed: prices react to actual stock within 1–2 days, enabling profitable buy-low-sell-high slave trading.

> **Tip:** Sell slaves at towns where you want faster construction projects. Excess slaves above the cap are automatically freed and returned as MP — don't worry about oversaturating, the system self-regulates.

---

## 10. War Effects & Devastation

### During War: What Happens to Your Settlements

| Event | Effect on Manpower |
|-------|-------------------|
| **Your village is raided** | -15% of bound settlement's MP pool (capped at 20%/day) |
| **Your castle/town is besieged** | After siege: pool set to 10% (Devastate), 40% (Pillage), or 70% (Show Mercy) |
| **Your troops die in battle** | Each casualty drains 0.5 MP from their home settlement |
| **You lose a settlement** | New owner keeps only 50% of the current pool (depleted castles below 25% keep up to 85% — conquest protection) |

### Frontier Devastation

When an enemy raids one of your villages, the village gains **Devastation** (0–100 scale):

- Each raid adds **+25 devastation**.
- Devastation **decays at 0.5/day** (~50 days to fully heal).
- Effects on the village: **up to -2 hearth/day**.
- Effects on the bound town/castle: **up to -2 prosperity, -1.5 security, -1.5 food/day** (scaled by average devastation across all bound villages).

### Governance Strain

Wars, raids, and sieges accumulate **governance strain** on your settlements (0–100):

- Strain decays at 0.3/day (~33 days for +10 strain to decay).
- At high strain: **up to -3 loyalty/day, -2 security/day, -1 prosperity/day**.
- Same-faction lords can fund **Provincial Stabilization** at strained towns and castles: Emergency Relief, Placate Local Elites, or Grant Amnesty. These spend gold to reduce strain immediately and add temporary loyalty/security recovery.

### Delayed Recovery

After war events, settlements suffer temporary regen penalties:

| Event | Penalty Duration | Regen Penalty |
|-------|-----------------|---------------|
| Raid | 20 days | -10% regen |
| Siege | 35 days | -18% regen |
| Conquest | 50 days | -25% regen |

When a pool is severely depleted (below 25%), recovery penalties are automatically halved — the settlement needs faster regen most, so the penalty is reduced to let it recover.

### How to Monitor This

- **Press M** → **Current** tab → click on any settlement to see its governance strain, devastation, and recovery penalties.
- **Press M** → **Rebellion** tab to see which settlements are at risk of revolt due to accumulated damage.

---

## 11. War Exhaustion & Diplomacy

### War Exhaustion

Every kingdom accumulates **war exhaustion** (0–100) from fighting:

| Event | Exhaustion Gained |
|-------|-------------------|
| Battle casualties | +0.001 per casualty |
| Village raid (your village raided) | +2.0 |
| Siege aftermath (defender) | +5.0 |
| Siege aftermath (attacker) | +3.0 |
| Settlement conquered | +4.0 |
| Noble captured | +5.0 |

Exhaustion **decays at 0.65/day** naturally. Recovery from 100→0 takes ~154 days (~1.8 in-game years).

### What Exhaustion Does

| Exhaustion Level | Effect |
|-----------------|--------|
| **35+ (Rising)** | AI increasingly pressured toward peace; moderate war declaration penalties |
| **65+ (Crisis)** | AI strongly pushed toward peace; new war declarations blocked |
| **80+** | **Forced peace triggered** — the mod auto-ends the most diplomatically favorable war |

The system uses **pressure bands** (Low → Rising → Crisis) with hysteresis to prevent oscillation when exhaustion hovers near a boundary.

Multi-front wars still accelerate collapse: each extra war beyond 2 reduces the forced-peace threshold by 5 (80→75→70).

Forced peace has safety rails:
- Minimum 40 days into a war before it can be force-ended.
- 10-day cooldown between forced peaces for the same kingdom.
- Won't fire if an enemy is actively besieging one of your core settlements.
- Won't fire against a faction your kingdom is actively besieging — sieges are completed before peace is forced.
- 30-day truce enforced after any peace (no instant re-declarations).

If a kingdom is in a true multi-front collapse, those rails can loosen slightly instead of breaking entirely:
- By default, the minimum war age can drop from 40 days to 15 days.
- This only happens if the kingdom is simultaneously in the Crisis band, fighting at least 2 kingdom wars, and sitting at 25% average manpower or lower.
- The same emergency window also softens the council peace-vote penalty, so desperate kingdoms can actually get out of a death spiral.

> **Siege-aware peace voting:** When your kingdom has armies besieging the enemy, the mod's exhaustion and manpower peace bonuses are fully suppressed — preventing absurd peace votes while you're winning a siege. Vanilla's own peace scoring still applies, so peace is merely harder during offensives, not impossible.

> **Early-war peace penalty (v0.2.7.2):** Council peace votes for wars younger than 40 days receive a large penalty (default 300, fading linearly to 0). This prevents councils from voting for peace in the first weeks of a war. The penalty is a soft gate, not a hard lock. In an extreme multi-front crisis, the vote penalty fades out earlier to match the emergency war-duration rule.

### Does This Affect the Player?

**Yes, by default.** `DiplomacyEnforcePlayerParity` is ON, meaning forced peace applies to your kingdom too — full AI parity. If you want to exempt yourself, disable it in MCM.

### How to Monitor Exhaustion

- **Press M** → **Wars** tab — see exhaustion levels and peace pressure status for every active war.
- **Press M** → **Factions** tab — see kingdom-level aggregate data.
- **Press M** → **Armies** tab — see army exhaustion.

---

## 12. Army Economics

### Tier-Exponential Costs

The mod makes higher-tier troops significantly more expensive:

| Cost Type | What Changes |
|-----------|-------------|
| **Hire & upgrade** | Tier-exponential scaling (configurable: Vanilla / Light / Moderate / Severe) |
| **Daily wages** | Same — higher tiers cost more to maintain |
| **Garrison wages** | Discounted to **80% of field rates** (garrisons are cheaper to maintain) |

Default preset is **Moderate** for hire and upgrade costs, and **Severe** for daily wages.

### Recruiting Outside Your Own Kingdom

Once you belong to a kingdom or own a fief, recruiting in a settlement your realm does not own costs extra gold — you are outbidding the local lord for men who owe him service and owe you nothing.

The premium is added to the troop's **base** price, and Campaign++ is already charging well above that through the hire and upgrade preset. Both land on the same bill, so at the default settings you pay:

| Troop | At home | Abroad |
|---|---|---|
| Village recruit (T1) | 11g | 21g |
| T2 | 65g | 115g |
| T3 | 175g | 275g |
| T5 | 1400g | 1800g |
| T6 | 3000g | 3600g |

So it bites hardest on cheap village volunteers, which is where most foreign recruiting happens, and barely shows on elite troops, whose price is already dominated by their tier. Set the preset to 3 if you want the full doubling to hold at the top end as well.

This includes landless vassals and mercenaries. The kingdom you serve is your realm; every other kingdom is abroad.

| Setting | What it does |
|---|---|
| **Foreign recruitment cost preset** | 0 = off, 1 = 1.5×, 2 = 2× *(default)*, 3 = 3× |
| **Block recruiting in enemy realms** | On by default — no recruiting at all in a kingdom you are at war with |

The **manpower** cost never changes. Manpower belongs to the settlement, so charging you extra manpower would just drain that village faster and punish the kingdom you are visiting instead of you. Gold is yours, so gold is what you pay.

**Tavern mercenaries and caravan guards cost the same everywhere.** They are wandering soldiers who owe no lord anything, so there is nobody to outbid for them — it makes no difference whose town they are drinking in. Your caravans are never charged the premium either; a caravan trades abroad by design.

Enemy lords pay the same premium you do.

### Elite Survivability

Higher-tier troops are tougher in **battles the game resolves for you** — autoresolve, and sieges you do not fight yourself. They take less damage and are more likely to come out wounded than dead, so your expensive veterans are worth the investment.

One setting controls how strong this is: **Combat Realism → Elite survivability preset**.

| Preset | What it does |
|--------|--------------|
| **0** | Off — vanilla behaviour |
| **1** *(default)* | Light — a small edge for T3 and above |
| **2** | Moderate |
| **3** | Strong — the setting used before v1.0.2.5 |

| Tier | Preset 1 | Preset 2 | Preset 3 |
|------|----------|----------|----------|
| T1–T2 | no change | no change | no change |
| T3 | −3% damage, +2% survival | −5%, +3% | −6%, +5% |
| T4 | −6% damage, +4% survival | −10%, +6% | −12%, +10% |
| T5 | −9% damage, +6% survival | −15%, +9% | −18%, +15% |
| T6+ | −12% damage, +8% survival | −20%, +12% | −24%, +20% |

Before v1.0.2.5 this was two separate toggles that quietly stacked, which is why elite troops felt unkillable. If you preferred it that way, set the preset to **3**. Enemy lords get exactly the same treatment as you do.

---

## 13. MCM Settings

### How to Access MCM

1. From the **main menu** or **in-game pause menu**, click **Mod Options** (requires MCM mod).
2. Find **"Campaign++"** in the list (two tabs: full settings + Quick Settings).
3. Browse through the full settings tab for detailed system-by-system tuning, or use Quick Settings for the master toggles.

### Quick Settings Tab

The **Campaign++ - Quick Settings** tab gathers all 22 system master toggles into one screen. Use it to enable or disable any system without searching through the full settings list:

| Group | Toggles |
|-------|---------|
| Core Systems | War Effects, War Exhaustion, Diplomacy Pressure, Forced Peace, Delayed Recovery, Militia Link |
| Economy & Investment | Slave Economy, Village Investment, Town Investment, Minor Faction Economy, Garrison Wage Discount |
| Recruitment & Military | Castle Recruitment, Open Castle Access, Elite Survivability (0–3), Clan Survival |
| Province & Governance | Governance Strain, Provincial Stabilization, Frontier Devastation, Castle Supply Chain |
| Immersion & Modifiers | Seasonal Regen, Peace Dividend, Culture Discount, Governor Bonus, Overlay, Manpower Alerts |

All toggles are mirrors of the corresponding settings in the full tab — changing one changes the other.

### Key Settings to Know About

| Setting | Where in MCM | Default | What It Does |
|---------|-------------|---------|--------------|
| Enable castle recruitment | Castle Recruitment | ON | Master toggle for the entire castle system |
| Open castle access | Castle Recruitment | ON | Removes bribe/clan-tier restriction at neutral castles |
| Castle holding fee % | Castle Recruitment | 30% | Commission the castle owner takes from cross-clan prisoner processing |
| Village volunteer tier max | Recruitment Cost | 2 | Highest troop tier recruitable from village volunteer boards |
| Town volunteer tier max | Recruitment Cost | 4 | Highest troop tier recruitable from town volunteer boards |
| Enable village investment | Village Investment | ON | Master toggle for village patronage system |
| Enable slave economy | Slave Economy | ON | Master toggle for slave acquisition, trade, and town bonuses |
| Enable war exhaustion | War Exhaustion | ON | Turns on per-kingdom exhaustion from combat |
| Enforce player parity | Diplomacy | ON | Player kingdom follows same forced-peace rules as AI |
| Forced peace threshold | Diplomacy | 80 | Exhaustion level that triggers forced peace |
| Early-war peace vote penalty | Diplomacy | 300 | Makes very young wars much harder to end through council votes |
| Enable multi-front war relief | Diplomacy | ON | Lets a kingdom in true multi-front collapse seek peace earlier instead of waiting the full minimum |
| Hotkey (0-6) | Overlay | 0 (M key) | Change the overlay toggle key |
| Hire & upgrade cost preset | Army Economics | 2 (Moderate) | 0=Vanilla, 1=Light, 2=Moderate, 3=Severe |
| Daily wage preset | Army Economics | 3 (Severe) | 0=Vanilla, 1=Light, 2=Moderate, 3=Severe |
| Garrison wage % of field | Army Economics | 80 | Garrisons pay 80% of field wages (20% discount) |
| Slave price decay rate | Slave Economy | 0.98 | Controls how steeply slave prices fall per unit of stock (exponential decay). Lower = steeper drop. |
| Slave cap per prosperity | Slave Economy | 0.03 | Max slaves per point of prosperity. Excess manumitted daily into MP. |
| Enable manpower alerts | Alerts & Militia | ON | Warning when pools drop below 25% |

---

## 14. Clan Survival

When a kingdom is destroyed in vanilla, all member clans are annihilated and their heroes killed. Campaign++ rescues eligible clans instead.

### What Happens

1. **Kingdom falls** — the kingdom is destroyed via war, diplomacy, or leader death with no successor.
2. **Rescue check** — each clan is evaluated: do they have living adult lords? If yes, they're rescued. If no surviving heroes exist, vanilla destruction proceeds normally.
3. **Fiefs transferred** — any settlements owned by the rescued clan go to a suitable heir clan (same as vanilla's fief inheritance).
4. **Independent survival** — the clan detaches from the dead kingdom and becomes an independent faction.
5. **War reset** — inherited wars are cleared so the rescued clan starts neutral.
6. **Tracking** — the mod keeps rescued clans alive and tracked while they are independent; if they join a kingdom later through vanilla systems or other mods, tracking ends.

### What's NOT Rescued

- Clans destroyed by **failed rebellions** (legitimate consequence — the rebellion itself was defeated)
- **Rebel clans whose rebellion was crushed** — since v1.0.2.6 these die as they do in vanilla, whether they lose their last town or their leader dies. See below if you want the old behaviour back.
- Clans with **no living adult heroes** (no one to carry on)
- The **player's own clan** (vanilla handles this separately)

### Rebel Clan Rescue — Off by Default Since v1.0.2.6

**A crushed rebellion now dies, exactly as in vanilla.** The town goes back to its old owner and the rebel lords are gone.

Why it changed: keeping a rebel clan alive meant turning it into a **mercenary company**, because that is the only way it can draw Frontier Revenue. Every crushed rebellion therefore left a permanent company for hire, with no limit — players reported over 40 of them by day 800.

Turn **Rescue crushed rebel clans** on in MCM → Clan Survival if you want the old behaviour. Nothing was removed. Noble clans of a *destroyed kingdom* are still rescued either way; this setting only covers rebels.

**Already have dozens of them in your save?** Turn on **Clean up leftover rebel mercenary companies**, load the save, and wait one campaign day. **Back up your save first — it destroys clans.**

Nothing happens until you say so:

1. On the next campaign day a window tells you how many leftover companies it found in *your* save and asks whether to remove them. Say no and nothing is touched.
2. If you agree, they are disbanded a **few each day** rather than all at once. Removing 40–100 companies in a single day would empty a large part of the map at a stroke; a few a day looks like ordinary attrition. You are told how many are left each day.
3. When it has finished you get a message, and you can turn the setting back off.

It only removes companies that came from a rebellion, were made mercenaries by this mod, hold no fief, and are not currently hired by a kingdom. Vanilla minor factions like the Ghilman and the Jawwal are never touched, and neither is any clan that owns a settlement.

If you are loading an older save and leave both settings alone, Campaign++ tells you once that the rescue is off and that your existing companies are being left in place. Nothing is done to them without your say-so.

With the setting **on**, rebel clans are rescued when they would otherwise be destroyed:

- When a rebel clan **loses its last settlement** (e.g., another faction reconquers the town), Campaign++ intercepts the destruction and rescues the clan.
- When a rebel clan's **leader dies** and vanilla calls `DestroyClanAction`, the safety net promotes an heir (if one exists) and rescues the clan.
- Rescued rebel clans are **normalized**: `IsRebelClan` is cleared, `IsMinorFaction` is set to `true`, and the clan is removed from vanilla's internal rebel tracking. The clan is also **renamed** from its settlement-based name (e.g., "Pen Cannoc rebels") to a leader-derived warband name (e.g., "Borun's Warband") for uniqueness.
- Because `IsMinorFaction` is now `true`, rescued rebel clans qualify for **Frontier Revenue** — the same unaligned stipend that other minor factions receive.
- They then survive as an independent warband until vanilla's AI recruits them into a kingdom as a vassal or mercenary.
- **Save/load coverage**: Rebel clans that lost their settlement in a *previous session* (before Campaign++ was installed or before the save was loaded) are automatically detected and rescued on session start. You don't need to worry about timing — the mod scans for homeless rebel clans every time you load a save.

> **Note:** Rebel clans destroyed during the *rebellion itself* (i.e., the revolt fails before the town is taken) are NOT rescued — that's a legitimate consequence of a failed uprising.

### Key Settings

| Setting | Default | What It Does |
|---------|---------|-------------|
| Enable clan survival | On | Master toggle |
| Rescue crushed rebel clans | **Off** | Keeps crushed rebellions alive as mercenary companies (pre-v1.0.2.6 behaviour) |
| Clean up leftover rebel mercenary companies | **Off** | One-off cleanup for an existing save. Destroys clans — back up first |
| Grace period (days) | 30 | Reserved for planned auto-placement logic (no forced placement in v0.2.0.1) |
| Culture match weight | 2.0 | Reserved for planned auto-placement scoring (no forced placement in v0.2.0.1) |

### Tips

- If you want to audit rescue behavior, enable verbose logging in MCM and review the session or game logs.
- Rescued clans are not hard-assigned by this system in v0.2.0.1; later kingdom entry depends on vanilla campaign behavior or other mods.
- Keep clan survival enabled if you want more late-game noble continuity after kingdom collapse events.

---

## 15. Mod Compatibility Report

Campaign++ automatically scans your load order at game launch and tells you how every other gameplay mod interacts with it.

### What Happens at Launch

1. **Scan** — Campaign++ reads all active Harmony patches and finds any mods that patch the same methods it does.
2. **Filter** — Infrastructure mods (Harmony, ButterLib, MCM, UIExtenderEx, BLSE, LauncherEx, etc.) are silently excluded. Only gameplay mods appear.
3. **Popup** — A report appears showing every detected mod on one line:
   - `ModName - Compatible` — no overlap detected.
   - `ModName - Minor overlap in X areas - very likely fine` — both mods touch the same calculation additively; usually fine.
   - `ModName - Worth checking: Area` — one mod can override the other's result; play a few sessions and check that area.
4. **Copy** — Click **Copy Report** in the popup to copy the full text to clipboard (useful for bug reports).
5. **MCM tab** — Open **Mod Options → Campaign++ Compatibility** for the full breakdown: one row per overlapping method with hover hints explaining the interaction in plain English.

### Disabling the Startup Popup

If you've read the report and don't want it on every launch:
1. **Mod Options → Campaign++ Compatibility**
2. Toggle **Don't show this popup at startup** → ON.

### What the Ratings Mean

| Rating | Meaning |
|--------|---------|
| Compatible | No overlap detected — both mods run independently. |
| Minor overlap - very likely fine | Both mods add their effect to the same daily number (additive). Rarely a problem. |
| Worth checking | One mod can short-circuit the other's logic. Play normally and note if anything feels off. |

### MCM Tab — Summary Group

At the top of the **Campaign++ Compatibility** MCM tab, the **Summary** group shows:

| Row | What It Means |
|-----|---------------|
| **Report status** | `Partial — load any campaign` = Harmony scan is done but Core Game Systems have not been checked yet (title screen only). `Up to date` = full scan including model checks has run this session. |
| **Don't show this popup at startup** | Toggle to suppress the startup popup once you've reviewed the report. |
| **Running alongside** | How many mods were detected and their overall status. |
| **Core game systems** | Whether the food/volunteer/militia/prosperity models are intact (only meaningful after loading a campaign). |
| **Tip** | Reminds you that loading a campaign triggers the full report and popup. |
| **Open Full Report** | Re-opens the popup at any time. |
| **Rebuild Sources** | Re-scans volunteer boards and refreshes castle recruitment caches after troop-tree mods change mid-save. |

> **The "Report status" row is the quickest way to know if the MCM tab is showing fresh data.** If it says Partial, load any campaign and check back.

### Core Game Systems

The MCM tab also shows a **Core Game Systems** section that checks whether any other mod has fully replaced the food, volunteer, militia, or prosperity model. This runs when you load a campaign (not at the title screen). Status values:
- **Campaign++ active** — no replacement detected; Campaign++ model is active normally.
- **Replaced - auto-patched** — another mod replaced the model but Campaign++ detected it and compensated automatically.
- **[ISSUE] Features may be off** — another mod fully replaced the model and this area may no longer be patched by Campaign++.

> No action is required unless a system shows [ISSUE]. Everything else is informational.

### Lazy-Loading Mods

Some mods (like RBM) apply their Harmony patches at campaign load rather than at startup. Campaign++ detects these with a second scan that runs when a campaign is loaded — so even mods that patch lazily will appear in your report after you load a campaign.

---

## 15b. Known Mod Interactions

Some third-party mods have specific interactions with Campaign++ systems. These are not bugs in Campaign++ but are documented here so you know what to expect.

### Warsails (Naval DLC)

Fully compatible — no action needed. Warsails swaps in its own versions of several campaign systems Campaign++ also modifies (prosperity, security, garrison, militia, wages, clan finance and others), but each one still hands the calculation back to the base game, so Campaign++'s changes continue to apply on top. Warsails is never reported as a conflict in the compatibility report, and Campaign++ works whether or not you own the DLC.

One small exception: in port towns, the Boatswain perk's militia bonus from Warsails is not applied while Campaign++'s militia system is active. This affects militia growth only, nothing else.

### Realms of Thrones (ROT)

ROT's `HeroSpawnCampaignBehaviorPatches` may crash with a null reference when **Clan Survival (Protect Eliminated Clans)** is enabled. ROT assumes certain data exists on all clans, but clans rescued by Campaign++'s clan survival system may not have that data.

**Workaround:** Disable **Protect Eliminated Clans** in MCM → Clan Survival, or report the null-check issue to the ROT mod author.

### Retinues / Custom Troop Tree Mods

Mods that add custom troop trees outside the vanilla culture tree (e.g. Retinues) are compatible with Campaign++. The tier cap system will leave custom-tree troops untouched on the volunteer board if they cannot be traced back to the culture's basic or elite root troop.

If you add or remove one of these mods mid-save, use **Campaign++ Compatibility → Rebuild Sources** to re-scan volunteer boards and refresh the castle recruitment culture caches.

---

## 16. Quick Reference Cheat Sheet

| I Want To... | Do This |
|-------------|---------|
| **See manpower/war data** | Press **M** on campaign map |
| **Check a specific settlement** | Click it on the map, then look at the **Current** tab |
| **Find settlements near me** | Press **M** → **Nearby** tab |
| **Search for anything** | Press **M** → **Search** tab → type your query |
| **Recruit elite troops** | Go to a non-hostile castle → click **🏰 Recruit troops** |
| **Deposit prisoners** | Go to a castle → dungeon → **Donate prisoners** (allied) or **⚔️ Deposit prisoners** (neutral) |
| **Invest in a village** | Go to a non-hostile village → click **🏠 Invest in village** → pick a tier |
| **Enslave prisoners** | Go to any town → click **⛓ Enslave prisoners** |
| **Sell slaves** | Open **Trade** at a town → sell Slave goods from your inventory |
| **Check war exhaustion** | Press **M** → **Wars** tab |
| **See rebellion risk** | Press **M** → **Factions** tab or **Rebellion** tab |
| **View mod compatibility** | Launch game → compatibility popup OR Pause → **Mod Options** → **Campaign++ Compatibility** |
| **Copy compatibility report** | Compatibility popup → **Copy Report** |
| **Quick-toggle any system** | Pause → **Mod Options** → **Campaign++ - Quick Settings** |
| **Change mod settings** | Pause → **Mod Options** → **Campaign++** |
| **Change overlay hotkey** | MCM → Overlay → Hotkey (0=M, 1=N, 2=K, 3=F9–F12) |
| **Disable forced peace on player** | MCM → Diplomacy → Enforce player parity → OFF |
| **Disable castle bribe removal** | MCM → Castle Recruitment → Open castle access → OFF |

---

*That's everything. Play the campaign, press M to monitor the world, recruit from castles, enslave your enemies, and watch the AI struggle under the same economic and military constraints you do.*
