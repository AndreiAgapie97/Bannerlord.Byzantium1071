using System.Linq;
using System.Xml.Linq;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace Byzantium1071.Campaign.UI
{
    // Both movies use the same table markup and MapBarVM bindings. Only presentation differs.
    internal static class B1071_FullScreenLedgerLayout
    {
        internal const int RowHeight = 28;
        internal const int SidePadding = 40;
        internal const int ChromeHeight = 248;
        internal const int SearchHeight = 42;
        internal static readonly string Text = Build();

        private static string Build()
        {
            XElement compact = XElement.Parse(B1071_MapBarPanelLayout.Text);
            XElement panel = compact.Descendants().Single(e => (string)e.Attribute("SuggestedWidth") == "1200");
            panel.Remove();
            panel.SetAttributeValue("SuggestedWidth", "@B1071FullScreenWidth");
            panel.SetAttributeValue("SuggestedHeight", "@B1071FullScreenHeight");
            panel.SetAttributeValue("MarginTop", "0");
            panel.SetAttributeValue("HorizontalAlignment", "Center");
            panel.SetAttributeValue("VerticalAlignment", "Center");
            panel.Attribute("IsVisible")?.Remove();

            // The native canvas includes a decorative header and edge trim. Crop those
            // out of the entire background, as in the castle menu, before drawing our frame.
            XElement frame = panel.Elements("Children").Elements("BrushWidget").Single();
            XElement canvas = panel.Elements("Children").Elements("Widget").Single();
            frame.Remove();
            canvas.Remove();
            XElement backing = new XElement(canvas);
            backing.SetAttributeValue("Sprite", "BlankWhiteSquare_9");
            backing.SetAttributeValue("Color", "#181611FF");
            XElement crop = new XElement("Widget", canvas.Attributes());
            crop.Attribute("Sprite").Remove();
            crop.SetAttributeValue("ClipContents", "true");
            canvas.SetAttributeValue("MarginLeft", "-20");
            canvas.SetAttributeValue("MarginRight", "-20");
            canvas.SetAttributeValue("MarginTop", "-200");
            canvas.SetAttributeValue("MarginBottom", "-20");
            crop.Add(new XElement("Children", canvas));
            frame.SetAttributeValue("Brush", "B1071.PanelFrame");
            panel.Element("Children").AddFirst(backing, crop, frame);
            XElement contentStack = panel.Element("Children").Elements("ListPanel").Single();
            contentStack.SetAttributeValue("MarginLeft", SidePadding);
            contentStack.SetAttributeValue("MarginRight", SidePadding);

            foreach (XElement widget in panel.Descendants())
            {
                XAttribute font = widget.Attribute("Brush.FontSize");
                if (font != null) font.Value = "18";
                XAttribute height = widget.Attribute("SuggestedHeight");
                if (height != null && int.TryParse(height.Value, out int value) && value >= 20)
                    height.Value = (value < 28 ? 30 : 36).ToString();
                if ((string)widget.Attribute("IsVisible") == "@B1071SearchControlsVisible")
                    widget.SetAttributeValue("IsVisible", "@B1071FullScreenSearchVisible");
            }
            XElement rows = panel.Descendants().Single(e => (string)e.Attribute("DataSource") == "{B1071LedgerRows}");
            rows.Element("ItemTemplate").Elements().Single().SetAttributeValue("SuggestedHeight", "@FullScreenRowHeight");
            foreach (XElement text in rows.Descendants("TextWidget")) text.SetAttributeValue("Brush.FontSize", "20");
            XElement stripe = rows.Descendants("Widget").Single(e => (string)e.Attribute("IsVisible") == "@IsEven");
            stripe.SetAttributeValue("Color", "#FFFFFFFF");
            stripe.SetAttributeValue("AlphaFactor", "0.025");
            XElement factionTint = rows.Descendants("Widget").Single(e => (string)e.Attribute("IsVisible") == "@IsHighlighted");
            factionTint.SetAttributeValue("Color", "#D4B870FF");
            factionTint.SetAttributeValue("AlphaFactor", "0.06");
            XElement title = panel.Descendants("TextWidget").Single(e => (string)e.Attribute("Text") == "@B1071TitleText");
            title.SetAttributeValue("Brush.FontSize", "24");
            title.SetAttributeValue("HeightSizePolicy", "Fixed");
            title.SetAttributeValue("SuggestedHeight", "34");
            XElement detailLabel = rows.Descendants("TextWidget").Single(e => (string)e.Attribute("Text") == "@DetailLabel");
            detailLabel.SetAttributeValue("SuggestedWidth", "260");
            XElement details = detailLabel.Parent.Parent;
            details.SetAttributeValue("MarginTop", "@FullScreenDetailTopMargin");
            foreach (XElement text in details.Descendants("TextWidget")) text.SetAttributeValue("Brush.FontSize", "18");
            rows.Element("ItemTemplate").Elements().Single().Element("Children").Add(new XElement("Widget",
                new XAttribute("WidthSizePolicy", "StretchToParent"), new XAttribute("HeightSizePolicy", "Fixed"),
                new XAttribute("SuggestedHeight", "1"), new XAttribute("MarginTop", "8"),
                new XAttribute("Sprite", @"Encyclopedia\list_divider"), new XAttribute("AlphaFactor", "0.62"),
                new XAttribute("DoNotAcceptEvents", "true"), new XAttribute("IsVisible", "@BeginsDiagnostics")));
            foreach (XElement button in panel.Descendants("ButtonWidget"))
            {
                string command = (string)button.Attribute("Command.Click") ?? "";
                if (command.StartsWith("ExecuteB1071Filter") || command == "ExecuteB1071ClearSearch")
                    button.SetAttributeValue("SuggestedWidth", "90");
                if (command.StartsWith("ExecuteB1071Tab"))
                {
                    // Draw behind the labels and leave all clicks with the tab button.
                    button.Element("Children").AddFirst(new XElement("Widget",
                        new XAttribute("WidthSizePolicy", "StretchToParent"),
                        new XAttribute("HeightSizePolicy", "StretchToParent"),
                        new XAttribute("Sprite", "BlankWhiteSquare_9"),
                        new XAttribute("Color", "#D4B870FF"), new XAttribute("AlphaFactor", "0.12"),
                        new XAttribute("DoNotAcceptEvents", "true"),
                        new XAttribute("IsVisible", (string)button.Attribute("IsSelected"))));
                }
            }
            XElement modeButton = panel.Descendants("ButtonWidget").Single(e => (string)e.Attribute("Command.Click") == "ExecuteB1071FullScreen");
            modeButton.SetAttributeValue("Command.Click", "ExecuteB1071Compact");
            modeButton.Descendants("TextWidget").Single().SetAttributeValue("Text", "@B1071CompactText");
            XElement closeButton = panel.Descendants("ButtonWidget").Single(e => (string)e.Attribute("Command.Click") == "ExecuteB1071CloseLedger");
            closeButton.Remove();
            closeButton.SetAttributeValue("HorizontalAlignment", "Right");
            closeButton.SetAttributeValue("VerticalAlignment", "Center");
            closeButton.Attribute("MarginLeft")?.Remove();
            // Move the existing title above the tabs; the header uses the same 34 units.
            // Reserving the button width keeps long titles out of its click area.
            XElement content = title.Parent;
            title.Remove();
            title.SetAttributeValue("MarginRight", "152");
            content.AddFirst(new XElement("Widget", new XAttribute("WidthSizePolicy", "StretchToParent"),
                new XAttribute("HeightSizePolicy", "Fixed"), new XAttribute("SuggestedHeight", "34"),
                new XElement("Children", title, closeButton)));

            return new XElement("Widget", new XAttribute("WidthSizePolicy", "StretchToParent"),
                new XAttribute("HeightSizePolicy", "StretchToParent"), new XElement("Children",
                    new XElement("Widget", new XAttribute("WidthSizePolicy", "StretchToParent"),
                        new XAttribute("HeightSizePolicy", "StretchToParent"), new XAttribute("Sprite", "BlankWhiteSquare_9"),
                        new XAttribute("Color", "#000000E8"), new XAttribute("DoNotAcceptEvents", "true")), panel))
                .ToString(SaveOptions.DisableFormatting);
        }
    }

    [PrefabExtension("B1071_FullScreenLedger", "descendant::Widget[@Id='LedgerHost']/Children")]
    public sealed class B1071_FullScreenLedgerPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Child;
        [PrefabExtensionText] public string Text => B1071_FullScreenLedgerLayout.Text;
    }
}
