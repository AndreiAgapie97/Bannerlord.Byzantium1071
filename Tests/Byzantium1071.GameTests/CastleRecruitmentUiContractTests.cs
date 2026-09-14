#if GAME_TESTS_ENABLED
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Byzantium1071.Campaign.UI;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using Xunit;

namespace Byzantium1071.GameTests
{
    public sealed class CastleRecruitmentUiContractTests
    {
        private static XElement Prefab()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Byzantium1071.csproj")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return XElement.Load(Path.Combine(directory.FullName, "_Module", "GUI", "Prefabs", "B1071_CastleRecruitment.xml"));
        }

        [Fact]
        public void PrefabBindingsResolveInTheirActualDataContext()
        {
            CheckBindings(Prefab(), typeof(B1071_CastleRecruitmentVM));
        }

        private static void CheckBindings(XElement element, Type context)
        {
            string source = (string)element.Attribute("DataSource");
            if (source != null)
            {
                PropertyInfo property = context.GetProperty(source.Trim('{', '}'));
                Assert.NotNull(property);
                context = property.PropertyType.IsGenericType
                    ? property.PropertyType.GetGenericArguments()[0] : property.PropertyType;
            }
            // Collection widgets inherit their visibility from the parent VM;
            // their ItemTemplate alone switches to the collection's item context.
            Type attributeContext = source != null && element.Element("ItemTemplate") != null
                ? typeof(B1071_CastleRecruitmentVM) : context;
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.Value.StartsWith("@"))
                    Assert.NotNull(attributeContext.GetProperty(attribute.Value.Substring(1)));
                if (attribute.Name.LocalName.StartsWith("Command."))
                    Assert.NotNull(context.GetMethod(attribute.Value));
                if (attribute.Name.LocalName == "Brush.TextHorizontalAlignment")
                    Assert.Contains(attribute.Value, new[] { "Left", "Center", "Right" });
            }
            foreach (XElement child in element.Elements()) CheckBindings(child, context);
        }

        [Fact]
        public void AllDataSourceValueTypesCanBeRegisteredByTheInstalledGame()
        {
            Type[] supported = typeof(ViewModel).GetMethods()
                .Where(m => m.Name == "OnPropertyChangedWithValue" && !m.IsGenericMethod)
                .Select(m => m.GetParameters()[0].ParameterType).ToArray();
            foreach (Type type in new[] { typeof(B1071_CastleRecruitmentVM), typeof(B1071_CastleRecruitTroopVM), typeof(HintViewModel) })
            foreach (PropertyInfo property in type.GetProperties().Where(p => Attribute.IsDefined(p, typeof(DataSourceProperty))))
                Assert.True(!property.PropertyType.IsValueType || supported.Contains(property.PropertyType),
                    type.Name + "." + property.Name);
        }

        [Fact]
        public void OriginalWindowAndIndependentListAreasKeepTheirCapacity()
        {
            XElement root = Prefab();
            XElement panel = root.Descendants("Widget").Single(e => (string)e.Attribute("SuggestedWidth") == "820");
            Assert.Equal("650", (string)panel.Attribute("SuggestedHeight"));
            Assert.Equal(3, root.Descendants("ScrollablePanel").Count());
            foreach (string section in new[] { "Elite", "Ready", "Pending" })
            {
                XElement scroll = root.Descendants("ScrollablePanel")
                    .Single(e => (string)e.Attribute("ClipRect") == section + "ClipRect");
                Assert.Equal("..\\" + section + "Scrollbar", (string)scroll.Attribute("VerticalScrollbar"));
                Assert.Equal(section + "ClipRect\\" + section + "InnerPanel", (string)scroll.Attribute("InnerPanel"));
                XElement clip = scroll.Descendants("Widget").Single(e => (string)e.Attribute("Id") == section + "ClipRect");
                Assert.Equal("true", (string)clip.Attribute("ClipContents"));
                XElement viewport = scroll.Parent.Parent;
                if (section == "Pending")
                    Assert.Equal("StretchToParent", (string)viewport.Attribute("HeightSizePolicy"));
                else
                    Assert.Equal(section == "Elite" ? "145" : "116", (string)viewport.Attribute("SuggestedHeight"));
                XElement template = Assert.Single(clip.Descendants("ItemTemplate"));
                XElement row = template.Element("Widget");
                Assert.Equal("28", (string)row.Attribute("SuggestedHeight"));
                Assert.Equal("1", (string)row.Attribute("MarginBottom"));
                XElement cells = row.Descendants("ListPanel").Single().Element("Children");
                int fixedWidth = cells.Elements().Sum(e => (int?)e.Attribute("SuggestedWidth") ?? 0);
                // 820 panel - 48 side margins - 14 scrollbar inset - 8 row padding.
                int actionWidth = 750 - fixedWidth;
                Assert.True(actionWidth >= (section == "Pending" ? 100 : 216),
                    "Both recruitment buttons must fit beside fixed cells.");
                if (section != "Pending")
                {
                    XElement one = row.Descendants("ButtonWidget").Single(e => (string)e.Attribute("Command.Click") == "ExecuteRecruit");
                    XElement all = row.Descendants("ButtonWidget").Single(e => (string)e.Attribute("Command.Click") == "ExecuteRecruitAll");
                    Assert.True((int)one.Attribute("MarginRight") >= (int)all.Attribute("SuggestedWidth") + 6);
                    Assert.True((int)one.Attribute("SuggestedWidth") + (int)one.Attribute("MarginRight") <= actionWidth);
                    Assert.Equal("@CanRecruit", (string)all.Attribute("IsEnabled"));
                }
            }
            foreach (string section in new[] { "Elite", "Ready", "Pending" })
            {
                XElement empty = root.Descendants("TextWidget").Single(e => (string)e.Attribute("Text") == "@No" + section + "Text");
                if (section == "Pending")
                    Assert.Equal("StretchToParent", (string)empty.Attribute("HeightSizePolicy"));
                else
                    Assert.Equal(section == "Elite" ? "195" : "165", (string)empty.Attribute("SuggestedHeight"));
            }
            Assert.Equal(2, root.Descendants("ButtonWidget").Count(e => (string)e.Attribute("Command.Click") == "ExecuteRecruit"));
            Assert.Equal(2, root.Descendants("ButtonWidget").Count(e => (string)e.Attribute("Command.Click") == "ExecuteRecruitAll"));
        }
    }
}
#endif
