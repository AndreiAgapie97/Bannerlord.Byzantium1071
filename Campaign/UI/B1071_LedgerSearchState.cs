namespace Byzantium1071.Campaign.UI
{
    internal enum B1071SearchFilter { All, Hero, Place, Army, Market, Other }

    // Editing never changes the submitted query or pagination. The controller owns refresh.
    internal sealed class B1071_LedgerSearchState
    {
        internal string Draft { get; private set; } = string.Empty;
        internal string Query { get; private set; } = string.Empty;
        internal B1071SearchFilter Filter { get; set; }

        internal void Edit(string text) => Draft = text ?? string.Empty;
        internal void Submit() => Query = Draft.Trim();
        internal void Clear() { Draft = string.Empty; Query = string.Empty; Filter = B1071SearchFilter.All; }

        internal bool Includes(string category) => Filter == B1071SearchFilter.All ||
            (Filter == B1071SearchFilter.Other
                ? category != "Hero" && category != "Place" && category != "Army" && category != "Market"
                : category == Filter.ToString());
    }
}
