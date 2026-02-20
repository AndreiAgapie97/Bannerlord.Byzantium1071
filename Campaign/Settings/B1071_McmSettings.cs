using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace Byzantium1071.Campaign.Settings
{
    public sealed class B1071_McmSettings : AttributeGlobalSettings<B1071_McmSettings>
    {
        // Shared fallback instance — avoids creating multiple AttributeGlobalSettings
        // objects that could confuse MCM's lifecycle. All consumers share this one.
        private static B1071_McmSettings? _defaults;
        internal static B1071_McmSettings Defaults => _defaults ??= new();

        public override string Id => "Byzantium1071";
        public override string DisplayName => "Byzantium 1071";
        public override string FolderName => "Byzantium1071";
        public override string FormatType => "json";

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
        [SettingPropertyInteger("Minimum daily regen", 0, 500, "0", Order = 8, HintText = "Absolute minimum manpower regenerated per day.")]
        public int MinimumDailyRegen { get; set; } = 1;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Regen cap % of pool/day", 0.1f, 20f, "0.00", Order = 9, HintText = "Hard cap on daily regen as a percent of max pool. E.g., 2.0 means a 3000-pool town regens at most 60/day (~50 days to full).")]
        public float RegenCapPercent { get; set; } = 2.0f;

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

        [SettingPropertyGroup("Recruitment Cost", GroupOrder = 4)]
        [SettingPropertyInteger("Tiers per +1 cost step", 1, 10, "0", Order = 1, HintText = "How many tiers are needed before manpower cost increases by 1.")]
        public int TiersPerExtraCost { get; set; } = 2;

        [SettingPropertyGroup("Recruitment Cost", GroupOrder = 4)]
        [SettingPropertyInteger("Cost multiplier %", 1, 1000, "0", Order = 2, HintText = "Final manpower cost multiplier applied to tier-based cost.")]
        public int CostMultiplierPercent { get; set; } = 100;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 12)]
        [SettingPropertyBool("Show player debug messages", Order = 0, HintText = "Shows manpower messages in the game UI for player actions.")]
        public bool ShowPlayerDebugMessages { get; set; } = true;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 12)]
        [SettingPropertyBool("Use OnUnitRecruited fallback for player", Order = 1, HintText = "Consumes manpower for player recruits on per-click event.")]
        public bool UseOnUnitRecruitedFallbackForPlayer { get; set; } = true;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 12)]
        [SettingPropertyBool("Log AI manpower consumption", Order = 2, HintText = "Logs AI manpower consumption bands to the Bannerlord logs.")]
        public bool LogAiManpowerConsumption { get; set; } = true;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 12)]
        [SettingPropertyBool("Enable telemetry debug logs", Order = 3, HintText = "Logs structured WP1 telemetry events (regen snapshots, diplomacy rationale, truce/forced peace updates).")]
        public bool TelemetryDebugLogs { get; set; } = false;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 12)]
        [SettingPropertyBool("Show telemetry in Current tab", Order = 4, HintText = "Adds compact instrumentation rows to the Current ledger tab for balancing/debugging.")]
        public bool ShowTelemetryInOverlay { get; set; } = false;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyBool("Enable in-game overlay", Order = 0, HintText = "Enables the standalone Byzantium 1071 overlay on campaign map.")]
        public bool EnableOverlay { get; set; } = true;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyBool("Enable hotkey toggle", Order = 1, HintText = "Press the chosen key on campaign map to show/hide the overlay.")]
        public bool EnableOverlayHotkey { get; set; } = true;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyInteger("Hotkey (0-6)", 0, 6, "0", Order = 2, HintText = "0=M, 1=N, 2=K, 3=F9, 4=F10, 5=F11, 6=F12. Default: 0 (M).")]
        public int OverlayHotkeyChoice { get; set; } = 0;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyBool("Show settlement name", Order = 3, HintText = "Adds current settlement name to overlay text when available.")]
        public bool OverlayShowSettlementName { get; set; } = true;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyBool("Show pool identifier", Order = 4, HintText = "Shows the pool settlement used for manpower calculations.")]
        public bool OverlayShowPoolName { get; set; } = false;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyInteger("Ledger default tab (0-7)", 0, 7, "0", Order = 5, HintText = "0=Current, 1=Nearby, 2=Castles, 3=Towns, 4=Villages, 5=Factions, 6=Armies, 7=Wars.")]
        public int OverlayLedgerDefaultTab { get; set; } = 0;

        [SettingPropertyGroup("Overlay", GroupOrder = 6)]
        [SettingPropertyInteger("Ledger rows per page", 3, 15, "0", Order = 6, HintText = "How many ledger rows are shown per page.")]
        public int OverlayLedgerRowsPerPage { get; set; } = 7;

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
        [SettingPropertyInteger("Siege devastate retain %", 0, 100, "0", Order = 2, HintText = "% of max pool retained after choosing Devastate.")]
        public int SiegeDevastateRetainPercent { get; set; } = 10;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Siege pillage retain %", 0, 100, "0", Order = 3, HintText = "% of max pool retained after choosing Pillage.")]
        public int SiegePillageRetainPercent { get; set; } = 40;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Siege mercy retain %", 0, 100, "0", Order = 4, HintText = "% of max pool retained after choosing Show Mercy.")]
        public int SiegeMercyRetainPercent { get; set; } = 70;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyFloatingInteger("Battle casualty drain multiplier", 0f, 2f, "0.00", Order = 5, HintText = "Each battle casualty drains this × 1 manpower from the party's home pool.")]
        public float BattleCasualtyDrainMultiplier { get; set; } = 0.5f;

        [SettingPropertyGroup("War Effects", GroupOrder = 7)]
        [SettingPropertyInteger("Conquest pool retain %", 0, 100, "0", Order = 6, HintText = "% of current pool retained when settlement changes hands.")]
        public int ConquestPoolRetainPercent { get; set; } = 50;

        // ─── Immersion Modifiers ───

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyBool("Enable seasonal regen", Order = 0, HintText = "Spring/summer boosts regen, winter penalizes it.")]
        public bool EnableSeasonalRegen { get; set; } = true;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyInteger("Spring/summer regen %", 50, 200, "0", Order = 1, HintText = "Regen multiplier during spring and summer campaign seasons.")]
        public int SpringSummerRegenMultiplier { get; set; } = 115;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyInteger("Winter regen %", 25, 100, "0", Order = 2, HintText = "Regen multiplier during winter. E.g., 75 = regen at 75% rate.")]
        public int WinterRegenMultiplier { get; set; } = 75;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyBool("Enable peace dividend", Order = 3, HintText = "Pools regen faster when your kingdom is at peace with all factions.")]
        public bool EnablePeaceDividend { get; set; } = true;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyInteger("Peace dividend regen %", 100, 200, "0", Order = 4, HintText = "Regen multiplier when kingdom is at peace. E.g., 125 = 25% bonus.")]
        public int PeaceDividendMultiplier { get; set; } = 125;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyBool("Enable culture discount", Order = 5, HintText = "Recruiting in matching-culture settlements costs less manpower.")]
        public bool EnableCultureDiscount { get; set; } = true;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyInteger("Culture cost %", 50, 100, "0", Order = 6, HintText = "Manpower cost % when recruiting from matching culture. 75 = troops cost 75% of normal manpower (25% discount).")]
        public int CultureCostPercent { get; set; } = 75;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyBool("Enable governor bonus", Order = 7, HintText = "Governor Steward skill boosts regen; Leadership boosts max pool.")]
        public bool EnableGovernorBonus { get; set; } = true;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyFloatingInteger("Governor steward regen divisor", 100f, 500000f, "0", Order = 8, HintText = "Steward skill is divided by this, then divided by 100 to get additional regen %. E.g., 100000 means 200 Steward adds +0.002 regen pct (0.2% of pool/day).")]
        public float GovernorStewardRegenDivisor { get; set; } = 100000f;

        [SettingPropertyGroup("Immersion Modifiers", GroupOrder = 8)]
        [SettingPropertyFloatingInteger("Governor leadership pool divisor", 100f, 5000f, "0", Order = 9, HintText = "Leadership skill / this value multiplies max pool. E.g., 500 means 200 Leadership adds +40% to max.")]
        public float GovernorLeadershipPoolDivisor { get; set; } = 500f;

        // ─── Alerts & Militia ───

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 9)]
        [SettingPropertyBool("Enable manpower alerts", Order = 0, HintText = "Shows a warning when your settlements' manpower drops below the threshold.")]
        public bool EnableManpowerAlerts { get; set; } = true;

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 9)]
        [SettingPropertyInteger("Alert threshold %", 5, 50, "0", Order = 1, HintText = "Pool % below which a crisis alert is shown.")]
        public int AlertThresholdPercent { get; set; } = 25;

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 9)]
        [SettingPropertyBool("Enable militia-manpower link", Order = 2, HintText = "Militia growth scales with manpower ratio. Depleted pools produce less militia.")]
        public bool EnableMilitiaLink { get; set; } = true;

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 9)]
        [SettingPropertyInteger("Militia min scale % at 0% MP", 0, 100, "0", Order = 3, HintText = "Militia growth multiplier when manpower is at 0%.")]
        public int MilitiaManpowerMinScale { get; set; } = 0;

        [SettingPropertyGroup("Alerts & Militia", GroupOrder = 9)]
        [SettingPropertyInteger("Militia max scale % at 100% MP", 50, 200, "0", Order = 4, HintText = "Militia growth multiplier when manpower is at 100%.")]
        public int MilitiaManpowerMaxScale { get; set; } = 100;

        // ─── War Exhaustion ───

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 10)]
        [SettingPropertyBool("Enable war exhaustion", Order = 0, HintText = "Tracks per-kingdom exhaustion from battles, raids, and sieges. Reduces regen when high.")]
        public bool EnableWarExhaustion { get; set; } = true;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Daily decay", 0.1f, 5f, "0.0", Order = 1, HintText = "How much exhaustion decays per day toward 0.")]
        public float ExhaustionDailyDecay { get; set; } = 0.5f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Regen penalty divisor", 50f, 500f, "0", Order = 2, HintText = "Regen is multiplied by (1 - exhaustion/divisor). Higher = softer penalty. E.g., 200 means 100 exhaustion halves regen.")]
        public float ExhaustionRegenDivisor { get; set; } = 200f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Max exhaustion score", 50f, 200f, "0", Order = 3, HintText = "Maximum exhaustion a kingdom can accumulate.")]
        public float ExhaustionMaxScore { get; set; } = 100f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Battle exhaustion per casualty", 0.001f, 0.1f, "0.000", Order = 4, HintText = "Exhaustion gained per battle casualty.")]
        public float BattleExhaustionPerCasualty { get; set; } = 0.001f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Raid exhaustion gain", 0f, 10f, "0.0", Order = 5, HintText = "Exhaustion gained by defending kingdom when a village is raided.")]
        public float RaidExhaustionGain { get; set; } = 2f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Siege exhaustion (defender)", 0f, 20f, "0.0", Order = 6, HintText = "Exhaustion gained by defending kingdom after siege aftermath.")]
        public float SiegeExhaustionDefender { get; set; } = 5f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Siege exhaustion (attacker)", 0f, 20f, "0.0", Order = 7, HintText = "Exhaustion gained by attacking kingdom after siege aftermath.")]
        public float SiegeExhaustionAttacker { get; set; } = 3f;

        [SettingPropertyGroup("War Exhaustion", GroupOrder = 10)]
        [SettingPropertyFloatingInteger("Conquest exhaustion gain", 0f, 20f, "0.0", Order = 8, HintText = "Exhaustion gained by losing kingdom when a settlement changes hands.")]
        public float ConquestExhaustionGain { get; set; } = 4f;

        // ─── Diplomacy (War Exhaustion) ───

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyBool("Enable diplomacy pressure", Order = 0, HintText = "War exhaustion directly affects AI kingdom war/peace decisions.")]
        public bool EnableExhaustionDiplomacyPressure { get; set; } = true;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyFloatingInteger("No-new-war threshold", 1f, 200f, "0.0", Order = 1, HintText = "At or above this exhaustion, AI kingdoms are prevented from starting new wars.")]
        public float DiplomacyNoNewWarThreshold { get; set; } = 55f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyFloatingInteger("Peace pressure threshold", 1f, 200f, "0.0", Order = 2, HintText = "At or above this exhaustion, AI support strongly favors peace outcomes.")]
        public float DiplomacyPeacePressureThreshold { get; set; } = 45f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyFloatingInteger("War support penalty / point", 0f, 100f, "0.0", Order = 3, HintText = "Support penalty applied to declaring-war outcomes per exhaustion point.")]
        public float DiplomacyWarSupportPenaltyPerPoint { get; set; } = 6f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyFloatingInteger("Peace support bonus / point", 0f, 100f, "0.0", Order = 4, HintText = "Support bonus applied to make-peace outcomes per exhaustion point.")]
        public float DiplomacyPeaceSupportBonusPerPoint { get; set; } = 5f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyBool("Enable forced peace at crisis", Order = 5, HintText = "If exhaustion is critically high, AI kingdoms will automatically end one war per cooldown period.")]
        public bool EnableForcedPeaceAtCrisis { get; set; } = true;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyFloatingInteger("Forced peace threshold", 1f, 200f, "0.0", Order = 6, HintText = "At or above this exhaustion score, forced peace checks become active.")]
        public float DiplomacyForcedPeaceThreshold { get; set; } = 75f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyInteger("Forced peace cooldown (days)", 1, 30, "0", Order = 7, HintText = "Minimum number of days between automatic peaces for the same kingdom.")]
        public int DiplomacyForcedPeaceCooldownDays { get; set; } = 3;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyInteger("Forced peace max active wars", 0, 10, "0", Order = 8, HintText = "Forced peace only triggers when active wars exceed this number.")]
        public int DiplomacyForcedPeaceMaxActiveWars { get; set; } = 0;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyInteger("Min war days before forced peace", 0, 365, "0", Order = 9, HintText = "Forced peace cannot end a war younger than this many days.")]
        public int MinWarDurationDaysBeforeForcedPeace { get; set; } = 20;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyBool("Ignore if enemy besieges core fief", Order = 10, HintText = "Do not force peace with an enemy currently besieging one of your owned towns/castles.")]
        public bool IgnoreForcedPeaceIfEnemyBesiegingCoreSettlement { get; set; } = true;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyInteger("Forced peace truce days", 0, 365, "0", Order = 11, HintText = "After any peace between two kingdoms, block re-declaration for this many days.")]
        public int ForcedPeaceTruceDays { get; set; } = 30;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyInteger("Major-war pressure starts at", 1, 10, "0", Order = 12, HintText = "When active kingdom wars reach this count, extra peace pressure is applied.")]
        public int DiplomacyMajorWarPressureStartCount { get; set; } = 2;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyFloatingInteger("Extra peace bias per major war", 0f, 500f, "0.0", Order = 13, HintText = "Additional support bias toward peace for each war above the pressure start count.")]
        public float DiplomacyExtraPeaceBiasPerMajorWar { get; set; } = 40f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyFloatingInteger("Forced peace threshold reduction/war", 0f, 100f, "0.0", Order = 14, HintText = "Lowers forced-peace exhaustion threshold per extra major war.")]
        public float DiplomacyForcedPeaceThresholdReductionPerMajorWar { get; set; } = 8f;

        [SettingPropertyGroup("Diplomacy (War Exhaustion)", GroupOrder = 11)]
        [SettingPropertyBool("Enforce player parity", Order = 15, HintText = "If enabled, player kingdom follows the same truce/no-war diplomacy gates as AI.")]
        public bool DiplomacyEnforcePlayerParity { get; set; } = false;

        [SettingPropertyGroup("Developer Tools", GroupOrder = 12)]
        [SettingPropertyBool("Enable diplomacy debug logs", Order = 16, HintText = "Logs detailed reasons for forced-peace and war-gate decisions.")]
        public bool DiplomacyDebugLogs { get; set; } = false;

        [SettingPropertyGroup("Tooltips", GroupOrder = 13)]
        [SettingPropertyBool("Enable settlement manpower tooltips", Order = 0, HintText = "Appends manpower info to settlement property tooltips in campaign UI.")]
        public bool EnableSettlementManpowerTooltips { get; set; } = false;

        [SettingPropertyGroup("Tooltips", GroupOrder = 13)]
        [SettingPropertyBool("Enable experimental map-hover fallback", Order = 1, HintText = "Reserved for an optional world-map hover fallback path. Keep disabled unless explicitly testing.")]
        public bool EnableExperimentalHoverFallback { get; set; } = false;

        [SettingPropertyGroup("Tooltips", GroupOrder = 13)]
        [SettingPropertyInteger("Tooltip refresh (ms)", 100, 2000, "0", Order = 2, HintText = "Throttle interval for any tooltip refresh logic that may run repeatedly.")]
        public int TooltipRefreshMs { get; set; } = 300;
    }
}
