using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Patches;
using Byzantium1071.Campaign.UI;
using Bannerlord.UIExtenderEx;
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
        private UIExtender? _uiExtender;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            _harmony = new Harmony("com.andrei.byzantium1071");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            _uiExtender = UIExtender.Create("com.andrei.byzantium1071.ui");
            _uiExtender.Register(Assembly.GetExecutingAssembly());
            _uiExtender.Enable();

            B1071_SettlementTooltipManpowerPatch.TryEnableAndPatch(_harmony);

        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();

            _harmony?.UnpatchAll("com.andrei.byzantium1071");
            _harmony = null;

            B1071_ManpowerBehavior.Instance = null;

            _uiExtender?.Disable();
            _uiExtender?.Deregister();
            _uiExtender = null;

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

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            try
            {
                B1071_OverlayController.Tick(dt);
            }
            catch (System.Exception ex)
            {
                // Overlay must never crash gameplay or screen transitions.
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] Overlay tick error: {ex.Message}");
            }
        }
    }
}