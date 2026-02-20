using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.UI
{
    internal enum B1071LedgerTab
    {
        Current = 0,
        NearbyPools = 1,
        Castles = 2,
        Towns = 3,
        Villages = 4,
        Factions = 5,
        Armies = 6,
        Wars = 7
    }

    internal static class B1071_OverlayController
    {
        private static bool _isVisible = true;
        private static bool _isExpanded = true;
        private static bool _panelModeActive;
        private static bool _ledgerInitialized;
        private static float _refreshTimer;
        private static string _lastText = string.Empty;
        private static string _currentText = "Loading ledger...\n[Press M to toggle]";

        // Column data for widget-based layout
        private static string _titleText = "Loading...";
        private static string _totals1 = string.Empty;
        private static string _totals2 = string.Empty;
        private static string _totals3 = string.Empty;
        private static string _totals4 = string.Empty;
        private static string _header1 = string.Empty;
        private static string _header2 = string.Empty;
        private static string _header3 = string.Empty;
        private static string _header4 = string.Empty;

        // Row-based data for the UI — replaces the old 4x StringBuilder text-blob approach.
        private static readonly MBBindingList<B1071_LedgerRowVM> _ledgerRows = new MBBindingList<B1071_LedgerRowVM>();

        private static B1071LedgerTab _activeTab = B1071LedgerTab.Current;
        private static int _pageIndex;
        private static int _sortColumn;
        private static bool _sortAscending;
        private static string _pageLabel = "Page 1/1";

        // Row data cache — rebuilt once per in-game day, not per frame.
        private static List<LedgerRow>? _cachedRows;
        private static List<LedgerRow>? _cachedVillageRows;
        private static List<ArmiesLedgerRow>? _cachedArmiesRows;
        private static bool _cacheStale = true;
        private static bool _armiesCacheStale = true;

        // Scratch lists reused each refresh to avoid GC pressure.
        private static List<LedgerRow>? _scratchRows;
        private static List<LedgerRow>? _scratchVillageRows;
        private static List<LedgerRow>? _scratchCombined;

        // Party position tracking for dirty-flag distance recalculation.
        private static Vec2 _lastPartyPos;
        private static bool _distancesDirty = true;

        // Tiered distance updates: top N closest updated every 2s, rest on daily tick / every 30s.
        private const int NEAR_SET_SIZE = 30;
        private const int FULL_DISTANCE_EVERY_N = 15; // 15 × 2s = 30 real seconds
        private static int _partialDistanceCount;
        private static List<LedgerRow>? _nearSet;

        // View dirty flag — set when data changes so the UI mixin only syncs when needed.
        private static bool _viewDirty = true;
        private static string _sortTextCached = string.Empty;

        // Current-tab context cache: avoids rebuilding every 2s when selection is unchanged.
        private static string _lastCurrentContextSettlementId = string.Empty;

        // Columns dirty — set on daily tick or user action (tab/sort/page).
        // Non-Nearby tabs only rebuild when this is true.
        private static bool _columnsDirty = true;

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        /// <summary>
        /// Resets all static overlay state. Call on session launch / unload
        /// to prevent stale data from a previous save leaking into the new one.
        /// </summary>
        internal static void Reset()
        {
            B1071_PanelInjectionGuard.Reset();
            _isVisible = true;
            _isExpanded = true;
            _panelModeActive = false;
            _ledgerInitialized = false;
            _refreshTimer = 0f;
            _lastText = string.Empty;
            _currentText = "Loading ledger...\n[Press M to toggle]";
            _titleText = "Loading...";
            _ledgerRows.Clear();
            _totals1 = string.Empty;
            _totals2 = string.Empty;
            _totals3 = string.Empty;
            _totals4 = string.Empty;
            _header1 = string.Empty;
            _header2 = string.Empty;
            _header3 = string.Empty;
            _header4 = string.Empty;
            _activeTab = B1071LedgerTab.Current;
            _pageIndex = 0;
            _sortColumn = 0;
            _sortAscending = false;
            _pageLabel = "Page 1/1";
            _cachedRows = null;
            _cachedVillageRows = null;
            _cachedArmiesRows = null;
            _cacheStale = true;
            _armiesCacheStale = true;
            _scratchRows = null;
            _scratchVillageRows = null;
            _scratchCombined = null;
            _lastPartyPos = default;
            _distancesDirty = true;
            _partialDistanceCount = 0;
            _nearSet = null;
            _viewDirty = true;
            _columnsDirty = true;
            _sortTextCached = string.Empty;
            _lastCurrentContextSettlementId = string.Empty;
        }

        internal static bool IsVisible => _isVisible;
        internal static bool IsExpanded => _isExpanded;
        internal static string CurrentText => _currentText;
        internal static string TitleText => _titleText;
        internal static MBBindingList<B1071_LedgerRowVM> LedgerRows => _ledgerRows;
        internal static string Totals1 => _totals1;
        internal static string Totals2 => _totals2;
        internal static string Totals3 => _totals3;
        internal static string Totals4 => _totals4;
        internal static string Header1 => _header1;
        internal static string Header2 => _header2;
        internal static string Header3 => _header3;
        internal static string Header4 => _header4;
        internal static string TabCurrentText => FormatTabText("Current", _activeTab == B1071LedgerTab.Current);
        internal static string TabNearbyText => FormatTabText("Nearby", _activeTab == B1071LedgerTab.NearbyPools);
        internal static string TabCastlesText => FormatTabText("Castles", _activeTab == B1071LedgerTab.Castles);
        internal static string TabTownsText => FormatTabText("Towns", _activeTab == B1071LedgerTab.Towns);
        internal static string TabVillagesText => FormatTabText("Villages", _activeTab == B1071LedgerTab.Villages);
        internal static string TabFactionsText => FormatTabText("Factions", _activeTab == B1071LedgerTab.Factions);
        internal static string TabArmiesText => FormatTabText("Armies", _activeTab == B1071LedgerTab.Armies);
        internal static string TabWarsText => FormatTabText("Wars", _activeTab == B1071LedgerTab.Wars);
        internal static bool IsTabCurrentActive => _activeTab == B1071LedgerTab.Current;
        internal static bool IsTabNearbyActive => _activeTab == B1071LedgerTab.NearbyPools;
        internal static bool IsTabCastlesActive => _activeTab == B1071LedgerTab.Castles;
        internal static bool IsTabTownsActive => _activeTab == B1071LedgerTab.Towns;
        internal static bool IsTabVillagesActive => _activeTab == B1071LedgerTab.Villages;
        internal static bool IsTabFactionsActive => _activeTab == B1071LedgerTab.Factions;
        internal static bool IsTabArmiesActive => _activeTab == B1071LedgerTab.Armies;
        internal static bool IsTabWarsActive => _activeTab == B1071LedgerTab.Wars;
        private static readonly string[][] _sortKeys = new[]
        {
            new[] { "MP", "Regen", "Pool", "Name" },
            new[] { "Dist", "MP", "%", "Name" },
            new[] { "MP", "Prosp", "Regen", "Name" },
            new[] { "MP", "Prosp", "Regen", "Name" },
            new[] { "Hearth", "Fact", "Bound", "Name" },
            new[] { "Prosp", "MP", "Money", "Name" },
            new[] { "Power", "Troops", "Exhaust", "Name" },
            new[] { "Peace", "Exhaust", "Status", "Pair" }
        };

        // Inverse of the per-tab sortToHeader arrays used in each Build*Columns method.
        // Maps header position (0-based: H1=0, H2=1, H3=2, H4=3) → _sortColumn index.
        private static readonly int[][] _headerToSort = new[]
        {
            new[] { 3, 0, 1, 2 },  // Current:     H1→Name(3), H2→MP(0), H3→Regen(1), H4→Pool(2)
            new[] { 3, 1, 2, 0 },  // NearbyPools: H1→Name(3), H2→MP(1), H3→%(2), H4→Dist(0)
            new[] { 3, 0, 1, 2 },  // Castles:     H1→Name(3), H2→MP(0), H3→Prosp(1), H4→Regen(2)
            new[] { 3, 0, 1, 2 },  // Towns:       same as Castles
            new[] { 3, 0, 1, 2 },  // Villages:    H1→Name(3), H2→Hearth(0), H3→Fact(1), H4→Bound(2)
            new[] { 3, 1, 0, 2 },  // Factions:    H1→Name(3), H2→MP(1), H3→Prosp(0), H4→Money(2)
            new[] { 3, 0, 1, 2 },  // Armies:      H1→Name(3), H2→Power(0), H3→Troops(1), H4→Exhaust(2)
            new[] { 3, 1, 0, 2 }   // Wars:        H1→Pair(3), H2→Exhaust(1), H3→Peace(0), H4→Status(2)
        };

        internal static string SortText => _sortTextCached;
        internal static string PageText => _pageLabel;

        internal static void SetPanelMode(bool active)
        {
            _panelModeActive = active;
            if (active)
                MBInformationManager.HideInformations();
        }

        internal static void ToggleExpanded()
        {
            _isExpanded = !_isExpanded;
            _viewDirty = true;
        }

        internal static void SetLedgerTab(B1071LedgerTab tab)
        {
            if (_activeTab == tab)
                return;

            _activeTab = tab;
            _pageIndex = 0;
            _sortColumn = 0;
            _sortAscending = false;
            UpdateSortTextCache();
            ForceRefresh();
        }

        /// <summary>
        /// Called by the behavior on daily tick so the overlay rebuilds
        /// settlement data from scratch instead of every 0.35 s.
        /// </summary>
        internal static void MarkCacheStale()
        {
            _cacheStale = true;
            _armiesCacheStale = true;
            _distancesDirty = true;
            _columnsDirty = true;
        }

        internal static void NextPage()
        {
            _pageIndex++;
            ForceRefresh();
        }

        internal static void PreviousPage()
        {
            if (_pageIndex > 0)
            {
                _pageIndex--;
                ForceRefresh();
            }
        }

        /// <summary>
        /// Sort by clicking a column header (1-based: 1=H1, 2=H2, 3=H3, 4=H4).
        /// If already sorting on that column, toggles asc/desc.
        /// If switching to a new column, defaults to descending.
        /// </summary>
        internal static void SortByHeader(int headerIndex)
        {
            int tab = (int)_activeTab;
            if (tab < 0 || tab >= _headerToSort.Length) return;

            int h = Math.Max(0, Math.Min(3, headerIndex - 1)); // 1-based → 0-based
            int newSortCol = _headerToSort[tab][h];

            if (newSortCol == _sortColumn)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = newSortCol;
                _sortAscending = false; // default descending for new column
            }

            _pageIndex = 0;
            UpdateSortTextCache();
            ForceRefresh();
        }

        internal static void RefreshNow()
        {
            EnsureLedgerInitialized();
            _currentText = BuildOverlayText();
            _lastText = _currentText;
            _viewDirty = true;
        }

        internal static void Tick(float dt)
        {
            if (!IsCampaignMapReady())
            {
                HideOverlayIfVisible();
                return;
            }

            if (!Settings.EnableOverlay)
            {
                HideOverlayIfVisible();
                return;
            }

            EnsureLedgerInitialized();

            if (Settings.EnableOverlayHotkey && Input.IsKeyPressed(GetConfiguredHotkey()))
            {
                ToggleVisibility();
            }

            if (!_isVisible || !_isExpanded)
                return;

            _refreshTimer -= dt;
            if (_refreshTimer > 0f)
                return;

            _refreshTimer = 2.0f;

            // Nearby tab: always rebuild (distance updates every 2s).
            // Current tab: rebuild only when settlement context changes, or when forced dirty.
            // Other tabs: rebuild only when data is dirty.
            if (_activeTab == B1071LedgerTab.Current)
            {
                Settlement? contextSettlement = ResolveCurrentContextSettlement();
                string currentContextId = contextSettlement?.StringId ?? string.Empty;

                if (!_columnsDirty && string.Equals(currentContextId, _lastCurrentContextSettlementId, StringComparison.Ordinal))
                    return;

                _lastCurrentContextSettlementId = currentContextId;
            }
            else if (_activeTab != B1071LedgerTab.NearbyPools && _activeTab != B1071LedgerTab.Wars && !_columnsDirty)
            {
                return;
            }

            _columnsDirty = false;
            string text = BuildOverlayText();
            _lastText = text;
            _currentText = text;
            _viewDirty = true;
            if (!_panelModeActive)
                MBInformationManager.ShowHint("Byzantium 1071 Overlay\n" + text);
        }

        private static InputKey GetConfiguredHotkey()
        {
            switch (Settings.OverlayHotkeyChoice)
            {
                case 1: return InputKey.N;
                case 2: return InputKey.K;
                case 3: return InputKey.F9;
                case 4: return InputKey.F10;
                case 5: return InputKey.F11;
                case 6: return InputKey.F12;
                default: return InputKey.M;
            }
        }

        /// <summary>
        /// Returns true once when view data has changed, then false until next update.
        /// Called by the UI mixin to avoid redundant property syncs every frame.
        /// </summary>
        internal static bool ConsumeViewDirty()
        {
            if (!_viewDirty) return false;
            _viewDirty = false;
            return true;
        }

        private static void UpdateSortTextCache()
        {
            int tab = (int)_activeTab;
            int col = Math.Min(_sortColumn, _sortKeys[tab].Length - 1);
            string arrow = _sortAscending ? " \u2191" : " \u2193";
            _sortTextCached = _sortKeys[tab][col] + arrow;
        }

        internal static void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            _refreshTimer = 0f;
            _viewDirty = true;

            if (_isVisible)
            {
                _lastText = string.Empty;
                _currentText = BuildOverlayText();
                if (!_panelModeActive)
                    MBInformationManager.ShowHint("Byzantium 1071 Overlay\n" + _currentText);
            }
            else if (!_panelModeActive)
            {
                MBInformationManager.HideInformations();
            }
        }

        private static void HideOverlayIfVisible()
        {
            if (!_isVisible)
                return;

            _isVisible = false;
            _refreshTimer = 0f;
            _lastText = string.Empty;
            if (!_panelModeActive)
                MBInformationManager.HideInformations();
        }

        private static string BuildOverlayText()
        {
            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            if (behavior == null)
            {
                ClearColumns("Manpower behavior unavailable.");
                return "Manpower behavior unavailable.";
            }

            switch (_activeTab)
            {
                case B1071LedgerTab.Current: return BuildCurrentColumns(behavior);
                case B1071LedgerTab.NearbyPools: return BuildNearbyPoolsColumns(behavior);
                case B1071LedgerTab.Castles: return BuildCastlesColumns(behavior);
                case B1071LedgerTab.Towns: return BuildTownsColumns(behavior);
                case B1071LedgerTab.Factions: return BuildFactionsColumns(behavior);
                case B1071LedgerTab.Villages: return BuildVillagesColumns(behavior);
                case B1071LedgerTab.Armies: return BuildArmiesColumns();
                case B1071LedgerTab.Wars: return BuildWarsColumns(behavior);
                default: return BuildCurrentColumns(behavior);
            }
        }

        private static string BuildCurrentColumns(B1071_ManpowerBehavior behavior)
        {
            Settlement? settlement = ResolveCurrentContextSettlement();
            if (settlement == null || settlement.IsHideout)
            {
                ClearColumns("Current - No settlement selected.");
                _titleText = "Current Settlement";
                _header1 = "Settlement";
                _header2 = "Manpower";
                _header3 = "Regen/Day";
                _header4 = "Pool";
                _ledgerRows.Add(new B1071_LedgerRowVM(
                    "Left-click a settlement to view its details.",
                    "",
                    "",
                    "",
                    false,
                    true));
                _totals1 = "Selected";
                _totals2 = "-";
                _totals3 = "-";
                _totals4 = "-";
                _pageLabel = "Page 1/1";
                return _titleText;
            }

            behavior.GetManpowerPool(settlement, out int current, out int maximum, out Settlement pool);
            int ratio = maximum <= 0 ? 0 : (int)((100f * current) / maximum);
            int dailyRegen = pool != null ? B1071_ManpowerBehavior.GetDailyRegen(pool, maximum) : 0;
            string type = settlement.IsTown ? "Town" : (settlement.IsCastle ? "Castle" : (settlement.IsVillage ? "Village" : "Settlement"));

            _titleText = "Current Settlement";
            _header1 = "Settlement";
            _header2 = "Manpower";
            _header3 = "Regen/Day";
            _header4 = "Pool";
            ApplySortIndicator(new[] { 2, 3, 4, 1 });

            _ledgerRows.Clear();
            _ledgerRows.Add(new B1071_LedgerRowVM(
                TruncateForColumn(settlement.Name.ToString() + " (" + type + ")", 32),
                FormatMp(current, maximum) + " (" + ratio + "%)",
                "+" + dailyRegen.ToString("N0") + "/d",
                pool?.Name?.ToString() ?? "-",
                true,
                true));

            _totals1 = "Selected";
            _totals2 = FormatMp(current, maximum);
            _totals3 = "+" + dailyRegen.ToString("N0") + "/d";
            _totals4 = pool?.Name?.ToString() ?? "-";
            _pageLabel = "Page 1/1";
            return _titleText;
        }

        private static Settlement? ResolveCurrentContextSettlement()
        {
            Settlement? settlement = Settlement.CurrentSettlement
                                     ?? Hero.MainHero?.CurrentSettlement
                                     ?? MobileParty.MainParty?.CurrentSettlement;
            if (settlement != null)
                return settlement;

            return MobileParty.MainParty?.TargetSettlement;
        }

        private static void ClearColumns(string fallback)
        {
            _titleText = fallback;
            _ledgerRows.Clear();
            _totals1 = string.Empty;
            _totals2 = string.Empty;
            _totals3 = string.Empty;
            _totals4 = string.Empty;
            _header1 = string.Empty;
            _header2 = string.Empty;
            _header3 = string.Empty;
            _header4 = string.Empty;
        }

        private static string BuildNearbyPoolsColumns(B1071_ManpowerBehavior behavior)
        {
            List<LedgerRow> rows = BuildSettlementRows(behavior, includeVillages: false);
            UpdateDistancesIfNeeded();
            rows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => a.Current.CompareTo(b.Current),
                    2 => a.RatioPercent.CompareTo(b.RatioPercent),
                    3 => string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal),
                    _ => a.DistanceSq.CompareTo(b.DistanceSq)
                };
                if (!_sortAscending && _sortColumn != 3) compare = -compare;
                if (_sortColumn == 3 && _sortAscending) compare = -compare;
                if (compare != 0) return compare;
                return _sortColumn == 0 ? string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal)
                    : a.DistanceSq.CompareTo(b.DistanceSq);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(rows.Count, pageSize);
            int endIndex = Math.Min(rows.Count, startIndex + pageSize);

            if (rows.Count == 0)
            {
                ClearColumns("Nearby Pools - No entries found.");
                return "Nearby Pools\nNo entries found.";
            }

            string playerFaction = GetPlayerFactionName();
            int totalCurrent = 0, totalMaximum = 0;
            foreach (LedgerRow r in rows) { totalCurrent += r.Current; totalMaximum += r.Maximum; }

            _titleText = "Nearby Pools  (" + rows.Count + " entries)";
            _header1 = "Settlement";
            _header2 = "Manpower";
            _header3 = "%";
            _header4 = "Distance";
            ApplySortIndicator(new[] { 4, 2, 3, 1 });

            PopulateRows(startIndex, endIndex, rows, playerFaction, (row, rank, prefix) =>
            {
                float dist = (float)Math.Sqrt(row.DistanceSq);
                return (
                    prefix + rank + ". " + TruncateForColumn(row.SettlementName, 24),
                    FormatMp(row.Current, row.Maximum),
                    row.RatioPercent + "% " + row.Trend,
                    dist.ToString("F1") + " km"
                );
            });

            SetTotals(totalCurrent, totalMaximum);
            return _titleText;
        }

        private static string BuildCastlesColumns(B1071_ManpowerBehavior behavior)
        {
            return BuildSettlementTypeColumns(behavior, "Castle", "Castle Ledger");
        }

        private static string BuildTownsColumns(B1071_ManpowerBehavior behavior)
        {
            return BuildSettlementTypeColumns(behavior, "Town", "Town Ledger");
        }

        private static string BuildSettlementTypeColumns(B1071_ManpowerBehavior behavior, string typeFilter, string title)
        {
            List<LedgerRow> allRows = BuildSettlementRows(behavior, includeVillages: false);
            var rows = new List<LedgerRow>();
            foreach (LedgerRow r in allRows)
            {
                if (r.Type == typeFilter) rows.Add(r);
            }

            rows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => a.Prosperity.CompareTo(b.Prosperity),
                    2 => a.DailyRegen.CompareTo(b.DailyRegen),
                    3 => string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal),
                    _ => a.Current.CompareTo(b.Current)
                };
                if (_sortColumn != 3) { if (!_sortAscending) compare = -compare; }
                else { if (_sortAscending) compare = -compare; }
                if (compare != 0) return compare;
                return string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(rows.Count, pageSize);
            int endIndex = Math.Min(rows.Count, startIndex + pageSize);

            if (rows.Count == 0)
            {
                ClearColumns(title + " - No entries found.");
                return title + "\nNo entries found.";
            }

            string playerFaction = GetPlayerFactionName();
            int totalCurrent = 0, totalMaximum = 0;
            foreach (LedgerRow r in rows) { totalCurrent += r.Current; totalMaximum += r.Maximum; }

            _titleText = title + "  (" + rows.Count + " entries)";
            _header1 = typeFilter;
            _header2 = "Manpower";
            _header3 = "Prosperity";
            _header4 = "Regen/Day";
            ApplySortIndicator(new[] { 2, 3, 4, 1 });

            PopulateRows(startIndex, endIndex, rows, playerFaction, (row, rank, prefix) =>
            {
                return (
                    prefix + rank + ". " + TruncateForColumn(row.SettlementName, 24),
                    FormatMp(row.Current, row.Maximum),
                    row.Prosperity.ToString("N0"),
                    "+" + row.DailyRegen.ToString("N0") + "/d"
                );
            });

            SetTotals(totalCurrent, totalMaximum);
            return _titleText;
        }

        private static string BuildVillagesColumns(B1071_ManpowerBehavior behavior)
        {
            List<LedgerRow> rows = GetVillageRows(behavior);
            rows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => string.Compare(a.FactionName, b.FactionName, StringComparison.Ordinal),
                    2 => string.Compare(a.BoundTo, b.BoundTo, StringComparison.Ordinal),
                    3 => string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal),
                    _ => a.Hearth.CompareTo(b.Hearth)
                };
                if (_sortColumn == 0) { if (!_sortAscending) compare = -compare; }
                else { if (_sortAscending) compare = -compare; }
                if (compare != 0) return compare;
                return string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(rows.Count, pageSize);
            int endIndex = Math.Min(rows.Count, startIndex + pageSize);

            if (rows.Count == 0)
            {
                ClearColumns("Village Ledger - No entries found.");
                return "Village Ledger\nNo entries found.";
            }

            string playerFaction = GetPlayerFactionName();
            int totalHearth = 0;
            foreach (LedgerRow r in rows) { totalHearth += r.Hearth; }

            _titleText = "Village Ledger  (" + rows.Count + " entries)";
            _header1 = "Village";
            _header2 = "Hearth";
            _header3 = "Faction";
            _header4 = "Bound To";
            ApplySortIndicator(new[] { 2, 3, 4, 1 });

            PopulateRows(startIndex, endIndex, rows, playerFaction, (row, rank, prefix) =>
            {
                return (
                    prefix + rank + ". " + TruncateForColumn(row.SettlementName, 24),
                    row.Hearth.ToString("N0"),
                    TruncateForColumn(row.FactionName, 18),
                    TruncateForColumn(row.BoundTo, 18)
                );
            });

            _totals1 = "Total";
            _totals2 = totalHearth.ToString("N0");
            _totals3 = string.Empty;
            _totals4 = string.Empty;
            return _titleText;
        }

        private static string BuildFactionsColumns(B1071_ManpowerBehavior behavior)
        {
            List<LedgerRow> rows = BuildSettlementRows(behavior, includeVillages: false);
            var byFaction = new Dictionary<string, FactionLedgerRow>();
            var kingdomGoldById = new Dictionary<string, int>();

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId))
                    continue;

                int kingdomClanGold = 0;
                foreach (Clan clan in kingdom.Clans)
                {
                    if (clan == null)
                        continue;

                    kingdomClanGold += clan.Gold;
                }

                kingdomGoldById[kingdom.StringId] = kingdomClanGold;
            }

            foreach (LedgerRow row in rows)
            {
                string key = string.IsNullOrEmpty(row.FactionName) ? "Independent" : row.FactionName;
                if (!byFaction.TryGetValue(key, out FactionLedgerRow factionRow))
                {
                    factionRow = new FactionLedgerRow { Name = key };
                    byFaction[key] = factionRow;
                }
                factionRow.Current += row.Current;
                factionRow.Maximum += row.Maximum;
                factionRow.Settlements += 1;
                factionRow.TotalDailyRegen += row.DailyRegen;
                if (row.Type != "Village")
                    factionRow.TotalProsperity += row.Prosperity;

                // Capture kingdom StringId for exhaustion lookup (first settlement wins).
                if (string.IsNullOrEmpty(factionRow.KingdomId) && !string.IsNullOrEmpty(row.KingdomId))
                    factionRow.KingdomId = row.KingdomId;
            }

            // Populate exhaustion and money.
            foreach (FactionLedgerRow fr in byFaction.Values)
            {
                if (!string.IsNullOrEmpty(fr.KingdomId) && behavior != null)
                    fr.Exhaustion = behavior.GetWarExhaustion(fr.KingdomId);

                if (!string.IsNullOrEmpty(fr.KingdomId) && kingdomGoldById.TryGetValue(fr.KingdomId, out int kingdomGold))
                {
                    fr.HasMoney = true;
                    fr.Money = kingdomGold;
                }
            }

            var factionRows = new List<FactionLedgerRow>(byFaction.Values);
            factionRows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => a.Current.CompareTo(b.Current),
                    2 => a.Money.CompareTo(b.Money),
                    3 => string.Compare(a.Name, b.Name, StringComparison.Ordinal),
                    _ => a.TotalProsperity.CompareTo(b.TotalProsperity)
                };
                if (_sortColumn != 3) { if (!_sortAscending) compare = -compare; }
                else { if (_sortAscending) compare = -compare; }
                if (compare != 0) return compare;
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(factionRows.Count, pageSize);
            int endIndex = Math.Min(factionRows.Count, startIndex + pageSize);

            if (factionRows.Count == 0)
            {
                ClearColumns("Faction Ledger - No entries found.");
                return "Faction Ledger\nNo entries found.";
            }

            string playerFaction = GetPlayerFactionName();
            int totalCurrent = 0, totalMaximum = 0;
            foreach (FactionLedgerRow r in factionRows) { totalCurrent += r.Current; totalMaximum += r.Maximum; }

            _titleText = "Faction Ledger  (" + factionRows.Count + " entries)";
            _header1 = "Faction";
            _header2 = "Manpower";
            _header3 = "Prosperity";
            _header4 = "Money";
            ApplySortIndicator(new[] { 3, 2, 4, 1 });

            _ledgerRows.Clear();
            // Iterate in reverse: Bannerlord's VerticalTopToBottom renders first child at bottom.
            for (int i = endIndex - 1; i >= startIndex; i--)
            {
                FactionLedgerRow row = factionRows[i];
                int rank = i + 1;
                string prefix = row.Name == playerFaction ? "> " : "";
                bool highlight = row.Name == playerFaction;
                bool even = (i - startIndex) % 2 == 0;

                _ledgerRows.Add(new B1071_LedgerRowVM(
                    prefix + rank + ". " + TruncateForColumn(row.Name, 24),
                    FormatMp(row.Current, row.Maximum),
                    row.TotalProsperity.ToString("N0"),
                    row.HasMoney ? row.Money.ToString("N0") : "N/A",
                    highlight, even));
            }

            int totalProsperity = 0;
            int totalMoney = 0;
            foreach (FactionLedgerRow r in factionRows) totalProsperity += r.TotalProsperity;
            foreach (FactionLedgerRow r in factionRows) if (r.HasMoney) totalMoney += r.Money;
            SetTotals(totalCurrent, totalMaximum);
            _totals3 = totalProsperity.ToString("N0");
            _totals4 = totalMoney.ToString("N0");
            return _titleText;
        }

        private static void SetTotals(int totalCurrent, int totalMaximum, string col4Value = "")
        {
            int totalRatio = totalMaximum <= 0 ? 0 : (int)((100f * totalCurrent) / totalMaximum);
            _totals1 = "Total";
            _totals2 = FormatMp(totalCurrent, totalMaximum);
            _totals3 = totalRatio + "%";
            _totals4 = col4Value;
        }

        private static void ApplySortIndicator(int[] sortToHeader)
        {
            string arrow = _sortAscending ? " \u2191" : " \u2193";
            int h = sortToHeader[_sortColumn];
            if (h == 1) _header1 += arrow;
            else if (h == 2) _header2 += arrow;
            else if (h == 3) _header3 += arrow;
            else if (h == 4) _header4 += arrow;
        }

        private static string GetPlayerFactionName()
        {
            try
            {
                IFaction? playerFaction = Hero.MainHero?.MapFaction;
                return playerFaction?.Name?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Populates _ledgerRows from a slice of LedgerRow data using a cell formatter delegate.
        /// </summary>
        private static void PopulateRows(int startIndex, int endIndex, List<LedgerRow> rows,
            string playerFaction, Func<LedgerRow, int, string, (string c1, string c2, string c3, string c4)> formatter)
        {
            _ledgerRows.Clear();
            // Iterate in reverse: Bannerlord's VerticalTopToBottom renders first child at bottom.
            for (int i = endIndex - 1; i >= startIndex; i--)
            {
                LedgerRow row = rows[i];
                int rank = i + 1;
                string prefix = row.FactionName == playerFaction ? "> " : "";
                bool highlight = row.FactionName == playerFaction;
                bool even = (i - startIndex) % 2 == 0;

                var (c1, c2, c3, c4) = formatter(row, rank, prefix);
                _ledgerRows.Add(new B1071_LedgerRowVM(c1, c2, c3, c4, highlight, even));
            }
        }

        private static string FormatMp(int current, int maximum)
        {
            return current.ToString("N0") + "/" + maximum.ToString("N0");
        }

        private static string TruncateForColumn(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            if (maxLength <= 1)
                return text.Substring(0, maxLength);

            return text.Substring(0, maxLength - 1) + "…";
        }
        private static string GetExhaustionLabel(float exhaustion)
        {
            if (exhaustion <= 0f) return "Fresh";
            if (exhaustion < 25f) return "Strained (" + (int)exhaustion + ")";
            if (exhaustion < 50f) return "Tired (" + (int)exhaustion + ")";
            if (exhaustion < 75f) return "Exhausted (" + (int)exhaustion + ")";
            return "Crisis (" + (int)exhaustion + ")";
        }
        private static List<LedgerRow> BuildSettlementRows(B1071_ManpowerBehavior behavior, bool includeVillages)
        {
            // Rebuild cache once per in-game day (or on first call).
            if (_cacheStale || _cachedRows == null || _cachedVillageRows == null)
            {
                RebuildCache(behavior);
                _cacheStale = false;
                _distancesDirty = true; // force distance refresh after cache rebuild
            }

            // Return a scratch-list copy so callers can sort without disturbing the cache.
            if (!includeVillages)
            {
                if (_scratchRows == null) _scratchRows = new List<LedgerRow>(_cachedRows!.Count);
                _scratchRows.Clear();
                _scratchRows.AddRange(_cachedRows!);
                return _scratchRows;
            }

            if (_scratchCombined == null) _scratchCombined = new List<LedgerRow>(_cachedRows!.Count + _cachedVillageRows!.Count);
            _scratchCombined.Clear();
            _scratchCombined.AddRange(_cachedRows!);
            _scratchCombined.AddRange(_cachedVillageRows!);
            return _scratchCombined;
        }

        private static List<LedgerRow> GetVillageRows(B1071_ManpowerBehavior behavior)
        {
            if (_cacheStale || _cachedVillageRows == null)
            {
                RebuildCache(behavior);
                _cacheStale = false;
                _distancesDirty = true;
            }

            if (_scratchVillageRows == null) _scratchVillageRows = new List<LedgerRow>(_cachedVillageRows!.Count);
            _scratchVillageRows.Clear();
            _scratchVillageRows.AddRange(_cachedVillageRows!);
            return _scratchVillageRows;
        }

        /// <summary>
        /// Tiered distance recalculation:
        /// - Full recalc (all ~200 towns/castles): on daily tick or every ~30 real seconds.
        /// - Partial recalc (top 30 closest): every 2-second refresh when party moves.
        /// - Skipped entirely when the Nearby tab is not active.
        /// </summary>
        private static void UpdateDistancesIfNeeded()
        {
            if (_cachedRows == null || _cachedRows.Count == 0) return;

            MobileParty? mainParty = MobileParty.MainParty;
            if (mainParty == null)
            {
                if (_distancesDirty)
                {
                    foreach (LedgerRow r in _cachedRows) { r.DistanceSq = 0f; r.Distance = 0f; }
                    _distancesDirty = false;
                    _nearSet?.Clear();
                }
                return;
            }

            Vec2 partyPos = mainParty.GetPosition2D;
            float delta = (partyPos - _lastPartyPos).LengthSquared;

            _partialDistanceCount++;
            bool fullRecalc = _distancesDirty || _partialDistanceCount >= FULL_DISTANCE_EVERY_N;

            if (fullRecalc)
            {
                _lastPartyPos = partyPos;
                _distancesDirty = false;
                _partialDistanceCount = 0;

                foreach (LedgerRow r in _cachedRows)
                    r.DistanceSq = (r._position - partyPos).LengthSquared;

                RebuildNearSet();
                return;
            }

            // Partial update: skip if barely moved.
            if (delta < 0.0625f) return;

            _lastPartyPos = partyPos;

            // Only update near set distances.
            if (_nearSet != null)
            {
                foreach (LedgerRow r in _nearSet)
                    r.DistanceSq = (r._position - partyPos).LengthSquared;
            }
        }

        /// <summary>
        /// Selects the top NEAR_SET_SIZE closest settlements from _cachedRows
        /// so that only these get frequent distance updates.
        /// </summary>
        private static void RebuildNearSet()
        {
            if (_cachedRows == null || _cachedRows.Count == 0) return;

            if (_nearSet == null) _nearSet = new List<LedgerRow>(NEAR_SET_SIZE);
            _nearSet.Clear();

            if (_cachedRows.Count <= NEAR_SET_SIZE)
            {
                _nearSet.AddRange(_cachedRows);
                return;
            }

            // Copy, sort by current DistanceSq, take top N.
            _nearSet.AddRange(_cachedRows);
            _nearSet.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
            _nearSet.RemoveRange(NEAR_SET_SIZE, _nearSet.Count - NEAR_SET_SIZE);
        }

        private static void RebuildCache(B1071_ManpowerBehavior behavior)
        {
            var towns = new List<LedgerRow>();
            var villages = new List<LedgerRow>();
            var regenByPool = new Dictionary<string, int>();   // poolId → cached regen

            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null || settlement.IsHideout)
                    continue;

                if (!settlement.IsTown && !settlement.IsCastle && !settlement.IsVillage)
                    continue;

                behavior.GetManpowerPool(settlement, out int current, out int maximum, out Settlement pool);

                Town? town = settlement.Town;
                float prosperity = town != null ? town.Prosperity : 0f;
                float security = town != null ? town.Security : 0f;
                float hearth = settlement.IsVillage && settlement.Village != null ? settlement.Village.Hearth : 0f;
                int ratio = maximum <= 0 ? 0 : (int)((100f * current) / maximum);

                // Cache GetDailyRegen per pool so villages sharing a pool
                // don't redundantly recalculate the heavy regen formula.
                string poolId = pool.StringId ?? string.Empty;
                if (!regenByPool.TryGetValue(poolId, out int dailyRegen))
                {
                    dailyRegen = B1071_ManpowerBehavior.GetDailyRegen(pool, maximum);
                    regenByPool[poolId] = dailyRegen;
                }

                string trend = current >= maximum ? "=" : (dailyRegen > 0 ? "\u2191" : "\u2193");

                string boundTo = string.Empty;
                if (settlement.IsVillage && settlement.Village?.Bound != null)
                    boundTo = settlement.Village.Bound.Name?.ToString() ?? "-";

                var row = new LedgerRow
                {
                    SettlementName = settlement.Name.ToString(),
                    Type = settlement.IsTown ? "Town" : (settlement.IsCastle ? "Castle" : "Village"),
                    FactionName = settlement.MapFaction?.Name?.ToString() ?? "Independent",
                    OwnerName = settlement.OwnerClan?.Name?.ToString() ?? "-",
                    PoolName = pool.Name.ToString(),
                    Current = current,
                    Maximum = maximum,
                    RatioPercent = ratio,
                    Prosperity = (int)prosperity,
                    Security = (int)security,
                    Hearth = (int)hearth,
                    DailyRegen = dailyRegen,
                    Trend = trend,
                    KingdomId = (settlement.OwnerClan?.Kingdom as Kingdom)?.StringId ?? string.Empty,
                    BoundTo = boundTo,
                    _position = settlement.GetPosition2D
                };

                if (settlement.IsVillage)
                    villages.Add(row);
                else
                    towns.Add(row);
            }

            _cachedRows = towns;
            _cachedVillageRows = villages;
        }

        private static int GetPageStart(int totalEntries, int pageSize)
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalEntries / (float)pageSize));
            if (_pageIndex >= totalPages)
                _pageIndex = totalPages - 1;
            if (_pageIndex < 0)
                _pageIndex = 0;

            _pageLabel = "Page " + (_pageIndex + 1) + "/" + totalPages;
            return _pageIndex * pageSize;
        }

        private static int GetRowsPerPage()
        {
            int rows = Settings.OverlayLedgerRowsPerPage;
            if (rows < 3) rows = 3;
            if (rows > 15) rows = 15;
            return rows;
        }

        private static string FormatTabText(string label, bool active)
        {
            return label;
        }

        private static void EnsureLedgerInitialized()
        {
            if (_ledgerInitialized)
                return;

            int tabValue = Settings.OverlayLedgerDefaultTab;
            if (tabValue < 0) tabValue = 0;
            if (tabValue > 7) tabValue = 7;

            _activeTab = (B1071LedgerTab)tabValue;
            _pageIndex = 0;
            _sortColumn = 0;
            _sortAscending = false;
            _ledgerInitialized = true;
            UpdateSortTextCache();
            ForceRefresh();
        }

        private static void ForceRefresh()
        {
            _refreshTimer = 0f;
            _lastText = string.Empty;
            _columnsDirty = true;
            _viewDirty = true;
        }

        private static bool IsCampaignMapReady()
        {
            Game? game = Game.Current;
            if (game == null)
                return false;

            if (game.GameType is not TaleWorlds.CampaignSystem.Campaign)
                return false;

            var gsm = game.GameStateManager;
            if (gsm == null || gsm.ActiveState is not MapState)
                return false;

            TaleWorlds.CampaignSystem.Campaign? campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            return campaign != null;
        }

        private sealed class LedgerRow
        {
            public string SettlementName = string.Empty;
            public string Type = string.Empty;
            public string FactionName = string.Empty;
            public string OwnerName = string.Empty;
            public string PoolName = string.Empty;
            public int Current;
            public int Maximum;
            public int RatioPercent;
            public int Prosperity;
            public int Security;
            public int Hearth;
            public float Distance;
            public float DistanceSq;
            public int DailyRegen;
            public string Trend = "=";
            public string KingdomId = string.Empty;
            public string BoundTo = string.Empty;
            internal Vec2 _position;
        }

        private sealed class FactionLedgerRow
        {
            public string Name = string.Empty;
            public string KingdomId = string.Empty;
            public int Current;
            public int Maximum;
            public int Settlements;
            public int TotalProsperity;
            public int TotalDailyRegen;
            public float Exhaustion;
            public int Money;
            public bool HasMoney;
        }

        private sealed class ArmiesLedgerRow
        {
            public string Name = string.Empty;
            public string KingdomId = string.Empty;
            public int TotalTroops;
            public int MilitaryPower;   // Σ(tier² × count)
            public int PartyCount;
            public float Exhaustion;
        }

        private sealed class WarsLedgerRow
        {
            public string PairName = string.Empty;
            public string SideAName = string.Empty;
            public string SideBName = string.Empty;
            public float ExhaustionA;
            public float ExhaustionB;
            public float MaxExhaustion;
            public float PeacePressure;
            public bool HasTruce;
            public float TruceDays;
            public string StatusText = string.Empty;
            public int StatusScore;
        }

        // ─── Wars tab ───

        private static float GetNativePeacePressureScore(Kingdom sideA, Kingdom sideB)
        {
            try
            {
                var model = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.DiplomacyModel;
                if (model != null)
                    return model.GetScoreOfDeclaringPeace(sideA, sideB);
            }
            catch
            {
            }

            // Safe fallback if DiplomacyModel call is unavailable in this runtime.
            B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
            if (behavior == null)
                return 0f;

            float exA = behavior.GetWarExhaustion(sideA.StringId);
            float exB = behavior.GetWarExhaustion(sideB.StringId);
            return (exA + exB) * 0.5f;
        }

        private static int GetForcedPeaceRiskScore(float maxExhaustion)
        {
            if (!Settings.EnableExhaustionDiplomacyPressure)
                return 0;

            if (!Settings.EnableForcedPeaceAtCrisis)
                return 1;

            if (maxExhaustion >= Settings.DiplomacyForcedPeaceThreshold)
                return 4;

            if (maxExhaustion >= Settings.DiplomacyPeacePressureThreshold)
                return 3;

            if (maxExhaustion >= Settings.DiplomacyNoNewWarThreshold)
                return 2;

            return 1;
        }

        private static string GetForcedPeaceRiskLabel(int riskScore)
        {
            return riskScore switch
            {
                0 => "Off",
                1 => "Low",
                2 => "Gated",
                3 => "High",
                _ => "Crisis"
            };
        }

        private static string GetExhaustionCompact(float exhaustion)
        {
            if (exhaustion <= 0f) return "Fr0";
            if (exhaustion < 25f) return "St" + ((int)exhaustion).ToString();
            if (exhaustion < 50f) return "Ti" + ((int)exhaustion).ToString();
            if (exhaustion < 75f) return "Ex" + ((int)exhaustion).ToString();
            return "Cr" + ((int)exhaustion).ToString();
        }

        private static string GetPeacePressureBand(float peacePressure)
        {
            float abs = Math.Abs(peacePressure);
            string level = abs >= 100000f ? "Extreme"
                : abs >= 25000f ? "High"
                : abs >= 5000f ? "Medium"
                : abs >= 1000f ? "Low"
                : "Light";

            if (peacePressure > 0f)
                return "Peace " + level;
            if (peacePressure < 0f)
                return "War " + level;
            return "Neutral";
        }

        private static string BuildWarsColumns(B1071_ManpowerBehavior behavior)
        {
            var rows = new List<WarsLedgerRow>();
            var seenPairs = new HashSet<string>(StringComparer.Ordinal);

            foreach (Kingdom sideA in Kingdom.All)
            {
                if (sideA == null || sideA.IsEliminated)
                    continue;

                string idA = sideA.StringId ?? string.Empty;
                if (string.IsNullOrEmpty(idA))
                    continue;

                for (int i = 0; i < sideA.FactionsAtWarWith.Count; i++)
                {
                    IFaction enemyFaction = sideA.FactionsAtWarWith[i];
                    if (enemyFaction is not Kingdom sideB || sideB.IsEliminated)
                        continue;

                    if (!sideA.IsAtWarWith(sideB))
                        continue;

                    string idB = sideB.StringId ?? string.Empty;
                    if (string.IsNullOrEmpty(idB))
                        continue;

                    string pairKey = string.CompareOrdinal(idA, idB) <= 0 ? idA + "|" + idB : idB + "|" + idA;
                    if (!seenPairs.Add(pairKey))
                        continue;

                    float exA = behavior.GetWarExhaustion(idA);
                    float exB = behavior.GetWarExhaustion(idB);
                    float maxExhaustion = Math.Max(exA, exB);
                    float peacePressure = GetNativePeacePressureScore(sideA, sideB);

                    bool hasTruce = behavior.IsKingdomPairUnderTruce(sideA, sideB, out float truceDays);
                    int riskScore = GetForcedPeaceRiskScore(maxExhaustion);
                    string riskLabel = GetForcedPeaceRiskLabel(riskScore);
                    string status = hasTruce ? "Truce " + truceDays.ToString("0.0") + "d | " + riskLabel : "No Truce | " + riskLabel;

                    rows.Add(new WarsLedgerRow
                    {
                        PairName = sideA.Name + " vs " + sideB.Name,
                        SideAName = sideA.Name?.ToString() ?? "Unknown",
                        SideBName = sideB.Name?.ToString() ?? "Unknown",
                        ExhaustionA = exA,
                        ExhaustionB = exB,
                        MaxExhaustion = maxExhaustion,
                        PeacePressure = peacePressure,
                        HasTruce = hasTruce,
                        TruceDays = truceDays,
                        StatusText = status,
                        StatusScore = riskScore
                    });
                }
            }

            rows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => a.MaxExhaustion.CompareTo(b.MaxExhaustion),
                    2 => a.StatusScore.CompareTo(b.StatusScore),
                    3 => string.Compare(a.PairName, b.PairName, StringComparison.Ordinal),
                    _ => a.PeacePressure.CompareTo(b.PeacePressure)
                };

                if (_sortColumn != 3) { if (!_sortAscending) compare = -compare; }
                else { if (_sortAscending) compare = -compare; }
                if (compare != 0) return compare;
                return string.Compare(a.PairName, b.PairName, StringComparison.Ordinal);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(rows.Count, pageSize);
            int endIndex = Math.Min(rows.Count, startIndex + pageSize);

            if (rows.Count == 0)
            {
                ClearColumns("Wars Ledger - No active kingdom wars.");
                return "Wars Ledger\nNo active kingdom wars.\nLegend: Exhaustion uses Fr=Fresh, St=Strained, Ti=Tired, Ex=Exhausted, Cr=Crisis (left side is first kingdom, right side is second). Bias shows Peace or War tendency with strength (Light/Low/Medium/High/Extreme). Status shows Truce days remaining and forced-peace risk from exhaustion thresholds.";
            }

            string playerFaction = GetPlayerFactionName();
            int truceCount = 0;
            float totalPressure = 0f;
            foreach (WarsLedgerRow row in rows)
            {
                if (row.HasTruce) truceCount++;
                totalPressure += row.PeacePressure;
            }

            _titleText = "Wars Ledger  (" + rows.Count + " entries)\nLegend: Exhaustion uses Fr=Fresh, St=Strained, Ti=Tired, Ex=Exhausted, Cr=Crisis (left side is first kingdom, right side is second).\nBias shows Peace or War tendency with strength (Light/Low/Medium/High/Extreme). Status shows Truce days remaining and forced-peace risk from exhaustion thresholds.";
            _header1 = "War Pair";
            _header2 = "Exhaustion";
            _header3 = "Peace Bias";
            _header4 = "Truce / Risk";
            ApplySortIndicator(new[] { 3, 2, 4, 1 });

            _ledgerRows.Clear();
            for (int i = endIndex - 1; i >= startIndex; i--)
            {
                WarsLedgerRow row = rows[i];
                int rank = i + 1;
                bool involvesPlayer = string.Equals(row.SideAName, playerFaction, StringComparison.Ordinal) ||
                                      string.Equals(row.SideBName, playerFaction, StringComparison.Ordinal);
                string prefix = involvesPlayer ? "> " : "";
                bool even = (i - startIndex) % 2 == 0;

                _ledgerRows.Add(new B1071_LedgerRowVM(
                    prefix + rank + ". " + TruncateForColumn(row.PairName, 26),
                    GetExhaustionCompact(row.ExhaustionA) + "/" + GetExhaustionCompact(row.ExhaustionB),
                    GetPeacePressureBand(row.PeacePressure),
                    TruncateForColumn(row.StatusText, 24),
                    involvesPlayer,
                    even));
            }

            _totals1 = "Total Wars";
            _totals2 = rows.Count.ToString("N0");
            _totals3 = rows.Count > 0 ? GetPeacePressureBand(totalPressure / rows.Count) : "Neutral";
            _totals4 = truceCount.ToString("N0") + " truces";
            return _titleText;
        }

        // ─── Armies tab ───

        private static void RebuildArmiesCache()
        {
            if (_cachedArmiesRows == null)
                _cachedArmiesRows = new List<ArmiesLedgerRow>();
            _cachedArmiesRows.Clear();

            var byFaction = new Dictionary<string, ArmiesLedgerRow>();
            var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<B1071_ManpowerBehavior>();

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated) continue;

                string factionName = kingdom.Name?.ToString() ?? "Unknown";
                if (!byFaction.TryGetValue(factionName, out ArmiesLedgerRow row))
                {
                    row = new ArmiesLedgerRow
                    {
                        Name = factionName,
                        KingdomId = kingdom.StringId,
                        Exhaustion = !string.IsNullOrEmpty(kingdom.StringId) && behavior != null
                            ? behavior.GetWarExhaustion(kingdom.StringId)
                            : 0f
                    };
                    byFaction[factionName] = row;
                }

                foreach (Clan clan in kingdom.Clans)
                {
                    if (clan == null) continue;

                    foreach (var component in clan.WarPartyComponents)
                    {
                        MobileParty? party = component?.MobileParty;
                        if (party == null || party.MemberRoster == null) continue;
                        if (party.IsDisbanding) continue;

                        row.PartyCount++;

                        var roster = party.MemberRoster.GetTroopRoster();
                        for (int i = 0; i < roster.Count; i++)
                        {
                            var element = roster[i];
                            int count = element.Number;
                            if (count <= 0) continue;

                            int tier = element.Character != null ? Math.Max(1, element.Character.Tier) : 1;
                            row.TotalTroops += count;
                            row.MilitaryPower += tier * tier * count;
                        }
                    }
                }
            }

            _cachedArmiesRows.AddRange(byFaction.Values);
        }

        private static string BuildArmiesColumns()
        {
            if (_armiesCacheStale || _cachedArmiesRows == null || _cachedArmiesRows.Count == 0)
            {
                RebuildArmiesCache();
                _armiesCacheStale = false;
            }

            var rows = _cachedArmiesRows!;

            rows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => a.TotalTroops.CompareTo(b.TotalTroops),
                    2 => a.Exhaustion.CompareTo(b.Exhaustion),
                    3 => string.Compare(a.Name, b.Name, StringComparison.Ordinal),
                    _ => a.MilitaryPower.CompareTo(b.MilitaryPower)
                };
                if (_sortColumn != 3) { if (!_sortAscending) compare = -compare; }
                else { if (_sortAscending) compare = -compare; }
                if (compare != 0) return compare;
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(rows.Count, pageSize);
            int endIndex = Math.Min(rows.Count, startIndex + pageSize);

            if (rows.Count == 0)
            {
                ClearColumns("Armies Ledger - No entries found.");
                return "Armies Ledger\nNo entries found.";
            }

            string playerFaction = GetPlayerFactionName();
            int totalPower = 0, totalTroops = 0;
            float totalExhaustion = 0f;
            foreach (ArmiesLedgerRow r in rows)
            {
                totalPower += r.MilitaryPower;
                totalTroops += r.TotalTroops;
                totalExhaustion += r.Exhaustion;
            }

            _titleText = "Armies Ledger  (" + rows.Count + " entries)";
            _header1 = "Faction";
            _header2 = "Power";
            _header3 = "Troops";
            _header4 = "Exhaustion";
            ApplySortIndicator(new[] { 2, 3, 4, 1 });

            _ledgerRows.Clear();
            // Iterate in reverse: Bannerlord's VerticalTopToBottom renders first child at bottom.
            for (int i = endIndex - 1; i >= startIndex; i--)
            {
                ArmiesLedgerRow row = rows[i];
                int rank = i + 1;
                string prefix = row.Name == playerFaction ? "> " : "";
                bool highlight = row.Name == playerFaction;
                bool even = (i - startIndex) % 2 == 0;

                _ledgerRows.Add(new B1071_LedgerRowVM(
                    prefix + rank + ". " + TruncateForColumn(row.Name, 24),
                    row.MilitaryPower.ToString("N0"),
                    row.TotalTroops.ToString("N0"),
                    GetExhaustionLabel(row.Exhaustion),
                    highlight, even));
            }

            _totals1 = "Total";
            _totals2 = totalPower.ToString("N0");
            _totals3 = totalTroops.ToString("N0");
            _totals4 = rows.Count > 0 ? GetExhaustionLabel(totalExhaustion / rows.Count) : "Fresh";
            return _titleText;
        }
    }
}
