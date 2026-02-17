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

        private static B1071LedgerTab _activeTab = B1071LedgerTab.NearbyPools;
        private static int _pageIndex;
        private static bool _sortAscending;
        private static string _pageLabel = "Page 1/1";

        private static readonly B1071_McmSettings FallbackSettings = new();
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? FallbackSettings;

        internal static bool IsVisible => _isVisible;
        internal static bool IsExpanded => _isExpanded;
        internal static string CurrentText => _currentText;
        internal static string TabNearbyText => FormatTabText("Nearby", _activeTab == B1071LedgerTab.NearbyPools);
        internal static string TabPoolsText => FormatTabText("Pools", _activeTab == B1071LedgerTab.Pools);
        internal static string TabWorldText => FormatTabText("World", _activeTab == B1071LedgerTab.World);
        internal static string TabFactionsText => FormatTabText("Factions", _activeTab == B1071LedgerTab.Factions);
        internal static bool IsTabNearbyActive => _activeTab == B1071LedgerTab.NearbyPools;
        internal static bool IsTabPoolsActive => _activeTab == B1071LedgerTab.Pools;
        internal static bool IsTabWorldActive => _activeTab == B1071LedgerTab.World;
        internal static bool IsTabFactionsActive => _activeTab == B1071LedgerTab.Factions;
        internal static string SortText => _sortAscending ? "Sort ↑" : "Sort ↓";
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
            _sortAscending = !_sortAscending;
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
                return "Manpower behavior unavailable.";

            return _activeTab switch
            {
                B1071LedgerTab.NearbyPools => BuildNearbyPoolsText(behavior),
                B1071LedgerTab.Pools => BuildPoolsText(behavior),
                B1071LedgerTab.World => BuildWorldText(behavior),
                B1071LedgerTab.Factions => BuildFactionsText(behavior),
                _ => BuildNearbyPoolsText(behavior)
            };
        }

        private static string BuildNearbyPoolsText(B1071_ManpowerBehavior behavior)
        {
            List<LedgerRow> rows = BuildSettlementRows(behavior, includeVillages: false);
            rows.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(rows.Count, pageSize);
            int endIndex = Math.Min(rows.Count, startIndex + pageSize);

            if (rows.Count == 0)
                return "Nearby Pools\nNo entries found.\n[Press M to toggle]";

            string text = "Nearby Pools";
            text += "\n";
            for (int i = startIndex; i < endIndex; i++)
            {
                LedgerRow row = rows[i];
                text += "\n- " + row.SettlementName + "  " + row.Current + "/" + row.Maximum + " (" + row.RatioPercent + "%)";
            }

            text += "\n" + BuildFooter(rows.Count, pageSize);
            return text;
        }

        private static string BuildPoolsText(B1071_ManpowerBehavior behavior)
        {
            List<LedgerRow> rows = BuildSettlementRows(behavior, includeVillages: false);
            rows.Sort((a, b) =>
            {
                int ratioCompare = a.RatioPercent.CompareTo(b.RatioPercent);
                if (!_sortAscending)
                    ratioCompare = -ratioCompare;
                if (ratioCompare != 0)
                    return ratioCompare;

                return string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(rows.Count, pageSize);
            int endIndex = Math.Min(rows.Count, startIndex + pageSize);

            if (rows.Count == 0)
                return "Pool Ledger\nNo entries found.\n[Press M to toggle]";

            string text = "Pool Ledger";
            text += "\n";
            for (int i = startIndex; i < endIndex; i++)
            {
                LedgerRow row = rows[i];
                text += "\n- " + row.SettlementName + "  " + row.Current + "/" + row.Maximum + " (" + row.RatioPercent + "%)";
            }

            text += "\n" + BuildFooter(rows.Count, pageSize);
            return text;
        }

        private static string BuildWorldText(B1071_ManpowerBehavior behavior)
        {
            List<LedgerRow> rows = BuildSettlementRows(behavior, includeVillages: Settings.OverlayLedgerIncludeVillages);
            rows.Sort((a, b) =>
            {
                int compare = a.Current.CompareTo(b.Current);
                if (!_sortAscending)
                    compare = -compare;
                if (compare != 0)
                    return compare;

                return string.Compare(a.SettlementName, b.SettlementName, StringComparison.Ordinal);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(rows.Count, pageSize);
            int endIndex = Math.Min(rows.Count, startIndex + pageSize);

            if (rows.Count == 0)
                return "World Ledger\nNo entries found.\n[Press M to toggle]";

            string text = "World Ledger";
            text += "\n";
            for (int i = startIndex; i < endIndex; i++)
            {
                LedgerRow row = rows[i];
                text += "\n- " + row.SettlementName + "  " + row.Type + " · MP " + row.Current + "/" + row.Maximum;
                if (row.Type == "Village")
                    text += " · H " + row.Hearth;
                else
                    text += " · P " + row.Prosperity + " · S " + row.Security;
            }

            text += "\n" + BuildFooter(rows.Count, pageSize);
            return text;
        }

        private static string BuildFactionsText(B1071_ManpowerBehavior behavior)
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
            }

            var factionRows = new List<FactionLedgerRow>(byFaction.Values);
            factionRows.Sort((a, b) =>
            {
                int aRatio = a.Maximum <= 0 ? 0 : (int)((100f * a.Current) / a.Maximum);
                int bRatio = b.Maximum <= 0 ? 0 : (int)((100f * b.Current) / b.Maximum);

                int compare = aRatio.CompareTo(bRatio);
                if (!_sortAscending)
                    compare = -compare;
                if (compare != 0)
                    return compare;

                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            int pageSize = GetRowsPerPage();
            int startIndex = GetPageStart(factionRows.Count, pageSize);
            int endIndex = Math.Min(factionRows.Count, startIndex + pageSize);

            if (factionRows.Count == 0)
                return "Faction Ledger\nNo entries found.\n[Press M to toggle]";

            string text = "Faction Ledger";
            text += "\n";
            for (int i = startIndex; i < endIndex; i++)
            {
                FactionLedgerRow row = factionRows[i];
                int ratio = row.Maximum <= 0 ? 0 : (int)((100f * row.Current) / row.Maximum);
                text += "\n- " + row.Name + "  MP " + row.Current + "/" + row.Maximum + " (" + ratio + "%)";
            }

            text += "\n" + BuildFooter(factionRows.Count, pageSize);
            return text;
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

        private static string BuildFooter(int totalEntries, int pageSize)
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalEntries / (float)pageSize));
            _pageLabel = "Page " + (_pageIndex + 1) + "/" + totalPages;

            if (_panelModeActive)
                return string.Empty;

            return _pageLabel + " | " + SortText + "\n[Press M to toggle]";
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
        }
    }
}
