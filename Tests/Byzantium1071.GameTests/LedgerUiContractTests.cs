#if GAME_TESTS_ENABLED
using System;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Byzantium1071.Campaign.UI;
using TaleWorlds.TwoDimension;
using Xunit;

namespace Byzantium1071.GameTests
{
    [CollectionDefinition(nameof(LedgerUiCollection), DisableParallelization = true)]
    public sealed class LedgerUiCollection { }

    [Collection(nameof(LedgerUiCollection))]
    public sealed class LedgerUiContractTests
    {
        [Fact]
        public void ArrowNavigationMatchesTheRenderedTabsAndLeavesSearchEditingAlone()
        {
            foreach (string xml in new[] { B1071_MapBarPanelLayout.Text, B1071_FullScreenLedgerLayout.Text })
            {
                var tabs = XElement.Parse(xml).Descendants("ButtonWidget")
                    .Select(e => (string)e.Attribute("Command.Click") ?? "")
                    .Where(command => command.StartsWith("ExecuteB1071Tab"))
                    .Select(command => command.Substring("ExecuteB1071Tab".Length))
                    .Select(name => name == "Nearby" ? B1071LedgerTab.NearbyPools
                        : name == "Instability" ? B1071LedgerTab.ClanInstability
                        : (B1071LedgerTab)Enum.Parse(typeof(B1071LedgerTab), name)).ToArray();
                Assert.Equal(Enum.GetValues(typeof(B1071LedgerTab)).Length, tabs.Distinct().Count());
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool search = tabs[i] == B1071LedgerTab.Search;
                    Assert.Equal(search ? tabs[i] : tabs[(i + 1) % tabs.Length],
                        B1071_OverlayController.AdjacentTab(tabs[i], next: true));
                    Assert.Equal(search ? tabs[i] : tabs[(i + tabs.Length - 1) % tabs.Length],
                        B1071_OverlayController.AdjacentTab(tabs[i], next: false));
                }
            }
        }

        [Fact]
        public void EveryTabFitsTheExistingInnerWidth()
        {
            var tabs = (B1071LedgerTab[])Enum.GetValues(typeof(B1071LedgerTab));
            Assert.Equal(14, tabs.Length);
            foreach (B1071LedgerTab tab in tabs)
            {
                B1071_LedgerColumns columns = B1071_LedgerColumns.For(tab);
                Assert.Equal(1152f, columns.Widths.Sum() + 4 * 24);
                Assert.All(columns.Widths, width => Assert.True(width >= 80));
                Assert.Equal(5, columns.Alignments.Length);
                Assert.Equal(TextHorizontalAlignment.Left, columns.Alignments[0]);
            }
        }

        [Fact]
        public void EditingDoesNotSubmitOrLoseSpacesAndFilteringPreservesTheQuery()
        {
            var state = new B1071_LedgerSearchState();
            state.Edit("  imperial grain  ");
            Assert.Equal(string.Empty, state.Query);
            Assert.Equal("  imperial grain  ", state.Draft);
            state.Submit();
            Assert.Equal("imperial grain", state.Query);
            state.Edit("another query ");
            state.Filter = B1071SearchFilter.Market;
            Assert.Equal("imperial grain", state.Query);
            Assert.True(state.Includes("Market"));
            Assert.False(state.Includes("Hero"));
            state.Clear();
            Assert.Equal(string.Empty, state.Query);
            Assert.Equal(string.Empty, state.Draft);
            Assert.Equal(B1071SearchFilter.All, state.Filter);
        }

        [Theory]
        [InlineData("Hero", B1071SearchFilter.Hero)]
        [InlineData("Place", B1071SearchFilter.Place)]
        [InlineData("Army", B1071SearchFilter.Army)]
        [InlineData("Market", B1071SearchFilter.Market)]
        [InlineData("Clan", B1071SearchFilter.Other)]
        [InlineData("Kingdom", B1071SearchFilter.Other)]
        public void EachSearchCategoryHasExactlyOneSpecificFilter(string category, object expected)
        {
            var state = new B1071_LedgerSearchState();
            Assert.True(state.Includes(category));
            var matching = Enum.GetValues(typeof(B1071SearchFilter)).Cast<B1071SearchFilter>()
                .Where(filter => filter != B1071SearchFilter.All)
                .Where(filter => { state.Filter = filter; return state.Includes(category); }).ToArray();
            Assert.Equal(new[] { (B1071SearchFilter)expected }, matching);
        }

