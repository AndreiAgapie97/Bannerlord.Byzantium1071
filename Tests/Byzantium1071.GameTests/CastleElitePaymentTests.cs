#if GAME_TESTS_ENABLED
using System;
using System.Runtime.Serialization;
using Byzantium1071.Campaign.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using Xunit;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class CastleElitePaymentTests
    {
        private static CampaignEventDispatcher? _dispatcher;
        private static int _events;

        private static bool DispatcherPrefix(ref CampaignEventDispatcher __result)
        {
            __result = _dispatcher!;
            return false;
        }

        private static bool GoldEventPrefix() { _events++; return false; }

        [Theory]
        [InlineData("TryRecruitElite")]
        [InlineData("AiAutoRecruit")]
        public void PlayerAndAiUseTheTestedPaymentPath(string method)
        {
            var behavior = typeof(B1071_CastleRecruitmentBehavior);
            var payment = AccessTools.Method(behavior, "PayEliteRecruitment");
            Assert.Contains(PatchProcessor.GetOriginalInstructions(AccessTools.Method(behavior, method)),
                instruction => Equals(instruction.operand, payment));
        }

        [Theory]
        [InlineData(true, true, 1, 150, 850, 850)] // Castle owner, one recruit.
        [InlineData(true, true, 4, 150, 400, 400)] // Castle owner, AI batch.
        [InlineData(true, false, 1, 150, 850, 1000)] // Fellow clan member.
        [InlineData(true, false, 4, 150, 400, 1000)]
        [InlineData(false, false, 1, 300, 700, 1300)] // Visiting clan still pays owner.
        [InlineData(false, false, 3, 300, 100, 1900)]
        [InlineData(true, true, 1, 0, 1000, 1000)]
        public void RecruitmentDebitsQuotedCostWithoutRefundingTheHousehold(
            bool sameClan, bool isOwner, int count, int costPer, int expectedRecruiter, int expectedOwner)
        {
            var harmony = new Harmony("B1071.Tests.CastleElitePayment");
            try
            {
                // Use the installed game's GiveGoldAction and real Hero gold balances.
                // Only campaign event dispatch is stubbed: there is no running campaign here.
                _dispatcher = Empty<CampaignEventDispatcher>();
                _events = 0;
                harmony.Patch(AccessTools.PropertyGetter(typeof(CampaignEventDispatcher), "Instance"),
                    prefix: new HarmonyMethod(typeof(CastleElitePaymentTests), nameof(DispatcherPrefix)));
                harmony.Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroOrPartyTradedGold"),
                    prefix: new HarmonyMethod(typeof(CastleElitePaymentTests), nameof(GoldEventPrefix)));
                Hero recruiter = Empty<Hero>();
                Hero owner = isOwner ? recruiter : Empty<Hero>();
                recruiter.Gold = owner.Gold = 1000;

                B1071_CastleRecruitmentBehavior.PayEliteRecruitment(recruiter, owner, sameClan, costPer * count);

                Assert.Equal(expectedRecruiter, recruiter.Gold);
                Assert.Equal(expectedOwner, owner.Gold);
                Assert.Equal(costPer > 0 ? 1 : 0, _events);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                _dispatcher = null;
            }
        }

        private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    }
}
#endif
