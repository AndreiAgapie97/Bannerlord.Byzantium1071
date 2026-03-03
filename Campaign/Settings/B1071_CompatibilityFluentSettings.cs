using MCM.Abstractions.Base.Global;
using MCM.Abstractions.FluentBuilder;
using MCM.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
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
                    .Create("b1071_compat", L("b1071_compat_tab_title", "Campaign++ Compatibility"))
                    ?.SetFolderName("Byzantium1071")
                    ?.SetFormat("json"); // persists the suppress toggle

                if (builder == null) return null;

                // ── Summary group ──────────────────────────────────────────────────
                // NOTE: SetHintText is NOT on ISettingsPropertyBuilder base — it lives on each
                // specific sub-interface. Always call it directly on the builder param (b), never
                // on a chained result that has been widened to ISettingsPropertyBuilder.
                builder = builder.CreateGroup(L("b1071_compat_group_summary", "Summary"), g =>
                {
                    g.SetGroupOrder(0);

                    // Report freshness indicator — changes once a campaign is loaded.
                    g.AddText("report_status",
                        L("b1071_compat_label_report_status", "Report status"),
                        new ProxyRef<string>(
                            () => B1071_CompatibilityChecker.ModelChecksRan
                                ? L("b1071_compat_status_up_to_date", "Up to date")
                                : L("b1071_compat_status_old", "Partial — load any campaign"),
                            v => { }),
                        b =>
                        {
                            b.SetOrder(-1);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                L("b1071_compat_hint_report_status",
                                  "Shows whether this report reflects your current session. 'Partial' means the mod list scan is complete but Core Game Systems have not been checked yet — load any campaign and the report updates automatically."));
                        });

                    // Suppress popup toggle — the only user-editable property in this tab.
                    g.AddBool("suppress_popup",
                        L("b1071_compat_label_suppress_popup", "Don't show this popup at startup"),
                        new ProxyRef<bool>(() => _suppressCompatPopup, v => _suppressCompatPopup = v),
                        b =>
                        {
                            b.SetOrder(0);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                L("b1071_compat_hint_suppress_popup",
                                  "Turn this on if you don't want the compatibility report popping up every time you start a campaign. You can always read the report by clicking the button below."));
                        });

                    g.AddText("overlap_summary",
                        L("b1071_compat_label_overlap_summary", "Running alongside"),
                        new ProxyRef<string>(
                            () => B1071_CompatibilityChecker.OverallStatusSummaryText(),
                            v => { }),
                        b =>
                        {
                            b.SetOrder(1);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                L("b1071_compat_hint_overlap_summary",
                                  "This tells you at a glance whether Campaign++ is running smoothly alongside your other mods. \"Runs fine\" means both mods are active and their effects add up — no crashes, no missing features. \"Check below\" means one interaction is worth reviewing (hover those rows for plain-language details)."));
                        });

                    g.AddText("model_summary",
                        L("b1071_compat_label_model_summary", "Core game systems"),
                        new ProxyRef<string>(
                            () => B1071_CompatibilityChecker.ModelStatusSummaryText(),
                            v => { }),
                        b =>
                        {
                            b.SetOrder(2);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                L("b1071_compat_hint_model_summary",
                                  "Some mods replace the game's internal calculation engines entirely. This checks whether the systems Campaign++ relies on are intact. \"All normal\" means everything is working. If an issue is found, hover the rows in the section below."));
                        });

                    g.AddText("quick_guide",
                        L("b1071_compat_label_tip", "Tip"),
                        new ProxyRef<string>(
                            () => L("b1071_compat_tip_value", "Load a campaign - full report pops-up"),
                            v => { }),
                        b =>
                        {
                            b.SetOrder(3);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                                                L("b1071_compat_hint_tip",
                                                                    "When you load a campaign, a compatibility popup appears automatically. It shows the mod list (already visible above) plus Core Game Systems — whether any other mod has replaced the internal calculation engines Campaign++ relies on. That Core Systems section only updates after a campaign is active, which is why it shows 'Start a campaign to check' until then. You don't need to come back here — the popup tells you everything on campaign load."));
                        });

                    // Button to re-open the popup on demand.
                    g.AddButton("show_report",
                        L("b1071_compat_button_open_full", "Open Full Report"),
                        new ProxyRef<Action>(() => _showReportDelegate, v => { }),
                        L("b1071_compat_button_open", "Open Report"),
                        b =>
                        {
                            b.SetOrder(4);
                            b.SetRequireRestart(false);
                            b.SetHintText(
                                L("b1071_compat_hint_open_report",
                                  "Reopens the same popup that appears automatically when you load a campaign. If 'Core game systems' above still reads 'Start a campaign to check', load a campaign first — the popup will appear on its own and the Core Systems section will fill in."));
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
                    string groupName = new TextObject("{=b1071_compat_group_mod}Playing alongside: {MOD}")
                        .SetTextVariable("MOD", friendlyName).ToString();

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
                            // Row hint: what happens in-game + specific interaction explanation + optional actions.
                            string inGameEffect = B1071_CompatibilityChecker.GetInGameEffectText(conflict.MethodSignature);

                            string hintText = $"{inGameEffect}\n{conflict.RiskReason}";
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

                builder = builder?.CreateGroup(L("b1071_compat_group_core_systems", "Core Game Systems"), g =>
                {
                    g.SetGroupOrder(200);

                    // Summary row — at-a-glance answer before the player reads all detail rows.
                    g.AddText("model_group_status",
                        L("b1071_compat_label_model_group_status", "Status"),
                        new ProxyRef<string>(
                            () => !B1071_CompatibilityChecker.ModelChecksRan
                                ? L("b1071_compat_model_group_pending", "Load a campaign to verify")
                                : B1071_CompatibilityChecker.HasModelIssues
                                  ? L("b1071_compat_model_group_issues", "Issue detected — see below")
                                  : L("b1071_compat_model_group_ok", "All systems normal"),
                            v => { }),
                        b =>
                        {
                            b.SetOrder(-1);
                            b.SetRequireRestart(false);
                            b.SetHintText(L("b1071_compat_hint_model_group_status",
                                "Quick summary of the Core Game Systems scan. 'All systems normal' means no other mod has replaced anything Campaign++ relies on. If an issue is found, hover the individual rows below for details."));
                        });

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
                                    L("b1071_compat_hint_refresh_suffix", " Refreshes when a campaign is started or loaded."));
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
                        new TextObject("{=b1071_compat_no_campaign}[Campaign++] Load a campaign first to see the full compatibility report.").ToString(),
                        Colors.Yellow));
                    return;
                }

                string popupText = B1071_CompatibilityChecker.BuildPopupText();
                InformationManager.ShowInquiry(new InquiryData(
                    titleText: new TextObject("{=b1071_compat_title}Campaign++ - Playing Well With Others?").ToString(),
                    text: popupText,
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: new TextObject("{=b1071_compat_ok}OK").ToString(),
                    negativeText: new TextObject("{=b1071_compat_copy_report}Copy Report").ToString(),
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

        internal static void CopyToClipboard(string text)
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
                    new TextObject("{=b1071_compat_copied}[Campaign++] Compatibility report copied to clipboard.").ToString(),
                    Colors.Green));
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] CompatibilityFluentSettings.CopyToClipboard error: {ex.Message}");
            }
        }

        private static string L(string key, string fallback) =>
            new TextObject($"{{={key}}}{fallback}").ToString();
    }
}
