using System;

namespace Byzantium1071.Campaign.Behaviors
{
    internal sealed class B1071_AiRecoveryBudget
    {
        internal int Room { get; set; }
        internal int Gold { get; set; }
        internal int Manpower { get; set; }
        internal bool HasFiniteManpower { get; }

        internal B1071_AiRecoveryBudget(int room, int gold, int manpower, bool hasFiniteManpower)
        {
            Room = Math.Max(0, room);
            Gold = Math.Max(0, gold);
            Manpower = Math.Max(0, manpower);
            HasFiniteManpower = hasFiniteManpower;
        }
    }

    internal readonly struct B1071_AiRecoverySourceQuote
    {
        internal int Veterans { get; }
        internal int Elites { get; }
        internal int Prisoners { get; }
        internal int Manpower { get; }

        internal int Total => Veterans + Elites + Prisoners;

        internal B1071_AiRecoverySourceQuote(int veterans, int elites, int prisoners, int manpower)
        {
            Veterans = Math.Max(0, veterans);
            Elites = Math.Max(0, elites);
            Prisoners = Math.Max(0, prisoners);
            Manpower = Math.Max(0, manpower);
        }

        public static B1071_AiRecoverySourceQuote operator +(
            B1071_AiRecoverySourceQuote left,
            B1071_AiRecoverySourceQuote right)
            => new B1071_AiRecoverySourceQuote(
                left.Veterans + right.Veterans,
                left.Elites + right.Elites,
                left.Prisoners + right.Prisoners,
                left.Manpower + right.Manpower);
    }

    internal readonly struct B1071_AiRecoveryReservedSupply
    {
        internal int Veterans { get; }
        internal int Elites { get; }
        internal int Prisoners { get; }
        internal int Manpower { get; }

        internal B1071_AiRecoveryReservedSupply(int veterans, int elites, int prisoners, int manpower)
        {
            Veterans = Math.Max(0, veterans);
            Elites = Math.Max(0, elites);
            Prisoners = Math.Max(0, prisoners);
            Manpower = Math.Max(0, manpower);
        }

        internal B1071_AiRecoveryReservedSupply ForCandidate(
            bool sameSettlement,
            bool sameManpowerPool)
            => new B1071_AiRecoveryReservedSupply(
                sameSettlement ? Veterans : 0,
                sameSettlement ? Elites : 0,
                sameSettlement ? Prisoners : 0,
                sameManpowerPool ? Manpower : 0);

        public static B1071_AiRecoveryReservedSupply operator +(
            B1071_AiRecoveryReservedSupply left,
            B1071_AiRecoveryReservedSupply right)
            => new B1071_AiRecoveryReservedSupply(
                left.Veterans + right.Veterans,
                left.Elites + right.Elites,
                left.Prisoners + right.Prisoners,
                left.Manpower + right.Manpower);
    }
}
