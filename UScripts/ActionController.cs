using UdonSharp;
using UnityEngine;
using UnityEngine.UIElements;

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
        private float roundDifficultyBonus;
        private bool waitingForStartAnimation;
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
        private bool lastResolutionHadAnimation;
        private bool lastResolutionWasHit;
        private PigeonTarget lastHitPigeon;
        private int currentDifficultyLevel;
        private bool gameLocked;
        private bool carryScorePending;
        private int carryScoreValue;

        private Vector3[] cachedCorners = new Vector3[4];
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

                if (currentRoundIndex > 1)
                {
                    manager.animationController.PlayRoundNextMovementSequence();
                }
                else
                {
                    manager.animationController.PlayRoundStartMovementSequence();
                }

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
            roundEndLmaoAnimPending = false;
            roundEndLmaoAnimTimer = 0f;
            roundActive = false;
            roundEndPassed = DetermineRoundPass();

            if (manager.animationController != null && lastResolutionHadAnimation)
            {
                roundEndAnimationTimer = Mathf.Max(0f, manager.animationController.GetResolutionAnimationDuration(lastResolutionWasHit));
            }
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

                manager.animationController.PlayHitAnimation(pos.x);
            }
            else
            {
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
            manager.animationController.PlayHitAnimation(pos.x);
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
            manager.animationController.PlayHitAnimation(pos.x);
        }

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
            spawnedPigeon = pigeon;
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

            startPosition.y = minY;
            startPosition.x = SampleWithin(minX, maxX, manager.bottomEdgeSpawnSegment);
            directionIndex = SampleBottomEdgeDirection();

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
            currentRoundIndex = 0;
            roundDifficultyBonus = 0f;
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
            roundEndLmaoAnimPending = false;
            roundEndLmaoAnimTimer = 0f;
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = false;
            lastHitPigeon = null;
            ResetRoundRuntimeState();

            DespawnAllPigeons();
            ResetUIForCurrentRound();
        }

        private void ResetRoundRuntimeState()
        {
            targetQuotaThisRound = GetTargetQuotaForCurrentMode();
            pigeonsSpawnedThisRound = 0;
            pigeonsResolvedThisRound = 0;
            pigeonsHitThisRound = 0;
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
            lastResolutionHadAnimation = false;
            lastResolutionWasHit = false;
            lastHitPigeon = null;
            ResetWaveTracking();
        }

        private float GetPairModeLaunchDelay()
        {
            if (manager == null)
            {
                return 0f;
            }

            var delayMin = Mathf.Min(manager.pairModeLaunchDelayMin, manager.pairModeLaunchDelayMax);
            var delayMax = Mathf.Max(manager.pairModeLaunchDelayMin, manager.pairModeLaunchDelayMax);
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
    }
}



