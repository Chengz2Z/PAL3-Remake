// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Combat
{
    using Core.Contract.Enums;
    using Core.Utilities;

    /// <summary>
    /// Damage and miss/critical resolution for stage-1 combat.
    /// Formulas are intentionally simple; element/state interactions land in later stages.
    /// </summary>
    public static class DamageCalculator
    {
        public static int NormalAttack(CombatActorRuntimeState attacker, CombatActorRuntimeState defender)
        {
            int baseDamage = attacker.Attack - defender.Defense / 2;
            if (baseDamage < 1) baseDamage = 1;

            int variance = RandomGenerator.Range(-baseDamage / 10, baseDamage / 10 + 1);
            int damage = baseDamage + variance;

            if (RollCritical(attacker, defender))
            {
                damage *= 2;
            }

            return damage < 1 ? 1 : damage;
        }

        private static bool RollCritical(CombatActorRuntimeState attacker, CombatActorRuntimeState defender)
        {
            int luckDelta = attacker.Luck - defender.Luck;
            int chance = 5 + (luckDelta > 0 ? luckDelta : 0);
            if (chance > 30) chance = 30;
            return RandomGenerator.Range(0, 100) < chance;
        }

        public static bool IsAlly(ElementPosition position) => position <= ElementPosition.AllyCenter;
        public static bool IsEnemy(ElementPosition position) => position >= ElementPosition.EnemyWater;
    }
}
