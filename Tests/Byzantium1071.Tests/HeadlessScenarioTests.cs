using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Byzantium1071.Campaign;
using Xunit;
using Xunit.Abstractions;

namespace Byzantium1071.Tests
{
    // Scripted rule scenarios, not a substitute for the campaign event scheduler.
    // Fixed costs/settings below are test inputs, not native defaults or balance recommendations.
    public sealed class HeadlessScenarioTests
    {
        private readonly ITestOutputHelper _output;
        public HeadlessScenarioTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void CastlePrisonersConserveBudgetAndCountsAcrossArrivalsAndRecruitment()
        {
            Repeat((days, random) =>
            {
                int[] counts = { 40, 20, 10 };
                int[] costs = { 100, 500, 1500 };
                int[] points = { 0, 0, 0 };
                int remainder = 0, cursor = 0, arrivals = 70, recruited = 0;
                long granted = 0, spent = 0;
                var trace = new StringBuilder();
                for (int day = 1; day <= days; day++)
                {
                    int type = random.Next(counts.Length);
                    int added = random.Next(6);
                    counts[type] += added;
                    arrivals += added;
                    int leadership = random.Next(301);
                    int previousRemainder = remainder;
                    int budget = B1071_CastleConformityMath.DailyBudget(leadership, ref remainder);
                    Check(budget * 5 + remainder == 1200 + 6 * leadership + previousRemainder,
                        day, "fractional conformity budget was lost");
                    int[] needs = counts.Select((count, i) => B1071_CastleConformityMath.Capacity(count, costs[i]) - points[i]).ToArray();
                    int[] grants = B1071_CastleConformityMath.Allocate(needs, budget, ref cursor);
                    Check(grants.Sum() == Math.Min(needs.Sum(), budget), day, "allocation lost or created points");
                    granted += grants.Sum();
                    for (int i = 0; i < counts.Length; i++)
                    {
                        points[i] += grants[i];
                        int ready = B1071_CastleConformityMath.ReadyCount(counts[i], points[i], costs[i]);
                        Check(ready >= 0 && ready <= counts[i] && (long)ready * costs[i] <= points[i], day, "unfunded eligibility");
                        int take = random.Next(ready + 1);
                        counts[i] -= take;
                        points[i] -= take * costs[i];
                        spent += take * costs[i];
                        recruited += take;
                        Check(points[i] >= 0 && points[i] <= (long)counts[i] * costs[i], day, "invalid remaining conformity");
                    }
                    Check(counts.Sum() + recruited == arrivals, day, "prisoner accounting mismatch");
                    Check(points.Sum() + spent == granted, day, "conformity accounting mismatch");
                    trace.AppendLine($"{day}:{string.Join(',', counts)}:{string.Join(',', points)}:{remainder}:{cursor}:{recruited}");
                }
                return trace.ToString();
            });
        }

