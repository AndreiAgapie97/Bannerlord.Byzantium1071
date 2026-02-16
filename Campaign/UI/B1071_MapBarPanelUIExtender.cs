using Bannerlord.UIExtenderEx;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.UI
{
    [PrefabExtension("MapBar", "descendant::ListPanel[@Id='MapBar']")]
    [PrefabExtension("Map/MapBar", "descendant::ListPanel[@Id='MapBar']")]
    [PrefabExtension("MapBarView", "descendant::ListPanel[@Id='MapBar']")]
    [PrefabExtension("Map\\MapBar", "descendant::ListPanel[@Id='MapBar']")]
    public sealed class B1071_MapBarPanelPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText(false)]
        public string Text =>
            "<Widget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" MarginLeft=\"22\" MarginTop=\"92\" IsVisible=\"@B1071PanelVisible\">" +
            "<Children>" +
            "<ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">" +
            "<Children>" +
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"124\" SuggestedHeight=\"32\" Brush=\"MapInfoBarExtendButtonBrush\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071ToggleExpanded\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"MapTextBrush\" Brush.FontSize=\"16\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071ToggleText\"/>" +
            "</Children>" +
            "</ButtonWidget>" +
            "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"360\" MarginTop=\"4\" Sprite=\"MapBar\\mapbar_right_frame\" NinePatchLeft=\"70\" NinePatchTop=\"87\" NinePatchRight=\"10\" NinePatchBottom=\"11\" IsVisible=\"@B1071PanelExpanded\">" +
            "<Children>" +
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginLeft=\"22\" MarginRight=\"22\" MarginTop=\"20\" MarginBottom=\"16\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" Brush=\"MapTextBrush\" Brush.FontSize=\"19\" Brush.TextHorizontalAlignment=\"Center\" Text=\"Byzantium 1071\" />" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" Brush=\"MapTextBrush\" Brush.FontSize=\"16\" Brush.TextHorizontalAlignment=\"Center\" MarginTop=\"6\" Text=\"@B1071PanelText\" />" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</Widget>" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</Widget>";
    }

    [PrefabExtension("MapBar", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    [PrefabExtension("Map/MapBar", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    [PrefabExtension("MapBarView", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    [PrefabExtension("Map\\MapBar", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']")]
    public sealed class B1071_MapBarPanelPrefabExtensionAlt : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText(false)]
        public string Text =>
            "<Widget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" MarginLeft=\"22\" MarginTop=\"92\" IsVisible=\"@B1071PanelVisible\">" +
            "<Children>" +
            "<ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">" +
            "<Children>" +
            "<ButtonWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"124\" SuggestedHeight=\"32\" Brush=\"MapInfoBarExtendButtonBrush\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" Command.Click=\"ExecuteB1071ToggleExpanded\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"MapTextBrush\" Brush.FontSize=\"16\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"@B1071ToggleText\"/>" +
            "</Children>" +
            "</ButtonWidget>" +
            "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"360\" MarginTop=\"4\" Sprite=\"MapBar\\mapbar_right_frame\" NinePatchLeft=\"70\" NinePatchTop=\"87\" NinePatchRight=\"10\" NinePatchBottom=\"11\" IsVisible=\"@B1071PanelExpanded\">" +
            "<Children>" +
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginLeft=\"22\" MarginRight=\"22\" MarginTop=\"20\" MarginBottom=\"16\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" Brush=\"MapTextBrush\" Brush.FontSize=\"19\" Brush.TextHorizontalAlignment=\"Center\" Text=\"Byzantium 1071\" />" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" Brush=\"MapTextBrush\" Brush.FontSize=\"16\" Brush.TextHorizontalAlignment=\"Center\" MarginTop=\"6\" Text=\"@B1071PanelText\" />" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</Widget>" +
            "</Children>" +
            "</ListPanel>" +
            "</Children>" +
            "</Widget>";
    }

    [ViewModelMixin(nameof(MapBarVM.OnRefresh), true)]
    public sealed class B1071_MapBarVMMixin : BaseViewModelMixin<MapBarVM>
    {
        private bool _panelVisible = true;
        private bool _panelExpanded = true;
        private string _panelText = "Byzantium 1071 Overlay\n[Press M to toggle]";
        private string _toggleText = "Hide";

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

        [DataSourceMethod]
        public void ExecuteB1071ToggleExpanded()
        {
            B1071_OverlayController.ToggleExpanded();
            B1071PanelExpanded = B1071_OverlayController.IsExpanded;
            B1071ToggleText = B1071PanelExpanded ? "Hide" : "Byz 1071";

            OnPropertyChangedWithValue(B1071PanelExpanded, nameof(B1071PanelExpanded));
            OnPropertyChangedWithValue(B1071ToggleText, nameof(B1071ToggleText));
        }
    }
}
