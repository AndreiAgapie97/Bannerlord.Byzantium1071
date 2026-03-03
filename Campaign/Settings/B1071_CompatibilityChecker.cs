using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TLCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace Byzantium1071.Campaign.Settings
{
    /// <summary>
    /// Dynamically inspects loaded Harmony patches and game model replacements to determine
    /// compatibility between Campaign++ and other mods. No hardcoded mod list — everything
    /// is inferred at runtime from the actual patch state.
    /// </summary>
    internal static class B1071_CompatibilityChecker
    {
        // ─── Types ───────────────────────────────────────────────────────────────

        internal enum ConflictRisk { Safe, Caution, Warning }

        internal class HarmonyConflict
        {
            public string MethodSignature = string.Empty;  // e.g. "DefaultPartyWageModel.GetCharacterWage"
            public string OtherHarmonyId = string.Empty;   // e.g. "com.cavalrylogistics.overhaul"
            public string OtherPatchTypes = string.Empty;  // e.g. "Postfix", "Transpiler", "Prefix+Postfix"
            public ConflictRisk Risk;
            public string RiskReason = string.Empty;
            public List<string> McmHints = new();          // explicit player actions in Campaign++ MCM
        }

        internal class ModelIssue
        {
            public string ModelName = string.Empty;        // "Settlement Food Model"
            public string ActiveTypeName = string.Empty;   // "BLM_TownProductionModel"
            public bool IsSubclassOfExpected;              // true = patches still fire via base() call
            public bool IsDynamicallyHandled;              // true = B1071 has a runtime workaround
            public ConflictRisk Risk;
            public string Explanation = string.Empty;
        }

        private record ModelCheckEntry(
            string Name,
            Func<GameModels, object?> Getter,
            Type ExpectedType,
            bool IsDynamicallyHandled);

        // ─── State ───────────────────────────────────────────────────────────────

        private static readonly List<HarmonyConflict> _harmonyConflicts = new();
        private static readonly List<ModelIssue> _modelIssues = new();

        /// <summary>All non-framework mods that patch any gameplay method this session.</summary>
        private static readonly HashSet<string> _allGameplayOwners = new(StringComparer.Ordinal);

        /// <summary>True once RunModelChecks has completed at least once this session.</summary>
        private static bool _modelChecksRan = false;

        /// <summary>Exposed so the MCM tab can show a staleness indicator.</summary>
        internal static bool ModelChecksRan => _modelChecksRan;

        // ─── Constants ───────────────────────────────────────────────────────────

        private const string B1071HarmonyId = "com.andrei.byzantium1071";

        /// <summary>
        /// Maps patched method names to compact optional action bullets shown in MCM hover hints.
        /// Intentionally brief — fit for the MCM hint bar (~4 visible lines).
        /// Full explanations live in BuildPopupText() / the Open Report popup.
        /// </summary>
        private static readonly Dictionary<string, List<string>> MethodMcmHints = new()
        {
            ["GetCharacterWage"] = new()
            {
                "Optional: lower TroopWageMultiplier in Army Economics if combined wages feel too high."
            },
            ["GetTroopRecruitmentCost"] = new()
            {
                "Optional: lower TroopRecruitmentCostMultiplier if combined costs feel off."
            },
            ["GetTotalWage"] = new()
            {
                "Optional: lower TroopWageMultiplier in Army Economics if combined wages feel too high."
            },
            ["CalculateProsperityChange"] = new()
            {
                "Optional: disable EnableFrontierDevastation (devastation prosperity penalty).",
                "Optional: disable EnableGovernanceStrain (governance strain penalty).",
                "Optional: disable EnableSlaveEconomy (slave labor bonus).",
                "Optional: disable EnableTownInvestment (civic patronage bonus)."
            },
            ["CalculateLoyaltyChange"] = new()
            {
                "Optional: disable EnableGovernanceStrain, or lower GovernanceMaxLoyaltyPenalty."
            },
            ["CalculateSecurityChange"] = new()
            {
                "Optional: disable EnableFrontierDevastation or EnableGovernanceStrain."
            },
            ["CalculateHearthChange"] = new()
            {
                "Optional: disable EnableFrontierDevastation or EnableVillageInvestment."
            },
            ["CalculateTownFoodStocksChange"] = new()
            {
                "Optional: disable EnableFrontierDevastation or EnableSlaveEconomy.",
                "Or lower SlaveFoodConsumptionPerUnit individually."
            },
            ["CalculateDailyConstructionPower"] = new()
            {
                "Optional: disable EnableSlaveEconomy, or set SlaveConstructionAcceleration = 0."
            },
            // Castle access: struct last-write-wins — this is the one genuine interaction risk.
            ["CanMainHeroEnterSettlement"] = new()
            {
                "Note: last Postfix to run controls the outcome (not additive). " +
                "Optional: disable CastleOpenAccess to let the other mod decide castle entry."
            },
            ["CanMainHeroEnterLordsHall"] = new()
            {
                "Note: last Postfix to run controls the outcome (not additive). " +
                "Optional: disable CastleOpenAccess to let the other mod decide keep entry."
            },
            ["DetermineSupport"] = new()
            {
                "Optional: disable EnableExhaustionDiplomacyPressure, EnableForcedPeaceAtCrisis,",
                "or EnableTruceEnforcement if AI diplomacy feels erratic."
            },
            ["GetSurvivalChance"] = new()
            {
                "Optional: disable Campaign++ fatality tuning if the other mod's values are preferred."
            },
            ["GetMaximumDailyAutoRecruitmentCount"] = new()
            {
                "Optional: disable Campaign++ manpower gating if the other mod controls this."
            },
            ["GetDailyVolunteerProductionProbability"] = new()
            {
                "Optional: disable Campaign++ volunteer overrides if the other mod controls this."
            },
            ["SimulateHit"] = new()
            {
                "Optional: disable Campaign++ combat simulation tuning if values conflict."
            },
            ["ApplyInternal"] = new()
            {
                "Optional: disable Campaign++ manpower gating for recruitment if it conflicts."
            },
            ["CalculateClanIncomeInternal"] = new()
            {
                "No dedicated toggle - monitor in-game clan income tooltips for unexpected values."
            },
        };

                /// <summary>
                /// Methods where additive Postfix stacking is still order-sensitive in practice,
                /// because multiple mods mutate the same final output (ExplainedNumber/access).
                /// </summary>
                private static readonly HashSet<string> OrderSensitiveAdditiveMethods = new(StringComparer.Ordinal)
                {
                    "CalculateTownFoodStocksChange",
                    "CalculateHearthChange",
                    "CalculateProsperityChange",
                    "CalculateSecurityChange",
                    "CalculateLoyaltyChange",
                    "CanMainHeroEnterSettlement",
                    "CanMainHeroEnterLordsHall",
                };

                /// <summary>
                /// Short plain-language summary of what happens in-game when both mods are active on this method.
                /// Written for players who have no idea what a Postfix or ExplainedNumber is.
                /// </summary>
                private static readonly Dictionary<string, string> MethodInGameEffects = new(StringComparer.Ordinal)
                {
                    ["CalculateTownFoodStocksChange"]          = "Both mods change how much food your towns have each day.",
                    ["CalculateHearthChange"]                  = "Both mods affect how fast villages grow or shrink.",
                    ["CalculateProsperityChange"]              = "Both mods affect town prosperity every day.",
                    ["CalculateSecurityChange"]                = "Both mods affect town security every day.",
                    ["CalculateLoyaltyChange"]                 = "Both mods affect town loyalty every day.",
                    ["CalculateDailyConstructionPower"]        = "Both mods affect how fast buildings are constructed.",
                    ["CanMainHeroEnterSettlement"]             = "Both mods affect whether you can enter a castle. Only one mod's rule applies at a time.",
                    ["CanMainHeroEnterLordsHall"]              = "Both mods affect whether you can enter a lord's hall. Only one mod's rule applies at a time.",
                    ["GetCharacterWage"]                       = "Both mods change how much you pay each troop per day.",
                    ["GetTotalWage"]                           = "Both mods change your total army upkeep bill.",
                    ["GetTroopRecruitmentCost"]                = "Both mods change how much it costs to recruit troops.",
                    ["DetermineSupport"]                       = "Both mods influence when AI clans decide to go to war or sue for peace.",
                    ["GetDailyVolunteerProductionProbability"] = "Both mods affect how often villages produce volunteers.",
                    ["GetMaximumDailyAutoRecruitmentCount"]    = "Both mods affect how many troops can be auto-recruited per day.",
                    ["ApplyInternal"]                         = "Both mods interact with how new recruits are processed.",
                    ["GetSurvivalChance"]                     = "Both mods affect how likely troops are to survive a battle.",
                    ["SimulateHit"]                           = "Both mods affect how battle hits are calculated behind the scenes.",
                    ["CalculateClanIncomeInternal"]            = "Both mods affect how much money clans earn each day.",
                };

        /// <summary>
        /// Models that Campaign++ depends on, and whether we have a built-in dynamic workaround
        /// if another mod replaces them entirely.
        /// </summary>
        private static readonly List<ModelCheckEntry> ModelChecks = new()
        {
            new("Town Food System",
                m => m.SettlementFoodModel,
                typeof(DefaultSettlementFoodModel),
                IsDynamicallyHandled: true),   // B1071_DevastationBehavior dynamic re-patch

            new("Village Volunteers",
                m => m.VolunteerModel,
                typeof(DefaultVolunteerModel),
                IsDynamicallyHandled: true),   // converted to Harmony Postfix, not AddModel

            new("Town Prosperity System",
                m => m.SettlementProsperityModel,
                typeof(DefaultSettlementProsperityModel),
                IsDynamicallyHandled: false),

            new("Town Loyalty System",
                m => m.SettlementLoyaltyModel,
                typeof(DefaultSettlementLoyaltyModel),
                IsDynamicallyHandled: false),

            new("Town Security System",
                m => m.SettlementSecurityModel,
                typeof(DefaultSettlementSecurityModel),
                IsDynamicallyHandled: false),

            new("Building Construction",
                m => m.BuildingConstructionModel,
                typeof(DefaultBuildingConstructionModel),
                IsDynamicallyHandled: false),

            new("Clan Finances",
                m => m.ClanFinanceModel,
                typeof(DefaultClanFinanceModel),
                IsDynamicallyHandled: false),
        };

        // ─── Public aggregates (consumed by MCM properties) ──────────────────────

        internal static bool HasAnyHarmonyConflicts => _harmonyConflicts.Count > 0;

        internal static bool HasModelIssues => _modelIssues.Any(i => i.Risk > ConflictRisk.Safe);

        internal static bool HasWarningOrAbove =>
            _harmonyConflicts.Any(c => c.Risk >= ConflictRisk.Warning) ||
            _modelIssues.Any(i => i.Risk >= ConflictRisk.Warning);

        internal static int ConflictingModCount =>
            _harmonyConflicts.Select(c => c.OtherHarmonyId).Distinct().Count();

        /// <summary>
        /// One-line plain-language overview for the Summary group's top row.
        /// A player can read this and know immediately if anything needs attention.
        /// </summary>
        internal static string OverallStatusSummaryText()
        {
            // Deduplicate by friendly name so a mod with multiple Harmony IDs counts as one.
            var distinctMods = _allGameplayOwners
                .GroupBy(id => FriendlyModName(id), StringComparer.OrdinalIgnoreCase)
                .ToList();
            int modCount = distinctMods.Count;

            if (modCount == 0)
                return HasAnyHarmonyConflicts ? "Detected - check details" : "No other mods detected";

            bool hasWarning = HasWarningOrAbove;

            if (modCount == 1)
            {
                string name = distinctMods[0].Key;
                return hasWarning
                    ? $"{name} - check below"
                    : $"{name} - runs fine";
            }
            return hasWarning
                ? $"{modCount} mods - check details"
                : $"{modCount} mods - all compatible";
        }

        /// <summary>
        /// Short status for the model scan row in the Summary group.
        /// </summary>
        internal static string ModelStatusSummaryText() =>
            !_modelChecksRan ? "Start a campaign to verify"
            : HasModelIssues  ? "Issue found - see below"
            : "All normal";

        // ─── Stage 1: Harmony patch scan (runs at module screen, no campaign needed) ──

        /// <summary>
        /// Scans all active Harmony patches to find methods where both Campaign++ and another
        /// mod have patches. Called once at OnBeforeInitialModuleScreenSetAsRoot (all patches
        /// already applied at that point). Results are stable for the entire game session.
        /// </summary>
        internal static void RunHarmonyChecks()
        {
            _harmonyConflicts.Clear();
            _allGameplayOwners.Clear();

            try
            {
                foreach (var method in Harmony.GetAllPatchedMethods())
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;

                    // Collect ALL gameplay mod owners regardless of namespace so mods like RBM
                    // (which patch SandBoxCore rather than TaleWorlds.*) are never missed.
                    foreach (var p in info.Prefixes.Concat(info.Postfixes)
                                           .Concat(info.Transpilers).Concat(info.Finalizers))
                        if (p.owner != B1071HarmonyId && !IsFrameworkId(p.owner))
                            _allGameplayOwners.Add(p.owner);

                    bool b1071Patches = info.Prefixes.Any(p => p.owner == B1071HarmonyId)
                                     || info.Postfixes.Any(p => p.owner == B1071HarmonyId)
                                     || info.Transpilers.Any(p => p.owner == B1071HarmonyId)
                                     || info.Finalizers.Any(p => p.owner == B1071HarmonyId);
                    if (!b1071Patches) continue;

                    var otherOwners = info.Prefixes
                        .Concat(info.Postfixes)
                        .Concat(info.Transpilers)
                        .Concat(info.Finalizers)
                        .Where(p => p.owner != B1071HarmonyId)
                        .Select(p => p.owner)
                        .Distinct()
                        .ToList();

                    if (otherOwners.Count == 0) continue;

                    bool b1071HasBoolPrefix = info.Prefixes.Any(p =>
                        p.owner == B1071HarmonyId &&
                        p.GetMethod(method)?.ReturnType == typeof(bool));
                    bool b1071HasPostfix = info.Postfixes.Any(p => p.owner == B1071HarmonyId);

                    string methodSig = $"{method.DeclaringType?.Name ?? "(dynamic)"}.{method.Name}";

                    foreach (string otherId in otherOwners)
                    {
                        bool otherHasTranspiler = info.Transpilers.Any(p => p.owner == otherId);
                        bool otherHasBoolPrefix = info.Prefixes.Any(p =>
                            p.owner == otherId &&
                            p.GetMethod(method)?.ReturnType == typeof(bool));
                        bool otherHasPostfix = info.Postfixes.Any(p => p.owner == otherId);

                        var typeParts = new List<string>();
                        if (info.Prefixes.Any(p => p.owner == otherId)) typeParts.Add("Prefix");
                        if (info.Postfixes.Any(p => p.owner == otherId)) typeParts.Add("Postfix");
                        if (info.Transpilers.Any(p => p.owner == otherId)) typeParts.Add("Transpiler");
                        if (info.Finalizers.Any(p => p.owner == otherId)) typeParts.Add("Finalizer");
                        string patchTypes = string.Join("+", typeParts);

                        ConflictRisk risk;
                        string reason;

                        if (otherHasTranspiler)
                        {
                            risk = ConflictRisk.Warning;
                            reason = "The other mod rewrites this calculation at a deep level. Worth checking in-game that both mods' effects feel right.";
                        }
                        else if (b1071HasBoolPrefix && otherHasBoolPrefix)
                        {
                            risk = ConflictRisk.Caution;
                            reason = "Both mods have logic that can change the outcome here. Everything likely works, but it's worth playing a few sessions to see if anything feels off.";
                        }
                        else if (b1071HasPostfix && otherHasPostfix && IsOrderSensitiveAdditiveMethod(method))
                        {
                            risk = ConflictRisk.Caution;
                            reason = "Both mods add their own effect to the same daily calculation. The combined result is usually fine - you might just notice slightly stronger numbers than with either mod alone.";
                        }
                        else
                        {
                            risk = ConflictRisk.Safe;
                            reason = "Both mods add their own contribution here independently. No conflict.";
                        }

                        MethodMcmHints.TryGetValue(method.Name, out var hints);

                        _harmonyConflicts.Add(new HarmonyConflict
                        {
                            MethodSignature = methodSig,
                            OtherHarmonyId = otherId,
                            OtherPatchTypes = patchTypes,
                            Risk = risk,
                            RiskReason = reason,
                            McmHints = hints ?? new List<string>(),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] CompatibilityChecker.RunHarmonyChecks error: {ex.GetType().Name}: {ex.Message}");
            }

        }

        // ─── Stage 2: Model replacement scan (runs at campaign session start) ────

        /// <summary>
        /// Checks each vanilla model that Campaign++ patches against the actually-loaded model.
        /// If another mod replaced a model entirely (different type hierarchy), our static Harmony
        /// patches on the vanilla type will not fire. Called per session from B1071_CompatibilityBehavior.
        /// </summary>
        internal static void RunModelChecks()
        {
            _modelIssues.Clear();

            try
            {
                var models = TLCampaign.Current?.Models;
                if (models == null) return;

                foreach (var check in ModelChecks)
                {
                    object? activeModel;
                    try { activeModel = check.Getter(models); }
                    catch { continue; }

                    if (activeModel == null) continue;

                    var activeType = activeModel.GetType();

                    // If the active model IS the expected type (or a subclass), our patches fire normally
                    if (check.ExpectedType.IsAssignableFrom(activeType)) continue;


                    // Non-vanilla model detected
                    bool isSubclass = activeType.IsSubclassOf(check.ExpectedType);
                    ConflictRisk risk;
                    string explanation;

                    if (check.IsDynamicallyHandled)
                    {
                        risk = ConflictRisk.Safe;
                        explanation = $"Replaced by '{activeType.Name}'. " +
                                      "Campaign++ dynamic patching is ACTIVE - handled at runtime. No action needed.";
                    }
                    else if (isSubclass)
                    {
                        risk = ConflictRisk.Caution;
                        explanation = $"Replaced by '{activeType.Name}' (subclass). " +
                                      "Patches likely still fire via base() call, but verify in-game behavior.";
                    }
                    else
                    {
                        risk = ConflictRisk.Warning;
                        explanation = $"Replaced by '{activeType.Name}' (different hierarchy). " +
                                      $"Campaign++ static patches on {check.ExpectedType.Name} will NOT fire. " +
                                      "Features in this area may be silently disabled.";
                    }

                    _modelIssues.Add(new ModelIssue
                    {
                        ModelName = check.Name,
                        ActiveTypeName = activeType.Name,
                        IsSubclassOfExpected = isSubclass,
                        IsDynamicallyHandled = check.IsDynamicallyHandled,
                        Risk = risk,
                        Explanation = explanation,
                    });
                }
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[Byzantium1071] CompatibilityChecker.RunModelChecks error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _modelChecksRan = true;
            }
        }

        // ─── Popup text builder ───────────────────────────────────────────────────

        internal static string BuildPopupText()
        {
            var sb = new StringBuilder();

            // ── Overall verdict ──
            // Deduplicate Harmony IDs by friendly name so a mod with multiple IDs shows once.
            var modGroups = _allGameplayOwners
                .GroupBy(id => FriendlyModName(id), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key)
                .ToList();

            if (modGroups.Count == 0)
            {
                sb.AppendLine("No other gameplay mods detected. Campaign++ is running on its own.");
            }
            else
            {
                sb.AppendLine(HasWarningOrAbove
                    ? "One or more areas worth checking - see details below."
                    : "All mods running smoothly alongside Campaign++.");
            }
            sb.AppendLine();

            // ── One line per mod ──
            if (modGroups.Count > 0)
            {
                sb.AppendLine("Your mods:");
                foreach (var group in modGroups)
                {
                    string name   = group.Key;
                    string status = GetModPopupStatus(group.ToList());
                    sb.AppendLine($"  {name} - {status}");
                }
                sb.AppendLine();
            }

            // ── Core systems (only shown when something is wrong) ──
            var systemIssues = _modelIssues.Where(i => i.Risk > ConflictRisk.Safe).ToList();
            if (systemIssues.Count > 0)
            {
                sb.AppendLine("Game system replaced by another mod:");
                foreach (var issue in systemIssues)
                    sb.AppendLine($"  {GetModelHintText(issue.ModelName)}");
                sb.AppendLine();
            }

            // ── Footer ──
            sb.Append("Open \"Campaign++ Compatibility\" in Mod Options to adjust settings or read area details.");
            return sb.ToString();
        }

        /// <summary>
        /// Resets per-session model check state. Called before each new session's RunModelChecks.
        /// Harmony conflicts are stable per game launch and do not need resetting.
        /// </summary>
        internal static void ResetModelChecks()
        {
            _modelIssues.Clear();
            _modelChecksRan = false;
        }

        // ─── Accessors for FluentSettings builder ─────────────────────────────────

        /// <summary>
        /// Snapshot of the current harmony conflict list for fluent MCM settings construction.
        /// Stable per game launch (Harmony patches don't change after module load).
        /// </summary>
        internal static IReadOnlyList<HarmonyConflict> GetHarmonyConflicts() =>
            _harmonyConflicts.AsReadOnly();

        /// <summary>
        /// Names of all model checks in declaration order — used to create a fixed set of MCM
        /// text properties (one per model) whose values update dynamically per campaign session.
        /// </summary>
        internal static IReadOnlyList<string> GetModelCheckNames() =>
            ModelChecks.Select(c => c.Name).ToList().AsReadOnly();

        /// <summary>
        /// Returns a human-readable status string for a given model check.
        /// Returns a "not yet scanned" placeholder before any campaign session has started.
        /// </summary>
        internal static string GetModelStatusText(string modelName)
        {
            var issue = _modelIssues.FirstOrDefault(i => i.ModelName == modelName);
            if (issue == null)
                return _modelChecksRan ? "OK" : "Start a campaign to check";

            if (issue.IsDynamicallyHandled) return "Replaced - auto-patched";
            if (issue.IsSubclassOfExpected) return "[CHECK] May need attention";
            return "[ISSUE] Features may be off";
        }

        /// <summary>
        /// Returns the full explanation for a model check for use in hover hint text.
        /// </summary>
        internal static string GetModelHintText(string modelName)
        {
            var issue = _modelIssues.FirstOrDefault(i => i.ModelName == modelName);
            if (issue == null)
                return _modelChecksRan
                    ? $"{modelName}: No other mod has replaced this system. Campaign++ features here are working normally."
                    : $"{modelName}: Load a campaign and the game will check this automatically.";

            if (issue.IsDynamicallyHandled)
                return $"{modelName}: Another mod has replaced this system, but Campaign++ detected this and compensated automatically. No action needed.";

            if (issue.IsSubclassOfExpected)
                return $"{modelName}: Another mod has modified this system (as an extension of the original). Campaign++ features here should still work, but check in-game if something feels off.";

            return $"{modelName}: Another mod has fully replaced this system. Some Campaign++ features that affect {modelName.ToLower()} may not be active. Check in-game if something feels off.";
        }

        /// <summary>
        /// Short risk label for use in MCM group names and text property display.
        /// </summary>
        internal static string RiskLabel(ConflictRisk risk) => risk switch
        {
            ConflictRisk.Warning => "REVIEW",
            ConflictRisk.Caution => "STACKING",
            _                    => "COMPATIBLE",
        };

        /// <summary>
        /// Plain-language confidence phrase for use in player-facing MCM row hints.
        /// Replaces the raw percentage with something a non-technical player can understand.
        /// </summary>
        internal static string CompatibilityConfidenceText(ConflictRisk risk) => risk switch
        {
            ConflictRisk.Warning => "Worth checking in-game.",
            ConflictRisk.Caution => "Very likely fine.",
            _                    => "No known issues.",
        };

        /// <summary>
        /// Short row-value text shown in the MCM text box next to each conflict row.
        /// Plain language — no brackets, no technical labels.
        /// </summary>
        internal static string PlayerRowStatusText(ConflictRisk risk) => risk switch
        {
            ConflictRisk.Warning => "Worth a look - hover to read",
            ConflictRisk.Caution => "Both active - hover to read",
            _                    => "Works fine together",
        };

        internal static string GetInGameEffectText(string methodSignature)
        {
            string methodName = methodSignature?.Split('.').LastOrDefault() ?? string.Empty;
            if (methodName.Length > 0 && MethodInGameEffects.TryGetValue(methodName, out var text))
                return text;

            return "Multiple mods alter the same gameplay calculation; final behavior may differ from either mod's standalone intent.";
        }

        private static bool IsOrderSensitiveAdditiveMethod(MethodBase method)
        {
            if (OrderSensitiveAdditiveMethods.Contains(method.Name))
                return true;

            var returnType = (method as MethodInfo)?.ReturnType;
            if (returnType != null && returnType.Name == "ExplainedNumber")
                return true;

            return false;
        }

        // ─── Display helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Known-good friendly names for common mod Harmony IDs.
        /// Supplements the general stripping logic for well-known mods.
        /// </summary>
        private static readonly Dictionary<string, string> KnownModNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["com.mfive.aiinfluence"]           = "AI Influence",
            ["com.mfive.ai_influence"]           = "AI Influence",
            ["bannerlord.economy.overhaul"]      = "Economy Overhaul",
            ["com.cavalrylogistics.overhaul"]    = "Cavalry Logistics",
            ["com.gitsmith.realequipment"]       = "Real Equipment",
            ["harmony"]                          = "Harmony",
        };

        /// <summary>
        /// Converts a raw Harmony owner ID into a readable mod name.
        /// Order: 1) exact known-good table  2) strip common prefixes and title-case remainder.
        /// </summary>
        internal static string FriendlyModName(string harmonyId)
        {
            if (string.IsNullOrEmpty(harmonyId)) return harmonyId;
            if (KnownModNames.TryGetValue(harmonyId, out var known)) return known;

            // Strip leading "com.", "net.", "org." then strip author segment (first remaining dot-part)
            // e.g. "com.mfive.aiinfluence"   → strip "com."   → "mfive.aiinfluence"
            //      strip first segment        → "aiinfluence"
            //      title-case                 → "Aiinfluence"  (acceptable fallback)
            string s = harmonyId;
            foreach (var prefix in new[] { "com.", "net.", "org.", "io." })
            {
                if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                { s = s.Substring(prefix.Length); break; }
            }

            // Remove an author segment if there is at least one more dot
            int firstDot = s.IndexOf('.');
            if (firstDot > 0 && firstDot < s.Length - 1)
                s = s.Substring(firstDot + 1);

            // Replace dots/underscores/hyphens with spaces, title-case each word
            var words = s.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w =>
                w.Length == 0 ? w : char.ToUpper(w[0]) + w.Substring(1)));
        }

        /// <summary>
        /// Maps internal method names to short, readable player-facing labels
        /// for use in MCM row display names.
        /// </summary>
        private static readonly Dictionary<string, string> PlayerFacingNames = new(StringComparer.Ordinal)
        {
            ["CalculateTownFoodStocksChange"]        = "Town Food Supply",
            ["CalculateHearthChange"]                = "Village Hearth Growth",
            ["CalculateProsperityChange"]            = "Town Prosperity",
            ["CalculateSecurityChange"]              = "Town Security",
            ["CalculateLoyaltyChange"]               = "Town Loyalty",
            ["CalculateDailyConstructionPower"]      = "Building Construction Speed",
            ["CanMainHeroEnterSettlement"]           = "Castle Entry Access",
            ["CanMainHeroEnterLordsHall"]            = "Lord's Hall Entry",
            ["GetCharacterWage"]                     = "Individual Troop Wages",
            ["GetTotalWage"]                         = "Total Party Wages",
            ["GetTroopRecruitmentCost"]              = "Troop Recruitment Cost",
            ["DetermineSupport"]                     = "AI War/Peace Voting",
            ["GetDailyVolunteerProductionProbability"] = "Volunteer Spawn Rate",
            ["GetMaximumDailyAutoRecruitmentCount"]  = "Daily Auto-Recruitment Cap",
            ["ApplyInternal"]                        = "Recruitment Application",
            ["GetSurvivalChance"]                    = "Troop Survival Chance",
            ["SimulateHit"]                          = "Battle Hit Simulation",
            ["CalculateClanIncomeInternal"]          = "Clan Daily Income",
        };

        /// <summary>
        /// Returns a short player-facing label for a method signature.
        /// Falls back to the raw short method name if not in the table.
        /// </summary>
        internal static string GetPlayerFacingMethodName(string methodSignature)
        {
            string methodName = methodSignature?.Split('.').LastOrDefault() ?? string.Empty;
            if (methodName.Length > 0 && PlayerFacingNames.TryGetValue(methodName, out var label))
                return label;
            return methodName; // fallback: raw method name
        }

        // ─── Gameplay mod detection helpers ───────────────────────────────────────────────

        private static bool IsGameplayNamespace(string ns) =>
            ns.StartsWith("TaleWorlds.CampaignSystem", StringComparison.Ordinal) ||
            ns.StartsWith("TaleWorlds.MountAndBlade",  StringComparison.Ordinal) ||
            ns.StartsWith("TaleWorlds.Core",           StringComparison.Ordinal);

        /// <summary>
        /// Returns true if the Harmony owner ID belongs to an infrastructure mod
        /// (game core, patch framework, MCM, UI extension) rather than a gameplay mod.
        /// Heuristic: pure frameworks never patch TaleWorlds.CampaignSystem gameplay code.
        /// </summary>
        private static bool IsFrameworkId(string harmonyId)
        {
            if (string.IsNullOrEmpty(harmonyId)) return true;
            var lc = harmonyId.ToLowerInvariant();
            return lc.Contains("taleworlds")     || lc.Contains("butterlib")        ||
                   lc.Contains("butlib")         || lc.Contains(".mcm")             ||
                   lc.Contains("modlib")         || lc.Contains("uiextender")       ||
                   lc.Contains("mboptionscreen") || lc.Contains("betterexception")  ||
                   lc.Contains("debugmode")      || lc.Contains("nativemodule")     ||
                   lc.Contains("unpatch")        ||
                   lc == "0harmony"              || lc.StartsWith("0harmony.");
        }

        /// <summary>All non-framework mods detected patching gameplay methods this session.</summary>
        internal static IReadOnlyList<string> GetAllGameplayModOwners() =>
            _allGameplayOwners.OrderBy(id => FriendlyModName(id)).ToList().AsReadOnly();

        /// <summary>
        /// One-line popup status for a mod: "Compatible" or a brief plain-language description
        /// of the overlap area(s), pointing to MCM for details.
        /// </summary>
        /// <summary>
        /// Accepts all Harmony IDs belonging to one logical mod (same FriendlyModName group)
        /// so that mods with multiple Harmony IDs are evaluated together.
        /// </summary>
        private static string GetModPopupStatus(List<string> harmonyIds)
        {
            var conflicts = _harmonyConflicts.Where(c => harmonyIds.Contains(c.OtherHarmonyId)).ToList();
            if (conflicts.Count == 0 || conflicts.Max(c => c.Risk) == ConflictRisk.Safe)
                return "Compatible";

            var notable = conflicts
                .Where(c => c.Risk > ConflictRisk.Safe)
                .OrderByDescending(c => (int)c.Risk)
                .ToList();
            var areas = notable
                .Select(c => GetPlayerFacingMethodName(c.MethodSignature))
                .Distinct().ToList();
            bool hasWarning = notable.Any(c => c.Risk == ConflictRisk.Warning);
            string prefix     = hasWarning ? "Worth checking" : "Minor overlap";
            string confidence = hasWarning ? "" : " - very likely fine";

            return areas.Count == 1
                ? $"{prefix}: {areas[0]}{confidence}"
                : $"{prefix} in {areas.Count} areas{confidence}";
        }
    }
}
