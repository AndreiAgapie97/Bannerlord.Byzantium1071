using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.UI
{
    /// <summary>
    /// Campaign++ manpower and devastation added to the vanilla settlement tooltip — the large
    /// panel the campaign map shows when a settlement is hovered.
    ///
    /// The section is installed by wrapping the Settlement tooltip registration rather than by
    /// patching a refresher method. InformationManager keeps one refresher delegate per
    /// registered type in a plain dictionary and the last module to call RegisterTooltip wins:
    /// SandBox.View registers TooltipRefresherCollection.RefreshSettlementTooltip at module
    /// load, and the War Sails view module then overwrites that entry with its own verbatim
    /// copy of the same method, which never calls vanilla's. A Harmony postfix on vanilla's
    /// method was attached to a method the game had stopped calling — it verified as attached
    /// at launch, threw nothing, and produced no section. Wrapping the registration composes
    /// with whichever refresher is installed, needs no reference to any DLC or mod assembly,
    /// and has nothing to detect.
    ///
    /// Rows come from B1071_SettlementTooltipContent, the same builder the nameplate hover
    /// tooltip uses, so the two surfaces cannot report different numbers for one settlement.
    ///
    /// The block goes in above the trailing spacer rather than at the end of the list, so it
    /// reads as the last section of settlement data instead of sitting underneath "Hold 'Alt'
    /// for more info." and the parley hint. FindFooterIndex finds that spacer by row shape —
    /// empty definition, empty value, negative TextHeight — because matching the hint's wording
    /// would only work in the languages the mod ships. A spacer, header and divider — vanilla's
    /// own section break, the same one it uses between Owner, Information and Defenders — make
    /// the block read as its own section instead of as rows bolted onto the last vanilla one.
    /// </summary>
    public static class B1071_SettlementTooltipRefresher
    {
        private static IB1071Settings Settings =>
            B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // {=!} marks a literal vanilla never translates. The mod's name is the same in every
        // language pack, so this deliberately carries no b1071_ text ID for translators to chase.
        private static readonly TextObject _sectionHeader = new TextObject("{=!}Campaign++");

        // One delegate instance for the life of the process. Install recognises its own
        // registration by reference, so it neither wraps itself nor lets Uninstall tear down a
        // registration that now belongs to somebody else.
        private static readonly Action<PropertyBasedTooltipVM, object[]> _wrapper = Refresh;

        // Whichever refresher was registered when Install ran — vanilla's, War Sails', or
        // another mod's. Null while not installed.
        private static Action<PropertyBasedTooltipVM, object[]>? _inner;

        // Set by the first settlement tooltip built after installing, so the log carries one
        // line saying what happened. An absent section looks identical from the outside whether
        // the wrapper never ran, the toggle was off, or the builder had nothing to report.
        private static bool _outcomeLogged;

        /// <summary>
        /// Takes over the Settlement tooltip registration, keeping the refresher already there
        /// as the inner call. Called from SubModule.OnGameStart, which runs long after every
        /// module's OnSubModuleLoad, so the entry being wrapped is the one the game will
        /// actually use. Safe to call again: a registration that is already the wrapper is left
        /// alone rather than wrapped a second time.
        /// </summary>
        public static void Install()
        {
            try
            {
                if (!InformationManager.RegisteredTypes.TryGetValue(
                        typeof(Settlement), out InformationManager.TooltipRegistry registry))
                {
                    Debug.Print("[Byzantium1071][SettlementTooltip] No settlement tooltip is registered; section not installed.");
                    return;
                }

                if (ReferenceEquals(registry.OnRefreshData, _wrapper))
                    return;

                if (registry.OnRefreshData is not Action<PropertyBasedTooltipVM, object[]> inner)
                {
                    Debug.Print("[Byzantium1071][SettlementTooltip] Settlement tooltip refresher is not the expected delegate type; section not installed.");
                    return;
                }

                _inner = inner;
                _outcomeLogged = false;
                InformationManager.RegisterTooltip<Settlement, PropertyBasedTooltipVM>(_wrapper, registry.MovieName);

                // The owner is worth naming: which module ends up holding this registration is
                // exactly what decided whether the old Harmony postfix ran at all.
                Debug.Print($"[Byzantium1071][SettlementTooltip] Section installed over {inner.Method?.DeclaringType?.FullName ?? "an unnamed refresher"}.");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][SettlementTooltip] Install failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Puts the wrapped refresher back, so unloading the mod leaves the game's own settlement
        /// tooltip behind rather than a delegate into an unloaded assembly. Restores only while
        /// the registration is still the wrapper: if another mod has registered over it since,
        /// that registration is theirs and reinstating the old inner would silently drop it.
        /// </summary>
        public static void Uninstall()
        {
            Action<PropertyBasedTooltipVM, object[]>? inner = _inner;
            if (inner == null)
                return;

            try
            {
                if (InformationManager.RegisteredTypes.TryGetValue(
                        typeof(Settlement), out InformationManager.TooltipRegistry registry) &&
                    ReferenceEquals(registry.OnRefreshData, _wrapper))
                {
                    InformationManager.RegisterTooltip<Settlement, PropertyBasedTooltipVM>(inner, registry.MovieName);
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][SettlementTooltip] Uninstall failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _inner = null;
                _outcomeLogged = false;
            }
        }

        /// <summary>
        /// The registered refresher. Builds the tooltip exactly as the wrapped refresher would,
        /// then adds the Campaign++ section. The inner call is deliberately outside the
        /// try/catch: swallowing its exception here would turn a game-side failure into a
        /// half-built tooltip that reports nothing.
        /// </summary>
        private static void Refresh(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
        {
            _inner?.Invoke(propertyBasedTooltipVM, args);

            try
            {
                AddSection(propertyBasedTooltipVM, args);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][SettlementTooltip] Error: {ex.Message}");
            }
        }

        private static void AddSection(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
        {
            if (propertyBasedTooltipVM?.TooltipPropertyList == null)
            {
                LogFirstOutcome("no property list on the tooltip");
                return;
            }

            if (!Settings.EnableSettlementNameplateTooltips)
            {
                LogFirstOutcome("skipped, settlement tooltips are switched off");
                return;
            }

            // The settlement arrives untyped: the vanilla refresher reads it with
            // args[0] as Settlement, so a type-pattern test rather than a cast.
            if (args == null || args.Length == 0 || args[0] is not Settlement settlement)
            {
                LogFirstOutcome("no settlement in the tooltip arguments");
                return;
            }

            List<TooltipProperty> rows = B1071_SettlementTooltipContent.Build(settlement);
            if (rows.Count == 0)
            {
                LogFirstOutcome("nothing to report for " + settlement.Name.ToString());
                return;
            }

            MBBindingList<TooltipProperty> properties = propertyBasedTooltipVM.TooltipPropertyList;
            int index = FindFooterIndex(properties);
            int sectionStart = index;

            properties.Insert(index++, new TooltipProperty(string.Empty, string.Empty, -1));
            properties.Insert(index++, new TooltipProperty(_sectionHeader.ToString(), " ", 0));
            properties.Insert(index++, new TooltipProperty(string.Empty, string.Empty, 0, false,
                TooltipProperty.TooltipPropertyFlags.RundownSeperator));

            foreach (TooltipProperty row in rows)
            {
                properties.Insert(index++, row);
            }

            LogFirstOutcome($"{rows.Count} row(s) inserted at {sectionStart} of {properties.Count}");
        }

        /// <summary>
        /// Where the Campaign++ section goes: the index of the trailing spacer, the empty row
        /// vanilla adds after the last block of settlement data and directly above "Hold 'Alt'
        /// for more info." and the parley hint.
        ///
        /// The scan matches row shape rather than text — definition and value both empty with a
        /// negative TextHeight — which is the spacer's signature and no other row's. Vanilla adds
        /// spacers between sections too, so the search runs backwards and takes the last one.
        /// Falls back to the end of the list if the footer is ever restructured away, which puts
        /// the section below the hint: a worse position, not a broken one.
        /// </summary>
        private static int FindFooterIndex(MBBindingList<TooltipProperty> properties)
        {
            for (int i = properties.Count - 1; i >= 0; i--)
            {
                TooltipProperty row = properties[i];
                if (row.TextHeight < 0 &&
                    string.IsNullOrEmpty(row.DefinitionLabel) &&
                    string.IsNullOrEmpty(row.ValueLabel))
                {
                    return i;
                }
            }

            return properties.Count;
        }

        private static void LogFirstOutcome(string outcome)
        {
            if (_outcomeLogged)
                return;

            _outcomeLogged = true;
            Debug.Print($"[Byzantium1071][SettlementTooltip] First settlement tooltip: {outcome}.");
        }
    }
}
