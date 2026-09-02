using System;
using System.Globalization;
using System.Text;

namespace Byzantium1071.Campaign
{
    /// <summary>
    /// How one AI party's settlement visit ended, judged only on "did it come here with room
    /// to fill, and did it leave any fuller?".
    /// </summary>
    internal enum B1071_VisitOutcome
    {
        /// <summary>Party had room and left with more men than it arrived with.</summary>
        Gained,

        /// <summary>
        /// Party had room and left no fuller than it arrived. A wasted trip — the number the
        /// A3 recruitment-reachability question turns on.
        /// </summary>
        NoGain,

        /// <summary>
        /// Party was already at its size limit on arrival, so there was no gain to be had and
        /// nothing to diagnose. Excluded from the wasted-trip count rather than counted as a
        /// success, because it was never a recruitment trip.
        /// </summary>
        NotSeeking
    }

    /// <summary>
    /// One campaign day of telemetry. Plain counters written by B1071_TelemetryBehavior and
    /// formatted by <see cref="B1071_TelemetryMath"/>; holds no game references so the whole
    /// shape stays unit-testable.
    /// </summary>
    internal sealed class B1071_TelemetryDay
    {
        internal int Day { get; set; }

        // --- Troop power valuation (v1.0.3.7) --------------------------------------------
        internal int LordParties { get; set; }
        internal int Armies { get; set; }
        internal int WarsDeclared { get; set; }
        internal int SurvivabilityPreset { get; set; }

        /// <summary>Summed non-hero power of every AI lord party, at vanilla tier pricing.</summary>
        internal float VanillaPower { get; set; }

        /// <summary>The same sum with B1071_TroopPowerValuationPatch's factor applied.</summary>
        internal float PatchedPower { get; set; }

        // --- AI recovery routing (v1.0.3.5 / v1.0.3.6) -----------------------------------
        internal int RecoveryEligible { get; set; }
        internal int RecoveryQuoted { get; set; }
        internal int RecoveryZeroQuote { get; set; }
        internal int RecoveryProposed { get; set; }
        internal int RecoveryConfirmed { get; set; }

        // --- Settlement visits (the A3 discriminator) ------------------------------------
        internal int Visits { get; set; }
        internal int VisitsNoGain { get; set; }
        internal int VisitsNoGainWeak { get; set; }
        internal int VisitsNoGainHealthy { get; set; }

        // --- Why weak lords never reached a recovery pass --------------------------------

        /// <summary>
        /// Party-hours in which a lord below the 60% recovery threshold was turned away by
        /// <c>GetBlockReasons</c> before any settlement could be quoted.
        /// </summary>
        internal int BlockedWeak { get; set; }

        /// <summary>
        /// Per-reason histogram, indexed to match the bit positions of
        /// B1071_AiRecoveryBlockReason (index i is 1 &lt;&lt; i). One blocked party-hour can
        /// set several flags, so these SUM TO MORE than <see cref="BlockedWeak"/> -- they
        /// answer "which gates are firing", not "how many lords were turned away".
        /// </summary>
        internal int[] BlockedReasons { get; } =
            new int[B1071_TelemetryMath.BlockReasonNames.Length];

        // --- Demobilization AI extensions (v1.0.3.4) -------------------------------------
        internal int AiExtensions { get; set; }
        internal int AiExtensionGold { get; set; }
        internal int AiRetired { get; set; }
    }

    internal static class B1071_TelemetryMath
    {
        /// <summary>
        /// Short names for B1071_AiRecoveryBlockReason, ordered by bit position: index i is
        /// the flag 1 &lt;&lt; i. They are deliberately terser than the enum members because
        /// they become CSV column headers.
        ///
        /// This array's ORDER is load-bearing -- it is the only thing tying a histogram
        /// bucket to the flag that filled it. BlockReasonNamesMatchTheFlagOrder guards it.
        /// </summary>
        internal static readonly string[] BlockReasonNames =
        {
            "Inactive", "InvalidLeader", "Army", "MapEvent", "Siege", "Transition",
            "Disbanding", "Retreating", "Starving", "UrgentFood", "Besieged",
            "Objective", "PartyType", "Quest"
        };

