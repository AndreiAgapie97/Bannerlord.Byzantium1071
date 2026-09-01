using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Extends Bannerlord's completed settlement-visit scores with Campaign++ recruit
    /// availability. It never creates a route or issues a movement order.
    /// </summary>
    internal sealed class B1071_AiRecoveryBehavior : CampaignBehaviorBase
    {
        private const float ReservationDurationDays = 0.5f;

        internal static B1071_AiRecoveryBehavior? Instance { get; set; }

        private static IB1071Settings Settings
            => B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private sealed class RecoveryIntent
        {
            internal MobileParty Party = null!;
            internal Settlement Target = null!;
        }

        private sealed class RecoveryReservation
        {
            internal MobileParty Party = null!;
            internal Settlement Settlement = null!;
            internal B1071_AiRecoveryReservedSupply Supply;
            internal float ExpiryDay;
        }

        private readonly struct RecoveryCandidate
        {
            internal AIBehaviorData Behavior { get; }
            internal Settlement Settlement { get; }
            internal float AdjustedScore { get; }
            internal B1071_AiRecoverySourceQuote Quote { get; }

            internal RecoveryCandidate(
                AIBehaviorData behavior,
                Settlement settlement,
                float adjustedScore,
                B1071_AiRecoverySourceQuote quote)
            {
                Behavior = behavior;
                Settlement = settlement;
                AdjustedScore = adjustedScore;
                Quote = quote;
            }
        }

        private readonly Dictionary<string, RecoveryIntent> _intents
            = new Dictionary<string, RecoveryIntent>(StringComparer.Ordinal);
        private readonly Dictionary<string, RecoveryReservation> _reservations
            = new Dictionary<string, RecoveryReservation>(StringComparer.Ordinal);
        private bool _loggedNativeScoreObservation;
        private bool _pendingLoadReconstruction;

        public override void RegisterEvents()
        {
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, OnAiHourlyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, OnAfterSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Recovery intent and reservations are deliberately session-scoped.
        }

        /// <summary>
        /// Runtime state is not cleared here. The behavior is constructed once per campaign,
        /// so it starts empty anyway, and this event fires after <see cref="OnGameLoaded"/> —
        /// clearing would discard the intents the load path exists to rebuild.
        /// </summary>
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
            _loggedNativeScoreObservation = false;
        }

        /// <summary>
        /// Only records that this session came from a save. The reconstruction itself reads
        /// the veteran and castle singletons, and those are not assigned until their own
        /// OnSessionLaunched runs — which is after this event.
        /// </summary>
        private void OnGameLoaded(CampaignGameStarter starter)
        {
            _pendingLoadReconstruction = true;
        }

        /// <summary>
        /// Rebuilds the recovery intents a save could not carry. Runs after every
        /// OnSessionLaunched listener, so every quote source this walk needs now exists.
        /// </summary>
        private void OnAfterSessionLaunched(CampaignGameStarter starter)
        {
            if (!_pendingLoadReconstruction) return;
            _pendingLoadReconstruction = false;

            try
            {
                ClearRuntimeState();

                foreach (MobileParty party in MobileParty.AllLordParties)
                {
                    if (!IsPartyEligible(party)) continue;

                    Settlement? target = party.TargetSettlement;
                    int members = party.Party.NumberOfAllMembers;
                    int limit = party.Party.PartySizeLimit;
                    bool friendlyTarget = target != null && target.MapFaction == party.MapFaction;
                    bool ordinaryJourney = party.DefaultBehavior == AiBehavior.GoToSettlement;
                    int missing = B1071_AiRecoveryMath.MissingToStop(members, limit);
                    bool hasOffer = target != null
                        && QuoteSettlement(party, target, missing, default).Total > 0;

                    if (!B1071_AiRecoveryMath.CanReconstruct(
                            members,
                            limit,
                            ordinaryJourney,
                            friendlyTarget,
                            hasOffer))
                        continue;

                    string partyId = party.StringId;
                    if (!string.IsNullOrEmpty(partyId))
                        _intents[partyId] = new RecoveryIntent { Party = party, Target = target! };
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][AiRecovery] Load reconstruction skipped: {ex.GetType().Name}: {ex.Message}");
                ClearRuntimeState();
            }
        }

        private void OnAiHourlyTick(MobileParty party, PartyThinkParams thinkParams)
        {
            try
            {
                if (party == null || thinkParams == null) return;

                float nowDay = (float)CampaignTime.Now.ToDays;
                CleanupRuntimeState(nowDay);
                LogNativeScoreObservationOnce(party, thinkParams);

                if (!Settings.EnableAiRecoveryRouting)
                {
                    ClearRuntimeState();
                    return;
                }

                string partyId = party.StringId;
                if (string.IsNullOrEmpty(partyId)) return;

                if (!IsPartyEligible(party))
                {
                    ClearPartyState(partyId);
                    return;
                }

                int members = party.Party.NumberOfAllMembers;
                int limit = party.Party.PartySizeLimit;
                bool hasIntent = _intents.TryGetValue(partyId, out RecoveryIntent? intent);
                if (hasIntent && B1071_AiRecoveryMath.HasReachedStop(members, limit))
                {
                    ClearPartyState(partyId);
                    return;
                }

                if (!hasIntent && !B1071_AiRecoveryMath.ShouldStart(members, limit))
                    return;

                int missing = B1071_AiRecoveryMath.MissingToStop(members, limit);
                if (missing <= 0)
                {
                    ClearPartyState(partyId);
                    return;
                }

                float highestNativeScore = float.MinValue;
                foreach (var scoreEntry in thinkParams.AIBehaviorScores)
                {
                    float score = scoreEntry.Item2;
                    if (!float.IsNaN(score) && !float.IsInfinity(score))
                        highestNativeScore = Math.Max(highestNativeScore, score);
                }

                if (highestNativeScore == float.MinValue) return;

                var candidates = new List<RecoveryCandidate>();
                foreach (var scoreEntry in thinkParams.AIBehaviorScores)
                {
                    AIBehaviorData behavior = scoreEntry.Item1;
                    float nativeScore = scoreEntry.Item2;
                    if (behavior.AiBehavior != AiBehavior.GoToSettlement) continue;
                    if (!(behavior.Party is Settlement settlement)) continue;
                    if (nativeScore <= 0f || float.IsNaN(nativeScore) || float.IsInfinity(nativeScore)) continue;
                    if (!IsCandidateSettlement(party, settlement)) continue;

                    B1071_AiRecoveryReservedSupply reserved = GetReservedSupply(
                        settlement,
                        partyId,
                        nowDay);
                    B1071_AiRecoverySourceQuote quote = QuoteSettlement(
                        party,
                        settlement,
                        missing,
                        reserved);
                    if (quote.Total <= 0) continue;

                    candidates.Add(new RecoveryCandidate(
                        behavior,
                        settlement,
                        B1071_AiRecoveryMath.CandidateScore(nativeScore, quote.Total, missing),
                        quote));
                }

                if (candidates.Count == 0)
                {
                    ClearPartyState(partyId);
                    return;
                }

                RecoveryCandidate best = candidates
                    .OrderByDescending(candidate => candidate.AdjustedScore)
                    .First();
                RecoveryCandidate selected = best;

                if (intent?.Target != null)
                {
                    foreach (RecoveryCandidate candidate in candidates)
                    {
                        if (candidate.Settlement != intent.Target) continue;
                        if (B1071_AiRecoveryMath.IsWithinStickiness(
                                candidate.AdjustedScore,
                                best.AdjustedScore))
                            selected = candidate;
                        break;
                    }
                }

                float winningScore = B1071_AiRecoveryMath.WinningScore(highestNativeScore);
                AIBehaviorData winningBehavior = selected.Behavior;
                thinkParams.SetBehaviorScore(in winningBehavior, winningScore);

                _intents[partyId] = new RecoveryIntent { Party = party, Target = selected.Settlement };
                _reservations[partyId] = new RecoveryReservation
                {
                    Party = party,
                    Settlement = selected.Settlement,
                    Supply = new B1071_AiRecoveryReservedSupply(
                        selected.Quote.Veterans,
                        selected.Quote.Elites,
                        selected.Quote.Prisoners,
                        selected.Quote.Manpower),
                    ExpiryDay = nowDay + ReservationDurationDays
                };
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][AiRecovery] Score extension skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal void NotifyRecruitment(MobileParty party, Settlement settlement)
        {
            if (party == null || settlement == null || string.IsNullOrEmpty(party.StringId)) return;
            if (_reservations.TryGetValue(party.StringId, out RecoveryReservation? reservation)
                && reservation.Settlement == settlement)
                _reservations.Remove(party.StringId);
        }

        internal void ClearRuntimeState()
        {
            _intents.Clear();
            _reservations.Clear();
        }

        private B1071_AiRecoverySourceQuote QuoteSettlement(
            MobileParty party,
            Settlement settlement,
            int missing,
            B1071_AiRecoveryReservedSupply reserved)
        {
            if (missing <= 0 || party.LeaderHero == null) return default;

            int partyRoom = Math.Max(0, party.Party.PartySizeLimit - party.Party.NumberOfAllMembers);
            int room = Math.Min(partyRoom, missing);
            if (room <= 0) return default;

            B1071_ManpowerBehavior? manpower = B1071_ManpowerBehavior.Instance;
            bool finiteManpower = manpower != null;
            int availableManpower = int.MaxValue;
            if (manpower != null)
            {
                manpower.GetManpowerPool(settlement, out availableManpower, out _, out _);
                availableManpower = Math.Max(0, availableManpower - reserved.Manpower);
            }

            var budget = new B1071_AiRecoveryBudget(
                room,
                party.LeaderHero.Gold,
                availableManpower,
                finiteManpower);

            B1071_AiRecoverySourceQuote quote = default;
            B1071_DemobilizationBehavior? veterans = B1071_DemobilizationBehavior.Instance;
            if (veterans != null)
                quote += veterans.QuoteAiRecoveryVeterans(
                    party,
                    settlement,
                    budget,
                    reserved.Veterans);

            B1071_CastleRecruitmentBehavior? castle = B1071_CastleRecruitmentBehavior.Instance;
            if (castle != null && budget.Room > 0)
                quote += castle.QuoteAiRecoveryCastleRecruits(
                    party,
                    settlement,
                    budget,
                    reserved.Elites,
                    reserved.Prisoners);

            return quote;
        }

        private static bool IsCandidateSettlement(MobileParty party, Settlement settlement)
        {
            if (party.MapFaction == null || settlement.MapFaction != party.MapFaction) return false;
            if (settlement.IsUnderSiege || settlement.SiegeEvent != null) return false;
            if (settlement.IsVillage
                && settlement.Village.VillageState != Village.VillageStates.Normal) return false;
            return settlement.IsVillage || settlement.IsTown || settlement.IsCastle;
        }

        private static bool IsPartyEligible(MobileParty party)
            => B1071_AiRecoveryMath.IsEligible(GetBlockReasons(party));

        private static B1071_AiRecoveryBlockReason GetBlockReasons(MobileParty? party)
        {
            if (party == null) return B1071_AiRecoveryBlockReason.Inactive;

            B1071_AiRecoveryBlockReason reasons = B1071_AiRecoveryBlockReason.None;
            Hero? leader = party.LeaderHero;
            Clan? clan = party.ActualClan ?? leader?.Clan;

            if (!party.IsActive) reasons |= B1071_AiRecoveryBlockReason.Inactive;
            if (party == MobileParty.MainParty || leader == null || clan == null || clan.IsEliminated)
                reasons |= B1071_AiRecoveryBlockReason.InvalidLeader;
            if (party.Army != null) reasons |= B1071_AiRecoveryBlockReason.Army;
            if (party.MapEvent != null) reasons |= B1071_AiRecoveryBlockReason.MapEvent;
            if (party.SiegeEvent != null || party.BesiegedSettlement != null)
                reasons |= B1071_AiRecoveryBlockReason.Siege;
            if (party.IsTransitionInProgress) reasons |= B1071_AiRecoveryBlockReason.Transition;
            if (party.IsDisbanding) reasons |= B1071_AiRecoveryBlockReason.Disbanding;
            if (party.IsFleeing()) reasons |= B1071_AiRecoveryBlockReason.Retreating;
            if (party.Party.IsStarving) reasons |= B1071_AiRecoveryBlockReason.Starving;

            float urgentFoodDays = TaleWorlds.CampaignSystem.Campaign.Current?.Models.MobilePartyAIModel.NeededFoodsInDaysThresholdForSiege ?? 0f;
            if (urgentFoodDays > 0f && party.GetNumDaysForFoodToLast() < urgentFoodDays)
                reasons |= B1071_AiRecoveryBlockReason.UrgentFood;

            if (party.CurrentSettlement?.IsUnderSiege == true)
                reasons |= B1071_AiRecoveryBlockReason.BesiegedSettlement;

            if (party.DefaultBehavior != AiBehavior.Hold
                && party.DefaultBehavior != AiBehavior.None
                && party.DefaultBehavior != AiBehavior.GoToSettlement)
                reasons |= B1071_AiRecoveryBlockReason.ProtectedObjective;

            if (!party.IsLordParty
                || party.IsGarrison
                || party.IsMilitia
                || party.IsCaravan
                || party.IsVillager
                || party.IsBandit
                || party.IsPatrolParty)
                reasons |= B1071_AiRecoveryBlockReason.ExcludedPartyType;

            return reasons;
        }

        private B1071_AiRecoveryReservedSupply GetReservedSupply(
            Settlement settlement,
            string requestingPartyId,
            float nowDay)
        {
            B1071_AiRecoveryReservedSupply total = default;
            foreach (KeyValuePair<string, RecoveryReservation> entry in _reservations)
            {
                if (string.Equals(entry.Key, requestingPartyId, StringComparison.Ordinal)) continue;
                RecoveryReservation reservation = entry.Value;
                if (reservation.Settlement != settlement) continue;
                if (B1071_AiRecoveryMath.IsReservationExpired(nowDay, reservation.ExpiryDay)) continue;
                total += reservation.Supply;
            }
            return total;
        }

        /// <summary>
        /// Drops expired or orphaned reservations, then the intents of parties that will
        /// never tick again. Both removal lists are allocated only when there is something
        /// to remove: this runs for every party, every hour it thinks.
        /// </summary>
        private void CleanupRuntimeState(float nowDay)
        {
            List<string>? staleReservations = null;
            foreach (KeyValuePair<string, RecoveryReservation> entry in _reservations)
            {
                RecoveryReservation reservation = entry.Value;
                bool expired = B1071_AiRecoveryMath.IsReservationExpired(nowDay, reservation.ExpiryDay);
                bool invalid = reservation.Party == null
                    || !reservation.Party.IsActive
                    || !_intents.TryGetValue(entry.Key, out RecoveryIntent? intent)
                    || intent.Target != reservation.Settlement;
                if (expired || invalid)
                    (staleReservations ??= new List<string>()).Add(entry.Key);
            }

            if (staleReservations != null)
            {
                foreach (string partyId in staleReservations)
                    _reservations.Remove(partyId);
            }

            // A destroyed party never thinks again, so ClearPartyState would never fire for
            // it and the intent would hold the dead party for the rest of the session.
            List<string>? staleIntents = null;
            foreach (KeyValuePair<string, RecoveryIntent> entry in _intents)
            {
                RecoveryIntent intent = entry.Value;
                if (intent.Party == null || !intent.Party.IsActive || intent.Target == null)
                    (staleIntents ??= new List<string>()).Add(entry.Key);
            }

            if (staleIntents != null)
            {
                foreach (string partyId in staleIntents)
                    ClearPartyState(partyId);
            }
        }

        private void ClearPartyState(string partyId)
        {
            _intents.Remove(partyId);
            _reservations.Remove(partyId);
        }

        private void LogNativeScoreObservationOnce(MobileParty party, PartyThinkParams thinkParams)
        {
            if (_loggedNativeScoreObservation) return;
            if (!Settings.TelemetryDebugLogs && !B1071_VerboseLog.Enabled) return;

            int settlementScores = thinkParams.AIBehaviorScores.Count(entry =>
                entry.Item1.AiBehavior == AiBehavior.GoToSettlement
                && entry.Item1.Party is Settlement);
            if (settlementScores <= 0) return;

            _loggedNativeScoreObservation = true;
            Debug.Print(
                $"[Byzantium1071][Telemetry][AiRecovery] Observed {settlementScores} completed native GoToSettlement score(s) for {party.StringId} before Campaign++ adjustment; enabled={Settings.EnableAiRecoveryRouting}.");
        }
    }
}
