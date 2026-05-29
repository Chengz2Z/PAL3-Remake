// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Combat
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Actor.Controllers;
    using Core.Contract.Enums;
    using Core.DataReader.Gdb;
    using Core.Utilities;
    using Engine.Logging;

    /// <summary>
    /// Manages combat state effects for actors, including poison, paralysis, sleep, etc.
    /// </summary>
    public sealed class CombatStateManager
    {
        private readonly Dictionary<CombatActorController, Dictionary<ActorCombatStateType, CombatStateEffect>> _actorStates = new();

        /// <summary>
        /// Represents an active state effect on an actor.
        /// </summary>
        public class CombatStateEffect
        {
            public ActorCombatStateType StateType { get; set; }
            public int RemainingTurns { get; set; }
            public int Value { get; set; } // For damage/heal effects
            public CombatActorController Source { get; set; } // Who applied this state

            public CombatStateEffect(ActorCombatStateType stateType, int duration, int value = 0, CombatActorController source = null)
            {
                StateType = stateType;
                RemainingTurns = duration;
                Value = value;
                Source = source;
            }
        }

        /// <summary>
        /// Apply a state effect to an actor.
        /// </summary>
        public bool ApplyState(CombatActorController target, ActorCombatStateType stateType, int duration, int value = 0, CombatActorController source = null)
        {
            if (target == null) return false;

            // Check if target is resistant to this state
            if (IsResistantToState(target, stateType))
            {
                EngineLogger.Log($"{target.GetActor().Info.Name} is resistant to {stateType}");
                return false;
            }

            // Initialize actor states if not exists
            if (!_actorStates.ContainsKey(target))
            {
                _actorStates[target] = new Dictionary<ActorCombatStateType, CombatStateEffect>();
            }

            // Check if state already exists
            if (_actorStates[target].ContainsKey(stateType))
            {
                // Refresh duration
                _actorStates[target][stateType].RemainingTurns = duration;
                _actorStates[target][stateType].Value = value;
                EngineLogger.Log($"Refreshed {stateType} on {target.GetActor().Info.Name}");
            }
            else
            {
                // Apply new state
                _actorStates[target][stateType] = new CombatStateEffect(stateType, duration, value, source);
                EngineLogger.Log($"Applied {stateType} to {target.GetActor().Info.Name}");

                // Apply immediate effects
                ApplyImmediateStateEffect(target, stateType);
            }

            return true;
        }

        /// <summary>
        /// Remove a state effect from an actor.
        /// </summary>
        public bool RemoveState(CombatActorController target, ActorCombatStateType stateType)
        {
            if (target == null || !_actorStates.ContainsKey(target)) return false;

            if (_actorStates[target].ContainsKey(stateType))
            {
                _actorStates[target].Remove(stateType);
                EngineLogger.Log($"Removed {stateType} from {target.GetActor().Info.Name}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Remove all state effects from an actor.
        /// </summary>
        public void ClearAllStates(CombatActorController target)
        {
            if (target == null || !_actorStates.ContainsKey(target)) return;

            _actorStates[target].Clear();
            EngineLogger.Log($"Cleared all states from {target.GetActor().Info.Name}");
        }

        /// <summary>
        /// Check if an actor has a specific state.
        /// </summary>
        public bool HasState(CombatActorController target, ActorCombatStateType stateType)
        {
            return target != null &&
                   _actorStates.ContainsKey(target) &&
                   _actorStates[target].ContainsKey(stateType);
        }

        /// <summary>
        /// Get all active states for an actor.
        /// </summary>
        public IEnumerable<CombatStateEffect> GetActiveStates(CombatActorController target)
        {
            if (target == null || !_actorStates.ContainsKey(target))
                return Enumerable.Empty<CombatStateEffect>();

            return _actorStates[target].Values;
        }

        /// <summary>
        /// Process state effects at the beginning of a turn.
        /// </summary>
        public void ProcessTurnStart(CombatActorController actor)
        {
            if (actor == null || !_actorStates.ContainsKey(actor)) return;

            var statesToRemove = new List<ActorCombatStateType>();

            foreach (var stateEntry in _actorStates[actor])
            {
                var state = stateEntry.Value;

                // Apply turn-start effects
                ApplyTurnStartEffect(actor, state);

                // Decrease duration
                state.RemainingTurns--;

                // Check if state expired
                if (state.RemainingTurns <= 0)
                {
                    statesToRemove.Add(state.StateType);
                }
            }

            // Remove expired states
            foreach (var stateType in statesToRemove)
            {
                _actorStates[actor].Remove(stateType);
                EngineLogger.Log($"{stateType} expired on {actor.GetActor().Info.Name}");
            }
        }

        /// <summary>
        /// Process state effects at the end of a turn.
        /// </summary>
        public void ProcessTurnEnd(CombatActorController actor)
        {
            if (actor == null || !_actorStates.ContainsKey(actor)) return;

            foreach (var stateEntry in _actorStates[actor])
            {
                ApplyTurnEndEffect(actor, stateEntry.Value);
            }
        }

        /// <summary>
        /// Check if actor can take action (not paralyzed, sleeping, etc.).
        /// </summary>
        public bool CanTakeAction(CombatActorController actor)
        {
            if (actor == null || !_actorStates.ContainsKey(actor)) return true;

            // Check for states that prevent action
            var preventingStates = new[]
            {
                ActorCombatStateType.Paralysis,
                ActorCombatStateType.Sleep,
                ActorCombatStateType.Seal,
                ActorCombatStateType.Forbidden
            };

            foreach (var stateType in preventingStates)
            {
                if (_actorStates[actor].ContainsKey(stateType))
                {
                    EngineLogger.Log($"{actor.GetActor().Info.Name} cannot act due to {stateType}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if actor is resistant to a specific state.
        /// </summary>
        private bool IsResistantToState(CombatActorController target, ActorCombatStateType stateType)
        {
            // Check for resistance states
            var resistanceStates = new Dictionary<ActorCombatStateType, ActorCombatStateType[]>
            {
                { ActorCombatStateType.PoisonWind, new[] { ActorCombatStateType.PoisonResist, ActorCombatStateType.PoisonAny } },
                { ActorCombatStateType.PoisonThunder, new[] { ActorCombatStateType.PoisonResist, ActorCombatStateType.PoisonAny } },
                { ActorCombatStateType.PoisonWater, new[] { ActorCombatStateType.PoisonResist, ActorCombatStateType.PoisonAny } },
                { ActorCombatStateType.PoisonFire, new[] { ActorCombatStateType.PoisonResist, ActorCombatStateType.PoisonAny } },
                { ActorCombatStateType.PoisonEarth, new[] { ActorCombatStateType.PoisonResist, ActorCombatStateType.PoisonAny } },
            };

            if (resistanceStates.ContainsKey(stateType))
            {
                foreach (var resistance in resistanceStates[stateType])
                {
                    if (HasState(target, resistance))
                    {
                        return true;
                    }
                }
            }

            // Check for general resistance states
            if (HasState(target, ActorCombatStateType.EvilResist) ||
                HasState(target, ActorCombatStateType.DemonResist))
            {
                // These might provide general resistance
                // For now, we'll implement specific resistances
            }

            return false;
        }

        /// <summary>
        /// Apply immediate state effect when state is first applied.
        /// </summary>
        private void ApplyImmediateStateEffect(CombatActorController target, ActorCombatStateType stateType)
        {
            // Some states have immediate effects when applied
            switch (stateType)
            {
                case ActorCombatStateType.Death:
                    // Instant death
                    var runtimeState = target.GetRuntimeState();
                    if (runtimeState != null)
                    {
                        runtimeState.TakeDamage(runtimeState.CurrentHp);
                    }
                    break;

                case ActorCombatStateType.Dying:
                    // Set HP to 1
                    var dyingState = target.GetRuntimeState();
                    if (dyingState != null && dyingState.CurrentHp > 1)
                    {
                        dyingState.TakeDamage(dyingState.CurrentHp - 1);
                    }
                    break;
            }
        }

        /// <summary>
        /// Apply turn-start effects for a state.
        /// </summary>
        private void ApplyTurnStartEffect(CombatActorController actor, CombatStateEffect state)
        {
            var runtimeState = actor.GetRuntimeState();
            if (runtimeState == null) return;

            switch (state.StateType)
            {
                case ActorCombatStateType.PoisonWind:
                case ActorCombatStateType.PoisonThunder:
                case ActorCombatStateType.PoisonWater:
                case ActorCombatStateType.PoisonFire:
                case ActorCombatStateType.PoisonEarth:
                    // Poison damage at turn start
                    int poisonDamage = state.Value > 0 ? state.Value : (int)(runtimeState.MaxHp * 0.05f);
                    runtimeState.TakeDamage(poisonDamage);
                    EngineLogger.Log($"{actor.GetActor().Info.Name} takes {poisonDamage} poison damage");
                    break;

                case ActorCombatStateType.Regeneration:
                    // HP regeneration
                    int healAmount = state.Value > 0 ? state.Value : (int)(runtimeState.MaxHp * 0.05f);
                    runtimeState.Heal(healAmount);
                    EngineLogger.Log($"{actor.GetActor().Info.Name} regenerates {healAmount} HP");
                    break;
            }
        }

        /// <summary>
        /// Apply turn-end effects for a state.
        /// </summary>
        private void ApplyTurnEndEffect(CombatActorController actor, CombatStateEffect state)
        {
            // Most state effects are applied at turn start
            // Turn-end effects could include things like confusion causing random actions
        }

        /// <summary>
        /// Get damage modifier based on active states.
        /// </summary>
        public float GetDamageModifier(CombatActorController actor)
        {
            if (actor == null || !_actorStates.ContainsKey(actor)) return 1.0f;

            float modifier = 1.0f;

            foreach (var stateEntry in _actorStates[actor])
            {
                switch (stateEntry.Key)
                {
                    case ActorCombatStateType.AttackIncrease:
                        modifier *= 1.1f; // 10% increase
                        break;
                    case ActorCombatStateType.AttackDecrease:
                        modifier *= 0.9f; // 10% decrease
                        break;
                    case ActorCombatStateType.Madness:
                        modifier *= 1.5f; // 50% increase but may attack randomly
                        break;
                }
            }

            return modifier;
        }

        /// <summary>
        /// Get defense modifier based on active states.
        /// </summary>
        public float GetDefenseModifier(CombatActorController actor)
        {
            if (actor == null || !_actorStates.ContainsKey(actor)) return 1.0f;

            float modifier = 1.0f;

            foreach (var stateEntry in _actorStates[actor])
            {
                switch (stateEntry.Key)
                {
                    case ActorCombatStateType.DefenseIncrease:
                        modifier *= 1.1f; // 10% increase
                        break;
                    case ActorCombatStateType.DefenseDecrease:
                        modifier *= 0.9f; // 10% decrease
                        break;
                }
            }

            return modifier;
        }

        /// <summary>
        /// Get speed modifier based on active states.
        /// </summary>
        public float GetSpeedModifier(CombatActorController actor)
        {
            if (actor == null || !_actorStates.ContainsKey(actor)) return 1.0f;

            float modifier = 1.0f;

            foreach (var stateEntry in _actorStates[actor])
            {
                switch (stateEntry.Key)
                {
                    case ActorCombatStateType.SpeedIncrease:
                        modifier *= 1.1f; // 10% increase
                        break;
                    case ActorCombatStateType.SpeedDecrease:
                        modifier *= 0.9f; // 10% decrease
                        break;
                }
            }

            return modifier;
        }

        /// <summary>
        /// Check if actor reflects physical attacks.
        /// </summary>
        public bool ReflectsPhysicalAttack(CombatActorController actor)
        {
            return HasState(actor, ActorCombatStateType.Reflection);
        }

        /// <summary>
        /// Check if actor evades physical attacks.
        /// </summary>
        public bool EvadesPhysicalAttack(CombatActorController actor)
        {
            return HasState(actor, ActorCombatStateType.Evade);
        }

        /// <summary>
        /// Check if actor has barrier against magic attacks.
        /// </summary>
        public bool HasMagicBarrier(CombatActorController actor)
        {
            return HasState(actor, ActorCombatStateType.Barrier);
        }

        /// <summary>
        /// Check if actor is invisible.
        /// </summary>
        public bool IsInvisible(CombatActorController actor)
        {
            return HasState(actor, ActorCombatStateType.Invisible);
        }
    }
}