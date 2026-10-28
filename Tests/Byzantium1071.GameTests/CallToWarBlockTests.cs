#if GAME_TESTS_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Byzantium1071.Campaign;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using Xunit;

namespace Byzantium1071.GameTests
{
    // An ally's call to war is the one route into a new war that the called kingdom's council
    // never votes on, so the called kingdom's Crisis rule is applied to the call itself.
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class CallToWarBlockTests : IDisposable
    {
        private const string PatchClass = "Byzantium1071.Campaign.Patches.B1071_BlockCallToWarAtHighExhaustionPatch";
        private readonly B1071_ManpowerBehavior? _previous = B1071_ManpowerBehavior.Instance;
        private readonly B1071_McmSettings _settings = new B1071_McmSettings
        {
            EnableWarExhaustion = true, EnableExhaustionDiplomacyPressure = true,
            EnableDiplomacyPressureBands = true, EnableTruceEnforcement = true,
            // Parity applies the rule to every kingdom, so the player exemption never reads
            // Clan.PlayerClan, which needs a running campaign.
            DiplomacyEnforcePlayerParity = true,
            PressureBandRisingStart = 35f, PressureBandCrisisStart = 85f, PressureBandHysteresis = 5f
        };

        public CallToWarBlockTests() => B1071_TestHooks.Settings = _settings;

        [Theory]
        [InlineData(90f, false)] // Crisis: the called kingdom stays out of the war.
        [InlineData(50f, true)]  // Rising: the call goes ahead.
        [InlineData(0f, true)]   // No exhaustion recorded.
        public void CallToWarFollowsTheCalledKingdomsBand(float exhaustion, bool callGoesAhead)
        {
            B1071_ManpowerBehavior.Instance = Behavior("called", exhaustion);

            Assert.Equal(callGoesAhead, Prefix(Kingdom("calling"), Kingdom("called"), Kingdom("enemy")));
        }

        [Fact]
        public void CallingKingdomInCrisisCanStillCallItsAlly()
        {
            B1071_ManpowerBehavior.Instance = Behavior("calling", 90f);

            Assert.True(Prefix(Kingdom("calling"), Kingdom("called"), Kingdom("enemy")));
        }

        [Fact]
        public void PrefixBindsToTheInstalledGame()
        {
            Type patch = typeof(Byzantium1071.SubModule).Assembly.GetType(PatchClass, throwOnError: true)!;
            MethodInfo target = AccessTools.Method(
                typeof(AllianceCampaignBehavior), nameof(AllianceCampaignBehavior.StartCallToWarAgreement));
            var harmony = new Harmony("b1071.gametests.calltowar");
            try
            {
                // Harmony binds prefix parameters by name; a renamed game parameter throws here.
                harmony.CreateClassProcessor(patch).Patch();
                Assert.True(Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.owner == harmony.Id) == true,
                    "The call-to-war prefix did not attach to StartCallToWarAgreement.");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        private static bool Prefix(Kingdom calling, Kingdom called, Kingdom enemy)
        {
            Type patch = typeof(Byzantium1071.SubModule).Assembly.GetType(PatchClass, throwOnError: true)!;
            return (bool)AccessTools.Method(patch, "Prefix").Invoke(null, new object[] { calling, called, enemy });
        }

        // Loads exhaustion through the real SyncData; with no saved band it is evaluated from Low.
        private static B1071_ManpowerBehavior Behavior(string kingdomId, float exhaustion)
        {
            var behavior = new B1071_ManpowerBehavior();
            behavior.SyncData(new Store(new Dictionary<string, object>
            {
                ["B1071_WarExhaustion_Ids"] = new List<string> { kingdomId },
                ["B1071_WarExhaustion_Values"] = new List<float> { exhaustion }
            }));
            return behavior;
        }

        private static Kingdom Kingdom(string id)
        {
            var kingdom = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            kingdom.StringId = id;
            return kingdom;
        }

        private sealed class Store : IDataStore
        {
            private readonly Dictionary<string, object> _values;
            internal Store(Dictionary<string, object> values) => _values = values;
            public bool IsLoading => true;
            public bool IsSaving => false;
            public bool SyncData<T>(string key, ref T data)
            {
                if (_values.TryGetValue(key, out var value) && value is T typed) { data = typed; return true; }
                data = default!;
                return false;
            }
        }

        public void Dispose()
        {
            B1071_ManpowerBehavior.Instance = _previous;
            B1071_TestHooks.Reset();
        }
    }
}
#endif
