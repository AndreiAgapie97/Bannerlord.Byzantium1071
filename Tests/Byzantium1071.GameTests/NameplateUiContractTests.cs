using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Byzantium1071.Campaign.UI;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Information;
using Xunit;

namespace Byzantium1071.GameTests
{
    public sealed class NameplateUiContractTests
    {
        public static IEnumerable<object[]> NameplateSizes()
        {
            yield return new object[]
            {
                typeof(B1071_SettlementNameplateSmallHoverPatch),
                "SettlementNameplateItemSmall"
            };
            yield return new object[]
            {
                typeof(B1071_SettlementNameplateMediumHoverPatch),
                "SettlementNameplateItemMedium"
            };
            yield return new object[]
            {
                typeof(B1071_SettlementNameplateLargeHoverPatch),
                "SettlementNameplateItemLarge"
            };
        }

        // UIExtenderEx looks movies up by bare file name
        // (WidgetPrefabPatch.ProcessMovie -> Path.GetFileNameWithoutExtension), so a
        // directory-qualified movie name such as "Nameplate/..." can never match.
        [Theory]
        [MemberData(nameof(NameplateSizes))]
        public void HoverPatchTargetsCapsuleButtonByBareMovieName(Type patchType, string movie)
        {
            PrefabExtensionAttribute[] extensions = patchType
                .GetCustomAttributes(typeof(PrefabExtensionAttribute), inherit: false)
                .Cast<PrefabExtensionAttribute>()
                .ToArray();

            PrefabExtensionAttribute extension = Assert.Single(extensions);
            Assert.Equal(movie, extension.Movie);
            Assert.Equal("descendant::ButtonWidget[@Id='SettlementNameplateCapsuleWidget']", extension.XPath);

            PrefabExtensionSetAttributePatch patch = (PrefabExtensionSetAttributePatch)Activator.CreateInstance(patchType)!;
            Assert.Equal(
                new[]
                {
                    "Command.HoverBegin=ExecuteB1071NameplateHoverBegin",
                    "Command.HoverEnd=ExecuteB1071NameplateHoverEnd"
                },
                patch.Attributes.Select(attribute => $"{attribute.Name}={attribute.Value}"));
        }

        [Theory]
        [MemberData(nameof(NameplateSizes))]
        public void MarkerPatchTargetsTopSideIcons(Type _, string movie)
        {
            Type patchType = movie switch
            {
                "SettlementNameplateItemSmall" => typeof(B1071_SettlementNameplateSmallMarkerPatch),
                "SettlementNameplateItemMedium" => typeof(B1071_SettlementNameplateMediumMarkerPatch),
                "SettlementNameplateItemLarge" => typeof(B1071_SettlementNameplateLargeMarkerPatch),
                _ => throw new ArgumentOutOfRangeException(nameof(movie), movie, null)
            };

            PrefabExtensionAttribute[] extensions = patchType
                .GetCustomAttributes(typeof(PrefabExtensionAttribute), inherit: false)
                .Cast<PrefabExtensionAttribute>()
                .ToArray();

            PrefabExtensionAttribute extension = Assert.Single(extensions);
            Assert.Equal(movie, extension.Movie);
            Assert.Equal("descendant::ListPanel[@Id='TopSideIcons']", extension.XPath);
        }

        // The insert patch's content is re-read on every prefab load, so it must return
        // identical markup every time. A one-shot getter silently drops the marker after
        // any prefab reload.
        [Fact]
        public void MarkerMarkupIsConstantAcrossRepeatedReads()
        {
            B1071_SettlementNameplateSmallMarkerPatch patch = new();

            string first = patch.Text;
            string second = patch.Text;
            string fromAnotherInstance = new B1071_SettlementNameplateSmallMarkerPatch().Text;

            Assert.Contains("Id=\"B1071DevastationMarker\"", first);
            Assert.Contains("IsVisible=\"@B1071DevastationMarkerVisible\"", first);
            Assert.Equal(first, second);
            Assert.Equal(first, fromAnotherInstance);
        }

