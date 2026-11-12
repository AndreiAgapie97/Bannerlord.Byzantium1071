#if GAME_TESTS_ENABLED
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using Xunit;

namespace Byzantium1071.GameTests
{
    public sealed class CompatibilityOverrideTests
    {
        public class Root { public virtual int Value() => 1; }
        public class CallsBase : Root { public override int Value() => base.Value() + 1; }
        public class Skips : Root { public override int Value() => 5; }
        public class CallsBaseOverSkips : Skips { public override int Value() => base.Value() * 2; }
        public class InheritsCallsBase : CallsBase { }
        public class Hides : Root { public new int Value() => 7; }
        public class Plain { public int Value() => 1; }
        public class PlainSubclass : Plain { public new int Value() => 2; }

        // A mod's food model that inherits from the default but never runs it: the case the
        // compatibility check used to report as working.
        public class FoodModelSkippingDefault : DefaultSettlementFoodModel
        {
            public override ExplainedNumber CalculateTownFoodStocksChange(Town town, bool includeMarketStocks = true, bool includeDescriptions = false) =>
                new ExplainedNumber(0f);
        }

        public class FoodModelCallingDefault : DefaultSettlementFoodModel
        {
            public override ExplainedNumber CalculateTownFoodStocksChange(Town town, bool includeMarketStocks = true, bool includeDescriptions = false)
            {
                ExplainedNumber result = base.CalculateTownFoodStocksChange(town, includeMarketStocks, includeDescriptions);
                result.Add(1f);
                return result;
            }
        }

        private static MethodInfo RootValue => typeof(Root).GetMethod(nameof(Root.Value))!;

        [Theory]
        [InlineData(typeof(Root))]
        [InlineData(typeof(CallsBase))]
        [InlineData(typeof(InheritsCallsBase))]
        [InlineData(typeof(Hides))]
        public void ChainThatReachesTheOriginalIsNotReported(Type activeType)
        {
            Assert.Null(B1071_CompatibilityChecker.FindOverrideSkippingOriginal(activeType, RootValue));
        }

        [Fact]
        public void OverrideThatNeverCallsBaseIsReported()
        {
            MethodInfo? skipping = B1071_CompatibilityChecker.FindOverrideSkippingOriginal(typeof(Skips), RootValue);

            Assert.Equal(typeof(Skips), skipping?.DeclaringType);
        }

        // The most derived override calls base, but the one it calls does not, so the original
        // still never runs. The override to report is the one that cuts the chain.
        [Fact]
        public void CallingBaseIntoAnOverrideThatSkipsIsStillReported()
        {
            MethodInfo? skipping = B1071_CompatibilityChecker.FindOverrideSkippingOriginal(typeof(CallsBaseOverSkips), RootValue);

            Assert.Equal(typeof(Skips), skipping?.DeclaringType);
        }

        // A non-virtual method cannot be overridden, so a subclass's method of the same name
        // never replaces it on a call through the base type.
        [Fact]
        public void NonVirtualOriginalIsNeverReported()
        {
            MethodInfo original = typeof(Plain).GetMethod(nameof(Plain.Value))!;

            Assert.Null(B1071_CompatibilityChecker.FindOverrideSkippingOriginal(typeof(PlainSubclass), original));
        }

        [Fact]
        public void FoodModelOverridesAreJudgedAgainstTheRealGameMethod()
        {
            MethodInfo original = typeof(DefaultSettlementFoodModel).GetMethod(
                nameof(DefaultSettlementFoodModel.CalculateTownFoodStocksChange),
                BindingFlags.Instance | BindingFlags.Public)!;

            Assert.Equal(typeof(FoodModelSkippingDefault),
                B1071_CompatibilityChecker.FindOverrideSkippingOriginal(typeof(FoodModelSkippingDefault), original)?.DeclaringType);
            Assert.Null(B1071_CompatibilityChecker.FindOverrideSkippingOriginal(typeof(FoodModelCallingDefault), original));
            Assert.Null(B1071_CompatibilityChecker.FindOverrideSkippingOriginal(typeof(DefaultSettlementFoodModel), original));
        }

        // "Dynamically handled" is shown to players as "Replaced - auto-patched". Only the food
        // model has code that re-patches a replacement (B1071_DevastationBehavior), so it is the
        // only check allowed to claim it.
        [Fact]
        public void OnlyTheFoodModelClaimsARuntimeWorkaround()
        {
            FieldInfo field = typeof(B1071_CompatibilityChecker).GetField("ModelChecks", BindingFlags.NonPublic | BindingFlags.Static)!;
            var checks = ((IEnumerable)field.GetValue(null)!).Cast<object>().ToList();

            var handled = checks
                .Where(c => (bool)c.GetType().GetProperty("IsDynamicallyHandled")!.GetValue(c)!)
                .Select(c => (Type)c.GetType().GetProperty("ExpectedType")!.GetValue(c)!)
                .ToList();

            Assert.Equal(new[] { typeof(DefaultSettlementFoodModel) }, handled);
        }
    }
}
#endif
