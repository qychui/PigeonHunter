using UdonSharp;
using UnityEngine;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ActionController : UdonSharpBehaviour
    {
        #region Runtime State And Public Properties

        private GameManager manager;

        private bool roundActive;
        private int currentRoundIndex;
        private int targetQuotaThisRound;
        private int pigeonsSpawnedThisRound;
        private int pigeonsResolvedThisRound;
        private int pigeonsHitThisRound;
        private float spawnTimer;
        private float roundDifficultyBonus;
        private bool waitingForStartAnimation;
        private bool startMovementSequenceTriggered;
        private float startAnimationTimer = 0f;
        private float startDelayTime = 0f;
        private int activeGameMode = GameModeSingle;
        private bool pairWaveActive;
        private int pairWavePendingResolutions;
        private bool pairWaveHadHit;
        private bool pairWaveHadMiss;
        private int pairWaveHitCount;
        private bool pairWaveSecondSpawnPending;
        private float pairWaveSecondSpawnTimer;
        private int pairWaveFirstSpawnIndex;
        private PigeonColorType pairWaveFirstHitColor;
        private PigeonColorType pairWaveSecondHitColor;
        private PigeonTarget pairWaveFirstPigeon;
        private PigeonTarget pairWaveSecondPigeon;
        private int pairWaveFirstUiIndex = -1;
        private int pairWaveSecondUiIndex = -1;
        private Vector3 pairWaveLastHitPosition;
        private bool pairWaveHasLastHitPosition;
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
        private bool roundEndLmaoAnimPending;
        private float roundEndLmaoAnimTimer;
        [Min(0f)]
        [SerializeField] private float syncedRoundResultGraceTime = 0.75f;
        [Min(0f)]
        [SerializeField] private float syncedRoundResultEscapeGraceTime = 0.65f;
        private bool pendingSyncedRoundResult;
        private int pendingSyncedRoundNumber;
        private int pendingSyncedRoundScore;
        private int pendingSyncedRoundHitCount;
        private bool pendingSyncedRoundPassed;
        private float pendingSyncedRoundResultTimer;
        private bool pendingSyncedRoundResultEscapeRequested;
        private int appliedMode1RoundResultRound;
        private int appliedMode1RoundResultScore;
        private int appliedMode1RoundResultHitCount;
        private bool appliedMode1RoundResultPassed;
        private bool appliedMode1RoundResultActive;
        private bool lastResolutionHadAnimation;
        private bool lastResolutionWasHit;
        private PigeonTarget lastHitPigeon;
        private int currentDifficultyLevel;
        private bool gameLocked;
        private bool carryScorePending;
        private int carryScoreValue;
        private int mode1PlanRoundNumber;
        private int mode1PlanSeed;
        private bool mode1PlanActive;
        private int mode1LocalStartGateRoundNumber;
        private int mode1LocalStartGateSeed;
        private bool mode1LocalStartGateSent;
        private bool ownerRoundResultSyncAttempted;

        private const int DirectionRight = 0;
        private const int DirectionRightUp = 1;
        private const int DirectionRightDown = 2;
        private const int DirectionLeft = 3;
        private const int DirectionLeftUp = 4;
        private const int DirectionLeftDown = 5;

        private const int GameModeSingle = 1;
        private const int GameModePair = 2;

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

        public bool HasStartedGameSession
        {
            get { return currentRoundIndex > 0; }
        }

        public Vector3 GetRoundEndPigeonPosition()
        {
            return GetResolvedPigeonPosition(lastHitPigeon);
        }

        public float DifficultyMultiplier
        {
            get
            {
                float multiplier = 1f + Mathf.Max(0f, roundDifficultyBonus);
                if (manager != null && manager.maxDifficultyMultiplier > 0f)
                {
                    multiplier = Mathf.Min(multiplier, manager.maxDifficultyMultiplier);
                }

                return multiplier;
            }
        }

        #endregion

        #region Initialization And Main Tick

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

            TickPendingSyncedRoundResult();

            if (roundEndPending)
            {
                EnsureMode1RoundResultSyncedIfOwner();

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
                        if (isPerfectHit && !roundEndAudioTriggered)
                        {
                            roundEndAudioTriggered = true;
                            manager.uiController.SetGoodActive(false);

                            if (manager.soundManager != null)
                            {
                                var delay = Mathf.Max(0f, manager.soundManager.scoreCountAudioDuration);
                                var duration = Mathf.Max(0f, manager.soundManager.fullHitAudioDuration);
                                manager.uiController.ShowPerfectDisplay(delay, duration);
                                manager.soundManager.PlayRoundClearSequence(true);
                                roundEndLmaoAnimPending = false;
                                roundEndLmaoAnimTimer = 0f;
                            }
                            else
                            {
                                manager.uiController.ShowPerfectDisplay(0f, 0f);
                                roundEndLmaoAnimPending = false;
                                roundEndLmaoAnimTimer = 0f;
                            }
                        }

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
                            roundEndLmaoAnimPending = false;
                            roundEndLmaoAnimTimer = 0f;
                        }
                        else
                        {
                            if (manager.uiController != null)
                            {
                                manager.uiController.RefreshTopScoreOnGameOver();
                                if (manager.uiController.gameOverTextObject != null && !manager.uiController.gameOverTextObject.activeSelf)
                                {
                                    manager.uiController.gameOverTextObject.SetActive(true);
                                }
                            }

                            manager.soundManager.PlayRoundFailSequence();
                            roundEndLmaoAnimPending = manager.animationController != null;
                            roundEndLmaoAnimTimer = Mathf.Max(0f, manager.soundManager.endAudioDuration);
                        }
                    }
                    else
                    {
                        roundEndLmaoAnimPending = false;
                        roundEndLmaoAnimTimer = 0f;
                    }
                }

                if (roundEndLmaoAnimPending)
                {
                    if (roundEndLmaoAnimTimer > 0f)
                    {
                        roundEndLmaoAnimTimer -= Time.deltaTime;
                    }

                    if (roundEndLmaoAnimTimer <= 0f)
                    {
                        roundEndLmaoAnimPending = false;
                        if (manager.animationController != null)
                        {
                            manager.animationController.SetUseVRCDogForNextEndRoundLmao(ShouldUseVRCDogForEndRoundLmao());
                            manager.animationController.PlayEndRoundLmaoMovement();
                        }
                    }
                }

                if (manager.soundManager != null && manager.soundManager.IsSequenceActive())
                {
                    return;
                }

                if (!roundEndPassed && manager.uiController != null)
                {
                    manager.uiController.RefreshTopScoreOnGameOver();
                    manager.uiController.SetGameOverActive(true);
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

                if (!startMovementSequenceTriggered)
                {
                    startMovementSequenceTriggered = true;
                    if (currentRoundIndex > 1)
                    {
                        manager.animationController.PlayRoundNextMovementSequence();
                    }
                    else
                    {
                        manager.animationController.PlayRoundStartMovementSequence();
                    }
                }

                if (startAnimationTimer > 0f)
                {
                    return;
                }

                waitingForStartAnimation = false;
                startMovementSequenceTriggered = false;
                startAnimationTimer = 0f;

                if (manager.animationController != null)
                {
                    manager.animationController.ResetToIdle();
                }
            }

            if (ShouldWaitForSyncedRoundPlan())
            {
                return;
            }

            if (startDelayTime > 0f)
            {
                startDelayTime -= Time.deltaTime;

                return;
            }

            if (spawnTimer > 0f)
            {
                spawnTimer -= Time.deltaTime;

                return;
            }

            if (ShouldWaitForSyncedOwnerStartGate())
            {
                return;
            }

            if (IsPairModeActive() && pairWaveSecondSpawnPending)
            {
                TickPendingPairSecondSpawn();
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

        #endregion

        #region Round Flow Settlement And Synced Results

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

            if (manager.pigeonPool == null || manager.pigeonPool.Length == 0)
            {
                return;
            }

            activeGameMode = Mathf.Clamp(manager.gameMode, GameModeSingle, GameModePair);
            roundActive = true;
            roundEndPending = false;
            currentRoundIndex++;
            UpdateDifficultyForCurrentRound();
            ResetRoundRuntimeState();
            roundDifficultyBonus = CalculateRoundDifficultyBonus();
            EnsureMode1RoundPlanForCurrentRound();

            ResetUIForCurrentRound();

            BeginStartAnimation();
        }

        public void ApplyMode1RoundPlan(int roundNumber, int seed)
        {
            if (roundNumber <= 0)
            {
                return;
            }

            mode1PlanRoundNumber = roundNumber;
            mode1PlanSeed = seed;
            mode1PlanActive = true;
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
            roundEndLmaoAnimPending = false;
            roundEndLmaoAnimTimer = 0f;
            roundActive = false;
            roundEndPassed = DetermineRoundPass();
            ownerRoundResultSyncAttempted = false;
            SyncMode1RoundResultIfOwner();

            if (manager.animationController != null && lastResolutionHadAnimation)
            {
                roundEndAnimationTimer = Mathf.Max(0f, manager.animationController.GetResolutionAnimationDuration(lastResolutionWasHit));
            }
        }

        public void ApplySyncedMode1RoundResult(int roundNumber, int score, int hitCount, bool passed)
        {
            if (manager == null)
            {
                return;
            }

            if (IsDuplicateAppliedMode1RoundResult(roundNumber, score, hitCount, passed))
            {
                return;
            }

            if (!IsNetworkedPigeonMode() && manager.gameMode != GameModeSingle && manager.gameMode != GameModePair)
            {
                return;
            }

            if (IsAlreadyWithinSyncedMode1RoundResult(roundNumber, score, hitCount, passed))
            {
                return;
            }

            if (roundEndPending && currentRoundIndex == Mathf.Max(1, roundNumber))
            {
                QueueSyncedMode1RoundResult(roundNumber, score, hitCount, passed);
                ApplyPendingSyncedRoundResultToCurrentSettlement();
                ClearPendingSyncedRoundResult();
                return;
            }

            if (CanDeferSyncedMode1RoundResult(roundNumber))
            {
                QueueSyncedMode1RoundResult(roundNumber, score, hitCount, passed);
                return;
            }

            ClearPendingSyncedRoundResult();
            ApplySyncedMode1RoundResultImmediate(roundNumber, score, hitCount, passed);
        }

        private void ApplySyncedMode1RoundResultImmediate(int roundNumber, int score, int hitCount, bool passed)
        {
            currentRoundIndex = Mathf.Max(1, roundNumber);
            targetQuotaThisRound = GetTargetQuotaForCurrentMode();
            pigeonsHitThisRound = Mathf.Clamp(hitCount, 0, targetQuotaThisRound);
            pigeonsResolvedThisRound = targetQuotaThisRound;
            roundEndPassed = passed;
            MarkAppliedMode1RoundResult(roundNumber, score, hitCount, passed);
            PrepareSyncedRoundSettlementState(passed);
            roundEndAnimationTimer = 0f;
            DespawnAllPigeons();
            ApplySyncedRoundResultUi(score, true);
        }

        private bool CanDeferSyncedMode1RoundResult(int roundNumber)
        {
            var expectedRound = Mathf.Max(1, roundNumber);
            if (!roundActive || roundEndPending || currentRoundIndex != expectedRound)
            {
                return false;
            }

            if (pigeonsResolvedThisRound >= targetQuotaThisRound)
            {
                return false;
            }

            return CountActivePigeons() > 0 || spawnTimer > 0f || lastResolutionHadAnimation;
        }

        private void QueueSyncedMode1RoundResult(int roundNumber, int score, int hitCount, bool passed)
        {
            pendingSyncedRoundResult = true;
            pendingSyncedRoundNumber = Mathf.Max(1, roundNumber);
            pendingSyncedRoundScore = Mathf.Max(0, score);
            pendingSyncedRoundHitCount = Mathf.Max(0, hitCount);
            pendingSyncedRoundPassed = passed;
            pendingSyncedRoundResultTimer = Mathf.Max(0f, syncedRoundResultGraceTime);
            pendingSyncedRoundResultEscapeRequested = false;
        }

        private void TickPendingSyncedRoundResult()
        {
            if (!pendingSyncedRoundResult)
            {
                return;
            }

            if (roundEndPending)
            {
                ApplyPendingSyncedRoundResultToCurrentSettlement();
                ClearPendingSyncedRoundResult();
                return;
            }

            if (!roundActive || currentRoundIndex != pendingSyncedRoundNumber)
            {
                ApplySyncedMode1RoundResultImmediate(
                    pendingSyncedRoundNumber,
                    pendingSyncedRoundScore,
                    pendingSyncedRoundHitCount,
                    pendingSyncedRoundPassed);
                ClearPendingSyncedRoundResult();
                return;
            }

            if (pigeonsResolvedThisRound >= targetQuotaThisRound)
            {
                StartSyncedRoundSettlementFromPending(true);
                ClearPendingSyncedRoundResult();
                return;
            }

            var activePigeons = CountActivePigeons();
            if (activePigeons > 0 && !pendingSyncedRoundResultEscapeRequested)
            {
                pendingSyncedRoundResultEscapeRequested = true;
                pendingSyncedRoundResultTimer = Mathf.Max(pendingSyncedRoundResultTimer, syncedRoundResultEscapeGraceTime);
                BeginNaturalEscapeForActivePigeons();
            }

            if (pendingSyncedRoundResultTimer > 0f)
            {
                pendingSyncedRoundResultTimer -= Time.deltaTime;
                if (pendingSyncedRoundResultTimer > 0f)
                {
                    return;
                }
            }

            StartSyncedRoundSettlementFromPending(false);
            ClearPendingSyncedRoundResult();
        }

        private void ApplyPendingSyncedRoundResultToCurrentSettlement()
        {
            targetQuotaThisRound = GetTargetQuotaForCurrentMode();
            pigeonsHitThisRound = Mathf.Clamp(pendingSyncedRoundHitCount, 0, targetQuotaThisRound);
            pigeonsResolvedThisRound = Mathf.Max(pigeonsResolvedThisRound, targetQuotaThisRound);
            roundEndPassed = pendingSyncedRoundPassed;
            MarkAppliedMode1RoundResult(
                pendingSyncedRoundNumber,
                pendingSyncedRoundScore,
                pendingSyncedRoundHitCount,
                pendingSyncedRoundPassed);

            ApplySyncedRoundResultUi(pendingSyncedRoundScore, false);
        }

        private bool IsDuplicateAppliedMode1RoundResult(int roundNumber, int score, int hitCount, bool passed)
        {
            return appliedMode1RoundResultActive &&
                   appliedMode1RoundResultRound == Mathf.Max(1, roundNumber) &&
                   appliedMode1RoundResultScore == Mathf.Max(0, score) &&
                   appliedMode1RoundResultHitCount == Mathf.Max(0, hitCount) &&
                   appliedMode1RoundResultPassed == passed;
        }

        private void MarkAppliedMode1RoundResult(int roundNumber, int score, int hitCount, bool passed)
        {
            appliedMode1RoundResultActive = true;
            appliedMode1RoundResultRound = Mathf.Max(1, roundNumber);
            appliedMode1RoundResultScore = Mathf.Max(0, score);
            appliedMode1RoundResultHitCount = Mathf.Max(0, hitCount);
            appliedMode1RoundResultPassed = passed;
        }

        private void ApplySyncedRoundResultUi(int score, bool resetResultUi)
        {
            if (manager.uiController == null)
            {
                return;
            }

            manager.uiController.SetScoreValue(score);
            manager.uiController.SetPigeonQuota(targetQuotaThisRound);
            manager.uiController.SetHitCount(pigeonsHitThisRound);

            if (!resetResultUi)
            {
                return;
            }

            manager.uiController.ClearActivePigeonMask();
            manager.uiController.SetGoodActive(false);
            manager.uiController.ClearPerfectDisplay();
            manager.uiController.SetGameOverActive(false);
        }

        private void StartSyncedRoundSettlementFromPending(bool keepResolutionAnimationDelay)
        {
            ApplyPendingSyncedRoundResultToCurrentSettlement();
            PrepareSyncedRoundSettlementState(pendingSyncedRoundPassed);

            if (keepResolutionAnimationDelay && manager.animationController != null && lastResolutionHadAnimation)
            {
                roundEndAnimationTimer = Mathf.Max(0f, manager.animationController.GetResolutionAnimationDuration(lastResolutionWasHit));
            }
            else
            {
                roundEndAnimationTimer = 0f;
            }
        }

        private void PrepareSyncedRoundSettlementState(bool passed)
        {
            roundEndPending = true;
            roundEndHitCountTriggered = false;
            roundEndAudioTriggered = false;
            roundEndAudioDelayStarted = false;
            roundEndAudioDelayTimer = 0f;
            roundEndLmaoAnimPending = false;
            roundEndLmaoAnimTimer = 0f;
            roundActive = false;
            waitingForStartAnimation = false;
            startMovementSequenceTriggered = false;
            startAnimationTimer = 0f;
            spawnTimer = 0f;
            pairWaveActive = false;
            pairWavePendingResolutions = 0;
            pairWaveSecondSpawnPending = false;
            pairWaveSecondSpawnTimer = 0f;
            gameLocked = !passed;
            ResetWaveTracking();
        }

        private void ClearPendingSyncedRoundResult()
        {
            pendingSyncedRoundResult = false;
            pendingSyncedRoundNumber = 0;
            pendingSyncedRoundScore = 0;
            pendingSyncedRoundHitCount = 0;
            pendingSyncedRoundPassed = false;
            pendingSyncedRoundResultTimer = 0f;
            pendingSyncedRoundResultEscapeRequested = false;
        }

        private bool IsAlreadyWithinSyncedMode1RoundResult(int roundNumber, int score, int hitCount, bool passed)
        {
            if (manager == null || manager.uiController == null)
            {
                return false;
            }

            var expectedRound = Mathf.Max(1, roundNumber);
            var expectedScore = Mathf.Max(0, score);
            var expectedHits = Mathf.Max(0, hitCount);
            var localScoreMatches = manager.uiController.scoreCurrent == expectedScore;
            var localHitsMatch = pigeonsHitThisRound == expectedHits;

            if (!localScoreMatches || !localHitsMatch)
            {
                return false;
            }

            if (passed)
            {
                // Either already finishing the owner-approved round, or already moved to the next one.
                return (currentRoundIndex == expectedRound && roundEndPending && roundEndPassed == passed) ||
                       currentRoundIndex == expectedRound + 1;
            }

            return currentRoundIndex == expectedRound && gameLocked && roundEndPassed == passed;
        }

        public void ForcePassCurrentRound()
        {
            if (!CanForceSettleRound())
            {
                return;
            }

            var requiredHits = GetRequiredHitsForDifficulty(GetDisplayedDifficultyLevel(), targetQuotaThisRound);
            pigeonsHitThisRound = Mathf.Max(pigeonsHitThisRound, requiredHits);
            UpdateHitDisplay();
            PrepareForcedRoundSettlement();
            EndRound();
        }

        public void ForceSettleCurrentRound()
        {
            if (!CanForceSettleRound())
            {
                return;
            }

            PrepareForcedRoundSettlement();
            EndRound();
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
            roundEndLmaoAnimPending = false;
            roundEndLmaoAnimTimer = 0f;
            waitingForStartAnimation = false;
            startMovementSequenceTriggered = false;
            startAnimationTimer = 0f;
            spawnTimer = 0f;
            pairWaveActive = false;
            pairWavePendingResolutions = 0;
            pairWaveHadHit = false;
            pairWaveHadMiss = false;
            pairWaveHitCount = 0;
            pairWaveSecondSpawnPending = false;
            pairWaveSecondSpawnTimer = 0f;
            pairWaveFirstSpawnIndex = -1;
            pairWaveFirstHitColor = PigeonColorType.Black;
            pairWaveSecondHitColor = PigeonColorType.Black;
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

        private void SyncMode1RoundResultIfOwner()
        {
            if (manager == null ||
                !IsNetworkedPigeonMode() ||
                !IsMode1SyncOwner())
            {
                return;
            }

            var score = manager.uiController != null ? manager.uiController.scoreCurrent : 0;
            var roundNumber = GetDisplayedRoundNumber();
            ownerRoundResultSyncAttempted = true;
            manager.SyncMode1RoundResult(roundNumber, score, pigeonsHitThisRound, roundEndPassed);
            MarkAppliedMode1RoundResult(roundNumber, score, pigeonsHitThisRound, roundEndPassed);
        }

        private void EnsureMode1RoundResultSyncedIfOwner()
        {
            if (ownerRoundResultSyncAttempted)
            {
                return;
            }

            if (!IsMode1SyncOwner())
            {
                return;
            }

            ownerRoundResultSyncAttempted = true;
            SyncMode1RoundResultIfOwner();
        }

        private bool CanForceSettleRound()
        {
            if (manager == null)
            {
                return false;
            }

            if (!roundActive || roundEndPending || gameLocked)
            {
                return false;
            }

            return true;
        }

        private void PrepareForcedRoundSettlement()
        {
            pigeonsResolvedThisRound = Mathf.Max(pigeonsResolvedThisRound, targetQuotaThisRound);
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = false;

            if (manager.uiController != null)
            {
                manager.uiController.ClearActivePigeonMask();
            }

            DespawnAllPigeons();
        }

        #endregion

        #region Hit Shot And Resolution Flow

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
            lastHitPigeon = pigeon;
            pigeonsHitThisRound++;
            UpdateHitDisplay();
        }

        public void RegisterSyncedPigeonHit(PigeonTarget pigeon)
        {
            RegisterSyncedPigeonHit(pigeon, true);
        }

        public void RegisterSyncedPigeonHit(PigeonTarget pigeon, bool simulateShotUsage)
        {
            if (manager == null)
            {
                return;
            }

            if (!roundActive)
            {
                return;
            }

            if (simulateShotUsage)
            {
                SimulateSyncedShotUsage();
            }

            RegisterPigeonHit(pigeon);
        }

        public void ApplySyncedShotUsage(int usedShots)
        {
            var clampedUsedShots = Mathf.Max(0, usedShots);
            var previousShotsUsed = shotsUsedThisWave;
            if (waveActive && clampedUsedShots > previousShotsUsed)
            {
                for (int shotIndex = previousShotsUsed + 1; shotIndex <= clampedUsedShots; shotIndex++)
                {
                    shotsUsedThisWave = shotIndex;
                    TryTriggerShotDirectionChangeForShot(shotIndex);
                }
            }
            else
            {
                shotsUsedThisWave = clampedUsedShots;
            }

            UpdateBulletUsageDisplay();
            var shotLimit = GetCurrentWaveShotLimit();
            pendingExitOnMiss = waveActive &&
                                shotLimit < int.MaxValue &&
                                shotsUsedThisWave >= shotLimit &&
                                CountActivePigeons() > 0;
        }

        public int GetShotsUsedThisWave()
        {
            return Mathf.Max(0, shotsUsedThisWave);
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

        private void SimulateSyncedShotUsage()
        {
            if (!waveActive)
            {
                return;
            }

            var shotLimit = GetCurrentWaveShotLimit();
            if (shotsUsedThisWave >= shotLimit)
            {
                return;
            }

            shotsUsedThisWave++;
            UpdateBulletUsageDisplay();
            TryTriggerShotDirectionChange();
        }

        private void TryTriggerShotDirectionChange()
        {
            TryTriggerShotDirectionChangeForShot(shotsUsedThisWave);
        }

        private void TryTriggerShotDirectionChangeForShot(int shotIndex)
        {
            if (manager == null || manager.pigeonPool == null || manager.pigeonPool.Length == 0)
            {
                return;
            }

            var chance = Mathf.Clamp01(manager.shotDirectionChangeChance);
            if (chance <= 0f)
            {
                return;
            }

            for (int i = 0; i < manager.pigeonPool.Length; i++)
            {
                var pigeon = manager.pigeonPool[i];
                if (pigeon == null)
                {
                    continue;
                }

                if (ShouldTriggerShotDirectionChange(i, shotIndex, chance))
                {
                    pigeon.TryRandomizeFlightDirection();
                }
            }
        }

        private bool ShouldTriggerShotDirectionChange(int pigeonIndex, int shotIndex, float chance)
        {
            if (chance >= 1f)
            {
                return true;
            }

            if (chance <= 0f)
            {
                return false;
            }

            if (!ShouldUseSyncedRoundPlan())
            {
                return Random.value <= chance;
            }

            return GetMode1Plan01((shotIndex * 31) + Mathf.Max(0, pigeonIndex), 307) <= chance;
        }

        private bool ShouldUseVRCDogForEndRoundLmao()
        {
            return ShouldUseVRCDogForLmaoVariant(911);
        }

        private bool ShouldUseVRCDogForWaveMissLmao()
        {
            return ShouldUseVRCDogForLmaoVariant((pigeonsResolvedThisRound * 37) + 977);
        }

        private bool ShouldUseVRCDogForLmaoVariant(int salt)
        {
            if (manager == null || manager.animationController == null)
            {
                return false;
            }

            var chance = manager.animationController.LmfaoVRCDogChance;
            if (chance <= 0f)
            {
                return false;
            }

            if (chance >= 1f)
            {
                return true;
            }

            if (ShouldUseSyncedRoundPlan())
            {
                return GetMode1Plan01(GetDisplayedRoundNumber(), salt) <= chance;
            }

            return Random.value <= chance;
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
                HandlePairPigeonResolution(pigeon, wasHit);
            }
            else
            {
                HandleSinglePigeonResolution(pigeon, wasHit);
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

            if (IsPairModeActive())
            {
                BeginNaturalEscapeForActivePigeons();
                return;
            }

            manager.PlayPigeonExitAnimation(null);
        }

        private void HandleSinglePigeonResolution(PigeonTarget pigeon, bool wasHit)
        {
            pigeonsResolvedThisRound++;

            UpdateResolvedPigeonUI(wasHit);

            if (manager.uiController != null)
            {
                manager.uiController.ClearActivePigeonMask();
            }

            TryResetWaveAfterResolution();

            spawnTimer = Mathf.Max(0f, manager.spawnDelay);
            PlayResolutionAnimation(pigeon, wasHit);

            if (pigeonsResolvedThisRound >= targetQuotaThisRound)
            {
                EndRound();
                return;
            }
        }

        private void HandlePairPigeonResolution(PigeonTarget pigeon, bool wasHit)
        {
            if (!wasHit)
            {
                pairWaveHadMiss = true;
            }
            else
            {
                pairWaveHadHit = true;
                RecordPairWaveHitColor(pigeon);
                pairWaveLastHitPosition = GetResolvedPigeonPosition(pigeon);
                pairWaveHasLastHitPosition = true;
                pairWaveHitCount++;
            }

            ClearResolvedPairWavePigeon(pigeon);
            pigeonsResolvedThisRound++;
            UpdateResolvedPigeonUI(wasHit);

            pairWavePendingResolutions = Mathf.Max(0, pairWavePendingResolutions - 1);
            if (pairWavePendingResolutions > 0)
            {
                RefreshPairWaveMask();
                return;
            }

            pairWaveActive = false;

            if (manager.uiController != null)
            {
                manager.uiController.ClearActivePigeonMask();
            }

            TryResetWaveAfterResolution();

            spawnTimer = Mathf.Max(0f, manager.spawnDelay);

            PlayPairResolutionAnimation();

            if (pigeonsResolvedThisRound >= targetQuotaThisRound)
            {
                EndRound();
                return;
            }

            pairWaveHitCount = 0;
            pairWaveHadHit = false;
            pairWaveHadMiss = false;
            pairWaveFirstHitColor = PigeonColorType.Black;
            pairWaveSecondHitColor = PigeonColorType.Black;
            pairWaveFirstPigeon = null;
            pairWaveSecondPigeon = null;
            pairWaveFirstUiIndex = -1;
            pairWaveSecondUiIndex = -1;
            pairWaveLastHitPosition = Vector3.zero;
            pairWaveHasLastHitPosition = false;
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

        private void PlayResolutionAnimation(PigeonTarget pigeon, bool wasHit)
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
                var hitPigeon = pigeon != null ? pigeon : lastHitPigeon;
                var pos = GetResolvedPigeonPosition(hitPigeon);

                if (hitPigeon != null)
                {
                    manager.animationController.ShowGotOne(hitPigeon.ColorType);
                }

                manager.animationController.PlayHitAnimationAtWorldPosition(pos);
            }
            else
            {
                manager.animationController.SetUseVRCDogForNextMissLmao(ShouldUseVRCDogForWaveMissLmao());
                manager.animationController.PlayMissAnimation();
            }
        }

        private Vector3 GetResolvedPigeonPosition(PigeonTarget pigeon)
        {
            if (pigeon != null)
            {
                return pigeon.transform.position;
            }

            return Vector3.zero;
        }

        private void PlayPairResolutionAnimation()
        {
            if (pairWaveHitCount >= 2)
            {
                PlayPairDoubleHitAnimation();
                return;
            }

            if (pairWaveHitCount == 1)
            {
                PlayPairSingleHitAnimation();
                return;
            }

            PlayResolutionAnimation(null, false);
        }

        private void PlayPairDoubleHitAnimation()
        {
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = true;

            if (manager == null || manager.animationController == null || spawnTimer <= 0f)
            {
                return;
            }

            lastResolutionHadAnimation = true;
            var pos = pairWaveHasLastHitPosition ? pairWaveLastHitPosition : GetRoundEndPigeonPosition();
            manager.animationController.ShowGotTwo(pairWaveFirstHitColor, pairWaveSecondHitColor);
            manager.animationController.PlayHitAnimationAtWorldPosition(pos);
        }

        private void PlayPairSingleHitAnimation()
        {
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = true;

            if (manager == null || manager.animationController == null || spawnTimer <= 0f)
            {
                return;
            }

            lastResolutionHadAnimation = true;
            var pos = pairWaveHasLastHitPosition ? pairWaveLastHitPosition : GetRoundEndPigeonPosition();
            manager.animationController.ShowGotOne(pairWaveFirstHitColor);
            manager.animationController.PlayHitAnimationAtWorldPosition(pos);
        }

        #endregion

        #region Spawn Pair Mode And Wave Flow

        private void TryStartPairSpawn()
        {
            if (!TrySpawnSinglePigeon(out int firstIndex, out PigeonTarget firstPigeon, false, false))
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

            pairWaveFirstSpawnIndex = firstIndex;
            pairWaveActive = true;
            pairWavePendingResolutions = 2;
            pairWaveHadHit = false;
            pairWaveHadMiss = false;
            pairWaveHitCount = 0;
            pairWaveFirstHitColor = PigeonColorType.Black;
            pairWaveSecondHitColor = PigeonColorType.Black;
            pairWaveFirstPigeon = firstPigeon;
            pairWaveSecondPigeon = null;
            pairWaveFirstUiIndex = firstIndex;
            pairWaveSecondUiIndex = -1;
            pairWaveLastHitPosition = Vector3.zero;
            pairWaveHasLastHitPosition = false;
            pairWaveSecondSpawnPending = true;
            pairWaveSecondSpawnTimer = GetPairModeLaunchDelay();

            if (manager.uiController != null)
            {
                manager.uiController.ShowActivePigeonMask(firstIndex);
            }
        }

        private void TickPendingPairSecondSpawn()
        {
            pairWaveSecondSpawnTimer -= Time.deltaTime;
            if (pairWaveSecondSpawnTimer > 0f)
            {
                return;
            }

            pairWaveSecondSpawnPending = false;
            pairWaveSecondSpawnTimer = 0f;

            if (!TrySpawnSinglePigeon(out int secondIndex, out PigeonTarget secondPigeon, true, false))
            {
                if (manager.uiController != null)
                {
                    manager.uiController.ShowActivePigeonMask(pairWaveFirstSpawnIndex);
                }

                pairWaveSecondSpawnPending = false;
                pairWaveSecondSpawnTimer = 0f;
                pairWavePendingResolutions = Mathf.Max(0, pairWavePendingResolutions - 1);
                pairWaveFirstSpawnIndex = -1;

                return;
            }

            pairWaveSecondPigeon = secondPigeon;
            pairWaveSecondUiIndex = secondIndex;
            BeginPairWave(pairWaveFirstSpawnIndex, secondIndex);
            pairWaveFirstSpawnIndex = -1;
        }

        private void BeginPairWave(int firstIndex, int secondIndex)
        {
            Debug.Log("BeginPairWave");

            pairWaveActive = true;

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
            PigeonTarget spawnedPigeon;
            return TrySpawnSinglePigeon(out spawnIndex, out spawnedPigeon, ignoreActiveSlots, updateUiMask);
        }

        private bool TrySpawnSinglePigeon(out int spawnIndex, out PigeonTarget spawnedPigeon, bool ignoreActiveSlots = false, bool updateUiMask = true)
        {
            spawnIndex = -1;
            spawnedPigeon = null;

            if (manager.pigeonPool == null || manager.pigeonPool.Length == 0)
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

            var plannedPoolIndex = -1;
            if (ShouldUseSyncedRoundPlan())
            {
                plannedPoolIndex = GetSyncedPlannedPigeonPoolIndex(pigeonsSpawnedThisRound);
            }

            var pigeon = plannedPoolIndex >= 0 ? GetAvailablePigeonByPoolIndex(plannedPoolIndex) : GetAvailablePigeon();

            if (pigeon == null)
            {
                spawnTimer = 0.25f;

                return false;
            }

            Vector3 startPosition;
            int directionIndex;
            if (!TryBuildSpawnParametersForSpawnIndex(pigeonsSpawnedThisRound, out startPosition, out directionIndex))
            {
                spawnTimer = 0.25f;

                return false;
            }

            pigeon.SetPlayArea(manager.playArea);
            spawnIndex = pigeonsSpawnedThisRound;
            spawnedPigeon = pigeon;
            if (ShouldUseSyncedRoundPlan())
            {
                pigeon.BeginSeededFlight(
                    startPosition,
                    directionIndex,
                    0f,
                    DifficultyMultiplier,
                    GetCurrentPigeonEscapeTriggerReduction(),
                    manager.pigeonEscapeTriggerMinimum,
                    mode1PlanSeed,
                    spawnIndex,
                    GetPigeonPoolIndex(pigeon));
            }
            else
            {
                pigeon.BeginFlight(
                    startPosition,
                    directionIndex,
                    0f,
                    DifficultyMultiplier,
                    GetCurrentPigeonEscapeTriggerReduction(),
                    manager.pigeonEscapeTriggerMinimum);
            }

            pigeonsSpawnedThisRound++;
            BeginWaveIfNeeded();
            LogPigeonSpawnWave(spawnIndex);

            if (manager.uiController != null && updateUiMask)
            {
                manager.uiController.ShowActivePigeonMask(spawnIndex);
            }

            return true;
        }

        private bool TryBuildSpawnParametersForSpawnIndex(int spawnIndex, out Vector3 startPosition, out int directionIndex)
        {
            if (ShouldUseSyncedRoundPlan())
            {
                return TryBuildSyncedPlannedSpawnParameters(spawnIndex, out startPosition, out directionIndex);
            }

            return TryBuildSpawnParameters(out startPosition, out directionIndex);
        }

        private bool TryBuildSpawnParameters(out Vector3 startPosition, out int directionIndex)
        {
            startPosition = Vector3.zero;
            directionIndex = DirectionRight;

            if (manager.playArea == null)
            {
                return false;
            }

            var rect = manager.playArea.rect;
            var minX = rect.xMin;
            var maxX = rect.xMax;
            var minY = rect.yMin;
            var localInset = ConvertWorldHorizontalInsetToLocal(manager.playArea, manager.bottomEdgeSpawnSegment);
            var localPosition = Vector3.zero;
            localPosition.y = minY;
            localPosition.x = SampleWithin(minX, maxX, localInset);
            directionIndex = SampleBottomEdgeDirection();

            startPosition = manager.playArea.TransformPoint(localPosition);

            return true;
        }

        private bool TryBuildSyncedPlannedSpawnParameters(int spawnIndex, out Vector3 startPosition, out int directionIndex)
        {
            startPosition = Vector3.zero;
            directionIndex = DirectionRight;

            if (manager.playArea == null)
            {
                return false;
            }

            var rect = manager.playArea.rect;
            var minX = rect.xMin;
            var maxX = rect.xMax;
            var minY = rect.yMin;
            var localInset = ConvertWorldHorizontalInsetToLocal(manager.playArea, manager.bottomEdgeSpawnSegment);
            var localPosition = Vector3.zero;
            localPosition.y = minY;
            localPosition.x = SamplePlannedWithin(minX, maxX, localInset, spawnIndex, 17);
            directionIndex = SampleSyncedPlannedBottomEdgeDirection(spawnIndex);
            startPosition = manager.playArea.TransformPoint(localPosition);

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

        private float SamplePlannedWithin(float min, float max, float segmentInset, int spawnIndex, int salt)
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

            return Mathf.Lerp(paddedMin, paddedMax, GetMode1Plan01(spawnIndex, salt));
        }

        private void LogPigeonSpawnWave(int spawnIndex)
        {
            var displayIndex = Mathf.Max(1, spawnIndex + 1);
            var waveType = IsPairModeActive() ? "ModeB" : "ModeA";
            Debug.Log(
                $"<color=#32C8FF>[Wave]</color> {waveType} wave {displayIndex}/{Mathf.Max(1, targetQuotaThisRound)} started.");
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

        #endregion

        #region Target Pool And Synced Round Plan

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

        private int SampleSyncedPlannedBottomEdgeDirection(int spawnIndex)
        {
            return GetMode1PlanBit(spawnIndex, 29) == 0 ? DirectionLeftUp : DirectionRightUp;
        }

        private PigeonTarget GetAvailablePigeon()
        {
            if (manager.pigeonPool == null)
            {
                return null;
            }

            var availableCount = 0;
            for (int i = 0; i < manager.pigeonPool.Length; i++)
            {
                var candidate = manager.pigeonPool[i];
                if (candidate != null && candidate.IsAvailable)
                {
                    availableCount++;
                }
            }

            if (availableCount <= 0)
            {
                return null;
            }

            var selectedAvailableIndex = Random.Range(0, availableCount);
            var currentAvailableIndex = 0;
            for (int i = 0; i < manager.pigeonPool.Length; i++)
            {
                var candidate = manager.pigeonPool[i];
                if (candidate == null || !candidate.IsAvailable)
                {
                    continue;
                }

                if (currentAvailableIndex == selectedAvailableIndex)
                {
                    return candidate;
                }

                currentAvailableIndex++;
            }

            return null;
        }

        private PigeonTarget GetAvailablePigeonByPoolIndex(int poolIndex)
        {
            if (manager.pigeonPool == null || poolIndex < 0 || poolIndex >= manager.pigeonPool.Length)
            {
                return null;
            }

            var pigeon = manager.pigeonPool[poolIndex];
            if (pigeon != null && pigeon.IsAvailable)
            {
                return pigeon;
            }

            return null;
        }

        private int GetPigeonPoolIndex(PigeonTarget pigeon)
        {
            if (manager == null || manager.pigeonPool == null || pigeon == null)
            {
                return -1;
            }

            for (int i = 0; i < manager.pigeonPool.Length; i++)
            {
                if (manager.pigeonPool[i] == pigeon)
                {
                    return i;
                }
            }

            return -1;
        }

        private int GetSyncedPlannedPigeonPoolIndex(int spawnIndex)
        {
            if (manager == null || manager.pigeonPool == null || manager.pigeonPool.Length == 0)
            {
                return -1;
            }

            var poolLength = manager.pigeonPool.Length;
            var plannedIndex = Mathf.Abs(GetMode1PlanValue(spawnIndex, 41)) % poolLength;
            if (IsPairModeActive() && poolLength > 1 && spawnIndex % 2 == 1)
            {
                var previousIndex = Mathf.Abs(GetMode1PlanValue(spawnIndex - 1, 41)) % poolLength;
                if (plannedIndex == previousIndex)
                {
                    plannedIndex = (plannedIndex + 1) % poolLength;
                }
            }

            return plannedIndex;
        }

        private bool ShouldUseSyncedRoundPlan()
        {
            return IsNetworkedPigeonMode() &&
                   mode1PlanActive &&
                   mode1PlanRoundNumber == GetDisplayedRoundNumber();
        }

        private bool ShouldWaitForSyncedRoundPlan()
        {
            if (!IsNetworkedPigeonModeWithSync())
            {
                return false;
            }

            if (!ShouldUseSyncedRoundPlan() && IsMode1SyncOwner())
            {
                EnsureMode1RoundPlanForCurrentRound();
            }

            return !ShouldUseSyncedRoundPlan();
        }

        private bool ShouldWaitForSyncedOwnerStartGate()
        {
            return IsNetworkedPigeonModeWithSync() && !CanStartSyncedRoundSpawn();
        }

        private bool IsNetworkedPigeonModeWithSync()
        {
            return IsNetworkedPigeonMode() &&
                   manager != null &&
                   manager.syncController != null;
        }

        private bool IsNetworkedPigeonMode()
        {
            return activeGameMode == GameModeSingle || activeGameMode == GameModePair;
        }

        private bool IsMode1SyncOwner()
        {
            return IsNetworkedPigeonModeWithSync() &&
                   VRC.SDKBase.Networking.IsOwner(manager.syncController.gameObject);
        }

        private bool CanStartSyncedRoundSpawn()
        {
            if (!IsNetworkedPigeonModeWithSync())
            {
                return true;
            }

            var roundNumber = GetDisplayedRoundNumber();
            if (IsMode1SyncOwner())
            {
                if (!mode1LocalStartGateSent || mode1LocalStartGateRoundNumber != roundNumber)
                {
                    mode1LocalStartGateSent = true;
                    mode1LocalStartGateRoundNumber = roundNumber;
                    mode1LocalStartGateSeed = mode1PlanSeed;
                    manager.syncController.SyncMode1StartGate(roundNumber, mode1PlanSeed);
                }

                return true;
            }

            if (mode1LocalStartGateRoundNumber == roundNumber &&
                mode1LocalStartGateSeed == mode1PlanSeed)
            {
                return true;
            }

            if (manager.syncController.GetMode1StartGateRoundNumber() == roundNumber &&
                manager.syncController.GetMode1StartGateSeed() == mode1PlanSeed)
            {
                mode1LocalStartGateRoundNumber = roundNumber;
                mode1LocalStartGateSeed = mode1PlanSeed;
                return true;
            }

            return false;
        }

        private int GetMode1PlanBit(int spawnIndex, int salt)
        {
            return GetMode1PlanValue(spawnIndex, salt) & 1;
        }

        private float GetMode1Plan01(int spawnIndex, int salt)
        {
            var value = GetMode1PlanValue(spawnIndex, salt) & 0x7fffffff;
            return value / 2147483647f;
        }

        private int GetMode1PlanValue(int spawnIndex, int salt)
        {
            var value = Mathf.Abs(mode1PlanSeed);
            value = MixMode1PlanValue(value, mode1PlanRoundNumber);
            value = MixMode1PlanValue(value, spawnIndex);
            value = MixMode1PlanValue(value, salt);
            return value;
        }

        private int MixMode1PlanValue(int value, int salt)
        {
            var mixed = Mathf.Abs(value + 31 * (salt + 1));
            mixed = (mixed * 1103515245 + 12345) & 0x7fffffff;
            return mixed;
        }

        private int CountActivePigeons()
        {
            if (manager.pigeonPool == null)
            {
                return 0;
            }

            var count = 0;
            for (int i = 0; i < manager.pigeonPool.Length; i++)
            {
                var pigeon = manager.pigeonPool[i];
                if (pigeon != null && pigeon.OccupiesSlot)
                {
                    count++;
                }
            }

            return count;
        }

        private void BeginNaturalEscapeForActivePigeons()
        {
            if (manager == null || manager.pigeonPool == null)
            {
                return;
            }

            for (int i = 0; i < manager.pigeonPool.Length; i++)
            {
                var pigeon = manager.pigeonPool[i];
                if (pigeon == null || !pigeon.OccupiesSlot)
                {
                    continue;
                }

                pigeon.TryBeginNaturalEscape();
            }
        }

        #endregion

        #region Reset Rules And UI Helpers

        private void InitializePool()
        {
            if (manager.pigeonPool == null)
            {
                return;
            }

            for (int i = 0; i < manager.pigeonPool.Length; i++)
            {
                var pigeon = manager.pigeonPool[i];
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
            ResetRoundState(true);
        }

        private void ResetRoundState(bool resetUi)
        {
            currentRoundIndex = 0;
            roundDifficultyBonus = 0f;
            activeGameMode = GameModeSingle;
            currentDifficultyLevel = 0;
            gameLocked = false;
            carryScorePending = false;
            carryScoreValue = 0;
            mode1LocalStartGateRoundNumber = 0;
            mode1LocalStartGateSeed = 0;
            mode1LocalStartGateSent = false;
            ownerRoundResultSyncAttempted = false;
            appliedMode1RoundResultActive = false;
            appliedMode1RoundResultRound = 0;
            appliedMode1RoundResultScore = 0;
            appliedMode1RoundResultHitCount = 0;
            appliedMode1RoundResultPassed = false;
            roundEndPending = false;
            roundEndHitCountTriggered = false;
            roundEndAudioTriggered = false;
            roundEndAudioDelayStarted = false;
            roundEndAudioDelayTimer = 0f;
            roundEndPassed = false;
            roundEndAnimationTimer = 0f;
            roundEndLmaoAnimPending = false;
            roundEndLmaoAnimTimer = 0f;
            ClearPendingSyncedRoundResult();
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = false;
            lastHitPigeon = null;
            ResetRoundRuntimeState();

            DespawnAllPigeons();
            if (resetUi)
            {
                ResetUIForCurrentRound();
            }
        }

        private void ResetRoundRuntimeState()
        {
            targetQuotaThisRound = GetTargetQuotaForCurrentMode();
            pigeonsSpawnedThisRound = 0;
            pigeonsResolvedThisRound = 0;
            pigeonsHitThisRound = 0;
            mode1LocalStartGateRoundNumber = 0;
            mode1LocalStartGateSeed = 0;
            mode1LocalStartGateSent = false;
            ownerRoundResultSyncAttempted = false;
            appliedMode1RoundResultActive = false;
            appliedMode1RoundResultRound = 0;
            appliedMode1RoundResultScore = 0;
            appliedMode1RoundResultHitCount = 0;
            appliedMode1RoundResultPassed = false;
            spawnTimer = 0f;
            pairWaveActive = false;
            pairWavePendingResolutions = 0;
            pairWaveHadHit = false;
            pairWaveHadMiss = false;
            pairWaveHitCount = 0;
            pairWaveSecondSpawnPending = false;
            pairWaveSecondSpawnTimer = 0f;
            pairWaveFirstSpawnIndex = -1;
            pairWaveFirstHitColor = PigeonColorType.Black;
            pairWaveSecondHitColor = PigeonColorType.Black;
            pairWaveFirstPigeon = null;
            pairWaveSecondPigeon = null;
            pairWaveFirstUiIndex = -1;
            pairWaveSecondUiIndex = -1;
            pairWaveLastHitPosition = Vector3.zero;
            pairWaveHasLastHitPosition = false;
            roundEndPending = false;
            roundEndHitCountTriggered = false;
            roundEndAudioTriggered = false;
            roundEndAudioDelayStarted = false;
            roundEndAudioDelayTimer = 0f;
            roundEndPassed = false;
            roundEndAnimationTimer = 0f;
            roundEndLmaoAnimPending = false;
            roundEndLmaoAnimTimer = 0f;
            ClearPendingSyncedRoundResult();
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = false;
            lastHitPigeon = null;
            ResetWaveTracking();
        }

        private void EnsureMode1RoundPlanForCurrentRound()
        {
            if (manager == null || !IsNetworkedPigeonMode())
            {
                mode1PlanActive = false;
                return;
            }

            var roundNumber = GetDisplayedRoundNumber();
            if (mode1PlanActive && mode1PlanRoundNumber == roundNumber)
            {
                return;
            }

            if (IsMode1SyncOwner())
            {
                var seed = Random.Range(1, int.MaxValue);
                ApplyMode1RoundPlan(roundNumber, seed);
                manager.syncController.SyncMode1RoundPlan(roundNumber, seed);
                return;
            }

            mode1PlanActive = false;
        }

        private float GetPairModeLaunchDelay()
        {
            if (manager == null)
            {
                return 0f;
            }

            var delayMin = Mathf.Min(manager.pairModeLaunchDelayMin, manager.pairModeLaunchDelayMax);
            var delayMax = Mathf.Max(manager.pairModeLaunchDelayMin, manager.pairModeLaunchDelayMax);
            if (ShouldUseSyncedRoundPlan())
            {
                return Mathf.Lerp(delayMin, delayMax, GetMode1Plan01(pigeonsSpawnedThisRound, 53));
            }

            return Random.Range(delayMin, delayMax);
        }

        private void RecordPairWaveHitColor(PigeonTarget hitPigeon)
        {
            if (hitPigeon == null)
            {
                return;
            }

            if (pairWaveHitCount <= 0)
            {
                pairWaveFirstHitColor = hitPigeon.ColorType;
                return;
            }

            pairWaveSecondHitColor = hitPigeon.ColorType;
        }

        private void ClearResolvedPairWavePigeon(PigeonTarget pigeon)
        {
            if (pigeon == null)
            {
                return;
            }

            if (pairWaveFirstPigeon == pigeon)
            {
                pairWaveFirstPigeon = null;
                pairWaveFirstUiIndex = -1;
                return;
            }

            if (pairWaveSecondPigeon == pigeon)
            {
                pairWaveSecondPigeon = null;
                pairWaveSecondUiIndex = -1;
            }
        }

        private void RefreshPairWaveMask()
        {
            if (manager == null || manager.uiController == null)
            {
                return;
            }

            var firstActive = pairWaveFirstPigeon != null ? pairWaveFirstUiIndex : -1;
            var secondActive = pairWaveSecondPigeon != null ? pairWaveSecondUiIndex : -1;

            if (firstActive >= 0 && secondActive >= 0)
            {
                manager.uiController.SetActivePigeonMaskTargets(firstActive, secondActive);
                return;
            }

            if (firstActive >= 0)
            {
                manager.uiController.SetActivePigeonMaskTargets(firstActive, -1);
                return;
            }

            if (secondActive >= 0)
            {
                manager.uiController.SetActivePigeonMaskTargets(secondActive, -1);
                return;
            }

            manager.uiController.ClearActivePigeonMask();
        }

        private int GetTargetQuotaForCurrentMode()
        {
            if (manager == null)
            {
                return 1;
            }

            if (activeGameMode == GameModePair || manager.gameMode == GameModePair)
            {
                return Mathf.Max(1, manager.pairModeWaveCount) * 2;
            }

            return Mathf.Max(1, manager.pigeonsPerRound);
        }

        private void DespawnAllPigeons()
        {
            if (manager.pigeonPool == null)
            {
                return;
            }

            for (int i = 0; i < manager.pigeonPool.Length; i++)
            {
                var pigeon = manager.pigeonPool[i];
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

        private float GetCurrentPigeonEscapeTriggerReduction()
        {
            if (manager == null)
            {
                return 0f;
            }

            var maxRound = Mathf.Max(1, manager.pigeonEscapeTriggerReductionMaxRound);
            var reductionRounds = Mathf.Clamp(currentRoundIndex - 1, 0, maxRound - 1);
            return Mathf.Max(0f, reductionRounds * manager.pigeonEscapeTriggerReductionStep);
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

            return Mathf.Max(0, (roundIndex - 1) / 3);
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
            manager.uiController.SetPigeonQuota(targetQuotaThisRound);
            manager.uiController.ClearPigeonHitIndicators();
            manager.uiController.SetGoodActive(false);
            manager.uiController.ClearPerfectDisplay();
            manager.uiController.ShowRoundDisplay();

            if (manager.animationController != null)
            {
                manager.animationController.HideGotOneObjects();
            }
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

            var useNextTiming = currentRoundIndex > 1;
            var startDelay = useNextTiming ? manager.animationController.GameNextDuration : manager.animationController.GameStartDuration;
            startAnimationTimer = Mathf.Max(0f, startDelay);

            startDelayTime = useNextTiming ? manager.animationController.GameNextDelay : manager.animationController.GameStartDelay;
            if (startDelayTime > 0f)
            {
                manager.animationController.RestLayerOrder();
            }

            startMovementSequenceTriggered = false;
            waitingForStartAnimation = startAnimationTimer > 0f;
            manager.animationController.PlayGameStartAnimationWithAudio(useNextTiming);
        }

        public static Vector3 EvaluateClayHookTrajectory(
            Vector3 startPosition,
            Vector3 endPosition,
            float peakHeight,
            float lateralCurveAmount,
            Vector3 rightAxis,
            float normalizedTime,
            float minAllowedY,
            float maxAllowedY)
        {
            var t = Mathf.Clamp01(normalizedTime);
            var basePosition = Vector3.Lerp(startPosition, endPosition, t);

            var heightCurve = EvaluateClayHookHeight(t);
            var lateralCurve = EvaluateClayHookLateral(t);

            var position = basePosition;
            position += Vector3.up * (heightCurve * peakHeight);
            position += rightAxis * (lateralCurve * lateralCurveAmount);

            if (maxAllowedY < minAllowedY)
            {
                var swap = minAllowedY;
                minAllowedY = maxAllowedY;
                maxAllowedY = swap;
            }

            var minClampWeight = Mathf.Clamp01((t - 0.04f) / 0.18f);
            var dynamicMinAllowedY = Mathf.Lerp(startPosition.y, minAllowedY, minClampWeight);
            position.y = Mathf.Clamp(position.y, dynamicMinAllowedY, maxAllowedY);
            return position;
        }

        private static float EvaluateClayHookHeight(float t)
        {
            var rise = Mathf.Sin(t * Mathf.PI);
            var forwardBias = Mathf.Lerp(1.15f, 0.55f, t);
            var tailDrop = Mathf.Pow(t, 1.85f) * 0.7f;
            return Mathf.Max(0f, (rise * forwardBias) - tailDrop);
        }

        private static float EvaluateClayHookLateral(float t)
        {
            return Mathf.Sin(t * Mathf.PI * 0.9f) * (1f - (0.55f * t));
        }

        #endregion
    }
}



