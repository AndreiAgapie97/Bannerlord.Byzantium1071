using Bannerlord.UIExtenderEx;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.UI
{
    internal static class B1071_MapBarPanelLayout
    {
        internal const string Text =
            // Root widget: positioned at top-left of screen
            "<Widget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" MarginLeft=\"22\" MarginTop=\"92\" MarginRight=\"22\" MarginBottom=\"22\" IsVisible=\"@B1071PanelVisible\">" +
            "<Children>" +
            "<ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">" +
            "<Children>" +
            // Toggle button
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"124\" SuggestedHeight=\"32\" Brush=\"MapInfoBarExtendButtonBrush\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071ToggleExpanded\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"MapTextBrush\" Brush.FontSize=\"16\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071ToggleText\"/>" +
            "</Children>" +
            "</ButtonWidget>" +
            // Main panel (780 x 290)
            "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"780\" SuggestedHeight=\"290\" MarginTop=\"6\" IsVisible=\"@B1071PanelExpanded\">" +
            "<Children>" +
            "<BrushWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.Frame\" DoNotAcceptEvents=\"true\"/>" +
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginLeft=\"5\" MarginRight=\"5\" MarginTop=\"5\" MarginBottom=\"5\" Sprite=\"Encyclopedia\\canvas\" DoNotAcceptEvents=\"true\"/>" +
            // Tab bar background strip — removed, using tab-row-level background instead
            // Inner content stack
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginLeft=\"24\" MarginRight=\"24\" MarginTop=\"14\" MarginBottom=\"16\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">" +
            "<Children>" +
            // === FOOTER ROW (renders at BOTTOM) ===
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
            "<Children>" +
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"52\" SuggestedHeight=\"28\" MarginTop=\"1\" Brush=\"Encyclopedia.FilterListButton\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071PrevPage\"><Children><ImageWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"24\" SuggestedHeight=\"24\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Encyclopedia.Previous.Page.Navigation.Button\"/></Children></ButtonWidget>" +
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"110\" SuggestedHeight=\"28\" MarginLeft=\"4\" MarginTop=\"1\" Brush=\"Encyclopedia.FilterListButton\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071ToggleSort\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071SortText\"/></Children></ButtonWidget>" +
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"52\" SuggestedHeight=\"28\" MarginLeft=\"4\" MarginTop=\"1\" Brush=\"Encyclopedia.FilterListButton\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071NextPage\"><Children><ImageWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"24\" SuggestedHeight=\"24\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Encyclopedia.Next.Page.Navigation.Button\"/></Children></ButtonWidget>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginLeft=\"16\" MarginTop=\"1\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D0C2A5FF\" Brush.TextHorizontalAlignment=\"Left\" Text=\"@B1071PageText\" />" +
            "</Children>" +
            "</ListPanel>" +
            // === DIVIDER ===
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"3\" MarginTop=\"6\" MarginBottom=\"6\" Sprite=\"Encyclopedia\\list_divider\" AlphaFactor=\"0.85\"/>" +
            // === CONTENT AREA (fixed height) ===
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"160\" MarginTop=\"6\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">" +
            "<Children>" +
            // Totals row (bottom visually — 4-column, gold)
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginBottom=\"2\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"268\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D4B870FF\" Brush.TextHorizontalAlignment=\"Left\" Brush.TextVerticalAlignment=\"Top\" Text=\"@B1071Totals1\" />" +
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"120\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D4B870FF\" Brush.TextHorizontalAlignment=\"Right\" Brush.TextVerticalAlignment=\"Top\" Text=\"@B1071Totals2\" />" +
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"100\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D4B870FF\" Brush.TextHorizontalAlignment=\"Right\" Brush.TextVerticalAlignment=\"Top\" MarginLeft=\"12\" Text=\"@B1071Totals3\" />" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D4B870FF\" Brush.TextHorizontalAlignment=\"Right\" Brush.TextVerticalAlignment=\"Top\" MarginLeft=\"12\" Text=\"@B1071Totals4\" />" +
            "</Children>" +
            "</ListPanel>" +
            // Data columns
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginTop=\"2\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"268\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"15\" Brush.TextHorizontalAlignment=\"Left\" Brush.TextVerticalAlignment=\"Top\" Text=\"@B1071Col1\" />" +
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"120\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"15\" Brush.TextHorizontalAlignment=\"Right\" Brush.TextVerticalAlignment=\"Top\" Text=\"@B1071Col2\" />" +
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"100\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"15\" Brush.TextHorizontalAlignment=\"Right\" Brush.TextVerticalAlignment=\"Top\" MarginLeft=\"12\" Text=\"@B1071Col3\" />" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"15\" Brush.TextHorizontalAlignment=\"Right\" Brush.TextVerticalAlignment=\"Top\" MarginLeft=\"12\" Text=\"@B1071Col4\" />" +
            "</Children>" +
            "</ListPanel>" +
            // Header-data divider (thin separator)
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"2\" MarginTop=\"3\" MarginBottom=\"3\" Sprite=\"Encyclopedia\\list_divider\" AlphaFactor=\"0.5\"/>" +
            // Column headers (muted, small)
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginBottom=\"2\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"268\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"13\" Brush.FontColor=\"#A89C84FF\" Brush.TextHorizontalAlignment=\"Left\" Brush.TextVerticalAlignment=\"Top\" Text=\"@B1071Header1\" />" +
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"120\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"13\" Brush.FontColor=\"#A89C84FF\" Brush.TextHorizontalAlignment=\"Right\" Brush.TextVerticalAlignment=\"Top\" Text=\"@B1071Header2\" />" +
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"100\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"13\" Brush.FontColor=\"#A89C84FF\" Brush.TextHorizontalAlignment=\"Right\" Brush.TextVerticalAlignment=\"Top\" MarginLeft=\"12\" Text=\"@B1071Header3\" />" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"13\" Brush.FontColor=\"#A89C84FF\" Brush.TextHorizontalAlignment=\"Right\" Brush.TextVerticalAlignment=\"Top\" MarginLeft=\"12\" Text=\"@B1071Header4\" />" +
            "</Children>" +
            "</ListPanel>" +
            // Title (top visually)
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"15\" Brush.TextHorizontalAlignment=\"Left\" Text=\"@B1071TitleText\" />" +
            "</Children>" +
            "</ListPanel>" +
            // === DIVIDER ===
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"3\" MarginTop=\"4\" MarginBottom=\"4\" Sprite=\"Encyclopedia\\list_divider\" AlphaFactor=\"0.85\"/>" +
            // === TAB ROW (renders at TOP) — with navbar background ===
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"36\" MarginTop=\"4\" MarginBottom=\"2\">" +
            "<Children>" +
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Sprite=\"Encyclopedia\\navbar\" DoNotAcceptEvents=\"true\"/>" +
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginLeft=\"4\" MarginRight=\"4\" MarginTop=\"3\" MarginBottom=\"3\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
            "<Children>" +
            "<ButtonWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.FilterListButton\" IsSelected=\"@B1071TabNearbySelected\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071TabNearby\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D8CCB0FF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071TabNearbyText\"/></Children></ButtonWidget>" +
            "<ButtonWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginLeft=\"4\" Brush=\"Encyclopedia.FilterListButton\" IsSelected=\"@B1071TabCastlesSelected\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071TabCastles\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D8CCB0FF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071TabCastlesText\"/></Children></ButtonWidget>" +
            "<ButtonWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginLeft=\"4\" Brush=\"Encyclopedia.FilterListButton\" IsSelected=\"@B1071TabTownsSelected\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071TabTowns\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D8CCB0FF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071TabTownsText\"/></Children></ButtonWidget>" +
            "<ButtonWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginLeft=\"4\" Brush=\"Encyclopedia.FilterListButton\" IsSelected=\"@B1071TabVillagesSelected\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071TabVillages\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D8CCB0FF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071TabVillagesText\"/></Children></ButtonWidget>" +
            "<ButtonWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginLeft=\"4\" Brush=\"Encyclopedia.FilterListButton\" IsSelected=\"@B1071TabFactionsSelected\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071TabFactions\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D8CCB0FF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071TabFactionsText\"/></Children></ButtonWidget>" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</Widget>" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</Widget>" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</Widget>";
    }

    // Injection guard: prevents the panel from being inserted multiple times
    // if multiple PrefabExtension paths match in a given game version.
    internal static class B1071_PanelInjectionGuard
    {
        private static bool _injected;
        internal static bool TryInject()
        {
            if (_injected) return false;
            _injected = true;
            return true;
        }
        internal static void Reset() => _injected = false;
    }

    [PrefabExtension("MapBar", "descendant::ListPanel[@Id='MapBar']")]
    [PrefabExtension("Map/MapBar", "descendant::ListPanel[@Id='MapBar']")]
    public sealed class B1071_MapBarPanelPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText(false)]
        public string Text => B1071_PanelInjectionGuard.TryInject() ? B1071_MapBarPanelLayout.Text : "<Widget />";
    }

    [PrefabExtension("MapBar", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    [PrefabExtension("Map/MapBar", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    public sealed class B1071_MapBarPanelPrefabExtensionAlt : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText(false)]
        public string Text => B1071_PanelInjectionGuard.TryInject() ? B1071_MapBarPanelLayout.Text : "<Widget />";
    }

    [ViewModelMixin(nameof(MapBarVM.OnRefresh), true)]
    public sealed class B1071_MapBarVMMixin : BaseViewModelMixin<MapBarVM>
    {
        private bool _panelVisible = true;
        private bool _panelExpanded = true;
        private string _panelText = "Byzantium 1071 Overlay\n[Press M to toggle]";
        private string _toggleText = "Hide";
        private string _tabNearbyText = "Nearby";
        private string _tabCastlesText = "Castles";
        private string _tabTownsText = "Towns";
        private string _tabFactionsText = "Factions";
        private string _tabVillagesText = "Villages";
        private bool _tabNearbySelected;
        private bool _tabCastlesSelected;
        private bool _tabTownsSelected;
        private bool _tabFactionsSelected;
        private bool _tabVillagesSelected;
        private string _sortText = "Sort ↓";
        private string _pageText = "Page 1/1";
        private string _titleText = "Loading...";
        private string _col1 = string.Empty;
        private string _col2 = string.Empty;
        private string _col3 = string.Empty;
        private string _col4 = string.Empty;
        private string _totals1 = string.Empty;
        private string _totals2 = string.Empty;
        private string _totals3 = string.Empty;
        private string _totals4 = string.Empty;
        private string _header1 = string.Empty;
        private string _header2 = string.Empty;
        private string _header3 = string.Empty;
        private string _header4 = string.Empty;

        public B1071_MapBarVMMixin(MapBarVM vm) : base(vm)
        {
            B1071_OverlayController.SetPanelMode(true);
        }

        public override void OnRefresh()
        {
            base.OnRefresh();
            SyncFromController(notifyAll: false);
        }

        public override void OnFinalize()
        {
            B1071_OverlayController.SetPanelMode(false);
            base.OnFinalize();
        }

        [DataSourceProperty]
        public bool B1071PanelVisible
        {
            get => _panelVisible;
            set => SetField(ref _panelVisible, value, nameof(B1071PanelVisible));
        }

        [DataSourceProperty]
        public bool B1071PanelExpanded
        {
            get => _panelExpanded;
            set => SetField(ref _panelExpanded, value, nameof(B1071PanelExpanded));
        }

        [DataSourceProperty]
        public string B1071PanelText
        {
            get => _panelText;
            set => SetField(ref _panelText, value, nameof(B1071PanelText));
        }

        [DataSourceProperty]
        public string B1071ToggleText
        {
            get => _toggleText;
            set => SetField(ref _toggleText, value, nameof(B1071ToggleText));
        }

        [DataSourceProperty]
        public string B1071TabNearbyText
        {
            get => _tabNearbyText;
            set => SetField(ref _tabNearbyText, value, nameof(B1071TabNearbyText));
        }

        [DataSourceProperty]
        public string B1071TabCastlesText
        {
            get => _tabCastlesText;
            set => SetField(ref _tabCastlesText, value, nameof(B1071TabCastlesText));
        }

        [DataSourceProperty]
        public string B1071TabTownsText
        {
            get => _tabTownsText;
            set => SetField(ref _tabTownsText, value, nameof(B1071TabTownsText));
        }

        [DataSourceProperty]
        public string B1071TabFactionsText
        {
            get => _tabFactionsText;
            set => SetField(ref _tabFactionsText, value, nameof(B1071TabFactionsText));
        }

        [DataSourceProperty]
        public string B1071TabVillagesText
        {
            get => _tabVillagesText;
            set => SetField(ref _tabVillagesText, value, nameof(B1071TabVillagesText));
        }

        [DataSourceProperty]
        public bool B1071TabNearbySelected
        {
            get => _tabNearbySelected;
            set => SetField(ref _tabNearbySelected, value, nameof(B1071TabNearbySelected));
        }

        [DataSourceProperty]
        public bool B1071TabCastlesSelected
        {
            get => _tabCastlesSelected;
            set => SetField(ref _tabCastlesSelected, value, nameof(B1071TabCastlesSelected));
        }

        [DataSourceProperty]
        public bool B1071TabTownsSelected
        {
            get => _tabTownsSelected;
            set => SetField(ref _tabTownsSelected, value, nameof(B1071TabTownsSelected));
        }

        [DataSourceProperty]
        public bool B1071TabFactionsSelected
        {
            get => _tabFactionsSelected;
            set => SetField(ref _tabFactionsSelected, value, nameof(B1071TabFactionsSelected));
        }

        [DataSourceProperty]
        public bool B1071TabVillagesSelected
        {
            get => _tabVillagesSelected;
            set => SetField(ref _tabVillagesSelected, value, nameof(B1071TabVillagesSelected));
        }

        [DataSourceProperty]
        public string B1071SortText
        {
            get => _sortText;
            set => SetField(ref _sortText, value, nameof(B1071SortText));
        }

        [DataSourceProperty]
        public string B1071PageText
        {
            get => _pageText;
            set => SetField(ref _pageText, value, nameof(B1071PageText));
        }

        [DataSourceProperty]
        public string B1071TitleText
        {
            get => _titleText;
            set => SetField(ref _titleText, value, nameof(B1071TitleText));
        }

        [DataSourceProperty]
        public string B1071Col1
        {
            get => _col1;
            set => SetField(ref _col1, value, nameof(B1071Col1));
        }

        [DataSourceProperty]
        public string B1071Col2
        {
            get => _col2;
            set => SetField(ref _col2, value, nameof(B1071Col2));
        }

        [DataSourceProperty]
        public string B1071Col3
        {
            get => _col3;
            set => SetField(ref _col3, value, nameof(B1071Col3));
        }

        [DataSourceProperty]
        public string B1071Col4
        {
            get => _col4;
            set => SetField(ref _col4, value, nameof(B1071Col4));
        }

        [DataSourceProperty]
        public string B1071Totals1
        {
            get => _totals1;
            set => SetField(ref _totals1, value, nameof(B1071Totals1));
        }

        [DataSourceProperty]
        public string B1071Totals2
        {
            get => _totals2;
            set => SetField(ref _totals2, value, nameof(B1071Totals2));
        }

        [DataSourceProperty]
        public string B1071Totals3
        {
            get => _totals3;
            set => SetField(ref _totals3, value, nameof(B1071Totals3));
        }

        [DataSourceProperty]
        public string B1071Totals4
        {
            get => _totals4;
            set => SetField(ref _totals4, value, nameof(B1071Totals4));
        }

        [DataSourceProperty]
        public string B1071Header1
        {
            get => _header1;
            set => SetField(ref _header1, value, nameof(B1071Header1));
        }

        [DataSourceProperty]
        public string B1071Header2
        {
            get => _header2;
            set => SetField(ref _header2, value, nameof(B1071Header2));
        }

        [DataSourceProperty]
        public string B1071Header3
        {
            get => _header3;
            set => SetField(ref _header3, value, nameof(B1071Header3));
        }

        [DataSourceProperty]
        public string B1071Header4
        {
            get => _header4;
            set => SetField(ref _header4, value, nameof(B1071Header4));
        }

        [DataSourceMethod]
        public void ExecuteB1071ToggleExpanded()
        {
            B1071_OverlayController.ToggleExpanded();
            B1071PanelExpanded = B1071_OverlayController.IsExpanded;
            B1071ToggleText = B1071PanelExpanded ? "Hide" : "Byz 1071";

            OnPropertyChangedWithValue(B1071PanelExpanded, nameof(B1071PanelExpanded));
            OnPropertyChangedWithValue(B1071ToggleText, nameof(B1071ToggleText));
        }

        [DataSourceMethod]
        public void ExecuteB1071TabNearby()
        {
            B1071_OverlayController.SetLedgerTab(B1071LedgerTab.NearbyPools);
            RefreshLedgerBindings();
        }

        [DataSourceMethod]
        public void ExecuteB1071TabCastles()
        {
            B1071_OverlayController.SetLedgerTab(B1071LedgerTab.Castles);
            RefreshLedgerBindings();
        }

        [DataSourceMethod]
        public void ExecuteB1071TabTowns()
        {
            B1071_OverlayController.SetLedgerTab(B1071LedgerTab.Towns);
            RefreshLedgerBindings();
        }

        [DataSourceMethod]
        public void ExecuteB1071TabFactions()
        {
            B1071_OverlayController.SetLedgerTab(B1071LedgerTab.Factions);
            RefreshLedgerBindings();
        }

        [DataSourceMethod]
        public void ExecuteB1071TabVillages()
        {
            B1071_OverlayController.SetLedgerTab(B1071LedgerTab.Villages);
            RefreshLedgerBindings();
        }

        [DataSourceMethod]
        public void ExecuteB1071PrevPage()
        {
            B1071_OverlayController.PreviousPage();
            RefreshLedgerBindings();
        }

        [DataSourceMethod]
        public void ExecuteB1071NextPage()
        {
            B1071_OverlayController.NextPage();
            RefreshLedgerBindings();
        }

        [DataSourceMethod]
        public void ExecuteB1071ToggleSort()
        {
            B1071_OverlayController.ToggleSort();
            RefreshLedgerBindings();
        }

        private void RefreshLedgerBindings()
        {
            B1071_OverlayController.RefreshNow();
            SyncFromController(notifyAll: true);
        }

        private void SyncFromController(bool notifyAll)
        {
            B1071PanelVisible = B1071_OverlayController.IsVisible;
            B1071PanelExpanded = B1071_OverlayController.IsExpanded;
            B1071PanelText = B1071_OverlayController.CurrentText;
            B1071ToggleText = B1071PanelExpanded ? "Hide" : "Byz 1071";
            B1071TabNearbyText = B1071_OverlayController.TabNearbyText;
            B1071TabCastlesText = B1071_OverlayController.TabCastlesText;
            B1071TabTownsText = B1071_OverlayController.TabTownsText;
            B1071TabFactionsText = B1071_OverlayController.TabFactionsText;
            B1071TabVillagesText = B1071_OverlayController.TabVillagesText;
            B1071TabNearbySelected = B1071_OverlayController.IsTabNearbyActive;
            B1071TabCastlesSelected = B1071_OverlayController.IsTabCastlesActive;
            B1071TabTownsSelected = B1071_OverlayController.IsTabTownsActive;
            B1071TabFactionsSelected = B1071_OverlayController.IsTabFactionsActive;
            B1071TabVillagesSelected = B1071_OverlayController.IsTabVillagesActive;
            B1071SortText = B1071_OverlayController.SortText;
            B1071PageText = B1071_OverlayController.PageText;
            B1071TitleText = B1071_OverlayController.TitleText;
            B1071Col1 = B1071_OverlayController.Col1Text;
            B1071Col2 = B1071_OverlayController.Col2Text;
            B1071Col3 = B1071_OverlayController.Col3Text;
            B1071Col4 = B1071_OverlayController.Col4Text;
            B1071Totals1 = B1071_OverlayController.Totals1;
            B1071Totals2 = B1071_OverlayController.Totals2;
            B1071Totals3 = B1071_OverlayController.Totals3;
            B1071Totals4 = B1071_OverlayController.Totals4;
            B1071Header1 = B1071_OverlayController.Header1;
            B1071Header2 = B1071_OverlayController.Header2;
            B1071Header3 = B1071_OverlayController.Header3;
            B1071Header4 = B1071_OverlayController.Header4;

            if (!notifyAll) return;

            OnPropertyChangedWithValue(B1071PanelText, nameof(B1071PanelText));
            OnPropertyChangedWithValue(B1071TabNearbyText, nameof(B1071TabNearbyText));
            OnPropertyChangedWithValue(B1071TabCastlesText, nameof(B1071TabCastlesText));
            OnPropertyChangedWithValue(B1071TabTownsText, nameof(B1071TabTownsText));
            OnPropertyChangedWithValue(B1071TabFactionsText, nameof(B1071TabFactionsText));
            OnPropertyChangedWithValue(B1071TabVillagesText, nameof(B1071TabVillagesText));
            OnPropertyChangedWithValue(B1071TabNearbySelected, nameof(B1071TabNearbySelected));
            OnPropertyChangedWithValue(B1071TabCastlesSelected, nameof(B1071TabCastlesSelected));
            OnPropertyChangedWithValue(B1071TabTownsSelected, nameof(B1071TabTownsSelected));
            OnPropertyChangedWithValue(B1071TabFactionsSelected, nameof(B1071TabFactionsSelected));
            OnPropertyChangedWithValue(B1071TabVillagesSelected, nameof(B1071TabVillagesSelected));
            OnPropertyChangedWithValue(B1071SortText, nameof(B1071SortText));
            OnPropertyChangedWithValue(B1071PageText, nameof(B1071PageText));
            OnPropertyChangedWithValue(B1071TitleText, nameof(B1071TitleText));
            OnPropertyChangedWithValue(B1071Col1, nameof(B1071Col1));
            OnPropertyChangedWithValue(B1071Col2, nameof(B1071Col2));
            OnPropertyChangedWithValue(B1071Col3, nameof(B1071Col3));
            OnPropertyChangedWithValue(B1071Col4, nameof(B1071Col4));
            OnPropertyChangedWithValue(B1071Totals1, nameof(B1071Totals1));
            OnPropertyChangedWithValue(B1071Totals2, nameof(B1071Totals2));
            OnPropertyChangedWithValue(B1071Totals3, nameof(B1071Totals3));
            OnPropertyChangedWithValue(B1071Totals4, nameof(B1071Totals4));
            OnPropertyChangedWithValue(B1071Header1, nameof(B1071Header1));
            OnPropertyChangedWithValue(B1071Header2, nameof(B1071Header2));
            OnPropertyChangedWithValue(B1071Header3, nameof(B1071Header3));
            OnPropertyChangedWithValue(B1071Header4, nameof(B1071Header4));
        }
    }
}