        /// <summary>
        /// Vanilla per-troop power for a non-hero, mirroring the game's own tier curve
        /// (2 + tier) * (10 + tier) * 0.02f.
        ///
        /// This is a deliberate second copy of a formula the mod does not own. It exists so
        /// telemetry can report "what the AI used to believe" beside "what it believes now",
        /// which is the only way to see B1071_TroopPowerValuationPatch working from a log. It
        /// is never consumed by gameplay — the patch itself multiplies the game's real return
        /// value and never reconstructs it. If a game update changes the curve, this number
        /// goes stale and the reported ratio drifts; the patch does not.
        ///
        /// Heroes are excluded by the caller: vanilla prices them off Hero.Level rather than
        /// Tier and the patch skips them, so including them would only dilute the signal.
        /// </summary>
        internal static float VanillaTroopPower(int tier)
        {
            int clamped = tier < 0 ? 0 : tier;
            return (2 + clamped) * (10 + clamped) * 0.02f;
        }

        /// <summary>
        /// Patched-over-vanilla power ratio, as a factor around 1. Returns 1 when there is no
        /// vanilla baseline to divide by, so an empty campaign day reads as "no change"
        /// rather than as a spike.
        /// </summary>
        internal static float PowerRatio(float vanillaPower, float patchedPower)
        {
            if (vanillaPower <= 0f || float.IsNaN(vanillaPower) || float.IsInfinity(vanillaPower))
                return 1f;

            float ratio = patchedPower / vanillaPower;
            return float.IsNaN(ratio) || float.IsInfinity(ratio) ? 1f : ratio;
        }

        /// <summary>
        /// Classifies a completed visit. A party that arrived full is <see
        /// cref="B1071_VisitOutcome.NotSeeking"/> — it never wanted recruits, so counting it
        /// as a wasted trip would bury the signal under garrison rotations and patrol stops.
        /// </summary>
        internal static B1071_VisitOutcome ClassifyVisit(int membersOnEntry, int membersOnExit, int sizeLimit)
        {
            if (sizeLimit <= 0 || membersOnEntry >= sizeLimit) return B1071_VisitOutcome.NotSeeking;
            return membersOnExit > membersOnEntry
                ? B1071_VisitOutcome.Gained
                : B1071_VisitOutcome.NoGain;
        }

        /// <summary>
        /// Splits a wasted trip by the SAME 60% threshold that gates B1071_AiRecoveryBehavior
        /// (<see cref="B1071_AiRecoveryMath.ShouldStart"/>). That is the whole point of the
        /// bucket: recovery routing only ever sees parties below this line, so wasted trips
        /// landing mostly in "weak" mean the existing system can be taught to avoid them,
        /// while wasted trips landing mostly in "healthy" mean it structurally cannot and the
        /// fix belongs somewhere else.
        /// </summary>
        internal static bool IsWeakEnoughForRecovery(int members, int sizeLimit)
            => B1071_AiRecoveryMath.ShouldStart(members, sizeLimit);

        internal static string CsvHeader()
            => "day,lordParties,armies,warsDeclared,preset,vanillaPower,patchedPower,powerRatio,"
             + "recoveryEligible,recoveryQuoted,recoveryZeroQuote,recoveryProposed,recoveryConfirmed,"
             + "visits,visitsNoGain,visitsNoGainWeak,visitsNoGainHealthy,"
             + "aiExtensions,aiExtensionGold,aiRetired,"
             + "blockedWeak," + string.Join(",", BlockPrefixedNames());

        /// <summary>Histogram column headers, "blk" + reason name.</summary>
        private static string[] BlockPrefixedNames()
        {
            var names = new string[BlockReasonNames.Length];
            for (int i = 0; i < names.Length; i++) names[i] = "blk" + BlockReasonNames[i];
            return names;
        }

