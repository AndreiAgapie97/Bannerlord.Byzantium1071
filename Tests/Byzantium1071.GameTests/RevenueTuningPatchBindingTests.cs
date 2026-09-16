using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;
using Xunit;

namespace Byzantium1071.GameTests
{
    /// <summary>
    /// Applies the settlement-revenue patch classes to the installed game the same way the mod
    /// does at load, which is the only way to catch the failure mode these patches are prone to.
    ///
    /// SubModule.PatchAssemblySafely logs a patch-time HarmonyException and carries on, so a patch
    /// that fails to bind is a skipped patch with a quiet log line, not a crash. Nothing else in
    /// the suite would notice: DeclarativeHarmonyPatchTargetStillExists resolves the target by
    /// reflection and never asks whether the postfix attached to it. Harmony binds prefix and
    /// postfix parameters BY NAME, so renaming <c>applyWithdrawals</c> in the game assembly -- or
    /// misspelling it in ours -- compiles cleanly and dies here instead of in play.
    ///
    /// The patch classes are internal, so this reaches them through the mod assembly rather than
    /// naming them directly.
    /// </summary>
    public sealed class RevenueTuningPatchBindingTests
    {
        private const string PatchNamespace = "Byzantium1071.Campaign.Patches.";
        private const string TaperOwner = PatchNamespace + "B1071_RevenueTaper";

        private static readonly (string PatchClass, Type Target, string Method)[] ExpectedPatches =
        {
            (PatchNamespace + "B1071_SettlementTariffTuningPatch",
                typeof(DefaultClanFinanceModel), "CalculateTownIncomeFromTariffs"),
            (PatchNamespace + "B1071_SettlementVillageTariffTuningPatch",
                typeof(DefaultClanFinanceModel), "CalculateVillageIncome"),
            (PatchNamespace + "B1071_SettlementTaxTuningPatch",
                typeof(DefaultSettlementTaxModel), "CalculateTownTax"),
        };

        public static IEnumerable<object[]> Patches() =>
            ExpectedPatches.Select(entry => new object[] { entry.PatchClass, entry.Target, entry.Method });

