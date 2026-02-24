using Byzantium1071.Campaign.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign.Behaviors
{
    /// <summary>
    /// Provincial Governance Friction (RV1):
    ///
    /// Tracks a per-settlement "governance strain" index (0–100) that accumulates
    /// from war events and decays slowly over time. Strain represents the
    /// administrative disruption, refugee crisis, military overextension, and
    /// civic breakdown that follow sustained conflict.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// ACCUMULATION (event-driven)
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// | Event                         | Strain | Target                    |
    /// |-------------------------------|--------|---------------------------|
    /// | War declared vs kingdom       | +5     | All settlements of kingdom|
    /// | Village raided (looted)       | +10    | Bound town/castle         |
    /// | Settlement besieged           | +15    | The besieged settlement   |
    /// | Noble captured from clan      | +8     | Clan's home settlement    |
    /// | Settlement conquered          | +20    | The conquered settlement  |
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// DECAY
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// -0.3/day (configurable). A +10 raid event takes ~33 days to fully decay.
    /// Strain is clamped to [0, GovernanceStrainCap].
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// EFFECTS (applied via Harmony patches in separate files)
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// At strain S (0–100):
    ///   Loyalty:    -(S / 100) × MaxLoyaltyPenalty   per day
    ///   Security:   -(S / 100) × MaxSecurityPenalty   per day
    ///   Prosperity: -(S / 100) × MaxProsperityPenalty per day
    ///
    /// All three appear as "Governance Strain" tooltip lines in their respective
    /// settlement info panels.
    ///
    /// ──────────────────────────────────────────────────────────────────────
    /// PERSISTENCE
    /// ──────────────────────────────────────────────────────────────────────
    ///
    /// Dictionary&lt;string, float&gt; keyed by Settlement.StringId, persisted via SyncData.
    /// </summary>
    public sealed class B1071_GovernanceBehavior : CampaignBehaviorBase
    {
        public static B1071_GovernanceBehavior? Instance { get; internal set; }

        private static B1071_McmSettings Settings => B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults;

        // Per-settlement governance strain (0 to GovernanceStrainCap).
        private Dictionary<string, float> _strainBySettlement = new Dictionary<string, float>();

        // ── Public API (used by Harmony patches) ──────────────────────────

        /// <summary>Returns governance strain for the given settlement (0–100).</summary>
        public float GetStrain(Settlement settlement)
        {
            if (settlement == null) return 0f;
            return _strainBySettlement.TryGetValue(settlement.StringId, out float val) ? val : 0f;
        }

        /// <summary>Returns governance strain for a Town's settlement.</summary>
        public float GetStrainForTown(Town town)
            => town?.Settlement != null ? GetStrain(town.Settlement) : 0f;

        // ── CampaignBehaviorBase ──────────────────────────────────────────

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("b1071_governanceStrain", ref _strainBySettlement);
            _strainBySettlement ??= new Dictionary<string, float>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Instance = this;
        }

        // ── Daily decay ───────────────────────────────────────────────────

        private void OnDailyTickSettlement(Settlement settlement)
        {
            try
            {
                if (!Settings.EnableGovernanceStrain) return;
                if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;

                string key = settlement.StringId;
                if (!_strainBySettlement.TryGetValue(key, out float strain) || strain <= 0f) return;

                float decay = Settings.GovernanceStrainDecayPerDay;
                strain = Math.Max(0f, strain - decay);

                if (strain <= 0f)
                    _strainBySettlement.Remove(key);
                else
                    _strainBySettlement[key] = strain;
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Governance] DailyTick error: {ex.Message}");
            }
        }

        // ── Event: Village looted → +10 to bound settlement ──────────────

        private void OnVillageLooted(Village village)
        {
            try
            {
                if (!Settings.EnableGovernanceStrain) return;
                if (village?.Bound == null) return;

                AddStrain(village.Bound, 10f);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Governance] VillageLooted error: {ex.Message}");
            }
        }

        // ── Event: War declared → +5 to all settlements of defending kingdom

        private void OnWarDeclared(IFaction faction1, IFaction faction2, DeclareWarAction.DeclareWarDetail detail)
        {
            try
            {
                if (!Settings.EnableGovernanceStrain) return;

                // Apply strain to both factions' settlements (war affects both sides)
                ApplyStrainToFactionSettlements(faction1, 5f);
                ApplyStrainToFactionSettlements(faction2, 5f);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Governance] WarDeclared error: {ex.Message}");
            }
        }

        // ── Event: Siege/battle ended → +15 to besieged settlement ───────

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            try
            {
                if (!Settings.EnableGovernanceStrain) return;
                if (mapEvent == null) return;

                // Siege completed — apply strain to the besieged settlement
                if (mapEvent.IsSiegeAssault || mapEvent.IsSiegeOutside || mapEvent.IsSallyOut)
                {
                    Settlement? besieged = mapEvent.MapEventSettlement;
                    if (besieged != null)
                        AddStrain(besieged, 15f);
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Governance] MapEventEnded error: {ex.Message}");
            }
        }

        // ── Event: Noble captured → +8 to clan's home settlement ─────────

        private void OnHeroPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            try
            {
                if (!Settings.EnableGovernanceStrain) return;
                if (prisoner == null || !prisoner.IsLord) return;

                // Find the captured noble's clan home settlement
                Settlement? home = prisoner.Clan?.HomeSettlement;
                if (home != null)
                    AddStrain(home, 8f);
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Governance] HeroPrisonerTaken error: {ex.Message}");
            }
        }

        // ── Event: Settlement conquered → +20 to conquered settlement ────

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim,
            Hero newOwner, Hero oldOwner, Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            try
            {
                if (!Settings.EnableGovernanceStrain) return;
                if (settlement == null) return;

                // Only conquest/barter — not internal fief grants
                if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege
                    || detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByBarter
                    || detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByRebellion)
                {
                    AddStrain(settlement, 20f);
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[Byzantium1071][Governance] SettlementOwnerChanged error: {ex.Message}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void AddStrain(Settlement settlement, float amount)
        {
            // Only track strain for towns/castles — villages would create orphan
            // entries that never decay (daily decay filters to IsTown || IsCastle).
            if (settlement == null || amount <= 0f) return;
            if (!settlement.IsTown && !settlement.IsCastle) return;

            string key = settlement.StringId;
            float current = _strainBySettlement.TryGetValue(key, out float val) ? val : 0f;
            float cap = Settings.GovernanceStrainCap;
            _strainBySettlement[key] = Math.Min(cap, current + amount);
        }

        private void ApplyStrainToFactionSettlements(IFaction? faction, float amount)
        {
            if (faction == null || amount <= 0f) return;

            // Handle both Kingdom (many settlements) and Clan (minor faction) cases
            if (faction is Kingdom kingdom)
            {
                foreach (Settlement s in kingdom.Settlements)
                    AddStrain(s, amount);
            }
            else if (faction is Clan clan)
            {
                foreach (Settlement s in clan.Settlements)
                    AddStrain(s, amount);
            }
        }
    }
}
