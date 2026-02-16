using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.TroopSuppliers;
using TaleWorlds.Core;

namespace Byzantium1071.Campaign.Patches
{
    // Signature observed in callstacks: PartyGroupTroopSupplier.OnTroopScoreHit(..., bool isFatal, ...) :contentReference[oaicite:6]{index=6}
    [HarmonyPatch(typeof(PartyGroupTroopSupplier), "OnTroopScoreHit")]
    public static class B1071_FatalityPatch
    {
        /// <summary>
        /// Harmony argument indices:
        /// __1 = attackedCharacter (BasicCharacterObject)
        /// __3 = isFatal (bool)
        /// </summary>
        public static void Prefix(BasicCharacterObject __1, ref bool __3)
        {
            if (!__3) return; // only intervene when it's currently fatal

            int tier = 1;

            // If it’s a troop (CharacterObject), use Tier directly. :contentReference[oaicite:7]{index=7}
            if (__1 is CharacterObject co)
                tier = co.Tier;
            else
                tier = __1.GetBattleTier();

            tier = tier < 1 ? 1 : tier;

            // Example curve:
            // Tier1: 0% save
            // Tier2: 5%
            // Tier3: 10%
            // Tier4: 15%
            // Tier5: 20%
            // Tier6+: 25%
            float saveChance = (tier - 1) * 0.05f;
            if (saveChance > 0.25f) saveChance = 0.25f;

            // MBRandom.RandomFloat is public. :contentReference[oaicite:8]{index=8}
            if (MBRandom.RandomFloat < saveChance)
                __3 = false;
        }
    }
}
