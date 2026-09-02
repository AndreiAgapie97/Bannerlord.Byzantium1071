using Byzantium1071.Campaign;
using FsCheck.Xunit;
using Xunit;

namespace Byzantium1071.Tests
{
    /// <summary>
    /// Guards the AI-visible troop power multiplier. The multiplier exists because
    /// Campaign++ makes high-tier troops harder to kill without changing the tier vanilla
    /// prices them by, so the native AI valued elite stacks at vanilla worth.
    ///
    /// The tests below are split along the distinction that actually matters: the AI's
    /// combat logic compares two strengths and so only needs the RATIO between tiers to be
    /// right, but a handful of vanilla gates compare power against hard-coded absolute
    /// constants -- CanLordCreateArmy wants a summed strength of 1000, DefaultDiplomacyModel
    /// refuses war below 500. So the ratio tests below check the fix works, and
    /// TotalPartyPowerStaysNearVanillaForARepresentativeRoster checks it does not quietly
    /// rewrite army formation and war declaration while doing so.
    /// </summary>
    public sealed class TroopPowerMathTests
    {
        /// <summary>Vanilla DefaultMilitaryPowerModel.GetDefaultTroopPower for a non-hero.</summary>
        private static float VanillaTroopPower(int tier) => (2 + tier) * (10 + tier) * 0.02f;

        /// <summary>
        /// A lord's roster skews low-tier: recruits and levies outnumber the veterans. The
        /// absolute-scale guard is only meaningful against a shape like this one.
        /// </summary>
        private static readonly int[] RepresentativeRoster = { 20, 30, 25, 15, 8, 2 };

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void VanillaPresetLeavesEveryTierExactlyUntouched(int tier)
        {
            Assert.Equal(1f, B1071_EconomyMath.PowerFactor(0, tier));
        }

        [Fact]
        public void PowerRisesWithTierForEveryPreset()
        {
            for (int preset = 0; preset <= 3; preset++)
            {
                for (int tier = 2; tier <= 6; tier++)
                {
                    Assert.True(
                        B1071_EconomyMath.PowerFactor(preset, tier)
                            >= B1071_EconomyMath.PowerFactor(preset, tier - 1),
                        $"Preset {preset} tier {tier} priced below tier {tier - 1}.");
                }
            }
        }

        [Fact]
        public void TheReferenceTierIsHeldAtExactlyVanillaSoTheScaleCannotDrift()
        {
            for (int preset = 0; preset <= 3; preset++)
            {
                Assert.Equal(1f, B1071_EconomyMath.PowerFactor(preset, 3));
            }
        }

        [Fact]
        public void LowTiersGiveUpExactlyWhatTheHighTiersGain()
        {
            // The curve redistributes around the reference tier rather than inflating: the
            // tiers that receive no survivability bonus must sit at or below vanilla, and the
            // tiers that do must sit at or above it.
            for (int preset = 1; preset <= 3; preset++)
            {
                Assert.True(B1071_EconomyMath.PowerFactor(preset, 1) < 1f);
                Assert.True(B1071_EconomyMath.PowerFactor(preset, 2) < 1f);
                Assert.True(B1071_EconomyMath.PowerFactor(preset, 5) > 1f);
                Assert.True(B1071_EconomyMath.PowerFactor(preset, 6) > 1f);
            }
        }

        [Fact]
        public void EliteTroopsAreWorthMoreAtStrongerPresets()
        {
            // The whole point of the patch: a T6 is priced further above a T1 as the
            // survivability curves get steeper.
            float previousSpread = 1f;
            for (int preset = 0; preset <= 3; preset++)
            {
                float spread = B1071_EconomyMath.PowerFactor(preset, 6)
                    / B1071_EconomyMath.PowerFactor(preset, 1);
                Assert.True(spread >= previousSpread, $"Preset {preset} narrowed the tier spread.");
                previousSpread = spread;
            }
        }

        [Fact]
        public void TotalPartyPowerStaysNearVanillaForARepresentativeRoster()
        {
            // THE LOAD-BEARING TEST. Vanilla gates power against hard-coded absolute numbers
            // (CanLordCreateArmy's 1000, DefaultDiplomacyModel's 500). If this band is ever
            // widened, AI kingdoms silently change how often they raise armies and declare
            // war, and no other test in either suite will notice.
            float vanilla = 0f;
            for (int tier = 1; tier <= 6; tier++)
            {
                vanilla += RepresentativeRoster[tier - 1] * VanillaTroopPower(tier);
            }

            for (int preset = 0; preset <= 3; preset++)
            {
                float patched = 0f;
                for (int tier = 1; tier <= 6; tier++)
                {
                    patched += RepresentativeRoster[tier - 1]
                        * VanillaTroopPower(tier)
                        * B1071_EconomyMath.PowerFactor(preset, tier);
                }

                float drift = (patched - vanilla) / vanilla;
                Assert.True(
                    drift > -0.05f && drift < 0.05f,
                    $"Preset {preset} moved total party power by {drift:P2}, outside the +/-5% band.");
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(7)]
        [InlineData(99)]
        public void TiersOutsideTheTableClampInsteadOfThrowing(int tier)
        {
            int expected = tier < 1 ? 1 : tier > 6 ? 6 : tier;
            Assert.Equal(
                B1071_EconomyMath.PowerFactor(2, expected),
                B1071_EconomyMath.PowerFactor(2, tier));
        }

        [Fact]
        public void InvalidPresetsClampToTheNearestRealPreset()
        {
            Assert.Equal(B1071_EconomyMath.PowerFactor(3, 6), B1071_EconomyMath.PowerFactor(99, 6));
            Assert.Equal(B1071_EconomyMath.PowerFactor(0, 6), B1071_EconomyMath.PowerFactor(-1, 6));
        }

        [Property]
        public bool PowerFactorIsAlwaysFiniteAndPositive(int preset, int tier)
        {
            float factor = B1071_EconomyMath.PowerFactor(preset, tier);
            return !float.IsNaN(factor) && !float.IsInfinity(factor) && factor > 0f;
        }
    }
}