        /// <summary>
        /// One CSV row. Every field is numeric and invariant-formatted, so no quoting or
        /// escaping is needed and the file opens the same way in every locale.
        /// </summary>
        internal static string CsvRow(B1071_TelemetryDay day)
        {
            if (day == null) return string.Empty;

            string core = string.Join(",", new[]
            {
                Int(day.Day),
                Int(day.LordParties),
                Int(day.Armies),
                Int(day.WarsDeclared),
                Int(day.SurvivabilityPreset),
                Num(day.VanillaPower),
                Num(day.PatchedPower),
                Num(PowerRatio(day.VanillaPower, day.PatchedPower), 4),
                Int(day.RecoveryEligible),
                Int(day.RecoveryQuoted),
                Int(day.RecoveryZeroQuote),
                Int(day.RecoveryProposed),
                Int(day.RecoveryConfirmed),
                Int(day.Visits),
                Int(day.VisitsNoGain),
                Int(day.VisitsNoGainWeak),
                Int(day.VisitsNoGainHealthy),
                Int(day.AiExtensions),
                Int(day.AiExtensionGold),
                Int(day.AiRetired)
            });

            var tail = new string[1 + BlockReasonNames.Length];
            tail[0] = Int(day.BlockedWeak);
            int[]? buckets = day.BlockedReasons;
            for (int i = 0; i < BlockReasonNames.Length; i++)
                tail[i + 1] = Int(buckets != null && i < buckets.Length ? buckets[i] : 0);

            return core + "," + string.Join(",", tail);
        }

        /// <summary>
        /// The troop-power digest. Reports the ratio as a signed percentage because that is
        /// the figure the v1.0.3.7 design is bounded on (a representative roster moves at most
        /// +2.5%), and prints army and war counts beside it because those are the two vanilla
        /// gates that read absolute power and would move if the bound were ever wrong.
        /// </summary>
        internal static string PowerDigest(B1071_TelemetryDay day)
        {
            float ratio = PowerRatio(day.VanillaPower, day.PatchedPower);
            return $"d{day.Day} parties={day.LordParties} armies={day.Armies} wars={day.WarsDeclared} "
                 + $"preset={day.SurvivabilityPreset} power={Num(day.PatchedPower)} "
                 + $"vanilla={Num(day.VanillaPower)} ({Signed((ratio - 1f) * 100f)}%)";
        }

        internal static string RecoveryDigest(B1071_TelemetryDay day)
            => $"d{day.Day} eligible={day.RecoveryEligible} quoted={day.RecoveryQuoted} "
             + $"zeroQuote={day.RecoveryZeroQuote} proposed={day.RecoveryProposed} "
             + $"confirmed={day.RecoveryConfirmed}";

        /// <summary>
        /// The visit digest. "noGain" is wasted recruitment trips; the weak/healthy split says
        /// whether B1071_AiRecoveryBehavior could have prevented them — see
        /// <see cref="IsWeakEnoughForRecovery"/>.
        /// </summary>
        internal static string VisitDigest(B1071_TelemetryDay day)
            => $"d{day.Day} visits={day.Visits} noGain={day.VisitsNoGain} "
             + $"(weak={day.VisitsNoGainWeak} healthy={day.VisitsNoGainHealthy})";

        /// <summary>
        /// The blocked-party digest: how many weak-lord party-hours never reached a settlement
        /// quote, and which gates turned them away, dominant reason first.
        ///
        /// This is the counterpart to <see cref="RecoveryDigest"/>'s "eligible" figure. That
        /// one counts who got in; without this one, a recovery system that helps almost nobody
        /// is indistinguishable from a map on which almost nobody needs help.
        /// </summary>
        internal static string BlockDigest(B1071_TelemetryDay day)
        {
            var text = new StringBuilder();
            text.Append("d").Append(Int(day.Day))
                .Append(" blockedWeak=").Append(Int(day.BlockedWeak));

            int[]? buckets = day.BlockedReasons;
            if (buckets == null) return text.ToString();

            int count = Math.Min(buckets.Length, BlockReasonNames.Length);
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, (a, b) => buckets[b].CompareTo(buckets[a]));

            foreach (int i in order)
            {
                if (buckets[i] <= 0) continue;
                text.Append(' ').Append(BlockReasonNames[i]).Append('=').Append(Int(buckets[i]));
            }

            return text.ToString();
        }

        internal static string DemobDigest(B1071_TelemetryDay day)
            => $"d{day.Day} aiExtensions={day.AiExtensions} gold={day.AiExtensionGold} "
             + $"retired={day.AiRetired}";

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Num(float value, int decimals = 2)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return Math.Round(value, decimals).ToString(CultureInfo.InvariantCulture);
        }

        private static string Signed(float value)
        {
            string body = Num(value);
            return value > 0f ? "+" + body : body;
        }
    }
}
