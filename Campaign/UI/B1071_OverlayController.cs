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
        Pools = 1,
        World = 2,
        Factions = 3
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
        internal static string TabPoolsText => FormatTabText("Pools", _activeTab == B1071LedgerTab.Pools);
        internal static string TabWorldText => FormatTabText("World", _activeTab == B1071LedgerTab.World);
        internal static string TabFactionsText => FormatTabText("Factions", _activeTab == B1071LedgerTab.Factions);
        internal static bool IsTabNearbyActive => _activeTab == B1071LedgerTab.NearbyPools;
        internal static bool IsTabPoolsActive => _activeTab == B1071LedgerTab.Pools;
        internal static bool IsTabWorldActive => _activeTab == B1071LedgerTab.World;
        internal static bool IsTabFactionsActive => _activeTab == B1071LedgerTab.Factions;
        private static readonly string[][] _sortKeys = new[]
        {
            new[] { "Dist", "MP", "%", "Name" },
            new[] { "%", "MP", "Owner", "Name" },
            new[] { "MP", "Prosp", "Sec", "Name" },
            new[] { "%", "MP", "Prosp", "Name" }
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
                case B1071LedgerTab.Pools: return BuildPoolsColumns(behavior);
                case B1071LedgerTab.World: return BuildWorldColumns(behavior);
                case B1071LedgerTab.Factions: return BuildFactionsColumns(behavior);
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
                c3.Append(row.RatioPercent + "%");
                c4.Append(row.Distance.ToString("F1") + " km");
            }

            _col1Text = c1.ToString();
            _col2Text = c2.ToString();
            _col3Text = c3.ToString();
            _col4Text = c4.ToString();
            SetTotals(totalCurrent, totalMaximum);
            return _titleText;
        }

        private static string BuildPoolsColumns(B1071_ManpowerBehavior behavior)
        {
            List<LedgerRow> rows = BuildSettlementRows(behavior, includeVillages: false);
            rows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => a.Current.CompareTo(b.Current),
                    2 => string.Compare(a.OwnerName ?? "", b.OwnerName ?? "", StringComparison.Ordinal),
                    3 => string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal),
                    _ => a.RatioPercent.CompareTo(b.RatioPercent)
                };
                if (_sortColumn <= 1) { if (!_sortAscending) compare = -compare; }
                else { if (_sortAscending) compare = -compare; }
                if (compare != 0) return compare;
                return string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(rows.Count, pageSize);
            int endIndex = Math.Min(rows.Count, startIndex + pageSize);

            if (rows.Count == 0)
            {
                ClearColumns("Pool Ledger - No entries found.");
                return "Pool Ledger\nNo entries found.";
            }

            string playerFaction = GetPlayerFactionName();
            int totalCurrent = 0, totalMaximum = 0;
            foreach (LedgerRow r in rows) { totalCurrent += r.Current; totalMaximum += r.Maximum; }

            _titleText = "Pool Ledger  (" + rows.Count + " entries)";
            _header1 = "Settlement";
            _header2 = "Manpower";
            _header3 = "%";
            _header4 = "Owner";
            ApplySortIndicator(new[] { 3, 2, 4, 1 });

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
                c3.Append(row.RatioPercent + "%");
                c4.Append(TruncateForColumn(row.OwnerName, 16));
            }

            _col1Text = c1.ToString();
            _col2Text = c2.ToString();
            _col3Text = c3.ToString();
            _col4Text = c4.ToString();
            SetTotals(totalCurrent, totalMaximum);
            return _titleText;
        }

        private static string BuildWorldColumns(B1071_ManpowerBehavior behavior)
        {
            List<LedgerRow> rows = BuildSettlementRows(behavior, includeVillages: Settings.OverlayLedgerIncludeVillages);
            rows.Sort((a, b) =>
            {
                int compare = _sortColumn switch
                {
                    1 => (a.Type == "Village" ? a.Hearth : a.Prosperity).CompareTo(b.Type == "Village" ? b.Hearth : b.Prosperity),
                    2 => (a.Type == "Village" ? 0f : a.Security).CompareTo(b.Type == "Village" ? 0f : b.Security),
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
                ClearColumns("World Ledger - No entries found.");
                return "World Ledger\nNo entries found.";
            }

            string playerFaction = GetPlayerFactionName();
            int totalCurrent = 0, totalMaximum = 0;
            foreach (LedgerRow r in rows) { totalCurrent += r.Current; totalMaximum += r.Maximum; }

            _titleText = "World Ledger  (" + rows.Count + " entries)";
            _header1 = "Settlement";
            _header2 = "Manpower";
            _header3 = "Hearth/Prosp";
            _header4 = "Security";
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

                c1.Append(prefix + rank + ". " + TruncateForColumn(row.SettlementName, 20) + " [" + row.Type + "]");
                c2.Append(FormatMp(row.Current, row.Maximum));
                c3.Append(row.Type == "Village"
                    ? row.Hearth.ToString("N0")
                    : row.Prosperity.ToString("N0"));
                c4.Append(row.Type == "Village"
                    ? "-"
                    : row.Security.ToString("N0"));
            }

            _col1Text = c1.ToString();
            _col2Text = c2.ToString();
            _col3Text = c3.ToString();
            _col4Text = c4.ToString();
            SetTotals(totalCurrent, totalMaximum);
            return _titleText;
        }

        private static string BuildFactionsColumns(B1071_ManpowerBehavior behavior)
        {
            List<LedgerRow> rows = BuildSettlementRows(behavior, includeVillages: Settings.OverlayLedgerIncludeVillages);
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
                if (row.Type != "Village")
                    factionRow.TotalProsperity += row.Prosperity;
            }

            var factionRows = new List<FactionLedgerRow>(byFaction.Values);
            factionRows.Sort((a, b) =>
            {
                int aRatio = a.Maximum <= 0 ? 0 : (int)((100f * a.Current) / a.Maximum);
                int bRatio = b.Maximum <= 0 ? 0 : (int)((100f * b.Current) / b.Maximum);
                int compare = _sortColumn switch
                {
                    1 => a.Current.CompareTo(b.Current),
                    2 => a.TotalProsperity.CompareTo(b.TotalProsperity),
                    3 => string.Compare(a.Name, b.Name, StringComparison.Ordinal),
                    _ => aRatio.CompareTo(bRatio)
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
            int totalCurrent = 0, totalMaximum = 0, grandProsperity = 0;
            foreach (FactionLedgerRow r in factionRows) { totalCurrent += r.Current; totalMaximum += r.Maximum; grandProsperity += r.TotalProsperity; }

            _titleText = "Faction Ledger  (" + factionRows.Count + " entries)";
            _header1 = "Faction";
            _header2 = "Manpower";
            _header3 = "%";
            _header4 = "Prosperity";
            ApplySortIndicator(new[] { 3, 2, 4, 1 });

            var c1 = new StringBuilder();
            var c2 = new StringBuilder();
            var c3 = new StringBuilder();
            var c4 = new StringBuilder();

            for (int i = startIndex; i < endIndex; i++)
            {
                if (i > startIndex) { c1.Append('\n'); c2.Append('\n'); c3.Append('\n'); c4.Append('\n'); }
                FactionLedgerRow row = factionRows[i];
                int ratio = row.Maximum <= 0 ? 0 : (int)((100f * row.Current) / row.Maximum);
                int rank = i + 1;
                string prefix = row.Name == playerFaction ? "> " : "";

                c1.Append(prefix + rank + ". " + TruncateForColumn(row.Name, 24));
                c2.Append(FormatMp(row.Current, row.Maximum));
                c3.Append(ratio + "%");
                c4.Append(row.TotalProsperity.ToString("N0"));
            }

            _col1Text = c1.ToString();
            _col2Text = c2.ToString();
            _col3Text = c3.ToString();
            _col4Text = c4.ToString();
            SetTotals(totalCurrent, totalMaximum, grandProsperity.ToString("N0"));
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

        private static List<LedgerRow> BuildSettlementRows(B1071_ManpowerBehavior behavior, bool includeVillages)
        {
            MobileParty? mainParty = MobileParty.MainParty;
            Vec2 partyPosition = mainParty != null ? mainParty.GetPosition2D : new Vec2(0f, 0f);
            bool hasPartyPosition = mainParty != null;

            var rows = new List<LedgerRow>();
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null || settlement.IsHideout)
                    continue;

                if (!settlement.IsTown && !settlement.IsCastle && !settlement.IsVillage)
                    continue;

                if (!includeVillages && settlement.IsVillage)
                    continue;

                behavior.GetManpowerPool(settlement, out int current, out int maximum, out Settlement pool);

                Town? town = settlement.Town;
                float prosperity = town != null ? town.Prosperity : 0f;
                float security = town != null ? town.Security : 0f;
                float hearth = settlement.IsVillage && settlement.Village != null ? settlement.Village.Hearth : 0f;
                int ratio = maximum <= 0 ? 0 : (int)((100f * current) / maximum);
                float distance = hasPartyPosition ? (settlement.GetPosition2D - partyPosition).Length : 0f;

                rows.Add(new LedgerRow
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
                    Distance = distance
                });
            }

            return rows;
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
            if (tabValue > 3) tabValue = 3;

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
        }

        private sealed class FactionLedgerRow
        {
            public string Name = string.Empty;
            public int Current;
            public int Maximum;
            public int Settlements;
            public int TotalProsperity;
        }
    }
}
