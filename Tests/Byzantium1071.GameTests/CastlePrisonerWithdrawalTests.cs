#if GAME_TESTS_ENABLED
using System;
using System.Runtime.Serialization;
using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using Xunit;

namespace Byzantium1071.GameTests
{
    [Collection(nameof(CastleRecruitmentActionCollection))]
    public sealed class CastlePrisonerWithdrawalTests : IDisposable
    {
        private readonly Harmony _harmony = new Harmony("B1071.Tests.CastlePrisonerWithdrawal");
        private static Settlement _castle = null!;
        private static PartyBase _party = null!;
        private static TroopRoster _roster = null!;
        private readonly B1071_CastleRecruitmentBehavior? _previous = B1071_CastleRecruitmentBehavior.Instance;
        private readonly B1071_CastleRecruitmentBehavior _behavior = new B1071_CastleRecruitmentBehavior();
        private readonly CharacterObject _troop = Empty<CharacterObject>();
        private readonly PartyScreenLogic _logic = Empty<PartyScreenLogic>();

        public CastlePrisonerWithdrawalTests()
        {
            _castle = Empty<Settlement>(); _castle.StringId = "castle";
            _party = Empty<PartyBase>();
            _roster = TroopRoster.CreateDummyTroopRoster();
            _troop.StringId = "troop";
            _roster.AddToCounts(_troop, 10);
            B1071_CastleRecruitmentBehavior.Instance = _behavior;
            PatchGetter(typeof(Settlement), "CurrentSettlement", nameof(Castle));
            PatchGetter(typeof(Settlement), "IsCastle", nameof(IsCastle));
            PatchGetter(typeof(Settlement), "Party", nameof(Party));
            PatchGetter(typeof(PartyBase), "MainParty", nameof(Party));
            PatchGetter(typeof(PartyBase), "PrisonRoster", nameof(Roster));
            PatchGetter(typeof(CharacterObject), "Tier", nameof(Tier));
            PatchGetter(typeof(CharacterObject), "PlayerCharacter", nameof(Player));
            PatchGetter(typeof(CharacterObject), "IsNotTransferableInPartyScreen", nameof(Transferable));
            PatchGetter(typeof(Hero), "MainHero", nameof(MainHero));
            // Only the campaign-dependent price lookup is supplied. Real FIFO records,
            // native validation, history and mod patches are exercised below.
            _harmony.Patch(AccessTools.Method(typeof(B1071_CastleRecruitmentBehavior), "GetEffectiveGoldCost"),
                prefix: new HarmonyMethod(typeof(CastlePrisonerWithdrawalTests), nameof(Fee)));
            _harmony.Patch(AccessTools.Method(typeof(InformationManager), "DisplayMessage", new[] { typeof(InformationMessage) }),
                prefix: new HarmonyMethod(typeof(CastlePrisonerWithdrawalTests), nameof(NoMessage)));
            Set("RightOwnerParty", _party);
            Set("CurrentData", new PartyScreenData());
            Set("PrisonerTransferState", PartyScreenLogic.TransferState.Transferable);
            _logic.PrisonerRosters = new[] { _roster, TroopRoster.CreateDummyTroopRoster() };
            _harmony.CreateClassProcessor(typeof(B1071_CastlePrisonerWithdrawalPatch)).Patch();
            _harmony.CreateClassProcessor(typeof(B1071_CastlePrisonerWithdrawalCommandPatch)).Patch();
        }

        [Fact]
        public void PaidEntryBehindFreeEntryBlocksSingleAndBulkWithdrawalWithoutConsumingFifo()
        {
            _behavior.RecordDeposit("castle", "free", "troop", 3);
            _behavior.RecordDeposit("castle", "paid", "troop", 7);
            Assert.False(_logic.IsTroopTransferable(PartyScreenLogic.TroopType.Prisoner, _troop, 0));
            Assert.False(Validate(1));
            Assert.False(Validate(10));
            Assert.Equal(new (string?, int)[] { ("free", 3), ("paid", 7) }, _behavior.GetPrisonerDepositors("castle", "troop", 10));
            Assert.Equal(10, _roster.GetTroopCount(_troop));
        }

