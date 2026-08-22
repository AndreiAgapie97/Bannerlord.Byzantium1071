using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Byzantium1071.GameTests
{
    public sealed class PatchSignatureTests
    {
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

        public static IEnumerable<object[]> DeclarativePatchTargets()
        {
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementProsperityModel", "CalculateHearthChange");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementFoodModel", "CalculateTownFoodStocksChange");
            yield return Target("TaleWorlds.CampaignSystem.Kingdom", "DeactivateKingdom");
            yield return Target("TaleWorlds.CampaignSystem.Actions.DestroyClanAction", "Apply");
            yield return Target("TaleWorlds.CampaignSystem.Actions.DestroyClanAction", "ApplyByClanLeaderDeath");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultBuildingConstructionModel", "CalculateDailyConstructionPower");
            yield return Target("TaleWorlds.CampaignSystem.CampaignBehaviors.PartiesSellPrisonerCampaignBehavior", "DailyTickSettlement");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultPartyWageModel", "GetTroopRecruitmentCost");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultPartyWageModel", "GetCharacterWage");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultPartyWageModel", "GetTotalWage");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultVolunteerModel", "GetDailyVolunteerProductionProbability");
            yield return Target("TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitVolunteerVM", "ExecuteRecruit");
            yield return Target("TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitmentVM", "ExecuteRecruitAll");
            yield return Target("TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitmentVM", "OnDone");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementAccessModel", "CanMainHeroEnterSettlement");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementAccessModel", "CanMainHeroEnterLordsHall");
            yield return Target("TaleWorlds.CampaignSystem.CampaignBehaviors.PartiesSellPrisonerCampaignBehavior", "OnSettlementEntered");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultPartyHealingModel", "GetSurvivalChance");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementSecurityModel", "CalculateSecurityChange");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementGarrisonModel", "GetMaximumDailyAutoRecruitmentCount");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementProsperityModel", "CalculateProsperityChange");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultCombatSimulationModel", "SimulateHit");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel", "CalculateClanIncomeInternal");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementLoyaltyModel", "CalculateLoyaltyChange");
            yield return Target("TaleWorlds.CampaignSystem.CampaignBehaviors.RecruitmentCampaignBehavior", "ApplyInternal");
            yield return Target("TaleWorlds.CampaignSystem.CampaignBehaviors.InfluenceGainCampaignBehavior", "OnPrisonerDonatedToSettlement");
            yield return Target("TaleWorlds.CampaignSystem.Election.DeclareWarDecision", "DetermineSupport");
            yield return Target("TaleWorlds.CampaignSystem.Election.MakePeaceKingdomDecision", "DetermineSupport");
            yield return Target("TaleWorlds.CampaignSystem.Kingdom", "AddDecision");
            yield return Target("TaleWorlds.CampaignSystem.Actions.MakePeaceAction", "Apply");
            yield return Target("TaleWorlds.CampaignSystem.Actions.MakePeaceAction", "ApplyByKingdomDecision");
            yield return Target("TaleWorlds.CampaignSystem.GameComponents.DefaultTradeItemPriceFactorModel", "GetBasePriceFactor");
            yield return Target("TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitmentVM", "RefreshScreen");
            yield return Target("TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitmentVM", "RefreshPartyProperties");
        }

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
        public void DeclarativeHarmonyPatchTargetStillExists(string typeName, string methodName)
        {
            Type type = ResolveType(typeName);
            MethodInfo? method = FindMethod(type, methodName);

            Assert.True(method != null, $"Missing declarative Harmony patch target {typeName}.{methodName}.");
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
