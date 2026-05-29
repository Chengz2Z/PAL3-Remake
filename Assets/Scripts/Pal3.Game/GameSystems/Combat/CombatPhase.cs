// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Combat
{
    public enum CombatPhase
    {
        Idle = 0,        // Combat scene loaded but turn system has not started
        TurnStart,       // Decide whose turn is next, advance pointer
        WaitingForPlayerInput, // Waiting for player to select an action
        ActorActing,     // An actor is performing an action (animation/coroutine running)
        TurnEnd,         // Bookkeeping for end of one actor's turn
        CheckResult,     // Win/loss check; transitions to Finished or back to TurnStart
        Finished,        // Outcome decided; CombatManager raises OnCombatFinished
    }
}
