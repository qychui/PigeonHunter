using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PigeonLocalLeaderboardController : UdonSharpBehaviour
    {
        private const int MaxTrackedPlayers = 100;

        #region Configuration And Runtime State

        [Header("Leaderboard")]
        public Transform leaderboardSlotsParent;
        public int entriesPerPage = 10;

        [Header("Pagination")]
        public TMP_Text pageText;
        public Button previousPageButton;
        public Button nextPageButton;
        public ScrollRect leaderboardScrollRect;

        [Header("Runtime")]
        [SerializeField] private int selectedMode = PigeonRunRecordController.ModeA;

        private readonly int[] restoredPlayerIds = new int[MaxTrackedPlayers];
        private int restoredPlayerCount;
        private bool refreshQueued;
        private int currentPage;
        private int lastSlotCount;

        #endregion

        #region Player And View Lifecycle

        private void Start()
        {
            RequestRefresh();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            MarkPlayerRestored(player);
            RequestRefresh();
        }

        public override void OnPlayerDataUpdated(VRCPlayerApi player, PlayerData.Info[] infos)
        {
            MarkPlayerRestored(player);
            RequestRefresh();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            RequestRefresh();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            RemoveRestoredPlayer(player);
            RequestRefresh();
        }

        public void SetMode(int modeId)
        {
            if (!IsValidMode(modeId))
            {
                return;
            }

            selectedMode = modeId;
            currentPage = 0;
            RequestRefresh();
        }

        public int GetSelectedMode()
        {
            return selectedMode;
        }

        public void RequestRefresh()
        {
            if (refreshQueued)
            {
                return;
            }

            refreshQueued = true;
            SendCustomEventDelayedFrames(nameof(RefreshLeaderboard), 2);
        }

        #endregion

        #region Ranking And Rendering

        public void RefreshLeaderboard()
        {
            refreshQueued = false;
            if (leaderboardSlotsParent == null)
            {
                return;
            }

            var slots = leaderboardSlotsParent.GetComponentsInChildren<LeaderboardSlot>(true);
            var count = slots.Length;
            lastSlotCount = count;
            currentPage = Mathf.Clamp(currentPage, 0, GetPageCount() - 1);

            var scores = new int[count];
            var rounds = new int[count];
            var totalHits = new int[count];
            var datesYmd = new int[count];
            var valid = new bool[count];
            var restored = new bool[count];

            for (var i = 0; i < count; i++)
            {
                var player = slots[i].GetPlayer();
                valid[i] = HasRecord(player);
                restored[i] = valid[i] || IsPlayerRestored(player);
                if (!valid[i])
                {
                    continue;
                }

                scores[i] = GetPlayerInt(player, ".bestScore", 0);
                rounds[i] = GetPlayerInt(player, ".bestRound", 0);
                totalHits[i] = GetPlayerInt(player, ".bestTotalHits", 0);
                datesYmd[i] = GetPlayerInt(player, ".bestDateYmd", 0);
            }

            SortSlots(slots, scores, rounds, totalHits, datesYmd, valid, restored);
            RenderSlots(slots, scores, rounds, totalHits, datesYmd, valid, restored);
            ApplyCurrentPage(slots);
            UpdatePaginationState();
        }

        private void RenderSlots(
            LeaderboardSlot[] slots,
            int[] scores,
            int[] rounds,
            int[] totalHits,
            int[] datesYmd,
            bool[] valid,
            bool[] restored)
        {
            var validPosition = 0;
            var displayedRank = 0;
            var previousScore = -1;
            var previousRound = -1;

            for (var i = 0; i < slots.Length; i++)
            {
                var player = slots[i].GetPlayer();
                var playerName = Utilities.IsValid(player) ? player.displayName : "PLAYER";
                var safePlayerName = playerName.Replace("&", "&\u200B").Replace("<", "<\u200B");

                if (valid[i])
                {
                    validPosition++;
                    if (scores[i] != previousScore || rounds[i] != previousRound)
                    {
                        displayedRank = validPosition;
                    }

                    SetSlotText(slots[i], "PositionText", displayedRank.ToString());
                    SetSlotText(
                        slots[i],
                        "NameText",
                        safePlayerName + "\n<size=75%>HITS " + Mathf.Max(0, totalHits[i]) + "</size>");
                    SetSlotText(
                        slots[i],
                        "ScoreText",
                        FormatScore(scores[i]) + " PTS\n<size=75%>ROUND " + Mathf.Max(1, rounds[i])
                        + "  " + FormatDate(datesYmd[i]) + "</size>");
                    previousScore = scores[i];
                    previousRound = rounds[i];
                }
                else if (restored[i])
                {
                    SetSlotText(slots[i], "PositionText", "-");
                    SetSlotText(slots[i], "NameText", safePlayerName + "\n<size=75%>NO RECORD</size>");
                    SetSlotText(slots[i], "ScoreText", "");
                }
                else
                {
                    SetSlotText(slots[i], "PositionText", "-");
                    SetSlotText(slots[i], "NameText", safePlayerName + "\n<size=75%>LOADING</size>");
                    SetSlotText(slots[i], "ScoreText", "");
                }
            }
        }

        private void SortSlots(
            LeaderboardSlot[] slots,
            int[] scores,
            int[] rounds,
            int[] totalHits,
            int[] datesYmd,
            bool[] valid,
            bool[] restored)
        {
            for (var i = 1; i < slots.Length; i++)
            {
                var slot = slots[i];
                var score = scores[i];
                var reachedRound = rounds[i];
                var hits = totalHits[i];
                var date = datesYmd[i];
                var hasRecord = valid[i];
                var isRestored = restored[i];
                var j = i - 1;

                while (j >= 0 && ShouldRankAfter(
                           valid[j], restored[j], scores[j], rounds[j],
                           hasRecord, isRestored, score, reachedRound))
                {
                    slots[j + 1] = slots[j];
                    scores[j + 1] = scores[j];
                    rounds[j + 1] = rounds[j];
                    totalHits[j + 1] = totalHits[j];
                    datesYmd[j + 1] = datesYmd[j];
                    valid[j + 1] = valid[j];
                    restored[j + 1] = restored[j];
                    j--;
                }

                slots[j + 1] = slot;
                scores[j + 1] = score;
                rounds[j + 1] = reachedRound;
                totalHits[j + 1] = hits;
                datesYmd[j + 1] = date;
                valid[j + 1] = hasRecord;
                restored[j + 1] = isRestored;
            }
        }

        private bool ShouldRankAfter(
            bool leftValid,
            bool leftRestored,
            int leftScore,
            int leftRound,
            bool rightValid,
            bool rightRestored,
            int rightScore,
            int rightRound)
        {
            if (leftValid != rightValid)
            {
                return !leftValid;
            }

            if (leftValid)
            {
                if (leftScore != rightScore)
                {
                    return rightScore > leftScore;
                }

                return rightRound > leftRound;
            }

            return leftRestored != rightRestored && !leftRestored;
        }

        #endregion

        #region Pagination

        public void PreviousPage()
        {
            if (currentPage <= 0)
            {
                return;
            }

            currentPage--;
            RefreshLeaderboard();
        }

        public void NextPage()
        {
            if (currentPage + 1 >= GetPageCount())
            {
                return;
            }

            currentPage++;
            RefreshLeaderboard();
        }

        public void ResetScrollPosition()
        {
            if (leaderboardScrollRect != null)
            {
                leaderboardScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private void ApplyCurrentPage(LeaderboardSlot[] slots)
        {
            var pageSize = Mathf.Max(1, entriesPerPage);
            var first = currentPage * pageSize;
            var last = Mathf.Min(first + pageSize, slots.Length);
            var siblingIndex = 0;

            for (var i = first; i < last; i++)
            {
                SetSlotVisible(slots[i], true);
                slots[i].transform.SetSiblingIndex(siblingIndex++);
            }

            for (var i = 0; i < slots.Length; i++)
            {
                if (i >= first && i < last)
                {
                    continue;
                }

                SetSlotVisible(slots[i], false);
                slots[i].transform.SetSiblingIndex(siblingIndex++);
            }

            ResetScrollPosition();
            SendCustomEventDelayedFrames(nameof(ResetScrollPosition), 1);
        }

        private int GetPageCount()
        {
            var pageSize = Mathf.Max(1, entriesPerPage);
            return Mathf.Max(1, (lastSlotCount + pageSize - 1) / pageSize);
        }

        private void UpdatePaginationState()
        {
            var pageCount = GetPageCount();
            if (pageText != null)
            {
                pageText.text = (currentPage + 1) + " / " + pageCount;
            }

            if (previousPageButton != null)
            {
                previousPageButton.interactable = currentPage > 0;
            }

            if (nextPageButton != null)
            {
                nextPageButton.interactable = currentPage + 1 < pageCount;
            }
        }

        #endregion

        #region PlayerData And UI Helpers

        private bool HasRecord(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
            {
                return false;
            }

            var key = GetModePrefix() + ".hasRecord";
            return PlayerData.HasKey(player, key) && PlayerData.GetBool(player, key);
        }

        private int GetPlayerInt(VRCPlayerApi player, string suffix, int fallback)
        {
            if (!Utilities.IsValid(player))
            {
                return fallback;
            }

            var key = GetModePrefix() + suffix;
            return PlayerData.HasKey(player, key) ? PlayerData.GetInt(player, key) : fallback;
        }

        private string GetModePrefix()
        {
            if (selectedMode == PigeonRunRecordController.ModeA)
            {
                return PigeonLeaderboardPersistence.ModeAPrefix;
            }

            return selectedMode == PigeonRunRecordController.ModeB
                ? PigeonLeaderboardPersistence.ModeBPrefix
                : PigeonLeaderboardPersistence.ModeCPrefix;
        }

        private bool IsValidMode(int modeId)
        {
            return modeId == PigeonRunRecordController.ModeA
                   || modeId == PigeonRunRecordController.ModeB
                   || modeId == PigeonRunRecordController.ModeC;
        }

        private void MarkPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || IsPlayerRestored(player) || restoredPlayerCount >= MaxTrackedPlayers)
            {
                return;
            }

            restoredPlayerIds[restoredPlayerCount] = player.playerId;
            restoredPlayerCount++;
        }

        private bool IsPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
            {
                return false;
            }

            for (var i = 0; i < restoredPlayerCount; i++)
            {
                if (restoredPlayerIds[i] == player.playerId)
                {
                    return true;
                }
            }

            return false;
        }

        private void RemoveRestoredPlayer(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
            {
                return;
            }

            for (var i = 0; i < restoredPlayerCount; i++)
            {
                if (restoredPlayerIds[i] != player.playerId)
                {
                    continue;
                }

                restoredPlayerCount--;
                restoredPlayerIds[i] = restoredPlayerIds[restoredPlayerCount];
                restoredPlayerIds[restoredPlayerCount] = 0;
                return;
            }
        }

        private string FormatScore(int score)
        {
            return Mathf.Max(0, score).ToString("000000");
        }

        private string FormatDate(int dateYmd)
        {
            if (dateYmd <= 0)
            {
                return "---- -- --";
            }

            var year = dateYmd / 10000;
            var month = dateYmd / 100 % 100;
            var day = dateYmd % 100;
            return year.ToString("0000") + "-" + month.ToString("00") + "-" + day.ToString("00");
        }

        private void SetSlotVisible(LeaderboardSlot slot, bool visible)
        {
            SetChildVisible(slot, "Background", visible);
            SetChildVisible(slot, "PositionText", visible);
            SetChildVisible(slot, "NameText", visible);
            SetChildVisible(slot, "ScoreText", visible);
        }

        private void SetChildVisible(LeaderboardSlot slot, string childName, bool visible)
        {
            var child = slot.transform.Find(childName);
            if (child != null && child.gameObject.activeSelf != visible)
            {
                child.gameObject.SetActive(visible);
            }
        }

        private void SetSlotText(LeaderboardSlot slot, string childName, string value)
        {
            var child = slot.transform.Find(childName);
            if (child == null)
            {
                return;
            }

            var text = child.GetComponent<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = value;
            }
        }

        #endregion
    }
}
