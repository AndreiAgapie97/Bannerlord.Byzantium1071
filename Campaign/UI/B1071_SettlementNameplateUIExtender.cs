using System;
using System.Collections.Generic;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.UI
{
    [ViewModelMixin(nameof(SettlementNameplateVM.RefreshBindValues), true)]
    public sealed class B1071_SettlementNameplateVMMixin : BaseViewModelMixin<SettlementNameplateVM>
    {
        private const float DevastationMarkerThreshold = 50f;

        private static bool _failureLogged;

        private readonly BasicTooltipViewModel _tooltip;
        private bool _devastationMarkerVisible;
        private int _cachedDevastationVersion = int.MinValue;
        private float _cachedDevastation;

        private static IB1071Settings Settings =>
            B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        public B1071_SettlementNameplateVMMixin(SettlementNameplateVM vm) : base(vm)
        {
            _tooltip = new BasicTooltipViewModel(BuildTooltipProperties);
        }

        // Runs for every nameplate on every map frame, so it is guarded for the same reason as
        // the hover: an exception here would escape into the nameplate update every frame.
        public override void OnRefresh()
        {
            base.OnRefresh();

            try
            {
                RefreshDevastationMarker();
            }
            catch (Exception ex)
            {
                LogFirstFailure("devastation marker", ex);
            }
        }

        public override void OnFinalize()
        {
            _tooltip.ExecuteEndHint();
            base.OnFinalize();
        }

        [DataSourceProperty]
        public bool B1071DevastationMarkerVisible
        {
            get => _devastationMarkerVisible;
            private set => SetField(ref _devastationMarkerVisible, value, nameof(B1071DevastationMarkerVisible));
        }

        /// <summary>
        /// Gauntlet calls this through ViewModel.ExecuteCommand, whose InvokeWithLog logs and
        /// rethrows, and GauntletView.OnCommand does not catch either, so neither stops an
        /// exception from here. The tooltip rows are built inside ExecuteBeginHint and
        /// bind to game members TaleWorlds has changed before: by v1.5.6 the TooltipProperty
        /// constructor had gained a parameter, so a build compiled against an earlier game
        /// references one that no longer exists and gets MissingMethodException. Caught here, a failure costs this
        /// hover's tooltip, not the campaign.
        /// </summary>
        [DataSourceMethod]
        public void ExecuteB1071NameplateHoverBegin()
        {
            if (!Settings.EnableSettlementNameplateTooltips)
                return;

            try
            {
                _tooltip.ExecuteBeginHint();
            }
            catch (Exception ex)
            {
                LogFirstFailure("hover tooltip", ex);
            }
        }

        [DataSourceMethod]
        public void ExecuteB1071NameplateHoverEnd()
        {
            _tooltip.ExecuteEndHint();
        }

        private void RefreshDevastationMarker()
        {
            Settlement? settlement = ViewModel?.Settlement;
            if (settlement == null ||
                !Settings.EnableSettlementNameplateTooltips ||
                !Settings.EnableFrontierDevastation)
            {
                B1071DevastationMarkerVisible = false;
                return;
            }

            B1071DevastationMarkerVisible = GetCachedDevastation(settlement) >= DevastationMarkerThreshold;
        }

        /// <summary>
        /// RefreshBindValues runs for every nameplate on every map frame
        /// (GauntletMapSettlementNameplateView.OnMapScreenUpdate -> SettlementNameplatesVM.Update),
        /// so the bound-village walk in B1071_SettlementTooltipContent.GetDevastation must not
        /// run per frame. Devastation only changes when the behavior mutates it, so recompute
        /// only when its counter moves. The settings checks above stay uncached so MCM toggles
        /// apply immediately.
        /// </summary>
        private float GetCachedDevastation(Settlement settlement)
        {
            B1071_DevastationBehavior? devastation = B1071_DevastationBehavior.Instance;
            if (devastation == null)
            {
                _cachedDevastationVersion = int.MinValue;
                return 0f;
            }

            int version = devastation.ChangeVersion;
            if (version != _cachedDevastationVersion)
            {
                _cachedDevastationVersion = version;
                _cachedDevastation = B1071_SettlementTooltipContent.GetDevastation(settlement);
            }

            return _cachedDevastation;
        }

        /// <summary>
        /// Built on hover rather than on refresh: BasicTooltipViewModel calls this only when the
        /// tooltip is actually shown, so the manpower and devastation lookups stay off the
        /// per-frame nameplate path. B1071_SettlementTooltipContent is shared with the vanilla
        /// settlement tooltip so both surfaces report the same numbers.
        /// </summary>
        private List<TooltipProperty> BuildTooltipProperties() =>
            B1071_SettlementTooltipContent.Build(ViewModel?.Settlement);

        // One line per launch: both guarded paths can fail on every frame or every hover, and
        // the first stack trace is the one that names the missing game member.
        private static void LogFirstFailure(string surface, Exception ex)
        {
            if (_failureLogged)
                return;

            _failureLogged = true;
            Debug.Print($"[Byzantium1071][SettlementNameplate] {surface} failed: {ex}");
        }
    }

    [PrefabExtension("SettlementNameplateItemSmall", "descendant::ButtonWidget[@Id='SettlementNameplateCapsuleWidget']")]
    public sealed class B1071_SettlementNameplateSmallHoverPatch : PrefabExtensionSetAttributePatch
    {
        public override List<PrefabExtensionSetAttributePatch.Attribute> Attributes { get; } = new()
        {
            new("Command.HoverBegin", nameof(B1071_SettlementNameplateVMMixin.ExecuteB1071NameplateHoverBegin)),
            new("Command.HoverEnd", nameof(B1071_SettlementNameplateVMMixin.ExecuteB1071NameplateHoverEnd))
        };
    }

    [PrefabExtension("SettlementNameplateItemMedium", "descendant::ButtonWidget[@Id='SettlementNameplateCapsuleWidget']")]
    public sealed class B1071_SettlementNameplateMediumHoverPatch : PrefabExtensionSetAttributePatch
    {
        public override List<PrefabExtensionSetAttributePatch.Attribute> Attributes { get; } = new()
        {
            new("Command.HoverBegin", nameof(B1071_SettlementNameplateVMMixin.ExecuteB1071NameplateHoverBegin)),
            new("Command.HoverEnd", nameof(B1071_SettlementNameplateVMMixin.ExecuteB1071NameplateHoverEnd))
        };
    }

    [PrefabExtension("SettlementNameplateItemLarge", "descendant::ButtonWidget[@Id='SettlementNameplateCapsuleWidget']")]
    public sealed class B1071_SettlementNameplateLargeHoverPatch : PrefabExtensionSetAttributePatch
    {
        public override List<PrefabExtensionSetAttributePatch.Attribute> Attributes { get; } = new()
        {
            new("Command.HoverBegin", nameof(B1071_SettlementNameplateVMMixin.ExecuteB1071NameplateHoverBegin)),
            new("Command.HoverEnd", nameof(B1071_SettlementNameplateVMMixin.ExecuteB1071NameplateHoverEnd))
        };
    }

    [PrefabExtension("SettlementNameplateItemSmall", "descendant::ListPanel[@Id='TopSideIcons']")]
    public sealed class B1071_SettlementNameplateSmallMarkerPatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        // UIExtenderEx re-reads this property every time the prefab is processed
        // (WidgetPrefab.LoadFrom -> PrefabComponent.ProcessMovieIfNeeded), and each pass
        // starts from the pristine prefab XML. It must stay a pure constant: a getter that
        // returns different markup on a later read drops the marker on any prefab reload.
        [PrefabExtensionText(false)]
        public string Text => "<TextWidget Id=\"B1071DevastationMarker\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"14\" SuggestedHeight=\"16\" Brush=\"GameMenu.Text\" Brush.FontSize=\"13\" Brush.FontColor=\"#C85D5DFF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"▲\" IsVisible=\"@B1071DevastationMarkerVisible\" DoNotAcceptEvents=\"true\" />";
    }

    [PrefabExtension("SettlementNameplateItemMedium", "descendant::ListPanel[@Id='TopSideIcons']")]
    public sealed class B1071_SettlementNameplateMediumMarkerPatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        // UIExtenderEx re-reads this property every time the prefab is processed
        // (WidgetPrefab.LoadFrom -> PrefabComponent.ProcessMovieIfNeeded), and each pass
        // starts from the pristine prefab XML. It must stay a pure constant: a getter that
        // returns different markup on a later read drops the marker on any prefab reload.
        [PrefabExtensionText(false)]
        public string Text => "<TextWidget Id=\"B1071DevastationMarker\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"17\" SuggestedHeight=\"19\" Brush=\"GameMenu.Text\" Brush.FontSize=\"15\" Brush.FontColor=\"#C85D5DFF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"▲\" IsVisible=\"@B1071DevastationMarkerVisible\" DoNotAcceptEvents=\"true\" />";
    }

    [PrefabExtension("SettlementNameplateItemLarge", "descendant::ListPanel[@Id='TopSideIcons']")]
    public sealed class B1071_SettlementNameplateLargeMarkerPatch : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        // UIExtenderEx re-reads this property every time the prefab is processed
        // (WidgetPrefab.LoadFrom -> PrefabComponent.ProcessMovieIfNeeded), and each pass
        // starts from the pristine prefab XML. It must stay a pure constant: a getter that
        // returns different markup on a later read drops the marker on any prefab reload.
        [PrefabExtensionText(false)]
        public string Text => "<TextWidget Id=\"B1071DevastationMarker\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"21\" SuggestedHeight=\"23\" Brush=\"GameMenu.Text\" Brush.FontSize=\"18\" Brush.FontColor=\"#C85D5DFF\" Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" Text=\"▲\" IsVisible=\"@B1071DevastationMarkerVisible\" DoNotAcceptEvents=\"true\" />";
    }
}
