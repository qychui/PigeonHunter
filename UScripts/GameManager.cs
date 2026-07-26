using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.Core;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GameManager : UdonSharpBehaviour
    {
        public GameObject TitleScreenBGM;

        [Header("Round Settings")]
        public int pigeonsPerRound = 10;
        public float spawnDelay = 4f;

        [Header("Pair Mode")]
        [Min(1)]
        [Tooltip("Number of paired-pigeon waves generated per Mode2 round. Each wave always spawns 2 pigeons.")]
        public int pairModeWaveCount = 5;
        [Min(0f)]
        [Tooltip("Minimum launch delay for the second pigeon in each Mode2 paired wave.")]
        public float pairModeLaunchDelayMin = 0.05f;
        [Min(0f)]
        [Tooltip("Maximum launch delay for the second pigeon in each Mode2 paired wave.")]
        public float pairModeLaunchDelayMax = 1.45f;

        [Header("Game Mode")]
        [Tooltip("1 = Single mode, 2 = Pair mode, 3 = Training mode")]
        [Range(1, 3)]
        public int gameMode = 1;

        [Header("Main Menu")]
        [Min(0f)]
        public float modeConfirmDelay = 0.5f;

        [Header("Difficulty")]
        public float roundDifficultyStep = 0.25f;
        public float maxDifficultyMultiplier = 4f;
        [Min(0f)]
        [Tooltip("Seconds subtracted from each target's escapeTriggerTime per completed Mode1/Mode2 round.")]
        public float pigeonEscapeTriggerReductionStep = 0.2f;
        [Min(1)]
        [Tooltip("Last round where escapeTriggerTime reduction is applied. Later rounds keep the same reduction.")]
        public int pigeonEscapeTriggerReductionMaxRound = 15;
        [Min(0f)]
        [Tooltip("Minimum runtime escapeTriggerTime allowed for each target.")]
        public float pigeonEscapeTriggerMinimum = 4f;

        [Header("Pigeon Boundary Reflection")]
        [Range(0f, 1f)]
        [Tooltip("Base chance for random deflection when a pigeon hits a boundary on round 1.")]
        public float pigeonBoundaryRandomDeflectionChanceBase = 0f;
        [Min(0f)]
        [Tooltip("Chance added per round for random deflection when a pigeon hits a boundary.")]
        public float pigeonBoundaryRandomDeflectionChanceStepPerRound = 0.03f;
        [Range(0f, 1f)]
        [Tooltip("Maximum chance for random deflection when a pigeon hits a boundary.")]
        public float pigeonBoundaryRandomDeflectionChanceMax = 0.45f;

        [Header("Shot Reaction")]
        [Tooltip("Chance per shot for each flying, unhit pigeon to change direction.")]
        [Range(0f, 1f)]
        public float shotDirectionChangeChance = 0.7f;

        [Header("Play Area")]
        public RectTransform playArea;
        public RectTransform shootingRangeMoveArea;
        [Tooltip("World-space inset applied at both ends of the bottom edge spawn segment.")]
        [FormerlySerializedAs("bottomEdgePadding")]
        public float bottomEdgeSpawnSegment = 0.25f;

        [Header("Shooting Range Spawn")]
        [Tooltip("World-space inset applied to the left and right side of the shooting range bottom edge.")]
        public float shootingRangeBottomSpawnSegment = 0.2f;
        [Tooltip("Minimum launch angle in degrees from world right/up mirrored by side.")]
        public float shootingRangeMinLaunchAngle = 58f;
        [Tooltip("Maximum launch angle in degrees from world right/up mirrored by side.")]
        public float shootingRangeMaxLaunchAngle = 74f;
        [Tooltip("Normalized speed in relation to the shooting range move area width.")]
        public float shootingRangeTravelSpeedPerSecond = 0.3f;
        [Tooltip("Peak height relative to the shooting range area height.")]
        public Vector2 shootingRangePeakHeightRange = new Vector2(0.76f, 0.88f);
        [Tooltip("Height relative to the shooting range area height where all clay visuals should hide.")]
        public Vector2 shootingRangeEndHeightRange = new Vector2(0.38f, 0.38f);
        [Tooltip("Maximum clay lifetime at round 1.")]
        public float shootingRangeClayLifetimeMax = 6f;
        [Tooltip("Minimum clay lifetime after difficulty ramps up.")]
        public float shootingRangeClayLifetimeMin = 3f;
        [Tooltip("Clay lifetime reduction applied after each completed shooting range round.")]
        public float shootingRangeClayLifetimeStepPerRound = 0.2f;
        [Tooltip("Optional delay after visuals hide before the clay target is recycled.")]
        public float shootingRangeHideRecycleDelay = 0f;

        [Header("Shooting Range Session")]
        [Min(1)]
        public int shootingRangeWaveCount = 5;
        [Min(0f)]
        public float shootingRangePairLaunchDelayMin = 0.05f;
        [Min(0f)]
        public float shootingRangePairLaunchDelayMax = 2.45f;
        [Min(0f)]
        public float shootingRangeWaveDelay = 0.75f;

        [Header("References")]
        public PigeonTarget[] pigeonPool;
        public ClayTarget[] clayPigeonPool;
        public UIController uiController;

        [Header("Animation")]
        public MainAreaAnimationController animationController;

        [Header("Audio")]
        public SoundManager soundManager;

        [Header("Controller")]
        public ActionController actionController;

        [Header("SyncController")]
        public SyncController syncController;

        [Header("Gun Respawn")]
        public GameObject gunObject;
        public Transform gunRespawnPoint;

        [Header("Gun Shot Flash")]
        public bool enableGunShotFlashObjects;
        public GameObject[] gunShotFlashObjects;
        [Min(0f)]
        public float gunShotFlashDuration = 0.1f;

        [Header("ScreenRetroTV Mask")]
        public GameObject screenRetroTvMask;

        private const float DefaultDifficulty = 1f;
        private bool gunShotFlashActive;
        private float gunShotFlashTimer;
        private bool exitAnimationActive;
        private int exitAnimationPending;
        private bool pendingModeStart;
        private int pendingModeIndex = -1;
        private float pendingModeStartTimer;
        private bool shootingRangeSessionActive;
        private int shootingRangeLaunchedThisWave;
        private int shootingRangeResolvedThisWave;
        private bool shootingRangeWaveShotWindowActive;
        private int shootingRangeWaveShotsUsed;
        private bool shootingRangeSecondClayPending;
        private float shootingRangeSecondClayTimer;
        private bool shootingRangeNextWavePending;
        private float shootingRangeNextWaveTimer;
        private bool[] shootingRangeClayHitStates;
        private int[] shootingRangeClayUiIndices;
        private int shootingRangeRoundsCompleted;
        private int shootingRangeHitsThisRound;
        private bool shootingRangeRoundEndPending;
        private bool shootingRangeRoundPerfect;
        private bool shootingRangeRoundPassed;
        private bool shootingRangeRoundEndUiTriggered;
        private bool shootingRangeRoundEndAudioTriggered;
        private bool shootingRangeRoundRestartPending;
        private bool shootingRangeGameOver;
        private int shootingRangeRoundWaveCursor;
        private int pendingSyncedPigeonHitPoolIndex = -1;
        private int pendingSyncedPigeonHitUsedShots;
        private int pendingSyncedPigeonHitRound;
        private float pendingSyncedPigeonHitTimer;
        private bool mode3OwnerWaveStartInProgress;
        private bool mode3SyncedWaveStartInProgress;
        private int mode3WaveSeed;
        private bool mode3WaveSeedActive;
        private int pendingSyncedClayHitPoolIndex = -1;
        private int pendingSyncedClayHitUsedShots;
        private int pendingSyncedClayHitRound;
        private float pendingSyncedClayHitTimer;
        private bool mode3RoundSnapshotPendingForJoiner;
        private int handledMode3RoundSnapshotRound;
        private Vector3 gunInitialPosition;
        private Quaternion gunInitialRotation;
        private bool gunInitialTransformCached;
        private const float PendingSyncedPigeonHitTimeout = 3f;
        private const float PendingSyncedClayHitTimeout = 3f;
        private const int ShootingRangeMaxDifficultyLevel = 4;
        private readonly Vector3[] shootingRangeCorners = new Vector3[4];

        public bool RoundActive => actionController != null && actionController.RoundActive;
        public float DifficultyMultiplier => actionController != null ? actionController.DifficultyMultiplier : DefaultDifficulty;
        public float CurrentPigeonBoundaryRandomDeflectionChance => GetPigeonBoundaryRandomDeflectionChanceForRound(actionController != null ? actionController.CurrentRoundNumber : 1);

        public void TransferGameplayOwnershipToLocalPlayer(GameObject pickedUpObject)
        {
            if (Networking.LocalPlayer == null)
            {
                return;
            }

            TransferOwnerIfNeeded(pickedUpObject);

            if (syncController != null)
            {
                syncController.TransferOwnershipToLocalPlayer();
            }

            if (uiController != null)
            {
                TransferOwnerIfNeeded(uiController.gameObject);
            }
        }

        private void TransferOwnerIfNeeded(GameObject target)
        {
            if (target == null || Networking.IsOwner(target))
            {
                return;
            }

            Networking.SetOwner(Networking.LocalPlayer, target);
        }

        public bool IsLocalGameplayOwner()
        {
            return syncController == null || syncController.IsLocalOwner();
        }

        public void SyncRespawnGun()
        {
            ApplyRespawnGun(true);
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkRespawnGun));
        }

        public void NetworkRespawnGun()
        {
            ApplyRespawnGun(false);
        }

        public void SyncGunShotFlashObjects()
        {
            if (!enableGunShotFlashObjects)
            {
                return;
            }

            NetworkShowGunShotFlashObjects();
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkShowGunShotFlashObjects));
        }

        public void NetworkShowGunShotFlashObjects()
        {
            if (!enableGunShotFlashObjects)
            {
                return;
            }

            SetGunShotFlashObjectsActive(true);
            gunShotFlashActive = true;
            gunShotFlashTimer = Mathf.Max(0f, gunShotFlashDuration);

            if (gunShotFlashTimer <= 0f)
            {
                HideGunShotFlashObjects();
            }
        }

        private void Start()
        {
            CacheGunInitialTransform();
            BindSyncControllerReceivers();

            if (actionController != null)
            {
                actionController.Initialize(this);
            }
            else
            {
                Debug.LogWarning("[GameManager] Missing ActionController reference.");
            }

            if (uiController != null)
            {
                ShowTitleScreenScene();
            }
        }

        private void BindSyncControllerReceivers()
        {
            if (syncController == null || uiController == null)
            {
                return;
            }

            if (syncController.syncedIndexChangedReceiver == null)
            {
                syncController.syncedIndexChangedReceiver = this;
                syncController.syncedIndexChangedEventName = nameof(ApplySyncedModeSelection);
            }

            if (syncController.syncedStartReceiver == null)
            {
                syncController.syncedStartReceiver = this;
                syncController.syncedStartEventName = nameof(ApplySyncedModeStart);
            }

            if (syncController.mode1RoundPlanReceiver == null)
            {
                syncController.mode1RoundPlanReceiver = this;
                syncController.mode1RoundPlanEventName = nameof(ApplySyncedMode1RoundPlan);
            }

            if (syncController.mode1HitReceiver == null)
            {
                syncController.mode1HitReceiver = this;
                syncController.mode1HitEventName = nameof(ApplySyncedMode1PigeonHit);
            }

            if (syncController.mode1ShotReceiver == null)
            {
                syncController.mode1ShotReceiver = this;
                syncController.mode1ShotEventName = nameof(ApplySyncedMode1ShotMiss);
            }

            if (syncController.mode1RoundResultReceiver == null)
            {
                syncController.mode1RoundResultReceiver = this;
                syncController.mode1RoundResultEventName = nameof(ApplySyncedMode1RoundResult);
            }

            if (syncController.mode3WaveStartReceiver == null)
            {
                syncController.mode3WaveStartReceiver = this;
                syncController.mode3WaveStartEventName = nameof(ApplySyncedMode3WaveStart);
            }

            if (syncController.mode3HitReceiver == null)
            {
                syncController.mode3HitReceiver = this;
                syncController.mode3HitEventName = nameof(ApplySyncedMode3ClayHit);
            }

            if (syncController.mode3ShotReceiver == null)
            {
                syncController.mode3ShotReceiver = this;
                syncController.mode3ShotEventName = nameof(ApplySyncedMode3ShotMiss);
            }

            if (syncController.mode3RoundSnapshotReceiver == null)
            {
                syncController.mode3RoundSnapshotReceiver = this;
                syncController.mode3RoundSnapshotEventName = nameof(ApplySyncedMode3RoundSnapshot);
            }

            if (syncController.gunShotFlashReceiver == null)
            {
                syncController.gunShotFlashReceiver = this;
                syncController.gunShotFlashEventName = nameof(NetworkShowGunShotFlashObjects);
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (player == null || player.isLocal)
            {
                return;
            }

            if (IsLocalGameplayOwner() && IsShootingRangeModeWithSync() && shootingRangeSessionActive && !shootingRangeGameOver)
            {
                mode3RoundSnapshotPendingForJoiner = true;
            }
        }

        public override void Interact()
        {
            if (actionController == null)
            {
                return;
            }

            if (TryRestartOnGameOver())
            {
                return;
            }

            if (uiController != null)
            {
                uiController.SetTitleScreenActive(false);
            }

            actionController.HandleInteract();
        }

        private void Update()
        {
            TickGunShotFlashObjects();
            TickPendingModeStart();
            TickShootingRangeSession();
            TickShootingRangeRoundEnd();

            if (actionController != null)
            {
                actionController.Tick();
            }

            TickPendingSyncedPigeonHit();
            TickPendingSyncedClayHit();
        }

        public void BeginRound()
        {
            if (actionController != null)
            {
                actionController.BeginRound();
            }
        }

        public void EndRound()
        {
            if (actionController != null)
            {
                actionController.EndRound();
            }
        }

        public void ForcePassCurrentRound()
        {
            if (TryForceSettleShootingRangeRound(true))
            {
                return;
            }

            if (actionController != null)
            {
                actionController.ForcePassCurrentRound();
            }
        }

        public void ForceSettleCurrentRound()
        {
            if (TryForceSettleShootingRangeRound(false))
            {
                return;
            }

            if (actionController != null)
            {
                actionController.ForceSettleCurrentRound();
            }
        }

        public float GetPigeonBoundaryRandomDeflectionChanceForRound(int roundNumber)
        {
            var baseValue = Mathf.Clamp01(pigeonBoundaryRandomDeflectionChanceBase);
            var maxValue = Mathf.Clamp01(Mathf.Max(baseValue, pigeonBoundaryRandomDeflectionChanceMax));
            var stepValue = Mathf.Max(0f, pigeonBoundaryRandomDeflectionChanceStepPerRound);
            return CalculateRoundScaledValue(roundNumber, baseValue, stepValue, maxValue);
        }

        private float CalculateRoundScaledValue(int roundNumber, float baseValue, float stepPerRound, float maxValue)
        {
            roundNumber = Mathf.Max(1, roundNumber);
            var value = baseValue + ((roundNumber - 1) * stepPerRound);
            return Mathf.Min(value, maxValue);
        }

        public void ResetAndRestartGame()
        {
            exitAnimationActive = false;
            exitAnimationPending = 0;
            shootingRangeGameOver = false;
            CancelPendingModeStart();
            ResetShootingRangeSessionState();

            if (soundManager != null)
            {
                soundManager.StopAll();
            }

            SetFlyAwayUiActive(false);
            DespawnAllClayTargets();

            if (uiController != null)
            {
                uiController.SetGameOverActive(false);
                uiController.ClearActivePigeonMask();
            }

            if (animationController != null)
            {
                animationController.ResetToIdle();
            }

            if (actionController != null)
            {
                actionController.Initialize(this);
            }

            if (gameMode == 3)
            {
                if (uiController != null)
                {
                    uiController.ClearClayTargetHitIndicators();
                    uiController.ClearPerfectDisplay();
                    uiController.SetGoodActive(false);
                    ShowShootingRangeScene();
                }

                StartShootingRangeDemo();
                return;
            }

            if (uiController != null)
            {
                ShowModeABScene();
            }

            if (actionController != null)
            {
                actionController.BeginRound();
                actionController.DebugTriggerStartAnimation();
            }
        }

        public bool TryHandleModeOptionHit(Collider hitCollider)
        {
            if (!IsAwaitingMainMenuModeSelection() || uiController == null)
            {
                return false;
            }

            return uiController.TryHandleModeOptionHit(hitCollider, this);
        }

        public void HandleConfirmedModeSelection(int modeIndex)
        {
            if (syncController != null)
            {
                syncController.SyncStartIndex(modeIndex);
                return;
            }

            StartConfirmedMode(modeIndex);
        }

        public void HandleModeSelectionChanged(int modeIndex)
        {
            if (syncController != null)
            {
                syncController.SetSyncedIndex(modeIndex);
            }

            if (uiController != null)
            {
                uiController.SetSyncedModeSelectionIndex(modeIndex);
            }
        }

        public void ApplySyncedModeSelection()
        {
            if (syncController == null || uiController == null)
            {
                return;
            }

            var index = syncController.GetSyncedIndex();
            if (index >= 0)
            {
                uiController.SetSyncedModeSelectionIndex(index);
            }
        }

        public void ApplySyncedModeStart()
        {
            if (syncController == null)
            {
                return;
            }

            var modeIndex = syncController.GetSyncedStartIndex();
            if (modeIndex < 0)
            {
                return;
            }

            if (uiController != null)
            {
                uiController.SetSyncedModeSelectionIndex(modeIndex);
            }

            StartConfirmedMode(modeIndex);
        }

        public void SyncStartModeA()
        {
            HandleConfirmedModeSelection(0);
        }

        public void SyncStartModeB()
        {
            HandleConfirmedModeSelection(1);
        }

        public void SyncStartModeC()
        {
            HandleConfirmedModeSelection(2);
        }

        public void ApplySyncedMode1RoundPlan()
        {
            if (syncController == null || actionController == null)
            {
                return;
            }

            actionController.ApplyMode1RoundPlan(syncController.GetMode1RoundNumber(), syncController.GetMode1RoundSeed());
        }

        public void ApplySyncedMode3WaveStart()
        {
            if (syncController == null)
            {
                return;
            }

            StartSyncedShootingRangeWave(
                syncController.GetMode3WaveRoundNumber(),
                syncController.GetMode3WaveIndex(),
                syncController.GetMode3WaveSeed());
        }

        public void ApplySyncedMode3RoundSnapshot()
        {
            if (syncController == null || !syncController.GetMode3SnapshotActive())
            {
                return;
            }

            ApplyMode3RoundSnapshot(
                syncController.GetMode3SnapshotRoundNumber(),
                syncController.GetMode3SnapshotScore(),
                syncController.GetMode3SnapshotDifficulty());
        }

        private bool TryRestartOnGameOver()
        {
            var normalModeGameOver = actionController != null && actionController.IsGameOver;
            if (!normalModeGameOver && !shootingRangeGameOver)
            {
                return false;
            }

            ResetAndRestartGame();
            return true;
        }

        private bool IsAwaitingMainMenuModeSelection()
        {
            if (uiController == null || actionController == null)
            {
                return false;
            }

            if (!uiController.IsTitleScreenVisible())
            {
                return false;
            }

            if (actionController.RoundActive)
            {
                return false;
            }

            return !actionController.HasStartedGameSession || actionController.IsGameOver || shootingRangeGameOver;
        }

        private void StartModeA()
        {
            gameMode = 1;
            CancelPendingModeStart();
            ResetShootingRangeSessionState();
            DespawnAllClayTargets();

            if (uiController != null)
            {
                uiController.SetGameOverActive(false);
                uiController.ClearClayTargetHitIndicators();
                uiController.ClearPerfectDisplay();
                uiController.SetGoodActive(false);
                uiController.ClearActivePigeonMask();
                ShowModeABScene();
            }

            if (actionController != null)
            {
                actionController.Initialize(this);
                actionController.BeginRound();
            }
        }

        private void StartModeB()
        {
            gameMode = 2;
            CancelPendingModeStart();
            ResetShootingRangeSessionState();
            DespawnAllClayTargets();

            if (uiController != null)
            {
                uiController.SetGameOverActive(false);
                uiController.ClearClayTargetHitIndicators();
                uiController.ClearPerfectDisplay();
                uiController.SetGoodActive(false);
                uiController.ClearActivePigeonMask();
                ShowModeABScene();
            }

            if (actionController != null)
            {
                actionController.Initialize(this);
                actionController.BeginRound();
            }
        }

        private void StartModeC()
        {
            gameMode = 3;
            CancelPendingModeStart();

            if (actionController != null)
            {
                actionController.Initialize(this);
            }

            if (uiController != null)
            {
                uiController.SetGameOverActive(false);
                uiController.ClearClayTargetHitIndicators();
                uiController.ClearPerfectDisplay();
                uiController.SetGoodActive(false);
                uiController.ClearActivePigeonMask();
                ShowShootingRangeScene();
            }

            StartShootingRangeDemo();
        }

        private void ScheduleModeStart(int modeIndex)
        {
            pendingModeStart = true;
            pendingModeIndex = modeIndex;
            pendingModeStartTimer = Mathf.Max(0f, modeConfirmDelay);
        }

        private void CancelPendingModeStart()
        {
            pendingModeStart = false;
            pendingModeIndex = -1;
            pendingModeStartTimer = 0f;
        }

        private void TickPendingModeStart()
        {
            if (!pendingModeStart)
            {
                return;
            }

            if (!IsAwaitingMainMenuModeSelection())
            {
                CancelPendingModeStart();
                return;
            }

            if (pendingModeStartTimer > 0f)
            {
                pendingModeStartTimer -= Time.deltaTime;
                if (pendingModeStartTimer > 0f)
                {
                    return;
                }
            }

            var modeIndex = pendingModeIndex;
            CancelPendingModeStart();
            StartConfirmedMode(modeIndex);
        }

        private void StartConfirmedMode(int modeIndex)
        {
            switch (modeIndex)
            {
                case 0:
                    StartModeA();
                    break;
                case 1:
                    StartModeB();
                    break;
                case 2:
                    StartModeC();
                    break;
            }
        }

        public void RegisterPigeonHit(PigeonTarget pigeon)
        {
            SyncMode1PigeonHit(pigeon);

            if (actionController != null)
            {
                actionController.RegisterPigeonHit(pigeon);
            }
        }

        public void ApplySyncedMode1PigeonHit()
        {
            if (syncController == null || pigeonPool == null)
            {
                return;
            }

            var poolIndex = syncController.GetMode1HitPigeonPoolIndex();
            var usedShots = syncController.GetMode1HitUsedShots();
            if (!TryApplySyncedPigeonHit(poolIndex, usedShots))
            {
                QueueSyncedPigeonHit(poolIndex, usedShots);
            }
        }

        private bool TryApplySyncedPigeonHit(int poolIndex, int usedShots)
        {
            if (pigeonPool == null)
            {
                return false;
            }

            if (poolIndex < 0 || poolIndex >= pigeonPool.Length)
            {
                return true;
            }

            var pigeon = pigeonPool[poolIndex];
            if (pigeon == null)
            {
                return true;
            }

            if (!pigeon.CanApplySyncedHit)
            {
                return false;
            }

            if (actionController != null)
            {
                actionController.ApplySyncedShotUsage(usedShots);
                actionController.RegisterSyncedPigeonHit(pigeon, false);
            }

            pigeon.ApplySyncedHit();
            return true;
        }

        private void QueueSyncedPigeonHit(int poolIndex, int usedShots)
        {
            if (poolIndex < 0)
            {
                return;
            }

            pendingSyncedPigeonHitPoolIndex = poolIndex;
            pendingSyncedPigeonHitUsedShots = Mathf.Max(0, usedShots);
            pendingSyncedPigeonHitRound = actionController != null ? actionController.CurrentRoundNumber : 0;
            pendingSyncedPigeonHitTimer = PendingSyncedPigeonHitTimeout;
        }

        private void TickPendingSyncedPigeonHit()
        {
            if (pendingSyncedPigeonHitPoolIndex < 0)
            {
                return;
            }

            if (pendingSyncedPigeonHitTimer > 0f)
            {
                pendingSyncedPigeonHitTimer -= Time.deltaTime;
            }

            if (actionController != null &&
                pendingSyncedPigeonHitRound > 0 &&
                actionController.CurrentRoundNumber != pendingSyncedPigeonHitRound)
            {
                ClearPendingSyncedPigeonHit();
                return;
            }

            if (TryApplySyncedPigeonHit(pendingSyncedPigeonHitPoolIndex, pendingSyncedPigeonHitUsedShots) ||
                pendingSyncedPigeonHitTimer <= 0f)
            {
                ClearPendingSyncedPigeonHit();
            }
        }

        private void ClearPendingSyncedPigeonHit()
        {
            pendingSyncedPigeonHitPoolIndex = -1;
            pendingSyncedPigeonHitUsedShots = 0;
            pendingSyncedPigeonHitRound = 0;
            pendingSyncedPigeonHitTimer = 0f;
        }

        private void SyncMode1PigeonHit(PigeonTarget pigeon)
        {
            if (!CanSendPigeonModeSync() || pigeonPool == null || pigeon == null)
            {
                return;
            }

            var poolIndex = GetPigeonPoolIndex(pigeon);
            if (poolIndex < 0)
            {
                return;
            }

            var usedShots = actionController != null ? actionController.GetShotsUsedThisWave() : 0;
            syncController.SyncMode1PigeonHit(poolIndex, usedShots);
        }

        private int GetPigeonPoolIndex(PigeonTarget pigeon)
        {
            if (pigeonPool == null || pigeon == null)
            {
                return -1;
            }

            for (int i = 0; i < pigeonPool.Length; i++)
            {
                if (pigeonPool[i] == pigeon)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool TryRegisterShot()
        {
            if (TryRegisterShootingRangeShot())
            {
                return true;
            }

            return actionController != null && actionController.TryRegisterShot();
        }

        public void NotifyShotOutcome(bool hitPigeon)
        {
            if (shootingRangeSessionActive)
            {
                if (!hitPigeon)
                {
                    SyncMode3ShotMiss();
                }

                return;
            }

            if (!hitPigeon)
            {
                SyncMode1ShotMiss();
            }

            if (actionController != null)
            {
                actionController.NotifyShotOutcome(hitPigeon);
            }
        }

        public void ApplySyncedMode1ShotMiss()
        {
            if (syncController == null || actionController == null)
            {
                return;
            }

            actionController.ApplySyncedShotUsage(syncController.GetMode1ShotUsedShots());
            actionController.NotifyShotOutcome(false);
        }

        public void SyncMode1RoundResult(int roundNumber, int score, int hitCount, bool passed)
        {
            if (!CanSendPigeonModeSync())
            {
                return;
            }

            syncController.SyncMode1RoundResult(roundNumber, score, hitCount, passed);
        }

        public void ApplySyncedMode1RoundResult()
        {
            if (syncController == null || actionController == null)
            {
                return;
            }

            actionController.ApplySyncedMode1RoundResult(
                syncController.GetMode1ResultRoundNumber(),
                syncController.GetMode1ResultScore(),
                syncController.GetMode1ResultHitCount(),
                syncController.GetMode1ResultPassed());
        }

        private void SyncMode1ShotMiss()
        {
            if (!CanSendPigeonModeSync() || actionController == null)
            {
                return;
            }

            syncController.SyncMode1ShotMiss(actionController.GetShotsUsedThisWave());
        }

        private bool CanSendPigeonModeSync()
        {
            return IsNetworkedPigeonMode() &&
                   syncController != null &&
                   IsLocalGameplayOwner();
        }

        private void SyncMode3ClayHit(int clayIndex)
        {
            if (!CanSendMode3Sync())
            {
                return;
            }

            syncController.SyncMode3ClayHit(clayIndex, shootingRangeWaveShotsUsed);
        }

        private void SyncMode3ShotMiss()
        {
            if (!CanSendMode3Sync())
            {
                return;
            }

            syncController.SyncMode3ShotMiss(shootingRangeWaveShotsUsed);
        }

        private bool CanSendMode3Sync()
        {
            return IsShootingRangeModeWithSync() && IsLocalGameplayOwner();
        }

        private bool TryApplySyncedClayHit(int poolIndex, int usedShots)
        {
            if (!shootingRangeSessionActive || clayPigeonPool == null)
            {
                return false;
            }

            if (poolIndex < 0 || poolIndex >= clayPigeonPool.Length)
            {
                return true;
            }

            var clayTarget = clayPigeonPool[poolIndex];
            if (clayTarget == null)
            {
                return true;
            }

            if (!clayTarget.CanApplySyncedHit)
            {
                return false;
            }

            var uiIndex = GetClayTargetUiIndex(poolIndex);
            if (uiIndex < 0)
            {
                return false;
            }

            ApplySyncedShootingRangeShotUsage(usedShots);
            EnsureClayHitStateBuffer();
            if (shootingRangeClayHitStates != null && poolIndex < shootingRangeClayHitStates.Length)
            {
                if (shootingRangeClayHitStates[poolIndex])
                {
                    return true;
                }

                shootingRangeClayHitStates[poolIndex] = true;
            }

            shootingRangeHitsThisRound++;
            if (uiController != null)
            {
                uiController.AddScore(clayTarget.GetScoreForCurrentRound());
                uiController.SetClayTargetHitState(uiIndex, true);
            }

            RefreshActiveClayMasks();
            clayTarget.ApplySyncedHit();
            return true;
        }

        private void QueueSyncedClayHit(int poolIndex, int usedShots)
        {
            if (poolIndex < 0)
            {
                return;
            }

            pendingSyncedClayHitPoolIndex = poolIndex;
            pendingSyncedClayHitUsedShots = Mathf.Max(0, usedShots);
            pendingSyncedClayHitRound = Mathf.Max(1, shootingRangeRoundsCompleted + 1);
            pendingSyncedClayHitTimer = PendingSyncedClayHitTimeout;
        }

        private void TickPendingSyncedClayHit()
        {
            if (pendingSyncedClayHitPoolIndex < 0)
            {
                return;
            }

            if (pendingSyncedClayHitTimer > 0f)
            {
                pendingSyncedClayHitTimer -= Time.deltaTime;
            }

            if (pendingSyncedClayHitRound > 0 &&
                pendingSyncedClayHitRound != Mathf.Max(1, shootingRangeRoundsCompleted + 1))
            {
                ClearPendingSyncedClayHit();
                return;
            }

            if (TryApplySyncedClayHit(pendingSyncedClayHitPoolIndex, pendingSyncedClayHitUsedShots) ||
                pendingSyncedClayHitTimer <= 0f)
            {
                ClearPendingSyncedClayHit();
            }
        }

        private void ClearPendingSyncedClayHit()
        {
            pendingSyncedClayHitPoolIndex = -1;
            pendingSyncedClayHitUsedShots = 0;
            pendingSyncedClayHitRound = 0;
            pendingSyncedClayHitTimer = 0f;
        }

        private void ApplySyncedShootingRangeShotUsage(int usedShots)
        {
            shootingRangeWaveShotsUsed = Mathf.Clamp(usedShots, 0, GetShootingRangeWaveShotLimit());
            UpdateShootingRangeWaveBulletUi();
        }

        private bool IsNetworkedPigeonMode()
        {
            return gameMode == 1 || gameMode == 2;
        }

        private bool IsShootingRangeMode()
        {
            return gameMode == 3;
        }

        private bool IsShootingRangeModeWithSync()
        {
            return IsShootingRangeMode() && syncController != null;
        }

        public void RegisterClayHit(ClayTarget clayTarget)
        {
            if (!shootingRangeSessionActive)
            {
                return;
            }

            if (uiController == null || clayPigeonPool == null || clayTarget == null)
            {
                return;
            }

            var clayIndex = GetClayTargetIndex(clayTarget);
            if (clayIndex < 0)
            {
                return;
            }

            var uiIndex = GetClayTargetUiIndex(clayIndex);
            if (uiIndex < 0)
            {
                return;
            }

            SyncMode3ClayHit(clayIndex);
            EnsureClayHitStateBuffer();
            if (shootingRangeClayHitStates != null && clayIndex < shootingRangeClayHitStates.Length)
            {
                if (shootingRangeClayHitStates[clayIndex])
                {
                    return;
                }

                shootingRangeClayHitStates[clayIndex] = true;
            }

            shootingRangeHitsThisRound++;
            uiController.AddScore(clayTarget.GetScoreForCurrentRound());
            uiController.SetClayTargetHitState(uiIndex, true);
            RefreshActiveClayMasks();
        }

        public void ApplySyncedMode3ClayHit()
        {
            if (syncController == null)
            {
                return;
            }

            var poolIndex = syncController.GetMode3HitClayPoolIndex();
            var usedShots = syncController.GetMode3HitUsedShots();
            if (!TryApplySyncedClayHit(poolIndex, usedShots))
            {
                QueueSyncedClayHit(poolIndex, usedShots);
            }
        }

        public void ApplySyncedMode3ShotMiss()
        {
            if (syncController == null || !shootingRangeSessionActive)
            {
                return;
            }

            ApplySyncedShootingRangeShotUsage(syncController.GetMode3ShotUsedShots());
        }

        private void SyncMode3RoundSnapshotIfNeeded()
        {
            if (!mode3RoundSnapshotPendingForJoiner || !CanSendMode3Sync() || syncController == null || uiController == null)
            {
                return;
            }

            var nextRoundNumber = Mathf.Max(1, shootingRangeRoundsCompleted + 1);
            syncController.SyncMode3RoundSnapshot(nextRoundNumber, uiController.scoreCurrent, GetShootingRangeDisplayedDifficultyLevel());
            mode3RoundSnapshotPendingForJoiner = false;
        }

        private void ApplyMode3RoundSnapshot(int roundNumber, int score, int difficulty)
        {
            if (roundNumber <= 0 || IsLocalGameplayOwner())
            {
                return;
            }

            if (handledMode3RoundSnapshotRound == roundNumber)
            {
                return;
            }

            handledMode3RoundSnapshotRound = roundNumber;
            gameMode = 3;
            CancelPendingModeStart();

            if (actionController != null)
            {
                actionController.Initialize(this);
            }

            shootingRangeSessionActive = true;
            shootingRangeGameOver = false;
            shootingRangeRoundsCompleted = Mathf.Max(0, roundNumber - 1);
            shootingRangeRoundWaveCursor = 0;
            shootingRangeLaunchedThisWave = 0;
            shootingRangeResolvedThisWave = 0;
            shootingRangeWaveShotWindowActive = false;
            shootingRangeWaveShotsUsed = 0;
            shootingRangeSecondClayPending = false;
            shootingRangeSecondClayTimer = 0f;
            shootingRangeNextWavePending = false;
            shootingRangeNextWaveTimer = 0f;
            shootingRangeHitsThisRound = 0;
            shootingRangeRoundEndPending = false;
            shootingRangeRoundPerfect = false;
            shootingRangeRoundPassed = false;
            shootingRangeRoundEndUiTriggered = false;
            shootingRangeRoundEndAudioTriggered = false;
            shootingRangeRoundRestartPending = false;
            mode3OwnerWaveStartInProgress = false;
            mode3SyncedWaveStartInProgress = false;
            mode3WaveSeed = 0;
            mode3WaveSeedActive = false;
            ClearPendingSyncedClayHit();
            ResetClayHitStateBuffer();
            ResetShootingRangeWaveBulletUi();
            DespawnAllClayTargets();

            if (uiController == null)
            {
                return;
            }

            uiController.CancelShootingRangeIntro();
            uiController.SetGameOverActive(false);
            uiController.SetScoreValue(score);
            uiController.SetRoundLevel(roundNumber);
            uiController.SetDifficultyLevel(difficulty);
            uiController.SetGoodActive(false);
            uiController.ClearPerfectDisplay();
            uiController.ClearClayTargetHitIndicators();
            uiController.ClearActivePigeonMask();
            ShowShootingRangeScene();
            uiController.PlayShootingRangeIntro(this);
        }

        public void NotifyClayAvailable(ClayTarget clayTarget, bool wasHit)
        {
            if (!shootingRangeSessionActive)
            {
                return;
            }

            if (uiController != null)
            {
                var clayIndex = GetClayTargetIndex(clayTarget);
                if (clayIndex >= 0)
                {
                    var uiIndex = GetClayTargetUiIndex(clayIndex);
                    if (uiIndex >= 0)
                    {
                        uiController.SetClayTargetHitState(uiIndex, wasHit);
                    }
                }
            }

            RefreshActiveClayMasks();

            shootingRangeResolvedThisWave++;
            if (shootingRangeLaunchedThisWave < 2 || shootingRangeResolvedThisWave < 2)
            {
                return;
            }

            EndShootingRangeWaveShotWindow();
            shootingRangeRoundWaveCursor++;
            if (shootingRangeRoundWaveCursor >= Mathf.Max(1, shootingRangeWaveCount))
            {
                BeginShootingRangeRoundEnd();
                return;
            }

            ScheduleNextShootingRangeWave();
        }

        public void PlayPigeonExitAnimation(PigeonTarget pigeon)
        {
            exitAnimationActive = false;
            exitAnimationPending = 0;

            if (pigeonPool == null || pigeonPool.Length == 0)
            {
                SetFlyAwayUiActive(false);
                return;
            }

            for (int i = 0; i < pigeonPool.Length; i++)
            {
                var target = pigeonPool[i];
                if (target == null)
                {
                    continue;
                }

                if (target.TryBeginExitFlight())
                {
                    exitAnimationPending++;
                }
            }

            if (exitAnimationPending <= 0)
            {
                SetFlyAwayUiActive(false);
                return;
            }

            exitAnimationActive = true;
            SetFlyAwayUiActive(true);
        }

        public void NotifyPigeonExitStarted(PigeonTarget pigeon)
        {
            if (pigeon == null)
            {
                return;
            }

            exitAnimationPending = Mathf.Max(0, exitAnimationPending + 1);
            if (!exitAnimationActive)
            {
                exitAnimationActive = true;
            }

            SetFlyAwayUiActive(true);
        }

        public void NotifyPigeonAvailable(PigeonTarget pigeon, bool wasHit)
        {
            if (exitAnimationActive && exitAnimationPending > 0 && pigeon != null && pigeon.ConsumeExitNotification())
            {
                exitAnimationPending = Mathf.Max(0, exitAnimationPending - 1);
                if (exitAnimationPending == 0)
                {
                    exitAnimationActive = false;
                    SetFlyAwayUiActive(false);
                }
            }

            if (actionController != null)
            {
                actionController.NotifyPigeonAvailable(pigeon, wasHit);
            }
        }

        private void SetFlyAwayUiActive(bool active)
        {
            if (uiController == null)
            {
                return;
            }

            SetGameObjectActive(uiController.flyAwayBackgroundObject, active);
            SetGameObjectActive(uiController.flyAwayShootMask, active);
            SetGameObjectActive(uiController.flyAwayTextObject, active);
        }

        private void SetGameObjectActive(GameObject target, bool shouldBeActive)
        {
            if (target != null && target.activeSelf != shouldBeActive)
            {
                target.SetActive(shouldBeActive);
            }
        }

        private void StartShootingRangeDemo()
        {
            ResetShootingRangeSessionState();
            DespawnAllClayTargets();

            if (uiController != null)
            {
                uiController.ClearClayTargetHitIndicators();
            }

            if (clayPigeonPool == null || clayPigeonPool.Length < 2)
            {
                ReturnToTitleScreen();
                return;
            }

            shootingRangeSessionActive = true;
            shootingRangeRoundsCompleted = 0;
            shootingRangeRoundWaveCursor = 0;
            if (uiController != null)
            {
                uiController.SetScoreValue(0);
                uiController.SetRoundLevel(1);
                uiController.SetDifficultyLevel(GetShootingRangeDisplayedDifficultyLevel());
                uiController.SetGoodActive(false);
                uiController.ClearPerfectDisplay();
                uiController.PlayShootingRangeIntro(this);
            }
            else
            {
                BeginNextShootingRangeWave();
            }
        }

        private void DespawnAllClayTargets()
        {
            if (clayPigeonPool == null)
            {
                return;
            }

            for (int i = 0; i < clayPigeonPool.Length; i++)
            {
                var target = clayPigeonPool[i];
                if (target == null)
                {
                    continue;
                }

                target.SetManager(this);
                target.DespawnImmediate();
            }
        }

        private void TickShootingRangeSession()
        {
            if (!shootingRangeSessionActive)
            {
                return;
            }

            if (shootingRangeRoundEndPending)
            {
                return;
            }

            if (shootingRangeSecondClayPending)
            {
                shootingRangeSecondClayTimer -= Time.deltaTime;
                if (shootingRangeSecondClayTimer > 0f)
                {
                    return;
                }

                shootingRangeSecondClayPending = false;
                shootingRangeSecondClayTimer = 0f;

                if (!TryLaunchClayTargetFromPool(1))
                {
                    ReturnToTitleScreen();
                    return;
                }

                shootingRangeLaunchedThisWave = 2;
                return;
            }

            if (!shootingRangeNextWavePending)
            {
                return;
            }

            shootingRangeNextWaveTimer -= Time.deltaTime;
            if (shootingRangeNextWaveTimer > 0f)
            {
                return;
            }

            shootingRangeNextWavePending = false;
            shootingRangeNextWaveTimer = 0f;
            BeginNextShootingRangeWave();
        }

        private void ReturnToTitleScreen()
        {
            var preserveGameOver = shootingRangeGameOver;
            ResetShootingRangeSessionState();
            shootingRangeGameOver = preserveGameOver;
            DespawnAllClayTargets();

            if (uiController != null)
            {
                uiController.CancelShootingRangeIntro();
                if (!shootingRangeGameOver)
                {
                    uiController.SetGameOverActive(false);
                }
                uiController.SetGoodActive(false);
                uiController.ClearPerfectDisplay();
                uiController.ClearClayTargetHitIndicators();
                uiController.ClearActivePigeonMask();
                SetGameObjectActive(uiController.roundBackgroundObject, false);
                ShowTitleScreenScene();
            }
        }

        private void CacheGunInitialTransform()
        {
            if (gunObject == null)
            {
                return;
            }

            gunInitialPosition = gunObject.transform.position;
            gunInitialRotation = gunObject.transform.rotation;
            gunInitialTransformCached = true;
        }

        private void ApplyRespawnGun(bool takeOwnership)
        {
            if (gunObject == null)
            {
                return;
            }

            var targetPosition = gunInitialTransformCached ? gunInitialPosition : gunObject.transform.position;
            var targetRotation = gunInitialTransformCached ? gunInitialRotation : gunObject.transform.rotation;
            if (gunRespawnPoint != null)
            {
                targetPosition = gunRespawnPoint.position;
                targetRotation = gunRespawnPoint.rotation;
            }

            var pickup = gunObject.GetComponent<VRCPickup>();
            if (pickup != null)
            {
                pickup.Drop();
            }

            if (takeOwnership && Networking.LocalPlayer != null && !Networking.IsOwner(gunObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gunObject);
            }

            var body = gunObject.GetComponent<Rigidbody>();
            if (takeOwnership && body != null && !body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            gunObject.transform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        private void TickGunShotFlashObjects()
        {
            if (!gunShotFlashActive)
            {
                return;
            }

            gunShotFlashTimer -= Time.deltaTime;
            if (gunShotFlashTimer > 0f)
            {
                return;
            }

            HideGunShotFlashObjects();
        }

        private void HideGunShotFlashObjects()
        {
            gunShotFlashActive = false;
            gunShotFlashTimer = 0f;
            SetGunShotFlashObjectsActive(false);
        }

        private void SetGunShotFlashObjectsActive(bool active)
        {
            if (gunShotFlashObjects == null)
            {
                return;
            }

            for (int i = 0; i < gunShotFlashObjects.Length; i++)
            {
                var target = gunShotFlashObjects[i];
                if (target != null && target.activeSelf != active)
                {
                    target.SetActive(active);
                }
            }
        }

        private void DespawnAllPigeons()
        {
            if (pigeonPool == null)
            {
                return;
            }

            for (int i = 0; i < pigeonPool.Length; i++)
            {
                var pigeon = pigeonPool[i];
                if (pigeon == null)
                {
                    continue;
                }

                pigeon.SetManager(this);
                pigeon.SetPlayArea(playArea);
                pigeon.DespawnImmediate();
            }
        }

        private void ResetShootingRangeSessionState()
        {
            shootingRangeSessionActive = false;
            shootingRangeGameOver = false;
            mode3RoundSnapshotPendingForJoiner = false;
            handledMode3RoundSnapshotRound = 0;
            shootingRangeRoundWaveCursor = 0;
            shootingRangeLaunchedThisWave = 0;
            shootingRangeResolvedThisWave = 0;
            shootingRangeWaveShotWindowActive = false;
            shootingRangeWaveShotsUsed = 0;
            shootingRangeSecondClayPending = false;
            shootingRangeSecondClayTimer = 0f;
            shootingRangeNextWavePending = false;
            shootingRangeNextWaveTimer = 0f;
            shootingRangeRoundsCompleted = 0;
            shootingRangeHitsThisRound = 0;
            shootingRangeRoundEndPending = false;
            shootingRangeRoundPerfect = false;
            shootingRangeRoundPassed = false;
            shootingRangeRoundEndUiTriggered = false;
            shootingRangeRoundEndAudioTriggered = false;
            shootingRangeRoundRestartPending = false;
            mode3OwnerWaveStartInProgress = false;
            mode3SyncedWaveStartInProgress = false;
            mode3WaveSeed = 0;
            mode3WaveSeedActive = false;
            ClearPendingSyncedClayHit();
            ResetClayHitStateBuffer();
            ResetShootingRangeWaveBulletUi();
            ClearMode3RoundSnapshotIfOwner();
        }

        private void BeginNextShootingRangeWave()
        {
            if (!shootingRangeSessionActive)
            {
                return;
            }

            if (!mode3OwnerWaveStartInProgress && !mode3SyncedWaveStartInProgress && IsShootingRangeModeWithSync() && !IsLocalGameplayOwner())
            {
                return;
            }

            shootingRangeLaunchedThisWave = 0;
            shootingRangeResolvedThisWave = 0;
            shootingRangeWaveShotsUsed = 0;
            shootingRangeSecondClayPending = false;
            shootingRangeSecondClayTimer = 0f;
            shootingRangeNextWavePending = false;
            shootingRangeNextWaveTimer = 0f;
            shootingRangeRoundEndPending = false;
            shootingRangeRoundPerfect = false;
            shootingRangeRoundPassed = false;
            shootingRangeRoundEndUiTriggered = false;
            shootingRangeRoundEndAudioTriggered = false;
            shootingRangeRoundRestartPending = false;
            EnsureMode3WaveSeedForCurrentWave();
            BeginShootingRangeWaveShotWindow();

            if (!TryLaunchClayTargetFromPool(0))
            {
                ReturnToTitleScreen();
                return;
            }

            shootingRangeLaunchedThisWave = 1;

            var delayMin = Mathf.Min(shootingRangePairLaunchDelayMin, shootingRangePairLaunchDelayMax);
            var delayMax = Mathf.Max(shootingRangePairLaunchDelayMin, shootingRangePairLaunchDelayMax);
            shootingRangeSecondClayPending = true;
            shootingRangeSecondClayTimer = SampleMode3Range(delayMin, delayMax, 53);
        }

        private void StartSyncedShootingRangeWave(int roundNumber, int waveIndex, int seed)
        {
            if (!shootingRangeSessionActive || !IsShootingRangeMode())
            {
                return;
            }

            if (IsLocalGameplayOwner())
            {
                return;
            }

            var currentRound = Mathf.Max(1, shootingRangeRoundsCompleted + 1);
            if (roundNumber < currentRound)
            {
                return;
            }

            if (roundNumber > currentRound)
            {
                shootingRangeRoundsCompleted = Mathf.Max(0, roundNumber - 1);
            }

            shootingRangeRoundWaveCursor = Mathf.Max(0, waveIndex);
            mode3WaveSeed = seed;
            mode3WaveSeedActive = true;
            mode3SyncedWaveStartInProgress = true;
            BeginNextShootingRangeWave();
            mode3SyncedWaveStartInProgress = false;
        }

        private void EnsureMode3WaveSeedForCurrentWave()
        {
            if (mode3WaveSeedActive)
            {
                return;
            }

            mode3WaveSeed = Random.Range(1, int.MaxValue);
            mode3WaveSeedActive = true;

            if (!CanSendMode3Sync())
            {
                return;
            }

            mode3OwnerWaveStartInProgress = true;
            syncController.SyncMode3WaveStart(Mathf.Max(1, shootingRangeRoundsCompleted + 1), shootingRangeRoundWaveCursor, mode3WaveSeed);
            ClearMode3RoundSnapshotIfOwner();
            mode3OwnerWaveStartInProgress = false;
        }

        private void ScheduleNextShootingRangeWave()
        {
            EndShootingRangeWaveShotWindow();
            mode3WaveSeedActive = false;
            shootingRangeSecondClayPending = false;
            shootingRangeSecondClayTimer = 0f;
            shootingRangeNextWavePending = true;
            shootingRangeNextWaveTimer = Mathf.Max(0f, shootingRangeWaveDelay);
        }

        public void OnShootingRangeIntroFinished()
        {
            if (!shootingRangeSessionActive)
            {
                return;
            }

            shootingRangeRoundWaveCursor = 0;
            shootingRangeHitsThisRound = 0;
            shootingRangeRoundPerfect = false;
            shootingRangeRoundPassed = false;
            shootingRangeRoundEndPending = false;
            shootingRangeRoundEndUiTriggered = false;
            shootingRangeRoundEndAudioTriggered = false;
            BeginNextShootingRangeWave();
        }

        private bool TryRegisterShootingRangeShot()
        {
            if (!shootingRangeWaveShotWindowActive)
            {
                return false;
            }

            var shotLimit = GetShootingRangeWaveShotLimit();
            if (shotLimit <= 0)
            {
                return false;
            }

            if (shootingRangeWaveShotsUsed >= shotLimit)
            {
                return false;
            }

            shootingRangeWaveShotsUsed++;
            UpdateShootingRangeWaveBulletUi();
            return true;
        }

        private void BeginShootingRangeWaveShotWindow()
        {
            shootingRangeWaveShotWindowActive = true;
            shootingRangeWaveShotsUsed = 0;

            if (uiController == null)
            {
                return;
            }

            uiController.ConfigureBulletClip(uiController.bulletMaxCount);
            uiController.ResetBulletClipUsage();
        }

        private void EndShootingRangeWaveShotWindow()
        {
            shootingRangeWaveShotWindowActive = false;
            shootingRangeWaveShotsUsed = 0;
            ResetShootingRangeWaveBulletUi();
        }

        private void UpdateShootingRangeWaveBulletUi()
        {
            if (uiController == null)
            {
                return;
            }

            uiController.SetBulletUsage(shootingRangeWaveShotsUsed);
        }

        private void ResetShootingRangeWaveBulletUi()
        {
            if (uiController == null)
            {
                return;
            }

            uiController.ResetBulletClipUsage();
        }

        private int GetShootingRangeWaveShotLimit()
        {
            if (uiController == null)
            {
                return 0;
            }

            return Mathf.Max(0, uiController.bulletMaxCount);
        }

        private bool TryLaunchClayTargetFromPool(int poolIndex)
        {
            if (clayPigeonPool == null || poolIndex < 0 || poolIndex >= clayPigeonPool.Length)
            {
                return false;
            }

            var target = clayPigeonPool[poolIndex];
            if (target == null || !target.IsAvailable)
            {
                return false;
            }

            if (!TryBuildClaySpawnParameters(
                out Vector3 spawnPosition,
                out Vector3 endPosition,
                out float hideHeightY,
                out float duration,
                out float peakHeight,
                out float lifetime,
                out float recycleDelay))
            {
                return false;
            }

            target.SetManager(this);
            target.BeginFlightInSpace(
                spawnPosition,
                endPosition,
                hideHeightY,
                duration,
                peakHeight,
                lifetime,
                recycleDelay,
                shootingRangeMoveArea);

            EnsureClayHitStateBuffer();
            if (shootingRangeClayHitStates != null && poolIndex < shootingRangeClayHitStates.Length)
            {
                shootingRangeClayHitStates[poolIndex] = false;
            }

            var uiIndex = GetWaveClayUiIndex(shootingRangeRoundWaveCursor, shootingRangeLaunchedThisWave);
            EnsureClayUiIndexBuffer();
            if (shootingRangeClayUiIndices != null && poolIndex < shootingRangeClayUiIndices.Length)
            {
                shootingRangeClayUiIndices[poolIndex] = uiIndex;
            }

            if (uiController != null && uiIndex >= 0)
            {
                uiController.SetClayTargetHitState(uiIndex, false);
            }

            RefreshActiveClayMasks();

            return true;
        }

        private int GetClayTargetIndex(ClayTarget clayTarget)
        {
            if (clayPigeonPool == null || clayTarget == null)
            {
                return -1;
            }

            for (int i = 0; i < clayPigeonPool.Length; i++)
            {
                if (clayPigeonPool[i] == clayTarget)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureClayHitStateBuffer()
        {
            if (clayPigeonPool == null || clayPigeonPool.Length == 0)
            {
                shootingRangeClayHitStates = null;
                return;
            }

            var desired = clayPigeonPool.Length;
            if (shootingRangeClayHitStates == null || shootingRangeClayHitStates.Length != desired)
            {
                shootingRangeClayHitStates = new bool[desired];
            }
        }

        private void EnsureClayUiIndexBuffer()
        {
            if (clayPigeonPool == null || clayPigeonPool.Length == 0)
            {
                shootingRangeClayUiIndices = null;
                return;
            }

            var desired = clayPigeonPool.Length;
            if (shootingRangeClayUiIndices == null || shootingRangeClayUiIndices.Length != desired)
            {
                shootingRangeClayUiIndices = new int[desired];
            }
        }

        private void ResetClayHitStateBuffer()
        {
            EnsureClayHitStateBuffer();
            EnsureClayUiIndexBuffer();
            if (shootingRangeClayHitStates == null)
            {
                return;
            }

            for (int i = 0; i < shootingRangeClayHitStates.Length; i++)
            {
                shootingRangeClayHitStates[i] = false;
            }

            if (shootingRangeClayUiIndices != null)
            {
                for (int i = 0; i < shootingRangeClayUiIndices.Length; i++)
                {
                    shootingRangeClayUiIndices[i] = -1;
                }
            }
        }

        private void RefreshActiveClayMasks()
        {
            if (uiController == null)
            {
                return;
            }

            uiController.ClearActivePigeonMask();

            if (clayPigeonPool == null || clayPigeonPool.Length == 0)
            {
                return;
            }

            EnsureClayHitStateBuffer();

            var firstActiveIndex = -1;
            var secondActiveIndex = -1;
            for (int i = 0; i < clayPigeonPool.Length; i++)
            {
                var clayTarget = clayPigeonPool[i];
                if (clayTarget == null || clayTarget.IsAvailable)
                {
                    continue;
                }

                if (shootingRangeClayHitStates != null && i < shootingRangeClayHitStates.Length && shootingRangeClayHitStates[i])
                {
                    continue;
                }

                var uiIndex = GetClayTargetUiIndex(i);
                if (uiIndex < 0)
                {
                    continue;
                }

                if (firstActiveIndex < 0)
                {
                    firstActiveIndex = uiIndex;
                }
                else
                {
                    secondActiveIndex = uiIndex;
                    break;
                }
            }

            if (firstActiveIndex >= 0 && secondActiveIndex >= 0)
            {
                uiController.SetActivePigeonMaskTargets(firstActiveIndex, secondActiveIndex);
                return;
            }

            if (firstActiveIndex >= 0)
            {
                uiController.SetActivePigeonMaskTargets(firstActiveIndex, -1);
            }
        }

        private int GetClayTargetUiIndex(int clayPoolIndex)
        {
            if (clayPoolIndex < 0)
            {
                return -1;
            }

            EnsureClayUiIndexBuffer();
            if (shootingRangeClayUiIndices == null || clayPoolIndex >= shootingRangeClayUiIndices.Length)
            {
                return -1;
            }

            return shootingRangeClayUiIndices[clayPoolIndex];
        }

        private int GetWaveClayUiIndex(int waveIndex, int launchedIndexInWave)
        {
            if (waveIndex < 0 || launchedIndexInWave < 0)
            {
                return -1;
            }

            return (waveIndex * 2) + launchedIndexInWave;
        }

        private bool DetermineShootingRangeRoundPass(int roundClayCount)
        {
            var requiredHits = GetRequiredHitsForShootingRangeDifficulty(GetShootingRangeDisplayedDifficultyLevel(), roundClayCount);
            return shootingRangeHitsThisRound >= requiredHits;
        }

        private int GetShootingRangeDisplayedDifficultyLevel()
        {
            var roundNumber = Mathf.Max(1, shootingRangeRoundsCompleted + 1);
            return Mathf.Min(GetShootingRangeDifficultyLevelForRound(roundNumber), ShootingRangeMaxDifficultyLevel);
        }

        private int GetShootingRangeDifficultyLevelForRound(int roundNumber)
        {
            if (roundNumber <= 0)
            {
                return 0;
            }

            return Mathf.Max(0, (roundNumber - 1) / 3);
        }

        private int GetRequiredHitsForShootingRangeDifficulty(int difficultyLevel, int quota)
        {
            var effective = Mathf.Clamp(difficultyLevel, 0, ShootingRangeMaxDifficultyLevel);
            var required = 6 + effective;
            if (quota > 0)
            {
                required = Mathf.Min(required, quota);
            }

            return Mathf.Max(0, required);
        }

        private float GetCurrentShootingRangeClayLifetime()
        {
            var maxLifetime = Mathf.Max(0.1f, shootingRangeClayLifetimeMax);
            var minLifetime = Mathf.Max(0.1f, shootingRangeClayLifetimeMin);
            if (minLifetime > maxLifetime)
            {
                var swap = minLifetime;
                minLifetime = maxLifetime;
                maxLifetime = swap;
            }

            var roundNumber = Mathf.Max(1, shootingRangeRoundsCompleted + 1);
            var lifetime = maxLifetime - ((roundNumber - 1) * Mathf.Max(0f, shootingRangeClayLifetimeStepPerRound));
            return Mathf.Clamp(lifetime, minLifetime, maxLifetime);
        }

        private void BeginShootingRangeRoundEnd()
        {
            shootingRangeRoundEndPending = true;
            shootingRangeRoundEndUiTriggered = false;
            shootingRangeRoundEndAudioTriggered = false;
            shootingRangeRoundRestartPending = false;
            mode3WaveSeedActive = false;
            var roundClayCount = Mathf.Max(1, shootingRangeWaveCount) * 2;
            shootingRangeRoundPerfect = shootingRangeHitsThisRound >= roundClayCount;
            shootingRangeRoundPassed = DetermineShootingRangeRoundPass(roundClayCount);

            if (uiController != null)
            {
                uiController.ClearActivePigeonMask();
            }
        }

        private bool TryForceSettleShootingRangeRound(bool forcePass)
        {
            if (!shootingRangeSessionActive)
            {
                return false;
            }

            if (shootingRangeRoundEndPending)
            {
                return true;
            }

            shootingRangeSecondClayPending = false;
            shootingRangeSecondClayTimer = 0f;
            shootingRangeNextWavePending = false;
            shootingRangeNextWaveTimer = 0f;
            EndShootingRangeWaveShotWindow();
            DespawnAllClayTargets();

            shootingRangeRoundWaveCursor = Mathf.Max(1, shootingRangeWaveCount);
            if (forcePass)
            {
                shootingRangeHitsThisRound = Mathf.Max(shootingRangeHitsThisRound, Mathf.Max(1, shootingRangeWaveCount) * 2);
            }

            BeginShootingRangeRoundEnd();
            return true;
        }

        private void TickShootingRangeRoundEnd()
        {
            if (!shootingRangeSessionActive || !shootingRangeRoundEndPending)
            {
                return;
            }

            if (!shootingRangeRoundEndUiTriggered)
            {
                shootingRangeRoundEndUiTriggered = true;

                if (uiController != null)
                {
                    if (shootingRangeRoundPerfect)
                    {
                        uiController.SetGoodActive(false);
                        if (!shootingRangeRoundEndAudioTriggered)
                        {
                            shootingRangeRoundEndAudioTriggered = true;
                            if (soundManager != null)
                            {
                                var delay = Mathf.Max(0f, soundManager.scoreCountAudioDuration);
                                var duration = Mathf.Max(0f, soundManager.fullHitAudioDuration);
                                uiController.ShowPerfectDisplay(delay, duration);
                                soundManager.PlayRoundClearSequence(true);
                            }
                            else
                            {
                                uiController.ShowPerfectDisplay(0f, 0f);
                            }
                        }

                        if (uiController.BeginClayRoundEndHitAnimation(true))
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (uiController.BeginClayRoundEndHitAnimation(false))
                        {
                            return;
                        }
                    }
                }
            }

            if (uiController != null && uiController.IsHitCountAnimationActive())
            {
                return;
            }

            if (!shootingRangeRoundEndAudioTriggered)
            {
                if (!shootingRangeRoundPassed)
                {
                    if (uiController != null)
                    {
                        uiController.SetGoodActive(false);
                        uiController.ClearPerfectDisplay();
                        uiController.RefreshTopScoreOnGameOver();
                        SetGameObjectActive(uiController.gameOverTextObject, true);
                    }

                    shootingRangeRoundEndAudioTriggered = true;
                    if (soundManager != null)
                    {
                        soundManager.PlayRoundFailSequenceWithoutDog();
                    }
                }
                else if (soundManager != null)
                {
                    shootingRangeRoundEndAudioTriggered = true;
                    if (shootingRangeRoundPassed)
                    {
                        if (uiController != null)
                        {
                            uiController.SetGoodActive(!shootingRangeRoundPerfect);

                            if (shootingRangeRoundPerfect)
                            {
                                var delay = Mathf.Max(0f, soundManager.scoreCountAudioDuration);
                                var duration = Mathf.Max(0f, soundManager.fullHitAudioDuration);
                                uiController.ShowPerfectDisplay(delay, duration);
                            }
                            else
                            {
                                uiController.ClearPerfectDisplay();
                            }
                        }

                        soundManager.PlayRoundClearSequence(shootingRangeRoundPerfect);
                    }
                }
                else if (shootingRangeRoundPassed && uiController != null)
                {
                    shootingRangeRoundEndAudioTriggered = true;
                    uiController.SetGoodActive(!shootingRangeRoundPerfect);
                    if (shootingRangeRoundPerfect)
                    {
                        uiController.ShowPerfectDisplay(0f, 0f);
                    }
                    else
                    {
                        uiController.ClearPerfectDisplay();
                    }
                }
            }

            if (soundManager != null && soundManager.IsSequenceActive())
            {
                return;
            }

            FinalizeShootingRangeRoundEnd();
        }

        private void FinalizeShootingRangeRoundEnd()
        {
            shootingRangeRoundEndPending = false;
            shootingRangeRoundEndUiTriggered = false;
            shootingRangeRoundEndAudioTriggered = false;

            if (uiController != null)
            {
                uiController.SetGoodActive(false);
                if (!shootingRangeRoundPerfect)
                {
                    uiController.ClearPerfectDisplay();
                }
            }

            if (!shootingRangeRoundPassed)
            {
                shootingRangeGameOver = true;
                ReturnToTitleScreen();
                return;
            }

            shootingRangeGameOver = false;
            shootingRangeRoundsCompleted++;
            if (uiController != null)
            {
                uiController.SetRoundLevel(Mathf.Max(1, shootingRangeRoundsCompleted + 1));
                uiController.SetDifficultyLevel(GetShootingRangeDisplayedDifficultyLevel());
            }

            SyncMode3RoundSnapshotIfNeeded();

            if (uiController != null)
            {
                uiController.ClearClayTargetHitIndicators();
                uiController.PlayShootingRangeIntro(this);
            }
            else
            {
                BeginNextShootingRangeWave();
            }
        }

        private void ClearMode3RoundSnapshotIfOwner()
        {
            if (syncController != null && IsLocalGameplayOwner())
            {
                syncController.ClearMode3RoundSnapshot();
            }
        }

        private bool TryBuildClaySpawnParameters(
            out Vector3 spawnPosition,
            out Vector3 endPosition,
            out float hideHeightY,
            out float duration,
            out float peakHeight,
            out float lifetime,
            out float recycleDelay)
        {
            spawnPosition = Vector3.zero;
            endPosition = Vector3.zero;
            hideHeightY = 0f;
            var currentLifetime = GetCurrentShootingRangeClayLifetime();
            duration = Mathf.Max(0.1f, currentLifetime);
            peakHeight = 0f;
            lifetime = Mathf.Max(0.1f, currentLifetime);
            recycleDelay = Mathf.Max(0f, shootingRangeHideRecycleDelay);

            var moveArea = shootingRangeMoveArea;
            if (moveArea == null)
            {
                return false;
            }

            var rect = moveArea.rect;
            var minX = rect.xMin;
            var maxX = rect.xMax;
            var minY = rect.yMin;
            var maxY = rect.yMax;
            var width = Mathf.Max(0.0001f, rect.width);
            var height = Mathf.Max(0.0001f, rect.height);
            var localInset = ConvertWorldHorizontalInsetToLocal(moveArea, shootingRangeBottomSpawnSegment);
            var startX = SampleWithin(minX, maxX, localInset, GetMode3Wave01(11 + shootingRangeLaunchedThisWave));
            var startY = minY;
            var centerX = (minX + maxX) * 0.5f;
            var moveRight = startX <= centerX;
            var angleMin = Mathf.Min(shootingRangeMinLaunchAngle, shootingRangeMaxLaunchAngle);
            var angleMax = Mathf.Max(shootingRangeMinLaunchAngle, shootingRangeMaxLaunchAngle);
            var angle = SampleRange(angleMin, angleMax, GetMode3Wave01(23 + shootingRangeLaunchedThisWave));
            var radians = angle * Mathf.Deg2Rad;
            var horizontalDirection = moveRight ? 1f : -1f;
            var horizontalSpeedRatio = Mathf.Max(0.01f, Mathf.Cos(radians));
            var lifetimeSeconds = Mathf.Max(0.1f, currentLifetime);
            var worldSpeed = Mathf.Max(0.01f, shootingRangeTravelSpeedPerSecond * width);
            var horizontalDistance = worldSpeed * horizontalSpeedRatio * lifetimeSeconds;
            var endX = startX + (horizontalDistance * horizontalDirection);
            endX = Mathf.Clamp(endX, minX, maxX);

            var peakMinRatio = Mathf.Min(shootingRangePeakHeightRange.x, shootingRangePeakHeightRange.y);
            var peakMaxRatio = Mathf.Max(shootingRangePeakHeightRange.x, shootingRangePeakHeightRange.y);
            peakHeight = height * SampleRange(peakMinRatio, peakMaxRatio, GetMode3Wave01(37 + shootingRangeLaunchedThisWave));
            peakHeight = Mathf.Max(0.01f, peakHeight);

            var endHeightMinRatio = Mathf.Min(shootingRangeEndHeightRange.x, shootingRangeEndHeightRange.y);
            var endHeightMaxRatio = Mathf.Max(shootingRangeEndHeightRange.x, shootingRangeEndHeightRange.y);
            hideHeightY = minY + (height * SampleRange(endHeightMinRatio, endHeightMaxRatio, GetMode3Wave01(41 + shootingRangeLaunchedThisWave)));
            hideHeightY = Mathf.Clamp(hideHeightY, minY, maxY);

            spawnPosition = new Vector3(startX, startY, 0f);
            endPosition = new Vector3(endX, startY, 0f);
            duration = lifetimeSeconds;
            lifetime = lifetimeSeconds;
            recycleDelay = Mathf.Max(0f, shootingRangeHideRecycleDelay);

            return true;
        }

        private float ConvertWorldHorizontalInsetToLocal(RectTransform area, float worldInset)
        {
            if (area == null)
            {
                return Mathf.Max(0f, worldInset);
            }

            var scale = Mathf.Max(0.0001f, Mathf.Abs(area.lossyScale.x));
            return Mathf.Max(0f, worldInset) / scale;
        }

        private float SampleWithin(float min, float max, float segmentInset)
        {
            return SampleWithin(min, max, segmentInset, Random.value);
        }

        private float SampleWithin(float min, float max, float segmentInset, float value01)
        {
            var clampedInset = Mathf.Max(0f, segmentInset);
            var paddedMin = min + clampedInset;
            var paddedMax = max - clampedInset;

            if (paddedMin > paddedMax)
            {
                var midpoint = (min + max) * 0.5f;
                paddedMin = midpoint;
                paddedMax = midpoint;
            }

            if (Mathf.Approximately(paddedMin, paddedMax))
            {
                return paddedMin;
            }

            return SampleRange(paddedMin, paddedMax, value01);
        }

        private float SampleRange(float min, float max, float value01)
        {
            if (Mathf.Approximately(min, max))
            {
                return min;
            }

            return Mathf.Lerp(min, max, Mathf.Clamp01(value01));
        }

        private float SampleMode3Range(float min, float max, int salt)
        {
            return SampleRange(min, max, GetMode3Wave01(salt));
        }

        private float GetMode3Wave01(int salt)
        {
            if (!mode3WaveSeedActive)
            {
                return Random.value;
            }

            var value = Mathf.Abs(mode3WaveSeed);
            value = MixMode3WaveValue(value, shootingRangeRoundsCompleted + 1);
            value = MixMode3WaveValue(value, shootingRangeRoundWaveCursor);
            value = MixMode3WaveValue(value, salt);
            return (value & 0x7fffffff) / 2147483647f;
        }

        private int MixMode3WaveValue(int value, int salt)
        {
            var mixed = Mathf.Abs(value + 31 * (salt + 1));
            mixed = (mixed * 1103515245 + 12345) & 0x7fffffff;
            return mixed;
        }

        private void ShowTitleScreenScene()
        {
            if (uiController == null)
            {
                return;
            }

            SetGameObjectActive(uiController.gameOverTextObject, false);
            SetGameObjectActive(uiController.titleScreenObject, true);
            SetGameObjectActive(uiController.modeABSceneObject, false);
            SetGameObjectActive(uiController.shootingRangeSceneObject, false);
        }

        private void ShowModeABScene()
        {
            if (uiController == null)
            {
                return;
            }

            SetGameObjectActive(uiController.gameOverTextObject, false);
            SetGameObjectActive(uiController.titleScreenObject, false);
            SetGameObjectActive(uiController.modeABSceneObject, true);
            SetGameObjectActive(uiController.shootingRangeSceneObject, false);
        }

        private void ShowShootingRangeScene()
        {
            if (uiController == null)
            {
                return;
            }

            SetGameObjectActive(uiController.gameOverTextObject, false);
            SetGameObjectActive(uiController.titleScreenObject, false);
            SetGameObjectActive(uiController.modeABSceneObject, false);
            SetGameObjectActive(uiController.shootingRangeSceneObject, true);
        }

        public void ScreenRetroTvMaskToggle()
        {
            if (screenRetroTvMask == null)
            {
                return;
            }

            screenRetroTvMask.SetActive(!screenRetroTvMask.activeSelf);

            enableGunShotFlashObjects = !enableGunShotFlashObjects;
        }

        public void BGMToggle()
        {
            if (TitleScreenBGM != null)
            {
                TitleScreenBGM.SetActive(!TitleScreenBGM.activeSelf);
            }
        }
    }
}
