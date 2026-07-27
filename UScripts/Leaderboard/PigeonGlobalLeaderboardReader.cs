using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PigeonGlobalLeaderboardReader : UdonSharpBehaviour
    {
        private const int DatasetCount = 6;

        #region Configuration

        [Header("View")]
        public PigeonGlobalLeaderboardView view;

        [Header("Mode A / Single")]
        public VRCUrl modeAWeeklyUrl;
        public VRCUrl modeAAllTimeUrl;

        [Header("Mode B / Pair")]
        public VRCUrl modeBWeeklyUrl;
        public VRCUrl modeBAllTimeUrl;

        [Header("Mode C / Range")]
        public VRCUrl modeCWeeklyUrl;
        public VRCUrl modeCAllTimeUrl;

        [Header("Leaderboard IDs")]
        public string modeALeaderboardId = "duck_single_v1";
        public string modeBLeaderboardId = "duck_pair_v1";
        public string modeCLeaderboardId = "duck_range_v1";

        #endregion

        #region Runtime State

        private readonly string[] cachedJson = new string[DatasetCount];
        private int selectedMode = PigeonRunRecordController.ModeA;
        private int selectedScope = PigeonLeaderboardModeController.ScopeWeekly;
        private int activeRequestMode;
        private int activeRequestScope;
        private bool isLoading;

        #endregion

        #region Requests

        public void RequestLeaderboard(int modeId, int scope)
        {
            if (view == null || !IsValidMode(modeId) || !IsExternalScope(scope))
            {
                return;
            }

            selectedMode = modeId;
            selectedScope = scope;

            var cached = GetCachedJson(modeId, scope);
            if (!string.IsNullOrEmpty(cached) && TryReadResponse(cached, modeId, scope, true))
            {
                view.SetStatusMessage("REFRESHING...");
            }
            else
            {
                view.ShowLoading(GetTitle(modeId, scope));
            }

            if (!isLoading)
            {
                StartRequest(modeId, scope);
            }
        }

        public void RefreshCurrent()
        {
            RequestLeaderboard(selectedMode, selectedScope);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            var responseMode = activeRequestMode;
            var responseScope = activeRequestScope;
            isLoading = false;

            var shouldRender = selectedMode == responseMode && selectedScope == responseScope;
            if (TryReadResponse(result.Result, responseMode, responseScope, shouldRender))
            {
                SetCachedJson(responseMode, responseScope, result.Result);
                if (shouldRender)
                {
                    view.SetStatusMessage("UPDATED");
                }
            }
            else if (shouldRender)
            {
                RestoreCachedOrShowError(responseMode, responseScope, "INVALID SERVER DATA");
            }

            ContinueSelectedRequest(responseMode, responseScope);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            var responseMode = activeRequestMode;
            var responseScope = activeRequestScope;
            isLoading = false;

            if (selectedMode == responseMode && selectedScope == responseScope)
            {
                RestoreCachedOrShowError(
                    responseMode,
                    responseScope,
                    "LOAD FAILED (" + result.ErrorCode + ")");
            }

            ContinueSelectedRequest(responseMode, responseScope);
        }

        private void StartRequest(int modeId, int scope)
        {
            var url = GetUrl(modeId, scope);
            if (VRCUrl.IsNullOrEmpty(url))
            {
                RestoreCachedOrShowError(modeId, scope, "API URL MISSING");
                return;
            }

            activeRequestMode = modeId;
            activeRequestScope = scope;
            isLoading = true;
            VRCStringDownloader.LoadUrl(url, this);
        }

        private void ContinueSelectedRequest(int completedMode, int completedScope)
        {
            if (selectedMode != completedMode || selectedScope != completedScope)
            {
                StartRequest(selectedMode, selectedScope);
            }
        }

        private void RestoreCachedOrShowError(int modeId, int scope, string message)
        {
            var cached = GetCachedJson(modeId, scope);
            if (!string.IsNullOrEmpty(cached))
            {
                TryReadResponse(cached, modeId, scope, true);
            }
            else
            {
                view.ShowMessage(GetTitle(modeId, scope), message);
                return;
            }

            view.SetStatusMessage(message);
        }

        #endregion

        #region Response Parsing

        private bool TryReadResponse(string json, int modeId, int scope, bool render)
        {
            DataToken rootToken;
            if (!VRCJson.TryDeserializeFromJson(json, out rootToken)
                || rootToken.TokenType != TokenType.DataDictionary)
            {
                return false;
            }

            var root = rootToken.DataDictionary;
            DataToken schemaToken;
            DataToken boardToken;
            DataToken periodToken;
            DataToken entriesToken;
            if (!root.TryGetValue("schemaVersion", out schemaToken)
                || (int)schemaToken.Double != 1
                || !root.TryGetValue("leaderboardId", out boardToken)
                || boardToken.String != GetLeaderboardId(modeId)
                || !root.TryGetValue("period", out periodToken)
                || periodToken.String != GetPeriodName(scope)
                || !root.TryGetValue("entries", out entriesToken)
                || entriesToken.TokenType != TokenType.DataList)
            {
                return false;
            }

            var entries = entriesToken.DataList;
            var count = entries.Count;
            var ranks = new int[count];
            var names = new string[count];
            var scores = new int[count];
            var rounds = new int[count];
            var totalHits = new int[count];
            var dates = new int[count];

            for (var i = 0; i < count; i++)
            {
                var entryToken = entries[i];
                if (entryToken.TokenType != TokenType.DataDictionary)
                {
                    return false;
                }

                var entry = entryToken.DataDictionary;
                DataToken rankToken;
                DataToken runnerToken;
                DataToken nameToken;
                DataToken scoreToken;
                DataToken roundToken;
                DataToken hitsToken;
                DataToken dateToken;
                if (!entry.TryGetValue("rank", out rankToken)
                    || !entry.TryGetValue("runnerId", out runnerToken)
                    || !entry.TryGetValue("displayName", out nameToken)
                    || !entry.TryGetValue("score", out scoreToken)
                    || !entry.TryGetValue("reachedRound", out roundToken)
                    || !entry.TryGetValue("totalHits", out hitsToken)
                    || !entry.TryGetValue("submittedDate", out dateToken))
                {
                    return false;
                }

                var rank = (int)rankToken.Double;
                var runnerId = runnerToken.String;
                var displayName = nameToken.String;
                var score = (int)scoreToken.Double;
                var reachedRound = (int)roundToken.Double;
                var hits = (int)hitsToken.Double;
                var date = ParseDate(dateToken.String);
                if (rank < 1 || string.IsNullOrEmpty(runnerId) || runnerId.Length != 32
                    || string.IsNullOrEmpty(displayName) || score < 0 || reachedRound < 1
                    || hits < 0 || date <= 0)
                {
                    return false;
                }

                ranks[i] = rank;
                names[i] = displayName;
                scores[i] = score;
                rounds[i] = reachedRound;
                totalHits[i] = hits;
                dates[i] = date;
            }

            if (render)
            {
                view.ShowEntries(GetTitle(modeId, scope), ranks, names, scores, rounds, totalHits, dates);
            }

            return true;
        }

        private int ParseDate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 10
                || value[4] != '-' || value[7] != '-')
            {
                return 0;
            }

            int year;
            int month;
            int day;
            if (!int.TryParse(value.Substring(0, 4), out year)
                || !int.TryParse(value.Substring(5, 2), out month)
                || !int.TryParse(value.Substring(8, 2), out day)
                || year < 2000 || month < 1 || month > 12 || day < 1 || day > 31)
            {
                return 0;
            }

            return year * 10000 + month * 100 + day;
        }

        #endregion

        #region Dataset Mapping

        private string GetCachedJson(int modeId, int scope)
        {
            var index = GetDatasetIndex(modeId, scope);
            return index >= 0 ? cachedJson[index] : "";
        }

        private void SetCachedJson(int modeId, int scope, string json)
        {
            var index = GetDatasetIndex(modeId, scope);
            if (index >= 0)
            {
                cachedJson[index] = json;
            }
        }

        private int GetDatasetIndex(int modeId, int scope)
        {
            if (!IsValidMode(modeId) || !IsExternalScope(scope))
            {
                return -1;
            }

            return (modeId - PigeonRunRecordController.ModeA) * 2
                   + (scope == PigeonLeaderboardModeController.ScopeWeekly ? 0 : 1);
        }

        private VRCUrl GetUrl(int modeId, int scope)
        {
            if (modeId == PigeonRunRecordController.ModeA)
            {
                return scope == PigeonLeaderboardModeController.ScopeWeekly
                    ? modeAWeeklyUrl
                    : modeAAllTimeUrl;
            }

            if (modeId == PigeonRunRecordController.ModeB)
            {
                return scope == PigeonLeaderboardModeController.ScopeWeekly
                    ? modeBWeeklyUrl
                    : modeBAllTimeUrl;
            }

            return scope == PigeonLeaderboardModeController.ScopeWeekly
                ? modeCWeeklyUrl
                : modeCAllTimeUrl;
        }

        private string GetLeaderboardId(int modeId)
        {
            if (modeId == PigeonRunRecordController.ModeA)
            {
                return modeALeaderboardId;
            }

            return modeId == PigeonRunRecordController.ModeB
                ? modeBLeaderboardId
                : modeCLeaderboardId;
        }

        private string GetPeriodName(int scope)
        {
            return scope == PigeonLeaderboardModeController.ScopeWeekly ? "week" : "all";
        }

        private string GetTitle(int modeId, int scope)
        {
            var mode = modeId == PigeonRunRecordController.ModeA
                ? "SINGLE"
                : modeId == PigeonRunRecordController.ModeB ? "PAIR" : "RANGE";
            var period = scope == PigeonLeaderboardModeController.ScopeWeekly
                ? "WEEKLY"
                : "ALL TIME";
            return mode + " - " + period;
        }

        private bool IsValidMode(int modeId)
        {
            return modeId == PigeonRunRecordController.ModeA
                   || modeId == PigeonRunRecordController.ModeB
                   || modeId == PigeonRunRecordController.ModeC;
        }

        private bool IsExternalScope(int scope)
        {
            return scope == PigeonLeaderboardModeController.ScopeWeekly
                   || scope == PigeonLeaderboardModeController.ScopeAllTime;
        }

        #endregion
    }
}