        [Fact]
        public void DataSourcesUseTypesSupportedByTheInstalledGame()
        {
            // Registration selects a concrete notification overload for value types. Unsupported
            // enums fall back to a generic method constrained to reference types and throw.
            Type vm = typeof(TaleWorlds.Library.ViewModel);
            Type[] supportedValues = vm.GetMethods().Where(m => m.Name == "OnPropertyChangedWithValue" && !m.IsGenericMethod)
                .Select(m => m.GetParameters()[0].ParameterType).ToArray();
            foreach (Type source in new[] { typeof(B1071_MapBarVMMixin), typeof(B1071_LedgerRowVM) })
            foreach (PropertyInfo property in source.GetProperties())
            {
                if (!Attribute.IsDefined(property, typeof(TaleWorlds.Library.DataSourceProperty))) continue;
                Assert.True(!property.PropertyType.IsValueType || supportedValues.Contains(property.PropertyType),
                    source.Name + "." + property.Name + ": " + property.PropertyType.Name);
            }
            Assert.NotNull(new B1071_LedgerRowVM());
            XElement root = XElement.Parse(B1071_MapBarPanelLayout.Text);
            Assert.All(root.DescendantsAndSelf().Attributes("Brush.TextHorizontalAlignment"),
                a => Assert.Contains(a.Value, new[] { "Left", "Center", "Right" }));
        }

        [Fact]
        public void AllInlineBindingsAndCommandsResolve()
        {
            XElement root = XElement.Parse(B1071_MapBarPanelLayout.Text);
            Type[] sources = { typeof(B1071_MapBarVMMixin), typeof(B1071_LedgerRowVM),
                typeof(TaleWorlds.Core.ViewModelCollection.Information.HintViewModel) };
            foreach (XAttribute attribute in root.DescendantsAndSelf().Attributes())
            {
                string value = attribute.Value;
                if (value.StartsWith("@"))
                    Assert.True(sources.Any(type => type.GetProperty(value.Substring(1)) != null), value);
                if (attribute.Name.LocalName.StartsWith("Command."))
                    Assert.True(sources.Any(type => type.GetMethod(value) != null), value);
                if (attribute.Name.LocalName == "DataSource")
                    Assert.True(sources.Any(type => type.GetProperty(value.Trim('{', '}')) != null), value);
            }
        }

        [Fact]
        public void TableKeepsWidthDensityAndHeaderRowTotalsOrder()
        {
            XElement root = XElement.Parse(B1071_MapBarPanelLayout.Text);
            Assert.Single(root.Descendants().Where(e => (string)e.Attribute("SuggestedWidth") == "1200"));
            XElement rows = root.Descendants().Single(e => (string)e.Attribute("DataSource") == "{B1071LedgerRows}");
            Assert.Equal("20", (string)rows.Element("ItemTemplate").Elements().Single().Attribute("SuggestedHeight"));
            XElement content = rows.Parent.Parent;
            Assert.Equal("VerticalTopToBottom", (string)content.Attribute("StackLayout.LayoutMethod"));
            XElement[] sections = rows.Parent.Elements().ToArray();
            Assert.True(Array.IndexOf(sections, rows) > 0);
            Assert.Contains(sections.Skip(Array.IndexOf(sections, rows) + 1),
                e => (string)e.Attribute("IsVisible") == "@B1071TotalsVisible");
            Assert.Equal(9, rows.Descendants("ListPanel").Single(e => (string)e.Attribute("IsVisible") == "@IsTableRow").Descendants("TextWidget").Count());
            Assert.Equal(2, rows.Descendants("ListPanel").Single(e => (string)e.Attribute("IsVisible") == "@IsDetail").Descendants("TextWidget").Count());
            Assert.All(root.Descendants("ButtonWidget").Where(e => ((string)e.Attribute("Command.Click") ?? "").StartsWith("ExecuteB1071SortCol")).Skip(1),
                e => Assert.Equal("24", (string)e.Attribute("MarginLeft")));
            Assert.All(rows.Descendants("TextWidget"), e => Assert.Equal("true", (string)e.Attribute("ClipContents")));
            Assert.Contains(root.Descendants("ButtonWidget"), e => (string)e.Attribute("IsEnabled") == "@B1071CanNextPage");
            Assert.Contains(root.Descendants("ButtonWidget"), e => (string)e.Attribute("IsEnabled") == "@B1071CanPreviousPage");
        }

