using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using System;

namespace Byzantium1071.Campaign.Settings
{
    public sealed class B1071_McmSettings : AttributeGlobalSettings<B1071_McmSettings>
    {
        // Shared fallback instance — avoids creating multiple AttributeGlobalSettings
        // objects that could confuse MCM's lifecycle. All consumers share this one.
        private static B1071_McmSettings? _defaults;
        internal static B1071_McmSettings Defaults => _defaults ??= new();

        public override string Id => "Byzantium1071";
        public override string DisplayName => "Campaign++";
        public override string FolderName => "Byzantium1071";
        public override string FormatType => "json";

        // ─── Settings Profile Version ───
        // MCM persists user-modified values to a JSON file on disk. When we ship
        // new balance defaults, existing users keep the old values forever.
        // This version counter gates one-time hard migration of specific settings.
        // Bump LATEST_PROFILE_VERSION and add a new migration block below.
        internal const int LATEST_PROFILE_VERSION = 8;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 98)]
        [SettingPropertyInteger("Settings profile version (do not change)", 0, 1000, "0", Order = 99, HintText = "Tracks which balance profile was last applied. Do not change manually — the mod migrates this automatically on update.")]
        public int SettingsProfileVersion { get; set; } = 0;

        /// <summary>
        /// One-time migration: force-overwrites specific settings that were rebalanced
        /// between mod versions. Preserves all settings NOT listed in the migration block
        /// (pool sizes, slave economy, toggles, etc.). Called from SubModule on first load.
        /// Returns a user-facing message if migration occurred, or null if no migration needed.
        /// </summary>
        internal string? MigrateToLatestProfile()
        {
            if (SettingsProfileVersion >= LATEST_PROFILE_VERSION)
                return null;

            string migrated = string.Empty;

            // ── Profile v2: v0.1.8.2 historically-calibrated rebalance ──
            if (SettingsProfileVersion < 2)
            {
                // War exhaustion
                ExhaustionDailyDecay = 0.65f;
                BattleExhaustionPerCasualty = 0.001f; // unchanged, but pin to ensure consistency
                NobleCaptureExhaustionGain = 3f;
                RaidExhaustionGain = 2f;
                SiegeExhaustionDefender = 5f;
                SiegeExhaustionAttacker = 3f;
                ConquestExhaustionGain = 4f;
                ManpowerDepletionAmplifier = 0.5f;
                BattleCasualtyDrainMultiplier = 0f; // changed 0.5→0 in v0.1.7.9

                // Pressure bands
                PressureBandRisingStart = 35f;
                PressureBandCrisisStart = 65f;
                PressureBandHysteresis = 5f;

                // Diplomacy
                DiplomacyForcedPeaceThreshold = 80f;
                DiplomacyForcedPeaceCooldownDays = 10;
                DiplomacyForcedPeaceThresholdReductionPerMajorWar = 5f;
                DiplomacyNoNewWarThreshold = 65f;
                DiplomacyPeacePressureThreshold = 45f;
                MinWarDurationDaysBeforeForcedPeace = 40;

                // Support formulas
                PeaceBiasBandLow = 1.5f;
                PeaceBiasBandHigh = 3.0f;
                DiplomacyWarSupportPenaltyPerPoint = 4.0f;
                DiplomacyExtraPeaceBiasPerMajorWar = 20f;
                DiplomacyMajorWarPressureStartCount = 2;
                WarSupportPenaltyCap = -400f;
                PeaceSupportBonusCap = 350f;

                // Manpower diplomacy
                ManpowerDiplomacyThresholdPercent = 35;
                ManpowerDiplomacyPressureStrength = 100f;

                migrated += "v0.1.8.2 balance retuning (war exhaustion, pressure bands, diplomacy). ";
            }

            // ── Profile v3: v0.1.8.3 historical realism pass ──
            if (SettingsProfileVersion < 3)
            {
                // Slaves are labor (construction, prosperity), not a recruitment pool.
                // Zero out the legacy manpower settings so existing saves stop injecting MP.
                SlaveManpowerPerUnit = 0f;
                SlaveManpowerCapPerTown = 0;

                // Historically-calibrated regen: castles received garrisons from towns
                // (institutional rotation), not from local births. Reduce castle floor
                // and emergency regen to match 11th-century demographic reality.
                CastleMinimumDailyRegen = 1;       // was 3; castles don't generate MP organically
                DepletedRegenBonusAtZero = 2;       // was 5; devastated provinces took decades to recover
                DepletedRegenThresholdPercent = 15; // was 25; emergency only at critical depletion

                // Castle supply chain: castles transfer MP from nearest same-faction town
                // rather than creating it from nothing. Only a local trickle is free.
                EnableCastleSupplyChain = true;

                migrated += "v0.1.8.3 slave manpower removed, regen historically calibrated, castle supply chain enabled. ";
            }

            // ── Profile v4: v0.1.8.4 slave price curve flattening ──
            if (SettingsProfileVersion < 4)
            {
                // Playtest data showed 0.925 decay was far too steep: stock 30+ = floor (150d),
                // 96.7% of towns at floor, zero price differential → no caravan trading.
                // 0.98 puts the "interesting range" at stock 30-100 (818d-199d),
                // creating 125%+ differentials between towns for caravan arbitrage.
                SlavePriceDecayRate = 0.98f;

                migrated += "v0.1.8.4 slave price decay flattened (0.925→0.98) for caravan trading. ";
            }

            // ── Profile v5: v0.1.8.5 slave price rebalance ──
            if (SettingsProfileVersion < 5)
            {
                // Base value lowered from 1500d to 300d (in items.xml) to align
                // slave sell prices with ~2× T1-T3 ransom. Cap lowered to match:
                // P3000 town holds 60 slaves (was 90). Enslaving still beats
                // ransoming but no longer prints 10× the gold.
                SlaveCapPerProsperity = 0.02f;

                migrated += "v0.1.8.5 slave base value 1500→300d, cap/prosperity 0.03→0.02 for balanced economics. ";
            }

            // ── Profile v6: v0.1.9.0 investment influence 5× buff ──
            if (SettingsProfileVersion < 6)
            {
                // Influence from patronage was too small to feel impactful.
                // 5× multiplier: Grand village 2→10, Grand town 10→50.
                VillageInvestInfluenceModest = 2.5f;
                VillageInvestInfluenceGenerous = 5.0f;
                VillageInvestInfluenceGrand = 10.0f;
                TownInvestInfluenceModest = 10.0f;
                TownInvestInfluenceGenerous = 25.0f;
                TownInvestInfluenceGrand = 50.0f;

                migrated += "v0.1.9.0 investment influence 5× (village 0.5/1/2→2.5/5/10, town 2/5/10→10/25/50). ";
            }

            // ── Profile v7: v0.1.9.0 war exhaustion decay + crisis band rebalance ──
            if (SettingsProfileVersion < 7)
            {
                // Decay was too slow (0.65/day) — kingdoms stuck in Crisis for 50+ days.
                // Crisis band was too low (65) — 7/9 kingdoms locked out of war.
                ExhaustionDailyDecay = 1.0f;
                PressureBandCrisisStart = 85f;

                migrated += "v0.1.9.0 exhaustion decay 0.65→1.0/day, crisis band 65→85 (fewer frozen kingdoms, faster recovery). ";
            }

            // ── Profile v8: v0.1.10.0 clan survival system ──
            if (SettingsProfileVersion < 8)
            {
                // New system: prevent clan annihilation when kingdoms are destroyed.
                // Rescued clans become independent for a grace period, then seek
                // mercenary service with culture-weighted kingdom selection.
                EnableClanSurvival = true;
                ClanSurvivalGracePeriodDays = 30;
                ClanSurvivalCultureWeight = 2.0f;

                migrated += "v0.1.10.0 clan survival enabled (30-day grace, culture weight 2.0). ";
            }

            // ── Future migrations go here ──
            // if (SettingsProfileVersion < 9) { ... migrated += "..."; }

            SettingsProfileVersion = LATEST_PROFILE_VERSION;

            TaleWorlds.Library.Debug.Print($"[Byzantium1071] Settings migrated to profile v{LATEST_PROFILE_VERSION}: {migrated}");
            return $"Campaign++ v{LATEST_PROFILE_VERSION}: Balance settings updated to new defaults. Customize in MCM if desired. ({migrated.Trim()})";
        }

        [SettingPropertyGroup("Pool Sizes", GroupOrder = 0)]
        [SettingPropertyInteger("Town max manpower", 50, 50000, "0", Order = 0, HintText = "Base manpower pool for towns before economic scaling.")]
        public int TownPoolMax { get; set; } = 1500;

        [SettingPropertyGroup("Pool Sizes", GroupOrder = 0)]
        [SettingPropertyInteger("Castle max manpower", 50, 50000, "0", Order = 1, HintText = "Base manpower pool for castles before economic scaling.")]
        public int CastlePoolMax { get; set; } = 400;

        [SettingPropertyGroup("Pool Sizes", GroupOrder = 0)]
        [SettingPropertyInteger("Other settlement max manpower", 10, 20000, "0", Order = 2, HintText = "Fallback pool size for settlements that are not town or castle.")]
        public int OtherPoolMax { get; set; } = 1000;

        [SettingPropertyGroup("Pool Sizes", GroupOrder = 0)]
        [SettingPropertyBool("Use tiny pools for testing", Order = 3, HintText = "Scales down all pool sizes by the divisor below.")]
        public bool UseTinyPoolsForTesting { get; set; } = false;

        [SettingPropertyGroup("Pool Sizes", GroupOrder = 0)]
        [SettingPropertyInteger("Tiny pool divisor", 1, 500, "0", Order = 4, HintText = "If tiny pools are enabled, poolSize = max(minTinyPool, poolSize / divisor).")]
        public int TinyPoolDivisor { get; set; } = 50;

        [SettingPropertyGroup("Pool Sizes", GroupOrder = 0)]
        [SettingPropertyInteger("Tiny pool minimum", 1, 2000, "0", Order = 5, HintText = "Minimum pool size after tiny-pool scaling.")]
        public int TinyPoolMinimum { get; set; } = 10;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Prosperity normalizer", 1000f, 20000f, "0", Order = 0, HintText = "Prosperity value at which pool reaches max scale. Typical towns: 2000-8000+, castles: 500-3500.")]
        public float ProsperityNormalizer { get; set; } = 6000f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Prosperity min scale %", 1f, 100f, "0", Order = 1, HintText = "Pool % when prosperity is 0. E.g., 30 = ruined settlement has 30% of base pool.")]
        public float MaxPoolProsperityMinScale { get; set; } = 30f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Prosperity max scale %", 50f, 200f, "0", Order = 2, HintText = "Pool % at full prosperity. E.g., 100 = thriving settlement has 100% of base pool.")]
        public float MaxPoolProsperityMaxScale { get; set; } = 100f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Security bonus min %", 1f, 150f, "0", Order = 3, HintText = "Pool multiplier when security is 0. E.g., 80 = insecure settlement has 80% of prosperity-scaled pool.")]
        public float SecurityBonusMinScale { get; set; } = 80f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Security bonus max %", 50f, 200f, "0", Order = 4, HintText = "Pool multiplier at full security (100). E.g., 120 = safe settlement gets 120% bonus.")]
        public float SecurityBonusMaxScale { get; set; } = 120f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Hearth multiplier", 0f, 5f, "0.00", Order = 5, HintText = "Each village hearth × this = flat bonus to max pool. E.g., 0.10 means 1000 hearth village adds 100 max manpower.")]
        public float MaxPoolHearthMultiplier { get; set; } = 0.10f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Town regen min %", 0f, 20f, "0.00", Order = 0, HintText = "Minimum daily town regen as a percent of max pool (at 0 prosperity).")]
        public float TownRegenMinPercent { get; set; } = 0.10f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Town regen max %", 0f, 20f, "0.00", Order = 1, HintText = "Maximum daily town regen as a percent of max pool (at full prosperity).")]
        public float TownRegenMaxPercent { get; set; } = 0.50f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Castle regen min %", 0f, 20f, "0.00", Order = 2, HintText = "Minimum daily castle regen as a percent of max pool (at 0 prosperity).")]
        public float CastleRegenMinPercent { get; set; } = 0.05f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Castle regen max %", 0f, 20f, "0.00", Order = 3, HintText = "Maximum daily castle regen as a percent of max pool (at full prosperity).")]
        public float CastleRegenMaxPercent { get; set; } = 0.30f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Other settlement regen %", 0f, 20f, "0.00", Order = 4, HintText = "Daily regen percent for non-town/non-castle settlements.")]
        public float OtherRegenPercent { get; set; } = 1.00f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyInteger("Hearth normalizer", 100, 10000, "0", Order = 5, HintText = "Higher values reduce the impact of village hearth sum on regen.")]
        public int HearthNormalizer { get; set; } = 3000;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Hearth bonus max %", 0f, 10f, "0.00", Order = 6, HintText = "Maximum additional regen percent from hearth contribution.")]
        public float HearthBonusMaxPercent { get; set; } = 0.10f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Siege regen multiplier %", 0f, 200f, "0.00", Order = 7, HintText = "While under siege, regen is multiplied by this percent.")]
        public float SiegeRegenMultiplierPercent { get; set; } = 25.00f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyInteger("Minimum daily regen", 0, 500, "0", Order = 8, HintText = "Absolute minimum manpower regenerated per day (towns and fallback).")]
        public int MinimumDailyRegen { get; set; } = 1;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyInteger("Castle minimum daily regen", 0, 20, "0", Order = 81, HintText = "Castle-specific regen floor. Represents peasant levies from bound villages and the castle's tiny civilian community. This trickle is organic (not drawn from towns). Default: 1.")]
        public int CastleMinimumDailyRegen { get; set; } = 1;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyBool("Enable castle supply chain", Order = 78, HintText = "When enabled, castle regen above the local trickle floor is transferred from the nearest same-faction town (not created from nothing). If the supply town is depleted, the castle starves. Historically, castle garrisons were rotated from towns by the strategos. Disabling reverts to legacy behavior where castles regen independently.")]
        public bool EnableCastleSupplyChain { get; set; } = true;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyBool("Enable depleted emergency regen", Order = 82, HintText = "When a pool drops below a threshold, a bonus flat regen is added that scales inversely with fill ratio (emptier = faster recovery). Models limited Crown frontier investment — historically, devastated provinces took decades to recover.")]
        public bool EnableDepletedEmergencyRegen { get; set; } = true;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyInteger("Depleted threshold %", 5, 50, "0", Order = 83, HintText = "Pool fill ratio below which emergency regen activates. E.g., 15 = emergency regen when pool is below 15% full. Default: 15.")]
        public int DepletedRegenThresholdPercent { get; set; } = 15;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyInteger("Depleted regen bonus at 0%", 1, 30, "0", Order = 84, HintText = "Maximum flat bonus manpower/day added when pool is at 0%. Scales linearly to 0 as pool approaches the depleted threshold. E.g., 2 = at 0% pool get +2/day bonus, at 7.5% get +1, at 15% get +0. Default: 2.")]
        public int DepletedRegenBonusAtZero { get; set; } = 2;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Regen cap % of pool/day", 0.1f, 20f, "0.00", Order = 9, HintText = "Hard cap on daily regen as a percent of max pool. E.g., 2.0 means a 3000-pool town regens at most 60/day (~50 days to full).")]
        public float RegenCapPercent { get; set; } = 2.0f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyBool("Enable regen soft-cap", Order = 10, HintText = "Near full manpower, regen progressively slows down instead of staying linear.")]
        public bool EnableRegenSoftCap { get; set; } = true;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Soft-cap start ratio", 0.60f, 0.95f, "0.00", Order = 11, HintText = "Pool fill ratio where soft-cap slowdown starts. Example: 0.75 means slowdown begins at 75% full.")]
        public float RegenSoftCapStartRatio { get; set; } = 0.75f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Soft-cap strength", 0f, 3f, "0.00", Order = 12, HintText = "How strongly regen slows between soft-cap start and full pool. 0 disables slowdown effect.")]
        public float RegenSoftCapStrength { get; set; } = 1.25f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Stress floor %", 0f, 2f, "0.00", Order = 13, HintText = "Minimum daily regen percent of max pool after stacked stress penalties.")]
        public float RegenStressFloorPercent { get; set; } = 0.05f;

        [SettingPropertyGroup("Regen Modifiers", GroupOrder = 3)]
        [SettingPropertyFloatingInteger("Security regen min %", 0f, 150f, "0", Order = 0, HintText = "Regen multiplier when security is 0. E.g., 80 = insecure settlement regens at 80%.")]
        public float SecurityRegenMinScale { get; set; } = 80f;

        [SettingPropertyGroup("Regen Modifiers", GroupOrder = 3)]
        [SettingPropertyFloatingInteger("Security regen max %", 50f, 200f, "0", Order = 1, HintText = "Regen multiplier at full security. E.g., 120 = safe settlement regens at 120%.")]
        public float SecurityRegenMaxScale { get; set; } = 120f;

        [SettingPropertyGroup("Regen Modifiers", GroupOrder = 3)]
        [SettingPropertyFloatingInteger("Food regen min %", 0f, 100f, "0", Order = 2, HintText = "Regen multiplier when starving (food stocks = 0). E.g., 50 = starving halves regen.")]
        public float FoodRegenMinScale { get; set; } = 50f;

        [SettingPropertyGroup("Regen Modifiers", GroupOrder = 3)]
        [SettingPropertyFloatingInteger("Food regen max %", 50f, 200f, "0", Order = 3, HintText = "Regen multiplier when well-fed. E.g., 100 = normal regen rate.")]
        public float FoodRegenMaxScale { get; set; } = 100f;

        [SettingPropertyGroup("Regen Modifiers", GroupOrder = 3)]
        [SettingPropertyFloatingInteger("Food stocks normalizer", 1f, 100f, "0", Order = 4, HintText = "Food stock value at which food modifier reaches maximum. Typical: 20-40.")]
        public float FoodStocksNormalizer { get; set; } = 30f;

        [SettingPropertyGroup("Regen Modifiers", GroupOrder = 3)]
        [SettingPropertyFloatingInteger("Loyalty regen min %", 0f, 100f, "0", Order = 5, HintText = "Regen multiplier when loyalty is 0. E.g., 50 = disloyal population halves regen.")]
        public float LoyaltyRegenMinScale { get; set; } = 50f;

        [SettingPropertyGroup("Regen Modifiers", GroupOrder = 3)]
        [SettingPropertyFloatingInteger("Loyalty regen max %", 50f, 200f, "0", Order = 6, HintText = "Regen multiplier at full loyalty. E.g., 100 = loyal population regens at normal rate.")]
        public float LoyaltyRegenMaxScale { get; set; } = 100f;

        [SettingPropertyGroup("Recruitment Cost", GroupOrder = 4)]
        [SettingPropertyInteger("Base manpower cost", 1, 50, "0", Order = 0, HintText = "Base manpower cost for a tier-1 recruit.")]
        public int BaseManpowerCostPerTroop { get; set; } = 1;

        [SettingPropertyGroup("Legacy", GroupOrder = 99)]
        [SettingPropertyInteger("[Legacy] Tiers per +1 cost step", 1, 10, "0", Order = 10, HintText = "[LEGACY — NOT USED] This setting is no longer active. Manpower cost is now flat per troop (BaseManpowerCostPerTroop). Kept for save compatibility.")]
        public int TiersPerExtraCost { get; set; } = 2;

        [SettingPropertyGroup("Legacy", GroupOrder = 99)]
        [SettingPropertyInteger("[Legacy] Cost multiplier %", 1, 1000, "0", Order = 11, HintText = "[LEGACY — NOT USED] This setting is no longer active. Manpower cost is now flat per troop (BaseManpowerCostPerTroop). Kept for save compatibility.")]
        public int CostMultiplierPercent { get; set; } = 100;

        // ─── Combat Realism ───

        [SettingPropertyGroup("Combat Realism", GroupOrder = 5)]
        [SettingPropertyBool("Enable tier survivability", Order = 0, HintText = "Higher-tier troops are more likely to survive as wounded rather than die in autoresolve. T1=+0%, T2=+5%, T3=+10%, T4=+15%, T5=+20%, T6=+25% flat bonus to survival chance on top of Medicine-based base rate.")]
        public bool EnableTierSurvivability { get; set; } = true;

        [SettingPropertyGroup("Combat Realism", GroupOrder = 5)]
        [SettingPropertyBool("Enable tier armor simulation", Order = 1, HintText = "Higher-tier troops receive reduced simulated hit damage in autoresolve (T2=-6%, T3=-12%, T4=-18%, T5=-24%, T6=-30%). Lowers the probability of the fatal-hit gate firing per tick. Works together with tier survivability: fewer casualty checks AND better survival within each check.")]
        public bool EnableTierArmorSimulation { get; set; } = true;

        // ─── Army Economics ───

        [SettingPropertyGroup("Army Economics", GroupOrder = 14)]
        [SettingPropertyInteger("Hire & upgrade cost preset", 0, 3, "0", Order = 0, HintText = "Tier-exponential scaling for recruitment gold and upgrade gold. 0=Vanilla | 1=Light (+0/+15/+35/+65/+100/+150% T1-T6) | 2=Moderate (+10/+30/+75/+150/+250/+400%) | 3=Severe (+25/+75/+175/+350/+600/+1000%). Upgrade cost cascades automatically from hire cost (no double-stacking). Applies to AI and player equally. CaravanGuard/Mercenary/Gangster hire premiums are normalised to match regular soldiers at the same tier.")]
        public int HireUpgradeCostPreset { get; set; } = 2;

        [SettingPropertyGroup("Army Economics", GroupOrder = 14)]
        [SettingPropertyInteger("Daily wage preset", 0, 3, "0", Order = 1, HintText = "Tier-exponential scaling for troop wages. 0=Vanilla | 1=Light (T3+20%, T4+40%, T5+70%, T6+100%) | 2=Moderate (T3+50%, T4+100%, T5+160%, T6+250%) | 3=Severe (T3+70%, T4+150%, T5+250%, T6+400%). Heroes/companions and CaravanGuards are excluded. Applies to AI and player equally.")]
        public int WagePreset { get; set; } = 2;

        [SettingPropertyGroup("Army Economics", GroupOrder = 14)]
        [SettingPropertyBool("Enable garrison wage discount", Order = 2, HintText = "Garrison parties pay a reduced wage relative to field troops. Historically accurate: garrison soldiers often received lower pay, supplemented by shelter and rations. Applies on top of vanilla perk and building discounts.")]
        public bool EnableGarrisonWageDiscount { get; set; } = true;

        [SettingPropertyGroup("Army Economics", GroupOrder = 14)]
        [SettingPropertyInteger("Garrison wage % of field", 10, 100, "0", Order = 3, HintText = "Garrison party total wage as a percentage of what the same troops would cost in a field party. 60 = garrison pays 60% (40% discount). 100 = no discount. Default: 60.")]
        public int GarrisonWagePercent { get; set; } = 60;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 98)]
        [SettingPropertyBool("Enable verbose mod log", Order = -1, HintText = "Master switch: logs ALL mod activity (manpower, slaves, diplomacy, patches) to the Bannerlord rgl_log file. Superset of every other debug toggle. Performance cost — disable for normal play.")]
        public bool EnableVerboseModLog { get; set; } = false;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 98)]
        [SettingPropertyBool("Show player debug messages", Order = 0, HintText = "Shows slave economy and manpower messages in the game UI for player actions. Disable for normal play.")]
        public bool ShowPlayerDebugMessages { get; set; } = false;

        [SettingPropertyGroup("Recruitment Cost", GroupOrder = 4)]
        [SettingPropertyBool("Enable player recruit manpower hook", Order = 3, HintText = "Required for consistent player recruitment manpower consumption in current event flow. Keep enabled unless explicitly troubleshooting compatibility.")]
        public bool UseOnUnitRecruitedFallbackForPlayer { get; set; } = true;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 98)]
        [SettingPropertyBool("Log AI manpower consumption", Order = 2, HintText = "Logs AI manpower consumption bands to the Bannerlord logs.")]
        public bool LogAiManpowerConsumption { get; set; } = false;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 98)]
        [SettingPropertyBool("Enable telemetry debug logs", Order = 3, HintText = "Logs structured WP1 telemetry events (regen snapshots, diplomacy rationale, truce/forced peace updates).")]
        public bool TelemetryDebugLogs { get; set; } = false;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 98)]
        [SettingPropertyBool("Show telemetry in Current tab", Order = 4, HintText = "Adds compact instrumentation rows to the Current ledger tab for balancing/debugging.")]
        public bool ShowTelemetryInOverlay { get; set; } = false;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyBool("Enable in-game overlay", Order = 0, HintText = "Enables the standalone Campaign++ overlay on campaign map.")]
        public bool EnableOverlay { get; set; } = true;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyBool("Enable hotkey toggle", Order = 1, HintText = "Press the chosen key on campaign map to show/hide the overlay.")]
        public bool EnableOverlayHotkey { get; set; } = true;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyInteger("Hotkey (0-6)", 0, 6, "0", Order = 2, HintText = "0=M, 1=N, 2=K, 3=F9, 4=F10, 5=F11, 6=F12. Default: 0 (M).")]
        public int OverlayHotkeyChoice { get; set; } = 0;

        [SettingPropertyGroup("Legacy", GroupOrder = 99)]
        [SettingPropertyBool("Show settlement name (legacy)", Order = 0, HintText = "Legacy setting kept for save/config compatibility. Not used by current overlay UI.")]
        public bool OverlayShowSettlementName { get; set; } = true;

        [SettingPropertyGroup("Legacy", GroupOrder = 99)]
        [SettingPropertyBool("Show pool identifier (legacy)", Order = 1, HintText = "Legacy setting kept for save/config compatibility. Not used by current overlay UI.")]
        public bool OverlayShowPoolName { get; set; } = false;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyInteger("Ledger default tab (0-12)", 0, 12, "0", Order = 5, HintText = "0=Current, 1=Nearby, 2=Castles, 3=Towns, 4=Villages, 5=Factions, 6=Armies, 7=Wars, 8=Rebellion, 9=Prisoners, 10=Clans, 11=Characters, 12=Search.")]
        public int OverlayLedgerDefaultTab { get; set; } = 0;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyInteger("Ledger rows per page", 3, 22, "0", Order = 6, HintText = "How many ledger rows are shown per page.")]
        public int OverlayLedgerRowsPerPage { get; set; } = 9;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyInteger("Overlay top offset (px)", 40, 320, "0", Order = 7, HintText = "Vertical offset from the top of the screen for the overlay root panel. Increase this if game menus overlap the tab row.")]
        public int OverlayPanelTopOffset { get; set; } = 152;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyInteger("Overlay left offset (px)", 0, 300, "0", Order = 8, HintText = "Horizontal offset from the left edge for the overlay root panel.")]
        public int OverlayPanelLeftOffset { get; set; } = 78;

        // ─── War Effects ───

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyBool("Enable war effects", Order = 0, HintText = "Master toggle for raid/siege/battle/conquest manpower consequences.")]
        public bool EnableWarEffects { get; set; } = true;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Raid drain %", 0, 50, "0", Order = 1, HintText = "% of current pool drained when a bound village raid is fully completed.")]
        public int RaidManpowerDrainPercent { get; set; } = 15;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Raid daily cap %", 0, 100, "0", Order = 2, HintText = "Max total manpower drained by completed village raids from the same pool in one day (% of max pool).")]
        public int RaidDailyPoolDrainCapPercent { get; set; } = 20;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Siege devastate retain %", 0, 100, "0", Order = 3, HintText = "% of max pool retained after choosing Devastate.")]
        public int SiegeDevastateRetainPercent { get; set; } = 10;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Siege pillage retain %", 0, 100, "0", Order = 4, HintText = "% of max pool retained after choosing Pillage.")]
        public int SiegePillageRetainPercent { get; set; } = 40;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Siege mercy retain %", 0, 100, "0", Order = 5, HintText = "% of max pool retained after choosing Show Mercy.")]
        public int SiegeMercyRetainPercent { get; set; } = 70;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyFloatingInteger("Battle casualty drain multiplier", 0f, 2f, "0.00", Order = 6, HintText = "Each battle casualty drains this × 1 manpower from the party's home pool.")]
        public float BattleCasualtyDrainMultiplier { get; set; } = 0f;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Conquest pool retain %", 0, 100, "0", Order = 7, HintText = "% of current pool retained when settlement changes hands (at full pool). Scales higher for depleted pools if dynamic conquest protection is enabled.")]
        public int ConquestPoolRetainPercent { get; set; } = 50;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyBool("Enable dynamic conquest protection", Order = 71, HintText = "Depleted pools retain a higher percentage during conquest. Prevents ping-pong border castles from being permanently zeroed by repeated ownership changes.")]
        public bool EnableDynamicConquestProtection { get; set; } = true;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Conquest depleted retain %", 50, 100, "0", Order = 72, HintText = "Retain % when pool is below the depleted threshold. Scales linearly between this and ConquestPoolRetainPercent based on fill ratio. Default: 85.")]
        public int ConquestDepletedRetainPercent { get; set; } = 85;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Conquest depleted threshold %", 5, 50, "0", Order = 73, HintText = "Pool fill ratio below which dynamic conquest protection kicks in. Default: 25.")]
        public int ConquestDepletedThresholdPercent { get; set; } = 25;

        // ─── Delayed Recovery (WP3) ───

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyBool("Enable delayed recovery", Order = 0, HintText = "Applies temporary recovery penalties after raid/siege/conquest events, decaying over time.")]
        public bool EnableDelayedRecovery { get; set; } = true;

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyInteger("Raid recovery days", 0, 180, "0", Order = 1, HintText = "How long raid-related recovery penalties persist.")]
        public int RaidRecoveryDays { get; set; } = 20;

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyInteger("Siege recovery days", 0, 365, "0", Order = 2, HintText = "How long siege-related recovery penalties persist.")]
        public int SiegeRecoveryDays { get; set; } = 35;

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyInteger("Conquest recovery days", 0, 365, "0", Order = 3, HintText = "How long conquest-related recovery penalties persist.")]
        public int ConquestRecoveryDays { get; set; } = 50;

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyInteger("Raid recovery penalty %", 0, 100, "0", Order = 4, HintText = "Base regen penalty applied after a completed raid.")]
        public int RecoveryPenaltyRaidPercent { get; set; } = 10;

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyInteger("Siege recovery penalty %", 0, 100, "0", Order = 5, HintText = "Base regen penalty applied after siege aftermath.")]
        public int RecoveryPenaltySiegePercent { get; set; } = 18;

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyInteger("Conquest recovery penalty %", 0, 100, "0", Order = 6, HintText = "Base regen penalty applied after ownership change.")]
        public int RecoveryPenaltyConquestPercent { get; set; } = 25;

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyInteger("Recovery penalty max %", 0, 100, "0", Order = 7, HintText = "Maximum stacked delayed-recovery penalty.")]
        public int RecoveryPenaltyMaxPercent { get; set; } = 50;

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyBool("Reduce penalty when depleted", Order = 8, HintText = "When pool is below the depleted threshold, recovery penalty effectiveness is halved. Prevents penalties from crushing already-ruined settlements that have nothing left to penalize.")]
        public bool ReduceRecoveryPenaltyWhenDepleted { get; set; } = true;

        [SettingPropertyGroup("Delayed Recovery", GroupOrder = 8)]
        [SettingPropertyInteger("Recovery depleted threshold %", 5, 50, "0", Order = 9, HintText = "Pool fill ratio below which recovery penalty is halved. Default: 25.")]
        public int RecoveryDepletedThresholdPercent { get; set; } = 25;

        // ─── Bounded Stochasticity (WP4) ───

        [SettingPropertyGroup("Bounded Stochasticity", GroupOrder = 9)]
        [SettingPropertyBool("Enable recruitment variance", Order = 0, HintText = "Adds small bounded randomness to volunteer production and post-shock recovery. Makes campaigns less predictable without wild swings.")]
        public bool EnableRecruitmentVariance { get; set; } = true;

        [SettingPropertyGroup("Bounded Stochasticity", GroupOrder = 9)]
        [SettingPropertyInteger("Volunteer variance %", 0, 50, "0", Order = 1, HintText = "Maximum random spread applied to volunteer production probability. E.g., 10 means ±10% around the base value.")]
        public int VolunteerVariancePercent { get; set; } = 10;

        [SettingPropertyGroup("Bounded Stochasticity", GroupOrder = 9)]
        [SettingPropertyInteger("Recovery tick variance %", 0, 50, "0", Order = 2, HintText = "Maximum random spread applied to daily regen output. E.g., 8 means ±8% around the computed regen value.")]
        public int RecoveryVariancePercent { get; set; } = 8;

        // ─── Immersion Modifiers ───

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyBool("Enable seasonal regen", Order = 0, HintText = "Spring/summer boosts regen, winter penalizes it.")]
        public bool EnableSeasonalRegen { get; set; } = true;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyInteger("Spring/summer regen %", 50, 200, "0", Order = 1, HintText = "Regen multiplier during spring and summer campaign seasons.")]
        public int SpringSummerRegenMultiplier { get; set; } = 115;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyInteger("Winter regen %", 25, 100, "0", Order = 2, HintText = "Regen multiplier during winter. E.g., 75 = regen at 75% rate.")]
        public int WinterRegenMultiplier { get; set; } = 75;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyBool("Enable peace dividend", Order = 3, HintText = "Pools regen faster when your kingdom is at peace with all factions.")]
        public bool EnablePeaceDividend { get; set; } = true;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyInteger("Peace dividend regen %", 100, 200, "0", Order = 4, HintText = "Regen multiplier when kingdom is at peace. E.g., 125 = 25% bonus.")]
        public int PeaceDividendMultiplier { get; set; } = 125;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyBool("Enable culture discount", Order = 5, HintText = "Recruiting in matching-culture settlements costs less manpower.")]
        public bool EnableCultureDiscount { get; set; } = true;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyInteger("Culture cost %", 50, 100, "0", Order = 6, HintText = "Manpower cost % when recruiting from matching culture. 75 = troops cost 75% of normal manpower (25% discount).")]
        public int CultureCostPercent { get; set; } = 75;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyBool("Enable governor bonus", Order = 7, HintText = "Governor Steward skill boosts regen; Leadership boosts max pool.")]
        public bool EnableGovernorBonus { get; set; } = true;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Governor steward regen divisor", 100f, 500000f, "0", Order = 8, HintText = "Steward skill is divided by this, then divided by 100 to get additional regen %. E.g., 100000 means 200 Steward adds +0.002 regen pct (0.2% of pool/day).")]
        public float GovernorStewardRegenDivisor { get; set; } = 100000f;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Governor leadership pool divisor", 100f, 5000f, "0", Order = 9, HintText = "Leadership skill / this value multiplies max pool. E.g., 500 means 200 Leadership adds +40% to max.")]
        public float GovernorLeadershipPoolDivisor { get; set; } = 500f;

        // ─── Alerts & Militia ───

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 11)]
        [SettingPropertyBool("Enable manpower alerts", Order = 0, HintText = "Shows a warning when your settlements' manpower drops below the threshold.")]
        public bool EnableManpowerAlerts { get; set; } = true;

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 11)]
        [SettingPropertyInteger("Alert threshold %", 5, 50, "0", Order = 1, HintText = "Pool % below which a crisis alert is shown.")]
        public int AlertThresholdPercent { get; set; } = 25;

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 11)]
        [SettingPropertyBool("Enable militia-manpower link", Order = 2, HintText = "Militia growth scales with manpower ratio. Depleted pools produce less militia.")]
        public bool EnableMilitiaLink { get; set; } = true;

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 11)]
        [SettingPropertyInteger("Militia min scale % at 0% MP", 0, 100, "0", Order = 3, HintText = "Militia growth multiplier when manpower is at 0%.")]
        public int MilitiaManpowerMinScale { get; set; } = 0;

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 11)]
        [SettingPropertyInteger("Militia max scale % at 100% MP", 50, 200, "0", Order = 4, HintText = "Militia growth multiplier when manpower is at 100%.")]
        public int MilitiaManpowerMaxScale { get; set; } = 100;

        // ─── War Exhaustion ───

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyBool("Enable war exhaustion", Order = 0, HintText = "Tracks per-kingdom exhaustion from battles, raids, and sieges. Reduces regen when high.")]
        public bool EnableWarExhaustion { get; set; } = true;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Daily decay", 0.1f, 5f, "0.0", Order = 1, HintText = "How much exhaustion decays per day toward 0.")]
        public float ExhaustionDailyDecay { get; set; } = 1.0f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Regen penalty divisor", 50f, 500f, "0", Order = 2, HintText = "Regen is multiplied by (1 - exhaustion/divisor). Higher = softer penalty. E.g., 200 means 100 exhaustion halves regen.")]
        public float ExhaustionRegenDivisor { get; set; } = 200f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Max exhaustion score", 50f, 200f, "0", Order = 3, HintText = "Maximum exhaustion a kingdom can accumulate.")]
        public float ExhaustionMaxScore { get; set; } = 100f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Battle exhaustion per casualty", 0.001f, 0.1f, "0.000", Order = 4, HintText = "Exhaustion gained per battle casualty.")]
        public float BattleExhaustionPerCasualty { get; set; } = 0.001f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Raid exhaustion gain", 0f, 10f, "0.0", Order = 5, HintText = "Exhaustion gained by defending kingdom when a village is raided.")]
        public float RaidExhaustionGain { get; set; } = 2f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Siege exhaustion (defender)", 0f, 20f, "0.0", Order = 6, HintText = "Exhaustion gained by defending kingdom after siege aftermath.")]
        public float SiegeExhaustionDefender { get; set; } = 5f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Siege exhaustion (attacker)", 0f, 20f, "0.0", Order = 7, HintText = "Exhaustion gained by attacking kingdom after siege aftermath.")]
        public float SiegeExhaustionAttacker { get; set; } = 3f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Conquest exhaustion gain", 0f, 20f, "0.0", Order = 8, HintText = "Exhaustion gained by losing kingdom when a settlement changes hands.")]
        public float ConquestExhaustionGain { get; set; } = 4f;

        // ─── Noble-capture exhaustion ───

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyBool("Enable noble-capture exhaustion", Order = 9, HintText = "Capturing an enemy noble (lord/lady) inflicts additional war exhaustion on the victim's kingdom.")]
        public bool EnableNobleCaptureExhaustion { get; set; } = true;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Noble capture exhaustion gain", 0f, 20f, "0.0", Order = 10, HintText = "Exhaustion added to a kingdom each time one of its nobles is captured in battle. Default: 3.")]
        public float NobleCaptureExhaustionGain { get; set; } = 3f;

        // ─── Tier-weighted battle casualties ───

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyBool("Enable tier-weighted casualty drain", Order = 11, HintText = "Higher-tier troop casualties drain more manpower than low-tier ones (veterans are harder to replace). T1×1.0, T2×1.1, T3×1.25, T4×1.5, T5×1.75, T6×2.0.")]
        public bool EnableTierWeightedCasualties { get; set; } = true;

        // ─── Manpower-depletion exhaustion amplifier ───

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyBool("Enable manpower depletion amplifier", Order = 12, HintText = "War exhaustion gains are amplified when a kingdom's average settlement manpower is depleted. Reflects the feedback loop: empty pools mean losses are felt more acutely.")]
        public bool EnableManpowerDepletionAmplifier { get; set; } = true;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 12)]
        [SettingPropertyFloatingInteger("Manpower depletion amplifier", 0f, 3f, "0.00", Order = 13, HintText = "Scales how much depleted manpower amplifies exhaustion gains. At 1.0, a kingdom at 0% average manpower gets 2× exhaustion from each event; at 50% manpower it gets 1.5×. 0 = disabled.")]
        public float ManpowerDepletionAmplifier { get; set; } = 0.5f;

        // ─── Diplomacy (War Exhaustion) ───

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyBool("Enable diplomacy pressure", Order = 0, HintText = "War exhaustion directly affects AI kingdom war/peace decisions (DetermineSupport patches). Disable if using a dedicated AI diplomacy mod such as AI Influence to avoid double war-fatigue stacking.")]
        public bool EnableExhaustionDiplomacyPressure { get; set; } = true;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("No-new-war threshold", 1f, 200f, "0.0", Order = 1, HintText = "At or above this exhaustion, AI kingdoms are prevented from starting new wars.")]
        public float DiplomacyNoNewWarThreshold { get; set; } = 65f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Peace pressure threshold", 1f, 200f, "0.0", Order = 2, HintText = "At or above this exhaustion, AI support strongly favors peace outcomes.")]
        public float DiplomacyPeacePressureThreshold { get; set; } = 45f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("War support penalty / point", 0f, 100f, "0.0", Order = 3, HintText = "Support penalty applied to declaring-war outcomes per exhaustion point.")]
        public float DiplomacyWarSupportPenaltyPerPoint { get; set; } = 4f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Peace support bonus / point", 0f, 100f, "0.0", Order = 4, HintText = "Support bonus applied to make-peace outcomes per exhaustion point.")]
        public float DiplomacyPeaceSupportBonusPerPoint { get; set; } = 5f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyBool("Enable forced peace at crisis", Order = 5, HintText = "If exhaustion is critically high, AI kingdoms will automatically end one war per cooldown period. Disable if using AI Influence or another mod that manages its own peace-forcing logic.")]
        public bool EnableForcedPeaceAtCrisis { get; set; } = true;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Forced peace threshold", 1f, 200f, "0.0", Order = 6, HintText = "At or above this exhaustion score, forced peace checks become active.")]
        public float DiplomacyForcedPeaceThreshold { get; set; } = 80f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyInteger("Forced peace cooldown (days)", 1, 30, "0", Order = 7, HintText = "Minimum number of days between automatic peaces for the same kingdom.")]
        public int DiplomacyForcedPeaceCooldownDays { get; set; } = 10;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyInteger("Forced peace max active wars", 0, 10, "0", Order = 8, HintText = "Forced peace only triggers when active wars exceed this number.")]
        public int DiplomacyForcedPeaceMaxActiveWars { get; set; } = 0;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyInteger("Min war days before forced peace", 0, 365, "0", Order = 9, HintText = "Forced peace cannot end a war younger than this many days.")]
        public int MinWarDurationDaysBeforeForcedPeace { get; set; } = 40;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyBool("Ignore if enemy besieges core fief", Order = 10, HintText = "Do not force peace with an enemy currently besieging one of your owned towns/castles.")]
        public bool IgnoreForcedPeaceIfEnemyBesiegingCoreSettlement { get; set; } = true;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyBool("Enable truce enforcement", Order = 10, HintText = "After peace is made, block both kingdoms from declaring war again for the truce duration below. Disable if using AI Influence or another mod that controls its own war-declaration timing.")]
        public bool EnableTruceEnforcement { get; set; } = true;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyInteger("Forced peace truce days", 0, 365, "0", Order = 11, HintText = "After any peace between two kingdoms, block re-declaration for this many days. Only active when truce enforcement is enabled.")]
        public int ForcedPeaceTruceDays { get; set; } = 30;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyInteger("Major-war pressure starts at", 1, 10, "0", Order = 12, HintText = "When active kingdom wars reach this count, extra peace pressure is applied.")]
        public int DiplomacyMajorWarPressureStartCount { get; set; } = 2;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Extra peace bias per major war", 0f, 500f, "0.0", Order = 13, HintText = "Additional support bias toward peace for each war above the pressure start count.")]
        public float DiplomacyExtraPeaceBiasPerMajorWar { get; set; } = 20f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Forced peace threshold reduction/war", 0f, 100f, "0.0", Order = 14, HintText = "Lowers forced-peace exhaustion threshold per extra major war.")]
        public float DiplomacyForcedPeaceThresholdReductionPerMajorWar { get; set; } = 5f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyBool("Enforce player parity", Order = 15, HintText = "If enabled, player kingdom follows the same truce/no-war diplomacy gates as AI.")]
        public bool DiplomacyEnforcePlayerParity { get; set; } = true;

        // ─── WP5 Pressure Bands ───

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyBool("Enable pressure bands", Order = 20, HintText = "Replaces hard exhaustion thresholds with graded Low/Rising/Crisis bands and hysteresis. Produces smoother diplomatic transitions.")]
        public bool EnableDiplomacyPressureBands { get; set; } = true;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Rising band start", 1f, 200f, "0.0", Order = 21, HintText = "Exhaustion level at which kingdom enters Rising pressure band (elevated peace bias, softer war penalties).")]
        public float PressureBandRisingStart { get; set; } = 35f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Crisis band start", 1f, 200f, "0.0", Order = 22, HintText = "Exhaustion level at which kingdom enters Crisis band (war declarations blocked, strong peace pressure). Default: 85.")]
        public float PressureBandCrisisStart { get; set; } = 85f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Band hysteresis", 0f, 30f, "0.0", Order = 23, HintText = "Buffer below band threshold before dropping back to lower band. Prevents rapid up/down oscillation.")]
        public float PressureBandHysteresis { get; set; } = 5f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Peace bias (Low band)", 0f, 50f, "0.0", Order = 24, HintText = "Per-point peace support bias when kingdom is in Low pressure band.")]
        public float PeaceBiasBandLow { get; set; } = 1.5f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Peace bias (Rising band)", 0f, 100f, "0.0", Order = 25, HintText = "Per-point peace support bias when kingdom is in Rising pressure band.")]
        public float PeaceBiasBandHigh { get; set; } = 3.0f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("War support penalty cap", -5000f, 0f, "0", Order = 26, HintText = "Maximum total penalty applied to war declaration support in band mode. Replaces the hard -10000 override.")]
        public float WarSupportPenaltyCap { get; set; } = -400f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Peace support bonus cap", 0f, 5000f, "0", Order = 27, HintText = "Maximum total bonus applied to peace decision support in band mode. Replaces the hard +200 override.")]
        public float PeaceSupportBonusCap { get; set; } = 350f;

        // ─── Low-manpower diplomacy pressure (independent of war exhaustion) ───

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyBool("Enable manpower diplomacy pressure", Order = 30, HintText = "When a kingdom's average settlement manpower falls below the threshold, AI clans gain extra support for peace outcomes (independent of war exhaustion).")]
        public bool EnableManpowerDiplomacyPressure { get; set; } = true;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyInteger("Manpower diplomacy threshold %", 10, 80, "0", Order = 31, HintText = "Average settlement manpower fill below which peace pressure kicks in. E.g., 40 = pressure starts when average pool is below 40%.")]
        public int ManpowerDiplomacyThresholdPercent { get; set; } = 35;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 13)]
        [SettingPropertyFloatingInteger("Manpower diplomacy pressure strength", 0f, 1000f, "0", Order = 32, HintText = "Maximum peace-support bonus added when average manpower is at 0%. Scales linearly down to 0 as manpower approaches the threshold. Default: 100.")]
        public float ManpowerDiplomacyPressureStrength { get; set; } = 100f;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 98)]
        [SettingPropertyBool("Enable diplomacy debug logs", Order = 16, HintText = "Logs detailed reasons for forced-peace and war-gate decisions.")]
        public bool DiplomacyDebugLogs { get; set; } = false;

        [SettingPropertyGroup("Tooltips", GroupOrder = 97)]
        [SettingPropertyBool("Enable settlement manpower tooltips", Order = 0, HintText = "Appends manpower info to settlement property tooltips in campaign UI.")]
        public bool EnableSettlementManpowerTooltips { get; set; } = false;

        // ─── Slave Economy ───

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyBool("Enable slave economy", Order = 0, HintText = "Master toggle. Enables: (1) village-raid slave acquisition, (2) prisoner enslavement at the town 'Enslave prisoners' menu, (3) daily town bonuses while Slave goods are present in the town's civilian market.")]
        public bool EnableSlaveEconomy { get; set; } = true;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyInteger("Hearths per slave (raid)", 50, 2000, "0", Order = 1, HintText = "During a raid, 1 slave is added per this many village hearths, per loot event (same cadence as grain/clay appearing in the raid log). E.g., 300 = a 300-hearth village gives 1 slave per event; 600 hearths = 2. Villages below this threshold give 0 slaves per event. Default: 300 (historically balanced).")]
        public int SlaveHearthDivisor { get; set; } = 300;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyFloatingInteger("Slave effectiveness multiplier", 1.0f, 4.0f, "0.00", Order = 2, HintText = "Scales all daily town bonuses generated by slaves in the market (manpower, prosperity, construction). Higher = more effective slaves. Gold from selling slaves comes from the normal trade price in items.xml. Default: 1.5.")]
        public float SlaveRansomMultiplier { get; set; } = 1.5f;

        // SlaveManpowerPerUnit and SlaveManpowerCapPerTown REMOVED in v0.1.8.3.
        // Slaves are labor (construction, prosperity), not a recruitment pool.
        // Properties kept as hidden stubs so that existing MCM JSON files don't
        // cause deserialization errors on upgrade.
        [System.ComponentModel.Browsable(false)]
        public float SlaveManpowerPerUnit { get; set; } = 0f;
        [System.ComponentModel.Browsable(false)]
        public int SlaveManpowerCapPerTown { get; set; } = 0;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyFloatingInteger("Prosperity per slave per day", 0f, 0.1f, "0.0000", Order = 4, HintText = "Prosperity per slave per day (shown as 'Slave Labor' in Prosperity tooltip). At default effectiveness (1.5\u00d7): 0.0067 \u00d7 1.5 \u2248 0.01/slave. E.g., 100 slaves \u2192 ~1 prosperity/day. Default: 0.0067.")]
        public float SlaveProsperityPerUnit { get; set; } = 0.0067f;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyFloatingInteger("Construction bonus per slave per day", 0f, 3f, "0.00", Order = 5, HintText = "Construction progress per slave per day (same scale as vanilla daily rate ~130-180). Visible as 'Slave Labor +XX' in the construction tooltip. At default effectiveness (1.5\u00d7): 0.5 \u00d7 1.5 = 0.75/slave. E.g., 100 slaves = 75 progress/day (capped). Default: 0.5.")]
        public float SlaveConstructionAcceleration { get; set; } = 0.5f;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyFloatingInteger("Construction bonus cap (progress/day)", 0f, 500f, "0.0", Order = 6, HintText = "Maximum construction bonus per day from slave labour. Vanilla daily construction is ~130-180 for a typical town. Default: 150 (200 slaves at default settings max out the cap \u2014 roughly doubling build speed at peak).")]
        public float SlaveConstructionBonusCap { get; set; } = 150f;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyFloatingInteger("Food consumption per slave per day", 0f, 0.1f, "0.000", Order = 7, HintText = "Food consumed per slave per day. Creates a natural economic cap on slave hoarding. At 0.05: 50 slaves = -2.5 food/day, 100 slaves = -5.0 food/day (roughly a village's output), 200 slaves = -10.0 food/day (severe). Historically, enslaved labourers received subsistence rations comparable to garrison troops (~0.04–0.06 food units). Set to 0 to disable. Default: 0.05.")]
        public float SlaveFoodConsumptionPerUnit { get; set; } = 0.05f;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyFloatingInteger("Daily slave decay (%)", 0f, 10f, "0.00", Order = 8, HintText = "Percentage of the slave population lost per day (deaths, escapes, manumission). Without decay, slave populations grow forever. At 1%: 100 slaves lose 1/day. At 2%: 100 slaves lose 2/day. Creates equilibrium where inflow must match decay. Set to 0 to disable. Default: 1.0.")]
        public float SlaveDailyDecayPercent { get; set; } = 1.0f;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyFloatingInteger("Slave cap per prosperity", 0f, 0.1f, "0.0000", Order = 9, HintText = "Maximum slaves a town can hold per point of prosperity. Excess slaves are manumitted (freed) daily and converted to manpower. E.g., at 0.02: a 3000-prosperity town can hold 60 slaves; 5000 prosperity = 100. Set to 0 to disable the cap. Default: 0.02.")]
        public float SlaveCapPerProsperity { get; set; } = 0.02f;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyInteger("Slave cap minimum", 0, 100, "0", Order = 10, HintText = "Minimum slave cap regardless of prosperity. Even a low-prosperity town can hold at least this many slaves before manumission kicks in. Default: 10.")]
        public int SlaveCapMinimum { get; set; } = 10;

        [SettingPropertyGroup("Slave Economy", GroupOrder = 15)]
        [SettingPropertyFloatingInteger("Slave price decay rate", 0.90f, 0.999f, "0.000", Order = 11, HintText = "Controls how steeply slave prices decrease per unit of stock. Price factor = decayRate ^ stock, clamped at 0.1 (floor = 30d at base value 300). Lower values = steeper price drop. At 0.98 (default): stock 10 = ~245d, stock 30 = ~164d, stock 50 = ~109d, stock 80 = ~60d. At 0.96: stock 30 = ~88d, stock 50 = ~39d (floor). Default: 0.98.")]
        public float SlavePriceDecayRate { get; set; } = 0.98f;

        [SettingPropertyGroup("Legacy", GroupOrder = 99)]
        [SettingPropertyInteger("[Legacy] Construction bonus duration (days)", 1, 180, "0", Order = 12, HintText = "[LEGACY — NOT USED] Previously set how long the one-time construction bonus lasted after a slave sale. Superseded by the continuous market-based daily bonus in v3. Kept for save compatibility.")]
        public int SlaveConstructionBonusDays { get; set; } = 30;

        [SettingPropertyGroup("Legacy", GroupOrder = 99)]
        [SettingPropertyBool("Enable experimental map-hover fallback (legacy)", Order = 2, HintText = "Legacy reserved setting. Currently not used by active tooltip path.")]
        public bool EnableExperimentalHoverFallback { get; set; } = false;

        [SettingPropertyGroup("Legacy", GroupOrder = 99)]
        [SettingPropertyInteger("Tooltip refresh (ms) (legacy)", 100, 2000, "0", Order = 3, HintText = "Legacy reserved setting. Currently not used by active tooltip path.")]
        public int TooltipRefreshMs { get; set; } = 300;

        // ─── Minor Faction Economy ───

        [SettingPropertyGroup("Minor Faction Economy", GroupOrder = 19)]
        [SettingPropertyBool("Enable minor faction income boost", Order = 0, HintText = "Grants non-bandit minor factions a daily 'Frontier Revenue' stipend to prevent bankruptcy under tier-exponential wages. Mercenary clans receive less than unaligned factions (since mercs get contract pay).")]
        public bool EnableMinorFactionIncome { get; set; } = true;

        [SettingPropertyGroup("Minor Faction Economy", GroupOrder = 19)]
        [SettingPropertyInteger("Mercenary stipend per tier (denars/day)", 0, 1000, "0", Order = 1, HintText = "Daily income per clan tier for minor factions that are currently under mercenary contract. Lower than unaligned factions because they receive contract pay from their employer kingdom. Default: 250.")]
        public int MinorFactionMercenaryStipendPerTier { get; set; } = 250;

        [SettingPropertyGroup("Minor Faction Economy", GroupOrder = 19)]
        [SettingPropertyInteger("Unaligned stipend per tier (denars/day)", 0, 1500, "0", Order = 2, HintText = "Daily income per clan tier for minor factions NOT under any mercenary contract. Higher than mercenary stipend because they have no employer subsidising them. Default: 400.")]
        public int MinorFactionUnalignedStipendPerTier { get; set; } = 400;

        // ─── Provincial Governance ───

        [SettingPropertyGroup("Provincial Governance", GroupOrder = 20)]
        [SettingPropertyBool("Enable governance strain", Order = 0, HintText = "Wars, raids, sieges, and conquests accumulate governance strain on settlements. High strain reduces loyalty, security, and prosperity — modelling the administrative cost of prolonged conflict.")]
        public bool EnableGovernanceStrain { get; set; } = true;

        [SettingPropertyGroup("Provincial Governance", GroupOrder = 20)]
        [SettingPropertyFloatingInteger("Strain decay per day", 0.05f, 5f, "0.00", Order = 1, HintText = "How much governance strain decays per day toward 0. Lower = longer-lasting effects. Default: 0.3 (a +10 raid strain takes ~33 days to fully decay).")]
        public float GovernanceStrainDecayPerDay { get; set; } = 0.3f;

        [SettingPropertyGroup("Provincial Governance", GroupOrder = 20)]
        [SettingPropertyFloatingInteger("Max loyalty penalty at full strain", 0f, 10f, "0.0", Order = 2, HintText = "Loyalty penalty per day when strain is at the cap. Scales linearly from 0 at strain 0. Default: 3.0.")]
        public float GovernanceMaxLoyaltyPenalty { get; set; } = 3.0f;

        [SettingPropertyGroup("Provincial Governance", GroupOrder = 20)]
        [SettingPropertyFloatingInteger("Max security penalty at full strain", 0f, 10f, "0.0", Order = 3, HintText = "Security penalty per day when strain is at the cap. Default: 2.0.")]
        public float GovernanceMaxSecurityPenalty { get; set; } = 2.0f;

        [SettingPropertyGroup("Provincial Governance", GroupOrder = 20)]
        [SettingPropertyFloatingInteger("Max prosperity penalty at full strain", 0f, 10f, "0.0", Order = 4, HintText = "Prosperity penalty per day when strain is at the cap. Default: 1.0.")]
        public float GovernanceMaxProsperityPenalty { get; set; } = 1.0f;

        [SettingPropertyGroup("Provincial Governance", GroupOrder = 20)]
        [SettingPropertyFloatingInteger("Strain cap", 10f, 200f, "0", Order = 5, HintText = "Maximum governance strain a settlement can accumulate. Default: 100.")]
        public float GovernanceStrainCap { get; set; } = 100f;

        [SettingPropertyGroup("Provincial Governance", GroupOrder = 20)]
        [SettingPropertyFloatingInteger("Max combined prosperity penalty", -20f, 0f, "0.0", Order = 6, HintText = "Floor for the combined daily prosperity penalty from Governance Strain + Devastated Hinterlands. Prevents runaway settlement death spirals when both systems stack. Default: -8.0.")]
        public float MaxCombinedModProsperityPenalty { get; set; } = -8.0f;

        // ─── Frontier Devastation ───

        [SettingPropertyGroup("Frontier Devastation", GroupOrder = 21)]
        [SettingPropertyBool("Enable frontier devastation", Order = 0, HintText = "Village raids accumulate persistent devastation (0-100) that decays slowly. Devastated villages suffer reduced hearth growth, and their bound town/castle suffers prosperity, food, and security penalties.")]
        public bool EnableFrontierDevastation { get; set; } = true;

        [SettingPropertyGroup("Frontier Devastation", GroupOrder = 21)]
        [SettingPropertyFloatingInteger("Devastation per raid", 5f, 50f, "0", Order = 1, HintText = "Devastation added to a village when its loot event completes. Two raids within the decay window stack. Default: 25.")]
        public float DevastationPerRaid { get; set; } = 25f;

        [SettingPropertyGroup("Frontier Devastation", GroupOrder = 21)]
        [SettingPropertyFloatingInteger("Devastation decay per day", 0.1f, 5f, "0.00", Order = 2, HintText = "Daily devastation decay while the village is in Normal state. Frozen during Looted/BeingRaided. Default: 0.5 (a single raid takes 50 days to fully heal).")]
        public float DevastationDecayPerDay { get; set; } = 0.5f;

        [SettingPropertyGroup("Frontier Devastation", GroupOrder = 21)]
        [SettingPropertyFloatingInteger("Max hearth penalty (per village)", 0f, 10f, "0.0", Order = 3, HintText = "Hearth growth penalty per day applied directly to the village at devastation 100. Default: 2.0.")]
        public float DevastationMaxHearthPenalty { get; set; } = 2.0f;

        [SettingPropertyGroup("Frontier Devastation", GroupOrder = 21)]
        [SettingPropertyFloatingInteger("Max prosperity penalty (bound town)", 0f, 10f, "0.0", Order = 4, HintText = "Prosperity penalty per day applied to the bound town/castle at avg bound village devastation 100. Default: 2.0.")]
        public float DevastationMaxProsperityPenalty { get; set; } = 2.0f;

        [SettingPropertyGroup("Frontier Devastation", GroupOrder = 21)]
        [SettingPropertyFloatingInteger("Max security penalty (bound town)", 0f, 10f, "0.0", Order = 5, HintText = "Security penalty per day applied to the bound town/castle at avg bound village devastation 100. Default: 1.5.")]
        public float DevastationMaxSecurityPenalty { get; set; } = 1.5f;

        [SettingPropertyGroup("Frontier Devastation", GroupOrder = 21)]
        [SettingPropertyFloatingInteger("Max food penalty per village", 0f, 5f, "0.0", Order = 6, HintText = "Food supply penalty per devastated village at devastation 100. Summed across all bound villages. E.g., 3 villages at dev 50 = 3 × 0.5 × 1.5 = 2.25 food/day lost. Default: 1.5.")]
        public float DevastationMaxFoodPenaltyPerVillage { get; set; } = 1.5f;

        // ─── Castle Recruitment ───

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyBool("Enable castle recruitment", Order = 0, HintText = "Master toggle. Enables castle prisoner auto-processing (T1-T3 enslaved to nearest town market, T4+ become recruitable after a waiting period) and the castle recruitment menu.")]
        public bool EnableCastleRecruitment { get; set; } = true;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("Auto-enslave tier max", 1, 6, "0", Order = 1, HintText = "Unified enslavement tier cap (player + AI parity). Prisoners at or below this tier can be enslaved: (1) player 'Enslave prisoners' at towns, (2) AI auto-enslave at towns, (3) castle auto-enslave to nearest market. Prisoners above this tier must be taken to castles for recruitment conversion or ransomed. Default: 3 (T1-T3 enslaved, T4+ recruitable).")]
        public int CastlePrisonerAutoEnslaveTierMax { get; set; } = 3;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("T4 recruitment wait (days)", 1, 60, "0", Order = 2, HintText = "Days a Tier 4 prisoner must be held before becoming recruitable. Represents the time needed to break resistance and negotiate loyalty. Default: 7.")]
        public int CastleRecruitT4Days { get; set; } = 7;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("T5 recruitment wait (days)", 1, 60, "0", Order = 3, HintText = "Days a Tier 5 prisoner must be held before becoming recruitable. Elite troops resist longer. Default: 14.")]
        public int CastleRecruitT5Days { get; set; } = 14;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("T6+ recruitment wait (days)", 1, 60, "0", Order = 4, HintText = "Days a Tier 6+ prisoner must be held before becoming recruitable. Champion-tier troops resist the longest. Default: 21.")]
        public int CastleRecruitT6Days { get; set; } = 21;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("T4 recruitment gold cost", 100, 10000, "0", Order = 5, HintText = "Gold cost to recruit a Tier 4 prisoner. Follows the tier-exponential philosophy: elite troops are expensive to turn. Default: 1200.")]
        public int CastleRecruitGoldT4 { get; set; } = 1200;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("T5 recruitment gold cost", 100, 20000, "0", Order = 6, HintText = "Gold cost to recruit a Tier 5 prisoner. Default: 2500.")]
        public int CastleRecruitGoldT5 { get; set; } = 2500;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("T6+ recruitment gold cost", 100, 50000, "0", Order = 7, HintText = "Gold cost to recruit a Tier 6+ prisoner. Default: 5000.")]
        public int CastleRecruitGoldT6 { get; set; } = 5000;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyBool("Castle recruitment drains manpower", Order = 8, HintText = "When enabled, recruiting a prisoner from a castle also drains the castle's manpower pool (same cost formula as normal recruitment). Creates a double gate: gold + manpower. Default: true.")]
        public bool CastleRecruitDrainsManpower { get; set; } = true;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("Elite pool max per castle", 5, 100, "0", Order = 9, HintText = "Maximum number of culture-based elite troops (T4-T6) a castle can hold at once. The pool regenerates daily from the castle's manpower. Default: 20.")]
        public int CastleElitePoolMax { get; set; } = 20;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("Elite regen min per day", 0, 10, "0", Order = 10, HintText = "Minimum elite troops regenerated per castle per day (at zero prosperity). Default: 1.")]
        public int CastleEliteRegenMin { get; set; } = 1;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("Elite regen max per day", 1, 20, "0", Order = 11, HintText = "Maximum elite troops regenerated per castle per day (at max prosperity). Actual amount scales linearly with prosperity. Default: 3.")]
        public int CastleEliteRegenMax { get; set; } = 3;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("Elite regen manpower cost", 1, 100, "0", Order = 12, HintText = "Manpower drained from the castle's pool for each elite troop regenerated. Higher = slower elite regen in low-manpower regions. Default: 1.")]
        public int CastleEliteManpowerCost { get; set; } = 1;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyBool("AI recruits from castle", Order = 13, HintText = "When enabled, AI lord parties visiting their own faction's castles recruit from both the elite pool and converted prisoners, paying gold per troop (same as player). No daily cap — lords fill to party limit. Default: true.")]
        public bool CastleEliteAiRecruits { get; set; } = true;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyInteger("Castle holding fee %", 5, 50, "0", Order = 14, HintText = "Commission the castle owner receives when another clan's prisoners are processed (enslaved or recruited). The depositing lord gets the remainder. Example: 30% fee on a 1,200g recruitment → owner gets 360g, depositor gets 840g. Default: 30.")]
        public int CastleHoldingFeePercent { get; set; } = 30;

        [SettingPropertyGroup("Castle Recruitment", GroupOrder = 22)]
        [SettingPropertyBool("Open castle access (early game)", Order = 15, HintText = "Removes the clan-tier bribe (~800g) for entering castle lord's halls and allows entry to neutral castles even with slightly negative owner relations. Hostile castles and crime-based restrictions are unaffected. Needed for early-game prisoner deposit and recruitment. Default: true.")]
        public bool CastleOpenAccess { get; set; } = true;

        // ─── Village Investment (Patronage) ───

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyBool("Enable village investment", Order = 0, HintText = "Master toggle. Enables the village investment (patronage) system — spend gold at villages for hearth growth, notable relations, influence, and notable power. AI lords also invest in their own villages. Default: true.")]
        public bool EnableVillageInvestment { get; set; } = true;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Modest investment cost", 500, 10000, "0", Order = 1, HintText = "Gold cost for a Modest investment. Smallest tier with proportionally smaller bonuses. Default: 2000.")]
        public int VillageInvestCostModest { get; set; } = 2000;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Generous investment cost", 1000, 25000, "0", Order = 2, HintText = "Gold cost for a Generous investment. Middle tier with balanced bonuses. Default: 5000.")]
        public int VillageInvestCostGenerous { get; set; } = 5000;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Grand investment cost", 2000, 50000, "0", Order = 3, HintText = "Gold cost for a Grand investment. Highest tier with the strongest bonuses and longest duration. Default: 12000.")]
        public int VillageInvestCostGrand { get; set; } = 12000;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Modest duration (days)", 5, 120, "0", Order = 4, HintText = "How many days the Modest investment hearth bonus lasts. Also serves as the cooldown — no re-investment until expired. Default: 20.")]
        public int VillageInvestDurationModest { get; set; } = 20;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Generous duration (days)", 10, 180, "0", Order = 5, HintText = "How many days the Generous investment hearth bonus lasts. Also serves as the cooldown. Default: 30.")]
        public int VillageInvestDurationGenerous { get; set; } = 30;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Grand duration (days)", 15, 240, "0", Order = 6, HintText = "How many days the Grand investment hearth bonus lasts. Also serves as the cooldown. Default: 45.")]
        public int VillageInvestDurationGrand { get; set; } = 45;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyFloatingInteger("Modest hearth bonus/day", 0.05f, 5f, "0.00", Order = 7, HintText = "Daily hearth growth bonus from a Modest investment. Visible in village tooltip as 'Patronage'. Default: 0.3.")]
        public float VillageInvestHearthModest { get; set; } = 0.3f;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyFloatingInteger("Generous hearth bonus/day", 0.1f, 5f, "0.00", Order = 8, HintText = "Daily hearth growth bonus from a Generous investment. Default: 0.6.")]
        public float VillageInvestHearthGenerous { get; set; } = 0.6f;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyFloatingInteger("Grand hearth bonus/day", 0.2f, 10f, "0.00", Order = 9, HintText = "Daily hearth growth bonus from a Grand investment. Default: 1.0.")]
        public float VillageInvestHearthGrand { get; set; } = 1.0f;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Modest relation gain", 0, 20, "0", Order = 10, HintText = "Relation gained with each village notable from a Modest investment. Default: 3.")]
        public int VillageInvestRelationModest { get; set; } = 3;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Generous relation gain", 0, 30, "0", Order = 11, HintText = "Relation gained with each village notable from a Generous investment. Default: 6.")]
        public int VillageInvestRelationGenerous { get; set; } = 6;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Grand relation gain", 0, 50, "0", Order = 12, HintText = "Relation gained with each village notable from a Grand investment. Default: 10.")]
        public int VillageInvestRelationGrand { get; set; } = 10;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyFloatingInteger("Modest influence gain", 0f, 10f, "0.0", Order = 13, HintText = "Influence gained from a Modest investment (only if village is in your kingdom). Default: 2.5.")]
        public float VillageInvestInfluenceModest { get; set; } = 2.5f;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyFloatingInteger("Generous influence gain", 0f, 20f, "0.0", Order = 14, HintText = "Influence gained from a Generous investment (only if village is in your kingdom). Default: 5.")]
        public float VillageInvestInfluenceGenerous { get; set; } = 5.0f;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyFloatingInteger("Grand influence gain", 0f, 30f, "0.0", Order = 15, HintText = "Influence gained from a Grand investment (only if village is in your kingdom). Default: 10.")]
        public float VillageInvestInfluenceGrand { get; set; } = 10.0f;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Modest power gain", 0, 50, "0", Order = 16, HintText = "Power added to each village notable from a Modest investment. Higher power = higher-tier volunteers. Default: 5.")]
        public int VillageInvestPowerModest { get; set; } = 5;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Generous power gain", 0, 75, "0", Order = 17, HintText = "Power added to each village notable from a Generous investment. Default: 10.")]
        public int VillageInvestPowerGenerous { get; set; } = 10;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Grand power gain", 0, 100, "0", Order = 18, HintText = "Power added to each village notable from a Grand investment. Default: 20.")]
        public int VillageInvestPowerGrand { get; set; } = 20;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Notable power cap", 50, 500, "0", Order = 19, HintText = "Maximum notable power allowed via investment. Investment power bonus is skipped if notable already at or above this level. Prevents absurdly high volunteer tiers. Default: 200.")]
        public int VillageInvestPowerCap { get; set; } = 200;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("Cross-clan relation gain", 0, 20, "0", Order = 20, HintText = "Relation gained with the village owner's clan leader when investing in another clan's village. Creates a diplomatic investment path. Default: 2.")]
        public int VillageInvestCrossClanRelation { get; set; } = 2;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyBool("AI village investment", Order = 21, HintText = "When enabled, AI lords invest in their own faction's villages when visiting them (if they can afford it). Uses the same tier costs and provides the same bonuses. Default: true.")]
        public bool VillageInvestAiEnabled { get; set; } = true;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("AI gold safety multiplier", 2, 20, "0", Order = 22, HintText = "AI will only invest when their gold exceeds the tier cost multiplied by this value. Higher = more conservative AI spending (e.g., 5 means a lord needs 60,000d to pick Grand). Default: 5.")]
        public int VillageInvestAiGoldMultiplier { get; set; } = 5;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("AI investment chance (%)", 5, 100, "0", Order = 23, HintText = "Percentage chance that an AI lord will invest when entering an eligible village (after all other checks pass). Lower values reduce overall AI investment frequency. Default: 40.")]
        public int VillageInvestAiChance { get; set; } = 40;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyBool("AI random tier selection", Order = 24, HintText = "When enabled, AI randomly picks from all affordable tiers instead of always choosing the highest. Creates more natural variation — rich lords won't always pick Grand. Default: true.")]
        public bool VillageInvestAiRandomTier { get; set; } = true;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("AI hero cooldown (days)", 0, 30, "0", Order = 25, HintText = "Minimum in-game days between any two investments by the same AI lord (across all villages). Prevents carpet-bombing every village on a travel route. Set to 0 to disable. Default: 3.")]
        public int VillageInvestAiHeroCooldownDays { get; set; } = 3;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyInteger("AI hearth ceiling", 0, 2000, "0", Order = 26, HintText = "AI will not invest in villages with hearth at or above this value, focusing patronage on poorer villages that need it most. Set to 0 to disable (invest anywhere). Default: 600.")]
        public int VillageInvestAiHearthCeiling { get; set; } = 600;

        [SettingPropertyGroup("Village Investment", GroupOrder = 23)]
        [SettingPropertyBool("Notify when AI invests in your villages", Order = 27, HintText = "Shows a message when an AI lord invests in a village you own. Lets you see the patronage system working in your territory. Default: true.")]
        public bool VillageInvestNotifyPlayer { get; set; } = true;

        // ─── Town Investment (Civic Patronage) ───

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyBool("Enable town investment", Order = 0, HintText = "Master toggle. Enables the town investment (civic patronage) system — spend gold at non-hostile towns for a daily prosperity bonus, notable relations, influence, and notable power. AI lords also invest in their own towns. Default: true.")]
        public bool EnableTownInvestment { get; set; } = true;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Modest investment cost", 1000, 25000, "0", Order = 1, HintText = "Gold cost for a Modest town investment. Default: 5000.")]
        public int TownInvestCostModest { get; set; } = 5000;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Generous investment cost", 3000, 50000, "0", Order = 2, HintText = "Gold cost for a Generous town investment. Default: 15000.")]
        public int TownInvestCostGenerous { get; set; } = 15000;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Grand investment cost", 5000, 100000, "0", Order = 3, HintText = "Gold cost for a Grand town investment. Highest tier with the strongest bonuses and longest duration. Default: 40000.")]
        public int TownInvestCostGrand { get; set; } = 40000;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Modest duration (days)", 5, 120, "0", Order = 4, HintText = "How many days the Modest town investment lasts. Also serves as the cooldown. Default: 20.")]
        public int TownInvestDurationModest { get; set; } = 20;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Generous duration (days)", 10, 180, "0", Order = 5, HintText = "How many days the Generous town investment lasts. Also serves as the cooldown. Default: 40.")]
        public int TownInvestDurationGenerous { get; set; } = 40;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Grand duration (days)", 15, 240, "0", Order = 6, HintText = "How many days the Grand town investment lasts. Also serves as the cooldown. Default: 60.")]
        public int TownInvestDurationGrand { get; set; } = 60;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyFloatingInteger("Modest prosperity bonus/day", 0.1f, 5f, "0.00", Order = 7, HintText = "Daily prosperity growth bonus from a Modest town investment. Visible in the town tooltip as 'Civic Patronage'. Default: 0.5.")]
        public float TownInvestProsperityModest { get; set; } = 0.5f;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyFloatingInteger("Generous prosperity bonus/day", 0.2f, 10f, "0.00", Order = 8, HintText = "Daily prosperity growth bonus from a Generous town investment. Default: 1.0.")]
        public float TownInvestProsperityGenerous { get; set; } = 1.0f;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyFloatingInteger("Grand prosperity bonus/day", 0.5f, 15f, "0.00", Order = 9, HintText = "Daily prosperity growth bonus from a Grand town investment. Default: 2.0.")]
        public float TownInvestProsperityGrand { get; set; } = 2.0f;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Modest relation gain", 0, 20, "0", Order = 10, HintText = "Relation gained with each town notable from a Modest investment. Default: 3.")]
        public int TownInvestRelationModest { get; set; } = 3;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Generous relation gain", 0, 30, "0", Order = 11, HintText = "Relation gained with each town notable from a Generous investment. Default: 6.")]
        public int TownInvestRelationGenerous { get; set; } = 6;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Grand relation gain", 0, 50, "0", Order = 12, HintText = "Relation gained with each town notable from a Grand investment. Default: 10.")]
        public int TownInvestRelationGrand { get; set; } = 10;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyFloatingInteger("Modest influence gain", 0f, 25f, "0.0", Order = 13, HintText = "Influence gained from a Modest town investment (only if town is in your kingdom). Default: 10.")]
        public float TownInvestInfluenceModest { get; set; } = 10.0f;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyFloatingInteger("Generous influence gain", 0f, 50f, "0.0", Order = 14, HintText = "Influence gained from a Generous town investment (only if town is in your kingdom). Default: 25.")]
        public float TownInvestInfluenceGenerous { get; set; } = 25.0f;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyFloatingInteger("Grand influence gain", 0f, 100f, "0.0", Order = 15, HintText = "Influence gained from a Grand town investment (only if town is in your kingdom). Default: 50.")]
        public float TownInvestInfluenceGrand { get; set; } = 50.0f;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Modest power gain", 0, 50, "0", Order = 16, HintText = "Power added to each town notable from a Modest investment. Default: 5.")]
        public int TownInvestPowerModest { get; set; } = 5;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Generous power gain", 0, 75, "0", Order = 17, HintText = "Power added to each town notable from a Generous investment. Default: 10.")]
        public int TownInvestPowerGenerous { get; set; } = 10;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Grand power gain", 0, 100, "0", Order = 18, HintText = "Power added to each town notable from a Grand investment. Default: 20.")]
        public int TownInvestPowerGrand { get; set; } = 20;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Notable power cap", 50, 500, "0", Order = 19, HintText = "Maximum notable power allowed via town investment. Investment power bonus is skipped if notable already at or above this level. Default: 200.")]
        public int TownInvestPowerCap { get; set; } = 200;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("Cross-clan relation gain", 0, 20, "0", Order = 20, HintText = "Relation gained with the town owner's clan leader when investing in another clan's town. Default: 2.")]
        public int TownInvestCrossClanRelation { get; set; } = 2;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyBool("AI town investment", Order = 21, HintText = "When enabled, AI lords invest in their own faction's towns when visiting them. Uses the same tier costs and provides the same bonuses. Default: true.")]
        public bool TownInvestAiEnabled { get; set; } = true;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("AI gold safety multiplier", 2, 30, "0", Order = 22, HintText = "AI will only invest when their gold exceeds the tier cost multiplied by this value. Higher for towns since costs are higher (e.g., 15 means a lord needs 600,000d for Grand). Default: 15.")]
        public int TownInvestAiGoldMultiplier { get; set; } = 15;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("AI investment chance (%)", 5, 100, "0", Order = 23, HintText = "Percentage chance that an AI lord will invest when entering an eligible town. Default: 30.")]
        public int TownInvestAiChance { get; set; } = 30;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyBool("AI random tier selection", Order = 24, HintText = "When enabled, AI randomly picks from all affordable tiers instead of always choosing the highest. Default: true.")]
        public bool TownInvestAiRandomTier { get; set; } = true;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("AI hero cooldown (days)", 0, 30, "0", Order = 25, HintText = "Minimum in-game days between any two town investments by the same AI lord. Prevents carpet-bombing every town on a travel route. Set to 0 to disable. Default: 5.")]
        public int TownInvestAiHeroCooldownDays { get; set; } = 5;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyInteger("AI prosperity ceiling", 0, 15000, "0", Order = 26, HintText = "AI will not invest in towns with prosperity at or above this value, focusing patronage on less prosperous towns. Set to 0 to disable. Default: 5000.")]
        public int TownInvestAiProsperityCeiling { get; set; } = 5000;

        [SettingPropertyGroup("Town Investment", GroupOrder = 24)]
        [SettingPropertyBool("Notify when AI invests in your towns", Order = 27, HintText = "Shows a message when an AI lord invests in a town you own. Default: true.")]
        public bool TownInvestNotifyPlayer { get; set; } = true;

        // ─── Clan Survival ───

        [SettingPropertyGroup("Clan Survival", GroupOrder = 25)]
        [SettingPropertyBool("Enable clan survival", Order = 0, HintText = "Master toggle. When a kingdom is destroyed, eligible clans are rescued instead of annihilated. They become independent factions during a grace period, then seek mercenary service with a culture-weighted kingdom. Default: true.")]
        public bool EnableClanSurvival { get; set; } = true;

        [SettingPropertyGroup("Clan Survival", GroupOrder = 25)]
        [SettingPropertyInteger("Grace period (days)", 1, 120, "0", Order = 1, HintText = "Number of in-game days a rescued clan stays independent before seeking mercenary service. During this period they patrol near their home settlement. Default: 30.")]
        public int ClanSurvivalGracePeriodDays { get; set; } = 30;

        [SettingPropertyGroup("Clan Survival", GroupOrder = 25)]
        [SettingPropertyFloatingInteger("Culture match weight", 0f, 10f, "0.0", Order = 2, HintText = "How strongly same-culture kingdoms are preferred when assigning mercenary service. Higher values make culture almost mandatory. At 2.0, same-culture kingdoms score 3× higher. Default: 2.0.")]
        public float ClanSurvivalCultureWeight { get; set; } = 2.0f;
    }
}