        // Every tooltip row the nameplate mixin builds passes textHeight 0, because
        // TooltipPropertyWidget aligns a row into two columns only when the definition and
        // value are both non-empty and TextHeight is 0 (its IsTwoColumn flag). Nothing in the
        // mod references IsTwoColumn, so if TaleWorlds renames or retypes either member the
        // build still succeeds and the tooltip silently reverts to the single-column path,
        // where the label and value render touching. This test is the only alarm.
        [Fact]
        public void TooltipTwoColumnLayoutContractStillHolds()
        {
            PropertyInfo? textHeight = typeof(TooltipProperty).GetProperty(
                "TextHeight",
                BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo? isTwoColumn = typeof(TooltipPropertyWidget).GetProperty(
                "IsTwoColumn",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.NotNull(textHeight);
            Assert.Equal(typeof(int), textHeight!.PropertyType);
            Assert.NotNull(isTwoColumn);
            Assert.Equal(typeof(bool), isTwoColumn!.PropertyType);
        }

        // The Campaign++ section is added by wrapping the Settlement tooltip registration, not
        // by patching a refresher method. InformationManager keeps one refresher delegate per
        // registered type and the last module to call RegisterTooltip wins, so War Sails
        // re-registers Settlement with its own verbatim copy of vanilla's refresher and a
        // postfix on vanilla's method attaches, verifies, throws nothing and never runs. Install
        // therefore has to wrap whatever is registered rather than assume vanilla's, keep the
        // movie the game asked for, and recognise its own registration so a second call does not
        // wrap the wrapper and add the section twice.
        [Fact]
        public void InstallWrapsWhicheverRefresherIsRegisteredAndUninstallPutsItBack()
        {
            Action<PropertyBasedTooltipVM, object[]> registered = (tooltip, args) => { };
            InformationManager.RegisterTooltip<Settlement, PropertyBasedTooltipVM>(
                registered, "PropertyBasedTooltip");

            try
            {
                B1071_SettlementTooltipRefresher.Install();

                InformationManager.TooltipRegistry wrapped =
                    InformationManager.RegisteredTypes[typeof(Settlement)];
                Assert.NotSame(registered, wrapped.OnRefreshData);
                Assert.IsType<Action<PropertyBasedTooltipVM, object[]>>(wrapped.OnRefreshData);
                Assert.Equal("PropertyBasedTooltip", wrapped.MovieName);
                Assert.Equal(typeof(PropertyBasedTooltipVM), wrapped.TooltipType);

                B1071_SettlementTooltipRefresher.Install();
                Assert.Same(
                    wrapped.OnRefreshData,
                    InformationManager.RegisteredTypes[typeof(Settlement)].OnRefreshData);
            }
            finally
            {
                B1071_SettlementTooltipRefresher.Uninstall();
            }

            Assert.Same(
                registered,
                InformationManager.RegisteredTypes[typeof(Settlement)].OnRefreshData);
            InformationManager.UnregisterTooltip<Settlement>();
        }

        // The rows go in above vanilla's trailing spacer, so they read as the last block of
        // settlement data instead of sitting below "Hold 'Alt' for more info." and the parley
        // hint. The spacer is matched by row shape - empty definition, empty value, negative
        // TextHeight - because matching the hint's wording would only work in the languages the
        // mod ships. Vanilla uses the same spacer between sections, so only the last one is the
        // footer and the scan has to run backwards.
        [Fact]
        public void SectionGoesInAboveTheTrailingSpacerNotBelowTheHoldAltHint()
        {
            MBBindingList<TooltipProperty> rows = new()
            {
                new TooltipProperty("Owner", "Clan Elaches", 0),
                new TooltipProperty(string.Empty, string.Empty, -1),
                new TooltipProperty("Garrison", "425", 0),
                new TooltipProperty(string.Empty, string.Empty, -1),
                new TooltipProperty(string.Empty, "Hold 'Alt' for more info.", -1)
            };

            Assert.Equal(3, FindFooterIndex(rows));
        }

        // No spacer at all: the scan falls back to the end of the list, which puts the section
        // below the hint. A worse position, not a broken one.
        [Fact]
        public void FooterScanFallsBackToTheEndOfTheListWhenTheSpacerIsGone()
        {
            MBBindingList<TooltipProperty> rows = new()
            {
                new TooltipProperty("Owner", "Clan Elaches", 0)
            };

            Assert.Equal(1, FindFooterIndex(rows));
        }

        // FindFooterIndex is private - nothing outside the refresher has any business choosing
        // the insertion point - but the choice is exactly what the two tests above pin.
        private static int FindFooterIndex(MBBindingList<TooltipProperty> properties)
        {
            MethodInfo? method = typeof(B1071_SettlementTooltipRefresher).GetMethod(
                "FindFooterIndex", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (int)method!.Invoke(null, new object[] { properties })!;
        }
    }
}
