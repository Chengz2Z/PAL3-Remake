// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Combat
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Actor;
    using Actor.Controllers;
    using Core.Contract.Enums;
    using Core.DataReader.Gdb;
    using Core.Utilities;
    using Data;
    using Engine.Logging;

    /// <summary>
    /// Manages combat skills, including skill data retrieval and skill execution.
    /// </summary>
    public sealed class SkillManager
    {
        private readonly GameResourceProvider _resourceProvider;
        private readonly IDictionary<int, SkillInfo> _skillInfos;

        public SkillManager(GameResourceProvider resourceProvider)
        {
            _resourceProvider = Requires.IsNotNull(resourceProvider, nameof(resourceProvider));
            _skillInfos = _resourceProvider.GetSkillInfos();
        }

        /// <summary>
        /// Get all available skills for a specific actor.
        /// </summary>
        public IEnumerable<SkillInfo> GetAvailableSkills(CombatActorController actorController)
        {
            if (actorController == null) yield break;

            CombatActor actor = actorController.GetActor();
            if (actor == null) yield break;

            // Get skills that the actor can use based on their ID
            foreach (var skillInfo in _skillInfos.Values)
            {
                if (CanActorUseSkill(actorController, skillInfo))
                {
                    yield return skillInfo;
                }
            }
        }

        /// <summary>
        /// Check if an actor can use a specific skill.
        /// </summary>
        public bool CanActorUseSkill(CombatActorController actorController, SkillInfo skillInfo)
        {
            if (actorController == null || skillInfo.Id == 0) return false;

            CombatActor actor = actorController.GetActor();
            if (actor == null) return false;

            // Check if the skill is applicable to this actor
            if (skillInfo.ApplicableActors != null && skillInfo.ApplicableActors.Count > 0)
            {
                // Convert actor ID to PlayerActorId for comparison
                PlayerActorId actorId = GetPlayerActorIdFromCombatActor(actor);
                if (!skillInfo.ApplicableActors.Contains(actorId))
                {
                    return false;
                }
            }

            // Check if actor has enough MP
            CombatActorRuntimeState runtimeState = actorController.GetRuntimeState();
            if (runtimeState != null && runtimeState.CurrentMp < skillInfo.MpConsumeValue)
            {
                return false;
            }

            // Check if actor meets level requirement
            if (runtimeState != null && actor.Info.Level < skillInfo.RequiredActorLevel)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get skill info by skill ID.
        /// </summary>
        public SkillInfo GetSkillInfo(int skillId)
        {
            _skillInfos.TryGetValue(skillId, out SkillInfo skillInfo);
            return skillInfo;
        }

        /// <summary>
        /// Execute a skill effect.
        /// </summary>
        public void ExecuteSkill(CombatActorController caster, CombatActorController target, SkillInfo skillInfo)
        {
            if (caster == null || target == null || skillInfo.Id == 0) return;

            // Consume MP
            CombatActorRuntimeState casterState = caster.GetRuntimeState();
            if (casterState != null)
            {
                casterState.ConsumeMp(skillInfo.MpConsumeValue);
            }

            // Apply skill effects based on target range type
            switch (skillInfo.TargetRangeType)
            {
                case TargetRangeType.EnemyPartySingle:
                case TargetRangeType.FirstPartySingle:
                    ApplySkillEffectToSingleTarget(target, skillInfo);
                    break;

                case TargetRangeType.EnemyPartyAll:
                case TargetRangeType.FirstPartyAll:
                    ApplySkillEffectToAllTargets(caster, skillInfo);
                    break;

                case TargetRangeType.EnemyPartyOneRow:
                    // TODO: Implement row targeting
                    ApplySkillEffectToSingleTarget(target, skillInfo);
                    break;

                case TargetRangeType.EnemyPartyOneColumn:
                    // TODO: Implement column targeting
                    ApplySkillEffectToSingleTarget(target, skillInfo);
                    break;

                default:
                    ApplySkillEffectToSingleTarget(target, skillInfo);
                    break;
            }

            EngineLogger.Log($"Skill {skillInfo.Name} executed by {caster.GetActor().Info.Name}");
        }

        private void ApplySkillEffectToSingleTarget(CombatActorController target, SkillInfo skillInfo)
        {
            if (target == null || skillInfo.Id == 0) return;

            CombatActorRuntimeState targetState = target.GetRuntimeState();
            if (targetState == null) return;

            // Apply attribute impacts
            foreach (var impact in skillInfo.AttributeImpacts)
            {
                switch (impact.Key)
                {
                    case ActorAttributeType.Hp:
                        if (impact.Value.Type == AttributeImpactType.Absolute)
                        {
                            if (impact.Value.Value > 0)
                            {
                                // Heal
                                targetState.Heal(impact.Value.Value);
                            }
                            else
                            {
                                // Damage
                                targetState.TakeDamage(-impact.Value.Value);
                            }
                        }
                        else if (impact.Value.Type == AttributeImpactType.Percentage)
                        {
                            int healAmount = (int)(targetState.MaxHp * (impact.Value.Value / 100f));
                            targetState.Heal(healAmount);
                        }
                        break;

                    case ActorAttributeType.Mp:
                        if (impact.Value.Type == AttributeImpactType.Absolute)
                        {
                            if (impact.Value.Value > 0)
                            {
                                targetState.RestoreMp(impact.Value.Value);
                            }
                            else
                            {
                                targetState.ConsumeMp(-impact.Value.Value);
                            }
                        }
                        break;

                    case ActorAttributeType.Sp:
                        if (impact.Value.Type == AttributeImpactType.Absolute)
                        {
                            if (impact.Value.Value > 0)
                            {
                                targetState.RestoreSp(impact.Value.Value);
                            }
                            else
                            {
                                targetState.ConsumeSp(-impact.Value.Value);
                            }
                        }
                        break;
                }
            }
        }

        private void ApplySkillEffectToAllTargets(CombatActorController caster, SkillInfo skillInfo)
        {
            // Determine if we should target allies or enemies
            bool targetAllies = skillInfo.TargetRangeType == TargetRangeType.FirstPartyAll ||
                               skillInfo.TargetRangeType == TargetRangeType.FirstPartySingle;

            // Get all actors from the combat scene
            // Note: This requires access to CombatScene, which we don't have directly
            // For now, we'll implement this as applying to the single target
            // In a full implementation, we'd need to get all targets from the combat scene
            EngineLogger.LogWarning("All-target skill execution not fully implemented yet");
        }

        private PlayerActorId GetPlayerActorIdFromCombatActor(CombatActor actor)
        {
            // Map combat actor ID to player actor ID
            // This is a simplified mapping - you may need to adjust based on your data
            int actorId = (int)actor.Info.Id;

            // Common mappings for PAL3
            switch (actorId)
            {
                case 1: return PlayerActorId.JingTian;
                case 2: return PlayerActorId.XueJian;
                case 3: return PlayerActorId.LongKui;
                case 4: return PlayerActorId.ZiXuan;
                case 5: return PlayerActorId.ChangQing;
                case 6: return PlayerActorId.HuaYing;
                default: return PlayerActorId.JingTian; // Default
            }
        }
    }
}