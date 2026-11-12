#if GAME_TESTS_ENABLED
using System;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Byzantium1071.Campaign.UI;
using Xunit;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(LedgerUiCollection))]
    public sealed class FullScreenLedgerTests
    {
        [Theory]
        [InlineData(1280, 720)]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1080)]
        [InlineData(1536, 864)] // 1080p with a larger interface scale.
        public void ScreenFitsColumnsAndAtLeastTenRowsWithoutScrolling(int width, int height)
        {
            foreach (B1071LedgerTab tab in Enum.GetValues(typeof(B1071LedgerTab)))
            {
                var columns = B1071_LedgerColumns.For(tab).Widen(width - 32 - 80);
                Assert.InRange(Math.Abs(columns.Widths.Sum() + 96 - (width - 112)), 0, 0.01f);
                var expectedAlignment = (TaleWorlds.TwoDimension.TextHorizontalAlignment[])B1071_LedgerColumns.For(tab).Alignments.Clone();
                if (tab == B1071LedgerTab.Current) expectedAlignment[4] = TaleWorlds.TwoDimension.TextHorizontalAlignment.Left;
                Assert.Equal(expectedAlignment, columns.Alignments);
                Assert.All(columns.Widths, w => Assert.True(w > 0));
            }
            foreach (bool search in new[] { false, true })
            {
                int rows = B1071_OverlayController.FullScreenRows(height - 32, search);
                int chrome = B1071_FullScreenLedgerLayout.ChromeHeight + (search ? B1071_FullScreenLedgerLayout.SearchHeight : 0);
                Assert.True(rows >= 10);
                Assert.True(rows * 28 + chrome <= height - 32);
                Assert.True((rows + 1) * 28 + chrome > height - 32);
            }
        }

        [Fact]
        public void FullScreenUsesTheSharedBindingsWithReadableRowsAndItsOwnSearchFocus()
        {
            XElement root = XElement.Parse(B1071_FullScreenLedgerLayout.Text);
            Assert.Equal("StretchToParent", (string)root.Attribute("WidthSizePolicy"));
            Assert.Equal("StretchToParent", (string)root.Attribute("HeightSizePolicy"));
            Type[] sources = { typeof(B1071_MapBarVMMixin), typeof(B1071_LedgerRowVM),
                typeof(TaleWorlds.Core.ViewModelCollection.Information.HintViewModel) };
            foreach (XAttribute attribute in root.DescendantsAndSelf().Attributes())
            {
                string value = attribute.Value;
                if (value.StartsWith("@")) Assert.True(sources.Any(t => t.GetProperty(value.Substring(1)) != null), value);
                if (attribute.Name.LocalName.StartsWith("Command.")) Assert.True(sources.Any(t => t.GetMethod(value) != null), value);
                if (attribute.Name.LocalName == "Brush.TextHorizontalAlignment") Assert.Contains(value, new[] { "Left", "Right", "Center" });
            }
            XElement rows = root.Descendants().Single(e => (string)e.Attribute("DataSource") == "{B1071LedgerRows}");
            Assert.Equal("@FullScreenRowHeight", (string)rows.Element("ItemTemplate").Elements().Single().Attribute("SuggestedHeight"));
            Assert.Equal(28, new B1071_LedgerRowVM().FullScreenRowHeight);
            var diagnostics = new B1071_LedgerRowVM { BeginsDiagnostics = true };
            Assert.Equal(44, diagnostics.FullScreenRowHeight);
            Assert.Equal(16, diagnostics.FullScreenDetailTopMargin);
            Assert.All(rows.Descendants("TextWidget"), e => Assert.Equal(
                ((string)e.Attribute("Text")).StartsWith("@Detail") ? "18" : "20", (string)e.Attribute("Brush.FontSize")));
            Assert.Equal("@B1071FullScreenSearchVisible", (string)root.Descendants("EditableTextWidget").Single().Attribute("IsVisible"));
            Assert.Equal("@B1071SearchControlsVisible", (string)XElement.Parse(B1071_MapBarPanelLayout.Text)
                .Descendants("EditableTextWidget").Single().Attribute("IsVisible"));
            var commands = root.Descendants("ButtonWidget").Select(e => (string)e.Attribute("Command.Click")).ToArray();
            Assert.Contains("ExecuteB1071Compact", commands);
            Assert.Single(commands.Where(command => command == "ExecuteB1071CloseLedger"));
            XElement compactRoot = XElement.Parse(B1071_MapBarPanelLayout.Text);
            XElement compactClose = compactRoot.Descendants("ButtonWidget").Single(e => (string)e.Attribute("Command.Click") == "ExecuteB1071CloseLedger");
            Assert.Equal("ExecuteB1071FullScreen", (string)compactClose.ElementsAfterSelf().First().Attribute("Command.Click"));
            Assert.Equal("@B1071CloseText", (string)compactClose.Descendants("TextWidget").Single().Attribute("Text"));
            Assert.DoesNotContain("ExecuteB1071FullScreen", commands);
            Assert.DoesNotContain(root.DescendantsAndSelf().Attributes(), a => a.Value == "@B1071PanelVisible");
        }

        [Fact]
        public void SwitchingSizeKeepsTabSortSearchAndTheCurrentEntryOnThePage()
        {
            Type controller = typeof(B1071_OverlayController);
            string[] names = { "_activeTab", "_pageIndex", "_sortColumn", "_sortAscending", "_columnsDirty", "_viewDirty" };
            FieldInfo[] fields = names.Select(n => controller.GetField(n, BindingFlags.Static | BindingFlags.NonPublic)).ToArray();
            object[] old = fields.Select(f => f.GetValue(null)).ToArray();
            var getRows = controller.GetMethod("GetRowsPerPage", BindingFlags.Static | BindingFlags.NonPublic);
            var search = (B1071_LedgerSearchState)controller.GetField("_search", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            string oldDraft = search.Draft, oldQuery = search.Query;
            var oldFilter = search.Filter;
            try
            {
                fields[0].SetValue(null, B1071LedgerTab.Search);
                fields[1].SetValue(null, 5);
                fields[2].SetValue(null, 3);
                fields[3].SetValue(null, false);
                search.Edit("imperial grain");
                search.Submit();
                search.Edit("another draft ");
                search.Filter = B1071SearchFilter.Market;
                string query = B1071_OverlayController.SearchQuery;
                int first = 5 * (int)getRows.Invoke(null, null);
                Assert.True(B1071_OverlayController.SetFullScreenViewport(1920, 1080));
                int size = (int)getRows.Invoke(null, null);
                int start = (int)fields[1].GetValue(null) * size;
                Assert.InRange(first, start, start + size - 1);
                Assert.False(B1071_OverlayController.SetFullScreenViewport(1920, 1080));
                Assert.Equal(B1071LedgerTab.Search, fields[0].GetValue(null));
                Assert.Equal(3, fields[2].GetValue(null));
                Assert.Equal(false, fields[3].GetValue(null));
                Assert.Equal(query, B1071_OverlayController.SearchQuery);
                Assert.Equal("imperial grain", search.Query);
                Assert.Equal(B1071SearchFilter.Market, search.Filter);
                Assert.True(B1071_OverlayController.SetFullScreenViewport(0, 0));
                Assert.False(B1071_OverlayController.IsFullScreen);
                Assert.Same(B1071_LedgerColumns.For(B1071LedgerTab.Search), B1071_OverlayController.Columns);
            }
            finally
            {
                B1071_OverlayController.SetFullScreenViewport(0, 0);
                search.Edit(oldQuery);
                search.Submit();
                search.Edit(oldDraft);
                search.Filter = oldFilter;
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, old[i]);
            }
        }
    }
}
#endif
