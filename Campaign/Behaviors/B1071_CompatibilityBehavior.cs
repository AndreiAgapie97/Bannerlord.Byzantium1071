using Byzantium1071.Campaign.Settings;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Runs the model-replacement compatibility check at campaign session start and shows
    /// the popup inquiry to the player every session (new game and loaded saves).
    /// Stage 1 (Harmony checks) runs earlier in SubModule.OnBeforeInitialModuleScreenSetAsRoot.
    /// </summary>
    public class B1071_CompatibilityBehavior : CampaignBehaviorBase
    {
        public static B1071_CompatibilityBehavior? Instance { get; internal set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                // Reset and re-run model checks for this session (models can differ per save/new game).
                B1071_CompatibilityChecker.ResetModelChecks();
                B1071_CompatibilityChecker.RunModelChecks();

                // Re-run the Harmony scan now that all mods (including those that patch lazily
                // on first campaign load, e.g. RBM) have had a chance to apply their patches.
                // The title-screen scan caught mods that patch at startup; this catches the rest.
                B1071_CompatibilityChecker.RunHarmonyChecks();

                // Rebuild the fluent MCM tab so the per-session model replacement data is current.
                // Safe to call even if the initial Build failed — it will retry.
                B1071_CompatibilityFluentSettings.Rebuild();

                // Show the popup unless the player has opted out via the suppress toggle in MCM.
                if (!B1071_CompatibilityFluentSettings.SuppressPopup)
                {
                    string popupText = B1071_CompatibilityChecker.BuildPopupText();

                    InformationManager.ShowInquiry(new InquiryData(
                        titleText: new TextObject("{=b1071_compat_title}Campaign++ - Playing Well With Others?").ToString(),
                        text: popupText,
                        isAffirmativeOptionShown: true,
                        isNegativeOptionShown: true,
                        affirmativeText: new TextObject("{=b1071_compat_ok}OK").ToString(),
                        negativeText: new TextObject("{=b1071_compat_copy_report}Copy Report").ToString(),
                        affirmativeAction: null,
                        negativeAction: () => B1071_CompatibilityFluentSettings.CopyToClipboard(popupText)
                    ));
                }
            }
            catch (Exception ex)
            {
                Debug.Print(
                    $"[Byzantium1071] CompatibilityBehavior.OnSessionLaunched error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public override void SyncData(IDataStore dataStore) { }
    }
}
