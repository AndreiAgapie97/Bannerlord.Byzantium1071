using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Patches;
using HarmonyLib;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;


namespace Byzantium1071
{
    public class SubModule : MBSubModuleBase
    {
        private Harmony? _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            _harmony = new Harmony("com.andrei.byzantium1071");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            B1071_SettlementTooltipManpowerPatch.TryEnableAndPatch(_harmony);

        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();

        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            if (game.GameType is TaleWorlds.CampaignSystem.Campaign && gameStarterObject is CampaignGameStarter starter)
            {
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_ManpowerBehavior());
                starter.AddModel(new Byzantium1071.Campaign.Models.B1071_ManpowerVolunteerModel());
            }
        }
    }
}