        [Fact]
        public void SlaveUpkeepPreservesFractionalDecayAndTransferAccounting()
        {
            Repeat((days, random) =>
            {
                var settings = new FakeSettings { SlaveDailyDecayPercent = 1f, SlaveFoodConsumptionPerUnit = 0.05f };
                int market = 30, stash = 20, carried = 50, totalAcquired = 100, lost = 0;
                float remainder = 0f;
                double expectedLoss = 0;
                var trace = new StringBuilder();
                for (int day = 1; day <= days; day++)
                {
                    int bought = random.Next(8);
                    carried += bought;
                    totalAcquired += bought;
                    int deposited = random.Next(carried + 1);
                    carried -= deposited;
                    stash += deposited;
                    int sold = random.Next(stash + 1);
                    stash -= sold;
                    market += sold;
                    expectedLoss += market * 0.01d;
                    SlaveDecayResult decay = B1071_SlaveMath.DailyDecay(market, remainder, settings);
                    Check(decay.WholeLoss >= 0 && decay.WholeLoss <= market, day, "invalid market losses");
                    market -= decay.WholeLoss;
                    lost += decay.WholeLoss;
                    remainder = decay.RemainingAccumulator;
                    Check(remainder >= 0 && remainder < 1, day, "invalid fractional loss");
                    Check(Math.Abs(lost + remainder - expectedLoss) < 0.05, day, "fractional decay drift");
                    Check(market + stash + carried + lost == totalAcquired, day, "inventory transfer created or lost slaves");
                    float food = B1071_SlaveMath.FoodConsumption(market, settings)
                        + B1071_SlaveMath.FoodConsumption(stash, settings)
                        + B1071_SlaveMath.FoodConsumption(carried, settings);
                    Check(Math.Abs(food - (market + stash + carried) * 0.05d) < 0.01, day, "food is not additive across inventories");
                    int men = random.Next(1, 301), prisoners = random.Next(101), capacity = random.Next(201);
                    float escort = B1071_SlaveMath.EscortFactor(men, prisoners + carried);
                    float overload = B1071_SlaveMath.OverCapacityFactor(prisoners + carried, capacity);
                    Check(float.IsFinite(escort) && escort > -1 && escort <= 0, day, "invalid escort penalty");
                    Check(float.IsFinite(overload) && overload >= -1 && overload <= 0, day, "invalid overload penalty");
                    trace.AppendLine(FormattableString.Invariant($"{day}:{market}:{stash}:{carried}:{lost}:{remainder:R}:{food:R}:{escort:R}:{overload:R}"));
                }
                return trace.ToString();
            });
        }

        [Fact]
        public void RevenueRemainsBoundedAcrossGrowthAndSettingChanges()
        {
            Repeat((days, random) =>
            {
                var trace = new StringBuilder();
                for (int day = 1; day <= days; day++)
                {
                    int basis = random.Next(1, 1000000), strength = random.Next(101), knee = random.Next(20001);
                    float curve = random.Next(1, 11);
                    float town = B1071_RevenueMath.TownTariffScale(basis, strength, curve, knee);
                    float village = B1071_RevenueMath.VillageTariffScale(basis, strength, curve, knee);
                    float tax = B1071_RevenueMath.TownTaxScale(basis, strength, curve, knee);
                    foreach (float scale in new[] { town, village, tax })
                        Check(float.IsFinite(scale) && scale >= 0 && scale <= 1, day, "revenue scale escaped bounds");
                    double nextPayout = (basis + 1000d) * B1071_RevenueMath.TownTariffScale(basis + 1000, strength, curve, knee);
                    Check(nextPayout + 0.1 >= basis * (double)town, day, "growth reduced town tariff payout");
                    Check(B1071_RevenueMath.TownTariffScale(basis, 100, curve, knee) == 1, day, "100% setting failed to bypass taper");
                    trace.AppendLine(FormattableString.Invariant($"{day}:{basis}:{strength}:{knee}:{curve}:{town:R}:{village:R}:{tax:R}"));
                }
                return trace.ToString();
            });
        }

