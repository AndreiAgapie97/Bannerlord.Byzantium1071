using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.UI;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
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
        // Error dedup: log each unique exception type periodically (not just once) to aid diagnostics.
        private readonly System.Collections.Generic.Dictionary<string, int> _exceptionCounts = new();
        private const int EXCEPTION_LOG_INTERVAL = 100; // re-log every N occurrences

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            _harmony = new Harmony("com.andrei.byzantium1071");
            PatchAssemblySafely(_harmony, Assembly.GetExecutingAssembly());

            _uiExtender = UIExtender.Create("com.andrei.byzantium1071.ui");
            _uiExtender.Register(Assembly.GetExecutingAssembly());
            _uiExtender.Enable();
        }

        private static void PatchAssemblySafely(Harmony harmony, Assembly assembly)
        {
            List<Type> patchTypes = assembly
                .GetTypes()
                .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Any())
                .ToList();

            int ok = 0;
            int failed = 0;

            foreach (Type patchType in patchTypes)
            {
                try
                {
                    harmony.CreateClassProcessor(patchType).Patch();
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    TaleWorlds.Library.Debug.Print($"[Byzantium1071] Harmony patch skipped: {patchType.FullName} ({ex.GetType().Name}: {ex.Message})");
                }
            }

            TaleWorlds.Library.Debug.Print($"[Byzantium1071] Harmony patches applied: {ok}, failed: {failed}");
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();

            _harmony?.UnpatchAll("com.andrei.byzantium1071");
            _harmony = null;

            B1071_ManpowerBehavior.Instance = null;
            B1071_OverlayController.Reset();

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
                starter.AddModel(new Byzantium1071.Campaign.Models.B1071_ManpowerMilitiaModel());
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
                string exType = ex.GetType().FullName ?? ex.GetType().Name;
                if (!_exceptionCounts.TryGetValue(exType, out int count))
                    count = 0;
                count++;
                _exceptionCounts[exType] = count;
                // Log on first occurrence and every EXCEPTION_LOG_INTERVAL thereafter
                if (count == 1 || count % EXCEPTION_LOG_INTERVAL == 0)
                    TaleWorlds.Library.Debug.Print($"[Byzantium1071] Overlay tick error ({exType}, #{count}): {ex.Message}");
            }
        }
    }
}