# Headless checks for Campaign++

Run from the repository root. Requires Python 3.10+ and a .NET SDK capable of building the existing .NET 8 and .NET Framework 4.7.2 test projects. Game-backed checks additionally require Windows, the .NET Framework targeting/runtime prerequisites, and installed Bannerlord assemblies. The first run may restore NuGet packages.

```powershell
python Tests/modtest.py list
python Tests/modtest.py run all
python Tests/modtest.py run slave-transport
python Tests/modtest.py run castle-prisoners --days 1000 --seed 42
python Tests/modtest.py run service --days 1000 --seed 17
python Tests/modtest.py run manpower --days 1000 --seed 17
```

These commands build and execute tests; they do not start Bannerlord or deploy the mod. Every test build passes `-p:ModuleId=` to disable the module's automatic deployment target. Production gameplay code is not modified by the runner.

To run on a machine without Bannerlord:

```powershell
python Tests/modtest.py run all --layer rules
```

To select game-backed checks only, or override the installed game directory:

```powershell
python Tests/modtest.py run compatibility --layer game
python Tests/modtest.py run all --game-folder "D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"
```

The default is `--layer all`. A missing game installation is an **error**, not a silent fallback. Rules checks can still complete and appear in the report. A rules-only pass explicitly excludes game-backed verification. The game folder otherwise comes from the game test project's `GameFolder` property.

## Reports and failures

Each invocation creates a unique folder under `TestResults/headless/`, which is ignored by Git. Its path is printed at completion. Use `--output PATH` to choose a different parent directory.

- `report.md`: human-readable results, failure messages and scenario output.
- `report.json`: the same test results plus exact commands, input seed/day count and run start time.
- `rules/` and/or `game/`: original TRX test results and complete build/test console logs.

Exit code **0** means every requested layer executed tests and every reported test passed. Named selections additionally require at least one executed test for every configured class/method selector, so a partially missing selection cannot pass. Matching uses TRX method identities where available, allowing custom display names. Exit code **1** means a failure, skipped test, missing dependency/report, missing selected checks, an aborted/incomplete TRX run, a run-level TRX error or timeout. Run-level errors cannot be hidden by passing individual results; warning-only run diagnostics remain non-fatal in the original TRX/log. Invalid command-line arguments use exit code **2**. Ctrl+C during test execution uses exit code **130**, marks the current layer `CANCELLED`, retains completed layers in the report, and stops before starting another layer. The overall report remains `FAIL` because the requested checks did not finish. A build failure cannot reuse stale results: each invocation gets a new directory. `--timeout` bounds each test project invocation in seconds (default 600); timed-out test process trees are terminated. Console logs retain the evidence even if no TRX is produced.

The reports describe checks, not a percentage of gameplay covered. Passing selected checks is not the same as passing `all`, and a passing headless run is not an in-game compatibility certification.

## Configurable scenarios

`--days` accepts 1–10,000 and defaults to 365. `--seed` accepts 0–2,147,483,647 and defaults to 42. **These inputs affect the seven rules scenarios in `HeadlessScenarioTests` and the three managed castle checkpoint cases in `CastleConformityTests`.** Other regression/property tests keep their existing inputs and randomization. Direct `dotnet test` runs use the defaults unless `B1071_SCENARIO_DAYS` / `B1071_SCENARIO_SEED` are set.

Each configurable rules scenario executes twice from fresh local state and the same seed. It compares the daily traces, checks invariants daily, and records a SHA-256 trace fingerprint. The trace contains the state fields explicitly recorded by each fixture, not a dump of all game state. Failure messages include the day; the test output includes the seed and requested duration. Replay is checked on the same code/runtime, not guaranteed across framework versions or operating systems.

The managed castle scenario instead compares two real `TroopRoster` instances driven by the production behavior: uninterrupted execution versus `SyncData` save/restore and copied native roster entries every 1, 17 or 53 days. It varies arrivals, Leadership and recruitment, and empties rosters periodically to exercise removal/re-addition of troop types. It checks daily count/XP conservation, bounded conversion budgets, and equivalent roster state after checkpoints. A separate fixed-duration test interleaves two castles and compares their independent budgets against isolated behaviors, including a save/restore. The fixture substitutes character tier and conformity-cost lookups; a dictionary supplies `IDataStore`. These are managed state round trips, **not native campaign-file serialization**.

