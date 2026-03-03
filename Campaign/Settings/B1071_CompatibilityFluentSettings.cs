using MCM.Abstractions.Base.Global;
using MCM.Abstractions.FluentBuilder;
using MCM.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TLCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace Byzantium1071.Campaign.Settings
{
    /// <summary>
    /// Manages the "Campaign++ Compatibility" tab in the MCM mod options menu.
    /// Uses the MCM Fluent Builder API to generate exactly one property per detected
    /// Harmony conflict — no empty placeholder slots. The tab is rebuilt at each
    /// campaign session start to reflect updated model replacement data.
    ///
    /// Lifecycle:
    ///   BuildAndRegister() — called at OnBeforeInitialModuleScreenSetAsRoot (after RunHarmonyChecks)
    ///   Rebuild()          — called at OnSessionLaunched (after RunModelChecks)
    ///   Unregister()       — called at OnSubModuleUnloaded
    /// </summary>
    internal static class B1071_CompatibilityFluentSettings
    {
        // ── State ──────────────────────────────────────────────────────────────────

        private static FluentGlobalSettings? _instance;

        /// <summary>
        /// User-configurable: suppress the automatic popup at campaign session start.
        /// Persisted to JSON by MCM via the ProxyRef setter.
        /// </summary>
        private static bool _suppressCompatPopup = false;

        /// <summary>Read by B1071_CompatibilityBehavior before showing the popup.</summary>
        internal static bool SuppressPopup => _suppressCompatPopup;

        // Cached delegate so AddButton always gets the same instance.
        private static readonly Action _showReportDelegate = static () => ShowReportAction();

        // ── Public lifecycle ───────────────────────────────────────────────────────

        /// <summary>
        /// Initial build + register. Call at OnBeforeInitialModuleScreenSetAsRoot,
        /// after RunHarmonyChecks() has populated the conflict list.
        /// </summary>
        internal static void BuildAndRegister()
        {
            _instance?.Unregister();
            _instance = Build();
            _instance?.Register();
        }

        /// <summary>
        /// Rebuilds and re-registers after a campaign session's model checks complete.
        /// Keeps the old registration alive if Build() fails, so the tab never disappears.
        /// </summary>
        internal static void Rebuild()
        {
            var newInstance = Build();
            if (newInstance == null) return; // keep old registration if build fails

            _instance?.Unregister();
            _instance = newInstance;
            _instance.Register();
        }

        /// <summary>Unregister from MCM — call at OnSubModuleUnloaded.</summary>
        internal static void Unregister()
        {
            _instance?.Unregister();
            _instance = null;
        }

        // ── Builder ────────────────────────────────────────────────────────────────

        private static FluentGlobalSettings? Build()
        {
            try
            {
                var harmonyConflicts = B1071_CompatibilityChecker.GetHarmonyConflicts();
                var modelNames = B1071_CompatibilityChecker.GetModelCheckNames();

                ISettingsBuilder? builder = BaseSettingsBuilder
                    .Create("b1071_compat", "Campaign++ Compatibility")
                    ?.SetFolderName("Byzantium1071")
                    ?.SetFormat("json"); // persists the suppress toggle

                if (builder == null) return null;

                // ── Summary group ──────────────────────────────────────────────────
                // NOTE: SetHintText is NOT on ISettingsPropertyBuilder base — it lives on each
                // specific sub-interface. Always call it directly on the builder param (b), never
                // on a chained result that has been widened to ISettingsPropertyBuilder.
                builder = builder.CreateGroup("Summary", g =>
                {
                    g.SetGroupOrder(0);

                    // Suppress popup toggle — the only user-editable property in this tab.
                    g.AddBool("suppress_popup",
                        "Don't show this popup at startup",
                        new ProxyRef<bool>(() => _suppressCompatPopup, v => _suppressCompatPopup = v),
                        b =>
                        {
                            b.SetOrder(0);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                "Turn this on if you don't want the compatibility report popping up every time you start a campaign. " +
                                "You can always read the report by clicking the button below.");
                        });

                    g.AddText("overlap_summary",
                        "Running alongside",
                        new ProxyRef<string>(
                            () => B1071_CompatibilityChecker.OverallStatusSummaryText(),
                            v => { }),
                        b =>
                        {
                            b.SetOrder(1);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                "This tells you at a glance whether Campaign++ is running smoothly alongside your other mods. " +
                                "\"Runs fine\" means both mods are active and their effects add up — no crashes, no missing features. " +
                                "\"Check below\" means one interaction is worth reviewing (hover those rows for plain-language details).");
                        });

                    g.AddText("model_summary",
                        "Core game systems",
                        new ProxyRef<string>(
                            () => B1071_CompatibilityChecker.ModelStatusSummaryText(),
                            v => { }),
                        b =>
                        {
                            b.SetOrder(2);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                "Some mods replace the game's internal calculation engines entirely. " +
                                "This checks whether the systems Campaign++ relies on are intact. " +
                                "\"All normal\" means everything is working. If an issue is found, hover the rows in the section below.");
                        });

                    g.AddText("quick_guide",
                        "Tip",
                        new ProxyRef<string>(
                            () => "Hover rows for details",
                            v => { }),
                        b =>
                        {
                            b.SetOrder(3);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                "Every row in the sections below has a plain-language explanation when you hover over it. " +
                                "You don't need to change anything unless something in-game feels off.");
                        });

                    // Button to re-open the popup on demand.
                    g.AddButton("show_report",
                        "Open Full Report",
                        new ProxyRef<Action>(() => _showReportDelegate, v => { }),
                        "Open Report",
                        b =>
                        {
                            b.SetOrder(4);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                "Opens a detailed compatibility report. " +
                                "Load a campaign first to get the full picture including game system checks.");
                        });
                });

                // ── One group per conflicting mod (dynamically generated) ───────────
                //
                // Groups are ordered by max risk (WARNING first). Inside each group,
                // one text property per method conflict, ordered by risk descending.
                var byMod = harmonyConflicts
                    .GroupBy(c => c.OtherHarmonyId)
                    .OrderByDescending(grp => grp.Max(c => (int)c.Risk))
                    .ToList();

                int modGroupOrder = 10;
                foreach (var modGroup in byMod)
                {
                    string modId = modGroup.Key;
                    string friendlyName = B1071_CompatibilityChecker.FriendlyModName(modId);
                    var maxRisk = modGroup.Max(c => c.Risk);
                    // No risk label prefix in the group name — the player just sees the mod name.
                    // Risk info lives in the individual row values and their hover hints.
                    string groupName = $"Playing alongside: {friendlyName}";

                    // MCM property IDs must be alphanumeric + underscore.
                    string safeModId = Regex.Replace(modId, @"[^a-zA-Z0-9_]", "_");

                    var conflicts = modGroup.OrderByDescending(c => (int)c.Risk).ToList();
                    int capturedOrder = modGroupOrder;

                    builder = builder?.CreateGroup(groupName, g =>
                    {
                        g.SetGroupOrder(capturedOrder);

                        for (int i = 0; i < conflicts.Count; i++)
                        {
                            var conflict = conflicts[i];
                            string propId = $"h_{safeModId}_{i}";

                            // Row label: just the player-facing gameplay area name — no [Postfix] badge.
                            string displayName = B1071_CompatibilityChecker.GetPlayerFacingMethodName(conflict.MethodSignature);

                            // Row value: plain-language status without jargon.
                            // Row hint: what happens + confidence + optional actions.
                            string inGameEffect = B1071_CompatibilityChecker.GetInGameEffectText(conflict.MethodSignature);
                            string confidenceText = B1071_CompatibilityChecker.CompatibilityConfidenceText(conflict.Risk);

                            string hintText = $"{inGameEffect}\n{confidenceText}";
                            if (conflict.McmHints.Count > 0)
                                hintText += "\n" + string.Join("\n", conflict.McmHints);

                            int capturedI = i;
                            g.AddText(propId,
                                displayName,
                                new ProxyRef<string>(
                                    () => B1071_CompatibilityChecker.PlayerRowStatusText(conflict.Risk),
                                    v => { }),
                                b =>
                                {
                                    b.SetOrder(capturedI);
                                    b.SetRequireRestart(false);
                                    b.SetHintText(hintText);
                                });
                        }
                    });

                    modGroupOrder += 10;
                }

                // ── Model replacements group (fixed 7 entries, values update per session) ──
                //
                // There are always exactly N model checks (one per vanilla model Campaign++
                // depends on). The ProxyRef getter reads live from _modelIssues each time MCM
                // renders, so values reflect the current session without rebuilding property count.
                var capturedModelNames = modelNames.ToList(); // snapshot for closure safety

                builder = builder?.CreateGroup("Core Game Systems", g =>
                {
                    g.SetGroupOrder(200);

                    for (int i = 0; i < capturedModelNames.Count; i++)
                    {
                        string modelName = capturedModelNames[i]; // captured per iteration
                        string propId = $"m_{i}";
                        int capturedI = i;

                        g.AddText(propId,
                            modelName,
                            new ProxyRef<string>(
                                () => B1071_CompatibilityChecker.GetModelStatusText(modelName),
                                v => { }),
                            b =>
                            {
                                b.SetOrder(capturedI);
                                b.SetRequireRestart(false);
                                b.SetHintText(
                                    B1071_CompatibilityChecker.GetModelHintText(modelName) +
                                    " Refreshes when a campaign is started or loaded.");
                            });
                    }
                });

                return builder?.BuildAsGlobal();
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] CompatibilityFluentSettings.Build error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // ── Button action ──────────────────────────────────────────────────────────

        private static void ShowReportAction()
        {
            try
            {
                if (TLCampaign.Current == null)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[Campaign++] Load a campaign first to see the full compatibility report.",
                        Colors.Yellow));
                    return;
                }

                string popupText = B1071_CompatibilityChecker.BuildPopupText();
                InformationManager.ShowInquiry(new InquiryData(
                    titleText: "Campaign++ - Playing Well With Others?",
                    text: popupText,
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: "OK",
                    negativeText: "Copy Report",
                    affirmativeAction: null,
                    negativeAction: () => CopyToClipboard(popupText)));
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] CompatibilityFluentSettings.ShowReportAction error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ── Clipboard helper ───────────────────────────────────────────────────────────────

        private static void CopyToClipboard(string text)
        {
            try
            {
                // Clipboard requires an STA thread; the game's main thread may not be STA.
                var thread = new System.Threading.Thread(() =>
                {
                    try { System.Windows.Forms.Clipboard.SetText(text); }
                    catch { }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join(1000); // 1 s timeout

                InformationManager.DisplayMessage(new InformationMessage(
                    "[Campaign++] Compatibility report copied to clipboard.",
                    Colors.Green));
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] CompatibilityFluentSettings.CopyToClipboard error: {ex.Message}");
            }
        }
    }
}