        [Fact]
        public void ManpowerRecoveryRemainsBoundedThroughRecruitmentSiegeAndConquest()
        {
            Repeat((days, random) =>
            {
                var settings = new FakeSettings
                {
                    TownRegenMinPercent = 1, TownRegenMaxPercent = 3, ProsperityNormalizer = 1000,
                    SecurityRegenMinScale = 50, SecurityRegenMaxScale = 100,
                    FoodStocksNormalizer = 100, FoodRegenMinScale = 50, FoodRegenMaxScale = 100,
                    LoyaltyRegenMinScale = 50, LoyaltyRegenMaxScale = 100,
                    SiegeRegenMultiplierPercent = 25, RegenCapPercent = 10, MinimumDailyRegen = 1,
                    EnableWarExhaustion = true, ExhaustionRegenDivisor = 100,
                    EnableDelayedRecovery = true, EnableRecruitmentVariance = true, RecoveryVariancePercent = 20,
                    EnableSeasonalRegen = true, SpringSummerRegenMultiplier = 110, WinterRegenMultiplier = 75,
                    ConquestPoolRetainPercent = 50, BaseManpowerCostPerTroop = 2
                };
                const int maximum = 1000;
                int pool = 200, generated = 0, spent = 0, lost = 0, recruited = 0;
                var trace = new StringBuilder();
                for (int day = 1; day <= days; day++)
                {
                    bool siege = day % 60 < 15;
                    float security = random.Next(101), food = random.Next(101), loyalty = random.Next(101);
                    float exhaustion = random.Next(101), variance = 0.8f + (float)random.NextDouble() * 0.4f;
                    float recovery = B1071_ManpowerMath.RecoveryPenaltyFraction(0.5f, 0, 30, day % 60, pool, maximum, settings);
                    PoolFacts Facts(bool underSiege, float pressure) => new(isTown: true, isCastle: false, hasTown: true,
                        prosperity: 1000, security: security, foodStocks: food, loyalty: loyalty,
                        isUnderSiege: underSiege, exhaustion: pressure, recoveryPenalty: recovery,
                        currentPool: pool, season: (B1071Season)((day / 21) % 4));
                    DailyRegenResult Regen(bool underSiege, float pressure) => B1071_ManpowerMath.DailyRegen(
                        Facts(underSiege, pressure), maximum, settings, new FakeRandom(floats: new[] { variance }));
                    var regen = Regen(siege, exhaustion);
                    Check(regen.Amount >= 1 && regen.Amount <= 100 && float.IsFinite(regen.FinalPercent), day, "regen escaped daily bounds");
                    Check(regen.Amount <= Regen(false, 0).Amount, day, "siege/exhaustion increased regeneration");
                    int applied = Math.Min(maximum - pool, regen.Amount);
                    generated += applied;
                    pool += applied;
                    if (day % 60 == 15)
                    {
                        var retained = B1071_ManpowerMath.ConquestRetention(pool, maximum, settings);
                        Check(retained.AppliedPool >= 0 && retained.AppliedPool <= pool, day, "conquest created manpower");
                        lost += pool - retained.AppliedPool;
                        pool = retained.AppliedPool;
                    }
                    int cost = B1071_ManpowerMath.RecruitmentCostPerTroop(settings);
                    int take = Math.Min(random.Next(16), pool / cost);
                    pool -= take * cost;
                    spent += take * cost;
                    recruited += take;
                    Check(pool >= 0 && pool <= maximum && pool + spent + lost == 200 + generated,
                        day, "manpower accounting diverged");
                    Check(spent == recruited * 2, day, "recruitment did not charge configured manpower");
                    trace.AppendLine($"Day {day}: pool={pool}, recruits={recruited}, regenerated={generated}, war losses={lost}");
                }
                return trace.ToString();
            });
        }

