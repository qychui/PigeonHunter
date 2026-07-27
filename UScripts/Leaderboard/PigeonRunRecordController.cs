using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PigeonRunRecordController : UdonSharpBehaviour
    {
        private const string LeaderboardRootName = "Leaderboard GameObject PigeonHunt";

        public const int ModeA = 1;
        public const int ModeB = 2;
        public const int ModeC = 3;

        public const int RunStateIdle = 0;
        public const int RunStatePrepared = 1;
        public const int RunStateRunning = 2;
        public const int RunStateFinalized = 3;
        public const int RunStateInvalid = 4;

        public const int InvalidReasonNone = 0;
        public const int InvalidReasonDebugCommand = 1;
        public const int InvalidReasonForcedSettlement = 2;
        public const int InvalidReasonOwnerTransfer = 3;
        public const int InvalidReasonWrongStart = 4;
        public const int InvalidReasonLateJoinState = 5;
        public const int InvalidReasonInterrupted = 6;

        #region Configuration And Runtime State

        [Header("Versions")]
        [Min(1)] public int rulesetVersion = 1;
        [Min(1)] public int buildVersion = 1;

        [Header("Eligibility")]
        public bool leaderboardTestMode;

        [Header("Persistence")]
        public PigeonLeaderboardPersistence persistence;

        [Header("Leaderboard Upload")]
        public PigeonGlobalLeaderboardBridge leaderboardBridge;

        [Header("Runtime")]
        [SerializeField] private int runState;
        [SerializeField] private int invalidReason;
        [SerializeField] private int activeModeId;
        [SerializeField] private int activeTotalHits;
        [SerializeField] private int startedOwnerPlayerId = -1;

        [Header("Latest Result")]
        [SerializeField] private bool hasLiveResult;
        [SerializeField] private int liveModeId;
        [SerializeField] private int liveFinalScore;
        [SerializeField] private int liveReachedRound;
        [SerializeField] private int liveTotalHits;
        [SerializeField] private int liveRulesetVersion;
        [SerializeField] private int liveBuildVersion;
        [SerializeField] private string liveRunId;

        private GameManager gameManager;
        private bool resultSubmitted;
        private int activeRulesetVersion;
        private int activeBuildVersion;
        private int runSequence;
        private string currentRunId;

        #endregion

        #region Lifecycle And Ownership Validation

        public void Initialize(GameManager manager)
        {
            gameManager = manager;
            RegisterLeaderboardSource();
        }

        private void Update()
        {
            if (runState != RunStatePrepared && runState != RunStateRunning)
            {
                return;
            }

            if (!IsOriginalLocalOwner())
            {
                InvalidateRun(InvalidReasonOwnerTransfer);
            }
        }

        private bool IsOriginalLocalOwner()
        {
            var localPlayer = Networking.LocalPlayer;
            return localPlayer != null &&
                   localPlayer.playerId == startedOwnerPlayerId &&
                   (gameManager == null || gameManager.IsLocalGameplayOwner());
        }

        #endregion

        #region Run Lifecycle

        public void PrepareRun(int modeId)
        {
            if (!IsValidMode(modeId))
            {
                ResetRun();
                runState = RunStateInvalid;
                invalidReason = InvalidReasonWrongStart;
                return;
            }

            if (gameManager != null && !gameManager.IsLocalGameplayOwner())
            {
                if (runState == RunStatePrepared || runState == RunStateRunning)
                {
                    InvalidateRun(InvalidReasonOwnerTransfer);
                }

                return;
            }

            var localPlayer = Networking.LocalPlayer;
            if (localPlayer == null)
            {
                return;
            }

            ResetRun();
            RegisterLeaderboardSource();
            activeModeId = modeId;
            activeRulesetVersion = Mathf.Max(1, rulesetVersion);
            activeBuildVersion = Mathf.Max(1, buildVersion);
            startedOwnerPlayerId = localPlayer.playerId;
            currentRunId = CreateRunId(localPlayer.playerId);
            runState = RunStatePrepared;

            if (leaderboardTestMode)
            {
                InvalidateRun(InvalidReasonDebugCommand);
            }
        }

        public void BeginRun()
        {
            if (runState != RunStatePrepared)
            {
                return;
            }

            if (!IsOriginalLocalOwner())
            {
                InvalidateRun(InvalidReasonOwnerTransfer);
                return;
            }

            ClearLiveResult();
            activeTotalHits = 0;
            resultSubmitted = false;
            runState = RunStateRunning;
        }

        public void RecordHit()
        {
            if (runState != RunStateRunning)
            {
                return;
            }

            if (!IsOriginalLocalOwner())
            {
                InvalidateRun(InvalidReasonOwnerTransfer);
                return;
            }

            activeTotalHits++;
        }

        public void FinalizeRun(int finalScore, int reachedRound)
        {
            if (runState != RunStateRunning || resultSubmitted)
            {
                return;
            }

            if (!IsOriginalLocalOwner())
            {
                InvalidateRun(InvalidReasonOwnerTransfer);
                return;
            }

            resultSubmitted = true;
            liveModeId = activeModeId;
            liveFinalScore = Mathf.Max(0, finalScore);
            liveReachedRound = Mathf.Max(1, reachedRound);
            liveTotalHits = Mathf.Max(0, activeTotalHits);
            liveRulesetVersion = activeRulesetVersion;
            liveBuildVersion = activeBuildVersion;
            liveRunId = currentRunId;
            hasLiveResult = !string.IsNullOrEmpty(liveRunId);
            invalidReason = InvalidReasonNone;
            runState = RunStateFinalized;

            if (hasLiveResult && persistence != null)
            {
                persistence.RecordRunResult(
                    liveModeId,
                    liveFinalScore,
                    liveReachedRound,
                    liveTotalHits,
                    liveRulesetVersion,
                    liveBuildVersion,
                    liveRunId);
            }

            RegisterLeaderboardSource();
        }

        public void InvalidateRun(int reason)
        {
            if (runState != RunStatePrepared && runState != RunStateRunning)
            {
                return;
            }

            invalidReason = reason == InvalidReasonNone ? InvalidReasonInterrupted : reason;
            resultSubmitted = false;
            runState = RunStateInvalid;
        }

        public void ResetRun()
        {
            runState = RunStateIdle;
            invalidReason = InvalidReasonNone;
            activeModeId = 0;
            activeTotalHits = 0;
            activeRulesetVersion = 0;
            activeBuildVersion = 0;
            startedOwnerPlayerId = -1;
            currentRunId = "";
            resultSubmitted = false;
        }

        public void SetLeaderboardTestMode(bool enabled)
        {
            leaderboardTestMode = enabled;
            if (enabled)
            {
                InvalidateRun(InvalidReasonDebugCommand);
            }
        }

        private bool IsValidMode(int modeId)
        {
            return modeId == ModeA || modeId == ModeB || modeId == ModeC;
        }

        private string CreateRunId(int playerId)
        {
            runSequence++;
            return Networking.GetServerTimeInMilliseconds().ToString("x8") +
                   Mathf.Max(0, playerId).ToString("x8") +
                   runSequence.ToString("x8");
        }

        private void RegisterLeaderboardSource()
        {
            if (leaderboardBridge == null)
            {
                var leaderboardRoot = GameObject.Find(LeaderboardRootName);
                if (leaderboardRoot != null)
                {
                    leaderboardBridge = leaderboardRoot.GetComponentInChildren<PigeonGlobalLeaderboardBridge>(true);
                }
            }

            if (leaderboardBridge != null)
            {
                leaderboardBridge.RegisterRecordSource(persistence, this);
            }
        }

        #endregion

        #region Result Queries

        public int GetRunState()
        {
            return runState;
        }

        public int GetInvalidReason()
        {
            return invalidReason;
        }

        public bool HasLiveResult()
        {
            return hasLiveResult && !string.IsNullOrEmpty(liveRunId);
        }

        public int GetLiveModeId()
        {
            return liveModeId;
        }

        public int GetLiveFinalScore()
        {
            return liveFinalScore;
        }

        public int GetLiveReachedRound()
        {
            return liveReachedRound;
        }

        public int GetLiveTotalHits()
        {
            return liveTotalHits;
        }

        public int GetLiveRulesetVersion()
        {
            return liveRulesetVersion;
        }

        public int GetLiveBuildVersion()
        {
            return liveBuildVersion;
        }

        public string GetLiveRunId()
        {
            return liveRunId;
        }

        private void ClearLiveResult()
        {
            hasLiveResult = false;
            liveModeId = 0;
            liveFinalScore = 0;
            liveReachedRound = 0;
            liveTotalHits = 0;
            liveRulesetVersion = 0;
            liveBuildVersion = 0;
            liveRunId = "";
        }

        #endregion
    }
}
