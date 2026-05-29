// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Combat
{
    using System;
    using Core.Contract.Enums;
    using Core.DataReader.Gdb;

    /// <summary>
    /// Mutable per-combat runtime state of a combat actor.
    /// Static data lives in CombatActorInfo; this tracks values that change during a fight.
    /// </summary>
    public sealed class CombatActorRuntimeState
    {
        public CombatActorInfo Info { get; }

        public int CurrentHp { get; set; }
        public int CurrentSp { get; set; }
        public int CurrentMp { get; set; }

        public int MaxHp => Info.AttributeValues[ActorAttributeType.Hp];
        public int MaxSp => Info.AttributeValues[ActorAttributeType.Sp];
        public int MaxMp => Info.AttributeValues[ActorAttributeType.Mp];

        public int Attack => Info.AttributeValues[ActorAttributeType.Attack];
        public int Defense => Info.AttributeValues[ActorAttributeType.Defense];
        public int Speed => Info.AttributeValues[ActorAttributeType.Speed];
        public int Luck => Info.AttributeValues[ActorAttributeType.Luck];

        public bool IsDefeated => CurrentHp <= 0;

        public CombatActorRuntimeState(CombatActorInfo info)
        {
            Info = info;
            CurrentHp = MaxHp;
            CurrentSp = MaxSp;
            CurrentMp = MaxMp;
        }

        public int TakeDamage(int rawDamage)
        {
            int actual = rawDamage < 0 ? 0 : rawDamage;
            if (actual > CurrentHp) actual = CurrentHp;
            CurrentHp -= actual;
            return actual;
        }

        public void Heal(int amount)
        {
            if (amount < 0) return;
            CurrentHp = Math.Min(CurrentHp + amount, MaxHp);
        }

        public void ConsumeMp(int amount)
        {
            if (amount < 0) return;
            CurrentMp = Math.Max(CurrentMp - amount, 0);
        }

        public void RestoreMp(int amount)
        {
            if (amount < 0) return;
            CurrentMp = Math.Min(CurrentMp + amount, MaxMp);
        }

        public void ConsumeSp(int amount)
        {
            if (amount < 0) return;
            CurrentSp = Math.Max(CurrentSp - amount, 0);
        }

        public void RestoreSp(int amount)
        {
            if (amount < 0) return;
            CurrentSp = Math.Min(CurrentSp + amount, MaxSp);
        }
    }
}
