#if GAME_TESTS_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.UI;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using Xunit;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(LedgerUiCollection))]
    public sealed class LedgerSortingTests
    {
        private static readonly Type Controller = typeof(B1071_OverlayController);
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;

        [Theory]
        [InlineData("BuildCharactersColumns", "CharacterLedgerRow", 0, "DistanceSq", 4)]
        [InlineData("BuildNearbyPoolsColumns", "LedgerRow", 3, "SettlementName", 1)]
        [InlineData("BuildSettlementTypeColumns", "LedgerRow", 3, "SettlementName", 1)]
        [InlineData("BuildVillagesColumns", "LedgerRow", 1, "FactionName", 3)]
        [InlineData("BuildVillagesColumns", "LedgerRow", 2, "BoundTo", 4)]
        [InlineData("BuildVillagesColumns", "LedgerRow", 3, "SettlementName", 1)]
        [InlineData("BuildVillagesColumns", "LedgerRow", 4, "OwnerName", 5)]
        [InlineData("BuildFactionsColumns", "FactionLedgerRow", 3, "Name", 1)]
        [InlineData("BuildArmiesColumns", "ArmiesLedgerRow", 3, "Name", 1)]
        [InlineData("BuildWarsColumns", "WarsLedgerRow", 3, "PairName", 1)]
        [InlineData("BuildCasualtiesColumns", "CasualtiesLedgerRow", 3, "NameA", 1)]
        [InlineData("BuildSearchColumns", "SearchResultRow", 1, "Affiliation", 2)]
        [InlineData("BuildSearchColumns", "SearchResultRow", 2, "SortValue", 3)]
        [InlineData("BuildSearchColumns", "SearchResultRow", 3, "Name", 1)]
        [InlineData("BuildSearchColumns", "SearchResultRow", 4, "Status", 5)]
        public void DisplayedArrowMatchesTheProductionComparison(string builder, string rowName, int column, string key, int header)
        {
            using (new ControllerState())
            {
                Type rowType = Controller.GetNestedType(rowName, BindingFlags.NonPublic);
                object low = Activator.CreateInstance(rowType, true), high = Activator.CreateInstance(rowType, true);
                FieldInfo field = rowType.GetField(key);
                field.SetValue(low, field.FieldType == typeof(string) ? (object)"Alpha" : Convert.ChangeType(2, field.FieldType));
                field.SetValue(high, field.FieldType == typeof(string) ? (object)"Zulu" : Convert.ChangeType(10, field.FieldType));
                // Invoke the actual compiled comparison lambdas without constructing a live campaign.
                MethodInfo[] comparisons = Controller.GetNestedTypes(BindingFlags.NonPublic)
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    .Where(m => m.Name.StartsWith("<" + builder + ">b__") && m.ReturnType == typeof(int)
                        && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == rowType).ToArray();
                Assert.NotEmpty(comparisons);
                foreach (bool ascending in new[] { true, false })
                {
                    Set("_sortColumn", column);
                    Set("_sortAscending", ascending);
                    foreach (MethodInfo comparison in comparisons)
                    {
                        object? target = comparison.IsStatic ? null : Activator.CreateInstance(comparison.DeclaringType, true);
                        int forward = (int)comparison.Invoke(target, new[] { low, high });
                        int reverse = (int)comparison.Invoke(target, new[] { high, low });
                        Assert.Equal(ascending ? -1 : 1, Math.Sign(forward));
                        Assert.Equal(-Math.Sign(forward), Math.Sign(reverse));
                    }
                    Set("_header" + header, "Value");
                    int[] mapping = new int[5];
                    mapping[column] = header;
                    Controller.GetMethod("ApplySortIndicator", Static).Invoke(null, new object[] { mapping });
                    Assert.Equal("Value" + (ascending ? " ↑" : " ↓"), Get("_header" + header));
                }
            }
        }

        [Fact]
        public void CharactersOpenNearestFirstWithAnAscendingIndicator()
        {
            using (new ControllerState())
            {
                Set("_activeTab", B1071LedgerTab.Current);
                B1071_OverlayController.SetLedgerTab(B1071LedgerTab.Characters);
                Assert.True((bool)Get("_sortAscending"));
                Assert.Equal("Dist ↑", B1071_OverlayController.SortText);
            }
        }

        [Fact]
        public void ConfiguredOpeningTabUsesTheSameDirectionAsClickingItsTab()
        {
            using (new ControllerState())
            {
                var settings = (Byzantium1071.Campaign.Settings.B1071_McmSettings)Controller.GetProperty("Settings", Static).GetValue(null);
                int previous = settings.OverlayLedgerDefaultTab;
                try
                {
                    foreach (B1071LedgerTab tab in Enum.GetValues(typeof(B1071LedgerTab)))
                    {
                        settings.OverlayLedgerDefaultTab = (int)tab;
                        Set("_ledgerInitialized", false);
                        Controller.GetMethod("EnsureLedgerInitialized", Static).Invoke(null, null);
                        bool initial = (bool)Get("_sortAscending");
                        Set("_activeTab", (B1071LedgerTab)(-1));
                        B1071_OverlayController.SetLedgerTab(tab);
                        Assert.Equal(initial, Get("_sortAscending"));
                    }
                }
                finally { settings.OverlayLedgerDefaultTab = previous; }
            }
        }

        [Fact]
        public void CasualtyHeaderClicksSortAndMarkTheSameColumnInBothDirections()
        {
            using (new ControllerState())
            {
                var harmony = new Harmony("B1071.Tests.LedgerCasualties");
                try
                {
                    harmony.Patch(AccessTools.Method(typeof(B1071_ManpowerBehavior), "GetCasualtiesLedger"),
                        prefix: new HarmonyMethod(typeof(LedgerSortingTests), nameof(CasualtiesPrefix)));
                    Set("_activeTab", B1071LedgerTab.Casualties);
                    var behavior = (B1071_ManpowerBehavior)FormatterServices.GetUninitializedObject(typeof(B1071_ManpowerBehavior));
                    foreach (int header in new[] { 2, 3, 4 })
                    foreach (bool ascending in new[] { false, true })
                    {
                        Set("_sortColumn", -1);
                        B1071_OverlayController.SortByHeader(header);
                        if (ascending) B1071_OverlayController.SortByHeader(header);
                        Controller.GetMethod("BuildCasualtiesColumns", Static).Invoke(null, new object[] { behavior });
                        Assert.EndsWith(ascending ? " ↑" : " ↓", (string)Get("_header" + header));
                        foreach (int other in Enumerable.Range(1, 5).Where(h => h != header))
                            Assert.DoesNotContain(ascending ? "↑" : "↓", (string)Get("_header" + other));
                        var rows = ((IEnumerable<B1071_LedgerRowVM>)Get("_ledgerRows")).Reverse().ToArray();
                        string expected = header == 3 ? "Beta" : "Alpha";
                        if (ascending) expected = expected == "Alpha" ? "Beta" : "Alpha";
                        Assert.Contains(expected, rows[0].Cell1);
                    }
                }
                finally { harmony.UnpatchAll(harmony.Id); }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void TruceCountdownUsesDurationOnFirstAndOverflowPages(int page)
        {
            using (new ControllerState())
            {
                var harmony = new Harmony("B1071.Tests.LedgerTruces");
                try
                {
                    harmony.Patch(AccessTools.Method(typeof(B1071_ManpowerBehavior), "GetActiveTruces"),
                        prefix: new HarmonyMethod(typeof(LedgerSortingTests), nameof(TrucesPrefix)));
                    harmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"),
                        prefix: new HarmonyMethod(typeof(LedgerSortingTests), nameof(KingdomsPrefix)));
                    harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"),
                        prefix: new HarmonyMethod(typeof(LedgerSortingTests), nameof(HeroPrefix)));
                    Set("_activeTab", B1071LedgerTab.Wars);
                    Set("_pageIndex", page);
                    var behavior = (B1071_ManpowerBehavior)FormatterServices.GetUninitializedObject(typeof(B1071_ManpowerBehavior));
                    Controller.GetMethod("BuildWarsColumns", Static).Invoke(null, new object[] { behavior });
                    var rows = ((IEnumerable<B1071_LedgerRowVM>)Get("_ledgerRows")).Where(r => r.Cell3 == "Truce").ToArray();
                    Assert.NotEmpty(rows);
                    Assert.All(rows, row => { Assert.Equal("-", row.Cell2); Assert.Equal("26d left", row.Cell4); });
                    Assert.Equal(page, Get("_pageIndex"));
                }
                finally { harmony.UnpatchAll(harmony.Id); }
            }
        }

        private static bool CasualtiesPrefix(ref List<(string, string, string, int, int)> __result)
        {
            __result = new List<(string, string, string, int, int)> { ("a", "Alpha", "Enemy", 200, 50), ("b", "Beta", "Enemy", 10, 90) };
            return false;
        }
        private static bool TrucesPrefix(ref List<(string, string, float)> __result)
        {
            __result = Enumerable.Range(0, 40).Select(i => ("Realm " + i, "Other", 26f)).ToList();
            return false;
        }
        private static bool KingdomsPrefix(ref MBReadOnlyList<Kingdom> __result)
        {
            __result = new MBList<Kingdom>();
            return false;
        }
        private static bool HeroPrefix(ref Hero? __result) { __result = null; return false; }
        private static object Get(string name) => Controller.GetField(name, Static).GetValue(null);
        private static void Set(string name, object value) => Controller.GetField(name, Static).SetValue(null, value);

        private sealed class ControllerState : IDisposable
        {
            private readonly FieldInfo[] _fields = Controller.GetFields(Static).Where(f => !f.IsInitOnly && !f.IsLiteral).ToArray();
            private readonly object[] _values;
            private readonly B1071_LedgerRowVM[] _rows = ((IEnumerable<B1071_LedgerRowVM>)Get("_ledgerRows")).ToArray();
            public ControllerState() { _values = _fields.Select(f => f.GetValue(null)).ToArray(); }
            public void Dispose()
            {
                for (int i = 0; i < _fields.Length; i++) _fields[i].SetValue(null, _values[i]);
                var rows = (MBBindingList<B1071_LedgerRowVM>)Get("_ledgerRows");
                rows.Clear();
                foreach (var row in _rows) rows.Add(row);
            }
        }
    }
}
#endif
