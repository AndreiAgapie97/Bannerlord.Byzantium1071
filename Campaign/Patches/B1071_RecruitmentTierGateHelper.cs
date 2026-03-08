using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
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
    }
}