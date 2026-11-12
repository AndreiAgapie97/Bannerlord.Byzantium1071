using TaleWorlds.TwoDimension;

namespace Byzantium1071.Campaign.UI
{
    // Five columns share 1152 units, including four 24-unit gutters.
    // Profiles are static: changing tabs does not allocate a new layout.
    internal sealed class B1071_LedgerColumns
    {
        internal readonly float[] Widths;
        internal readonly TextHorizontalAlignment[] Alignments;

        private B1071_LedgerColumns(float a, float b, float c, float d, string align)
        {
            Widths = new[] { a, b, c, d, 1056f - a - b - c - d };
            Alignments = new TextHorizontalAlignment[5];
            for (int i = 0; i < 5; i++)
                Alignments[i] = align[i] == 'R' ? TextHorizontalAlignment.Right : TextHorizontalAlignment.Left;
        }

        internal readonly float CharacterWidth = 7f;

        private B1071_LedgerColumns(B1071_LedgerColumns source, float innerWidth)
        {
            Widths = new float[5];
            float available = innerWidth - 96f;
            for (int i = 0; i < 5; i++) Widths[i] = source.Widths[i] * available / 1056f;
            Alignments = source.Alignments;
            // Current is a single settlement: keep its stats together. Other tabs use
            // their proportional profiles so surplus width is shared across all columns.
            if (ReferenceEquals(source, Current))
            {
                float scale = System.Math.Min(1f, available / 1300f);
                Widths[0] = 360f * scale;
                Widths[1] = 190f * scale;
                Widths[2] = 150f * scale;
                Widths[3] = 250f * scale;
                Widths[4] = available - Widths[0] - Widths[1] - Widths[2] - Widths[3];
                Alignments = (TextHorizontalAlignment[])source.Alignments.Clone();
                Alignments[4] = TextHorizontalAlignment.Left;
            }
            CharacterWidth = 9.5f;
        }

        internal B1071_LedgerColumns Widen(float innerWidth) => new B1071_LedgerColumns(this, innerWidth);

        private static readonly B1071_LedgerColumns Settlements = new(330, 170, 150, 150, "LRRRL");
        private static readonly B1071_LedgerColumns Current = new(310, 180, 190, 230, "LRRLR");
        private static readonly B1071_LedgerColumns Nearby = new(300, 170, 90, 130, "LRRRL");
        private static readonly B1071_LedgerColumns Villages = new(270, 100, 250, 250, "LRLLL");
        private static readonly B1071_LedgerColumns Factions = new(290, 190, 180, 220, "LLRRR");
        private static readonly B1071_LedgerColumns Armies = new(330, 150, 150, 270, "LRRLR");
        private static readonly B1071_LedgerColumns Wars = new(350, 270, 160, 130, "LLLRR");
        private static readonly B1071_LedgerColumns Casualties = new(430, 160, 160, 160, "LRRRR");
        private static readonly B1071_LedgerColumns Rebellion = new(350, 90, 250, 160, "LRLLL");
        private static readonly B1071_LedgerColumns Prisoners = new(270, 210, 180, 230, "LLLLL");
        private static readonly B1071_LedgerColumns Clans = new(260, 220, 250, 90, "LLLRL");
        private static readonly B1071_LedgerColumns Characters = new(300, 220, 260, 130, "LLLRR");
        private static readonly B1071_LedgerColumns Search = new(330, 190, 270, 110, "LLLRL");

        internal static B1071_LedgerColumns For(B1071LedgerTab tab) => tab switch
        {
            B1071LedgerTab.Current => Current,
            B1071LedgerTab.NearbyPools => Nearby,
            B1071LedgerTab.Villages => Villages,
            B1071LedgerTab.Factions => Factions,
            B1071LedgerTab.Armies => Armies,
            B1071LedgerTab.Wars => Wars,
            B1071LedgerTab.Casualties => Casualties,
            B1071LedgerTab.Rebellion => Rebellion,
            B1071LedgerTab.Prisoners => Prisoners,
            B1071LedgerTab.ClanInstability => Clans,
            B1071LedgerTab.Characters => Characters,
            B1071LedgerTab.Search => Search,
            _ => Settlements
        };
    }
}
