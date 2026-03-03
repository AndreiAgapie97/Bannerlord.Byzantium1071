using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using Byzantium1071.Campaign.UI;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using Byzantium1071.Campaign;


namespace Byzantium1071
{
    public class SubModule : MBSubModuleBase
    {
        private Harmony? _harmony;
        private UIExtender? _uiExtender;
        // Error dedup: log each unique exception type periodically (not just once) to aid diagnostics.
        private readonly System.Collections.Generic.Dictionary<string, int> _exceptionCounts = new();
        private const int EXCEPTION_LOG_INTERVAL = 100; // re-log every N occurrences

        /// <summary>
        /// The custom ItemCategory for slaves, registered before XML loading so
        /// items.xml's item_category="b1071_slaves" resolves correctly.
        /// </summary>
        internal static ItemCategory? SlaveItemCategory { get; private set; }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            _harmony = new Harmony("com.andrei.byzantium1071");
            PatchAssemblySafely(_harmony, Assembly.GetExecutingAssembly());

            _uiExtender = UIExtender.Create("com.andrei.byzantium1071.ui");
            _uiExtender.Register(Assembly.GetExecutingAssembly());
            _uiExtender.Enable();
        }

        /// <summary>
        /// Register the b1071_slaves ItemCategory in the ObjectManager BEFORE
        /// XML deserialization runs. This is the same lifecycle phase that vanilla
        /// uses for DefaultItemCategories — without this, the items.xml attribute
        /// item_category="b1071_slaves" silently falls back to "unassigned"
        /// because Bannerlord's XML loader does NOT support ItemCategory XML nodes.
        /// </summary>
        public override void RegisterSubModuleObjects(bool isSavedCampaign)
        {
            base.RegisterSubModuleObjects(isSavedCampaign);

            try
            {
                // Check if already registered (e.g., from a previous game session
                // without unloading the module).
                var existing = MBObjectManager.Instance?.GetObject<ItemCategory>("b1071_slaves");
                if (existing != null)
                {
                    SlaveItemCategory = existing;
                    TaleWorlds.Library.Debug.Print("[Byzantium1071] b1071_slaves ItemCategory already registered, reusing.");
                }
                else
                {
                    // Register exactly like DefaultItemCategories.Create():
                    // ObjectManager.RegisterPresumedObject(new ItemCategory(stringId))
                    SlaveItemCategory = MBObjectManager.Instance!.RegisterPresumedObject(
                        new ItemCategory("b1071_slaves"));
                    TaleWorlds.Library.Debug.Print("[Byzantium1071] b1071_slaves ItemCategory registered in ObjectManager.");
                }

                // Initialize with trade-good properties matching our XML intent:
                //   BaseDemand  = 15 × 0.001 = 0.015  (same as wine/velvet)
                //   LuxuryDemand = 32 × 0.001 = 0.032 (same as jewelry/velvet)
                //   IsTradeGood = true
                //   IsValid     = true
                SlaveItemCategory.InitializeObject(
                    isTradeGood: true,
                    baseDemand: 15,
                    luxuryDemand: 32,
                    properties: ItemCategory.Property.None,
                    canSubstitute: null,
                    substitutionFactor: 0f,
                    isAnimal: false,
                    isValid: true);

                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] b1071_slaves initialized: IsTradeGood={SlaveItemCategory.IsTradeGood}, " +
                    $"BaseDemand={SlaveItemCategory.BaseDemand:F4}, LuxuryDemand={SlaveItemCategory.LuxuryDemand:F4}, " +
                    $"IsValid={SlaveItemCategory.IsValid}");
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071][ERROR] Failed to register b1071_slaves ItemCategory: {ex.GetType().Name}: {ex.Message}");
            }
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
            B1071_SessionAudit.SetHarmonyPatchResults(ok, failed);
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();

            B1071_SessionFileLog.EndSession();

            _harmony?.UnpatchAll("com.andrei.byzantium1071");
            _harmony = null;

            B1071_CompatibilityFluentSettings.Unregister();
            B1071_QuickSettingsFluentSettings.Unregister();
            B1071_CompatibilityBehavior.Instance = null;
            B1071_ManpowerBehavior.Instance = null;
            B1071_SlaveEconomyBehavior.Instance = null;
            B1071_GovernanceBehavior.Instance = null;
            B1071_DevastationBehavior.Instance = null;
            B1071_CastleRecruitmentBehavior.Instance = null;
            B1071_VillageInvestmentBehavior.Instance = null;
            B1071_TownInvestmentBehavior.Instance = null;
            B1071_ClanSurvivalBehavior.Instance = null;
            Byzantium1071.Campaign.Patches.B1071_ClanSurvivalPatch._alreadyRescued.Clear();
            B1071_DevastationBehavior.ResetDynamicPatchFlag();
            B1071_OverlayController.Reset();

            _uiExtender?.Disable();
            _uiExtender?.Deregister();
            _uiExtender = null;

        }

        /// <summary>
        /// Called when a campaign ends (player returns to main menu).
        /// Null stale singletons so a fresh campaign starts clean.
        /// </summary>
        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            B1071_SessionAudit.EmitEndOfSessionSummary();
            B1071_SessionFileLog.EndSession();
            B1071_CompatibilityBehavior.Instance = null;
            B1071_ManpowerBehavior.Instance = null;
            B1071_SlaveEconomyBehavior.Instance = null;
            B1071_GovernanceBehavior.Instance = null;
            B1071_DevastationBehavior.Instance = null;
            B1071_CastleRecruitmentBehavior.Instance = null;
            B1071_VillageInvestmentBehavior.Instance = null;
            B1071_TownInvestmentBehavior.Instance = null;
            B1071_ClanSurvivalBehavior.Instance = null;
            Byzantium1071.Campaign.Patches.B1071_ClanSurvivalPatch._alreadyRescued.Clear();
            B1071_OverlayController.Reset();
            _exceptionCounts.Clear();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            // MCM is initialized by this point. Run one-time settings migration.
            try
            {
                string? migrationMessage = B1071_McmSettings.Instance?.MigrateToLatestProfile();
                if (!string.IsNullOrEmpty(migrationMessage))
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        migrationMessage, TaleWorlds.Library.Colors.Green));
                }
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] Settings migration error: {ex}");
            }

            // Stage 1 compatibility check: scan all active Harmony patches for method overlaps.
            // All patches from all mods are applied by this point, so results are stable.
            // Stage 2 (model replacement check + popup) runs per campaign session in B1071_CompatibilityBehavior.
            try
            {
                B1071_CompatibilityChecker.RunHarmonyChecks();
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] Compat harmony check error: {ex.GetType().Name}: {ex.Message}");
            }

            // Build and register the "Campaign++ Compatibility" MCM tab (Fluent API).
            // Must run after RunHarmonyChecks() so conflict entries are ready, and at this
            // point MCM's DI container is initialized (OnBeforeInitialModuleScreenSetAsRoot timing).
            try
            {
                B1071_CompatibilityFluentSettings.BuildAndRegister();
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] CompatibilityFluentSettings.BuildAndRegister error: {ex.GetType().Name}: {ex.Message}");
            }

            // Quick Settings mirror tab — one-screen access to every system toggle.
            try
            {
                B1071_QuickSettingsFluentSettings.BuildAndRegister();
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] QuickSettingsFluentSettings.BuildAndRegister error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            B1071_SessionFileLog.BeginSession();
            B1071_SessionAudit.ResetForNewSession();
            if (!string.IsNullOrWhiteSpace(B1071_SessionFileLog.CurrentLogPath))
                TaleWorlds.Library.Debug.Print($"[Byzantium1071] Session file log: {B1071_SessionFileLog.CurrentLogPath}");

            if (game.GameType is TaleWorlds.CampaignSystem.Campaign && gameStarterObject is CampaignGameStarter starter)
            {
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_CompatibilityBehavior());
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_ManpowerBehavior());
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_SlaveEconomyBehavior());
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_GovernanceBehavior());
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_DevastationBehavior());
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_CastleRecruitmentBehavior());
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_VillageInvestmentBehavior());
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_TownInvestmentBehavior());
                starter.AddBehavior(new Byzantium1071.Campaign.Behaviors.B1071_ClanSurvivalBehavior());
                // Volunteer model is now a Harmony Postfix (B1071_ManpowerVolunteerPatch)
                // instead of AddModel, for compatibility with mods that replace VolunteerModel.
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
                // Cap dictionary size to prevent unbounded memory growth from exotic exception types
                if (_exceptionCounts.Count > 256 && !_exceptionCounts.ContainsKey(exType))
                    _exceptionCounts.Clear();
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