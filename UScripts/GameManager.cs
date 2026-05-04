using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;

namespace PigeonHunt
{
    [AddComponentMenu("PigeonHunt/Game Manager")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GameManager : UdonSharpBehaviour
    {
        [Header("Round Settings")]
        public int pigeonsPerRound = 10;
        public float spawnDelay = 4f;

        [Header("Game Mode")]
        [Tooltip("1 = Single mode, 2 = Pair mode, 3 = Training mode")]
        [Range(1, 3)]
        public int gameMode = 1;

        [Header("Main Menu")]
        [Min(0f)]
        public float modeConfirmDelay = 1f;

        [Header("Difficulty")]
        public float roundDifficultyStep = 0.25f;
        public float maxDifficultyMultiplier = 3f;

        [Header("Shot Reaction")]
        [Tooltip("Chance per shot for each flying, unhit pigeon to change direction.")]
        [Range(0f, 1f)]
        public float shotDirectionChangeChance = 0.4f;

        [Header("Play Area")]
        public RectTransform playArea;
        public RectTransform shootingRangeMoveArea;
        [Tooltip("World-space inset applied at both ends of the bottom edge spawn segment.")]
        [FormerlySerializedAs("bottomEdgePadding")]
        public float bottomEdgeSpawnSegment = 0.25f;

        [Header("Shooting Range Spawn")]
        [Tooltip("World-space inset applied to the left and right side of the shooting range bottom edge.")]
        public float shootingRangeBottomSpawnSegment = 0.25f;
        [Tooltip("Minimum launch angle in degrees from world right/up mirrored by side.")]
        public float shootingRangeMinLaunchAngle = 58f;
        [Tooltip("Maximum launch angle in degrees from world right/up mirrored by side.")]
        public float shootingRangeMaxLaunchAngle = 74f;
        [Tooltip("Normalized speed in relation to the shooting range move area width.")]
        public float shootingRangeTravelSpeedPerSecond = 0.48f;
        [Tooltip("Peak height relative to the shooting range area height.")]
        public Vector2 shootingRangePeakHeightRange = new Vector2(0.45f, 0.72f);
        [Tooltip("Height relative to the shooting range area height where all clay visuals should hide.")]
        public Vector2 shootingRangeEndHeightRange = new Vector2(0.1f, 0.25f);
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
        public float shootingRangePairLaunchDelayMin = 0.15f;
        [Min(0f)]
        public float shootingRangePairLaunchDelayMax = 0.45f;
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

        private const float DefaultDifficulty = 1f;
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
        private const int ShootingRangeMaxDifficultyLevel = 4;
        private readonly Vector3[] shootingRangeCorners = new Vector3[4];

        public bool RoundActive => actionController != null && actionController.RoundActive;
        public float DifficultyMultiplier => actionController != null ? actionController.DifficultyMultiplier : DefaultDifficulty;

        private void Start()
        {
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
            TickPendingModeStart();
            TickShootingRangeSession();
            TickShootingRangeRoundEnd();

            if (actionController != null)
            {
                actionController.Tick();
            }
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
            ScheduleModeStart(modeIndex);
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
                    Debug.Log("[GameManager] Mode B is not implemented yet.");
                    break;
                case 2:
                    StartModeC();
                    break;
            }
        }

        public void RegisterPigeonHit(PigeonTarget pigeon)
        {
            if (actionController != null)
            {
                actionController.RegisterPigeonHit(pigeon);
            }
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
                return;
            }

            if (actionController != null)
            {
                actionController.NotifyShotOutcome(hitPigeon);
            }
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

            EnsureClayHitStateBuffer();
            if (shootingRangeClayHitStates != null && clayIndex < shootingRangeClayHitStates.Length)
            {
                shootingRangeClayHitStates[clayIndex] = true;
            }

            shootingRangeHitsThisRound++;
            uiController.AddScore(clayTarget.GetScoreForCurrentRound());
            uiController.SetClayTargetHitState(uiIndex, true);
            RefreshActiveClayMasks();
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

        private void ResetShootingRangeSessionState()
        {
            shootingRangeSessionActive = false;
            shootingRangeGameOver = false;
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
            ResetClayHitStateBuffer();
            ResetShootingRangeWaveBulletUi();
        }

        private void BeginNextShootingRangeWave()
        {
            if (!shootingRangeSessionActive)
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
            shootingRangeSecondClayTimer = Random.Range(delayMin, delayMax);
        }

        private void ScheduleNextShootingRangeWave()
        {
            EndShootingRangeWaveShotWindow();
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
            target.BeginFlight(
                spawnPosition,
                endPosition,
                hideHeightY,
                duration,
                peakHeight,
                lifetime,
                recycleDelay);

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
                uiController.ShowActivePigeonMaskPair(firstActiveIndex, secondActiveIndex);
                return;
            }

            if (firstActiveIndex >= 0)
            {
                uiController.ShowActivePigeonMask(firstActiveIndex);
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

            if (!QychuiUtilities.TryGetRectWorldBounds(
                moveArea,
                shootingRangeCorners,
                out float minX,
                out float maxX,
                out float minY,
                out float maxY,
                out float planeZ))
            {
                return false;
            }

            var width = Mathf.Max(0.0001f, maxX - minX);
            var height = Mathf.Max(0.0001f, maxY - minY);
            var startX = SampleWithin(minX, maxX, shootingRangeBottomSpawnSegment);
            var startY = minY;
            var centerX = (minX + maxX) * 0.5f;
            var moveRight = startX <= centerX;
            var angleMin = Mathf.Min(shootingRangeMinLaunchAngle, shootingRangeMaxLaunchAngle);
            var angleMax = Mathf.Max(shootingRangeMinLaunchAngle, shootingRangeMaxLaunchAngle);
            var angle = Random.Range(angleMin, angleMax);
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
            peakHeight = height * Random.Range(peakMinRatio, peakMaxRatio);
            peakHeight = Mathf.Max(0.01f, peakHeight);

            var endHeightMinRatio = Mathf.Min(shootingRangeEndHeightRange.x, shootingRangeEndHeightRange.y);
            var endHeightMaxRatio = Mathf.Max(shootingRangeEndHeightRange.x, shootingRangeEndHeightRange.y);
            hideHeightY = minY + (height * Random.Range(endHeightMinRatio, endHeightMaxRatio));
            hideHeightY = Mathf.Clamp(hideHeightY, minY, maxY);

            spawnPosition = new Vector3(startX, startY, planeZ);
            endPosition = new Vector3(endX, startY, planeZ);
            duration = lifetimeSeconds;
            lifetime = lifetimeSeconds;
            recycleDelay = Mathf.Max(0f, shootingRangeHideRecycleDelay);

            return true;
        }

        private float SampleWithin(float min, float max, float segmentInset)
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

            return Random.Range(paddedMin, paddedMax);
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
    }
}
