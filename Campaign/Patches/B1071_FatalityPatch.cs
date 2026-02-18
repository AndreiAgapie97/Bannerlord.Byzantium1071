using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.TroopSuppliers;
using TaleWorlds.Core;

namespace Byzantium1071.Campaign.Patches
{
    [HarmonyPatch(typeof(PartyGroupTroopSupplier), "OnTroopScoreHit")]
    public static class B1071_FatalityPatch
    {
        /// <summary>
        /// Uses named HarmonyArgument bindings for version resilience.
        /// If TaleWorlds reorders or renames parameters, the patch will fail
        /// visibly at load rather than silently binding to the wrong arg.
        /// </summary>
        public static void Prefix(
            [HarmonyArgument("attackedCharacter")] BasicCharacterObject attackedCharacter,
            [HarmonyArgument("isFatal")] ref bool isFatal)
        {
            if (attackedCharacter == null || !isFatal) return;

            int tier = 1;

            if (attackedCharacter is CharacterObject co)
                tier = co.Tier;
            else
                tier = attackedCharacter.GetBattleTier();

            tier = tier < 1 ? 1 : tier;

            // Tier curve: T1=0%, T2=5%, T3=10%, T4=15%, T5=20%, T6+=25%
            float saveChance = (tier - 1) * 0.05f;
            if (saveChance > 0.25f) saveChance = 0.25f;

            if (MBRandom.RandomFloat < saveChance)
                isFatal = false;
        }
    }
}