| Selection | Configurable scenario | Deliberately controlled inputs and limits |
|---|---|---|
| `castle-prisoners` | Repeated arrivals, shared conformity allocation and spending, fractional daily budget, prisoner/point conservation | Three sample conformity costs, generated Leadership and recruitment choices. Calls production conformity math; local arrays stand in for rosters. Existing game-backed tests separately check actual roster operations, deposits/withdrawals, migration and payments. No full castle AI or campaign scheduler. |
| `slave-transport` | Transfers among three inventories, market decay with fractional carry-over, additive food costs, bounded escort/overload factors | Local inventory counts, eligible food consumers, fixed test food/decay settings. No modeled market demand or simulated hourly escape scheduler. Existing game-backed tests separately cover exemptions, stashes, party capacity, escape logic and transfer warnings. |
| `revenue` | Investment cooldowns, reserve thresholds, overlapping bonuses and independent expiry; daily variations in basis and settings; finite bounded scales, monotonic town tariff payouts, 100% bypass | Generated revenue/prosperity inputs and curve settings; scripted investment decisions with a fixed external allowance. No world economy, clan spending or market transactions. Existing managed tests separately cover model patch integration. |
| `diplomacy` | Repeated crises, paid stabilization, timed relief, devastation and recovery to zero during peace | Generated strain/raid events, fixed external gold allowance and scripted stabilization choices. Production rules run; kingdom events, actual payments and AI scheduling do not. |
| `manpower` | Recruitment, seasonal recovery, siege/exhaustion pressure and periodic conquest; manpower accounting and a comparison against unstressed regeneration | Fixed pool capacity, generated settlement conditions and a scripted sequence of events. Calls production regeneration/retention/cost rules; does not run actual supply routing or recruitment handlers. |
| `service` | Individual recruits, seasonal thresholds, paid extensions, retirement, veteran returns and periodic service-row round trips | Scripted employer decisions, a fixed external gold allowance and no battle casualties. Uses production costs/thresholds/return rules and save-row helpers; verifies every history field survives the round trip. Does not run courier parties or the native campaign scheduler. |

A managed slave-transfer test also runs 365 steps for each of two fixed seeds (42 and 17). It moves goods between actual `ItemRoster` instances for party, stash and market, periodically undoes transfers, and changes the upkeep setting/master toggle. It checks total rations, native food modifier ordering, market-only labor counts and immediately updated prisoner capacity. Food postfixes are called directly; the capacity getter is Harmony-patched. The fixture supplies a fixed native capacity of 100 and substitutes the main-party lookup. This does not execute trading actions or the campaign scheduler. These fixed inputs are independent of `--days` and `--seed`.

Three additional fixed 365-day managed cases invoke the production settlement tick for two towns, comparing uninterrupted decay with `SyncData` checkpoints every 1, 17 or 53 days. An independent integer oracle checks decay at the fixture rate of 6.25% (1/16), including arrivals, empty markets, and disabled decay/economy settings. Stashes must remain unchanged. Checkpoint dictionaries are copied on both save and load to prevent shared references masking a failure; missing/null fields and the initial-stock seeding flag have separate checks. Town components and inventories are initialized by the fixture; category market pricing is absent and manumission is disabled. This verifies managed attrition/persistence, not market pricing, manpower routing or native save files.

Managed town/village investment cases invoke the production settlement tick for 120 fixed days, with overlapping bonuses and different tick schedules for two settlements. They compare durations and expiry against independent per-settlement tick counts, pause/resume feature settings, and restore detached `SyncData` dictionaries every 1 or 17 days. Missing and null save fields are checked separately. The fixture constructs managed settlement components and seeds saved investments; it does not run investment menus, payments, AI decisions or native save serialization. These cases are included in `revenue` and use fixed inputs independent of `--days` and `--seed`.

Managed service cases follow eight individual records through banking, partial restoration, promotion and a confirmed death. They check home, origin, employer, extension count and join day at each transition, including zero promotion credit and credit capped at the current day. Other employers cannot restore the records, transfers cannot duplicate them, and deaths do not create restorable reserves. These call production accounting helpers directly; battle/party event delivery remains an in-game check.

Managed devastation cases run 365 fixed days of repeated raids and recovery across two villages, comparing uninterrupted behavior with detached `SyncData` checkpoints every 1, 17 or 53 days. An independent integer half-point oracle checks the 100-point cap, recovery, village isolation and removal of fully healed entries. Additional cases cover all five village states and the first raid after missing/null save fields. The fixture sets village states directly to avoid native event dispatch, then invokes the mod handlers; engine raid scheduling, bound-village food penalties and native save files remain outside this check. These cases are in `diplomacy`, with fixed inputs independent of `--days` and `--seed`.

Managed governance cases couple the production strain and stabilization handlers over 120 fixed days, exercising both tick orders, independent feature toggles, immediate strain relief, bonus expiry and two settlements. Detached checkpoints every 1 or 17 days restore all five dictionaries; separate cases cover missing/null fields. Ownership-change tests cover every current reason, the strain cap, village exclusion and the disabled feature. The fixture supplies managed settlement components, pre-existing stabilization records and the active stabilization singleton. It does not execute purchases, menu actions, sieges or native event dispatch; checking both orders does not establish the engine's scheduling order. These cases are included in `diplomacy` and keep fixed inputs independent of `--days` and `--seed`.

