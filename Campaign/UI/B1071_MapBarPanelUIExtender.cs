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
            "<Widget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" MarginLeft=\"22\" MarginTop=\"92\" MarginRight=\"22\" MarginBottom=\"22\" IsVisible=\"@B1071PanelVisible\">" +
            "<Children>" +
            "<ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">" +
            "<Children>" +
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"124\" SuggestedHeight=\"32\" Brush=\"MapInfoBarExtendButtonBrush\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071ToggleExpanded\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"MapTextBrush\" Brush.FontSize=\"16\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071ToggleText\"/>" +
            "</Children>" +
            "</ButtonWidget>" +
            "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"552\" SuggestedHeight=\"258\" MarginTop=\"6\" IsVisible=\"@B1071PanelExpanded\">" +
            "<Children>" +
            "<BrushWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.Frame\" DoNotAcceptEvents=\"true\"/>" +
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginLeft=\"5\" MarginRight=\"5\" MarginTop=\"5\" MarginBottom=\"5\" Sprite=\"Encyclopedia\\canvas\" DoNotAcceptEvents=\"true\"/>" +
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"34\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" MarginLeft=\"10\" MarginRight=\"10\" MarginTop=\"8\" Sprite=\"Encyclopedia\\navbar\"/>" +
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginLeft=\"38\" MarginRight=\"38\" MarginTop=\"23\" MarginBottom=\"18\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">" +
            "<Children>" +
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
            "<Children>" +
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"52\" SuggestedHeight=\"28\" MarginTop=\"1\" Brush=\"Encyclopedia.NoAudioButton\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071PrevPage\"><Children><ImageWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"24\" SuggestedHeight=\"24\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Encyclopedia.Previous.Page.Navigation.Button\"/></Children></ButtonWidget>" +
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"96\" SuggestedHeight=\"28\" MarginLeft=\"4\" MarginTop=\"1\" Brush=\"Encyclopedia.NoAudioButton\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071ToggleSort\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071SortText\"/></Children></ButtonWidget>" +
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"52\" SuggestedHeight=\"28\" MarginLeft=\"4\" MarginTop=\"1\" Brush=\"Encyclopedia.NoAudioButton\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071NextPage\"><Children><ImageWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"24\" SuggestedHeight=\"24\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Encyclopedia.Next.Page.Navigation.Button\"/></Children></ButtonWidget>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginLeft=\"16\" MarginTop=\"1\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D0C2A5FF\" Brush.TextHorizontalAlignment=\"Left\" Text=\"@B1071PageText\" />" +
            "</Children>" +
            "</ListPanel>" +
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"3\" MarginTop=\"6\" MarginBottom=\"6\" Sprite=\"Encyclopedia\\list_divider\" AlphaFactor=\"0.85\"/>" +
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"130\" MarginTop=\"10\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.SubPage.History.Text\" Brush.FontSize=\"16\" Brush.TextHorizontalAlignment=\"Left\" Brush.TextVerticalAlignment=\"Top\" Text=\"@B1071PanelText\" />" +
            "</Children>" +
            "</Widget>" +
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"3\" MarginTop=\"6\" MarginBottom=\"6\" Sprite=\"Encyclopedia\\list_divider\" AlphaFactor=\"0.85\"/>" +
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginTop=\"5\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
            "<Children>" +
            "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"102\" SuggestedHeight=\"30\"><Children><ButtonWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.FilterListButton\" IsSelected=\"@B1071TabNearbySelected\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071TabNearby\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" PositionYOffset=\"1\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D8CCB0FF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071TabNearbyText\"/></Children></ButtonWidget><Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"2\" MarginLeft=\"12\" MarginRight=\"12\" VerticalAlignment=\"Bottom\" Sprite=\"Encyclopedia\\list_hover\" AlphaFactor=\"0.82\" IsVisible=\"@B1071TabNearbySelected\"/></Children></Widget>" +
            "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"102\" SuggestedHeight=\"30\" MarginLeft=\"6\"><Children><ButtonWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.FilterListButton\" IsSelected=\"@B1071TabPoolsSelected\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071TabPools\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" PositionYOffset=\"1\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D8CCB0FF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071TabPoolsText\"/></Children></ButtonWidget><Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"2\" MarginLeft=\"12\" MarginRight=\"12\" VerticalAlignment=\"Bottom\" Sprite=\"Encyclopedia\\list_hover\" AlphaFactor=\"0.82\" IsVisible=\"@B1071TabPoolsSelected\"/></Children></Widget>" +
            "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"102\" SuggestedHeight=\"30\" MarginLeft=\"6\"><Children><ButtonWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.FilterListButton\" IsSelected=\"@B1071TabWorldSelected\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071TabWorld\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" PositionYOffset=\"1\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D8CCB0FF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071TabWorldText\"/></Children></ButtonWidget><Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"2\" MarginLeft=\"12\" MarginRight=\"12\" VerticalAlignment=\"Bottom\" Sprite=\"Encyclopedia\\list_hover\" AlphaFactor=\"0.82\" IsVisible=\"@B1071TabWorldSelected\"/></Children></Widget>" +
            "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"102\" SuggestedHeight=\"30\" MarginLeft=\"6\"><Children><ButtonWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Encyclopedia.FilterListButton\" IsSelected=\"@B1071TabFactionsSelected\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071TabFactions\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" PositionYOffset=\"1\" Brush=\"Encyclopedia.SubPage.Element.Name.Text\" Brush.FontSize=\"14\" Brush.FontColor=\"#D8CCB0FF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071TabFactionsText\"/></Children></ButtonWidget><Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"2\" MarginLeft=\"12\" MarginRight=\"12\" VerticalAlignment=\"Bottom\" Sprite=\"Encyclopedia\\list_hover\" AlphaFactor=\"0.82\" IsVisible=\"@B1071TabFactionsSelected\"/></Children></Widget>" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</Widget>" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</Widget>";
    }

    [PrefabExtension("MapBar", "descendant::ListPanel[@Id='MapBar']")]
    [PrefabExtension("Map/MapBar", "descendant::ListPanel[@Id='MapBar']")]
    [PrefabExtension("MapBarView", "descendant::ListPanel[@Id='MapBar']")]
    [PrefabExtension("Map\\MapBar", "descendant::ListPanel[@Id='MapBar']")]
    public sealed class B1071_MapBarPanelPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText(false)]
        public string Text => B1071_MapBarPanelLayout.Text;
    }

    [PrefabExtension("MapBar", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    [PrefabExtension("Map/MapBar", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    [PrefabExtension("MapBarView", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    [PrefabExtension("Map\\MapBar", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    public sealed class B1071_MapBarPanelPrefabExtensionAlt : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText(false)]
        public string Text => B1071_MapBarPanelLayout.Text;
    }

    [ViewModelMixin(nameof(MapBarVM.OnRefresh), true)]
    public sealed class B1071_MapBarVMMixin : BaseViewModelMixin<MapBarVM>
    {
        private bool _panelVisible = true;
        private bool _panelExpanded = true;
        private string _panelText = "Byzantium 1071 Overlay\n[Press M to toggle]";
        private string _toggleText = "Hide";
        private string _tabNearbyText = "Nearby";
        private string _tabPoolsText = "Pools";
        private string _tabWorldText = "World";
        private string _tabFactionsText = "Factions";
        private bool _tabNearbySelected;
        private bool _tabPoolsSelected;
        private bool _tabWorldSelected;
        private bool _tabFactionsSelected;
        private string _sortText = "Sort ↓";
        private string _pageText = "Page 1/1";

        public B1071_MapBarVMMixin(MapBarVM vm) : base(vm)
        {
            B1071_OverlayController.SetPanelMode(true);
        }

        public override void OnRefresh()
        {
            base.OnRefresh();

            B1071PanelVisible = B1071_OverlayController.IsVisible;
            B1071PanelExpanded = B1071_OverlayController.IsExpanded;
            B1071PanelText = B1071_OverlayController.CurrentText;
            B1071ToggleText = B1071PanelExpanded ? "Hide" : "Byz 1071";
            B1071TabNearbyText = B1071_OverlayController.TabNearbyText;
            B1071TabPoolsText = B1071_OverlayController.TabPoolsText;
            B1071TabWorldText = B1071_OverlayController.TabWorldText;
            B1071TabFactionsText = B1071_OverlayController.TabFactionsText;
            B1071TabNearbySelected = B1071_OverlayController.IsTabNearbyActive;
            B1071TabPoolsSelected = B1071_OverlayController.IsTabPoolsActive;
            B1071TabWorldSelected = B1071_OverlayController.IsTabWorldActive;
            B1071TabFactionsSelected = B1071_OverlayController.IsTabFactionsActive;
            B1071SortText = B1071_OverlayController.SortText;
            B1071PageText = B1071_OverlayController.PageText;
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
        public string B1071TabPoolsText
        {
            get => _tabPoolsText;
            set => SetField(ref _tabPoolsText, value, nameof(B1071TabPoolsText));
        }

        [DataSourceProperty]
        public string B1071TabWorldText
        {
            get => _tabWorldText;
            set => SetField(ref _tabWorldText, value, nameof(B1071TabWorldText));
        }

        [DataSourceProperty]
        public string B1071TabFactionsText
        {
            get => _tabFactionsText;
            set => SetField(ref _tabFactionsText, value, nameof(B1071TabFactionsText));
        }

        [DataSourceProperty]
        public bool B1071TabNearbySelected
        {
            get => _tabNearbySelected;
            set => SetField(ref _tabNearbySelected, value, nameof(B1071TabNearbySelected));
        }

        [DataSourceProperty]
        public bool B1071TabPoolsSelected
        {
            get => _tabPoolsSelected;
            set => SetField(ref _tabPoolsSelected, value, nameof(B1071TabPoolsSelected));
        }

        [DataSourceProperty]
        public bool B1071TabWorldSelected
        {
            get => _tabWorldSelected;
            set => SetField(ref _tabWorldSelected, value, nameof(B1071TabWorldSelected));
        }

        [DataSourceProperty]
        public bool B1071TabFactionsSelected
        {
            get => _tabFactionsSelected;
            set => SetField(ref _tabFactionsSelected, value, nameof(B1071TabFactionsSelected));
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
        public void ExecuteB1071TabPools()
        {
            B1071_OverlayController.SetLedgerTab(B1071LedgerTab.Pools);
            RefreshLedgerBindings();
        }

        [DataSourceMethod]
        public void ExecuteB1071TabWorld()
        {
            B1071_OverlayController.SetLedgerTab(B1071LedgerTab.World);
            RefreshLedgerBindings();
        }

        [DataSourceMethod]
        public void ExecuteB1071TabFactions()
        {
            B1071_OverlayController.SetLedgerTab(B1071LedgerTab.Factions);
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

            B1071PanelText = B1071_OverlayController.CurrentText;
            B1071TabNearbyText = B1071_OverlayController.TabNearbyText;
            B1071TabPoolsText = B1071_OverlayController.TabPoolsText;
            B1071TabWorldText = B1071_OverlayController.TabWorldText;
            B1071TabFactionsText = B1071_OverlayController.TabFactionsText;
            B1071TabNearbySelected = B1071_OverlayController.IsTabNearbyActive;
            B1071TabPoolsSelected = B1071_OverlayController.IsTabPoolsActive;
            B1071TabWorldSelected = B1071_OverlayController.IsTabWorldActive;
            B1071TabFactionsSelected = B1071_OverlayController.IsTabFactionsActive;
            B1071SortText = B1071_OverlayController.SortText;
            B1071PageText = B1071_OverlayController.PageText;

            OnPropertyChangedWithValue(B1071PanelText, nameof(B1071PanelText));
            OnPropertyChangedWithValue(B1071TabNearbyText, nameof(B1071TabNearbyText));
            OnPropertyChangedWithValue(B1071TabPoolsText, nameof(B1071TabPoolsText));
            OnPropertyChangedWithValue(B1071TabWorldText, nameof(B1071TabWorldText));
            OnPropertyChangedWithValue(B1071TabFactionsText, nameof(B1071TabFactionsText));
            OnPropertyChangedWithValue(B1071TabNearbySelected, nameof(B1071TabNearbySelected));
            OnPropertyChangedWithValue(B1071TabPoolsSelected, nameof(B1071TabPoolsSelected));
            OnPropertyChangedWithValue(B1071TabWorldSelected, nameof(B1071TabWorldSelected));
            OnPropertyChangedWithValue(B1071TabFactionsSelected, nameof(B1071TabFactionsSelected));
            OnPropertyChangedWithValue(B1071SortText, nameof(B1071SortText));
            OnPropertyChangedWithValue(B1071PageText, nameof(B1071PageText));
        }
    }
}
