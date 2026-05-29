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
    using Inventory;

    /// <summary>
    /// Manages combat item usage, including item selection and effect execution.
    /// </summary>
    public sealed class CombatItemManager
    {
        private readonly GameResourceProvider _resourceProvider;
        private readonly InventoryManager _inventoryManager;
        private readonly IDictionary<int, GameItemInfo> _gameItemInfos;

        public CombatItemManager(GameResourceProvider resourceProvider, InventoryManager inventoryManager)
        {
            _resourceProvider = Requires.IsNotNull(resourceProvider, nameof(resourceProvider));
            _inventoryManager = Requires.IsNotNull(inventoryManager, nameof(inventoryManager));
            _gameItemInfos = _resourceProvider.GetGameItemInfos();
        }

        /// <summary>
        /// Get all usable items in combat from player's inventory.
        /// </summary>
        public IEnumerable<GameItemInfo> GetUsableCombatItems()
        {
            foreach (var itemEntry in _inventoryManager.GetAllItems())
            {
                int itemId = itemEntry.Key;
                int count = itemEntry.Value;

                if (_gameItemInfos.TryGetValue(itemId, out GameItemInfo itemInfo))
                {
                    // Check if item is usable in combat
                    if (IsItemUsableInCombat(itemInfo))
                    {
                        yield return itemInfo;
                    }
                }
            }
        }

        /// <summary>
        /// Check if an item is usable in combat.
        /// </summary>
        public bool IsItemUsableInCombat(GameItemInfo itemInfo)
        {
            if (itemInfo.Id == 0) return false;

            // Check item type and special type
            switch (itemInfo.Type)
            {
                case ItemType.Healing: // 药品
                case ItemType.Throwable: // 投掷
                    return true;

                case ItemType.Wearable: // 饰品
                    // Some accessories might have combat effects
                    return itemInfo.ItemSpecialType != ItemSpecialType.None;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Check if player has a specific item.
        /// </summary>
        public bool HasItem(int itemId)
        {
            return _inventoryManager.HaveItem(itemId);
        }

        /// <summary>
        /// Get item count in inventory.
        /// </summary>
        public int GetItemCount(int itemId)
        {
            var items = _inventoryManager.GetAllItems();
            foreach (var item in items)
            {
                if (item.Key == itemId)
                {
                    return item.Value;
                }
            }
            return 0;
        }

        /// <summary>
        /// Use an item in combat.
        /// </summary>
        public void UseItem(CombatActorController user, CombatActorController target, GameItemInfo itemInfo)
        {
            if (user == null || target == null || itemInfo.Id == 0) return;

            // Remove item from inventory
            // Note: We need to execute the command through the command system
            // For now, we'll just log the usage
            EngineLogger.Log($"Using item {itemInfo.Name} in combat");

            // Apply item effects based on type
            switch (itemInfo.Type)
            {
                case ItemType.Healing:
                    ApplyDrugEffect(target, itemInfo);
                    break;

                case ItemType.Throwable:
                    ApplyThrowEffect(target, itemInfo);
                    break;

                case ItemType.Wearable:
                    ApplyAccessoryEffect(target, itemInfo);
                    break;
            }

            // Consume the item
            ConsumeItem(itemInfo.Id);
        }

        private void ApplyDrugEffect(CombatActorController target, GameItemInfo itemInfo)
        {
            CombatActorRuntimeState targetState = target.GetRuntimeState();
            if (targetState == null) return;

            foreach (var impact in itemInfo.AttributeImpacts)
            {
                switch (impact.Key)
                {
                    case ActorAttributeType.Hp:
                        if (impact.Value.Type == AttributeImpactType.Absolute)
                        {
                            if (impact.Value.Value > 0)
                            {
                                targetState.Heal(impact.Value.Value);
                            }
                            else
                            {
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

            EngineLogger.Log($"Drug effect applied to {target.GetActor().Info.Name}");
        }

        private void ApplyThrowEffect(CombatActorController target, GameItemInfo itemInfo)
        {
            // Throw items typically deal damage
            CombatActorRuntimeState targetState = target.GetRuntimeState();
            if (targetState == null) return;

            foreach (var impact in itemInfo.AttributeImpacts)
            {
                if (impact.Key == ActorAttributeType.Hp && impact.Value.Value < 0)
                {
                    int damage = -impact.Value.Value;
                    targetState.TakeDamage(damage);
                    EngineLogger.Log($"Throw item dealt {damage} damage to {target.GetActor().Info.Name}");
                }
            }
        }

        private void ApplyAccessoryEffect(CombatActorController target, GameItemInfo itemInfo)
        {
            // Accessories might have special effects
            // For now, just apply basic effects
            ApplyDrugEffect(target, itemInfo);
        }

        private void ConsumeItem(uint itemId)
        {
            // Execute item removal through command system
            // This would normally be done through the command system
            // For now, we'll just log it
            EngineLogger.Log($"Consuming item {itemId}");
        }
    }
}