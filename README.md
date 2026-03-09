# Campaign++ (formerly Byzantium 1071)

Strategic warfare and realism overhaul for Mount & Blade II: Bannerlord.

Campaign++ introduces a full **manpower economy** and connects recruitment, warfare, diplomacy, and provincial stability into one coherent simulation loop. The codebase is organized as a modular campaign-systems package with Harmony patch gates, behavior-driven orchestration, MCM-first configuration, and diagnostics-focused UI.

---

## At a glance

- **Current version:** 0.2.7.2  
- **Target game:** Bannerlord v1.3.15 (tested)  
- **Module ID:** `Byzantium1071`

---

## Why Campaign++ is different

- **AI parity first:** the same recruitment and resource constraints apply to player and AI.
- **War has persistent costs:** raids, sieges, casualties, and conquest all reduce military capacity over time.
- **Diplomacy is stateful, not random:** exhaustion, pressure bands, forced peace, and truces shape realistic war cycles.
- **Economy is systemic:** slave labor, provincial strain, devastation, and investment mechanics interact across regions.
- **Visibility over guesswork:** overlay + compatibility reporting expose simulation state and load-order interaction.

---

## Submod integration

Campaign++ exposes a **stable public API** for third-party submods. If you're building a submod that extends or depends on Campaign++:

- **Read [SUBMOD_API.md](Docs/SUBMOD_API.md)** for the full public surface, safe integration patterns, and compatibility checklist.
- **Key surfaces:** behavior instances (manpower, castle recruitment, clan survival, investment), settings read access, Harmony patch guidance, save/load safety
- **Example:** Population growth modifiers, custom war effects, diplomacy rule extensions, cavalry rework with manpower tier scaling

---

## Developer orientation

- **Architecture style:** campaign behaviors own state and ticks; Harmony patches intercept vanilla model/action paths.
- **Config surface:** all major systems are MCM-toggled, with a mirrored Quick Settings tab for operational testing.
- **Data safety:** behavior state persists via `SyncData`; runtime logic is generally null-safe and fail-open.
- **Parity contract:** recruitment/resource rules target player and AI through shared gate logic.
- **Compatibility posture:** runtime scanner reports patch overlap and model replacement risk per mod.

---

## Feature map (from the full GDD)

### Military population systems
- **Manpower pools** for towns/castles (finite recruitment source)
- **Daily regeneration model** with prosperity, security, food, loyalty, season, governor, exhaustion, and delayed recovery
- **Castle supply chain** (optional): castle regen above local trickle is supplied by nearest same-faction town
- **Volunteer production + militia scaling** tied to manpower ratio

### Recruitment control
- **Player gates** for single, recruit-all, and confirm flows
- **AI recruitment gate** on the same manpower rules
- **Garrison auto-recruit cap** constrained by available pool

### Castle recruitment pipeline
- **Elite culture pool** generation
- **Prisoner conversion** (T4/T5/T6 day gates + tiered gold costs)
- **Consignment economy** (depositor/owner split)
- **AI castle recruitment** and **garrison prisoner absorption** with affordability checks

### War pressure and diplomacy
- **War effects** drain pools via raids, sieges, battles, conquest
- **Delayed recovery** imposes post-war regen penalties with decay
- **War exhaustion** accumulates per kingdom and affects regen + diplomacy
- **Pressure bands** (Low/Rising/Crisis) with hysteresis
- **Forced peace** + **truce enforcement** for cooldowned war cycles

### Combat and economy realism
- **Combat realism**: tier survivability and autoresolve armor simulation
- **Army economics**: tier-exponential recruitment and wage scaling
- **Garrison wage discount** with historical rationale

### Province/economy layers
- **Slave economy** (capture, enslavement, market behavior, labor effects, decay, caps/manumission)
- **Minor faction economy** (Frontier Revenue stipends)
- **Provincial governance** (governance strain penalties)
- **Frontier devastation** (persistent raid damage and slow recovery)

### Development systems
- **Village investment (Patronage)**
- **Town investment (Civic Patronage)**
- **Clan survival** on kingdom destruction (rescue flow)
- **Runtime compatibility scanner + report UI**

### Implementation characteristics
- **Patch strategy:** additive where possible, guard-first where hard conflicts are likely
- **Hot-path controls:** day-level caching on expensive calculations (regen/exhaustion consumers)
- **Operational diagnostics:** overlay tabs, compatibility popup/MCM tab, verbose log channels
- **Migration discipline:** version-gated settings profile updates

---

## Overlay / intelligence panel

Default hotkey: `M` (configurable in MCM).

Includes tabs for:

- Current, Nearby, Castles, Towns, Villages
- Factions, Armies, Wars, Rebellion
- Prisoners, Clans, Characters, Search

Search supports heroes, settlements, armies, clans, kingdoms, and market data.

---

## Dependencies

Required:

- `Bannerlord.Harmony` >= 2.3.3
- `Bannerlord.ButterLib` >= 2.9.18
- `Bannerlord.UIExtenderEx` >= 2.12.0
- `Bannerlord.MBOptionScreen` (MCM) >= 5.10.2

---

## Installation

1. Install required dependencies.
2. Place the `Byzantium1071` module folder into Bannerlord `Modules`.
3. Enable dependencies first, then Campaign++.
4. Start or load a campaign.

---

## Configuration

Everything is configurable through MCM, including:

- manpower sizing/scaling/regen,
- war effects / delayed recovery / exhaustion / diplomacy,
- castle recruitment,
- combat realism and army economics,
- slave economy / governance / devastation,
- village and town investments,
- overlay behavior.

Includes a dedicated **Quick Settings** tab exposing major system toggles in grouped form.

---

## Compatibility

Campaign++ includes a runtime compatibility system that:

- scans Harmony overlap risk by method,
- checks model replacements at campaign load,
- groups and reports results in a startup popup and full MCM tab.

Known tested in project docs: Economy Overhaul, Cavalry Logistics Overhaul.

---

## Performance and engineering notes

- Heavy calculations are cached by in-game day where possible.
- Overlay uses staged cache refresh and low-allocation list patterns.
- Patches are null-safe and generally fail-open to vanilla behavior when context is unavailable.
- Save data is serialized via campaign `SyncData` for mid-campaign continuity.

---

## Project layout

- `Campaign/Behaviors` - campaign systems, state machines, daily/event loops
- `Campaign/Patches` - Harmony patches for model/action interception
- `Campaign/Settings` - MCM settings and settings migration profiles
- `UI` + `GUI/Prefabs` - overlay and runtime UI resources
- `_Module/ModuleData` - Bannerlord module data (items, strings, language packs)
- `Docs` - release notes and technical documentation maintained in-repo

---

## Documentation

Primary references:

- `Docs/Byzantium1071modexplanation.md`
- `Docs/PlayerGuide.md`
- `Docs/CHANGELOG.md`

---

## License (important)

This repository is **not MIT**.

It is distributed under a **custom non-commercial source-available license** in `LICENSE.txt`.

In short (see full license text for exact terms):

- no commercial use / monetization,
- no publishing or redistributing this work without prior written permission,
- no publishing derivatives without prior written permission,
- attribution is mandatory for any permitted publication.