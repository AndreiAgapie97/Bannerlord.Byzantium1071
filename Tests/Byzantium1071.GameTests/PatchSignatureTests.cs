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

        [Theory]
        [MemberData(nameof(CriticalTargets))]
        public void CriticalStringBasedPatchTargetStillExists(string typeName, string methodName)
        {
            Type type = ResolveType(typeName);
            MethodInfo? method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.True(method != null, $"Missing patched method {typeName}.{methodName}.");
        }

        private static object[] Target(string typeName, string methodName) => new object[] { typeName, methodName };

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
