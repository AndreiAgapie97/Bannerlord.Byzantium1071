using System;
using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    /// <summary>
    /// Per-day counters for the AI systems shipped in v1.0.3.4 - v1.0.3.7. Each Record*
    /// method checks <see cref="Enabled"/> itself, so call sites stay one unguarded line and
    /// cannot forget the gate -- the same shape as B1071_SessionAudit.
    ///
    /// WHY COUNTERS AND NOT EVENT LINES: the systems being observed run at a frequency no log
    /// can absorb. B1071_AiRecoveryBehavior.OnAiHourlyTick fires once per lord party per
    /// campaign hour -- on a mature map that is roughly two hundred parties times twenty-four,
    /// near five thousand calls a day -- and the troop power patch runs once per troop per
    /// party per strength query. One line each would bury the signal and cost more than the
    /// systems themselves. So the hot paths only increment an int, and B1071_TelemetryBehavior
    /// emits one digest and one CSV row per campaign day.
    ///
    /// All state is static and session-scoped by design: it is diagnostic output, never
    /// gameplay input, and must never reach SyncData (CLAUDE.md section 7). Counters are not
    /// locked because every writer is a campaign event on the main thread, matching
    /// B1071_SessionAudit; the file writers underneath do lock.
    /// </summary>
    internal static class B1071_TelemetryCounters
    {
        private static IB1071Settings Settings =>
            B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private static B1071_TelemetryDay _day = new();

        /// <summary>
        /// Gated on the existing telemetry toggle rather than a new setting: this reports on
        /// systems the toggle already covers, and a player who turns on telemetry logs wants
        /// exactly this. Verbose mod logging turns it on too, matching the house idiom used at
        /// B1071_AiRecoveryBehavior and B1071_ManpowerBehavior.
        /// </summary>
        internal static bool Enabled
        {
            get
            {
                try { return Settings.TelemetryDebugLogs || B1071_VerboseLog.Enabled; }
                catch { return false; }
            }
        }

        /// <summary>
        /// The day currently accumulating. Handed to B1071_TelemetryBehavior so it can fill in
        /// the once-a-day snapshot fields (party counts, summed power) immediately before
        /// emitting; every other field is already filled by the Record* calls below.
        /// </summary>
        internal static B1071_TelemetryDay Current => _day;

        /// <summary>Drops everything. Called when a campaign session starts or ends.</summary>
        internal static void ResetForNewSession()
        {
            _day = new B1071_TelemetryDay();
        }

        /// <summary>
        /// Starts a fresh accumulator for <paramref name="day"/>. Called after the previous
        /// day has been emitted, so counters describe exactly the day they are labelled with.
        /// </summary>
        internal static void BeginDay(int day)
        {
            _day = new B1071_TelemetryDay { Day = day };
        }

        // --- AI recovery routing ---------------------------------------------------------

        /// <summary>A party passed the strength gate and entered a recovery pass.</summary>
        internal static void RecordRecoveryEligible()
        {
            if (!Enabled) return;
            _day.RecoveryEligible++;
        }

        /// <summary>
        /// One candidate settlement was quoted. <paramref name="total"/> is the quote's men,
        /// so a zero quote -- a settlement that could supply nothing -- is separated out: it
        /// is the population the A3 penalty would act on.
        /// </summary>
        internal static void RecordRecoveryQuote(int total)
        {
            if (!Enabled) return;
            _day.RecoveryQuoted++;
            if (total <= 0) _day.RecoveryZeroQuote++;
        }

        /// <summary>A candidate's score was rewritten in PartyThinkParams.</summary>
        internal static void RecordRecoveryProposed()
        {
            if (!Enabled) return;
            _day.RecoveryProposed++;
        }

        /// <summary>The party actually took the proposed settlement as its destination.</summary>
        internal static void RecordRecoveryConfirmed()
        {
            if (!Enabled) return;
            _day.RecoveryConfirmed++;
        }

        /// <summary>
        /// A lord below the recovery threshold was turned away before any settlement could be
        /// quoted. Every flag set on <paramref name="reasons"/> is counted, so the histogram
        /// sums to more than the party-hour total -- one lord can be in an army AND on a
        /// protected objective, and both gates are worth seeing.
        /// </summary>
        internal static void RecordRecoveryBlocked(B1071_AiRecoveryBlockReason reasons)
        {
            if (!Enabled || reasons == B1071_AiRecoveryBlockReason.None) return;

            _day.BlockedWeak++;

            int[] buckets = _day.BlockedReasons;
            int flags = (int)reasons;
            for (int i = 0; i < buckets.Length; i++)
            {
                if ((flags & (1 << i)) != 0) buckets[i]++;
            }
        }

        // --- Settlement visits -----------------------------------------------------------

        /// <summary>
        /// A completed AI visit. <paramref name="weak"/> comes from
        /// B1071_TelemetryMath.IsWeakEnoughForRecovery and is only meaningful for a wasted
        /// trip, which is why the weak/healthy split is recorded on that branch alone.
        /// </summary>
        internal static void RecordVisit(B1071_VisitOutcome outcome, bool weak)
        {
            if (!Enabled) return;
            if (outcome == B1071_VisitOutcome.NotSeeking) return;

            _day.Visits++;
            if (outcome != B1071_VisitOutcome.NoGain) return;

            _day.VisitsNoGain++;
            if (weak) _day.VisitsNoGainWeak++;
            else _day.VisitsNoGainHealthy++;
        }

        // --- Demobilization AI extensions ------------------------------------------------

        /// <summary>Men an AI lord paid to keep in service, and what it cost him.</summary>
        internal static void RecordAiExtensions(int soldiers, int gold)
        {
            if (!Enabled || soldiers <= 0) return;
            _day.AiExtensions += soldiers;
            _day.AiExtensionGold += Math.Max(0, gold);
        }

        /// <summary>Men an AI lord lost to expired service this tick.</summary>
        internal static void RecordAiRetired(int count)
        {
            if (!Enabled || count <= 0) return;
            _day.AiRetired += count;
        }

        // --- War declarations ------------------------------------------------------------

        /// <summary>
        /// A war was declared. Watched because DefaultDiplomacyModel refuses war below a
        /// hard-coded CurrentTotalStrength of 500 -- an absolute threshold on the same scale
        /// B1071_TroopPowerValuationPatch multiplies. A jump here across a preset change is
        /// the failure signature that no unit test can see.
        /// </summary>
        internal static void RecordWarDeclared()
        {
            if (!Enabled) return;
            _day.WarsDeclared++;
        }

        /// <summary>
        /// Writes one digest line to the session log and the debug output. Lives here rather
        /// than on B1071_VerboseLog because that helper gates on EnableVerboseModLog alone,
        /// which would silently swallow everything for a player who turned on the telemetry
        /// setting and nothing else.
        /// </summary>
        internal static void Emit(string subsystem, string message)
        {
            if (!Enabled) return;

            try
            {
                TaleWorlds.Library.Debug.Print($"[Byzantium1071][Telemetry][{subsystem}] {message}");
                B1071_SessionFileLog.WriteTagged("Telemetry", $"[{subsystem}] {message}");
            }
            catch
            {
                // Diagnostics must never break gameplay.
            }
        }
    }
}