        [Fact]
        public void ServiceExtensionsRetirementAndCheckpointRowsPreserveIndividualHistory()
        {
            Repeat((days, random) =>
            {
                var settings = new FakeSettings
                {
                    DemobilizationIntensityPreset = 1, DemobilizationExtensionDays = 21,
                    DemobilizationExtensionGoldPerTierDay = 1, DemobilizationMaxExtensions = 3,
                    EnableDemobilizationSeasonality = true, DemobilizationSpringSummerThresholdPercent = 110,
                    DemobilizationWinterThresholdPercent = 75
                };
                var soldiers = new List<ServiceCohortSaveRow>();
                int recruited = 0, retired = 0, returned = 0, extensions = 0;
                long gold = 1000, spent = 0;
                var trace = new StringBuilder();
                for (int day = 1; day <= days; day++)
                {
                    gold += 50; // Explicit external budget for this fixture, not simulated campaign income.
                    int arrivals = random.Next(3);
                    for (int n = 0; n < arrivals; n++)
                    {
                        soldiers.Add(new ServiceCohortSaveRow("party", "tier" + random.Next(1, 7), day, 1, 0,
                            "home" + recruited, "origin" + random.Next(3), "employer" + random.Next(3)));
                        recruited++;
                    }
                    for (int i = soldiers.Count - 1; i >= 0; i--)
                    {
                        var soldier = soldiers[i];
                        int tier = soldier.TroopId.Last() - '0';
                        int threshold = B1071_ServiceMath.ServiceThresholdDays(tier, (B1071Season)((day / 21) % 4), false, settings);
                        Check(threshold > 0, day, "invalid service duration");
                        if (day - soldier.JoinDay < threshold) continue;
                        int cost = B1071_ServiceMath.ExtensionCost(tier, 1, soldier.ExtensionCount, settings);
                        if (soldier.ExtensionCount < B1071_ServiceMath.MaxExtensions(settings) && gold >= cost)
                        {
                            Check(cost == tier * 21 * (2 + soldier.ExtensionCount) / 2, day, "extension price mismatch");
                            gold -= cost;
                            spent += cost;
                            extensions++;
                            soldiers[i] = new ServiceCohortSaveRow(soldier.PartyId, soldier.TroopId,
                                soldier.JoinDay + 21, 1, soldier.ExtensionCount + 1, soldier.HomeId,
                                soldier.OriginClanId, soldier.EmployerClanId);
                        }
                        else
                        {
                            int roll = random.Next(100);
                            int veterans = B1071_ServiceMath.VeteranReturnCount(1, 70, new FakeRandom(integers: new[] { roll }));
                            Check(veterans == (roll < 70 ? 1 : 0), day, "veteran return ignored its probability");
                            returned += veterans;
                            retired++;
                            soldiers.RemoveAt(i);
                        }
                    }
                    if (day % 17 == 0)
                    {
                        var parties = new List<string>(); var troops = new List<string>(); var joined = new List<int>();
                        var counts = new List<int>(); var flags = new List<bool>(); var extended = new List<int>();
                        var homes = new List<string>(); var origins = new List<string>(); var employers = new List<string>();
                        foreach (var row in soldiers)
                            B1071_ServiceMath.AppendServiceCohortRows(parties, troops, joined, counts, flags, extended,
                                homes, origins, employers, row.PartyId, row.TroopId, row.JoinDay, row.Count,
                                row.ExtensionCount, row.HomeId, row.OriginClanId, row.EmployerClanId);
                        var restored = B1071_ServiceMath.ReadServiceCohortRows(parties, troops, joined, counts, flags,
                            extended, homes, origins, employers);
                        Check(soldiers.SequenceEqual(restored), day, "checkpoint altered individual service history");
                        soldiers = restored;
                    }
                    Check(soldiers.Count + retired == recruited && returned <= retired, day, "soldier accounting diverged");
                    Check(gold >= 0 && gold + spent == 1000 + day * 50L, day, "extension gold accounting diverged");
                    Check(soldiers.All(s => s.Count == 1 && s.ExtensionCount <= 3), day, "individual records merged or overextended");
                    trace.AppendLine($"Day {day}: serving={soldiers.Count}, retired={retired}, veterans={returned}, extensions={extensions}, gold={gold}");
                }
                return trace.ToString();
            });
        }

