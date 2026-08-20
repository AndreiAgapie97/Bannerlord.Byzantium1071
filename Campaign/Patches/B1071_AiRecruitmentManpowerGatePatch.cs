using Byzantium1071.Campaign.Behaviors;
using Byzantium1071.Campaign.Settings;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Byzantium1071.Campaign.Patches
{
    // NOTE: "ApplyInternal" is a private method in RecruitmentCampaignBehavior; nameof() cannot be used.
    // If Bannerlord renames this method in a future patch, this Harmony patch will silently not apply.
    // Verified against v1.4.8 TaleWorlds.CampaignSystem.dll (signature unchanged since v1.3.15). Re-verify after game updates.
    [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "ApplyInternal")]
    public static class B1071_AiRecruitmentManpowerGatePatch
    {
        /// <summary>
        /// True only while a tavern-mercenary hire is being applied. ApplyInternal fires
        /// OnTroopRecruited synchronously before it returns, so B1071_ManpowerBehavior can
        /// read this to tell tavern hires apart from levy recruitment and skip consuming the
        /// settlement pool. Cleared by the finalizer even if ApplyInternal throws.
        /// </summary>
        internal static bool IsProcessingTavernMercenary { get; private set; }

        private static bool Prefix(
            MobileParty side1Party,
            Settlement settlement,
            Hero individual,
            CharacterObject troop,
            int number,
            int bitCode,
            RecruitmentCampaignBehavior.RecruitingDetail detail)
        {
            try
            {
                if (side1Party == null || troop == null || number <= 0)
                    return true;

                // Tavern mercenaries are wandering soldiers being hired, not levies raised
                // from the local population, so they neither consume nor are blocked by the
                // settlement manpower pool. This must be checked before the manpower gate
                // below: gating it made the tavern hire dialogue silently no-op (no gold
                // taken, no troops added) once a town's pool ran dry mid-campaign, which is
                // exactly when a long war drains it. The tier gate below already exempts
                // this path implicitly, because tavern hires pass individual == null.
                if (detail == RecruitmentCampaignBehavior.RecruitingDetail.MercenaryFromTavern)
                {
                    // Flag it so the manpower behaviour's OnTroopRecruited listener — which
                    // ApplyInternal fires on its way out — skips consumption for AI parties too.
                    // Without this the pool would still be drained by the very hires we just
                    // declared exempt (the player path already returns early on its own).
                    IsProcessingTavernMercenary = true;
                    return true;
                }

                B1071_ManpowerBehavior? behavior = B1071_ManpowerBehavior.Instance;
                if (behavior == null)
                    return true;

                Settlement? recruitmentSettlement = settlement
                                                  ?? individual?.CurrentSettlement
                                                  ?? side1Party.CurrentSettlement;
                if (recruitmentSettlement == null)
                    return true;

                // Tier caps apply only to volunteer-board recruitment, where the notable's current
                // settlement identifies the actual source board. Do not apply these caps to broader
                // RecruitmentCampaignBehavior paths such as tavern mercenaries or other non-notable recruits.
                Settlement? volunteerSourceSettlement = individual?.CurrentSettlement ?? settlement;
                if (individual != null
                    && volunteerSourceSettlement != null
                    && B1071_RecruitmentTierGateHelper.TryGetTierGateBlock(
                        volunteerSourceSettlement,
                        troop,
                        out TextObject? settlementType,
                        out int tierCap))
                {
                    bool isMainParty = side1Party == MobileParty.MainParty;
                    if (isMainParty)
                    {
                        TextObject tierMsg = B1071_RecruitmentTierGateHelper.BuildSingleRecruitBlockedMessage(
                            volunteerSourceSettlement,
                            troop,
                            settlementType!,
                            tierCap);

                        InformationManager.DisplayMessage(new InformationMessage(tierMsg.ToString(), Colors.Yellow));
                    }
                    else if (B1071_McmSettings.Instance?.LogAiManpowerConsumption == true
                        || B1071_VerboseLog.Enabled)
                    {
                        B1071_RecruitmentTierGateHelper.LogAiTierGateBlock(
                            volunteerSourceSettlement,
                            troop,
                            settlementType!,
                            tierCap,
                            number,
                            detail.ToString());
                    }

                    return false;
                }

                if (behavior.CanRecruitCountForPlayer(
                        recruitmentSettlement,
                        side1Party,
                        troop,
                        amount: number,
                        out int available,
                        out int costPer,
                        out Settlement? pool))
                {
                    return true;
                }

                bool isPlayer = side1Party == MobileParty.MainParty;
                string poolName = pool?.Name?.ToString() ?? "pool";
                string troopName = troop.Name?.ToString() ?? "troop";
                int required = Math.Max(1, costPer) * Math.Max(1, number);

                if (isPlayer)
                {
                    TextObject msg = new TextObject("{=b1071_cr_manpower_block}Manpower: cannot recruit {TROOP} — {POOL} needs {COST}, only {LEFT} left.")
                        .SetTextVariable("TROOP", troopName)
                        .SetTextVariable("POOL", poolName)
                        .SetTextVariable("COST", required)
                        .SetTextVariable("LEFT", available);

                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Yellow));
                }
                else if (B1071_McmSettings.Instance?.LogAiManpowerConsumption == true
                    || B1071_VerboseLog.Enabled)
                {
                    Debug.Print(
                        $"[Byzantium1071][AIManpowerGate] Blocked {detail} for {troopName} x{number} at {poolName}. Need {required}, available {available}.");
                }

                return false;
            }
            catch (Exception ex) { TaleWorlds.Library.Debug.Print($"[Byzantium1071] AiRecruitmentManpowerGatePatch error: {ex}"); return true; }
        }

        // Runs after ApplyInternal whether it completed or threw, so the flag can never leak
        // into the next recruitment and silently exempt it.
        private static void Finalizer()
        {
            IsProcessingTavernMercenary = false;
        }
    }
}
