using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PigeonLeaderboardPersistence : UdonSharpBehaviour
    {
        public const string KeyRunnerId = "ph.lb.v1.runnerId";

        public const string ModeAPrefix = "ph.lb.v1.modeA";
        public const string ModeBPrefix = "ph.lb.v1.modeB";
        public const string ModeCPrefix = "ph.lb.v1.modeC";
        private const int ModeCount = 3;
        private const float RestoreRetrySeconds = 1f;

        #region Runtime State

        [SerializeField] private bool localPlayerRestored;
        [SerializeField] private string lastAcceptedRunId;
        private bool restoreRetryQueued;

        private readonly bool[] pendingHasResult = new bool[ModeCount];
        private readonly int[] pendingScore = new int[ModeCount];
        private readonly int[] pendingRound = new int[ModeCount];
        private readonly int[] pendingTotalHits = new int[ModeCount];
        private readonly int[] pendingDateYmd = new int[ModeCount];
        private readonly int[] pendingRulesetVersion = new int[ModeCount];
        private readonly int[] pendingBuildVersion = new int[ModeCount];

        #endregion

        #region PlayerData Lifecycle

        private void Start()
        {
            TryRecoverLocalPlayerRestore();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal)
            {
                return;
            }

            CompleteLocalPlayerRestore(player);
        }

        public void RetryLocalPlayerRestore()
        {
            restoreRetryQueued = false;
            TryRecoverLocalPlayerRestore();
        }

        private bool TryRecoverLocalPlayerRestore()
        {
            if (localPlayerRestored)
            {
                return true;
            }

            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player))
            {
                QueueLocalPlayerRestoreRetry();
                return false;
            }

            EnsureLocalRunnerId(player);
            if (!PlayerData.HasKey(player, KeyRunnerId))
            {
                QueueLocalPlayerRestoreRetry();
                return false;
            }

            var runnerId = PlayerData.GetString(player, KeyRunnerId);
            if (string.IsNullOrEmpty(runnerId) || runnerId.Length != 32)
            {
                QueueLocalPlayerRestoreRetry();
                return false;
            }

            CompleteLocalPlayerRestore(player);
            return true;
        }

        private void CompleteLocalPlayerRestore(VRCPlayerApi player)
        {
            if (localPlayerRestored)
            {
                return;
            }

            localPlayerRestored = true;
            EnsureLocalRunnerId(player);
            EnsureLocalRecordDate(player, PigeonRunRecordController.ModeA);
            EnsureLocalRecordDate(player, PigeonRunRecordController.ModeB);
            EnsureLocalRecordDate(player, PigeonRunRecordController.ModeC);
            FlushPendingResults();
        }

        private void QueueLocalPlayerRestoreRetry()
        {
            if (restoreRetryQueued)
            {
                return;
            }

            restoreRetryQueued = true;
            SendCustomEventDelayedSeconds(nameof(RetryLocalPlayerRestore), RestoreRetrySeconds);
        }

        #endregion

        #region Result Recording

        public void RecordRunResult(
            int modeId,
            int finalScore,
            int reachedRound,
            int totalHits,
            int rulesetVersion,
            int buildVersion,
            string runId)
        {
            if (!IsValidMode(modeId) || string.IsNullOrEmpty(runId) || runId == lastAcceptedRunId)
            {
                return;
            }

            finalScore = Mathf.Max(0, finalScore);
            reachedRound = Mathf.Max(1, reachedRound);
            totalHits = Mathf.Max(0, totalHits);
            rulesetVersion = Mathf.Max(1, rulesetVersion);
            buildVersion = Mathf.Max(1, buildVersion);
            lastAcceptedRunId = runId;

            var dateYmd = GetCurrentUtcDateYmd();
            TryRecoverLocalPlayerRestore();
            if (!localPlayerRestored)
            {
                CachePendingResult(
                    modeId,
                    finalScore,
                    reachedRound,
                    totalHits,
                    dateYmd,
                    rulesetVersion,
                    buildVersion);
                return;
            }

            SaveResult(
                modeId,
                finalScore,
                reachedRound,
                totalHits,
                dateYmd,
                rulesetVersion,
                buildVersion);
        }

        private void CachePendingResult(
            int modeId,
            int score,
            int reachedRound,
            int totalHits,
            int dateYmd,
            int rulesetVersion,
            int buildVersion)
        {
            var index = ModeToIndex(modeId);
            if (pendingHasResult[index] && !IsBetterResult(
                    score,
                    reachedRound,
                    pendingScore[index],
                    pendingRound[index]))
            {
                return;
            }

            pendingHasResult[index] = true;
            pendingScore[index] = score;
            pendingRound[index] = reachedRound;
            pendingTotalHits[index] = totalHits;
            pendingDateYmd[index] = dateYmd;
            pendingRulesetVersion[index] = rulesetVersion;
            pendingBuildVersion[index] = buildVersion;
        }

        private void FlushPendingResults()
        {
            FlushPendingResult(PigeonRunRecordController.ModeA);
            FlushPendingResult(PigeonRunRecordController.ModeB);
            FlushPendingResult(PigeonRunRecordController.ModeC);
        }

        private void FlushPendingResult(int modeId)
        {
            var index = ModeToIndex(modeId);
            if (!pendingHasResult[index])
            {
                return;
            }

            SaveResult(
                modeId,
                pendingScore[index],
                pendingRound[index],
                pendingTotalHits[index],
                pendingDateYmd[index],
                pendingRulesetVersion[index],
                pendingBuildVersion[index]);
            ClearPendingResult(index);
        }

        private void SaveResult(
            int modeId,
            int score,
            int reachedRound,
            int totalHits,
            int dateYmd,
            int rulesetVersion,
            int buildVersion)
        {
            var localPlayer = Networking.LocalPlayer;
            if (!localPlayerRestored || !Utilities.IsValid(localPlayer))
            {
                return;
            }

            var prefix = GetModePrefix(modeId);
            var hasRecordKey = prefix + ".hasRecord";
            var scoreKey = prefix + ".bestScore";
            var roundKey = prefix + ".bestRound";
            var hasRecord = PlayerData.HasKey(localPlayer, hasRecordKey)
                && PlayerData.GetBool(localPlayer, hasRecordKey);

            if (hasRecord && !IsBetterResult(
                    score,
                    reachedRound,
                    PlayerData.GetInt(localPlayer, scoreKey),
                    PlayerData.GetInt(localPlayer, roundKey)))
            {
                return;
            }

            PlayerData.SetInt(scoreKey, score);
            PlayerData.SetInt(roundKey, reachedRound);
            PlayerData.SetInt(prefix + ".bestTotalHits", totalHits);
            PlayerData.SetInt(prefix + ".bestDateYmd", dateYmd);
            PlayerData.SetInt(prefix + ".rulesetVersion", rulesetVersion);
            PlayerData.SetInt(prefix + ".buildVersion", buildVersion);
            PlayerData.SetString(prefix + ".runnerId", GetLocalRunnerId());
            PlayerData.SetBool(hasRecordKey, true);
        }

        private bool IsBetterResult(int score, int reachedRound, int bestScore, int bestRound)
        {
            if (score != bestScore)
            {
                return score > bestScore;
            }

            return reachedRound > bestRound;
        }

        private void ClearPendingResult(int index)
        {
            pendingHasResult[index] = false;
            pendingScore[index] = 0;
            pendingRound[index] = 0;
            pendingTotalHits[index] = 0;
            pendingDateYmd[index] = 0;
            pendingRulesetVersion[index] = 0;
            pendingBuildVersion[index] = 0;
        }

        #endregion

        #region Local Best Queries

        public bool IsLocalDataReady()
        {
            return localPlayerRestored;
        }

        public bool HasLocalBestRecord(int modeId)
        {
            return localPlayerRestored && HasPlayerBestRecord(Networking.LocalPlayer, modeId);
        }

        public int GetLocalBestScore(int modeId)
        {
            return GetLocalInt(modeId, ".bestScore", 0);
        }

        public int GetLocalBestRound(int modeId)
        {
            return GetLocalInt(modeId, ".bestRound", 0);
        }

        public int GetLocalBestTotalHits(int modeId)
        {
            return GetLocalInt(modeId, ".bestTotalHits", 0);
        }

        public int GetLocalBestDateYmd(int modeId)
        {
            return GetLocalInt(modeId, ".bestDateYmd", 0);
        }

        public int GetLocalBestRulesetVersion(int modeId)
        {
            return GetLocalInt(modeId, ".rulesetVersion", 0);
        }

        public int GetLocalBestBuildVersion(int modeId)
        {
            return GetLocalInt(modeId, ".buildVersion", 0);
        }

        public bool HasPlayerBestRecord(VRCPlayerApi player, int modeId)
        {
            if (!Utilities.IsValid(player) || !IsValidMode(modeId))
            {
                return false;
            }

            var key = GetModePrefix(modeId) + ".hasRecord";
            return PlayerData.HasKey(player, key) && PlayerData.GetBool(player, key);
        }

        public int GetPlayerBestScore(VRCPlayerApi player, int modeId)
        {
            return GetPlayerInt(player, modeId, ".bestScore", 0);
        }

        public int GetPlayerBestRound(VRCPlayerApi player, int modeId)
        {
            return GetPlayerInt(player, modeId, ".bestRound", 0);
        }

        public int GetPlayerBestTotalHits(VRCPlayerApi player, int modeId)
        {
            return GetPlayerInt(player, modeId, ".bestTotalHits", 0);
        }

        public int GetPlayerBestDateYmd(VRCPlayerApi player, int modeId)
        {
            return GetPlayerInt(player, modeId, ".bestDateYmd", 0);
        }

        public int GetPlayerBestRulesetVersion(VRCPlayerApi player, int modeId)
        {
            return GetPlayerInt(player, modeId, ".rulesetVersion", 0);
        }

        public int GetPlayerBestBuildVersion(VRCPlayerApi player, int modeId)
        {
            return GetPlayerInt(player, modeId, ".buildVersion", 0);
        }

        public string GetPlayerRunnerId(VRCPlayerApi player, int modeId)
        {
            if (!Utilities.IsValid(player) || !IsValidMode(modeId))
            {
                return "";
            }

            var key = GetModePrefix(modeId) + ".runnerId";
            return PlayerData.HasKey(player, key) ? PlayerData.GetString(player, key) : "";
        }

        public string GetLocalRunnerId()
        {
            var localPlayer = Networking.LocalPlayer;
            return localPlayerRestored
                   && Utilities.IsValid(localPlayer)
                   && PlayerData.HasKey(localPlayer, KeyRunnerId)
                ? PlayerData.GetString(localPlayer, KeyRunnerId)
                : "";
        }

        private int GetLocalInt(int modeId, string suffix, int fallback)
        {
            var localPlayer = Networking.LocalPlayer;
            if (!localPlayerRestored || !IsValidMode(modeId) || !Utilities.IsValid(localPlayer))
            {
                return fallback;
            }

            var key = GetModePrefix(modeId) + suffix;
            return PlayerData.HasKey(localPlayer, key) ? PlayerData.GetInt(localPlayer, key) : fallback;
        }

        private int GetPlayerInt(VRCPlayerApi player, int modeId, string suffix, int fallback)
        {
            if (!Utilities.IsValid(player) || !IsValidMode(modeId))
            {
                return fallback;
            }

            var key = GetModePrefix(modeId) + suffix;
            return PlayerData.HasKey(player, key) ? PlayerData.GetInt(player, key) : fallback;
        }

        #endregion

        #region Keys And Identity

        private bool IsValidMode(int modeId)
        {
            return modeId == PigeonRunRecordController.ModeA
                   || modeId == PigeonRunRecordController.ModeB
                   || modeId == PigeonRunRecordController.ModeC;
        }

        private int ModeToIndex(int modeId)
        {
            return Mathf.Clamp(modeId - PigeonRunRecordController.ModeA, 0, ModeCount - 1);
        }

        private string GetModePrefix(int modeId)
        {
            if (modeId == PigeonRunRecordController.ModeA)
            {
                return ModeAPrefix;
            }

            return modeId == PigeonRunRecordController.ModeB ? ModeBPrefix : ModeCPrefix;
        }

        private void EnsureLocalRunnerId(VRCPlayerApi player)
        {
            if (PlayerData.HasKey(player, KeyRunnerId))
            {
                var existing = PlayerData.GetString(player, KeyRunnerId);
                if (!string.IsNullOrEmpty(existing) && existing.Length == 32)
                {
                    return;
                }
            }

            var time = Networking.GetServerTimeInMilliseconds();
            var playerId = player.playerId;
            var partC = time ^ 0x5f3759df;
            var partD = playerId ^ time ^ 0x13579bdf;
            var runnerId = time.ToString("x8")
                           + playerId.ToString("x8")
                           + partC.ToString("x8")
                           + partD.ToString("x8");
            PlayerData.SetString(KeyRunnerId, runnerId);
        }

        private void EnsureLocalRecordDate(VRCPlayerApi player, int modeId)
        {
            var prefix = GetModePrefix(modeId);
            if (PlayerData.HasKey(player, prefix + ".hasRecord")
                && PlayerData.GetBool(player, prefix + ".hasRecord")
                && !PlayerData.HasKey(player, prefix + ".bestDateYmd"))
            {
                PlayerData.SetInt(prefix + ".bestDateYmd", GetCurrentUtcDateYmd());
            }
        }

        private int GetCurrentUtcDateYmd()
        {
            var now = System.DateTime.UtcNow;
            return now.Year * 10000 + now.Month * 100 + now.Day;
        }

        #endregion
    }
}