        [Fact]
        public void LongValuesKeepTheirHintAndUnchangedUpdatesReuseIt()
        {
            string name = new string('W', 150);
            var row = new B1071_LedgerRowVM(name, "123", "Details", "-", false, true);
            Assert.True(row.Cell1.Length < name.Length);
            var hint = row.Hint;
            row.Update(name, "123", "Details", "-", false, true);
            Assert.Same(hint, row.Hint);
            row.Update(name, "124", "Details", "-", false, true);
            Assert.NotSame(hint, row.Hint);
        }

        [Theory]
        [InlineData(0, 3f, "Rebellious")]
        [InlineData(1, 25f, "Low loyalty")]
        [InlineData(3, 28f, "~3d")]
        [InlineData(int.MaxValue, 50f, "No decline")]
        [InlineData(999, 70f, "~999+d")]
        public void RebellionOutlookDoesNotPromiseAnActualRevolt(int days, float loyalty, string expected)
        {
            Assert.Equal(expected, B1071_OverlayController.FormatRebellionOutlook(days, loyalty));
        }

        [Fact]
        public void CurrentDetailsUseTheWholeValueAreaAndSkipEmptyCells()
        {
            var row = new B1071_LedgerRowVM("Status", "", "Regen -30%", "War Pressure: Crisis", false, false, isDetail: true);
            Assert.True(row.IsDetail);
            Assert.False(row.IsTableRow);
            Assert.Equal("Status", row.DetailLabel);
            Assert.Equal("Regen -30%  |  War Pressure: Crisis", row.DetailText);
            row.Update("Diagnostics", "A", "", "B", false, false);
            Assert.Equal("A  |  B", row.DetailText);
            Assert.False(new B1071_LedgerRowVM().IsDetail);
        }

        [Fact]
        public void CasualtySummaryLabelsCombinedDeathsAndRetainsTheFullWarName()
        {
            Type controller = typeof(B1071_OverlayController);
            string[] names = { "_activeTab", "_totals2", "_totals3", "_totals4" };
            FieldInfo[] fields = names.Select(name => controller.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)).ToArray();
            object[] previous = fields.Select(field => field.GetValue(null)).ToArray();
            try
            {
                fields[0].SetValue(null, B1071LedgerTab.Casualties);
                fields[1].SetValue(null, "173,305");
                fields[2].SetValue(null, "Bloodiest: Calradian Empire/Khuzait");
                fields[3].SetValue(null, "Wars: 13");
                Assert.True(B1071_OverlayController.UsesSummary);
                Assert.Equal("Total deaths: 173,305  |  Wars: 13  |  Bloodiest: Calradian Empire/Khuzait", B1071_OverlayController.SummaryText);
                fields[0].SetValue(null, B1071LedgerTab.Towns);
                Assert.False(B1071_OverlayController.UsesSummary);
            }
            finally { for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, previous[i]); }
        }

        [Fact]
        public void PaginationClampsShrinkingResultsAndDisablesEndpoints()
        {
            Type controller = typeof(B1071_OverlayController);
            FieldInfo page = controller.GetField("_pageIndex", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo count = controller.GetField("_pageCount", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo tab = controller.GetField("_activeTab", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo paginate = controller.GetMethod("GetPageStart", BindingFlags.NonPublic | BindingFlags.Static);
            object oldPage = page.GetValue(null), oldCount = count.GetValue(null), oldTab = tab.GetValue(null);
            try
            {
                tab.SetValue(null, B1071LedgerTab.Characters);
                page.SetValue(null, 0);
                Assert.Equal(0, paginate.Invoke(null, new object[] { 951, 9 }));
                Assert.False(B1071_OverlayController.CanPreviousPage);
                Assert.True(B1071_OverlayController.CanNextPage);
                page.SetValue(null, 105);
                Assert.Equal(945, paginate.Invoke(null, new object[] { 951, 9 }));
                Assert.False(B1071_OverlayController.CanNextPage);
                Assert.True(B1071_OverlayController.CanPreviousPage);
                Assert.Equal(0, paginate.Invoke(null, new object[] { 2, 9 }));
                Assert.False(B1071_OverlayController.CanPreviousPage);
                Assert.False(B1071_OverlayController.CanNextPage);
                Assert.Equal(0, paginate.Invoke(null, new object[] { 0, 9 }));
            }
            finally { page.SetValue(null, oldPage); count.SetValue(null, oldCount); tab.SetValue(null, oldTab); }
        }
    }
}
#endif