        [Fact]
        public void InvestmentBonusesExpireIndependentlyAcrossSettlementsAndInvestors()
        {
            Repeat((days, random) =>
            {
                var settings = new FakeSettings
                {
                    TownInvestCostModest = 100, TownInvestCostGenerous = 250, TownInvestCostGrand = 500,
                    TownInvestDurationModest = 7, TownInvestDurationGenerous = 14, TownInvestDurationGrand = 21,
                    TownInvestProsperityModest = 1, TownInvestProsperityGenerous = 2, TownInvestProsperityGrand = 3,
                    VillageInvestCostModest = 60, VillageInvestCostGenerous = 150, VillageInvestCostGrand = 300,
                    VillageInvestDurationModest = 7, VillageInvestDurationGenerous = 14, VillageInvestDurationGrand = 21,
                    VillageInvestHearthModest = 2, VillageInvestHearthGenerous = 4, VillageInvestHearthGrand = 6
                };
                string[] settlements = { "town_A1", "town_A11", "village_A1", "village_A11" };
                var bonuses = new Dictionary<string, float>();
                var remaining = new Dictionary<string, float>();
                var grants = new Dictionary<(int Place, int Hero), (int Expires, int Bonus)>();
                int[] lastAction = { -5, -5, -5 };
                int gold = 0, spent = 0, investments = 0;
                var trace = new StringBuilder();
                for (int day = 1; day <= days; day++)
                {
                    gold += 100; // Fixture allowance, not campaign income.
                    foreach (string key in remaining.Keys.ToArray()) remaining[key]--;
                    int hero = random.Next(3), place = random.Next(4);
                    bool town = place < 2;
                    var tiers = Enumerable.Range(1, 3).Select(t => town
                        ? B1071_InvestmentMath.TownTier(t, settings)
                        : B1071_InvestmentMath.VillageTier(t, settings)).ToArray();
                    int[] costs = town ? new[] { 100, 250, 500 } : new[] { 60, 150, 300 };
                    var affordable = B1071_InvestmentMath.AffordableTiers(gold, 2, tiers[0], tiers[1], tiers[2]);
                    Check(affordable.SequenceEqual(Enumerable.Range(1, 3).Where(t => costs[t - 1] * 2 < gold)),
                        day, "investment reserve threshold diverged");
                    bool ready = B1071_InvestmentMath.IsHeroCooldownReady(day, lastAction[hero], 5);
                    Check(ready == (day >= lastAction[hero] + 5), day, "investor cooldown diverged");
                    // Leave every fourth month idle so even the longest grant must expire.
                    if (day % 120 < 90 && ready && affordable.Count > 0)
                    {
                        int tier = affordable[random.Next(affordable.Count)];
                        var grant = tiers[tier - 1];
                        gold -= grant.Cost;
                        spent += costs[tier - 1];
                        lastAction[hero] = day;
                        string key = settlements[place] + "_lord_" + hero;
                        bonuses[key] = grant.Bonus;
                        remaining[key] = grant.Duration;
                        // Structured keys and absolute expiry independently check string matching and countdowns.
                        grants[(place, hero)] = (day + tier * 7, tier * (town ? 1 : 2));
                        investments++;
                    }
                    for (int i = 0; i < settlements.Length; i++)
                    {
                        int expected = grants.Where(g => g.Key.Place == i && g.Value.Expires > day).Sum(g => g.Value.Bonus);
                        float actual = B1071_InvestmentMath.ActiveBonus(settlements[i], bonuses, remaining);
                        Check(actual == expected, day, "investment leaked across settlement or expiry boundary");
                        if (day % 120 == 119) Check(actual == 0, day, "expired investment remained active");
                        trace.Append($"{settlements[i]}={actual};");
                    }
                    Check(gold >= 0 && gold + spent == day * 100, day, "investment budget did not balance");
                    trace.AppendLine($"Day {day}: investments={investments}, gold={gold}, spent={spent}");
                }
                return trace.ToString();
            });
        }

