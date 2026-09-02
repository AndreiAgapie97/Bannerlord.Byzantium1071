using System;
using System.Collections.Generic;
using System.Linq;
using Byzantium1071.Campaign.Patches;
using Byzantium1071.Campaign.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
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

                // Advisory, not disqualifying -- see B1071_AiRecoveryMath.AdvisoryReasons.
                // It still changes how this pass behaves twice below: no drained penalty,
                // and no priority override.
                bool foodShort =
                    (blockReasons & B1071_AiRecoveryBlockReason.UrgentFood) != 0;

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
                if (foodShort) B1071_TelemetryCounters.RecordRecoveryFoodShort();

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
                    // Total counts the vanilla volunteer board, which this system never
                    // takes -- vanilla hires it on arrival. Recruiting here must therefore
                    // test only the sources Campaign++ can actually move, or a lord parked
                    // in a town whose sole supply is that board re-enters both no-op paths
                    // every campaign hour for as long as he stays.
                    if (currentQuote.Actionable > 0)
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

                // Settlements that quoted nobody. Held rather than forgotten so they can be
                // pushed down -- but only if something else can actually supply. See
                // B1071_AiRecoveryMath.DrainedCandidateScore.
                List<(AIBehaviorData Behavior, float NativeScore)>? drained = null;

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
                    if (quote.Total <= 0)
                    {
                        drained ??= new List<(AIBehaviorData, float)>();
                        drained.Add((behavior, nativeScore));
                        continue;
                    }

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

                // Something here can supply, so the settlements that cannot are now a worse
                // use of the same journey and are scored as such. Deliberately before the
                // RecoveryWins gate below: whether our own pick beats the native best is a
                // separate question from whether an empty village should be preferred to a
                // full one, and a lord who ignores our suggestion should still not be drawn
                // to the emptiest settlement on the map.
                // A lord short of food is the one case where pushing a settlement DOWN can
                // do real harm: the settlement that cannot supply him a single recruit may
                // be exactly the town vanilla scored up to sell him grain. Recruiting is
                // worth less than eating, so for him the penalty is not applied at all --
                // and because no penalty is written, the scoring bar read above is still
                // current and needs no re-read.
                //
                // A lord below vanilla's recruiting money floor is the second such case, and
                // it arrived with the floor itself. The penalty is meant to say "this
                // SETTLEMENT is empty"; for a lord who cannot afford to hire anywhere, every
                // board on the map quotes zero, so it would instead say "this LORD is poor"
                // -- a party condition written onto the map. It would not even fall evenly:
                // the castle and veteran quoters carry no such floor, so his castles keep
                // full scores while every town and village is halved, and the towns are
                // where vanilla was steering him to sell loot and raise the very gold the
                // floor demands. Skipped for the same reason and by the same shape as the
                // food case, and likewise writing no penalty, so the bar above still stands.
                if (drained != null && !foodShort
                    && !(party.LeaderHero != null
                         && IsBelowVanillaRecruitingMoneyFloor(
                                party.LeaderHero, party.LeaderHero.Gold)))
                {
                    foreach ((AIBehaviorData behavior, float nativeScore) in drained)
                    {
                        AIBehaviorData drainedBehavior = behavior;
                        thinkParams.SetBehaviorScore(
                            in drainedBehavior,
                            B1071_AiRecoveryMath.DrainedCandidateScore(nativeScore));
                    }

                    // The bar below was read BEFORE those penalties. If the settlement
                    // holding the top native score is one we just pushed down, RecoveryWins
                    // would measure our pick against a score that no longer exists and
                    // refuse it -- and an empty settlement outscoring everything is exactly
                    // the case the penalty exists for, so the fix would cancel itself.
                    // Re-read it: SetBehaviorScore updates in place (it never appends), and
                    // the penalty only ever lowers, so this is one more pass over the same
                    // list and can only move the bar down.
                    highestNativeScore = float.MinValue;
                    foreach (var scoreEntry in thinkParams.AIBehaviorScores)
                    {
                        float score = scoreEntry.Item2;
                        if (!float.IsNaN(score) && !float.IsInfinity(score))
                            highestNativeScore = Math.Max(highestNativeScore, score);
                    }

                    if (highestNativeScore == float.MinValue) return;
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

                // Recovery may SUGGEST a destination to a hungry lord, but it must never
                // OVERRIDE one. Taking priority replaces the winning score outright, which
                // would let a castle full of elites outrank the town that would have fed
                // him. Dropped to a plain comparison instead: our pick still wins if it
                // genuinely outscores the native best, and vanilla's own food bonus is part
                // of that native best, so a settlement offering both food and men still
                // comes out ahead on merit.
                bool takesPriority =
                    Settings.AiRecoveryTakesPriorityOverNewTasks && !foodShort;

                if (!B1071_AiRecoveryMath.RecoveryWins(
                        selected.AdjustedScore,
                        highestNativeScore,
                        takesPriority))
                {
                    ClearPartyState(partyId);
                    return;
                }

                float winningScore = B1071_AiRecoveryMath.FinalScore(
                    selected.AdjustedScore,
                    highestNativeScore,
                    takesPriority);
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
                    // The volunteer HEADCOUNT is not reserved: those men are vanilla's
                    // stock, and vanilla hands them to whichever lord arrives, so holding
                    // them for one would promise something this system cannot deliver. The
                    // manpower those hires will cost the settlement IS reserved -- it is
                    // folded into Quote.Manpower by QuoteSettlement, and it is the one
                    // constraint the two lords genuinely share.
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

            if (budget.Room > 0)
            {
                int volunteers = QuoteNotableVolunteers(
                    party, settlement, budget, out int volunteerManpower);

                // The manpower is carried even though the men are not: vanilla's own
                // recruitment on arrival goes through B1071_AiRecruitmentManpowerGatePatch
                // and charges the settlement pool for every volunteer it hires. Reporting
                // zero here would leave that spend out of the reservation below, and two
                // recovering lords would be quoted the same village twice over.
                quote += new B1071_AiRecoverySourceQuote(
                    0, 0, 0, volunteerManpower, volunteers);
            }

            return quote;
        }

        /// <summary>
        /// Quotes the vanilla notable volunteer board -- the recruits an AI lord actually
        /// finds in a town or village.
        ///
        /// This is the third quoter, and it exists because the first two cannot see most of
        /// the map. QuoteAiRecoveryVeterans needs the settlement to hold an entry in the
        /// demobilization veteran register, which is sparse. QuoteAiRecoveryCastleRecruits
        /// returns immediately for anything that is not a castle. Vanilla, meanwhile, keeps
        /// volunteer boards only in towns and villages -- RecruitmentCampaignBehavior's
        /// UpdateVolunteersOfNotablesInSettlement returns unless IsTown or IsVillage -- so
        /// between them the two quoters covered castles and almost nothing else, and every
        /// town and village in the world quoted zero. Routing then had nothing to say about
        /// the settlements lords actually recruit from.
        ///
        /// It counts men but never takes them. Vanilla's RecruitVolunteersFromNotable
        /// already recruits from the board when an AI party is inside the settlement, and
        /// recruiting here as well would take the same men twice. The number exists to steer
        /// the journey; the arrival stays vanilla's to handle.
        /// </summary>
        private static int QuoteNotableVolunteers(
            MobileParty party,
            Settlement settlement,
            B1071_AiRecoveryBudget budget,
            out int manpowerSpent)
        {
            manpowerSpent = 0;
            if (budget.Room <= 0) return 0;

            // Vanilla fills volunteer slots in towns and villages only, and refuses
            // recruitment at a raided settlement in OnBeforeSettlementEntered.
            if (!settlement.IsTown && !settlement.IsVillage) return 0;
            if (settlement.IsRaided || settlement.IsUnderRaid) return 0;

            Hero? leader = party.LeaderHero;
            if (leader == null) return 0;

            VolunteerModel? volunteerModel =
                TaleWorlds.CampaignSystem.Campaign.Current?.Models?.VolunteerModel;
            if (volunteerModel == null) return 0;

            // Campaign++ blocks recruitment at a settlement hosting an enemy war party, for
            // AI lords as well as the player (B1071_AiRecruitmentManpowerGatePatch). Quoting
            // past that would route a lord to a board he is turned away from on arrival.
            if (B1071_RecruitmentTierGateHelper.IsBlockedByWar(settlement, leader, out _))
                return 0;

            // Resolved once for the whole settlement. The per-troop form builds two
            // TextObjects it would then discard, and this loop runs over every slot of
            // every notable, for every candidate, every hour.
            bool hasTierCap = B1071_RecruitmentTierGateHelper.TryGetVolunteerTierCap(
                settlement, out int tierCap);

            PartyWageModel? wageModel =
                TaleWorlds.CampaignSystem.Campaign.Current?.Models?.PartyWageModel;
            B1071_ManpowerBehavior? manpower = B1071_ManpowerBehavior.Instance;

            // Everything above is structural -- wrong settlement type, raided, at war, no
            // model. What follows is affordability, and it is the only part of this quoter
            // whose effect cannot be read off any other counter: a lord refused here still
            // produces a quote, still gets scored, and looks identical to one the board
            // simply had nothing for. Counted from this line so the two rejections below
            // have an honest denominator.
            B1071_TelemetryCounters.RecordVolunteerQuoteAttempt();

            // Wages gate the vanilla pass twice: RecruitmentCampaignBehavior skips
            // RecruitVolunteersFromNotable entirely when the party is over its payment
            // limit, and refuses each individual recruit whose wage the remaining budget
            // cannot carry. A lord who cannot pay is not supplied by the fullest board.
            if (party.IsWageLimitExceeded())
            {
                B1071_TelemetryCounters.RecordVolunteerWageBlocked();
                return 0;
            }
            int wageBudget = party.GetAvailableWageBudget();

            // Wages are not the only floor -- see IsBelowVanillaRecruitingMoneyFloor.
            //
            // Tested against the leader's LIVE gold, not budget.Gold, and the difference is
            // not a detail. The budget is spent down in Campaign++ arrival order -- veterans,
            // then castle elites, then prisoners -- but vanilla does not hire in that order.
            // It hires the board from OnBeforeSettlementEntered, at the instant the party
            // crosses the gate, while every Campaign++ transfer happens on a later hourly
            // tick once the lord is already inside. So the gold vanilla actually weighs
            // against this floor is the gold he rode in with, which is what leader.Gold
            // holds. Charging the veteran register against the floor first under-quoted
            // every settlement that carries both a register entry and a board, and inflated
            // volGoldBlock with lords who were never refused.
            //
            // Per-recruit affordability below still spends budget.Gold. That is the opposite
            // question -- how many men the coin stretches to, once -- and double-spending
            // there is exactly what the shared budget exists to prevent.
            if (IsBelowVanillaRecruitingMoneyFloor(leader, leader.Gold))
            {
                B1071_TelemetryCounters.RecordVolunteerGoldBlocked();
                return 0;
            }

            int quoted = 0;

            // One small dictionary per settlement quoted, rather than one static one reused:
            // the allocation is a rounding error beside the model calls it saves, and a
            // per-call cache cannot outlive the leader and settlement its entries were
            // computed for. See the lookup below.
            var costs = new Dictionary<CharacterObject, VolunteerCost>();

            foreach (Hero notable in settlement.Notables)
            {
                if (budget.Room <= 0) break;
                if (notable == null || !notable.IsAlive || notable.VolunteerTypes == null)
                    continue;

                // Relation decides how far down a notable's board a given lord may reach.
                // Slots at or past this index are visible on the board but not his to take,
                // so counting them would quote men he cannot have.
                int reachable = Math.Min(
                    volunteerModel.MaximumIndexHeroCanRecruitFromHero(leader, notable),
                    notable.VolunteerTypes.Length);

                int takenFromNotable = 0;

                for (int slot = 0; slot < reachable && budget.Room > 0; slot++)
                {
                    CharacterObject troop = notable.VolunteerTypes[slot];
                    if (troop == null || troop.IsHero) continue;

                    // The settlement tier cap refuses this troop at recruit time, so a full
                    // slot holding one is not supply however full it looks.
                    if (hasTierCap && troop.Tier > tierCap)
                        continue;

                    // Resolved once per troop, then reused. Boards repeat heavily -- every
                    // notable of a culture offers largely the same basic tree -- and this
                    // loop now runs up to six slots per notable rather than one, so the
                    // model calls behind these four numbers multiplied by the same factor
                    // the quote did. GetTroopRecruitmentCost builds an ExplainedNumber per
                    // call, and this whole quoter runs per candidate settlement, per
                    // recovering lord, every campaign hour; the tier-cap TextObject was
                    // hoisted out of here for exactly this reason and these are dearer.
                    //
                    // Keyed by troop alone, which is only safe because the cache lives for
                    // one call: leader, settlement and party are fixed inside it, and all
                    // four figures depend on nothing else. It must not outlive the call.
                    if (!costs.TryGetValue(troop, out VolunteerCost cost))
                    {
                        int wage = wageModel?.GetCharacterWage(troop) ?? 0;
                        int gold =
                            wageModel?.GetTroopRecruitmentCost(troop, leader).RoundedResultNumber ?? 0;
                        int gate = 0;
                        int charge = 0;
                        if (manpower != null && budget.HasFiniteManpower)
                        {
                            gate = manpower.GetRecruitCostForParty(settlement, party, troop);
                            charge = manpower.GetManpowerChargePerTroop(troop);
                        }

                        cost = new VolunteerCost(wage, gold, gate, charge);
                        costs[troop] = cost;
                    }

                    int troopWage = cost.Wage;
                    if (wageBudget < troopWage) continue;

                    int goldPerMan = cost.Gold;
                    int manpowerGateCost = cost.ManpowerGate;
                    int manpowerChargeCost = cost.ManpowerCharge;

                    // One man per slot -- a volunteer slot holds exactly one recruit. The
                    // buffer multiplier is 1 because vanilla itself hires on a bare
                    // "PartyTradeGold > cost", and for a lord party PartyTradeGold IS
                    // LeaderHero.Gold, which is what the budget was built from.
                    int take = B1071_AiRecoveryMath.AffordableUnits(
                        1,
                        budget.Room,
                        budget.Gold,
                        goldPerMan,
                        goldBufferMultiplier: 1,
                        budget.Manpower,
                        manpowerGateCost);
                    if (take <= 0) continue;

                    budget.Room -= take;
                    budget.Gold = Math.Max(0, budget.Gold - goldPerMan * take);
                    if (budget.HasFiniteManpower)
                    {
                        int spent = Math.Min(budget.Manpower, manpowerChargeCost * take);
                        budget.Manpower -= spent;
                        manpowerSpent += spent;
                    }
                    wageBudget -= troopWage * take;
                    quoted += take;

                    // One man per notable PER PASS -- and vanilla makes several passes.
                    // RecruitVolunteersFromNotable does break out of its own slot loop the
                    // moment it hires one, so a notable yields at most one man per call.
                    // But OnBeforeSettlementEntered does not call it once: it loops
                    // CheckRecruiting `num` times, and for an ordinary AI lord party (not a
                    // caravan, not inside the player's army) num is 7. Treating the notable
                    // as good for a single man therefore undercounted a full board sevenfold
                    // and collapsed CandidateScore's usefulShare, so recovery routing lost
                    // races it should have won.
                    //
                    // Hero.VolunteerTypes is a six-slot array, so seven passes always outrun
                    // the board and this cap never actually fires: what the loop quotes today
                    // is every reachable slot, which is the true ceiling. The cap is written
                    // as the pass count anyway because that -- not the array length -- is the
                    // rule that makes it correct, and it is what would bind first if either
                    // number ever moved.
                    //
                    // Parties inside an army get num 1-3 instead, and are not quoted here:
                    // B1071_AiRecoveryBlockReason.Army disqualifies them before this runs.
                    if (++takenFromNotable >= B1071_AiRecoveryMath.VanillaRecruitPassesPerArrival)
                        break;
                }
            }

            return quoted;
        }

        /// <summary>
        /// What one volunteer of a given troop type costs this lord at this settlement: his
        /// wage, his recruitment price, the manpower the gate charges for him and the
        /// manpower actually deducted. Cached for the life of a single QuoteNotableVolunteers
        /// call, over which leader, settlement and party do not change.
        /// </summary>
        private readonly struct VolunteerCost
        {
            internal VolunteerCost(int wage, int gold, int manpowerGate, int manpowerCharge)
            {
                Wage = wage;
                Gold = gold;
                ManpowerGate = manpowerGate;
                ManpowerCharge = manpowerCharge;
            }

            internal int Wage { get; }
            internal int Gold { get; }
            internal int ManpowerGate { get; }
            internal int ManpowerCharge { get; }
        }

        /// <summary>
        /// Vanilla's refusal to begin recruiting at all, mirrored from
        /// RecruitmentCampaignBehavior.CheckRecruiting. <c>true</c> means the game would not
        /// sell this lord a single volunteer however full the board is.
        ///
        /// Two clauses, both vanilla's. The first is
        /// <c>HeroHelper.StartRecruitingMoneyLimit</c> -- <c>50 + min(150, manCount) * 20</c>,
        /// so a sixty-man lord needs 1,250 denars, and the floor RISES as he fills up, which
        /// makes it bite hardest on exactly the parties recovery is rebuilding. The second is
        /// the clan purse: a lord who does not lead his clan also qualifies on the clan's
        /// gold, and a generous one is exempt outright. Dropping that clause would refuse
        /// every non-leader with thin coffers and a rich clan, which is most of them.
        ///
        /// The gold to pass in is the lord's own, unspent: this is a precondition vanilla
        /// tests once on arrival, not a running balance. Callers holding a
        /// B1071_AiRecoveryBudget must not hand it budget.Gold.
        /// </summary>
        private static bool IsBelowVanillaRecruitingMoneyFloor(Hero leader, int gold)
        {
            if (leader == null) return false;

            if (gold <= Helpers.HeroHelper.StartRecruitingMoneyLimit(leader)) return true;

            return leader != leader.Clan?.Leader
                && !(leader.Clan?.Gold > Helpers.HeroHelper.StartRecruitingMoneyLimitForClanLeader(leader))
                && Helpers.TraitEffectHelper.GetTraitEffectBonus(
                       leader, TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultPersonalityTraitEffects
                           .GenerosityMercenaryRecruitmentEffect) == 0f;
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

            // Caravans, villagers, militia and bandits pour through this hook and are
            // rejected for InvalidLeader and ExcludedPartyType every hour of every day.
            // They were never recovery candidates, and counting them buries the gates that
            // matter: in the first run carrying this histogram they were 86% of every
            // rejection recorded, and the two flags they set were the top two buckets.
            const B1071_AiRecoveryBlockReason notACandidate =
                B1071_AiRecoveryBlockReason.Inactive
                | B1071_AiRecoveryBlockReason.InvalidLeader
                | B1071_AiRecoveryBlockReason.ExcludedPartyType;
            if ((reasons & notACandidate) != 0) return;
            if (!B1071_AiRecoveryMath.ShouldStart(
                    party.Party.NumberOfAllMembers,
                    party.Party.PartySizeLimit)) return;

            B1071_TelemetryCounters.RecordRecoveryBlocked(reasons);
        }

        private static bool IsPartyEligible(MobileParty party)
            => B1071_AiRecoveryMath.IsEligible(GetBlockReasons(party));

        /// <summary>
        /// Whether <paramref name="settlement"/> is the destination this system steered
        /// <paramref name="party"/> towards. Read-only, and read by B1071_TelemetryBehavior
        /// alone, so a visit can be attributed to recovery routing instead of to the dozen
        /// other reasons a lord walks into a village.
        ///
        /// It reports the INTENT, not the proposal: a proposal is a score written into
        /// PartyThinkParams that the native AI is free to ignore, while an intent is only
        /// recorded once the party actually took the destination. Counting proposals would
        /// credit this system with trips it did not cause.
        /// </summary>
        internal bool IsRecoveryDestination(MobileParty? party, Settlement? settlement)
        {
            if (party == null || settlement == null) return false;

            string partyId = party.StringId;
            if (string.IsNullOrEmpty(partyId)) return false;

            return _intents.TryGetValue(partyId, out RecoveryIntent? intent)
                && intent != null
                && intent.Target == settlement;
        }

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

            // PatrolAroundPoint is the AI's idle state, not an objective: a lord with
            // nothing to do circles a point until something needs him. Treating it as
            // protected turned away exactly the lords recovery routing exists for -- weak,
            // unoccupied, and free to go recruit. Everything else the enum can hold is a
            // real commitment (assault, raid, besiege, engage, join, escort, defend, flee)
            // and stays blocked.
            if (party.DefaultBehavior != AiBehavior.Hold
                && party.DefaultBehavior != AiBehavior.None
                && party.DefaultBehavior != AiBehavior.GoToSettlement
                && party.DefaultBehavior != AiBehavior.PatrolAroundPoint)
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
