// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Combat.UI
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
    using Engine.Core.Abstraction;
    using Engine.Extensions;
    using Engine.Logging;
    using Scene;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Combat action types available to the player.
    /// </summary>
    public enum CombatActionType
    {
        Attack,
        Skill,
        Item,
        Defend,
        Flee,
    }

    /// <summary>
    /// Result of player's combat action selection.
    /// </summary>
    public sealed class CombatActionSelection
    {
        public CombatActionType ActionType { get; set; }
        public CombatActorController Target { get; set; }
        public int SkillId { get; set; } = -1;
        public int ItemId { get; set; } = -1;
    }

    /// <summary>
    /// Manages the combat UI including action menu, HP/MP bars, and damage popups.
    /// </summary>
    public sealed class CombatUIManager : IDisposable
    {
        private const float ACTION_MENU_SHOW_HIDE_DURATION = 0.15f;
        private const float DAMAGE_POPUP_DURATION = 1.5f;
        private const float DAMAGE_POPUP_FLOAT_SPEED = 50f;

        private readonly GameResourceProvider _resourceProvider;
        private readonly CombatScene _combatScene;
        private readonly Canvas _combatUICanvas;
        private readonly EventSystem _eventSystem;
        private readonly SkillManager _skillManager;
        private readonly CombatItemManager _combatItemManager;

        // Action menu UI elements
        private GameObject _actionMenuPanel;
        private Button _attackButton;
        private Button _skillButton;
        private Button _itemButton;
        private Button _defendButton;
        private Button _fleeButton;

        // Skill selection UI
        private GameObject _skillSelectionPanel;
        private List<Button> _skillButtons = new();

        // Item selection UI
        private GameObject _itemSelectionPanel;
        private List<Button> _itemButtons = new();

        // Target selection UI
        private GameObject _targetSelectionPanel;
        private List<Button> _targetButtons = new();

        // HP/MP bars
        private Dictionary<ElementPosition, Slider> _hpBars = new();
        private Dictionary<ElementPosition, Slider> _mpBars = new();
        private Dictionary<ElementPosition, TextMeshProUGUI> _hpTexts = new();
        private Dictionary<ElementPosition, TextMeshProUGUI> _mpTexts = new();

        // Damage popup
        private GameObject _damagePopupPrefab;
        private List<GameObject> _activeDamagePopups = new();

        // State
        private bool _isWaitingForAction;
        private bool _isWaitingForTarget;
        private bool _isWaitingForSkill;
        private bool _isWaitingForItem;
        private CombatActionType _selectedActionType;
        private CombatActionSelection _currentSelection;
        private Action<CombatActionSelection> _onActionSelected;
        private CombatActorController _currentActorController;
        private SkillInfo _selectedSkill;
        private GameItemInfo _selectedItem;

        public CombatUIManager(
            GameResourceProvider resourceProvider,
            CombatScene combatScene,
            Canvas combatUICanvas,
            EventSystem eventSystem,
            SkillManager skillManager,
            CombatItemManager combatItemManager)
        {
            _resourceProvider = Requires.IsNotNull(resourceProvider, nameof(resourceProvider));
            _combatScene = Requires.IsNotNull(combatScene, nameof(combatScene));
            _combatUICanvas = Requires.IsNotNull(combatUICanvas, nameof(combatUICanvas));
            _eventSystem = Requires.IsNotNull(eventSystem, nameof(eventSystem));
            _skillManager = Requires.IsNotNull(skillManager, nameof(skillManager));
            _combatItemManager = Requires.IsNotNull(combatItemManager, nameof(combatItemManager));

            CreateUIElements();
            CreateHPMPBars();
        }

        public void Dispose()
        {
            CleanupDamagePopups();
            if (_actionMenuPanel != null)
            {
                UnityEngine.Object.Destroy(_actionMenuPanel);
            }
            if (_skillSelectionPanel != null)
            {
                UnityEngine.Object.Destroy(_skillSelectionPanel);
            }
            if (_itemSelectionPanel != null)
            {
                UnityEngine.Object.Destroy(_itemSelectionPanel);
            }
            if (_targetSelectionPanel != null)
            {
                UnityEngine.Object.Destroy(_targetSelectionPanel);
            }
        }

        /// <summary>
        /// Show the action menu and wait for player selection.
        /// </summary>
        public void ShowActionMenu(Action<CombatActionSelection> onActionSelected, CombatActorController currentActor = null)
        {
            _onActionSelected = onActionSelected;
            _currentActorController = currentActor;
            _isWaitingForAction = true;
            _actionMenuPanel.SetActive(true);
            _attackButton.Select();
        }

        /// <summary>
        /// Hide the action menu.
        /// </summary>
        public void HideActionMenu()
        {
            _isWaitingForAction = false;
            _isWaitingForSkill = false;
            _isWaitingForItem = false;
            _isWaitingForTarget = false;
            _actionMenuPanel.SetActive(false);
            _skillSelectionPanel.SetActive(false);
            _itemSelectionPanel.SetActive(false);
            _targetSelectionPanel.SetActive(false);
        }

        /// <summary>
        /// Update HP/MP bars for all combat actors.
        /// </summary>
        public void UpdateActorStatus()
        {
            foreach ((ElementPosition position, CombatActorController controller) in
                     _combatScene.GetAllCombatActorControllers())
            {
                if (controller == null) continue;

                CombatActorRuntimeState state = controller.GetRuntimeState();
                if (state == null) continue;

                if (_hpBars.TryGetValue(position, out Slider hpBar))
                {
                    hpBar.value = (float)state.CurrentHp / state.MaxHp;
                }

                if (_mpBars.TryGetValue(position, out Slider mpBar))
                {
                    mpBar.value = (float)state.CurrentMp / state.MaxMp;
                }

                if (_hpTexts.TryGetValue(position, out TextMeshProUGUI hpText))
                {
                    hpText.text = $"{state.CurrentHp}/{state.MaxHp}";
                }

                if (_mpTexts.TryGetValue(position, out TextMeshProUGUI mpText))
                {
                    mpText.text = $"{state.CurrentMp}/{state.MaxMp}";
                }
            }
        }

        /// <summary>
        /// Show a damage popup at the specified world position.
        /// </summary>
        public void ShowDamagePopup(Vector3 worldPosition, int damage, bool isCritical = false)
        {
            if (_damagePopupPrefab == null) return;

            GameObject popup = UnityEngine.Object.Instantiate(_damagePopupPrefab, _combatUICanvas.transform);
            _activeDamagePopups.Add(popup);

            TextMeshProUGUI damageText = popup.GetComponentInChildren<TextMeshProUGUI>();
            if (damageText != null)
            {
                damageText.text = damage.ToString();
                damageText.color = isCritical ? Color.yellow : Color.red;
                damageText.fontSize = isCritical ? 36 : 24;
            }

            // Position the popup in screen space
            RectTransform rectTransform = popup.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPosition);
                rectTransform.position = screenPos;
            }

            // Animate the popup
            StartCoroutine(AnimateDamagePopup(popup));
        }

        /// <summary>
        /// Show a heal popup at the specified world position.
        /// </summary>
        public void ShowHealPopup(Vector3 worldPosition, int healAmount)
        {
            if (_damagePopupPrefab == null) return;

            GameObject popup = UnityEngine.Object.Instantiate(_damagePopupPrefab, _combatUICanvas.transform);
            _activeDamagePopups.Add(popup);

            TextMeshProUGUI healText = popup.GetComponentInChildren<TextMeshProUGUI>();
            if (healText != null)
            {
                healText.text = $"+{healAmount}";
                healText.color = Color.green;
            }

            RectTransform rectTransform = popup.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPosition);
                rectTransform.position = screenPos;
            }

            StartCoroutine(AnimateDamagePopup(popup));
        }

        private System.Collections.IEnumerator AnimateDamagePopup(GameObject popup)
        {
            float elapsed = 0f;
            RectTransform rectTransform = popup.GetComponent<RectTransform>();
            Vector3 startPos = rectTransform.position;

            while (elapsed < DAMAGE_POPUP_DURATION)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / DAMAGE_POPUP_DURATION;

                // Float upward
                rectTransform.position = startPos + Vector3.up * (DAMAGE_POPUP_FLOAT_SPEED * elapsed);

                // Fade out
                CanvasGroup canvasGroup = popup.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f - progress;
                }

                yield return null;
            }

            _activeDamagePopups.Remove(popup);
            UnityEngine.Object.Destroy(popup);
        }

        private void CleanupDamagePopups()
        {
            foreach (var popup in _activeDamagePopups)
            {
                if (popup != null)
                {
                    UnityEngine.Object.Destroy(popup);
                }
            }
            _activeDamagePopups.Clear();
        }

        private void CreateUIElements()
        {
            // Create action menu panel
            _actionMenuPanel = CreatePanel("ActionMenu", new Vector2(200, 300));
            _actionMenuPanel.SetActive(false);

            // Create buttons
            _attackButton = CreateButton(_actionMenuPanel.transform, "Attack", "攻击", new Vector2(0, 120));
            _skillButton = CreateButton(_actionMenuPanel.transform, "Skill", "技能", new Vector2(0, 80));
            _itemButton = CreateButton(_actionMenuPanel.transform, "Item", "物品", new Vector2(0, 40));
            _defendButton = CreateButton(_actionMenuPanel.transform, "Defend", "防御", new Vector2(0, 0));
            _fleeButton = CreateButton(_actionMenuPanel.transform, "Flee", "逃跑", new Vector2(0, -40));

            // Add button listeners
            _attackButton.onClick.AddListener(() => OnActionButtonClicked(CombatActionType.Attack));
            _skillButton.onClick.AddListener(() => OnActionButtonClicked(CombatActionType.Skill));
            _itemButton.onClick.AddListener(() => OnActionButtonClicked(CombatActionType.Item));
            _defendButton.onClick.AddListener(() => OnActionButtonClicked(CombatActionType.Defend));
            _fleeButton.onClick.AddListener(() => OnActionButtonClicked(CombatActionType.Flee));

            // Create skill selection panel
            _skillSelectionPanel = CreatePanel("SkillSelection", new Vector2(300, 400));
            _skillSelectionPanel.SetActive(false);

            // Create item selection panel
            _itemSelectionPanel = CreatePanel("ItemSelection", new Vector2(300, 400));
            _itemSelectionPanel.SetActive(false);

            // Create target selection panel
            _targetSelectionPanel = CreatePanel("TargetSelection", new Vector2(300, 200));
            _targetSelectionPanel.SetActive(false);

            // Create damage popup prefab
            CreateDamagePopupPrefab();
        }

        private void CreateHPMPBars()
        {
            foreach ((ElementPosition position, CombatActorController controller) in
                     _combatScene.GetAllCombatActorControllers())
            {
                if (controller == null) continue;

                // Create HP bar
                GameObject hpBarObj = CreateStatusBar($"HPBar_{position}", Color.red);
                Slider hpBar = hpBarObj.GetComponent<Slider>();
                _hpBars[position] = hpBar;

                // Create HP text
                TextMeshProUGUI hpText = hpBarObj.GetComponentInChildren<TextMeshProUGUI>();
                _hpTexts[position] = hpText;

                // Create MP bar
                GameObject mpBarObj = CreateStatusBar($"MPBar_{position}", Color.blue);
                Slider mpBar = mpBarObj.GetComponent<Slider>();
                _mpBars[position] = mpBar;

                // Create MP text
                TextMeshProUGUI mpText = mpBarObj.GetComponentInChildren<TextMeshProUGUI>();
                _mpTexts[position] = mpText;

                // Position the bars based on element position
                PositionStatusBars(position, hpBarObj, mpBarObj);
            }
        }

        private GameObject CreatePanel(string name, Vector2 size)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(_combatUICanvas.transform, false);

            RectTransform rectTransform = panel.AddComponent<RectTransform>();
            rectTransform.sizeDelta = size;

            Image image = panel.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.8f);

            // Add vertical layout group
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return panel;
        }

        private Button CreateButton(Transform parent, string name, string text, Vector2 position)
        {
            GameObject buttonObj = new GameObject(name);
            buttonObj.transform.SetParent(parent, false);

            RectTransform rectTransform = buttonObj.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(180, 30);
            rectTransform.anchoredPosition = position;

            Image image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            Button button = buttonObj.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            colors.pressedColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            button.colors = colors;

            // Add text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI textComponent = textObj.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.fontSize = 18;
            textComponent.alignment = TextAlignmentOptions.Center;
            textComponent.color = Color.white;

            return button;
        }

        private GameObject CreateStatusBar(string name, Color barColor)
        {
            GameObject statusBar = new GameObject(name);
            statusBar.transform.SetParent(_combatUICanvas.transform, false);

            RectTransform rectTransform = statusBar.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(100, 20);

            // Background
            GameObject background = new GameObject("Background");
            background.transform.SetParent(statusBar.transform, false);

            RectTransform bgRect = background.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            Image bgImage = background.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            // Fill area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(statusBar.transform, false);

            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.sizeDelta = Vector2.zero;

            // Fill
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);

            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;

            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = barColor;

            // Slider
            Slider slider = statusBar.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            slider.interactable = false;

            // Text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(statusBar.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI textComponent = textObj.AddComponent<TextMeshProUGUI>();
            textComponent.text = "100/100";
            textComponent.fontSize = 10;
            textComponent.alignment = TextAlignmentOptions.Center;
            textComponent.color = Color.white;

            return statusBar;
        }

        private void PositionStatusBars(ElementPosition position, GameObject hpBar, GameObject mpBar)
        {
            // Position bars based on element position
            // This is a simplified positioning - you may need to adjust based on your UI layout
            Vector2 basePosition = position switch
            {
                ElementPosition.AllyWater => new Vector2(-200, -150),
                ElementPosition.AllyFire => new Vector2(-200, -180),
                ElementPosition.AllyWind => new Vector2(-200, -210),
                ElementPosition.AllyThunder => new Vector2(-200, -240),
                ElementPosition.AllyEarth => new Vector2(-200, -270),
                ElementPosition.AllyCenter => new Vector2(-200, -300),
                ElementPosition.EnemyWater => new Vector2(200, -150),
                ElementPosition.EnemyFire => new Vector2(200, -180),
                ElementPosition.EnemyWind => new Vector2(200, -210),
                ElementPosition.EnemyThunder => new Vector2(200, -240),
                ElementPosition.EnemyEarth => new Vector2(200, -270),
                ElementPosition.EnemyCenter => new Vector2(200, -300),
                _ => Vector2.zero,
            };

            RectTransform hpRect = hpBar.GetComponent<RectTransform>();
            hpRect.anchoredPosition = basePosition;

            RectTransform mpRect = mpBar.GetComponent<RectTransform>();
            mpRect.anchoredPosition = basePosition + new Vector2(0, -25);
        }

        private void CreateDamagePopupPrefab()
        {
            _damagePopupPrefab = new GameObject("DamagePopupPrefab");
            _damagePopupPrefab.SetActive(false);

            RectTransform rectTransform = _damagePopupPrefab.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(100, 40);

            CanvasGroup canvasGroup = _damagePopupPrefab.AddComponent<CanvasGroup>();

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(_damagePopupPrefab.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI textComponent = textObj.AddComponent<TextMeshProUGUI>();
            textComponent.text = "0";
            textComponent.fontSize = 24;
            textComponent.alignment = TextAlignmentOptions.Center;
            textComponent.color = Color.red;
        }

        private void OnActionButtonClicked(CombatActionType actionType)
        {
            _selectedActionType = actionType;

            if (actionType == CombatActionType.Attack)
            {
                // Show target selection for attack
                ShowTargetSelection(SelectAttackTarget);
            }
            else if (actionType == CombatActionType.Defend)
            {
                // Defend doesn't need target selection
                _currentSelection = new CombatActionSelection
                {
                    ActionType = CombatActionType.Defend,
                };
                HideActionMenu();
                _onActionSelected?.Invoke(_currentSelection);
            }
            else if (actionType == CombatActionType.Flee)
            {
                // Flee doesn't need target selection
                _currentSelection = new CombatActionSelection
                {
                    ActionType = CombatActionType.Flee,
                };
                HideActionMenu();
                _onActionSelected?.Invoke(_currentSelection);
            }
            else if (actionType == CombatActionType.Skill)
            {
                // Show skill selection UI
                ShowSkillSelection();
            }
            else if (actionType == CombatActionType.Item)
            {
                // Show item selection UI
                ShowItemSelection();
            }
        }

        private void ShowSkillSelection()
        {
            _isWaitingForSkill = true;
            _skillSelectionPanel.SetActive(true);

            // Clear existing buttons
            foreach (var button in _skillButtons)
            {
                if (button != null)
                {
                    UnityEngine.Object.Destroy(button.gameObject);
                }
            }
            _skillButtons.Clear();

            // Get available skills for current actor
            if (_currentActorController != null)
            {
                var availableSkills = _skillManager.GetAvailableSkills(_currentActorController);
                int index = 0;

                foreach (var skillInfo in availableSkills)
                {
                    string buttonName = $"{skillInfo.Name} (MP:{skillInfo.MpConsumeValue})";
                    Button skillButton = CreateButton(_skillSelectionPanel.transform,
                        $"Skill_{skillInfo.Id}", buttonName, new Vector2(0, -40 * index));

                    SkillInfo capturedSkill = skillInfo;
                    skillButton.onClick.AddListener(() =>
                    {
                        _selectedSkill = capturedSkill;
                        _isWaitingForSkill = false;
                        _skillSelectionPanel.SetActive(false);
                        ShowTargetSelection(SelectSkillTarget);
                    });

                    _skillButtons.Add(skillButton);
                    index++;
                }
            }

            // Select the first button if available
            if (_skillButtons.Count > 0)
            {
                _skillButtons[0].Select();
            }
        }

        private void ShowItemSelection()
        {
            _isWaitingForItem = true;
            _itemSelectionPanel.SetActive(true);

            // Clear existing buttons
            foreach (var button in _itemButtons)
            {
                if (button != null)
                {
                    UnityEngine.Object.Destroy(button.gameObject);
                }
            }
            _itemButtons.Clear();

            // Get usable combat items
            var usableItems = _combatItemManager.GetUsableCombatItems();
            int index = 0;

            foreach (var itemInfo in usableItems)
            {
                int itemCount = _combatItemManager.GetItemCount(itemInfo.Id);
                string buttonName = $"{itemInfo.Name} x{itemCount}";
                Button itemButton = CreateButton(_itemSelectionPanel.transform,
                    $"Item_{itemInfo.Id}", buttonName, new Vector2(0, -40 * index));

                GameItemInfo capturedItem = itemInfo;
                itemButton.onClick.AddListener(() =>
                {
                    _selectedItem = capturedItem;
                    _isWaitingForItem = false;
                    _itemSelectionPanel.SetActive(false);
                    ShowTargetSelection(SelectItemTarget);
                });

                _itemButtons.Add(itemButton);
                index++;
            }

            // Select the first button if available
            if (_itemButtons.Count > 0)
            {
                _itemButtons[0].Select();
            }
        }

        private void ShowTargetSelection(Action<CombatActorController> onTargetSelected)
        {
            _isWaitingForTarget = true;
            _targetSelectionPanel.SetActive(true);

            // Clear existing buttons
            foreach (var button in _targetButtons)
            {
                if (button != null)
                {
                    UnityEngine.Object.Destroy(button.gameObject);
                }
            }
            _targetButtons.Clear();

            // Create buttons for each enemy
            int index = 0;
            foreach ((ElementPosition position, CombatActorController controller) in
                     _combatScene.GetAllCombatActorControllers())
            {
                if (controller == null || controller.IsDefeated) continue;

                // Only show enemies for attack targets
                if (_selectedActionType == CombatActionType.Attack && DamageCalculator.IsAlly(position))
                    continue;

                CombatActor actor = controller.GetActor();
                string buttonName = actor.Info.Name;
                if (string.IsNullOrEmpty(buttonName))
                {
                    buttonName = $"敌人 {index + 1}";
                }

                Button targetButton = CreateButton(_targetSelectionPanel.transform,
                    $"Target_{position}", buttonName, new Vector2(0, -40 * index));

                CombatActorController capturedController = controller;
                targetButton.onClick.AddListener(() =>
                {
                    _isWaitingForTarget = false;
                    _targetSelectionPanel.SetActive(false);
                    onTargetSelected?.Invoke(capturedController);
                });

                _targetButtons.Add(targetButton);
                index++;
            }

            // Select the first button if available
            if (_targetButtons.Count > 0)
            {
                _targetButtons[0].Select();
            }
        }

        private void SelectAttackTarget(CombatActorController target)
        {
            _currentSelection = new CombatActionSelection
            {
                ActionType = CombatActionType.Attack,
                Target = target,
            };
            HideActionMenu();
            _onActionSelected?.Invoke(_currentSelection);
        }

        private void SelectSkillTarget(CombatActorController target)
        {
            _currentSelection = new CombatActionSelection
            {
                ActionType = CombatActionType.Skill,
                Target = target,
                SkillId = _selectedSkill?.Id ?? 0,
            };
            HideActionMenu();
            _onActionSelected?.Invoke(_currentSelection);
        }

        private void SelectItemTarget(CombatActorController target)
        {
            _currentSelection = new CombatActionSelection
            {
                ActionType = CombatActionType.Item,
                Target = target,
                ItemId = _selectedItem?.Id ?? 0,
            };
            HideActionMenu();
            _onActionSelected?.Invoke(_currentSelection);
        }
    }
}