        [Theory]
        [MemberData(nameof(Patches))]
        public void EveryRevenueTuningPostfixBindsToItsLiveTarget(string patchClassName, Type target, string methodName)
        {
            Type patchClass = ResolvePatchClass(patchClassName);
            MethodInfo targetMethod = FindMethod(target, methodName);

            Harmony harmony = new($"b1071.gametests.{patchClass.Name}");
            try
            {
                // This is the call SubModule makes, and the one that throws when a parameter name
                // no longer matches the game's method signature.
                Assert.NotNull(harmony.CreateClassProcessor(patchClass).Patch());

                Assert.True(
                    harmony.GetPatchedMethods().Contains(targetMethod),
                    $"{patchClass.Name} did not attach to {target.Name}.{methodName}.");

                Patches info = Harmony.GetPatchInfo(targetMethod);
                Assert.True(
                    info?.Postfixes.Any(patch => patch.owner == harmony.Id) == true,
                    $"{patchClass.Name} attached nothing to {target.Name}.{methodName}; the postfix was skipped.");
                if (target == typeof(DefaultClanFinanceModel))
                    Assert.True(info?.Prefixes.Any(patch => patch.owner == harmony.Id) == true,
                        $"{patchClass.Name} must capture the tariff basis before withdrawal.");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        /// <summary>
        /// Town tax is the only one of the three targets the mod has never patched, so a stale or
        /// renamed signature there would not be caught by any existing patch. Harmony binds by
        /// parameter name, so the names are asserted here directly.
        /// </summary>
        [Fact]
        public void TownTaxStillTakesTheTwoParametersThePostfixBindsByName()
        {
            MethodInfo method = FindMethod(typeof(DefaultSettlementTaxModel), "CalculateTownTax");
            string[] names = method.GetParameters().Select(parameter => parameter.Name!).ToArray();

            Assert.Equal(new[] { "town", "includeDescriptions" }, names);
            Assert.Equal("TaleWorlds.CampaignSystem.ExplainedNumber", method.ReturnType.FullName);
        }

        [Fact]
        public void TownTariffAndVillageIncomeStillTakeTheThreeParametersThePostfixesBindByName()
        {
            MethodInfo tariff = FindMethod(typeof(DefaultClanFinanceModel), "CalculateTownIncomeFromTariffs");
            Assert.Equal(
                new[] { "clan", "town", "applyWithdrawals" },
                tariff.GetParameters().Select(parameter => parameter.Name!).ToArray());
            Assert.Equal("TaleWorlds.CampaignSystem.ExplainedNumber", tariff.ReturnType.FullName);

            MethodInfo village = FindMethod(typeof(DefaultClanFinanceModel), "CalculateVillageIncome");
            Assert.Equal(
                new[] { "clan", "village", "applyWithdrawals" },
                village.GetParameters().Select(parameter => parameter.Name!).ToArray());
            Assert.Equal(typeof(int), village.ReturnType);
        }

        /// <summary>
        /// The taper reports itself to the player through a keyed TextObject, so the line the
        /// finance panel adds is translated rather than hardcoded English. A missing key would
        /// otherwise show the player the raw fallback or nothing at all.
        /// </summary>
        [Fact]
        public void TheTaperLabelIsKeyedAndDeclaredInEveryLanguagePack()
        {
            Type taper = ResolvePatchClass(TaperOwner);
            FieldInfo? label = taper.GetField(
                "Label", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(label);
            Assert.Equal("TaleWorlds.Localization.TextObject", label!.FieldType.FullName);
            // TextObject.ToString() returns the processed text, so the key must be read from
            // GetID(); ToString() would pass trivially or fail depending on what the language
            // manager happens to have loaded.
            Assert.Equal("b1071_revenue_taper", ((TextObject)label.GetValue(null)!).GetID());

            foreach (string language in new[] { "", "French", "German", "Chinese" })
            {
                string path = Path.Combine(
                    RepositoryRoot(), "_Module", "ModuleData", "Languages", language,
                    "std_module_strings_xml.xml");
                Assert.True(File.Exists(path), $"Missing language pack: {path}");
                Assert.Contains("b1071_revenue_taper", File.ReadAllText(path));
            }
        }

        /// <summary>
        /// A threshold setting that no patch reads would look like a working slider and do nothing,
        /// so this pins the wiring rather than the declared property. The three knees are read out
        /// of the MCM settings by name at each of the three call sites, in the same order as the
        /// curve above them.
        /// </summary>
        [Fact]
        public void EveryKneeSettingIsReadByThePatchThatOwnsIt()
        {
            string patchPath = Path.Combine(
                RepositoryRoot(), "Campaign", "Patches", "B1071_SettlementRevenueTuningPatch.cs");
            string patch = File.ReadAllText(patchPath);

            foreach (string setting in new[]
                     {
                         "SettlementTariffKneeTown",
                         "SettlementTariffKneeVillage",
                         "SettlementTaxKnee"
                     })
            {
                Assert.Contains(setting, patch);
            }

            // Each knee is passed to the math call that follows its own curve setting, so a knee
            // cannot be wired into the wrong base.
            Assert.Matches(@"SettlementTariffCurveTown,\s*\n\s*settings\.SettlementTariffKneeTown", patch);
            Assert.Matches(@"SettlementTariffCurveVillage,\s*\n\s*settings\.SettlementTariffKneeVillage", patch);
            Assert.Matches(@"SettlementTaxCurve,\s*\n\s*settings\.SettlementTaxKnee", patch);
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the repository root.");
        }

        private static Type ResolvePatchClass(string fullName) =>
            typeof(Byzantium1071.SubModule).Assembly.GetType(fullName, throwOnError: true)!;

        private static MethodInfo FindMethod(Type type, string methodName)
        {
            MethodInfo? method = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == methodName);

            Assert.True(method != null, $"{type.FullName}.{methodName} no longer exists.");
            return method!;
        }
    }
}
