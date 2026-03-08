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
    }
}