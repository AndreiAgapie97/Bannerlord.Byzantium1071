using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace Byzantium1071.Campaign.Settings
{
    public sealed class B1071_McmSettings : AttributeGlobalSettings<B1071_McmSettings>
    {
        public override string Id => "Byzantium1071";
        public override string DisplayName => "Byzantium 1071";
        public override string FolderName => "Byzantium1071";
        public override string FormatType => "json";

        [SettingPropertyGroup("Pool Sizes", GroupOrder = 0)]
        [SettingPropertyInteger("Town max manpower", 50, 50000, "0", Order = 0, HintText = "Base manpower pool for towns before optional test scaling.")]
        public int TownPoolMax { get; set; } = 6000;

        [SettingPropertyGroup("Pool Sizes", GroupOrder = 0)]
        [SettingPropertyInteger("Castle max manpower", 50, 50000, "0", Order = 1, HintText = "Base manpower pool for castles before optional test scaling.")]
        public int CastlePoolMax { get; set; } = 3000;

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
        [SettingPropertyFloatingInteger("Prosperity normalizer", 1000f, 20000f, "0", Order = 0, HintText = "Prosperity value at which town pool reaches max scale. Typical towns: 2000-8000+.")]
        public float ProsperityNormalizer { get; set; } = 8000f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Town pool min scale %", 1f, 100f, "0", Order = 1, HintText = "Minimum % of base town pool when prosperity is 0. E.g., 50 = ruined town has 50% of base.")]
        public float MaxPoolProsperityMinScale { get; set; } = 50f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Town pool max scale %", 50f, 200f, "0", Order = 2, HintText = "Maximum % of base town pool at full prosperity. E.g., 100 = thriving town has 100% of base.")]
        public float MaxPoolProsperityMaxScale { get; set; } = 100f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Castle pool min scale %", 1f, 100f, "0", Order = 3, HintText = "Minimum % of base castle pool when security is 0.")]
        public float MaxPoolSecurityMinScale { get; set; } = 50f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Castle pool max scale %", 50f, 200f, "0", Order = 4, HintText = "Maximum % of base castle pool at full security (100).")]
        public float MaxPoolSecurityMaxScale { get; set; } = 100f;

        [SettingPropertyGroup("Pool Scaling", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Hearth multiplier", 0f, 5f, "0.00", Order = 5, HintText = "Each village hearth × this = flat bonus to max pool. E.g., 0.50 means 1000 hearth village adds 500 max manpower.")]
        public float MaxPoolHearthMultiplier { get; set; } = 0.50f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Town regen min %", 0f, 20f, "0.00", Order = 0, HintText = "Minimum daily town regen as a percent of max pool.")]
        public float TownRegenMinPercent { get; set; } = 0.80f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Town regen max %", 0f, 20f, "0.00", Order = 1, HintText = "Maximum daily town regen as a percent of max pool.")]
        public float TownRegenMaxPercent { get; set; } = 2.00f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Castle regen min %", 0f, 20f, "0.00", Order = 2, HintText = "Minimum daily castle regen as a percent of max pool.")]
        public float CastleRegenMinPercent { get; set; } = 0.60f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Castle regen max %", 0f, 20f, "0.00", Order = 3, HintText = "Maximum daily castle regen as a percent of max pool.")]
        public float CastleRegenMaxPercent { get; set; } = 1.40f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Other settlement regen %", 0f, 20f, "0.00", Order = 4, HintText = "Daily regen percent for non-town/non-castle settlements.")]
        public float OtherRegenPercent { get; set; } = 1.00f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyInteger("Hearth normalizer", 100, 10000, "0", Order = 5, HintText = "Higher values reduce the impact of village hearth sum on regen.")]
        public int HearthNormalizer { get; set; } = 3000;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Hearth bonus max %", 0f, 10f, "0.00", Order = 6, HintText = "Maximum additional regen percent from hearth contribution.")]
        public float HearthBonusMaxPercent { get; set; } = 0.20f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Siege regen multiplier %", 0f, 200f, "0.00", Order = 7, HintText = "While under siege, regen is multiplied by this percent.")]
        public float SiegeRegenMultiplierPercent { get; set; } = 25.00f;

        [SettingPropertyGroup("Regen", GroupOrder = 2)]
        [SettingPropertyInteger("Minimum daily regen", 0, 500, "0", Order = 8, HintText = "Absolute minimum manpower regenerated per day.")]
        public int MinimumDailyRegen { get; set; } = 1;

        [SettingPropertyGroup("Recruitment Cost", GroupOrder = 3)]
        [SettingPropertyInteger("Base manpower cost", 1, 50, "0", Order = 0, HintText = "Base manpower cost for a tier-1 recruit.")]
        public int BaseManpowerCostPerTroop { get; set; } = 1;

        [SettingPropertyGroup("Recruitment Cost", GroupOrder = 3)]
        [SettingPropertyInteger("Tiers per +1 cost step", 1, 10, "0", Order = 1, HintText = "How many tiers are needed before manpower cost increases by 1.")]
        public int TiersPerExtraCost { get; set; } = 2;

        [SettingPropertyGroup("Recruitment Cost", GroupOrder = 3)]
        [SettingPropertyInteger("Cost multiplier %", 1, 1000, "0", Order = 2, HintText = "Final manpower cost multiplier applied to tier-based cost.")]
        public int CostMultiplierPercent { get; set; } = 100;

        [SettingPropertyGroup("Diagnostics", GroupOrder = 4)]
        [SettingPropertyBool("Show player debug messages", Order = 0, HintText = "Shows manpower messages in the game UI for player actions.")]
        public bool ShowPlayerDebugMessages { get; set; } = true;

        [SettingPropertyGroup("Diagnostics", GroupOrder = 4)]
        [SettingPropertyBool("Use OnUnitRecruited fallback for player", Order = 1, HintText = "Consumes manpower for player recruits on per-click event.")]
        public bool UseOnUnitRecruitedFallbackForPlayer { get; set; } = true;

        [SettingPropertyGroup("Diagnostics", GroupOrder = 4)]
        [SettingPropertyBool("Log AI manpower consumption", Order = 2, HintText = "Logs AI manpower consumption bands to the Bannerlord logs.")]
        public bool LogAiManpowerConsumption { get; set; } = true;

        [SettingPropertyGroup("Overlay", GroupOrder = 5)]
        [SettingPropertyBool("Enable in-game overlay", Order = 0, HintText = "Enables the standalone Byzantium 1071 overlay on campaign map.")]
        public bool EnableOverlay { get; set; } = true;

        [SettingPropertyGroup("Overlay", GroupOrder = 5)]
        [SettingPropertyBool("Enable M hotkey toggle", Order = 1, HintText = "Press M on campaign map to show/hide the overlay.")]
        public bool EnableOverlayHotkey { get; set; } = true;

        [SettingPropertyGroup("Overlay", GroupOrder = 5)]
        [SettingPropertyBool("Show settlement name", Order = 2, HintText = "Adds current settlement name to overlay text when available.")]
        public bool OverlayShowSettlementName { get; set; } = true;

        [SettingPropertyGroup("Overlay", GroupOrder = 5)]
        [SettingPropertyBool("Show pool identifier", Order = 3, HintText = "Shows the pool settlement used for manpower calculations.")]
        public bool OverlayShowPoolName { get; set; } = false;

        [SettingPropertyGroup("Overlay", GroupOrder = 5)]
        [SettingPropertyInteger("Ledger default tab (0-3)", 0, 3, "0", Order = 4, HintText = "0=Nearby, 1=Pools, 2=World, 3=Factions.")]
        public int OverlayLedgerDefaultTab { get; set; } = 0;

        [SettingPropertyGroup("Overlay", GroupOrder = 5)]
        [SettingPropertyInteger("Ledger rows per page", 3, 15, "0", Order = 5, HintText = "How many ledger rows are shown per page.")]
        public int OverlayLedgerRowsPerPage { get; set; } = 7;

        [SettingPropertyGroup("Overlay", GroupOrder = 5)]
        [SettingPropertyBool("World tab includes villages", Order = 6, HintText = "If enabled, World/Factions tabs include villages in addition to towns and castles.")]
        public bool OverlayLedgerIncludeVillages { get; set; } = true;
    }
}
