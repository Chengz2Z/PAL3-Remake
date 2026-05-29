// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Combat
{
    using System;
    using System.Collections.Generic;
    using Camera;
    using Command;
    using Command.Extensions;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Contract.Constants;
    using Core.Contract.Enums;
    using Core.DataReader.Gdb;
    using Core.DataReader.Ini;
    using Core.Primitives;
    using Core.Utilities;
    using Data;
    using Engine.Core.Abstraction;
    using Engine.Extensions;
    using Game.Scene;
    using Scene;
    using State;
    using Team;
    using UI;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.InputSystem;

    using Quaternion = UnityEngine.Quaternion;
    using Vector3 = UnityEngine.Vector3;

    public sealed class CombatResult
    {
        public bool IsPlayerWin { get; set; }
        public CombatContext CombatContext { get; set; }
    }

    public sealed class CombatManager
    {
        public event EventHandler<CombatResult> OnCombatFinished;

        private const string COMBAT_CAMERA_CONFIG_FILE_NAME = "cbCam.ini";
        private const float COMBAT_CAMERA_DEFAULT_FOV = 39f;

        private readonly TeamManager _teamManager;
        private readonly SceneManager _sceneManager;
        private readonly CameraManager _cameraManager;
        private readonly GameStateManager _gameStateManager;
        private readonly EventSystem _eventSystem;

        private readonly IDictionary<int, CombatActorInfo> _combatActorInfos;
        private readonly CombatCameraConfigFile _combatCameraConfigFile;

        private Vector3 _cameraPositionBeforeCombat;
        private Quaternion _cameraRotationBeforeCombat;
        private float _cameraFovBeforeCombat;

        private CombatScene _combatScene;
        private CombatContext _currentCombatContext;
        private CombatTurnSystem _turnSystem;
        private CombatUIManager _combatUIManager;
        private Canvas _combatUICanvas;
        private bool _resultDispatched;
        private SkillManager _skillManager;
        private CombatItemManager _combatItemManager;

        public CombatManager(GameResourceProvider resourceProvider,
            TeamManager teamManager,
            CameraManager cameraManager,
            SceneManager sceneManager,
            EventSystem eventSystem)
        {
            Requires.IsNotNull(resourceProvider, nameof(resourceProvider));
            _teamManager = Requires.IsNotNull(teamManager, nameof(teamManager));
            _cameraManager = Requires.IsNotNull(cameraManager, nameof(cameraManager));
            _sceneManager = Requires.IsNotNull(sceneManager, nameof(sceneManager));
            _eventSystem = Requires.IsNotNull(eventSystem, nameof(eventSystem));

            _combatActorInfos = resourceProvider.GetCombatActorInfos();
            _combatCameraConfigFile = resourceProvider.GetGameResourceFile<CombatCameraConfigFile>(
                FileConstants.DataScriptFolderVirtualPath + COMBAT_CAMERA_CONFIG_FILE_NAME);

            // Initialize skill and item managers
            _skillManager = new SkillManager(resourceProvider);
            _combatItemManager = new CombatItemManager(resourceProvider,
                Pal3.Instance.GetService<InventoryManager>());
        }

        public void EnterCombat(CombatContext combatContext)
        {
            _currentCombatContext = combatContext;

            _cameraManager.GetCameraTransform().GetPositionAndRotation(out _cameraPositionBeforeCombat,
                out _cameraRotationBeforeCombat);

            _cameraFovBeforeCombat = _cameraManager.GetFieldOfView();

            if (!string.IsNullOrEmpty(combatContext.CombatMusicName))
            {
                Pal3.Instance.Execute(new PlayScriptMusicCommand(combatContext.CombatMusicName, -1));
            }

            _combatScene = _sceneManager.LoadCombatScene(combatContext.CombatSceneName);

            // Create combat UI first
            CreateCombatUI();

            Dictionary<ElementPosition, CombatActorInfo> combatActors = new ();

            int positionIndex = 0;
            foreach (PlayerActorId playerActorId in _teamManager.GetActorsInTeam())
            {
                int combatActorId = ActorConstants.MainActorCombatActorIdMap[playerActorId];
                combatActors[(ElementPosition)positionIndex++] = _combatActorInfos[combatActorId];
            }

            for (int i = 0; i < combatContext.EnemyIds.Length; i++)
            {
                uint enemyActorId = combatContext.EnemyIds[i];
                if (enemyActorId == 0) continue;
                combatActors[(ElementPosition)((int)ElementPosition.EnemyWater + i)] =
                    _combatActorInfos[(int)enemyActorId];
            }

            _combatScene.LoadActors(combatActors, combatContext.MeetType, _combatUIManager);

            SetCameraPosition(_combatCameraConfigFile.DefaultCamConfigs[0]);

            Pal3.Instance.Execute(new CameraFadeInCommand());

            if (combatContext.MeetType != MeetType.RunningIntoEachOther)
            {
                Pal3.Instance.Execute(new UIDisplayNoteCommand(
                    combatContext.MeetType == MeetType.PlayerChasingEnemy
                        ? "偷袭敌方成功！"
                        : "被敌人偷袭！"));
            }

            _turnSystem = new CombatTurnSystem(_combatScene,
                routine => Pal3.Instance.StartCoroutine(routine),
                _combatUIManager,
                _skillManager,
                _combatItemManager,
                Pal3.Instance.GetService<GameResourceProvider>());
            _turnSystem.Begin();
            _resultDispatched = false;
        }

        public void ExitCombat()
        {
            // Stop combat music
            Pal3.Instance.Execute(new StopScriptMusicCommand());
            Pal3.Instance.Execute(new CameraFadeInCommand());
            _sceneManager.UnloadCombatScene();
            ResetCameraPosition();

            // Cleanup combat UI
            CleanupCombatUI();

            _turnSystem = null;
            _combatScene = null;
        }

        private void CreateCombatUI()
        {
            // Create a canvas for combat UI
            GameObject canvasObj = new GameObject("CombatUI");
            _combatUICanvas = canvasObj.AddComponent<Canvas>();
            _combatUICanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _combatUICanvas.sortingOrder = 100; // Ensure it's on top

            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // Create combat UI manager
            _combatUIManager = new CombatUIManager(
                Pal3.Instance.GetService<GameResourceProvider>(),
                _combatScene,
                _combatUICanvas,
                _eventSystem,
                _skillManager,
                _combatItemManager);
        }

        private void CleanupCombatUI()
        {
            if (_combatUIManager != null)
            {
                _combatUIManager.Dispose();
                _combatUIManager = null;
            }

            if (_combatUICanvas != null)
            {
                UnityEngine.Object.Destroy(_combatUICanvas.gameObject);
                _combatUICanvas = null;
            }
        }

        private void SetCameraPosition(CombatCameraConfig config)
        {
            Vector3 cameraPosition = new GameBoxVector3(
                    config.GameBoxPositionX,
                    config.GameBoxPositionY,
                    config.GameBoxPositionZ).ToUnityPosition();

            ITransform cameraTransform = _cameraManager.GetCameraTransform();
            cameraTransform.Position = cameraPosition;
            // cameraTransform.Rotation = UnityPrimitivesConvertor.ToUnityQuaternion(
            //     config.Pitch, config.Yaw, config.Roll);
            cameraTransform.LookAt(Vector3.zero);

            Pal3.Instance.Execute(new CameraSetFieldOfViewCommand(COMBAT_CAMERA_DEFAULT_FOV));
        }

        private void ResetCameraPosition()
        {
            _cameraManager.GetCameraTransform().SetPositionAndRotation(_cameraPositionBeforeCombat,
                _cameraRotationBeforeCombat);

            Pal3.Instance.Execute(new CameraSetFieldOfViewCommand(_cameraFovBeforeCombat));
        }

        public void Update(float deltaTime)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                FinishCombat(playerWin: true);
                return;
            }

            if (_turnSystem == null || _resultDispatched) return;

            _turnSystem.Tick();

            // Update UI
            if (_combatUIManager != null)
            {
                _combatUIManager.UpdateActorStatus();
            }

            if (_turnSystem.IsFinished)
            {
                FinishCombat(_turnSystem.IsPlayerWin);
            }
        }

        private void FinishCombat(bool playerWin)
        {
            if (_resultDispatched) return;
            _resultDispatched = true;

            OnCombatFinished?.Invoke(this, new CombatResult
            {
                IsPlayerWin = playerWin,
                CombatContext = _currentCombatContext,
            });
        }
    }
}