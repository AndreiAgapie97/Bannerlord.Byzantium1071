using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using System.Text;
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
        NearbyPools = 0,
        Castles = 1,
        Towns = 2,
        Villages = 3,
        Factions = 4
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
        private static string _col1Text = string.Empty;
        private static string _col2Text = string.Empty;
        private static string _col3Text = string.Empty;
        private static string _col4Text = string.Empty;
        private static string _totals1 = string.Empty;
        private static string _totals2 = string.Empty;
        private static string _totals3 = string.Empty;
        private static string _totals4 = string.Empty;
        private static string _header1 = string.Empty;
        private static string _header2 = string.Empty;
        private static string _header3 = string.Empty;
        private static string _header4 = string.Empty;

        private static B1071LedgerTab _activeTab = B1071LedgerTab.NearbyPools;
        private static int _pageIndex;
        private static int _sortColumn;
        private static bool _sortAscending;
        private static string _pageLabel = "Page 1/1";

        // Row data cache — rebuilt once per in-game day, not per frame.
        private static List<LedgerRow>? _cachedRows;
        private static List<LedgerRow>? _cachedVillageRows;
        private static bool _cacheStale = true;

        private static readonly B1071_McmSettings FallbackSettings = new();
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? FallbackSettings;

        internal static bool IsVisible => _isVisible;
        internal static bool IsExpanded => _isExpanded;
        internal static string CurrentText => _currentText;
        internal static string TitleText => _titleText;
        internal static string Col1Text => _col1Text;
        internal static string Col2Text => _col2Text;
        internal static string Col3Text => _col3Text;
        internal static string Col4Text => _col4Text;
        internal static string Totals1 => _totals1;
        internal static string Totals2 => _totals2;
        internal static string Totals3 => _totals3;
        internal static string Totals4 => _totals4;
        internal static string Header1 => _header1;
        internal static string Header2 => _header2;
        internal static string Header3 => _header3;
        internal static string Header4 => _header4;
        internal static string TabNearbyText => FormatTabText("Nearby", _activeTab == B1071LedgerTab.NearbyPools);
        internal static string TabCastlesText => FormatTabText("Castles", _activeTab == B1071LedgerTab.Castles);
        internal static string TabTownsText => FormatTabText("Towns", _activeTab == B1071LedgerTab.Towns);
        internal static string TabVillagesText => FormatTabText("Villages", _activeTab == B1071LedgerTab.Villages);
        internal static string TabFactionsText => FormatTabText("Factions", _activeTab == B1071LedgerTab.Factions);
        internal static bool IsTabNearbyActive => _activeTab == B1071LedgerTab.NearbyPools;
        internal static bool IsTabCastlesActive => _activeTab == B1071LedgerTab.Castles;
        internal static bool IsTabTownsActive => _activeTab == B1071LedgerTab.Towns;
        internal static bool IsTabVillagesActive => _activeTab == B1071LedgerTab.Villages;
        internal static bool IsTabFactionsActive => _activeTab == B1071LedgerTab.Factions;
        private static readonly string[][] _sortKeys = new[]
        {
            new[] { "Dist", "MP", "%", "Name" },
            new[] { "MP", "Prosp", "Regen", "Name" },
            new[] { "MP", "Prosp", "Regen", "Name" },
            new[] { "Hearth", "Fact", "Bound", "Name" },
            new[] { "Prosp", "MP", "Exhaust", "Name" }
        };

        internal static string SortText
        {
            get
            {
                int tab = (int)_activeTab;
                int col = Math.Min(_sortColumn, _sortKeys[tab].Length - 1);
                string arrow = _sortAscending ? " ↑" : " ↓";
                return _sortKeys[tab][col] + arrow;
            }
        }
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
        }

        internal static void SetLedgerTab(B1071LedgerTab tab)
        {
            if (_activeTab == tab)
                return;

            _activeTab = tab;
            _pageIndex = 0;
            _sortColumn = 0;
            _sortAscending = false;
            ForceRefresh();
        }

        /// <summary>
        /// Called by the behavior on daily tick so the overlay rebuilds
        /// settlement data from scratch instead of every 0.35 s.
        /// </summary>
        internal static void MarkCacheStale()
        {
            _cacheStale = true;
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

        internal static void ToggleSort()
        {
            int tab = (int)_activeTab;
            int maxCol = _sortKeys[tab].Length - 1;
            if (_sortAscending)
            {
                _sortAscending = false;
                _sortColumn++;
                if (_sortColumn > maxCol)
                    _sortColumn = 0;
            }
            else
            {
                _sortAscending = true;
            }
            _pageIndex = 0;
            ForceRefresh();
        }

        internal static void RefreshNow()
        {
            EnsureLedgerInitialized();
            _currentText = BuildOverlayText();
            _lastText = _currentText;
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

            if (Settings.EnableOverlayHotkey && Input.IsKeyPressed(InputKey.M))
            {
                ToggleVisibility();
            }

            if (!_isVisible)
                return;

            _refreshTimer -= dt;
            if (_refreshTimer > 0f)
                return;

            _refreshTimer = 0.35f;
            string text = BuildOverlayText();
            if (text == _lastText)
                return;

            _lastText = text;
            _currentText = text;
            if (!_panelModeActive)
                MBInformationManager.ShowHint("Byzantium 1071 Overlay\n" + text);
        }

        internal static void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            _refreshTimer = 0f;

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
                case B1071LedgerTab.NearbyPools: return BuildNearbyPoolsColumns(behavior);
                case B1071LedgerTab.Castles: return BuildCastlesColumns(behavior);
                case B1071LedgerTab.Towns: return BuildTownsColumns(behavior);
                case B1071LedgerTab.Factions: return BuildFactionsColumns(behavior);
                case B1071LedgerTab.Villages: return BuildVillagesColumns(behavior);
                default: return BuildNearbyPoolsColumns(behavior);
            }
        }

        private static void ClearColumns(string fallback)
        {
            _titleText = fallback;
            _col1Text = string.Empty;
            _col2Text = string.Empty;
            _col3Text = string.Empty;
            _col4Text = string.Empty;
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
            rows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => a.Current.CompareTo(b.Current),
                    2 => a.RatioPercent.CompareTo(b.RatioPercent),
                    3 => string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal),
                    _ => a.Distance.CompareTo(b.Distance)
                };
                if (!_sortAscending && _sortColumn != 3) compare = -compare;
                if (_sortColumn == 3 && _sortAscending) compare = -compare;
                if (compare != 0) return compare;
                return _sortColumn == 0 ? string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal)
                    : a.Distance.CompareTo(b.Distance);
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

            var c1 = new StringBuilder();
            var c2 = new StringBuilder();
            var c3 = new StringBuilder();
            var c4 = new StringBuilder();

            for (int i = startIndex; i < endIndex; i++)
            {
                if (i > startIndex) { c1.Append('\n'); c2.Append('\n'); c3.Append('\n'); c4.Append('\n'); }
                LedgerRow row = rows[i];
                int rank = i + 1;
                string prefix = row.FactionName == playerFaction ? "> " : "";

                c1.Append(prefix + rank + ". " + TruncateForColumn(row.SettlementName, 24));
                c2.Append(FormatMp(row.Current, row.Maximum));
                c3.Append(row.RatioPercent + "% " + row.Trend);
                c4.Append(row.Distance.ToString("F1") + " km");
            }

            _col1Text = c1.ToString();
            _col2Text = c2.ToString();
            _col3Text = c3.ToString();
            _col4Text = c4.ToString();
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

            var c1 = new StringBuilder();
            var c2 = new StringBuilder();
            var c3 = new StringBuilder();
            var c4 = new StringBuilder();

            for (int i = startIndex; i < endIndex; i++)
            {
                if (i > startIndex) { c1.Append('\n'); c2.Append('\n'); c3.Append('\n'); c4.Append('\n'); }
                LedgerRow row = rows[i];
                int rank = i + 1;
                string prefix = row.FactionName == playerFaction ? "> " : "";

                c1.Append(prefix + rank + ". " + TruncateForColumn(row.SettlementName, 24));
                c2.Append(FormatMp(row.Current, row.Maximum));
                c3.Append(row.Prosperity.ToString("N0"));
                c4.Append("+" + row.DailyRegen.ToString("N0") + "/d");
            }

            _col1Text = c1.ToString();
            _col2Text = c2.ToString();
            _col3Text = c3.ToString();
            _col4Text = c4.ToString();
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

            var c1 = new StringBuilder();
            var c2 = new StringBuilder();
            var c3 = new StringBuilder();
            var c4 = new StringBuilder();

            for (int i = startIndex; i < endIndex; i++)
            {
                if (i > startIndex) { c1.Append('\n'); c2.Append('\n'); c3.Append('\n'); c4.Append('\n'); }
                LedgerRow row = rows[i];
                int rank = i + 1;
                string prefix = row.FactionName == playerFaction ? "> " : "";

                c1.Append(prefix + rank + ". " + TruncateForColumn(row.SettlementName, 24));
                c2.Append(row.Hearth.ToString("N0"));
                c3.Append(TruncateForColumn(row.FactionName, 18));
                c4.Append(TruncateForColumn(row.BoundTo, 18));
            }

            _col1Text = c1.ToString();
            _col2Text = c2.ToString();
            _col3Text = c3.ToString();
            _col4Text = c4.ToString();
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

            // Populate exhaustion from behavior.
            foreach (FactionLedgerRow fr in byFaction.Values)
            {
                if (!string.IsNullOrEmpty(fr.KingdomId) && behavior != null)
                    fr.Exhaustion = behavior.GetWarExhaustion(fr.KingdomId);
            }

            var factionRows = new List<FactionLedgerRow>(byFaction.Values);
            factionRows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => a.Current.CompareTo(b.Current),
                    2 => a.Exhaustion.CompareTo(b.Exhaustion),
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
            _header4 = "Exhaustion";
            ApplySortIndicator(new[] { 3, 2, 4, 1 });

            var c1 = new StringBuilder();
            var c2 = new StringBuilder();
            var c3 = new StringBuilder();
            var c4 = new StringBuilder();

            for (int i = startIndex; i < endIndex; i++)
            {
                if (i > startIndex) { c1.Append('\n'); c2.Append('\n'); c3.Append('\n'); c4.Append('\n'); }
                FactionLedgerRow row = factionRows[i];
                int rank = i + 1;
                string prefix = row.Name == playerFaction ? "> " : "";

                c1.Append(prefix + rank + ". " + TruncateForColumn(row.Name, 24));
                c2.Append(FormatMp(row.Current, row.Maximum));
                c3.Append(row.TotalProsperity.ToString("N0"));
                c4.Append(GetExhaustionLabel(row.Exhaustion));
            }

            _col1Text = c1.ToString();
            _col2Text = c2.ToString();
            _col3Text = c3.ToString();
            _col4Text = c4.ToString();
            int totalProsperity = 0;
            foreach (FactionLedgerRow r in factionRows) totalProsperity += r.TotalProsperity;
            SetTotals(totalCurrent, totalMaximum);
            _totals3 = totalProsperity.ToString("N0");
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
            }

            // Update distances (cheap — no Settlement.All iteration).
            MobileParty? mainParty = MobileParty.MainParty;
            Vec2 partyPos = mainParty != null ? mainParty.GetPosition2D : new Vec2(0f, 0f);
            bool hasPos = mainParty != null;
            var source = includeVillages ? _cachedRows! : _cachedRows!;
            foreach (LedgerRow r in source)
                r.Distance = hasPos ? (r._position - partyPos).Length : 0f;
            if (includeVillages && _cachedVillageRows != null)
                foreach (LedgerRow r in _cachedVillageRows)
                    r.Distance = hasPos ? (r._position - partyPos).Length : 0f;

            // Return a copy so callers can sort without disturbing the cache.
            if (!includeVillages)
                return new List<LedgerRow>(_cachedRows!);

            var combined = new List<LedgerRow>(_cachedRows!.Count + _cachedVillageRows!.Count);
            combined.AddRange(_cachedRows);
            combined.AddRange(_cachedVillageRows);
            return combined;
        }

        private static List<LedgerRow> GetVillageRows(B1071_ManpowerBehavior behavior)
        {
            if (_cacheStale || _cachedVillageRows == null)
            {
                RebuildCache(behavior);
                _cacheStale = false;
            }

            MobileParty? mainParty = MobileParty.MainParty;
            Vec2 partyPos = mainParty != null ? mainParty.GetPosition2D : new Vec2(0f, 0f);
            bool hasPos = mainParty != null;
            foreach (LedgerRow r in _cachedVillageRows!)
                r.Distance = hasPos ? (r._position - partyPos).Length : 0f;

            return new List<LedgerRow>(_cachedVillageRows);
        }

        private static void RebuildCache(B1071_ManpowerBehavior behavior)
        {
            var towns = new List<LedgerRow>();
            var villages = new List<LedgerRow>();

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
                int dailyRegen = B1071_ManpowerBehavior.GetDailyRegen(pool, maximum);
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
            if (tabValue > 4) tabValue = 4;

            _activeTab = (B1071LedgerTab)tabValue;
            _pageIndex = 0;
            _sortColumn = 0;
            _sortAscending = false;
            _ledgerInitialized = true;
            ForceRefresh();
        }

        private static void ForceRefresh()
        {
            _refreshTimer = 0f;
            _lastText = string.Empty;
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
        }
    }
}
