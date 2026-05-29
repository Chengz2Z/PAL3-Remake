// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Combat
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Actor.Controllers;
    using Core.Contract.Enums;
    using Core.DataReader.Gdb;
    using Core.Utilities;
    using Data;
    using Scene;
    using UI;
    using UnityEngine;

    /// <summary>
    /// Stage-4 turn-based combat driver with UI support.
    ///
    /// Each round:
    ///   1. Build a turn queue ordered by Speed (descending). Defeated actors are skipped.
    ///   2. For each actor in the queue:
    ///      - If it's a player actor, show the action menu and wait for player input.
    ///      - If it's an enemy actor, pick a random target and perform a normal attack.
    ///   3. After every individual turn, check whether either side has been wiped out;
    ///      if so the combat ends.
    /// </summary>
    public sealed class CombatTurnSystem
    {
        public CombatPhase Phase { get; private set; } = CombatPhase.Idle;
        public bool IsPlayerWin { get; private set; }
        public bool IsFinished => Phase == CombatPhase.Finished;

        private readonly CombatScene _combatScene;
        private readonly Func<IEnumerator, object> _coroutineRunner;
        private readonly CombatUIManager _combatUIManager;
        private readonly SkillManager _skillManager;
        private readonly CombatItemManager _combatItemManager;
        private readonly CombatStateManager _combatStateManager;
        private readonly GameResourceProvider _resourceProvider;

        private readonly Queue<CombatActorController> _turnQueue = new();
        private bool _actionInProgress;
        private int _roundNumber;
        private CombatActorController _currentActor;
        private CombatActionSelection _playerAction;

        public CombatTurnSystem(CombatScene combatScene,
            Func<IEnumerator, object> coroutineRunner,
            CombatUIManager combatUIManager = null,
            SkillManager skillManager = null,
            CombatItemManager combatItemManager = null,
            CombatStateManager combatStateManager = null,
            GameResourceProvider resourceProvider = null)
        {
            _combatScene = Requires.IsNotNull(combatScene, nameof(combatScene));
            _coroutineRunner = Requires.IsNotNull(coroutineRunner, nameof(coroutineRunner));
            _combatUIManager = combatUIManager;
            _skillManager = skillManager;
            _combatItemManager = combatItemManager;
            _combatStateManager = combatStateManager;
            _resourceProvider = resourceProvider;
        }

        public void Begin()
        {
            _roundNumber = 0;
            Phase = CombatPhase.TurnStart;
        }

        public void Tick()
        {
            switch (Phase)
            {
                case CombatPhase.Idle:
                case CombatPhase.Finished:
                    return;

                case CombatPhase.TurnStart:
                    // Process state effects at turn start
                    if (_currentActor != null && _combatStateManager != null)
                    {
                        _combatStateManager.ProcessTurnStart(_currentActor);
                    }
                    AdvanceToNextActor();
                    break;

                case CombatPhase.WaitingForPlayerInput:
                    // Waiting for player to select an action
                    break;

                case CombatPhase.ActorActing:
                    if (!_actionInProgress) Phase = CombatPhase.TurnEnd;
                    break;

                case CombatPhase.TurnEnd:
                    // Process state effects at turn end
                    if (_currentActor != null && _combatStateManager != null)
                    {
                        _combatStateManager.ProcessTurnEnd(_currentActor);
                    }
                    Phase = CombatPhase.CheckResult;
                    break;

                case CombatPhase.CheckResult:
                    EvaluateResult();
                    break;
            }
        }

        private void AdvanceToNextActor()
        {
            if (_turnQueue.Count == 0)
            {
                BuildRoundQueue();
                if (_turnQueue.Count == 0)
                {
                    Phase = CombatPhase.CheckResult;
                    return;
                }
            }

            CombatActorController next = null;
            while (_turnQueue.Count > 0)
            {
                CombatActorController candidate = _turnQueue.Dequeue();
                if (candidate != null && !candidate.IsDefeated)
                {
                    next = candidate;
                    break;
                }
            }

            if (next == null)
            {
                Phase = CombatPhase.CheckResult;
                return;
            }

            _currentActor = next;

            // Check if actor can take action (not paralyzed, sleeping, etc.)
            if (_combatStateManager != null && !_combatStateManager.CanTakeAction(next))
            {
                // Actor cannot act due to state effect, skip turn
                Phase = CombatPhase.TurnEnd;
                return;
            }

            // Check if this is a player actor
            if (IsPlayerActor(next))
            {
                // Show action menu for player actor
                if (_combatUIManager != null)
                {
                    Phase = CombatPhase.WaitingForPlayerInput;
                    _combatUIManager.ShowActionMenu(OnPlayerActionSelected, next);
                }
                else
                {
                    // Fallback to auto-attack if no UI manager
                    PerformAutoAttack(next);
                }
            }
            else
            {
                // Enemy actor - auto attack
                PerformAutoAttack(next);
            }
        }

        private void PerformAutoAttack(CombatActorController actor)
        {
            CombatActorController target = PickTarget(actor);
            if (target == null)
            {
                Phase = CombatPhase.CheckResult;
                return;
            }

            _actionInProgress = true;
            _coroutineRunner(RunActorTurn(actor, target, CombatActionType.Attack));
            Phase = CombatPhase.ActorActing;
        }

        private void OnPlayerActionSelected(CombatActionSelection selection)
        {
            _playerAction = selection;
            _actionInProgress = true;

            CombatActorController target = selection.Target;

            // If no target selected (defend/flee), use self as target
            if (target == null)
            {
                target = _currentActor;
            }

            _coroutineRunner(RunActorTurn(_currentActor, target, selection.ActionType));
            Phase = CombatPhase.ActorActing;
        }

        private IEnumerator RunActorTurn(CombatActorController actor,
            CombatActorController target,
            CombatActionType actionType)
        {
            switch (actionType)
            {
                case CombatActionType.Attack:
                    yield return actor.StartNormalAttackAsync(target, _combatScene);
                    break;

                case CombatActionType.Skill:
                    if (_skillManager != null && _playerAction != null && _playerAction.SkillId > 0)
                    {
                        SkillInfo skillInfo = _skillManager.GetSkillInfo(_playerAction.SkillId);
                        if (skillInfo.Id != 0)
                        {
                            _skillManager.ExecuteSkill(actor, target, skillInfo);

                            // Apply skill state effects
                            if (_combatStateManager != null && skillInfo.CombatStateImpactTypes != null)
                            {
                                foreach (var stateImpact in skillInfo.CombatStateImpactTypes)
                                {
                                    if (stateImpact.Value == CombatStateImpactType.Increase)
                                    {
                                        _combatStateManager.ApplyState(target, stateImpact.Key, 3); // 3 turns duration
                                    }
                                    else if (stateImpact.Value == CombatStateImpactType.Remove)
                                    {
                                        _combatStateManager.RemoveState(target, stateImpact.Key);
                                    }
                                }
                            }

                            // Show skill effect animation or message
                            yield return new WaitForSeconds(1.0f);
                        }
                        else
                        {
                            // Fallback to normal attack
                            yield return actor.StartNormalAttackAsync(target, _combatScene);
                        }
                    }
                    else
                    {
                        // Fallback to normal attack
                        yield return actor.StartNormalAttackAsync(target, _combatScene);
                    }
                    break;

                case CombatActionType.Item:
                    if (_combatItemManager != null && _playerAction != null && _playerAction.ItemId > 0)
                    {
                        GameItemInfo itemInfo = _resourceProvider.GetGameItemInfos()[_playerAction.ItemId];
                        if (itemInfo.Id != 0)
                        {
                            _combatItemManager.UseItem(actor, target, itemInfo);
                            // Show item use animation or message
                            yield return new WaitForSeconds(1.0f);
                        }
                        else
                        {
                            // Fallback to normal attack
                            yield return actor.StartNormalAttackAsync(target, _combatScene);
                        }
                    }
                    else
                    {
                        // Fallback to normal attack
                        yield return actor.StartNormalAttackAsync(target, _combatScene);
                    }
                    break;

                case CombatActionType.Defend:
                    // Implement defend action (reduce damage for one turn)
                    // For now, just wait a bit
                    yield return new WaitForSeconds(0.5f);
                    break;

                case CombatActionType.Flee:
                    // Implement flee logic
                    // For now, just wait a bit
                    yield return new WaitForSeconds(0.5f);
                    break;
            }

            _actionInProgress = false;
        }

        private bool IsPlayerActor(CombatActorController actor)
        {
            ElementPosition position = actor.GetElementPosition();
            return DamageCalculator.IsAlly(position);
        }

        private CombatActorController PickTarget(CombatActorController attacker)
        {
            bool attackerIsAlly = DamageCalculator.IsAlly(attacker.GetElementPosition());

            List<CombatActorController> candidates = new();
            foreach ((ElementPosition position, CombatActorController controller) in
                     _combatScene.GetAllCombatActorControllers())
            {
                if (controller == null || controller.IsDefeated) continue;
                bool isAlly = DamageCalculator.IsAlly(position);
                if (isAlly == attackerIsAlly) continue;
                candidates.Add(controller);
            }

            if (candidates.Count == 0) return null;
            return candidates[RandomGenerator.Range(0, candidates.Count)];
        }

        private void BuildRoundQueue()
        {
            _roundNumber++;

            IEnumerable<CombatActorController> living = _combatScene.GetAllCombatActorControllers().Values
                .Where(c => c != null && !c.IsDefeated)
                .OrderByDescending(c => c.GetRuntimeState().Speed)
                .ThenBy(c => (int)c.GetElementPosition());

            foreach (CombatActorController controller in living)
            {
                _turnQueue.Enqueue(controller);
            }
        }

        private void EvaluateResult()
        {
            bool anyAllyAlive = false;
            bool anyEnemyAlive = false;

            foreach ((ElementPosition position, CombatActorController controller) in
                     _combatScene.GetAllCombatActorControllers())
            {
                if (controller == null || controller.IsDefeated) continue;
                if (DamageCalculator.IsAlly(position)) anyAllyAlive = true;
                else if (DamageCalculator.IsEnemy(position)) anyEnemyAlive = true;
            }

            if (!anyAllyAlive)
            {
                IsPlayerWin = false;
                Phase = CombatPhase.Finished;
            }
            else if (!anyEnemyAlive)
            {
                IsPlayerWin = true;
                Phase = CombatPhase.Finished;
            }
            else
            {
                Phase = CombatPhase.TurnStart;
            }
        }
    }
}