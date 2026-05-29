// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Trading
{
    using System;
    using System.Collections.Generic;
    using Command;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Contract.Constants;
    using Core.DataReader.Cpk;
    using Core.DataReader.DealScript;
    using Core.DataReader.Gdb;
    using Data;
    using Engine.Logging;

    public sealed class TradingManager : IDisposable,
        ICommandExecutor<UIShowDealerMenuCommand>
    {
        private readonly GameResourceProvider _resourceProvider;
        private readonly ShopUIManager _shopUIManager;
        private readonly Dictionary<string, DealScriptFile> _dealScriptCache = new();

        public TradingManager(GameResourceProvider resourceProvider, ShopUIManager shopUIManager)
        {
            _resourceProvider = resourceProvider;
            _shopUIManager = shopUIManager;
            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Dispose()
        {
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);
            _dealScriptCache.Clear();
        }

        public void Execute(UIShowDealerMenuCommand command)
        {
            string dealScriptPath = command.DealerScriptName;

            if (string.IsNullOrEmpty(dealScriptPath))
            {
                EngineLogger.LogError("UIShowDealerMenuCommand: dealerScriptName is empty");
                return;
            }

            // 规范化路径
            dealScriptPath = dealScriptPath.Replace('\\', CpkConstants.DirectorySeparatorChar);

            // 确保路径以DealScript文件夹开头
            if (!dealScriptPath.StartsWith(FileConstants.DealScriptFolderVirtualPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                dealScriptPath = FileConstants.DealScriptFolderVirtualPath + dealScriptPath;
            }

            // 确保有.txt扩展名
            if (!dealScriptPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                dealScriptPath += ".txt";
            }

            dealScriptPath = dealScriptPath.ToLower();

            DealScriptFile dealScript = LoadDealScriptFile(dealScriptPath);
            if (dealScript == null)
            {
                EngineLogger.LogError($"Failed to load deal script: {dealScriptPath}");
                return;
            }

            // 获取物品信息字典
            IDictionary<int, GameItemInfo> gameItemInfos = _resourceProvider.GetGameItemInfos();

            // 构建商店数据
            ShopData shopData = ConvertToShopData(dealScript, gameItemInfos);

            // 显示商店UI
            _shopUIManager.ShowShop(shopData);
        }

        private DealScriptFile LoadDealScriptFile(string path)
        {
            if (_dealScriptCache.TryGetValue(path, out DealScriptFile cached))
            {
                return cached;
            }

            try
            {
                DealScriptFile dealScript = _resourceProvider.GetGameResourceFile<DealScriptFile>(path);
                _dealScriptCache[path] = dealScript;
                return dealScript;
            }
            catch (Exception ex)
            {
                EngineLogger.LogError($"Failed to load deal script file '{path}': {ex.Message}");
                return null;
            }
        }

        private static ShopData ConvertToShopData(DealScriptFile dealScript,
            IDictionary<int, GameItemInfo> gameItemInfos)
        {
            var shopData = new ShopData
            {
                ShopName = dealScript.MerchantName ?? "商店",
                ShopType = (int)dealScript.Type,
                Items = new List<ShopItem>()
            };

            // 添加装备物品
            if (dealScript.EquipmentIds != null)
            {
                foreach (int itemId in dealScript.EquipmentIds)
                {
                    if (gameItemInfos.TryGetValue(itemId, out GameItemInfo itemInfo))
                    {
                        shopData.Items.Add(new ShopItem
                        {
                            Id = itemId,
                            Name = itemInfo.Name,
                            Description = itemInfo.Description,
                            Price = itemInfo.Price,
                            Type = (int)itemInfo.Type
                        });
                    }
                }
            }

            // 添加道具物品
            if (dealScript.PropIds != null)
            {
                foreach (int itemId in dealScript.PropIds)
                {
                    if (gameItemInfos.TryGetValue(itemId, out GameItemInfo itemInfo))
                    {
                        shopData.Items.Add(new ShopItem
                        {
                            Id = itemId,
                            Name = itemInfo.Name,
                            Description = itemInfo.Description,
                            Price = itemInfo.Price,
                            Type = (int)itemInfo.Type
                        });
                    }
                }
            }

            return shopData;
        }
    }
}