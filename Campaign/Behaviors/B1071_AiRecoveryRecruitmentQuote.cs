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

        /// <summary>
        /// Men on the vanilla notable volunteer board this party could take. Unlike the
        /// other three this is a count only -- Campaign++ never recruits it, because
        /// vanilla's own RecruitVolunteersFromNotable already does when the party arrives.
        /// It is here so routing can see the supply that decides where a lord should go.
        /// </summary>
        internal int Volunteers { get; }

        internal int Total => Veterans + Elites + Prisoners + Volunteers;

        /// <summary>
        /// The men Campaign++ can hand over itself, which is Total minus the vanilla
        /// volunteer board. Routing wants Total -- where a lord should go is decided by all
        /// the supply he will find. Recruiting wants this: acting on a board this mod never
        /// takes is a guaranteed no-op.
        /// </summary>
        internal int Actionable => Veterans + Elites + Prisoners;

        internal B1071_AiRecoverySourceQuote(
            int veterans,
            int elites,
            int prisoners,
            int manpower,
            int volunteers = 0)
        {
            Veterans = Math.Max(0, veterans);
            Elites = Math.Max(0, elites);
            Prisoners = Math.Max(0, prisoners);
            Manpower = Math.Max(0, manpower);
            Volunteers = Math.Max(0, volunteers);
        }

        public static B1071_AiRecoverySourceQuote operator +(
            B1071_AiRecoverySourceQuote left,
            B1071_AiRecoverySourceQuote right)
            => new B1071_AiRecoverySourceQuote(
                left.Veterans + right.Veterans,
                left.Elites + right.Elites,
                left.Prisoners + right.Prisoners,
                left.Manpower + right.Manpower,
                left.Volunteers + right.Volunteers);
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
