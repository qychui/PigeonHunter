using UdonSharp;
using UnityEngine;

namespace PigeonHunt
{
    [AddComponentMenu("PigeonHunt/Action Controller")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ActionController : UdonSharpBehaviour
    {
        private GameManager manager;

        private bool roundActive;
        private int currentRoundIndex;
        private int targetQuotaThisRound;
        private int pigeonsSpawnedThisRound;
        private int pigeonsResolvedThisRound;
        private int pigeonsHitThisRound;
        private float spawnTimer;
        private int streak;
        private float roundDifficultyBonus;
        private float hitDifficultyBonus;
        private bool waitingForStartAnimation;
        private float startAnimationTimer = 0f;
        private float startDelayTime = 0f;
        private int activeGameMode = GameModeSingle;
        private bool pairWaveActive;
        private int pairWavePendingResolutions;
        private bool pairWaveHadHit;
        private bool pairWaveHadMiss;
        private bool waveActive;
        private int shotsUsedThisWave;
        private bool pendingExitOnMiss;
        private bool roundEndPending;
        private bool roundEndHitCountTriggered;
        private bool roundEndAudioTriggered;
        private bool roundEndPassed;
        private float roundEndAnimationTimer;
        [Min(0f)]
        [SerializeField] private float roundEndAudioDelay = 0.5f;
        private float roundEndAudioDelayTimer;
        private bool roundEndAudioDelayStarted;
        private bool lastResolutionHadAnimation;
        private bool lastResolutionWasHit;
        private int currentDifficultyLevel;
        private bool gameLocked;
        private bool carryScorePending;
        private int carryScoreValue;

        private Vector3[] cachedCorners = new Vector3[4];
        private int[] edgeSelectionBuffer = new int[4];

        private const int EdgeLeft = 0;
        private const int EdgeRight = 1;
        private const int EdgeTop = 2;
        private const int EdgeBottom = 3;

        private const int DirectionRight = 0;
        private const int DirectionRightUp = 1;
        private const int DirectionRightDown = 2;
        private const int DirectionLeft = 3;
        private const int DirectionLeftUp = 4;
        private const int DirectionLeftDown = 5;

        private const int GameModeSingle = 1;
        private const int GameModePair = 2;

        private const string StatusWaiting = "Press trigger to start";
        private const string StatusPlaying = "";
        private const string StatusFinished = "Round complete";
        private const int WeaponZeroClipSize = 3;
        private const int MaxDifficultyLevel = 4;
        public bool RoundActive
        {
            get { return roundActive; }
        }

        public int CurrentRoundNumber
        {
            get { return GetDisplayedRoundNumber(); }
        }

        public bool IsGameOver
        {
            get { return gameLocked; }
        }

        public float DifficultyMultiplier
        {
            get
            {
                float bonus = Mathf.Max(0f, roundDifficultyBonus + hitDifficultyBonus);
                float multiplier = 1f + bonus;
                if (manager != null && manager.maxDifficultyMultiplier > 0f)
                {
                    multiplier = Mathf.Min(multiplier, manager.maxDifficultyMultiplier);
                }

                return multiplier;
            }
        }

        public void Initialize(GameManager owner)
        {
            manager = owner;
            InitializePool();
            ResetRoundState();
        }

        public void HandleInteract()
        {
            if (manager == null)
            {
                return;
            }

            if (gameLocked)
            {
                return;
            }

            if (!roundActive)
            {
                if (!roundEndPending)
                {
                    BeginRound();
                }
            }
        }

        public void Tick()
        {
            if (manager == null)
            {
                return;
            }

            if (roundEndPending)
            {
                var isPerfectHit = targetQuotaThisRound > 0 && pigeonsHitThisRound >= targetQuotaThisRound;
                var showGoodResult = roundEndPassed && !isPerfectHit;

                if (roundEndAnimationTimer > 0f)
                {
                    roundEndAnimationTimer -= Time.deltaTime;
                    if (roundEndAnimationTimer > 0f)
                    {
                        return;
                    }
                }

                if (!roundEndHitCountTriggered)
                {
                    roundEndHitCountTriggered = true;
                    if (manager.uiController != null)
                    {
                        if (manager.uiController.BeginRoundEndHitAnimation(isPerfectHit))
                        {
                            return;
                        }
                    }
                }

                if (manager.uiController != null && manager.uiController.IsHitCountAnimationActive())
                {
                    return;
                }

                if (!roundEndAudioDelayStarted)
                {
                    roundEndAudioDelayStarted = true;
                    roundEndAudioDelayTimer = Mathf.Max(0f, roundEndAudioDelay);
                }

                if (roundEndAudioDelayTimer > 0f)
                {
                    roundEndAudioDelayTimer -= Time.deltaTime;
                    if (roundEndAudioDelayTimer > 0f)
                    {
                        return;
                    }
                }

                if (!roundEndAudioTriggered)
                {
                    roundEndAudioTriggered = true;
                    if (manager.uiController != null)
                    {
                        manager.uiController.SetGoodActive(showGoodResult);
                        if (!showGoodResult)
                        {
                            manager.uiController.SetGameOverActive(!roundEndPassed);
                        }

                        if (isPerfectHit)
                        {
                            var delay = 0f;
                            var duration = 0f;
                            if (manager.soundManager != null)
                            {
                                delay = Mathf.Max(0f, manager.soundManager.scoreCountAudioDuration);
                                duration = Mathf.Max(0f, manager.soundManager.fullHitAudioDuration);
                            }

                            manager.uiController.ShowPerfectDisplay(delay, duration);
                        }
                        else
                        {
                            manager.uiController.ClearPerfectDisplay();
                        }
                    }
                    if (manager.soundManager != null)
                    {
                        if (roundEndPassed)
                        {
                            manager.soundManager.PlayRoundClearSequence(isPerfectHit);
                        }
                        else
                        {
                            manager.soundManager.PlayRoundFailSequence();
                        }
                    }
                }

                if (manager.soundManager != null && manager.soundManager.IsSequenceActive())
                {
                    return;
                }

                var passedRound = roundEndPassed;
                FinalizeEndRound();
                if (passedRound)
                {
                    HandleRoundPassed();
                    BeginRound();
                }

                return;
            }

            if (!roundActive)
            {
                return;
            }

            if (waitingForStartAnimation)
            {
                if (startAnimationTimer > 0f)
                {
                    startAnimationTimer -= Time.deltaTime;
                }

                manager.animationController.PlayRoundStartMovementSequence();

                if (startAnimationTimer > 0f)
                {
                    return;
                }

                waitingForStartAnimation = false;
                startAnimationTimer = 0f;

                if (manager.animationController != null)
                {
                    manager.animationController.ResetToIdle();
                }
            }

            #region Start Delay

            if (startDelayTime > 0f)
            {
                startDelayTime -= Time.deltaTime;

                return;
            }

            #endregion

            if (spawnTimer > 0f)
            {
                spawnTimer -= Time.deltaTime;

                return;
            }

            if (pigeonsSpawnedThisRound >= targetQuotaThisRound)
            {
                return;
            }

            if (CountActivePigeons() > 0)
            {
                return;
            }

            if (IsPairModeActive())
            {
                TryStartPairSpawn();
            }
            else
            {
                TrySpawnSinglePigeon();
            }
        }

        public void BeginRound()
        {
            if (manager == null)
            {
                return;
            }

            if (gameLocked)
            {
                return;
            }

            if (roundActive)
            {
                return;
            }

            if (manager.PigeonPool == null || manager.PigeonPool.Length == 0)
            {
                return;
            }

            activeGameMode = Mathf.Clamp(manager.gameMode, GameModeSingle, GameModePair);
            roundActive = true;
            roundEndPending = false;
            currentRoundIndex++;
            UpdateDifficultyForCurrentRound();
            ResetRoundRuntimeState();
            hitDifficultyBonus = 0f;
            roundDifficultyBonus = CalculateRoundDifficultyBonus();

            ClampHitDifficultyWithinLimit();

            ResetUIForCurrentRound();

            BeginStartAnimation();
        }

        public void DebugTriggerStartAnimation()
        {
            if (manager == null)
            {
                return;
            }

            BeginStartAnimation();
        }

        public void EndRound()
        {
            if (manager == null)
            {
                return;
            }

            if (!roundActive || roundEndPending)
            {
                return;
            }

            roundEndPending = true;
            roundEndHitCountTriggered = false;
            roundEndAudioTriggered = false;
            roundEndAudioDelayStarted = false;
            roundEndAudioDelayTimer = 0f;
            roundEndAnimationTimer = 0f;
            roundActive = false;
            roundEndPassed = DetermineRoundPass();

            if (manager.animationController != null && lastResolutionHadAnimation)
            {
                roundEndAnimationTimer = Mathf.Max(0f, manager.animationController.GetResolutionAnimationDuration(lastResolutionWasHit));
            }
        }

        private void FinalizeEndRound()
        {
            if (manager == null)
            {
                return;
            }

            if (!roundEndPassed)
            {
                gameLocked = true;
            }

            roundEndPending = false;
            roundActive = false;
            roundEndAudioTriggered = false;
            roundEndAudioDelayStarted = false;
            roundEndAudioDelayTimer = 0f;
            roundEndPassed = false;
            waitingForStartAnimation = false;
            startAnimationTimer = 0f;
            spawnTimer = 0f;
            pairWaveActive = false;
            pairWavePendingResolutions = 0;
            pairWaveHadHit = false;
            pairWaveHadMiss = false;
            ResetWaveTracking();
            if (manager.uiController != null)
            {
                manager.uiController.ClearActivePigeonMask();
            }
            if (pigeonsResolvedThisRound < targetQuotaThisRound)
            {
                pigeonsResolvedThisRound = targetQuotaThisRound;
            }

            DespawnAllPigeons();
        }

        private void HandleRoundPassed()
        {
            if (manager == null || manager.uiController == null)
            {
                return;
            }

            carryScoreValue = manager.uiController.scoreCurrent;
            carryScorePending = true;

            manager.uiController.SetRoundLevel(Mathf.Min(99, GetDisplayedRoundNumber() + 1));
        }

        public void RegisterPigeonHit(PigeonTarget pigeon)
        {
            if (manager == null)
            {
                return;
            }

            if (!roundActive)
            {
                return;
            }

            if (manager.uiController != null)
            {
                var scoreAmount = pigeon != null ? pigeon.GetScoreForCurrentRound() : manager.uiController.scorePerHit;
                manager.uiController.AddScore(scoreAmount);
            }
            streak++;
            pigeonsHitThisRound++;
            ApplyHitDifficultyBonus();
            UpdateHitDisplay();
        }

        public bool TryRegisterShot()
        {
            if (manager == null)
            {
                return false;
            }

            if (!roundActive)
            {
                return false;
            }

            if (!waveActive)
            {
                return false;
            }

            var activePigeonCount = CountActivePigeons();

            if (activePigeonCount <= 0)
            {
                return false;
            }

            var shotLimit = GetCurrentWaveShotLimit();

            if (shotsUsedThisWave >= shotLimit)
            {
                if (shotLimit < int.MaxValue && activePigeonCount > 0)
                {
                    Debug.Log("<color=#FF9A3C>[Gun]</color> Clip empty while pigeons are still flying.");
                }

                return false;
            }

            shotsUsedThisWave++;
            UpdateBulletUsageDisplay();
            if (shotLimit < int.MaxValue && shotsUsedThisWave >= shotLimit && activePigeonCount > 0)
            {
                pendingExitOnMiss = true;
            }

            TryTriggerShotDirectionChange();
            return true;
        }

        private void TryTriggerShotDirectionChange()
        {
            if (manager == null || manager.PigeonPool == null || manager.PigeonPool.Length == 0)
            {
                return;
            }

            var chance = Mathf.Clamp01(manager.shotDirectionChangeChance);
            if (chance <= 0f)
            {
                return;
            }

            for (int i = 0; i < manager.PigeonPool.Length; i++)
            {
                var pigeon = manager.PigeonPool[i];
                if (pigeon == null)
                {
                    continue;
                }

                if (Random.value <= chance)
                {
                    pigeon.TryRandomizeFlightDirection();
                }
            }
        }

        public void NotifyPigeonAvailable(PigeonTarget pigeon, bool wasHit)
        {
            if (manager == null)
            {
                return;
            }

            if (!roundActive)
            {
                return;
            }

            if (IsPairModeActive() && pairWaveActive)
            {
                HandlePairPigeonResolution(wasHit);
            }
            else
            {
                HandleSinglePigeonResolution(wasHit);
            }
        }

        public void NotifyShotOutcome(bool hitPigeon)
        {
            if (!pendingExitOnMiss)
            {
                return;
            }

            pendingExitOnMiss = false;

            if (manager == null || !roundActive)
            {
                return;
            }

            if (hitPigeon)
            {
                return;
            }

            if (CountActivePigeons() <= 0)
            {
                return;
            }

            manager.PlayPigeonExitAnimation(null);
        }

        private void RegisterPigeonMiss()
        {
            streak = 0;
            ReduceHitDifficultyBonus();
        }

        private void HandleSinglePigeonResolution(bool wasHit)
        {
            if (!wasHit)
            {
                RegisterPigeonMiss();
            }

            pigeonsResolvedThisRound++;

            UpdateResolvedPigeonUI(wasHit);

            if (manager.uiController != null)
            {
                manager.uiController.ClearActivePigeonMask();
            }

            TryResetWaveAfterResolution();

            spawnTimer = Mathf.Max(0f, manager.spawnDelay);
            PlayResolutionAnimation(wasHit);

            if (pigeonsResolvedThisRound >= targetQuotaThisRound)
            {
                EndRound();
                return;
            }
        }

        private void HandlePairPigeonResolution(bool wasHit)
        {
            if (!wasHit)
            {
                RegisterPigeonMiss();
                pairWaveHadMiss = true;
            }
            else
            {
                pairWaveHadHit = true;
            }

            pigeonsResolvedThisRound++;
            UpdateResolvedPigeonUI(wasHit);

            pairWavePendingResolutions = Mathf.Max(0, pairWavePendingResolutions - 1);
            if (pairWavePendingResolutions > 0)
            {
                return;
            }

            pairWaveActive = false;

            if (manager.uiController != null)
            {
                manager.uiController.ClearActivePigeonMask();
            }

            TryResetWaveAfterResolution();

            spawnTimer = Mathf.Max(0f, manager.spawnDelay);

            PlayResolutionAnimation(!pairWaveHadMiss);

            if (pigeonsResolvedThisRound >= targetQuotaThisRound)
            {
                EndRound();
                return;
            }

            pairWaveHadHit = false;
            pairWaveHadMiss = false;
        }

        private void UpdateResolvedPigeonUI(bool wasHit)
        {
            if (manager.uiController == null)
            {
                return;
            }

            var resolvedIndex = pigeonsResolvedThisRound - 1;
            if (resolvedIndex >= 0)
            {
                manager.uiController.SetPigeonHitState(resolvedIndex, wasHit);
            }
        }

        private void PlayResolutionAnimation(bool wasHit)
        {
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = wasHit;

            if (manager.animationController == null || spawnTimer <= 0f)
            {
                return;
            }

            lastResolutionHadAnimation = true;
            if (wasHit)
            {
                manager.animationController.PlayHitAnimation();
            }
            else
            {
                manager.animationController.PlayMissAnimation();
            }
        }

        private void TryStartPairSpawn()
        {
            if (!TrySpawnSinglePigeon(out int firstIndex, false, false))
            {
                return;
            }

            if (pigeonsSpawnedThisRound >= targetQuotaThisRound)
            {
                if (manager.uiController != null)
                {
                    manager.uiController.ShowActivePigeonMask(firstIndex);
                }

                return;
            }

            if (!TrySpawnSinglePigeon(out int secondIndex, true, false))
            {
                if (manager.uiController != null)
                {
                    manager.uiController.ShowActivePigeonMask(firstIndex);
                }

                pairWaveActive = false;
                pairWavePendingResolutions = 0;

                return;
            }

            BeginPairWave(firstIndex, secondIndex);
        }

        private void BeginPairWave(int firstIndex, int secondIndex)
        {
            Debug.Log("BeginPairWave");

            pairWaveActive = true;
            pairWavePendingResolutions = 2;
            pairWaveHadHit = false;
            pairWaveHadMiss = false;

            if (manager.uiController != null)
            {
                manager.uiController.ShowActivePigeonMaskPair(firstIndex, secondIndex);
            }
        }

        private bool IsPairModeActive()
        {
            return activeGameMode == GameModePair;
        }

        private bool TrySpawnSinglePigeon(bool ignoreActiveSlots = false)
        {
            int spawnIndex;

            return TrySpawnSinglePigeon(out spawnIndex, ignoreActiveSlots);
        }

        private bool TrySpawnSinglePigeon(out int spawnIndex, bool ignoreActiveSlots = false, bool updateUiMask = true)
        {
            spawnIndex = -1;

            if (manager.PigeonPool == null || manager.PigeonPool.Length == 0)
            {
                return false;
            }

            if (pigeonsSpawnedThisRound >= targetQuotaThisRound)
            {
                return false;
            }

            if (!ignoreActiveSlots && CountActivePigeons() > 0)
            {
                return false;
            }

            var pigeon = GetAvailablePigeon();

            if (pigeon == null)
            {
                spawnTimer = 0.25f;

                return false;
            }

            Vector3 startPosition;
            int directionIndex;
            if (!TryBuildSpawnParameters(out startPosition, out directionIndex))
            {
                spawnTimer = 0.25f;

                return false;
            }

            pigeon.SetPlayArea(manager.playArea);
            spawnIndex = pigeonsSpawnedThisRound;
            pigeon.BeginFlight(startPosition, directionIndex, 0f, DifficultyMultiplier);
            pigeonsSpawnedThisRound++;
            BeginWaveIfNeeded();
            LogPigeonSpawnWave(spawnIndex);

            if (manager.uiController != null && updateUiMask)
            {
                manager.uiController.ShowActivePigeonMask(spawnIndex);
            }

            return true;
        }

        private bool TryBuildSpawnParameters(out Vector3 startPosition, out int directionIndex)
        {
            startPosition = Vector3.zero;
            directionIndex = DirectionRight;

            if (manager.playArea == null)
            {
                return false;
            }

            if (!QychuiUtilities.TryGetRectWorldBounds(manager.playArea, cachedCorners, out float minX, out float maxX, out float minY, out float maxY, out float planeZ))
            {
                return false;
            }

            int edgeCount = 0;
            if (manager.allowLeftEdge)
            {
                edgeSelectionBuffer[edgeCount] = EdgeLeft;
                edgeCount++;
            }
            if (manager.allowRightEdge)
            {
                edgeSelectionBuffer[edgeCount] = EdgeRight;
                edgeCount++;
            }
            if (manager.allowTopEdge)
            {
                edgeSelectionBuffer[edgeCount] = EdgeTop;
                edgeCount++;
            }
            if (manager.allowBottomEdge)
            {
                edgeSelectionBuffer[edgeCount] = EdgeBottom;
                edgeCount++;
            }

            if (edgeCount == 0)
            {
                return false;
            }

            int selectedEdge = edgeSelectionBuffer[Random.Range(0, edgeCount)];

            if (selectedEdge == EdgeLeft)
            {
                startPosition.x = minX;
                startPosition.y = SampleWithin(minY, maxY, manager.leftEdgeSpawnSegment);
                directionIndex = SampleLeftEdgeDirection();
            }
            else if (selectedEdge == EdgeRight)
            {
                startPosition.x = maxX;
                startPosition.y = SampleWithin(minY, maxY, manager.rightEdgeSpawnSegment);
                directionIndex = SampleRightEdgeDirection();
            }
            else if (selectedEdge == EdgeTop)
            {
                startPosition.y = maxY;
                startPosition.x = SampleWithin(minX, maxX, manager.topEdgeSpawnSegment);
                directionIndex = SampleTopEdgeDirection();
            }
            else
            {
                startPosition.y = minY;
                startPosition.x = SampleWithin(minX, maxX, manager.bottomEdgeSpawnSegment);
                directionIndex = SampleBottomEdgeDirection();
            }

            startPosition.z = planeZ;

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

        private void LogPigeonSpawnWave(int spawnIndex)
        {
            var displayIndex = Mathf.Max(1, spawnIndex + 1);
            var waveType = IsPairModeActive() ? "Pair" : "Single";
            Debug.Log(
                $"<color=#32C8FF>[Pigeon Wave]</color> {waveType} wave {displayIndex}/{Mathf.Max(1, targetQuotaThisRound)} started.");
        }

        private void BeginWaveIfNeeded()
        {
            if (waveActive)
            {
                return;
            }

            waveActive = true;
            shotsUsedThisWave = 0;
            ConfigureWaveBulletDisplay();
        }

        private void ConfigureWaveBulletDisplay()
        {
            if (manager.uiController == null)
            {
                return;
            }

            if (manager.uiController.weaponType == 0)
            {
                var clipSize = Mathf.Min(WeaponZeroClipSize, GetBulletIconCapacity());
                manager.uiController.ConfigureBulletClip(clipSize);
            }

            manager.uiController.ResetBulletClipUsage();
        }

        private void UpdateBulletUsageDisplay()
        {
            if (manager.uiController == null)
            {
                return;
            }

            if (manager.uiController.weaponType == 0)
            {
                manager.uiController.SetBulletUsage(shotsUsedThisWave);
            }
        }

        private int GetBulletIconCapacity()
        {
            if (manager.uiController == null || manager.uiController.bulletObject == null || manager.uiController.bulletObject.Length == 0)
            {
                return WeaponZeroClipSize;
            }

            return Mathf.Max(1, Mathf.Min(WeaponZeroClipSize, manager.uiController.bulletObject.Length));
        }

        private int GetCurrentWaveShotLimit()
        {
            if (manager.uiController != null && manager.uiController.weaponType == 0)
            {
                return Mathf.Min(WeaponZeroClipSize, GetBulletIconCapacity());
            }

            return int.MaxValue;
        }

        private void TryResetWaveAfterResolution()
        {
            if (!waveActive)
            {
                return;
            }

            if (CountActivePigeons() > 0)
            {
                return;
            }

            ResetWaveTracking();
        }

        private void ResetWaveTracking()
        {
            waveActive = false;
            shotsUsedThisWave = 0;
            pendingExitOnMiss = false;

            if (manager.uiController != null)
            {
                manager.uiController.ResetBulletClipUsage();
            }
        }

        private int SampleLeftEdgeDirection()
        {
            if (Random.Range(0, 2) == 0)
            {
                return DirectionRightUp;
            }

            return DirectionRightDown;
        }

        private int SampleRightEdgeDirection()
        {
            if (Random.Range(0, 2) == 0)
            {
                return DirectionLeftUp;
            }

            return DirectionLeftDown;
        }

        private int SampleTopEdgeDirection()
        {
            if (Random.Range(0, 2) == 0)
            {
                return DirectionLeftDown;
            }

            return DirectionRightDown;
        }

        private int SampleBottomEdgeDirection()
        {
            if (Random.Range(0, 2) == 0)
            {
                return DirectionLeftUp;
            }

            return DirectionRightUp;
        }

        private PigeonTarget GetAvailablePigeon()
        {
            if (manager.PigeonPool == null)
            {
                return null;
            }

            for (int i = 0; i < manager.PigeonPool.Length; i++)
            {
                var candidate = manager.PigeonPool[i];
                if (candidate != null && candidate.IsAvailable)
                {
                    return candidate;
                }
            }

            return null;
        }

        private int CountActivePigeons()
        {
            if (manager.PigeonPool == null)
            {
                return 0;
            }

            var count = 0;
            for (int i = 0; i < manager.PigeonPool.Length; i++)
            {
                var pigeon = manager.PigeonPool[i];
                if (pigeon != null && pigeon.OccupiesSlot)
                {
                    count++;
                }
            }

            return count;
        }

        private void InitializePool()
        {
            if (manager.PigeonPool == null)
            {
                return;
            }

            for (int i = 0; i < manager.PigeonPool.Length; i++)
            {
                var pigeon = manager.PigeonPool[i];
                if (pigeon == null)
                {
                    continue;
                }

                if (manager != null)
                {
                    pigeon.SetManager(manager);
                }
                pigeon.SetPlayArea(manager.playArea);
                pigeon.DespawnImmediate();
            }
        }

        private void ResetRoundState()
        {
            currentRoundIndex = 0;
            roundDifficultyBonus = 0f;
            hitDifficultyBonus = 0f;
            activeGameMode = GameModeSingle;
            currentDifficultyLevel = 0;
            gameLocked = false;
            carryScorePending = false;
            carryScoreValue = 0;
            roundEndPending = false;
            roundEndHitCountTriggered = false;
            roundEndAudioTriggered = false;
            roundEndAudioDelayStarted = false;
            roundEndAudioDelayTimer = 0f;
            roundEndPassed = false;
            roundEndAnimationTimer = 0f;
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = false;
            ResetRoundRuntimeState();

            DespawnAllPigeons();
            ResetUIForCurrentRound();
        }

        private void ResetRoundRuntimeState()
        {
            targetQuotaThisRound = Mathf.Max(1, manager.pigeonsPerRound);
            pigeonsSpawnedThisRound = 0;
            pigeonsResolvedThisRound = 0;
            pigeonsHitThisRound = 0;
            spawnTimer = 0f;
            streak = 0;
            pairWaveActive = false;
            pairWavePendingResolutions = 0;
            pairWaveHadHit = false;
            pairWaveHadMiss = false;
            roundEndPending = false;
            roundEndHitCountTriggered = false;
            roundEndAudioTriggered = false;
            roundEndAudioDelayStarted = false;
            roundEndAudioDelayTimer = 0f;
            roundEndPassed = false;
            roundEndAnimationTimer = 0f;
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = false;
            ResetWaveTracking();
        }

        private void DespawnAllPigeons()
        {
            if (manager.PigeonPool == null)
            {
                return;
            }

            for (int i = 0; i < manager.PigeonPool.Length; i++)
            {
                var pigeon = manager.PigeonPool[i];
                if (pigeon != null)
                {
                    pigeon.DespawnImmediate();
                }
            }
        }

        private float CalculateRoundDifficultyBonus()
        {
            var maxBonus = Mathf.Max(0f, manager.maxDifficultyMultiplier - 1f);
            var desired = Mathf.Max(0f, (currentRoundIndex - 1) * manager.roundDifficultyStep);
            return Mathf.Min(desired, maxBonus);
        }

        private void UpdateDifficultyForCurrentRound()
        {
            currentDifficultyLevel = GetDifficultyLevelForRound(currentRoundIndex);
        }

        private int GetDifficultyLevelForRound(int roundIndex)
        {
            if (roundIndex <= 0)
            {
                return 0;
            }

            return Mathf.Max(0, (roundIndex - 1) / 2);
        }

        private int GetDisplayedDifficultyLevel()
        {
            return Mathf.Min(currentDifficultyLevel, MaxDifficultyLevel);
        }

        private bool DetermineRoundPass()
        {
            var requiredHits = GetRequiredHitsForDifficulty(GetDisplayedDifficultyLevel(), targetQuotaThisRound);
            return pigeonsHitThisRound >= requiredHits;
        }

        private int GetRequiredHitsForDifficulty(int difficultyLevel, int quota)
        {
            var effective = Mathf.Clamp(difficultyLevel, 0, MaxDifficultyLevel);
            var required = 6 + effective;
            if (quota > 0)
            {
                required = Mathf.Min(required, quota);
            }

            return Mathf.Max(0, required);
        }

        private void ApplyHitDifficultyBonus()
        {
            var capacity = GetAvailableHitBonusCapacity();
            if (capacity <= 0f)
            {
                return;
            }

            hitDifficultyBonus = Mathf.Min(hitDifficultyBonus + manager.hitDifficultyStep, capacity);
        }

        private void ReduceHitDifficultyBonus()
        {
            hitDifficultyBonus = Mathf.Max(0f, hitDifficultyBonus - manager.missDifficultyPenalty);
        }

        private float GetAvailableHitBonusCapacity()
        {
            var maxBonus = Mathf.Max(0f, manager.maxDifficultyMultiplier - 1f);
            return Mathf.Max(0f, maxBonus - roundDifficultyBonus);
        }

        private void ClampHitDifficultyWithinLimit()
        {
            var capacity = GetAvailableHitBonusCapacity();
            if (hitDifficultyBonus > capacity)
            {
                hitDifficultyBonus = capacity;
            }
        }

        private int GetDisplayedRoundNumber()
        {
            return Mathf.Max(1, currentRoundIndex);
        }

        private void ResetUIForCurrentRound()
        {
            if (manager.uiController == null)
            {
                return;
            }

            var scoreValue = carryScorePending ? carryScoreValue : 0;
            carryScorePending = false;
            carryScoreValue = 0;
            manager.uiController.SetScoreValue(scoreValue);
            manager.uiController.ResetBulletClipUsage();
            manager.uiController.SetDifficultyLevel(GetDisplayedDifficultyLevel());
            manager.uiController.SetRoundLevel(GetDisplayedRoundNumber());
            manager.uiController.ClearPigeonHitIndicators();
            manager.uiController.SetGoodActive(false);
            manager.uiController.ClearPerfectDisplay();
            manager.uiController.ShowRoundDisplay();
        }

        private void UpdateHitDisplay()
        {
            if (manager.uiController != null)
            {
                manager.uiController.SetHitCount(pigeonsHitThisRound);
            }
        }

        private void BeginStartAnimation()
        {
            if (manager == null || manager.animationController == null)
            {
                return;
            }

            var startDelay = manager.animationController.GameStartDuration;
            startAnimationTimer = Mathf.Max(0f, startDelay);

            startDelayTime = manager.animationController.GameStartDelay;
            if (startDelayTime > 0f)
            {
                manager.animationController.RestLayerOrder();
            }

            waitingForStartAnimation = startAnimationTimer > 0f;
            manager.animationController.PlayGameStartAnimation();
        }
    }
}



