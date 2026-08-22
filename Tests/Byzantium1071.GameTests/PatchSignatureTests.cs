using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Xunit;

namespace Byzantium1071.GameTests
{
    /// <summary>
    /// One Harmony patch the mod declares, resolved from the built mod assembly.
    /// </summary>
    internal sealed class DeclaredPatch
    {
        internal DeclaredPatch(string patchOwner, Type declaringType, string methodName, Type[]? argumentTypes)
        {
            PatchOwner = patchOwner;
            DeclaringType = declaringType;
            MethodName = methodName;
            ArgumentTypes = argumentTypes;
        }

        internal string PatchOwner { get; }
        internal Type DeclaringType { get; }
        internal string MethodName { get; }
        internal Type[]? ArgumentTypes { get; }
    }

    public sealed class PatchSignatureTests
    {
        private static Assembly ModAssembly => typeof(Byzantium1071.SubModule).Assembly;

        public static IEnumerable<object[]> CriticalTargets()
        {
            yield return Target(
                "TaleWorlds.CampaignSystem.CampaignBehaviors.RecruitmentCampaignBehavior",
                "ApplyInternal");
            yield return Target(
                "TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitmentVM",
                "OnDone");
            yield return Target(
                "TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitmentVM",
                "RefreshPartyProperties");
            yield return Target(
                "TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel",
                "CalculateClanIncomeInternal");
            yield return Target(
                "TaleWorlds.CampaignSystem.CampaignBehaviors.PartiesSellPrisonerCampaignBehavior",
                "OnSettlementEntered");
            yield return Target(
                "TaleWorlds.CampaignSystem.CampaignBehaviors.InfluenceGainCampaignBehavior",
                "OnPrisonerDonatedToSettlement");
            yield return Target(
                "TaleWorlds.CampaignSystem.CampaignBehaviors.PartiesSellPrisonerCampaignBehavior",
                "DailyTickSettlement");
        }

        /// <summary>
        /// Discovers every Harmony patch the mod declares by reflecting over the built mod
        /// assembly, so a newly added patch is guarded automatically instead of relying on
        /// somebody remembering to extend a hand-written list.
        /// </summary>
        public static IEnumerable<object[]> DeclarativePatchTargets() =>
            DiscoverDeclaredPatches()
                .Select(patch => new object[]
                {
                    patch.DeclaringType.FullName!,
                    patch.MethodName,
                    patch.PatchOwner,
                    FormatArgumentTypes(patch.ArgumentTypes)
                })
                .ToArray();

        public static IEnumerable<object[]> RequiredGameModels()
        {
            yield return new object[] { "TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementFoodModel" };
            yield return new object[] { "TaleWorlds.CampaignSystem.GameComponents.DefaultVolunteerModel" };
            yield return new object[] { "TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementProsperityModel" };
            yield return new object[] { "TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementLoyaltyModel" };
            yield return new object[] { "TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementSecurityModel" };
            yield return new object[] { "TaleWorlds.CampaignSystem.GameComponents.DefaultBuildingConstructionModel" };
            yield return new object[] { "TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel" };
        }

        [Theory]
        [MemberData(nameof(CriticalTargets))]
        public void CriticalStringBasedPatchTargetStillExists(string typeName, string methodName)
        {
            Type type = ResolveType(typeName);
            MethodInfo? method = FindMethod(type, methodName);

            Assert.True(method != null, $"Missing patched method {typeName}.{methodName}.");
        }

        [Theory]
        [MemberData(nameof(DeclarativePatchTargets))]
        public void DeclarativeHarmonyPatchTargetStillExists(
            string typeName,
            string methodName,
            string patchOwner,
            string argumentTypeNames)
        {
            Type type = ResolveType(typeName);
            MethodInfo? method = FindMethod(type, methodName);

            Assert.True(
                method != null,
                $"{patchOwner} patches {typeName}.{methodName}, which no longer exists in the installed game.");

            if (argumentTypeNames.Length == 0)
            {
                return;
            }

            Type[] argumentTypes = argumentTypeNames
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ResolveType)
                .ToArray();
            MethodInfo? overload = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: argumentTypes,
                modifiers: null);

