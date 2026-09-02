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

        /// <summary>
        /// Caught faults tolerated in <see cref="OnAiHourlyTick"/> before the pass stops for
        /// the session. Five is enough to ride out something incidental and low enough that a
        /// systemic fault costs five log lines instead of one per party per campaign hour.
        /// </summary>
        private const int MaxHandlerFailures = 5;

        internal static B1071_AiRecoveryBehavior? Instance { get; set; }

        private static IB1071Settings Settings
            => B1071_TestHooks.Settings ?? B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        private sealed class RecoveryIntent
        {
            internal MobileParty Party = null!;
            internal Settlement Target = null!;
            internal float ExpiryDay;
        }

        private sealed class RecoveryProposal
        {
            internal MobileParty Party = null!;
            internal Settlement Target = null!;
            internal float ExpiryDay;
        }

        private sealed class RecoveryReservation
        {
            internal MobileParty Party = null!;
            internal Settlement Settlement = null!;
            internal Settlement? ManpowerPool;
            internal B1071_AiRecoveryReservedSupply Supply;
            internal float ExpiryDay;
        }

        private readonly struct RecoveryCandidate
        {
            internal AIBehaviorData Behavior { get; }
            internal Settlement Settlement { get; }
            internal Settlement? ManpowerPool { get; }
            internal float AdjustedScore { get; }
            internal B1071_AiRecoverySourceQuote Quote { get; }

            internal RecoveryCandidate(
                AIBehaviorData behavior,
                Settlement settlement,
                Settlement? manpowerPool,
                float adjustedScore,
                B1071_AiRecoverySourceQuote quote)
            {
                Behavior = behavior;
                Settlement = settlement;
                ManpowerPool = manpowerPool;
                AdjustedScore = adjustedScore;
                Quote = quote;
            }
        }

        private readonly Dictionary<string, RecoveryIntent> _intents
            = new Dictionary<string, RecoveryIntent>(StringComparer.Ordinal);
        private readonly Dictionary<string, RecoveryProposal> _proposals
            = new Dictionary<string, RecoveryProposal>(StringComparer.Ordinal);
        private readonly Dictionary<string, RecoveryReservation> _reservations
            = new Dictionary<string, RecoveryReservation>(StringComparer.Ordinal);
        private List<string>? _savedIntentPartyIds;
        private List<string>? _savedIntentTargetIds;
        private List<float>? _savedIntentExpiryDays;
        private bool _loggedNativeScoreObservation;
        private bool _pendingLoadReconstruction;

        /// <summary>
        /// Session-only fuse. <see cref="OnAiHourlyTick"/> runs once per AI lord party per
        /// campaign hour, so a systemic fault — a game update moving a member this pass
        /// reads — throws hundreds of times a day. Once <see cref="MaxHandlerFailures"/>
        /// faults are caught the pass stops scoring for the rest of the session and parties
        /// fall back to Bannerlord's own settlement choice, which is exactly the behavior with
        /// the setting switched off. Not persisted: reloading retries.
        /// </summary>
        private int _handlerFailures;
        private bool _disabledThisSession;

        /// <summary>
        /// Re-entrancy latch. The pass recruits into the party roster and the recruitment
        /// behaviors re-anchor the party with <c>RecalculateShortTermBehavior</c>, which drives
        /// the very party AI this handler is running inside. Should that ever feed back into
        /// the think event, the nested call is skipped rather than recursing inside a native
        /// callback. Skipping costs that party one hour of scoring.
        /// </summary>
        private bool _inRecoveryPass;

        public override void RegisterEvents()
        {
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, OnAiHourlyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, OnAfterSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            _savedIntentPartyIds ??= new List<string>();
            _savedIntentTargetIds ??= new List<string>();
            _savedIntentExpiryDays ??= new List<float>();

            if (!dataStore.IsLoading)
            {
                _savedIntentPartyIds.Clear();
                _savedIntentTargetIds.Clear();
                _savedIntentExpiryDays.Clear();

                // Deciding what to persist reads live parties, and this runs inside the
                // player's save. The three lists are only ever appended together, so aborting
                // the walk can shorten them but never leave them unequal — and losing this
                // session's journeys is a far better outcome than a save that fails.
                try
                {
                    if (Settings.EnableAiRecoveryRouting)
                    {
                        foreach (KeyValuePair<string, RecoveryIntent> entry in _intents
                            .OrderBy(entry => entry.Key, StringComparer.Ordinal))
                        {
                            string targetId = entry.Value.Target?.StringId ?? string.Empty;
                            if (string.IsNullOrEmpty(entry.Key) || string.IsNullOrEmpty(targetId)) continue;
                            if (!IsConfirmedDestination(entry.Value.Party, entry.Value.Target)) continue;
                            _savedIntentPartyIds.Add(entry.Key);
                            _savedIntentTargetIds.Add(targetId);
                            _savedIntentExpiryDays.Add(entry.Value.ExpiryDay);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _savedIntentPartyIds.Clear();
                    _savedIntentTargetIds.Clear();
                    _savedIntentExpiryDays.Clear();
                    Debug.Print($"[Byzantium1071][AiRecovery] Intents not saved: {ex.GetType().Name}: {ex.Message}");
                }
            }

            dataStore.SyncData("b1071_aiRecoveryPartyIds", ref _savedIntentPartyIds);
            dataStore.SyncData("b1071_aiRecoveryTargetIds", ref _savedIntentTargetIds);
            dataStore.SyncData("b1071_aiRecoveryExpiryDays", ref _savedIntentExpiryDays);

            _savedIntentPartyIds ??= new List<string>();
            _savedIntentTargetIds ??= new List<string>();
            _savedIntentExpiryDays ??= new List<float>();
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
            _handlerFailures = 0;
            _disabledThisSession = false;
            _inRecoveryPass = false;
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
        /// Restores only recovery intents explicitly recorded in the save. Reservations and
        /// quotes are recalculated on the party's next AI tick.
        /// </summary>
        private void OnAfterSessionLaunched(CampaignGameStarter starter)
        {
            if (!_pendingLoadReconstruction) return;
            _pendingLoadReconstruction = false;

            try
            {
                ClearRuntimeState();
                if (!Settings.EnableAiRecoveryRouting) return;

                var partiesById = MobileParty.AllLordParties
                    .Where(party => party != null && !string.IsNullOrEmpty(party.StringId))
                    .GroupBy(party => party.StringId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                var settlementsById = Settlement.All
                    .Where(settlement => settlement != null && !string.IsNullOrEmpty(settlement.StringId))
                    .GroupBy(settlement => settlement.StringId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

                float nowDay = (float)CampaignTime.Now.ToDays;
                int count = Math.Min(
                    Math.Min(_savedIntentPartyIds?.Count ?? 0, _savedIntentTargetIds?.Count ?? 0),
                    _savedIntentExpiryDays?.Count ?? 0);
                for (int i = 0; i < count; i++)
                {
                    string partyId = _savedIntentPartyIds![i];
                    string targetId = _savedIntentTargetIds![i];
                    if (!partiesById.TryGetValue(partyId, out MobileParty? party)) continue;
                    if (!settlementsById.TryGetValue(targetId, out Settlement? target)) continue;
                    if (!IsPartyEligible(party)) continue;
                    if (B1071_AiRecoveryMath.HasReachedStop(
                            party.Party.NumberOfAllMembers,
                            party.Party.PartySizeLimit))
                        continue;
                    float expiryDay = _savedIntentExpiryDays![i];
                    if (B1071_AiRecoveryMath.IsIntentExpired(nowDay, expiryDay)) continue;
                    if (!IsCandidateSettlement(party, target)) continue;
                    if (!IsConfirmedDestination(party, target)) continue;

                    _intents[partyId] = new RecoveryIntent
                    {
                        Party = party,
                        Target = target,
                        ExpiryDay = expiryDay
                    };
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
            if (_disabledThisSession || _inRecoveryPass) return;

            _inRecoveryPass = true;
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

                // Resolved once and reused: IsPartyEligible is exactly
                // IsEligible(GetBlockReasons(party)), and the reasons are needed either way
                // for the telemetry histogram below.
                B1071_AiRecoveryBlockReason blockReasons = GetBlockReasons(party);
                if (!B1071_AiRecoveryMath.IsEligible(blockReasons))
                {
                    RecordBlockedWeakParty(party, blockReasons);
                    ClearPartyState(partyId);
                    return;
                }

                ConfirmOrDiscardProposal(partyId, party, nowDay);

                int members = party.Party.NumberOfAllMembers;
                int limit = party.Party.PartySizeLimit;
                bool hasIntent = _intents.TryGetValue(partyId, out RecoveryIntent? intent);
                if (hasIntent && !IsConfirmedDestination(party, intent!.Target))
                {
                    _intents.Remove(partyId);
                    _reservations.Remove(partyId);
                    hasIntent = false;
                    intent = null;
                }
                if (hasIntent && B1071_AiRecoveryMath.IsIntentExpired(nowDay, intent!.ExpiryDay))
                {
                    ClearPartyState(partyId);
                    return;
                }
                if (hasIntent && B1071_AiRecoveryMath.HasReachedStop(members, limit))
                {
                    ClearPartyState(partyId);
                    return;
                }

                if (!hasIntent && !B1071_AiRecoveryMath.ShouldStart(members, limit))
                    return;

                // Party-hours in recovery, not distinct parties: this runs hourly for as long
                // as a lord stays below the stop threshold.
                B1071_TelemetryCounters.RecordRecoveryEligible();

                int missing = B1071_AiRecoveryMath.MissingToStop(members, limit);
                if (missing <= 0)
                {
                    ClearPartyState(partyId);
                    return;
                }

                // A lord already standing in the settlement draws on it before anyone still
                // travelling there. A destination claim steers routing; it holds no stock, so
                // it must not turn a lord away from the castle he is inside. What another
                // lord has claimed here is still subtracted from the quote below.
                Settlement? currentSettlement = party.CurrentSettlement;
                if (currentSettlement != null
                    && IsCandidateSettlement(party, currentSettlement))
                {
                    Settlement? currentManpowerPool = B1071_ManpowerBehavior.Instance?
                        .GetManpowerPoolSettlement(currentSettlement);
                    B1071_AiRecoveryReservedSupply currentReserved = GetReservedSupply(
                        currentSettlement,
                        currentManpowerPool,
                        partyId,
                        nowDay);
                    B1071_AiRecoverySourceQuote currentQuote = QuoteSettlement(
                        party,
                        currentSettlement,
                        missing,
                        currentReserved);
                    if (currentQuote.Total > 0)
                    {
                        int before = party.Party.NumberOfAllMembers;
                        RecruitAtCurrentSettlement(party, currentSettlement);
                        members = party.Party.NumberOfAllMembers;
                        if (members > before)
                        {
                            SetConfirmedIntent(partyId, party, currentSettlement, nowDay);
                            hasIntent = true;
                            intent = _intents[partyId];
                        }

                        if (B1071_AiRecoveryMath.HasReachedStop(members, limit))
                        {
                            ClearPartyState(partyId);
                            return;
                        }

                        missing = B1071_AiRecoveryMath.MissingToStop(members, limit);
                    }
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
                    if (settlement == currentSettlement) continue;
                    if (nativeScore <= 0f || float.IsNaN(nativeScore) || float.IsInfinity(nativeScore)) continue;
                    if (!IsCandidateSettlement(party, settlement)) continue;
                    if (IsDestinationReserved(settlement, partyId, nowDay)) continue;

                    Settlement? manpowerPool = B1071_ManpowerBehavior.Instance?
                        .GetManpowerPoolSettlement(settlement);
                    B1071_AiRecoveryReservedSupply reserved = GetReservedSupply(
                        settlement,
                        manpowerPool,
                        partyId,
                        nowDay);
                    B1071_AiRecoverySourceQuote quote = QuoteSettlement(
                        party,
                        settlement,
                        missing,
                        reserved);
                    // Recorded before the drop so a settlement that can supply nothing is
                    // counted rather than vanishing -- that is the population an A3 penalty
                    // would act on.
                    B1071_TelemetryCounters.RecordRecoveryQuote(quote.Total);
                    if (quote.Total <= 0) continue;

                    candidates.Add(new RecoveryCandidate(
                        behavior,
                        settlement,
                        manpowerPool,
                        B1071_AiRecoveryMath.CandidateScore(nativeScore, quote.Total, missing),
                        quote));
                }

                if (candidates.Count == 0)
                {
                    // Supply can disappear temporarily while an active recovery is between
                    // 60% and 80%. Drop any unconfirmed promise, but retain a confirmed intent
                    // until its configured deadline.
                    _proposals.Remove(partyId);
                    _reservations.Remove(partyId);
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

                if (!B1071_AiRecoveryMath.RecoveryWins(
                        selected.AdjustedScore,
                        highestNativeScore,
                        Settings.AiRecoveryTakesPriorityOverNewTasks))
                {
                    ClearPartyState(partyId);
                    return;
                }

                float winningScore = B1071_AiRecoveryMath.FinalScore(
                    selected.AdjustedScore,
                    highestNativeScore,
                    Settings.AiRecoveryTakesPriorityOverNewTasks);
                AIBehaviorData winningBehavior = selected.Behavior;
                thinkParams.SetBehaviorScore(in winningBehavior, winningScore);
                B1071_TelemetryCounters.RecordRecoveryProposed();

                if (IsConfirmedDestination(party, selected.Settlement))
                {
                    if (!hasIntent || intent!.Target != selected.Settlement)
                        SetConfirmedIntent(partyId, party, selected.Settlement, nowDay);
                    _proposals.Remove(partyId);
                }
                else
                {
                    _proposals[partyId] = new RecoveryProposal
                    {
                        Party = party,
                        Target = selected.Settlement,
                        ExpiryDay = nowDay + ReservationDurationDays
                    };
                }
                _reservations[partyId] = new RecoveryReservation
                {
                    Party = party,
                    Settlement = selected.Settlement,
                    ManpowerPool = selected.ManpowerPool,
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
                NoteHandlerFailure(ex);
            }
            finally
            {
                _inRecoveryPass = false;
            }
        }

        /// <summary>
        /// Logs one caught fault from the hourly pass and blows the session fuse once they
        /// reach <see cref="MaxHandlerFailures"/>. Reservations and intents are dropped with
        /// it so nothing left behind keeps subtracting from quotes the pass no longer makes.
        /// </summary>
        private void NoteHandlerFailure(Exception ex)
        {
            _handlerFailures++;
            Debug.Print($"[Byzantium1071][AiRecovery] Score extension skipped "
                + $"({_handlerFailures}/{MaxHandlerFailures}): {ex.GetType().Name}: {ex.Message}");

            if (_handlerFailures < MaxHandlerFailures) return;

            _disabledThisSession = true;
            ClearRuntimeState();
            Debug.Print("[Byzantium1071][AiRecovery] Stopped for this session after repeated faults. "
                + "Lords revert to Bannerlord's own settlement scoring; reload to retry.");
        }

        /// <summary>
        /// Confirms a matching proposal and releases this party's reservation once recruitment
        /// has actually happened. The recruitment behaviors call this from inside their own
        /// passes — including the daily castle tick, which carries no try/catch of its own
        /// — so a fault here is caught rather than escaping into a caller that predates
        /// this feature.
        /// </summary>
        internal void NotifyRecruitment(MobileParty party, Settlement settlement)
        {
            try
            {
                if (party == null || settlement == null || string.IsNullOrEmpty(party.StringId)) return;
                if (_proposals.TryGetValue(party.StringId, out RecoveryProposal? proposal)
                    && proposal.Target == settlement
                    && IsConfirmedDestination(party, settlement))
                {
                    SetConfirmedIntent(
                        party.StringId,
                        party,
                        settlement,
                        (float)CampaignTime.Now.ToDays);
                }
                if (_reservations.TryGetValue(party.StringId, out RecoveryReservation? reservation)
                    && reservation.Settlement == settlement)
                    _reservations.Remove(party.StringId);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][AiRecovery] Recruitment notice skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal void ClearRuntimeState()
        {
            _intents.Clear();
            _proposals.Clear();
            _reservations.Clear();
        }

        private static void RecruitAtCurrentSettlement(MobileParty party, Settlement settlement)
        {
            B1071_DemobilizationBehavior.Instance?.TryAiHireVeteransAtCurrentSettlement(party, settlement);
            B1071_CastleRecruitmentBehavior.Instance?.TryAiAutoRecruitOnArrival(party, settlement);
        }

        private void ConfirmOrDiscardProposal(string partyId, MobileParty party, float nowDay)
        {
            if (!_proposals.TryGetValue(partyId, out RecoveryProposal? proposal)) return;

            if (!B1071_AiRecoveryMath.IsReservationExpired(nowDay, proposal.ExpiryDay)
                && IsConfirmedDestination(party, proposal.Target))
            {
                SetConfirmedIntent(partyId, party, proposal.Target, nowDay);
            }

            _proposals.Remove(partyId);
            _reservations.Remove(partyId);
        }

        private void SetConfirmedIntent(
            string partyId,
            MobileParty party,
            Settlement target,
            float nowDay)
        {
            if (_intents.TryGetValue(partyId, out RecoveryIntent? existing)
                && existing.Target == target
                && !B1071_AiRecoveryMath.IsIntentExpired(nowDay, existing.ExpiryDay))
                return;

            // Past the guard above this is a genuine new or changed commitment, not the same
            // intent being re-affirmed hour after hour.
            B1071_TelemetryCounters.RecordRecoveryConfirmed();

            _intents[partyId] = new RecoveryIntent
            {
                Party = party,
                Target = target,
                ExpiryDay = B1071_AiRecoveryMath.IntentExpiryDay(
                    nowDay,
                    Settings.AiRecoveryIntentDurationDays)
            };
        }

        private static bool IsConfirmedDestination(MobileParty? party, Settlement? target)
            => party != null
               && target != null
               && (party.CurrentSettlement == target
                   || (party.DefaultBehavior == AiBehavior.GoToSettlement
                       && party.TargetSettlement == target));

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

        /// <summary>
        /// Records why a lord who NEEDED recovery never got a pass. Restricted to parties
        /// below the start threshold on purpose: a healthy lord being turned away is not a
        /// missed recovery, and counting those would bury the signal under every garrison
        /// commander and caravan escort on the map.
        /// </summary>
        private static void RecordBlockedWeakParty(MobileParty party, B1071_AiRecoveryBlockReason reasons)
        {
            // Checked before touching the party so a normal session does none of this work.
            if (!B1071_TelemetryCounters.Enabled) return;
            if (!B1071_AiRecoveryMath.ShouldStart(
                    party.Party.NumberOfAllMembers,
                    party.Party.PartySizeLimit)) return;

            B1071_TelemetryCounters.RecordRecoveryBlocked(reasons);
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
            if (party.IsCurrentlyUsedByAQuest) reasons |= B1071_AiRecoveryBlockReason.Quest;
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
            Settlement? manpowerPool,
            string requestingPartyId,
            float nowDay)
        {
            B1071_AiRecoveryReservedSupply total = default;
            foreach (KeyValuePair<string, RecoveryReservation> entry in _reservations)
            {
                if (string.Equals(entry.Key, requestingPartyId, StringComparison.Ordinal)) continue;
                RecoveryReservation reservation = entry.Value;
                if (B1071_AiRecoveryMath.IsReservationExpired(nowDay, reservation.ExpiryDay)) continue;

                // Veterans, elites and prisoners are the stock of one settlement, so only a
                // claim on this settlement spends them. Manpower is pooled, so a claim on a
                // bound village spends the same points as its town. Both must be subtracted
                // or two lords are quoted the same men.
                bool sameSettlement = reservation.Settlement == settlement;
                bool sameManpowerPool = manpowerPool != null
                    && reservation.ManpowerPool == manpowerPool;
                if (!sameSettlement && !sameManpowerPool) continue;

                total += reservation.Supply.ForCandidate(sameSettlement, sameManpowerPool);
            }
            return total;
        }

        private bool IsDestinationReserved(Settlement settlement, string requestingPartyId, float nowDay)
        {
            foreach (KeyValuePair<string, RecoveryReservation> entry in _reservations)
            {
                if (string.Equals(entry.Key, requestingPartyId, StringComparison.Ordinal)) continue;
                RecoveryReservation reservation = entry.Value;
                if (!B1071_AiRecoveryMath.IsReservationExpired(nowDay, reservation.ExpiryDay)
                    && reservation.Settlement == settlement)
                    return true;
            }

            return false;
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
                bool hasMatchingIntent = _intents.TryGetValue(entry.Key, out RecoveryIntent? intent)
                    && intent.Target == reservation.Settlement;
                bool hasMatchingProposal = _proposals.TryGetValue(entry.Key, out RecoveryProposal? proposal)
                    && proposal.Target == reservation.Settlement;
                bool invalid = reservation.Party == null
                    || !reservation.Party.IsActive
                    || (!hasMatchingIntent && !hasMatchingProposal);
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
                if (intent.Party == null
                    || !intent.Party.IsActive
                    || intent.Target == null
                    || B1071_AiRecoveryMath.IsIntentExpired(nowDay, intent.ExpiryDay))
                    (staleIntents ??= new List<string>()).Add(entry.Key);
            }

            if (staleIntents != null)
            {
                foreach (string partyId in staleIntents)
                    ClearPartyState(partyId);
            }

            List<string>? staleProposals = null;
            foreach (KeyValuePair<string, RecoveryProposal> entry in _proposals)
            {
                RecoveryProposal proposal = entry.Value;
                if (proposal.Party == null
                    || !proposal.Party.IsActive
                    || proposal.Target == null
                    || B1071_AiRecoveryMath.IsReservationExpired(nowDay, proposal.ExpiryDay))
                    (staleProposals ??= new List<string>()).Add(entry.Key);
            }

            if (staleProposals != null)
            {
                foreach (string partyId in staleProposals)
                {
                    _proposals.Remove(partyId);
                    _reservations.Remove(partyId);
                }
            }
        }

        private void ClearPartyState(string partyId)
        {
            _intents.Remove(partyId);
            _proposals.Remove(partyId);
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
