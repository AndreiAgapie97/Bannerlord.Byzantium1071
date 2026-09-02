using System;
using System.Collections.Generic;
using Byzantium1071.Campaign.Patches;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Emits one telemetry snapshot per campaign day for the AI systems added in v1.0.3.4 -
    /// v1.0.3.7, so their effects can be read off a log instead of inferred from hours of
    /// play. Purely diagnostic: it reads campaign state and writes text, and changes nothing.
    ///
    /// WHAT IT ANSWERS:
    ///   - Is B1071_TroopPowerValuationPatch moving party power the small amount it is bounded
    ///     to, and are army count and war declarations holding steady? Those two are the
    ///     vanilla gates that read power as an ABSOLUTE number
    ///     (DefaultArmyManagementCalculationModel.CanLordCreateArmy's floor of 1000,
    ///     DefaultDiplomacyModel's floor of 500), so they are the side effect no unit test can
    ///     see. TroopPowerMathTests bounds the arithmetic; only this bounds the campaign.
    ///   - Is B1071_AiRecoveryBehavior actually finding supply, or quoting zero and giving up?
    ///   - And when a weak lord never reaches a quote at all, which eligibility gate turned
    ///     him away? Without that the "eligible" count cannot distinguish a system helping
    ///     almost nobody from a map on which almost nobody needs help.
    ///   - Are AI lords making wasted recruitment trips, and are the lords making them weak
    ///     enough for recovery routing to reach? That split is the open question the A3
    ///     recruitment-reachability work is waiting on -- see
    ///     B1071_TelemetryMath.IsWeakEnoughForRecovery.
    ///   - Are AI lords paying to keep their veterans, and how many are they still losing?
    ///
    /// Counter increments live at the sites being measured; this class only snapshots, formats
    /// and emits. Everything is gated on B1071_TelemetryCounters.Enabled, so an ordinary play
    /// session does none of the work below.
    /// </summary>
    internal sealed class B1071_TelemetryBehavior : CampaignBehaviorBase
    {
        private static IB1071Settings Settings =>
            B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        /// <summary>Party state captured on entry, so the visit can be judged on the way out.</summary>
        private sealed class VisitState
        {
            internal MobileParty Party = null!;
            internal int MembersOnEntry;
            internal int SizeLimit;

            /// <summary>
            /// Whether B1071_AiRecoveryBehavior had sent this party here. Sampled on entry,
            /// not on exit: the intent is cleared the moment the lord is topped up, so
            /// reading it on the way out would report every successful trip as unrouted and
            /// invert the whole measurement.
            /// </summary>
            internal bool RecoveryRouted;
        }

        /// <summary>
        /// A party's member count while it was still in the field, and when it was taken.
        ///
        /// THIS EXISTS BECAUSE SettlementEntered IS TOO LATE TO SAMPLE AN ARRIVAL. Vanilla
        /// hires the notable board from RecruitmentCampaignBehavior.OnBeforeSettlementEntered
        /// -> CheckRecruiting -> RecruitVolunteersFromNotable, and
        /// BeforeSettlementEnteredEvent fires ahead of SettlementEntered. So by the time this
        /// behavior sees an arrival, the recruits are already on the roster: the party leaves
        /// with the count it was first observed holding, and a successful recruiting trip is
        /// classified NoGain. That inverts the one number the visit metric exists to report.
        ///
        /// Moving to the earlier event would not help. MBCampaignEvent.RunHandlers walks its
        /// handler list in registration order, and vanilla's campaign behaviors register
        /// before any mod's, so a listener added here fires after the hire either way. The
        /// only ordering-independent answer is to sample before the party arrives at all.
        /// </summary>
        private sealed class FieldSample
        {
            internal MobileParty Party = null!;
            internal int Members;
            internal CampaignTime Stamp;
        }

        /// <summary>
        /// Open visits, keyed by party id. Session-scoped by design and never persisted: a
        /// visit spanning a save is worth less than the save-format risk of storing it
        /// (CLAUDE.md section 7).
        /// </summary>
        private readonly Dictionary<string, VisitState> _openVisits =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Latest in-the-field sample per party, consumed by the next arrival. Bounded by the
        /// lord-party count and swept daily alongside the open visits.
        /// </summary>
        private readonly Dictionary<string, FieldSample> _fieldSamples =
            new(StringComparer.Ordinal);

        /// <summary>
        /// How stale a field sample may be and still describe the arrival it is used for.
        /// Samples are taken on the hourly party tick and an arrival lands between two of
        /// them, so anything inside two hours is the strength the lord set out with; beyond
        /// that the party has been somewhere this behavior did not watch, and falling back to
        /// the entry count is wrong but bounded, where a stale sample could invent a gain.
        /// </summary>
        private const float MaxFieldSampleAgeInHours = 2f;

        /// <summary>Fuse: after this many handler faults the behavior stops touching anything.</summary>
        private const int MaxHandlerFailures = 5;
        private int _handlerFailures;
        private bool _disabledThisSession;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, OnHourlyTickParty);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
        }

        /// <summary>
        /// Nothing to persist. The counters describe one session's observations and the open
        /// visits are transient; writing either into the save would add a serialized field
        /// that no gameplay path reads.
        /// </summary>
        public override void SyncData(IDataStore dataStore) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _handlerFailures = 0;
            _disabledThisSession = false;
            _openVisits.Clear();
            _fieldSamples.Clear();

            B1071_TelemetryCounters.ResetForNewSession();
            if (!B1071_TelemetryCounters.Enabled) return;

            B1071_TelemetryCounters.BeginDay(CurrentDay());
            B1071_TelemetryCounters.Emit(
                "Session",
                $"Daily telemetry on. CSV: {B1071_TelemetryCsvLog.CurrentCsvPath ?? "(pending first row)"}");
        }

        private void OnWarDeclared(IFaction attacker, IFaction defender, DeclareWarAction.DeclareWarDetail detail)
        {
            if (_disabledThisSession) return;
            try { B1071_TelemetryCounters.RecordWarDeclared(); }
            catch (Exception ex) { NoteFailure("WarDeclared", ex); }
        }

        /// <summary>
        /// Records a party's strength while it is still travelling, so the arrival that
        /// follows can be judged on the strength the lord actually set out with. Only parties
        /// outside a settlement are sampled: one already inside is mid-visit, and overwriting
        /// its sample would erase the very figure the visit is being measured against.
        ///
        /// This runs for every party every campaign hour, so it does no work beyond a type
        /// check and a dictionary write, and none at all with telemetry off.
        /// </summary>
        private void OnHourlyTickParty(MobileParty party)
        {
            if (_disabledThisSession || !B1071_TelemetryCounters.Enabled) return;

            try
            {
                if (party == null || party == MobileParty.MainParty) return;
                if (party.LeaderHero == null || party.CurrentSettlement != null) return;

                string partyId = party.StringId;
                if (string.IsNullOrEmpty(partyId)) return;

                _fieldSamples[partyId] = new FieldSample
                {
                    Party = party,
                    Members = party.Party.NumberOfAllMembers,
                    Stamp = CampaignTime.Now
                };
            }
            catch (Exception ex) { NoteFailure("HourlyTickParty", ex); }
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (_disabledThisSession || !B1071_TelemetryCounters.Enabled) return;

            try
            {
                if (!IsTrackedVisit(party, settlement)) return;

                // The roster here already carries anything vanilla hired on the way in --
                // see FieldSample. A fresh in-the-field sample is what the lord set out
                // with, and is preferred whenever one exists. The size limit is read live
                // either way: it comes from clan tier and perks, not from the roster count,
                // so the hire cannot move it and a sampled copy could only go stale.
                int membersOnEntry = party.Party.NumberOfAllMembers;

                if (_fieldSamples.TryGetValue(party.StringId, out FieldSample? sample))
                {
                    _fieldSamples.Remove(party.StringId);

                    if (sample != null
                        && sample.Party == party
                        && sample.Stamp.ElapsedHoursUntilNow <= MaxFieldSampleAgeInHours)
                    {
                        membersOnEntry = sample.Members;
                    }
                }

                _openVisits[party.StringId] = new VisitState
                {
                    Party = party,
                    MembersOnEntry = membersOnEntry,
                    SizeLimit = party.Party.PartySizeLimit,
                    RecoveryRouted = B1071_AiRecoveryBehavior.Instance?
                        .IsRecoveryDestination(party, settlement) ?? false
                };
            }
            catch (Exception ex) { NoteFailure("SettlementEntered", ex); }
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            if (_disabledThisSession) return;

            try
            {
                if (party == null || string.IsNullOrEmpty(party.StringId)) return;
                if (!_openVisits.TryGetValue(party.StringId, out VisitState? state)) return;
                _openVisits.Remove(party.StringId);

                int membersOnExit = party.Party.NumberOfAllMembers;
                B1071_VisitOutcome outcome = B1071_TelemetryMath.ClassifyVisit(
                    state.MembersOnEntry,
                    membersOnExit,
                    state.SizeLimit);

                // The weak/healthy split is judged on ARRIVAL strength, because that is what
                // the party was when it chose to come here -- which is the decision under
                // examination, not the state it happened to leave in.
                bool weak = B1071_TelemetryMath.IsWeakEnoughForRecovery(
                    state.MembersOnEntry,
                    state.SizeLimit);

                B1071_TelemetryCounters.RecordVisit(outcome, weak, state.RecoveryRouted);
            }
            catch (Exception ex) { NoteFailure("SettlementLeft", ex); }
        }

        private void OnDailyTick()
        {
            if (_disabledThisSession) return;

            try
            {
                // Visits opened while telemetry was on would otherwise sit here forever after
                // the player switches it off mid-campaign.
                if (!B1071_TelemetryCounters.Enabled)
                {
                    if (_openVisits.Count > 0) _openVisits.Clear();
                    if (_fieldSamples.Count > 0) _fieldSamples.Clear();
                    return;
                }

                DropAbandonedVisits();
                DropStaleFieldSamples();

                B1071_TelemetryDay day = B1071_TelemetryCounters.Current;
                FillPartySnapshot(day);

                // Day 0 is the accumulator BeginDay never got to label, because telemetry was
                // off when the session launched. Its counters cover only the minutes since the
                // player switched it on, so it is started rather than reported.
                if (day.Day <= 0)
                {
                    B1071_TelemetryCounters.BeginDay(CurrentDay());
                    return;
                }

                B1071_TelemetryCsvLog.AppendRow(B1071_TelemetryMath.CsvRow(day));
                B1071_TelemetryCounters.Emit("Power", B1071_TelemetryMath.PowerDigest(day));
                B1071_TelemetryCounters.Emit("AiRecovery", B1071_TelemetryMath.RecoveryDigest(day));
                B1071_TelemetryCounters.Emit("AiRecoveryBlocks", B1071_TelemetryMath.BlockDigest(day));
                B1071_TelemetryCounters.Emit("Visits", B1071_TelemetryMath.VisitDigest(day));
                B1071_TelemetryCounters.Emit("Demob", B1071_TelemetryMath.DemobDigest(day));

                B1071_TelemetryCounters.BeginDay(CurrentDay());
            }
            catch (Exception ex) { NoteFailure("DailyTick", ex); }
        }

        /// <summary>
        /// A party destroyed or disbanded inside a settlement never fires SettlementLeft, so
        /// its entry would sit in the dictionary for the rest of the session. Over a long
        /// campaign that is slow unbounded growth in a system that is supposed to cost
        /// nothing, so the open set is swept once a day.
        /// </summary>
        private void DropAbandonedVisits()
        {
            if (_openVisits.Count == 0) return;

            List<string>? stale = null;
            foreach (KeyValuePair<string, VisitState> entry in _openVisits)
            {
                MobileParty party = entry.Value.Party;
                if (party != null && party.IsActive && party.CurrentSettlement != null) continue;

                stale ??= new List<string>();
                stale.Add(entry.Key);
            }

            if (stale == null) return;
            foreach (string key in stale) _openVisits.Remove(key);
        }

        /// <summary>
        /// A party destroyed in the field leaves its sample behind, and a party that never
        /// visits anywhere never consumes one. Both are swept on the same daily pass that
        /// clears abandoned visits, so neither grows across a long campaign.
        /// </summary>
        private void DropStaleFieldSamples()
        {
            if (_fieldSamples.Count == 0) return;

            List<string>? stale = null;
            foreach (KeyValuePair<string, FieldSample> entry in _fieldSamples)
            {
                MobileParty party = entry.Value.Party;
                if (party != null && party.IsActive) continue;

                stale ??= new List<string>();
                stale.Add(entry.Key);
            }

            if (stale == null) return;
            foreach (string key in stale) _fieldSamples.Remove(key);
        }

        /// <summary>
        /// Walks every AI lord party once and sums its non-hero roster power twice: at vanilla
        /// tier pricing, and with B1071_TroopPowerValuationPatch's factor applied. The pair is
        /// the whole point -- either number alone says nothing, and the game exposes no
        /// "what would this have been" value to read back.
        ///
        /// These two are a DRIFT PAIR, not the game's own numbers: GetPowerOfParty also folds
        /// in heroes, morale and battle-context modifiers, none of which this patch touches.
        /// So their ratio measures exactly what B1071_TroopPowerValuationPatch did, but the
        /// absolute totals are not the value CanLordCreateArmy compares against its floor of
        /// 1000 -- the armies column is what watches that gate.
        ///
        /// Cost is roughly the party count times the mean roster length -- on a mature map a
        /// few tens of thousands of integer operations, once per campaign day, allocating
        /// nothing, and only when telemetry is switched on.
        /// </summary>
        private static void FillPartySnapshot(B1071_TelemetryDay day)
        {
            day.SurvivabilityPreset = Settings.EliteSurvivabilityPreset;

            int parties = 0;
            float vanilla = 0f;
            float patched = 0f;

            foreach (MobileParty party in MobileParty.AllLordParties)
            {
                if (party == null || party == MobileParty.MainParty) continue;
                if (party.LeaderHero == null) continue;

                parties++;

                foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster())
                {
                    CharacterObject troop = element.Character;
                    if (troop == null || troop.IsHero) continue;

                    int alive = Math.Max(0, element.Number - element.WoundedNumber);
                    if (alive <= 0) continue;

                    float unit = B1071_TelemetryMath.VanillaTroopPower(troop.Tier);
                    vanilla += alive * unit;
                    patched += alive * unit * B1071_CombatRealismTuning.GetPowerFactor(troop.Tier);
                }
            }

            day.LordParties = parties;
            day.VanillaPower = vanilla;
            day.PatchedPower = patched;
            day.Armies = CountArmies();
        }

        /// <summary>
        /// Armies in the field. Watched because CanLordCreateArmy gates on a summed strength
        /// of 1000 -- a constant on the vanilla power scale, and therefore the first thing
        /// that would move if the power multiplier ever stopped being centred.
        /// </summary>
        private static int CountArmies()
        {
            int armies = 0;
            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated) continue;
                armies += kingdom.Armies.Count;
            }
            return armies;
        }

        /// <summary>
        /// AI lord parties visiting a settlement that can actually supply men. Hideouts and
        /// the player's own party are excluded: neither says anything about AI recruitment
        /// routing, and counting them would dilute the wasted-trip rate the A3 decision rests
        /// on.
        /// </summary>
        private static bool IsTrackedVisit(MobileParty? party, Settlement? settlement)
        {
            if (party == null || settlement == null) return false;
            if (party == MobileParty.MainParty || party.LeaderHero == null) return false;
            if (string.IsNullOrEmpty(party.StringId)) return false;
            return settlement.IsVillage || settlement.IsTown || settlement.IsCastle;
        }

        private static int CurrentDay() => (int)CampaignTime.Now.ToDays;

        /// <summary>
        /// Diagnostics must never take a campaign down with them. A repeatedly faulting
        /// handler switches the whole behavior off for the session rather than logging once
        /// per tick forever.
        /// </summary>
        private void NoteFailure(string where, Exception ex)
        {
            _handlerFailures++;
            Debug.Print($"[Byzantium1071][Telemetry] {where} failed: {ex.GetType().Name}: {ex.Message}");

            if (_handlerFailures < MaxHandlerFailures) return;

            _disabledThisSession = true;
            _openVisits.Clear();
            _fieldSamples.Clear();
            Debug.Print($"[Byzantium1071][Telemetry] Disabled for this session after {MaxHandlerFailures} failures.");
        }
    }
}
