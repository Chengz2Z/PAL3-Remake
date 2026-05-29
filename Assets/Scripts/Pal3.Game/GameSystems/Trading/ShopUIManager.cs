// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Trading
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Command;
    using Command.Extensions;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Contract.Enums;
    using Core.Utilities;
    using Engine.Animation;
    using Engine.Coroutine;
    using Engine.Extensions;
    using Engine.Logging;
    using Engine.Services;
    using Engine.UI;
    using Input;
    using State;
    using TMPro;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.InputSystem;
    using UnityEngine.UI;

    public sealed class ShopUIManager : IDisposable,
        ICommandExecutor<ResetGameStateCommand>
    {
        private const float UI_ANIMATION_DURATION = 0.15f;
        private const int MAX_ITEMS_PER_PAGE = 8;

        private readonly GameResourceProvider _resourceProvider;
        private readonly InventoryManager _inventoryManager;
        private readonly GameStateManager _gameStateManager;
        private readonly InputManager _inputManager;
        private readonly PlayerInputActions _inputActions;

        private readonly CanvasGroup _shopCanvasGroup;
        private readonly TextMeshProUGUI _shopNameText;
        private readonly TextMeshProUGUI _playerMoneyText;
        private readonly Transform _itemListContainer;
        private readonly GameObject _shopItemPrefab;
        private readonly Button _buyButton;
        private readonly Button _sellButton;
        private readonly Button _closeButton;
        private readonly TextMeshProUGUI _itemDescriptionText;
        private readonly TextMeshProUGUI _itemPriceText;

        private ShopData _currentShopData;
        private readonly List<GameObject> _itemButtons = new();
        private int _selectedItemIndex = -1;
        private bool _isShopVisible;
        private bool _isBuyMode = true;

        public ShopUIManager(
            GameResourceProvider resourceProvider,
            InventoryManager inventoryManager,
            GameStateManager gameStateManager,
            InputManager inputManager,
            CanvasGroup shopCanvasGroup,
            TextMeshProUGUI shopNameText,
            TextMeshProUGUI playerMoneyText,
            Transform itemListContainer,
            GameObject shopItemPrefab,
            Button buyButton,
            Button sellButton,
            Button closeButton,
            TextMeshProUGUI itemDescriptionText,
            TextMeshProUGUI itemPriceText)
        {
            _resourceProvider = Requires.IsNotNull(resourceProvider, nameof(resourceProvider));
            _inventoryManager = Requires.IsNotNull(inventoryManager, nameof(inventoryManager));
            _gameStateManager = Requires.IsNotNull(gameStateManager, nameof(gameStateManager));
            _inputManager = Requires.IsNotNull(inputManager, nameof(inputManager));

            _shopCanvasGroup = Requires.IsNotNull(shopCanvasGroup, nameof(shopCanvasGroup));
            _shopNameText = Requires.IsNotNull(shopNameText, nameof(shopNameText));
            _playerMoneyText = Requires.IsNotNull(playerMoneyText, nameof(playerMoneyText));
            _itemListContainer = Requires.IsNotNull(itemListContainer, nameof(itemListContainer));
            _shopItemPrefab = Requires.IsNotNull(shopItemPrefab, nameof(shopItemPrefab));
            _buyButton = Requires.IsNotNull(buyButton, nameof(buyButton));
            _sellButton = Requires.IsNotNull(sellButton, nameof(sellButton));
            _closeButton = Requires.IsNotNull(closeButton, nameof(closeButton));
            _itemDescriptionText = Requires.IsNotNull(itemDescriptionText, nameof(itemDescriptionText));
            _itemPriceText = Requires.IsNotNull(itemPriceText, nameof(itemPriceText));

            _inputActions = inputManager.GetPlayerInputActions();

            // 初始化UI状态
            _shopCanvasGroup.alpha = 0f;
            _shopCanvasGroup.interactable = false;
            _shopCanvasGroup.blocksRaycasts = false;
            _isShopVisible = false;

            // 绑定按钮事件
            _buyButton.onClick.AddListener(OnBuyButtonClicked);
            _sellButton.onClick.AddListener(OnSellButtonClicked);
            _closeButton.onClick.AddListener(OnCloseButtonClicked);

            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Dispose()
        {
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);

            _buyButton.onClick.RemoveListener(OnBuyButtonClicked);
            _sellButton.onClick.RemoveListener(OnSellButtonClicked);
            _closeButton.onClick.RemoveListener(OnCloseButtonClicked);

            ClearItemButtons();
        }

        public void ShowShop(ShopData shopData)
        {
            if (_isShopVisible)
            {
                EngineLogger.LogWarning("Shop is already visible");
                return;
            }

            _currentShopData = shopData;
            _isBuyMode = true;
            _selectedItemIndex = -1;

            // 更新UI文本
            _shopNameText.text = shopData.ShopName;
            UpdateMoneyDisplay();
            UpdateModeButtons();

            // 显示商品列表
            ShowBuyItems();

            // 播放显示动画
            _gameStateManager.GoToState(GameState.UI);
            _shopCanvasGroup.blocksRaycasts = true;
            _isShopVisible = true;

            CoroutineRunner.StartCoroutine(PlayShowAnimationAsync(true));
        }

        public void HideShop()
        {
            if (!_isShopVisible) return;

            _isShopVisible = false;
            _shopCanvasGroup.interactable = false;

            CoroutineRunner.StartCoroutine(PlayShowAnimationAsync(false));
        }

        private void UpdateMoneyDisplay()
        {
            int money = _inventoryManager.GetTotalMoney();
            _playerMoneyText.text = $"金钱: {money}";
        }

        private void UpdateModeButtons()
        {
            _buyButton.interactable = !_isBuyMode;
            _sellButton.interactable = _isBuyMode;

            Color buyColor = _isBuyMode ? new Color(0.8f, 0.8f, 0.8f) : Color.white;
            Color sellColor = !_isBuyMode ? new Color(0.8f, 0.8f, 0.8f) : Color.white;

            _buyButton.GetComponent<Image>().color = buyColor;
            _sellButton.GetComponent<Image>().color = sellColor;
        }

        private void ShowBuyItems()
        {
            ClearItemButtons();

            if (_currentShopData?.Items == null) return;

            for (int i = 0; i < _currentShopData.Items.Count; i++)
            {
                ShopItem item = _currentShopData.Items[i];
                CreateItemButton(item, i);
            }
        }

        private void ShowSellItems()
        {
            ClearItemButtons();

            IEnumerable<KeyValuePair<int, int>> playerItems = _inventoryManager.GetAllItems();
            IDictionary<int, GameItemInfo> gameItemInfos = _resourceProvider.GetGameItemInfos();

            int index = 0;
            foreach (KeyValuePair<int, int> playerItem in playerItems)
            {
                if (playerItem.Key == 0) continue; // Skip money

                if (gameItemInfos.TryGetValue(playerItem.Key, out GameItemInfo itemInfo))
                {
                    ShopItem shopItem = new ShopItem
                    {
                        Id = playerItem.Key,
                        Name = itemInfo.Name,
                        Description = itemInfo.Description,
                        Price = itemInfo.Price / 2, // 卖出价格是买入的一半
                        Type = (int)itemInfo.Type
                    };
                    CreateItemButton(shopItem, index, playerItem.Value);
                    index++;
                }
            }
        }

        private void CreateItemButton(ShopItem item, int index, int count = -1)
        {
            GameObject buttonObj = UnityEngine.Object.Instantiate(_shopItemPrefab, _itemListContainer);
            buttonObj.SetActive(true);

            TextMeshProUGUI nameText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (nameText != null)
            {
                string countStr = count > 0 ? $" x{count}" : "";
                nameText.text = $"{item.Name}{countStr}";
            }

            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                int itemIndex = index;
                button.onClick.AddListener(() => OnItemSelected(itemIndex));
            }

            _itemButtons.Add(buttonObj);
        }

        private void ClearItemButtons()
        {
            foreach (GameObject button in _itemButtons)
            {
                UnityEngine.Object.Destroy(button);
            }
            _itemButtons.Clear();
            _selectedItemIndex = -1;
            ClearItemInfo();
        }

        private void OnItemSelected(int index)
        {
            _selectedItemIndex = index;
            ShopItem item = GetSelectedItem();

            if (item != null)
            {
                _itemDescriptionText.text = item.Description ?? "无描述";
                _itemPriceText.text = $"价格: {item.Price}";
            }
        }

        private ShopItem GetSelectedItem()
        {
            if (_selectedItemIndex < 0) return null;

            if (_isBuyMode)
            {
                if (_currentShopData?.Items != null && _selectedItemIndex < _currentShopData.Items.Count)
                {
                    return _currentShopData.Items[_selectedItemIndex];
                }
            }
            else
            {
                // 卖出模式需要从玩家物品中获取
                IEnumerable<KeyValuePair<int, int>> playerItems = _inventoryManager.GetAllItems();
                IDictionary<int, GameItemInfo> gameItemInfos = _resourceProvider.GetGameItemInfos();

                int index = 0;
                foreach (KeyValuePair<int, int> playerItem in playerItems)
                {
                    if (playerItem.Key == 0) continue;

                    if (index == _selectedItemIndex && gameItemInfos.TryGetValue(playerItem.Key, out GameItemInfo itemInfo))
                    {
                        return new ShopItem
                        {
                            Id = playerItem.Key,
                            Name = itemInfo.Name,
                            Description = itemInfo.Description,
                            Price = itemInfo.Price / 2,
                            Type = (int)itemInfo.Type
                        };
                    }
                    index++;
                }
            }

            return null;
        }

        private void ClearItemInfo()
        {
            _itemDescriptionText.text = "";
            _itemPriceText.text = "";
        }

        private void OnBuyButtonClicked()
        {
            if (!_isBuyMode)
            {
                _isBuyMode = true;
                UpdateModeButtons();
                ShowBuyItems();
            }
        }

        private void OnSellButtonClicked()
        {
            if (_isBuyMode)
            {
                _isBuyMode = false;
                UpdateModeButtons();
                ShowSellItems();
            }
        }

        private void OnCloseButtonClicked()
        {
            HideShop();
        }

        private void OnBuyItemSelected()
        {
            ShopItem item = GetSelectedItem();
            if (item == null) return;

            int playerMoney = _inventoryManager.GetTotalMoney();
            if (playerMoney < item.Price)
            {
                EngineLogger.Log("Not enough money");
                Pal3.Instance.Execute(new UIDisplayNoteCommand("金钱不足！"));
                return;
            }

            // 执行购买
            Pal3.Instance.Execute(new InventoryAddMoneyCommand(-item.Price));
            Pal3.Instance.Execute(new InventoryAddItemCommand(item.Id, 1));

            UpdateMoneyDisplay();
            EngineLogger.Log($"Bought item: {item.Name} for {item.Price}");
            Pal3.Instance.Execute(new UIDisplayNoteCommand($"购买了 {item.Name}"));
        }

        private void OnSellItemSelected()
        {
            ShopItem item = GetSelectedItem();
            if (item == null) return;

            // 执行出售
            Pal3.Instance.Execute(new InventoryRemoveItemCommand(item.Id, 1));
            Pal3.Instance.Execute(new InventoryAddMoneyCommand(item.Price));

            UpdateMoneyDisplay();
            ShowSellItems(); // 刷新列表
            EngineLogger.Log($"Sold item: {item.Name} for {item.Price}");
            Pal3.Instance.Execute(new UIDisplayNoteCommand($"出售了 {item.Name}，获得 {item.Price} 金钱"));
        }

        private IEnumerator PlayShowAnimationAsync(bool show)
        {
            float startAlpha = show ? 0f : 1f;
            float endAlpha = show ? 1f : 0f;
            float duration = UI_ANIMATION_DURATION;
            float elapsed = 0f;

            _shopCanvasGroup.alpha = startAlpha;

            if (show)
            {
                _shopCanvasGroup.interactable = true;
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _shopCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
                yield return null;
            }

            _shopCanvasGroup.alpha = endAlpha;

            if (!show)
            {
                _shopCanvasGroup.interactable = false;
                _shopCanvasGroup.blocksRaycasts = false;
                _gameStateManager.GoToState(GameState.Gameplay);
                ClearItemButtons();
                _currentShopData = null;
            }
        }

        public void Execute(ResetGameStateCommand command)
        {
            if (_isShopVisible)
            {
                HideShop();
            }
        }
    }
}