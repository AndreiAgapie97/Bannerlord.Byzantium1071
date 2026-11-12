#if GAME_TESTS_ENABLED
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Byzantium1071.Campaign.Patches;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using Xunit;

namespace Byzantium1071.GameTests
{
    [CollectionDefinition("Revenue tuning", DisableParallelization = true)]
    public sealed class RevenueTuningCollection { }

    [Collection("Revenue tuning")]
    public sealed class RevenueTuningRegressionTests
    {
        [Theory]
        [InlineData(0f, 0.5f)]
        [InlineData(0.5f, 0.5f)]
        [InlineData(-0.5f, 0.5f)]
        [InlineData(1f, 0.25f)]
        [InlineData(0.5f, 0.25f)]
        public void TaperScalesTheFinalAmountOnceAndKeepsTheBreakdown(float factor, float scale)
        {
            var number = new ExplainedNumber(1000f, true, new TextObject("Base"));
            number.AddFactor(factor, new TextObject("Modifier"));
            float before = number.ResultNumber;
            var originalLines = number.GetLines().ToArray();

            B1071_RevenueTaper.Apply(ref number, scale);

            Assert.Equal((float)Math.Floor(before * scale), number.ResultNumber);
            foreach (var line in originalLines)
                Assert.Contains(line, number.GetLines());
            Assert.Equal(number.ResultNumber, number.GetLines().Sum(line => line.number), 3);
            Assert.InRange(number.ResultNumber, 0f, before);
        }

        [Theory]
        [InlineData(-50f, 0.5f)]
        [InlineData(0f, 0.5f)]
        [InlineData(123.75f, 1f)]
        public void NonpositiveOrDisabledIncomeIsUnchanged(float income, float scale)
        {
            var number = new ExplainedNumber(income);
            B1071_RevenueTaper.Apply(ref number, scale);
            Assert.Equal(income, number.ResultNumber);
        }

        [Fact]
        public void TaperRespectsTheResolvedClampAndFractionalAmount()
        {
            var number = new ExplainedNumber(1000.75f, true, new TextObject("Base"));
            number.AddFactor(0.5f, new TextObject("Bonus"));
            number.Clamp(0f, 900.5f);
            B1071_RevenueTaper.Apply(ref number, 0.5f);
            Assert.Equal(450f, number.ResultNumber);
            Assert.Equal(450f, number.GetLines().Sum(line => line.number), 3);
        }

        [Fact]
        public void LargeFiniteIncomeDoesNotOverflowAnIntegerDelta()
        {
            var number = new ExplainedNumber(4_000_000_000f);
            B1071_RevenueTaper.Apply(ref number, 0.75f);
            Assert.Equal(3_000_000_000f, number.ResultNumber);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PreviewAndWithdrawalUseTheSameOriginalPool(bool isTown)
        {
            var settings = B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;
            bool enabled = settings.EnableSettlementRevenueTuning;
            int strength = isTown ? settings.SettlementTariffStrengthTown : settings.SettlementTariffStrengthVillage;
            float curve = isTown ? settings.SettlementTariffCurveTown : settings.SettlementTariffCurveVillage;
            int knee = isTown ? settings.SettlementTariffKneeTown : settings.SettlementTariffKneeVillage;
            try
            {
                settings.EnableSettlementRevenueTuning = true;
                if (isTown)
                {
                    settings.SettlementTariffStrengthTown = 60;
                    settings.SettlementTariffCurveTown = 2f;
                    settings.SettlementTariffKneeTown = 0;
                }
                else
                {
                    settings.SettlementTariffStrengthVillage = 60;
                    settings.SettlementTariffCurveVillage = 2f;
                    settings.SettlementTariffKneeVillage = 0;
                }

                // Exercise the real hooks around vanilla's verified withdrawal, without needing
                // a running campaign/governor. Binding to the actual methods is tested separately.
                Type patch = isTown ? typeof(B1071_SettlementTariffTuningPatch) : typeof(B1071_SettlementVillageTariffTuningPatch);
                object settlement = FormatterServices.GetUninitializedObject(isTown ? typeof(Town) : typeof(Village));
                PropertyInfo pool = settlement.GetType().GetProperty("TradeTaxAccumulated")!;
                var model = (DefaultClanFinanceModel)FormatterServices.GetUninitializedObject(typeof(DefaultClanFinanceModel));
                MethodInfo? prefix = patch.GetMethod("Prefix");
                Assert.NotNull(prefix);
                float? preview = null;
                foreach (bool withdraw in new[] { false, true })
                {
                    pool.SetValue(settlement, 60000);
                    object[] captured = { model, settlement, 0 };
                    prefix!.Invoke(null, captured);
                    Assert.Equal(12000, captured[2]);
                    Assert.Equal(60000, pool.GetValue(settlement));
                    if (withdraw) pool.SetValue(settlement, 48000);
                    object[] args = { null!, settlement, withdraw, isTown ? (object)new ExplainedNumber(12000f) : 12000, captured[2] };
                    patch.GetMethod("Postfix")!.Invoke(null, args);
                    float payout = isTown ? ((ExplainedNumber)args[3]).ResultNumber : (int)args[3];
                    Assert.InRange(payout, 1f, 11999f);
                    if (preview.HasValue) Assert.Equal(preview.Value, payout);
                    else preview = payout;
                    Assert.Equal(withdraw ? 48000 : 60000, pool.GetValue(settlement));
                }
            }
            finally
            {
                settings.EnableSettlementRevenueTuning = enabled;
                if (isTown)
                {
                    settings.SettlementTariffStrengthTown = strength;
                    settings.SettlementTariffCurveTown = curve;
                    settings.SettlementTariffKneeTown = knee;
                }
                else
                {
                    settings.SettlementTariffStrengthVillage = strength;
                    settings.SettlementTariffCurveVillage = curve;
                    settings.SettlementTariffKneeVillage = knee;
                }
            }
        }
    }
}
#endif
