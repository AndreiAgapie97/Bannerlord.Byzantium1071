using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    internal static class B1071_RecruitmentTierGateHelper
    {
        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        internal static bool TryGetTierGateBlock(
            Settlement? recruitmentSettlement,
            CharacterObject? troop,
            out TextObject? settlementType,
            out int tierCap)
        {
            settlementType = null;
            tierCap = 0;

            if (recruitmentSettlement == null || troop == null)
                return false;

            if (!TryGetTierCap(recruitmentSettlement, out settlementType, out tierCap))
                return false;

            return troop.Tier > tierCap;
        }

        internal static bool TryGetFirstTierGateBlock(
            Settlement? recruitmentSettlement,
            IEnumerable<CharacterObject> troops,
            out CharacterObject? blockedTroop,
            out TextObject? settlementType,
            out int tierCap)
        {
            blockedTroop = null;
            settlementType = null;
            tierCap = 0;

            if (recruitmentSettlement == null || troops == null)
                return false;

            if (!TryGetTierCap(recruitmentSettlement, out settlementType, out tierCap))
                return false;

            foreach (CharacterObject troop in troops)
            {
                if (troop == null)
                    continue;

                if (troop.Tier > tierCap)
                {
                    blockedTroop = troop;
                    return true;
                }
            }

            return false;
        }

        internal static bool SanitizeSettlementVolunteerTypes(Settlement? settlement)
        {
            if (settlement == null)
                return false;

            if (!TryGetTierCap(settlement, out _, out int tierCap))
                return false;

            bool changed = false;
            foreach (Hero notable in settlement.Notables)
            {
                if (notable == null || !notable.IsAlive || notable.VolunteerTypes == null)
                    continue;

                for (int i = 0; i < notable.VolunteerTypes.Length; i++)
                {
                    CharacterObject volunteer = notable.VolunteerTypes[i];
                    if (volunteer == null || volunteer.IsHero || volunteer.Tier <= tierCap)
                        continue;

                    CharacterObject? replacement = FindHighestAllowedAncestor(volunteer, tierCap);
                    if (replacement == volunteer)
                        continue;

                    // No ancestor was reachable by walking forward from the culture roots.
                    // Troop overhauls (De Re Militari and similar) contain branches whose
                    // root is not culture.BasicTroop/EliteBasicTroop — a branch that runs
                    // T4→T6, say — so the forward search can never reach them.
                    //
                    // Fall back progressively, and clear the slot if nothing legal exists.
                    // Clearing is safe and self-healing: vanilla
                    // RecruitmentCampaignBehavior.UpdateVolunteersOfNotablesInSettlement
                    // refills any null slot with GetBasicVolunteer(notable) on its next
                    // daily roll.
                    //
                    // Leaving the over-cap troop in place is what is NOT safe. Vanilla only
                    // ever UPGRADES a non-null slot and never clears or downgrades one, so
                    // an over-cap troop the recruit-time gate refuses can never leave the
                    // board: the slot is dead forever and the village silently fills with
                    // unrecruitable volunteers.
                    if (replacement == null)
                        replacement = FindFallbackVolunteer(volunteer, settlement, tierCap);

                    // A null replacement here deliberately empties the slot for vanilla to reseed.
                    notable.VolunteerTypes[i] = replacement;
                    changed = true;
                }
            }

            return changed;
        }

        internal static TextObject BuildSingleRecruitBlockedMessage(
            Settlement recruitmentSettlement,
            CharacterObject troop,
            TextObject settlementType,
            int tierCap)
        {
            return new TextObject("{=b1071_cr_tier_block}Volunteer cap: {SETTLEMENT} is a {TYPE}; it only provides volunteers up to tier {CAP}. {TROOP} is tier {TIER}.")
                .SetTextVariable("SETTLEMENT", recruitmentSettlement.Name)
                .SetTextVariable("TYPE", settlementType)
                .SetTextVariable("CAP", tierCap)
                .SetTextVariable("TROOP", troop.Name)
                .SetTextVariable("TIER", troop.Tier);
        }

        internal static TextObject BuildRecruitAllBlockedMessage(
            Settlement recruitmentSettlement,
            CharacterObject troop,
            TextObject settlementType,
            int tierCap)
        {
            return new TextObject("{=b1071_ui_tier_recruit_all_block}Volunteer cap: Recruit All blocked — {SETTLEMENT} is a {TYPE}; {TROOP} is tier {TIER}, cap {CAP}.")
                .SetTextVariable("SETTLEMENT", recruitmentSettlement.Name)
                .SetTextVariable("TYPE", settlementType)
                .SetTextVariable("TROOP", troop.Name)
                .SetTextVariable("TIER", troop.Tier)
                .SetTextVariable("CAP", tierCap);
        }

        internal static TextObject BuildConfirmBlockedMessage(
            Settlement recruitmentSettlement,
            CharacterObject troop,
            TextObject settlementType,
            int tierCap)
        {
            return new TextObject("{=b1071_ui_tier_confirm_block}Volunteer cap: cannot confirm — {SETTLEMENT} is a {TYPE}; {TROOP} is tier {TIER}, cap {CAP}.")
                .SetTextVariable("SETTLEMENT", recruitmentSettlement.Name)
                .SetTextVariable("TYPE", settlementType)
                .SetTextVariable("TROOP", troop.Name)
                .SetTextVariable("TIER", troop.Tier)
                .SetTextVariable("CAP", tierCap);
        }

        internal static TextObject BuildDoneHint(
            Settlement recruitmentSettlement,
            CharacterObject troop,
            TextObject settlementType,
            int tierCap)
        {
            return new TextObject("{=b1071_ui_tier_donehint}Volunteer cap: {SETTLEMENT} is a {TYPE}; {TROOP} is tier {TIER}, cap {CAP}.")
                .SetTextVariable("SETTLEMENT", recruitmentSettlement.Name)
                .SetTextVariable("TYPE", settlementType)
                .SetTextVariable("TROOP", troop.Name)
                .SetTextVariable("TIER", troop.Tier)
                .SetTextVariable("CAP", tierCap);
        }

        internal static void LogAiTierGateBlock(
            Settlement recruitmentSettlement,
            CharacterObject troop,
            TextObject settlementType,
            int tierCap,
            int amount,
            string detail)
        {
            Debug.Print(
                $"[Byzantium1071][AIRecruitmentTierGate] Blocked {detail} for {troop.Name} x{amount} at {recruitmentSettlement.Name} " +
                $"({settlementType} cap T{tierCap}, troop T{troop.Tier}).");
        }

        private static bool TryGetTierCap(
            Settlement recruitmentSettlement,
            out TextObject settlementType,
            out int tierCap)
        {
            settlementType = new TextObject(string.Empty);
            tierCap = 0;

            if (recruitmentSettlement.IsVillage)
            {
                settlementType = new TextObject("{=b1071_recruit_type_village}village");
                tierCap = Math.Max(1, Settings.VillageVolunteerTierMax);
                return true;
            }

            if (recruitmentSettlement.IsTown && !recruitmentSettlement.IsCastle)
            {
                settlementType = new TextObject("{=b1071_recruit_type_town}town");
                tierCap = Math.Max(1, Settings.TownVolunteerTierMax);
                return true;
            }

            return false;
        }

        private static CharacterObject? FindHighestAllowedAncestor(CharacterObject troop, int tierCap)
        {
            if (troop == null)
                return null;

            if (troop.Tier <= tierCap)
                return troop;

            CultureObject culture = troop.Culture;
            if (culture == null)
                return null;

            CharacterObject? best = null;
            foreach (CharacterObject root in EnumerateVolunteerRoots(culture))
            {
                CharacterObject? candidate = FindHighestAllowedAncestorOnPath(root, troop, tierCap);
                if (candidate != null && !candidate.IsHero && candidate.Tier <= tierCap)
                {
                    if (best == null || candidate.Tier > best.Tier)
                        best = candidate;
                }
            }

            return best;
        }

        private static IEnumerable<CharacterObject> EnumerateVolunteerRoots(CultureObject culture)
        {
            if (culture.BasicTroop != null)
                yield return culture.BasicTroop;

            if (culture.EliteBasicTroop != null && culture.EliteBasicTroop != culture.BasicTroop)
                yield return culture.EliteBasicTroop;
        }

        // ── Fallback chain for troops unreachable from the culture roots ──────────
        //
        // FindHighestAllowedAncestor only sees troops on a forward path from
        // culture.BasicTroop / culture.EliteBasicTroop. Overhaul mods routinely ship
        // branches that are not rooted there, and vanilla itself would trip this if a
        // culture's EliteBasicTroop ever sat above the configured cap. Every step here
        // exists so the caller always has something legal to write into the slot.

        private static CharacterObject? FindFallbackVolunteer(CharacterObject troop, Settlement settlement, int tierCap)
        {
            // 1. Walk UP the tree via a reverse parent index. This reaches branches that
            //    are disconnected from the culture roots, which the forward search cannot.
            CharacterObject? ancestor = FindHighestAllowedAncestorByReverseIndex(troop, tierCap);
            if (ancestor != null)
                return ancestor;

            // 2. The troop's own culture root, when it is itself within the cap.
            CharacterObject? root = SelectBasicRootWithinCap(troop.Culture, tierCap);
            if (root != null)
                return root;

            // 3. The settlement's culture root. Covers troops whose Culture is not the
            //    settlement's — some overhauls park troops on shared or neutral cultures.
            root = SelectBasicRootWithinCap(settlement?.Culture, tierCap);
            if (root != null)
                return root;

            // 4. Nothing legal exists. Returning null clears the slot; vanilla reseeds it.
            return null;
        }

        private static CharacterObject? SelectBasicRootWithinCap(CultureObject? culture, int tierCap)
        {
            if (culture == null)
                return null;

            CharacterObject? best = null;
            foreach (CharacterObject root in EnumerateVolunteerRoots(culture))
            {
                if (root == null || root.IsHero || root.Tier > tierCap)
                    continue;

                if (best == null || root.Tier > best.Tier)
                    best = root;
            }

            return best;
        }

        private static CharacterObject? FindHighestAllowedAncestorByReverseIndex(CharacterObject troop, int tierCap)
        {
            Dictionary<CharacterObject, List<CharacterObject>> index;
            try
            {
                index = GetTroopParentIndex();
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][TierGate] Could not build troop parent index: {ex.Message}");
                return null;
            }

            var visited = new HashSet<CharacterObject> { troop };
            var queue = new Queue<CharacterObject>();
            queue.Enqueue(troop);

            CharacterObject? best = null;
            while (queue.Count > 0)
            {
                CharacterObject current = queue.Dequeue();
                if (!index.TryGetValue(current, out List<CharacterObject>? parents))
                    continue;

                foreach (CharacterObject parent in parents)
                {
                    if (parent == null || !visited.Add(parent))
                        continue;

                    if (!parent.IsHero && parent.Tier <= tierCap)
                    {
                        if (best == null || parent.Tier > best.Tier)
                            best = parent;

                        // This ancestor already fits; anything above it is lower tier,
                        // so there is nothing better to find further up this line.
                        continue;
                    }

                    queue.Enqueue(parent);
                }
            }

            return best;
        }

        // child → parents, built once per campaign session from static troop data.
        private static Dictionary<CharacterObject, List<CharacterObject>>? _troopParentIndex;

        /// <summary>
        /// Drops the cached troop parent index. Called on session launch so a different
        /// module set (and therefore a different troop tree) never reuses a stale index.
        /// </summary>
        internal static void ResetTroopParentIndex() => _troopParentIndex = null;

        private static Dictionary<CharacterObject, List<CharacterObject>> GetTroopParentIndex()
        {
            if (_troopParentIndex != null)
                return _troopParentIndex;

            var index = new Dictionary<CharacterObject, List<CharacterObject>>();
            foreach (CharacterObject character in CharacterObject.All)
            {
                if (character?.UpgradeTargets == null)
                    continue;

                foreach (CharacterObject child in character.UpgradeTargets)
                {
                    if (child == null)
                        continue;

                    if (!index.TryGetValue(child, out List<CharacterObject>? parents))
                    {
                        parents = new List<CharacterObject>();
                        index[child] = parents;
                    }

                    parents.Add(character);
                }
            }

            _troopParentIndex = index;
            return index;
        }

        private static CharacterObject? FindHighestAllowedAncestorOnPath(CharacterObject root, CharacterObject target, int tierCap)
        {
            var visited = new HashSet<CharacterObject>();
            var parents = new Dictionary<CharacterObject, CharacterObject?>();
            var queue = new Queue<CharacterObject>();

            queue.Enqueue(root);
            visited.Add(root);
            parents[root] = null;

            while (queue.Count > 0)
            {
                CharacterObject current = queue.Dequeue();
                if (current == target)
                    return SelectHighestAllowedFromParentChain(current, parents, tierCap);

                if (current.UpgradeTargets == null)
                    continue;

                foreach (CharacterObject next in current.UpgradeTargets)
                {
                    if (next == null || visited.Contains(next))
                        continue;

                    visited.Add(next);
                    parents[next] = current;
                    queue.Enqueue(next);
                }
            }

            return null;
        }

        private static CharacterObject? SelectHighestAllowedFromParentChain(
            CharacterObject current,
            Dictionary<CharacterObject, CharacterObject?> parents,
            int tierCap)
        {
            CharacterObject? best = null;
            CharacterObject? cursor = current;
            while (cursor != null)
            {
                if (!cursor.IsHero && cursor.Tier <= tierCap)
                {
                    if (best == null || cursor.Tier > best.Tier)
                        best = cursor;
                }

                parents.TryGetValue(cursor, out CharacterObject? parent);
                cursor = parent;
            }

            return best;
        }

        /// <summary>
        /// Villagers will not take service under the banner burning their fields.
        /// Recruiting is refused outright in any settlement belonging to a faction the
        /// recruiter's realm is at war with.
        ///
        /// In practice this closes the enemy VILLAGE route. Hostile towns and castles
        /// cannot be entered at all, but enemy villages can, and touring them was free
        /// manpower: the player refilled from the very pools their war was draining while
        /// their own settlements stayed untouched. That asymmetry is what made the
        /// manpower system a net player advantage rather than a constraint.
        ///
        /// This is the ACCESS half of the recruiting-abroad gate; the gold premium in
        /// B1071_ArmyEconomicsPatch is the price half, for realms merely at peace.
        /// Manpower cost is deliberately left alone in both: manpower belongs to the
        /// settlement, not to the recruiter, so charging more of it would punish the
        /// settlement being recruited from instead of the lord doing the recruiting.
        ///
        /// Exempt only when the clan has no kingdom AND no fief, matching the gold premium:
        /// locking the early-game player out of every realm at war would be punishing. A
        /// landless vassal or mercenary is not exempt - their realm is the kingdom they
        /// serve, and its wars are their wars.
        /// </summary>
        internal static bool IsBlockedByWar(Settlement? settlement, Hero? buyer, out TextObject? hostFactionName)
        {
            hostFactionName = null;

            if (settlement == null || buyer?.Clan == null)
                return false;

            if (!Settings.BlockEnemyRealmRecruitment)
                return false;

            if (buyer.Clan.Kingdom == null &&
                (buyer.Clan.Settlements == null || buyer.Clan.Settlements.Count == 0))
                return false;

            IFaction? hostFaction = settlement.OwnerClan?.MapFaction;
            IFaction? buyerFaction = buyer.MapFaction;
            if (hostFaction == null || buyerFaction == null || hostFaction == buyerFaction)
                return false;

            if (!FactionManager.IsAtWarAgainstFaction(hostFaction, buyerFaction))
                return false;

            hostFactionName = hostFaction.Name;
            return true;
        }

        internal static TextObject BuildWarBlockedMessage(Settlement settlement, TextObject hostFactionName)
        {
            TextObject msg = new TextObject("{=b1071_ui_war_recruit_block}{SETTLEMENT} will not raise men for you - your realm is at war with {FACTION}.");
            msg.SetTextVariable("SETTLEMENT", settlement.Name);
            msg.SetTextVariable("FACTION", hostFactionName);
            return msg;
        }
    }
}