        [Fact]
        public void GovernanceStabilizationAndDevastationRecoverThroughRepeatedCrises()
        {
            Repeat((days, random) =>
            {
                var settings = new FakeSettings
                {
                    GovernanceStrainCap = 100, GovernanceStabilizationAiGoldMultiplier = 2,
                    GovernanceStabilizationCostDonative = 100, GovernanceStabilizationCostElites = 250,
                    GovernanceStabilizationCostAmnesty = 500,
                    GovernanceStabilizationDurationDonative = 3, GovernanceStabilizationDurationElites = 5,
                    GovernanceStabilizationDurationAmnesty = 7,
                    GovernanceStabilizationStrainDonative = 5, GovernanceStabilizationStrainElites = 10,
                    GovernanceStabilizationStrainAmnesty = 20,
                    GovernanceStabilizationDecayDonative = 0.5f, GovernanceStabilizationDecayElites = 1,
                    GovernanceStabilizationDecayAmnesty = 2,
                    DevastationPerRaid = 25, DevastationDecayPerDay = 2, DevastationMaxFoodPenaltyPerVillage = 10
                };
                float strain = 0, devastation = 0, extraDecay = 0, added = 0, removed = 0;
                int expires = 0, gold = 0, spent = 0, raids = 0;
                var trace = new StringBuilder();
                for (int day = 1; day <= days; day++)
                {
                    gold += 20;
                    bool crisis = day % 90 < 30;
                    if (crisis && random.Next(3) == 0)
                    {
                        float previous = strain;
                        strain = B1071_GovernanceMath.AddStrain(strain, random.Next(10, 41), settings);
                        added += strain - previous;
                        devastation = B1071_GovernanceMath.AddDevastation(devastation, settings);
                        raids++;
                    }
                    if (crisis && day % 8 == 0)
                    {
                        int tier = B1071_GovernanceMath.AiStabilizationTier(gold, settings);
                        int expected = gold > 1000 ? 3 : gold > 500 ? 2 : gold > 200 ? 1 : 0;
                        Check(tier == expected, day, "stabilization reserve threshold diverged");
                        if (tier > 0)
                        {
                            var action = B1071_GovernanceMath.StabilizationTier(tier, settings);
                            gold -= action.Cost;
                            spent += new[] { 100, 250, 500 }[tier - 1];
                            float previous = strain;
                            strain = B1071_GovernanceMath.ReduceStrain(strain, action.StrainReduction);
                            Check(previous - strain == Math.Min(previous, new[] { 5, 10, 20 }[tier - 1]),
                                day, "stabilization immediate relief diverged");
                            removed += previous - strain;
                            expires = day + action.Duration;
                            extraDecay = action.Decay;
                        }
                    }
                    float beforeDecay = strain;
                    float bonus = day < expires ? extraDecay : 0;
                    strain = B1071_GovernanceMath.DailyStrain(strain, 2, bonus);
                    removed += beforeDecay - strain;
                    Check(beforeDecay - strain == Math.Min(beforeDecay, 2 + bonus), day, "daily relief diverged");
                    devastation = B1071_GovernanceMath.DailyDevastation(devastation, settings);
                    Check(strain >= 0 && strain <= 100 && devastation >= 0 && devastation <= 100, day, "crisis state escaped bounds");
                    Check(strain + removed == added && gold >= 0 && gold + spent == day * 20,
                        day, "governance relief or gold accounting diverged");
                    float penalty = B1071_GovernanceMath.GovernancePenalty(strain, 4, settings);
                    float food = B1071_GovernanceMath.DevastationFoodPenalty(devastation, settings);
                    Check(float.IsFinite(penalty) && penalty >= -4 && penalty <= 0 && food >= 0 && food <= 10,
                        day, "governance penalties escaped bounds");
                    if (day % 90 == 89)
                        Check(strain == 0 && devastation == 0 && penalty == 0 && food == 0,
                            day, "peace interval did not fully recover");
                    trace.AppendLine(FormattableString.Invariant($"Day {day}: strain={strain}, devastation={devastation}, raids={raids}, gold={gold}, reliefExpires={expires}"));
                }
                return trace.ToString();
            });
        }

        private void Repeat(Func<int, Random, string> run)
        {
            int days = Option("B1071_SCENARIO_DAYS", 365, 1, 10000);
            int seed = Option("B1071_SCENARIO_SEED", 42, 0, int.MaxValue);
            _output.WriteLine($"Scripted rule scenario: days={days}, seed={seed}; native engine is not running.");
            string first = run(days, new Random(seed));
            string second = run(days, new Random(seed));
            Assert.Equal(first, second);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first)));
            _output.WriteLine($"PASS: daily invariants and identical replay. Trace SHA256: {hash}");
            _output.WriteLine("Final scripted state: " + first.TrimEnd().Split('\n').Last());
        }

        private static int Option(string name, int fallback, int min, int max)
        {
            string? text = Environment.GetEnvironmentVariable(name);
            if (text == null) return fallback;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
                throw new ArgumentException($"{name} must be between {min} and {max}.");
            return value;
        }

        private static void Check(bool valid, int day, string message) => Assert.True(valid, $"Day {day}: {message}");
    }
}
