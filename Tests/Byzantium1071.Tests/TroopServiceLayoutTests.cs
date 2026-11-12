using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class TroopServiceLayoutTests
    {
        [Fact]
        public void TooltipHeaderLeavesTheRosterInsideTheWindow()
        {
            XDocument prefab = XDocument.Load(RepositoryPaths.FromRoot("_Module", "GUI", "Prefabs", "B1071_Demobilization.xml"));
            XElement hint = prefab.Descendants("HintWidget").Single(e => (string?)e.Attribute("DataSource") == "{ServiceDaysHint}");
            XElement header = hint.Ancestors("ListPanel").First();
            // CoverChildren around a StretchToParent hint can consume the content
            // height and push the following roster below the window.
            Assert.Equal("Fixed", (string?)header.Attribute("HeightSizePolicy"));
            Assert.InRange((int)header.Attribute("SuggestedHeight")!, 18, 28);
            Assert.Equal("StretchToParent", (string?)hint.Parent!.Parent!.Attribute("HeightSizePolicy"));

            XElement content = header.Ancestors("ListPanel").First();
            XElement panel = content.Parent!.Parent!;
            int used = (int)content.Attribute("MarginTop")! + (int)content.Attribute("MarginBottom")!;
            foreach (XElement child in content.Element("Children")!.Elements())
            {
                if ((string?)child.Attribute("IsVisible") == "@HasNoCohorts") continue;
                Assert.Equal("Fixed", (string?)child.Attribute("HeightSizePolicy"));
                used += (int)child.Attribute("SuggestedHeight")!
                    + ((int?)child.Attribute("MarginTop") ?? 0) + ((int?)child.Attribute("MarginBottom") ?? 0);
            }
            Assert.True(used <= (int)panel.Attribute("SuggestedHeight")!, "The roster must fit above the bottom frame.");
            XElement scroll = prefab.Descendants("ScrollablePanel").Single();
            int viewportHeight = (int)scroll.Parent!.Parent!.Attribute("SuggestedHeight")!;
            XElement row = scroll.Descendants("ItemTemplate").Single().Element("Widget")!;
            int rowHeight = (int)row.Attribute("SuggestedHeight")! + (int)row.Attribute("MarginBottom")!;
            Assert.True(viewportHeight / rowHeight >= 13, "Keep the existing visible roster capacity.");
            Assert.Equal("true", (string?)scroll.Descendants("Widget")
                .Single(e => (string?)e.Attribute("Id") == "DemobClipRect").Attribute("ClipContents"));
        }
    }
}