Managed service persistence cases invoke the complete demobilization `SyncData` path for active service, employer-partitioned transfer reserves, veteran registers and pending recalls together. They compare every saved list across 1, 10 or 50 detached round trips, check that individual records remain separate, and verify legacy extension flags, clearing previously loaded state on missing/null fields, recall-header/batch alignment and player ownership derived from batches. Expiry cases exercise both cleanup methods at the last eligible day and the next day, including the reserve retention minimum, warning/extension settings, veteran retention clamping, independent buckets, repeated detached checkpoints and empty-bucket pruning. Restoration checks reject expired reserve records and preserve each eligible soldier's history without duplicating them. Gold, manpower, courier positions and next-order handles are checked as stored state, not as actual payments or travel. The fixture substitutes the campaign day and player-clan lookup; it does not run campaign save-file serialization, event dispatch or native object resolution. These fixed cases are included in `service`.

The runner calls production rules through the existing linked-source test project. It does not maintain a second implementation of mod rules or construct a fake complete Bannerlord campaign. The scenario driver supplies events and accounts for their results; those synthetic transitions are not evidence that the engine will deliver the same events.

## Feature coverage map

This map describes available checks and the remaining boundary. `all` runs both entire suites, including tests outside the named selections. A named selection is a focused subset, not complete coverage of that feature.

| Feature | Available headless checks / selection | Still requires the running game |
|---|---|---|
| Manpower, recovery, supply and recruitment gates | `manpower`: math, persistence helpers, multi-day recovery, selected AI recovery contracts | Actual settlement/party events, supply routing, player/AI recruitment across a campaign |
| Castle levy, prisoner conversion and consignment | `castle-prisoners`: conformity, save helpers, selected roster/payment/withdrawal behavior, multi-day managed checkpoints and independent castle budgets | Whole campaign arrival/recruitment ordering, menus, real save migration and UI interaction |
| Slave economy and transport | `slave-transport`: food/decay/speed math, selected capacity/food/escape patches, settlement decay checkpoints and party-screen contracts | Trading patterns, long-term AI purchasing/balance, native UI and event delivery |
| Revenue, recruitment prices and wages | `revenue`: economy/revenue math and managed regression/binding tests | Actual finance tick, interacting mods and emergent kingdom finances |
| Service, extensions and veteran recall | `service`: service math, scripted extension/return lifecycle, individual history through transfer/promotion/death, complete managed behavior save/load and service contracts | Full individual history over battles/transfers, courier parties and real campaign saves |
| War pressure, governance, devastation, clan survival | `diplomacy`: math, recovery/war scenarios, production village raid/recovery handlers, coupled strain/stabilization checkpoints, ownership-change gates and selected survival decisions | War declarations, forced peace ordering, kingdom destruction, diplomacy interactions |
| Town/village investment | `revenue`: investment math, budget/cooldown checks, production settlement ticks, independent expiry and managed save/load checkpoints | Menu actions, actual payments, ownership/siege changes and influence awards |
| Combat realism / tier armor | `all`: troop power and formula tests; `compatibility` includes patch signatures | Native battle/autoresolve integration and casualty balance |
| Frontier stipends and remaining formulas | `all`: available economy/formula checks | Behavior scheduling, recipient selection and campaign-wide gold accounting |
| Ledger, recruitment/service/selection UI, localization | `ui`: structural, binding, sorting and selected lifecycle checks | Rendering, scaling, clipping, focus/input, native crashes and gameplay interaction |
| Settings and game update contracts | `compatibility`: settings migration, module data, patch signatures and selected assembly contracts | Actual startup/load order, other mods, native/API behavior beyond pinned contracts |
| Persistence | `all`: available save helper and managed migration/round-trip tests | Native save/load of an entire campaign, mid-campaign install/removal |

## Maintaining the runner

```powershell
python -m unittest discover -s Tests -p test_modtest.py -v
```

These runner tests validate failure reporting, empty/malformed results, aborted run summaries, run-level errors versus warnings, incomplete result counts, partially missing selections, custom test display names, skipped tests, missing dependencies, unique report paths, argument bounds, catalog source names and Windows timeout/interrupt cleanup, cancellation reports and retention of completed layers. They do not start dotnet or the game. Their small diagnostic files are retained under ignored `TestResults/runner-tests/`.

Add new production-rule scenarios to the existing test projects. Add their class/method selectors to `SCENARIOS` in `Tests/modtest.py` when they belong in a focused selection. Keep `all` unfiltered so new tests cannot silently fall outside the full run. Avoid calling native engine operations from game-backed fixtures; explicitly document every substituted engine dependency.