        [Fact]
        public void DepositsCanBeUndoneButBulkCommandsCannotTakeOriginalConsignments()
        {
            _behavior.RecordDeposit("castle", "paid", "troop", 10);
            _roster.AddToCounts(_troop, 2);
            _logic.CurrentData.TransferredPrisonersHistory.Add(Tuple.Create(_troop, -2));
            Assert.True(_logic.IsTroopTransferable(PartyScreenLogic.TroopType.Prisoner, _troop, 0));
            Assert.True(Validate(2));
            Assert.False(Validate(3));
            Assert.False(Validate(12));
            _roster.AddToCounts(_troop, -2);
            _logic.CurrentData.TransferredPrisonersHistory.Clear();
            Assert.False(Validate(1));
        }

        [Fact]
        public void FreeAndUntrackedPrisonersRemainTransferableAndNativeRejectionsRemainFalse()
        {
            _behavior.RecordDeposit("castle", "free", "troop", 4);
            Assert.True(_logic.IsTroopTransferable(PartyScreenLogic.TroopType.Prisoner, _troop, 0));
            Assert.True(Validate(10));
            Assert.False(Validate(11));
            _logic.IsTroopTransferableDelegate = (character, type, side, owner) => false;
            Assert.False(_logic.IsTroopTransferable(PartyScreenLogic.TroopType.Prisoner, _troop, 0));
        }

        [Fact]
        public void OtherRostersAndDepositingIntoTheCastleAreUnaffected()
        {
            _behavior.RecordDeposit("castle", "paid", "troop", 10);
            _logic.PrisonerRosters[1].AddToCounts(_troop, 2);
            Assert.True(Validate(2, PartyScreenLogic.PartyRosterSide.Right));
            var unrelated = TroopRoster.CreateDummyTroopRoster();
            unrelated.AddToCounts(_troop, 10);
            _logic.PrisonerRosters[0] = unrelated;
            Assert.True(Validate(10));
        }

        private bool Validate(int count, PartyScreenLogic.PartyRosterSide side = PartyScreenLogic.PartyRosterSide.Left)
        {
            var command = new PartyScreenLogic.PartyCommand();
            command.FillForTransferTroop(side, PartyScreenLogic.TroopType.Prisoner, _troop, count, 0, -1);
            return _logic.ValidateCommand(command);
        }
        private void Set(string property, object value) => AccessTools.PropertySetter(typeof(PartyScreenLogic), property).Invoke(_logic, new[] { value });
        private void PatchGetter(Type type, string property, string prefix) => _harmony.Patch(
            AccessTools.PropertyGetter(type, property), prefix: new HarmonyMethod(typeof(CastlePrisonerWithdrawalTests), prefix));
        private static bool Castle(ref Settlement __result) { __result = _castle; return false; }
        private static bool IsCastle(ref bool __result) { __result = true; return false; }
        private static bool Party(ref PartyBase __result) { __result = _party; return false; }
        private static bool Roster(ref TroopRoster __result) { __result = _roster; return false; }
        private static bool Tier(ref int __result) { __result = 4; return false; }
        private static bool Player(ref CharacterObject __result) { __result = null!; return false; }
        private static bool MainHero(ref Hero __result) { __result = null!; return false; }
        private static bool Transferable(ref bool __result) { __result = false; return false; }
        private static bool Fee(string? depositorHeroId, ref int __result) { __result = depositorHeroId == "paid" ? 100 : 0; return false; }
        private static bool NoMessage() => false;
        private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        public void Dispose()
        {
            _harmony.UnpatchAll(_harmony.Id);
            B1071_CastleRecruitmentBehavior.Instance = _previous;
        }
    }
}
#endif
