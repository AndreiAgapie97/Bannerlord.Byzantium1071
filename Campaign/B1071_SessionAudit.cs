using Byzantium1071.Campaign.Settings;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign
{
    internal static class B1071_SessionAudit
    {
        private static int _harmonyApplied;
        private static int _harmonyFailed;

        private static int _manpowerConsumeOps;
        private static int _manpowerRegenOps;
        private static int _manpowerCastleSupplyOps;
        private static int _manpowerBlockedTroops;

        private static int _slavePriceMin = int.MaxValue;
        private static int _slavePriceMax = int.MinValue;
        private static int _slavePriceSnapshots;
        private static int _slaveDailyBonusEvents;

        private static int _compatFoodPatchesApplied;

        // Observed spread of the tariff bases the revenue taper is calibrated against. Towns and
        // villages are tracked apart because their pools differ by roughly a factor of five.
        //
        // The min/max pair alone cannot calibrate the reference constants, because it says nothing
        // about where the bulk of the settlements actually sit: a range of 94-13035 is consistent
        // with a median of 500 and with a median of 9000. The octave counts below are what carries
        // that, and they are what the MCM "starts at" thresholds are read against.
        private static int _townTariffBasisMin = int.MaxValue;
        private static int _townTariffBasisMax = int.MinValue;
        private static int _townTariffBasisSamples;
        private static int _villageTariffBasisMin = int.MaxValue;
        private static int _villageTariffBasisMax = int.MinValue;
        private static int _villageTariffBasisSamples;

        // Counts of samples per power of two: index 0 is 0-1, index k is [2^k, 2^(k+1)), and the
        // last index is everything at or above 2^(OctaveBucketCount - 1). Bounds the memory at a
        // fixed handful of counters however long the session runs, which is why the distribution is
        // kept as octaves rather than as a sorted list of every sample.
        private const int OctaveBucketCount = 17;
        private static readonly int[] _townTariffBasisBuckets = new int[OctaveBucketCount];
        private static readonly int[] _villageTariffBasisBuckets = new int[OctaveBucketCount];

        private static int _softFails;
        private static readonly List<string> _softFailSamples = new();

        // Failures swallowed by the settlement-revenue postfixes' fail-open catch blocks. Counted
        // always -- the verbose log they used to go to is gated behind a Developer Tools toggle,
        // so without this counter a per-tick throw loop would be invisible in a normal session.
        private static int _revenueTaperFailures;

        // The campaign day the last failure line was written on, so a per-tick loop surfaces as
        // one line per day instead of one line per settlement.
        private static int _revenueTaperFailDay = -1;

        internal static void ResetForNewSession()
        {
            _manpowerConsumeOps = 0;
            _manpowerRegenOps = 0;
            _manpowerCastleSupplyOps = 0;
            _manpowerBlockedTroops = 0;
            _slavePriceMin = int.MaxValue;
            _slavePriceMax = int.MinValue;
            _slavePriceSnapshots = 0;
            _slaveDailyBonusEvents = 0;
            _compatFoodPatchesApplied = 0;
            _townTariffBasisMin = int.MaxValue;
            _townTariffBasisMax = int.MinValue;
            _townTariffBasisSamples = 0;
            Array.Clear(_townTariffBasisBuckets, 0, _townTariffBasisBuckets.Length);
            _villageTariffBasisMin = int.MaxValue;
            _villageTariffBasisMax = int.MinValue;
            _villageTariffBasisSamples = 0;
            Array.Clear(_villageTariffBasisBuckets, 0, _villageTariffBasisBuckets.Length);
            _softFails = 0;
            _softFailSamples.Clear();
            _revenueTaperFailures = 0;
            _revenueTaperFailDay = -1;
        }

        internal static void SetHarmonyPatchResults(int applied, int failed)
        {
            _harmonyApplied = Math.Max(0, applied);
            _harmonyFailed = Math.Max(0, failed);
            if (failed > 0)
                RecordSoftFail("Harmony", $"patch_failed={failed}");
        }

        internal static void RecordManpowerConsume(int blockedTroops)
        {
            _manpowerConsumeOps++;
            if (blockedTroops > 0)
                _manpowerBlockedTroops += blockedTroops;
        }

        internal static void RecordManpowerRegen()
        {
            _manpowerRegenOps++;
        }

        internal static void RecordManpowerCastleSupply()
        {
            _manpowerCastleSupplyOps++;
        }

        internal static void RecordSlavePriceSnapshot(int price)
        {
            if (price < 0) return;
            _slavePriceSnapshots++;
            if (price < _slavePriceMin) _slavePriceMin = price;
            if (price > _slavePriceMax) _slavePriceMax = price;
        }

        internal static void RecordSlaveDailyBonus()
        {
            _slaveDailyBonusEvents++;
        }

        internal static void RecordCompatFoodPatches(int applied)
        {
            if (applied <= 0) return;
            _compatFoodPatchesApplied += applied;
        }

        /// <summary>
        /// One settlement's daily tariff basis, which is its trade pool spread back over
        /// RevenueSmoothenFraction days. This is the quantity B1071_RevenueMath tapers against, so
        /// what is reported at end of session is what tells you whether the reference constant and
        /// the "starts at" threshold are calibrated for the campaign actually being played.
        ///
        /// An empty pool is skipped rather than recorded as a zero. A settlement with nothing in it
        /// is not evidence about the reference size, and folding the game's many zeros into the
        /// minimum would bury the distribution the calibration actually needs to see.
        /// </summary>
        internal static void RecordTownTariffBasis(int basis)
        {
            if (basis <= 0) return;
            _townTariffBasisSamples++;
            if (basis < _townTariffBasisMin) _townTariffBasisMin = basis;
            if (basis > _townTariffBasisMax) _townTariffBasisMax = basis;
            _townTariffBasisBuckets[OctaveBucket(basis)]++;
        }

        internal static void RecordVillageTariffBasis(int basis)
        {
            if (basis <= 0) return;
            _villageTariffBasisSamples++;
            if (basis < _villageTariffBasisMin) _villageTariffBasisMin = basis;
            if (basis > _villageTariffBasisMax) _villageTariffBasisMax = basis;
            _villageTariffBasisBuckets[OctaveBucket(basis)]++;
        }

        /// <summary>
        /// Which power-of-two bucket a basis falls in, saturating at the top index so an absurd
        /// value cannot index past the end of the array.
        /// </summary>
        private static int OctaveBucket(int basis)
        {
            int bucket = 0;
            while (bucket < OctaveBucketCount - 1 && basis >= 1 << (bucket + 1))
            {
                bucket++;
            }

            return bucket;
        }

        internal static void RecordSoftFail(string subsystem, string detail)
        {
            _softFails++;
            if (_softFailSamples.Count >= 3) return;
            _softFailSamples.Add($"{subsystem}:{detail}");
        }

        /// <summary>
        /// A settlement-revenue postfix caught an exception and left the vanilla value in place.
        /// Always recorded -- both into the soft-fail counter, which reaches the end-of-session
        /// summary however the log toggles are set, and as an ungated once-per-game-day line in
        /// the session file, so a per-tick throw loop announces itself on the day it starts
        /// instead of surfacing only in a verbose session.
        ///
        /// This must never throw into the daily tick: everything below is wrapped. If the campaign
        /// clock cannot be read, per-day deduplication is abandoned and the failure line is
        /// written anyway -- silence is the one wrong answer here.
        /// </summary>
        internal static void RecordRevenueTaperFailure(string where, Exception ex)
        {
            try
            {
                _revenueTaperFailures++;
                RecordSoftFail("RevenueTaper", $"{where}: {ex.GetType().Name}: {ex.Message}");

                bool dayKnown = false;
                int day = -1;
                try
                {
                    day = (int)CampaignTime.Now.ToDays;
                    dayKnown = true;
                }
                catch
                {
                    // No clock: log without deduplication rather than stay silent.
                }

                if (!dayKnown || day != _revenueTaperFailDay)
                {
                    _revenueTaperFailDay = day;
                    B1071_SessionFileLog.WriteTagged(
                        "Revenue",
                        $"taper error (first today, total {_revenueTaperFailures}) {where}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            catch
            {
                // Failure accounting must never take the daily tick down with it.
            }
        }

        internal static void EmitStartupCompatibilitySnapshot()
        {
            try
            {
                var owners = B1071_CompatibilityChecker.GetAllGameplayModOwners();
                var friendlyNames = owners
                    .Select(B1071_CompatibilityChecker.FriendlyModName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var modules = BuildModuleVersionSnapshot(friendlyNames);

                var conflicts = B1071_CompatibilityChecker.GetHarmonyConflicts();
                int safe = conflicts.Count(c => c.Risk == B1071_CompatibilityChecker.ConflictRisk.Safe);
                int caution = conflicts.Count(c => c.Risk == B1071_CompatibilityChecker.ConflictRisk.Caution);
                int warning = conflicts.Count(c => c.Risk == B1071_CompatibilityChecker.ConflictRisk.Warning);

                var modelOverrides = B1071_CompatibilityChecker
                    .GetModelIssuesSnapshot()
                    .Select(i =>
                    {
                        string mode = i.IsDynamicallyHandled ? "auto" : "raw";
                        return $"{i.ModelName}->{i.ActiveTypeName}({mode})";
                    })
                    .ToList();

                string moduleSegment = modules.Count == 0
                    ? "none"
                    : string.Join("; ", modules);

                string modelSegment = modelOverrides.Count == 0
                    ? "none"
                    : string.Join("; ", modelOverrides);

                int totalHarmony = _harmonyApplied + _harmonyFailed;
                string patchCounts = totalHarmony > 0
                    ? $"{_harmonyApplied}/{totalHarmony}"
                    : "n/a";

                Debug.Print(
                    "[Byzantium1071][Compat] Snapshot: " +
                    $"modules={moduleSegment} | " +
                    $"modelOverrides={modelSegment} | " +
                    $"conflicts(safe/caution/warning)={safe}/{caution}/{warning} | " +
                    $"patches={patchCounts}");
                B1071_SessionFileLog.WriteTagged(
                    "Compat",
                    "Snapshot: " +
                    $"modules={moduleSegment} | " +
                    $"modelOverrides={modelSegment} | " +
                    $"conflicts(safe/caution/warning)={safe}/{caution}/{warning} | " +
                    $"patches={patchCounts}");

                // The settlement-revenue group as this session will actually run it. Nothing else
                // in the mod echoes these values, so after an incident the log could not answer
                // whether the taper was even on -- this line is that answer, written every
                // session regardless of the log toggles.
                var revenueSettings = B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;
                string revenueEcho =
                    $"toggle={revenueSettings.EnableSettlementRevenueTuning} " +
                    $"town(strength/curve/knee)=" +
                    $"{revenueSettings.SettlementTariffStrengthTown}/{revenueSettings.SettlementTariffCurveTown:0.##}/{revenueSettings.SettlementTariffKneeTown} " +
                    $"village=" +
                    $"{revenueSettings.SettlementTariffStrengthVillage}/{revenueSettings.SettlementTariffCurveVillage:0.##}/{revenueSettings.SettlementTariffKneeVillage} " +
                    $"tax=" +
                    $"{revenueSettings.SettlementTaxStrength}/{revenueSettings.SettlementTaxCurve:0.##}/{revenueSettings.SettlementTaxKnee} " +
                    $"verboseLog={B1071_VerboseLog.Enabled}";
                Debug.Print("[Byzantium1071][Revenue] Settings echo: " + revenueEcho);
                B1071_SessionFileLog.WriteTagged("Revenue", "Settings echo: " + revenueEcho);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Compat] Snapshot failed: {ex.GetType().Name}: {ex.Message}");
                RecordSoftFail("Compat", "snapshot_failed");
            }
        }

        internal static void EmitEndOfSessionSummary()
        {
            try
            {
                int manpowerOps = _manpowerConsumeOps + _manpowerRegenOps + _manpowerCastleSupplyOps;

                string slaveRange = _slavePriceSnapshots > 0
                    ? $"{_slavePriceMin}-{_slavePriceMax}"
                    : "n/a";

                int totalHarmony = _harmonyApplied + _harmonyFailed;
                string harmony = totalHarmony > 0
                    ? $"{_harmonyApplied}/{totalHarmony}"
                    : "n/a";

                string softFailDetails = _softFailSamples.Count > 0
                    ? string.Join(";", _softFailSamples)
                    : "none";

                string tariffBasis =
                    $"town={Range(_townTariffBasisSamples, _townTariffBasisMin, _townTariffBasisMax)} " +
                    $"village={Range(_villageTariffBasisSamples, _villageTariffBasisMin, _villageTariffBasisMax)} " +
                    $"refs={B1071_RevenueMath.TownTariffReference:0}/{B1071_RevenueMath.VillageTariffReference:0}";

                // Which settlements fall in which band is what the "starts at" thresholds are
                // chosen from, so the two distributions are emitted on their own lines rather than
                // crammed into the summary, which is already long.
                string townSpread = Histogram(_townTariffBasisBuckets);
                string villageSpread = Histogram(_villageTariffBasisBuckets);

                Debug.Print(
                    "[Byzantium1071][Session] Summary: " +
                    $"manpowerOps={manpowerOps} " +
                    $"(consume={_manpowerConsumeOps},regen={_manpowerRegenOps},castleSupply={_manpowerCastleSupplyOps},blocked={_manpowerBlockedTroops}) | " +
                    $"slavePriceRange={slaveRange} (snapshots={_slavePriceSnapshots},dailyBonus={_slaveDailyBonusEvents}) | " +
                    $"tariffBasis({tariffBasis}) | " +
                    $"compatFoodPatches={_compatFoodPatchesApplied} | " +
                    $"harmony={harmony} | " +
                    $"revenueTaperErrors={_revenueTaperFailures} | " +
                    $"softFails={_softFails} [{softFailDetails}]");
                B1071_SessionFileLog.WriteTagged(
                    "Session",
                    "Summary: " +
                    $"manpowerOps={manpowerOps} " +
                    $"(consume={_manpowerConsumeOps},regen={_manpowerRegenOps},castleSupply={_manpowerCastleSupplyOps},blocked={_manpowerBlockedTroops}) | " +
                    $"slavePriceRange={slaveRange} (snapshots={_slavePriceSnapshots},dailyBonus={_slaveDailyBonusEvents}) | " +
                    $"tariffBasis({tariffBasis}) | " +
                    $"compatFoodPatches={_compatFoodPatchesApplied} | " +
                    $"harmony={harmony} | " +
                    $"revenueTaperErrors={_revenueTaperFailures} | " +
                    $"softFails={_softFails} [{softFailDetails}]");
                B1071_SessionFileLog.WriteTagged("Session", $"TariffBasisSpread town: {townSpread}");
                B1071_SessionFileLog.WriteTagged("Session", $"TariffBasisSpread village: {villageSpread}");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Session] Summary emit failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Min-max span of a sampled series, or "n/a" when the session never produced a sample.
        /// </summary>
        private static string Range(int samples, int min, int max) =>
            samples > 0 ? $"{min}-{max}(n={samples})" : "n/a";

        /// <summary>
        /// The octave counts behind a basis range, rendered as "at-least-2048=141,..." where each
        /// figure is a bucket's lower bound. The first non-empty and last non-empty buckets are the
        /// two that matter and everything between them is printed whether or not it is populated,
        /// so the gaps stay visible: a bimodal spread reading "512=3,1024=0,2048=400" says
        /// something a min-max pair cannot. Returns "n/a" when the session produced no samples.
        /// </summary>
        private static string Histogram(int[] buckets)
        {
            int first = -1;
            int last = -1;
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] > 0)
                {
                    if (first < 0) first = i;
                    last = i;
                }
            }

            if (first < 0 || last < 0)
            {
                return "n/a";
            }

            var parts = new List<string>();
            for (int i = first; i <= last; i++)
            {
                parts.Add($"{(i == buckets.Length - 1 ? ">=" : "atleast")}{1 << i}={buckets[i]}");
            }

            return string.Join(",", parts);
        }

        private static List<string> BuildModuleVersionSnapshot(List<string> friendlyNames)
        {
            if (friendlyNames.Count == 0)
                return new List<string>();

            var moduleMap = TryGetModuleManagerVersions();
            if (moduleMap.Count > 0)
            {
                return friendlyNames
                    .Select(name =>
                    {
                        if (moduleMap.TryGetValue(name, out string version))
                            return $"{name} {version}";
                        return $"{name} n/a";
                    })
                    .ToList();
            }

            var assemblyVersions = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Select(a =>
                {
                    string asmName = a.GetName().Name ?? string.Empty;
                    Version? v = a.GetName().Version;
                    string vText = v == null ? "n/a" : v.ToString(4);
                    return (asmName, vText);
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.asmName))
                .ToList();

            return friendlyNames
                .Select(name =>
                {
                    string key = Normalize(name);
                    var match = assemblyVersions.FirstOrDefault(x => Normalize(x.asmName).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                    return string.IsNullOrEmpty(match.asmName) ? $"{name} n/a" : $"{name} {match.vText}";
                })
                .ToList();
        }

        private static Dictionary<string, string> TryGetModuleManagerVersions()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Type? helperType = Type.GetType("TaleWorlds.ModuleManager.ModuleHelper, TaleWorlds.ModuleManager", throwOnError: false);
                MethodInfo? getModules = helperType?.GetMethod("GetModules", BindingFlags.Public | BindingFlags.Static);
                if (getModules == null) return result;

                object? raw = getModules.Invoke(null, null);
                if (raw is not IEnumerable modules) return result;

                foreach (object module in modules)
                {
                    string? name = GetStringProp(module, "Name");
                    string? versionRaw = GetStringProp(module, "Version")
                                         ?? GetStringProp(module, "VersionText")
                                         ?? GetStringProp(module, "VersionString");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    string version = string.IsNullOrWhiteSpace(versionRaw) ? "n/a" : NormalizeVersion(versionRaw!);
                    result[name!] = version;
                }
            }
            catch
            {
                // Non-fatal: fallback path will attempt assembly versions.
            }

            return result;
        }

        private static string? GetStringProp(object instance, string prop)
        {
            try
            {
                object? val = instance.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
                return val?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string Normalize(string value)
        {
            return new string(value
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToLowerInvariant();
        }

        private static string NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "n/a";
            string cleaned = raw.Trim();
            if (cleaned.StartsWith("v", true, CultureInfo.InvariantCulture))
                cleaned = cleaned.Substring(1);
            return "v" + cleaned;
        }
    }
}