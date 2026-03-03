using Byzantium1071.Campaign.Settings;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
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

        private static int _softFails;
        private static readonly List<string> _softFailSamples = new();

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
            _softFails = 0;
            _softFailSamples.Clear();
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

        internal static void RecordSoftFail(string subsystem, string detail)
        {
            _softFails++;
            if (_softFailSamples.Count >= 3) return;
            _softFailSamples.Add($"{subsystem}:{detail}");
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

                Debug.Print(
                    "[Byzantium1071][Session] Summary: " +
                    $"manpowerOps={manpowerOps} " +
                    $"(consume={_manpowerConsumeOps},regen={_manpowerRegenOps},castleSupply={_manpowerCastleSupplyOps},blocked={_manpowerBlockedTroops}) | " +
                    $"slavePriceRange={slaveRange} (snapshots={_slavePriceSnapshots},dailyBonus={_slaveDailyBonusEvents}) | " +
                    $"compatFoodPatches={_compatFoodPatchesApplied} | " +
                    $"harmony={harmony} | " +
                    $"softFails={_softFails} [{softFailDetails}]");
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Session] Summary emit failed: {ex.GetType().Name}: {ex.Message}");
            }
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