            Assert.True(
                overload != null,
                $"{patchOwner} pins the {typeName}.{methodName} overload taking " +
                $"({argumentTypeNames.Replace('|', ',')}), which no longer exists in the installed game.");
        }

        [Fact]
        public void PatchDiscoveryFindsEveryPatchClassInTheModAssembly()
        {
            DeclaredPatch[] discovered = DiscoverDeclaredPatches();

            // Guards against a reflection change silently reducing this suite to zero targets.
            Assert.True(
                discovered.Length >= 30,
                $"Only {discovered.Length} Harmony patch targets were discovered; the mod declares far more.");

            Type[] patchClasses = ModAssembly
                .GetTypes()
                .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0)
                .ToArray();
            string[] classesWithoutTarget = patchClasses
                .Select(type => type.FullName!)
                .Except(discovered.Select(patch => patch.PatchOwner), StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                classesWithoutTarget.Length == 0,
                $"These Harmony patch classes declare no resolvable target: {string.Join(", ", classesWithoutTarget)}.");
        }

        [Fact]
        public void CriticalTargetListCoversEveryStringBasedLookupInSubModule()
        {
            string subModuleSource = File.ReadAllText(
                Path.Combine(RepositoryRoot(), "SubModule.cs"));
            int verifySection = subModuleSource.IndexOf("VerifyCriticalPatches", StringComparison.Ordinal);
            Assert.True(verifySection >= 0, "SubModule.cs no longer contains VerifyCriticalPatches.");

            string[] declaredPairs = CriticalTargets()
                .Select(target => $"{(string)target[0]}.{(string)target[1]}")
                .ToArray();
            string[] missing = Regex
                .Matches(subModuleSource.Substring(verifySection), @"""(?<type>TaleWorlds\.[A-Za-z0-9_.]+)"",\s*""(?<method>[A-Za-z0-9_]+)""")
                .Cast<Match>()
                .Select(match => $"{match.Groups["type"].Value}.{match.Groups["method"].Value}")
                .Distinct(StringComparer.Ordinal)
                .Except(declaredPairs, StringComparer.Ordinal)
                .OrderBy(pair => pair, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                missing.Length == 0,
                $"VerifyCriticalPatches checks targets this suite does not guard: {string.Join(", ", missing)}.");
        }

        [Theory]
        [MemberData(nameof(RequiredGameModels))]
        public void CompatibilityCheckerRequiredGameModelStillExists(string typeName)
        {
            Assert.NotNull(ResolveType(typeName));
        }

        [Fact]
        public void TierArmorSimulationPatchStillTargetsTheNineParameterTroopOverload()
        {
            Type type = ResolveType("TaleWorlds.CampaignSystem.GameComponents.DefaultCombatSimulationModel");
            Type[] parameterTypes =
            {
                ResolveType("TaleWorlds.CampaignSystem.CharacterObject"),
                ResolveType("TaleWorlds.CampaignSystem.CharacterObject"),
                ResolveType("TaleWorlds.CampaignSystem.Party.PartyBase"),
                ResolveType("TaleWorlds.CampaignSystem.Party.PartyBase"),
                typeof(float),
                ResolveType("TaleWorlds.CampaignSystem.MapEvents.MapEvent"),
                ResolveType("TaleWorlds.Core.BattleEnvironment"),
                typeof(float),
                typeof(float)
            };
            MethodInfo? method = type.GetMethod(
                "SimulateHit",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: parameterTypes,
                modifiers: null);

            Assert.NotNull(method);
            Assert.Equal("TaleWorlds.CampaignSystem.ExplainedNumber", method!.ReturnType.FullName);
        }

        /// <summary>
        /// Walks every type in the mod assembly and merges class-level and method-level
        /// <c>[HarmonyPatch]</c> attributes the same way Harmony itself does, so both the
        /// "attribute on the class" and "attribute on each patch method" styles are covered.
        /// </summary>
        private static DeclaredPatch[] DiscoverDeclaredPatches()
        {
            List<DeclaredPatch> discovered = new();

            foreach (Type type in LoadAllModTypes())
            {
                object[] classAttributes = type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false);
                if (classAttributes.Length == 0)
                {
                    continue;
                }

                HarmonyMethod classInfo = MergeAttributes(classAttributes);
                MethodInfo[] annotatedMethods = type
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(method => method.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0)
                    .ToArray();

                if (annotatedMethods.Length == 0)
                {
                    AddIfResolvable(discovered, type.FullName!, classInfo, classInfo);
                    continue;
                }

                foreach (MethodInfo method in annotatedMethods)
                {
                    HarmonyMethod methodInfo = MergeAttributes(
                        method.GetCustomAttributes(typeof(HarmonyPatch), inherit: false));
                    AddIfResolvable(discovered, type.FullName!, methodInfo, classInfo);
                }
            }

            return discovered
                .GroupBy(patch => $"{patch.PatchOwner}|{patch.DeclaringType.FullName}|{patch.MethodName}|{FormatArgumentTypes(patch.ArgumentTypes)}",
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(patch => patch.PatchOwner, StringComparer.Ordinal)
                .ThenBy(patch => patch.MethodName, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Loads every type in the mod assembly. A partial load would silently drop patch
        /// classes from this suite, so a missing dependency is reported as a failure rather
        /// than quietly reducing coverage.
        /// </summary>
        private static Type[] LoadAllModTypes()
        {
            try
            {
                return ModAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                string[] reasons = exception.LoaderExceptions
                    .Where(loaderException => loaderException != null)
                    .Select(loaderException => loaderException!.Message)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                throw new InvalidOperationException(
                    "The mod assembly could not be fully loaded, so some Harmony patches would go unguarded. "
                    + $"Add the missing references to this test project. Reasons: {string.Join(" | ", reasons)}",
                    exception);
            }
        }

        private static void AddIfResolvable(
            List<DeclaredPatch> discovered,
            string patchOwner,
            HarmonyMethod primary,
            HarmonyMethod fallback)
        {
            Type? declaringType = primary.declaringType ?? fallback.declaringType;
            string? methodName = primary.methodName ?? fallback.methodName;
            Type[]? argumentTypes = primary.argumentTypes ?? fallback.argumentTypes;

            if (declaringType != null && !string.IsNullOrEmpty(methodName))
            {
                discovered.Add(new DeclaredPatch(patchOwner, declaringType, methodName!, argumentTypes));
            }
        }

        private static HarmonyMethod MergeAttributes(object[] attributes)
        {
            HarmonyMethod merged = new();

            foreach (object attribute in attributes)
            {
                HarmonyMethod info = ((HarmonyPatch)attribute).info;
                merged.declaringType ??= info.declaringType;
                merged.methodName ??= info.methodName;
                merged.argumentTypes ??= info.argumentTypes;
            }

            return merged;
        }

        private static string FormatArgumentTypes(Type[]? argumentTypes) =>
            argumentTypes == null || argumentTypes.Length == 0
                ? string.Empty
                : string.Join("|", argumentTypes.Select(type => type.FullName));

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

        private static object[] Target(string typeName, string methodName) => new object[] { typeName, methodName };

        private static MethodInfo? FindMethod(Type type, string methodName)
        {
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name == methodName)
                {
                    return method;
                }
            }

            return null;
        }

        private static Type ResolveType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type = assembly.GetType(typeName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            foreach (string assemblyName in new[]
            {
                "TaleWorlds.CampaignSystem",
                "TaleWorlds.CampaignSystem.ViewModelCollection",
                "SandBox",
                "SandBox.ViewModelCollection"
            })
            {
                try
                {
                    Type? type = Assembly.Load(assemblyName).GetType(typeName, throwOnError: false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch (Exception)
                {
                    // The next assembly may own this game type.
                }
            }

            throw new InvalidOperationException($"Missing patched target type {typeName}.");
        }
    }
}
