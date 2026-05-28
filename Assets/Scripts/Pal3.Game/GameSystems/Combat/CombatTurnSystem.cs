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
    using Core.Utilities;
    using Scene;

    /// <summary>
    /// Stage-1 turn-based combat driver.
    ///
    /// Each round:
    ///   1. Build a turn queue ordered by Speed (descending). Defeated actors are skipped.
    ///   2. For each actor in the queue, pick a target on the opposing side and run a
    ///      normal-attack coroutine.
    ///   3. After every individual turn, check whether either side has been wiped out;
    ///      if so the combat ends.
    ///
    /// Player-side action selection is still automated in this stage — the goal here is to
    /// replace the F1-F6 test keys with a proper round/turn loop. UI-driven action menus
    /// land in stage 4.
    /// </summary>
    public sealed class CombatTurnSystem
    {
        public CombatPhase Phase { get; private set; } = CombatPhase.Idle;
        public bool IsPlayerWin { get; private set; }
        public bool IsFinished => Phase == CombatPhase.Finished;

        private readonly CombatScene _combatScene;
        private readonly Func<IEnumerator, object> _coroutineRunner;

        private readonly Queue<CombatActorController> _turnQueue = new();
        private bool _actionInProgress;
        private int _roundNumber;

        public CombatTurnSystem(CombatScene combatScene, Func<IEnumerator, object> coroutineRunner)
        {
            _combatScene = Requires.IsNotNull(combatScene, nameof(combatScene));
            _coroutineRunner = Requires.IsNotNull(coroutineRunner, nameof(coroutineRunner));
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
                    AdvanceToNextActor();
                    break;

                case CombatPhase.ActorActing:
                    if (!_actionInProgress) Phase = CombatPhase.TurnEnd;
                    break;

                case CombatPhase.TurnEnd:
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

            CombatActorController target = PickTarget(next);
            if (target == null)
            {
                Phase = CombatPhase.CheckResult;
                return;
            }

            _actionInProgress = true;
            _coroutineRunner(RunActorTurn(next, target));
            Phase = CombatPhase.ActorActing;
        }

        private IEnumerator RunActorTurn(CombatActorController actor, CombatActorController target)
        {
            yield return actor.StartNormalAttackAsync(target, _combatScene);
            _actionInProgress = false;
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
