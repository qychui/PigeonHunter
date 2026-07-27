using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PigeonGlobalLeaderboardView : UdonSharpBehaviour
    {
        #region Configuration

        [Header("View")]
        public GameObject panelObject;
        public Transform slotsParent;
        public TMP_Text headerText;
        public TMP_Text statusText;
        public TMP_Text pageText;
        public Button previousPageButton;
        public Button nextPageButton;

        #endregion

        #region Runtime State

        private TMP_Text[] positionTexts;
        private TMP_Text[] nameTexts;
        private TMP_Text[] scoreTexts;
        private GameObject[] slotObjects;
        private int[] cachedRanks;
        private string[] cachedPlayerNames;
        private int[] cachedScores;
        private int[] cachedRounds;
        private int[] cachedTotalHits;
        private int[] cachedDatesYmd;
        private string currentTitle = "";
        private string currentStatus = "";
        private int currentPage;
        private bool paginationVisible;

        #endregion

        #region View Lifecycle

        private void Start()
        {
            CacheSlots();
        }

        public void SetVisible(bool visible)
        {
            if (panelObject != null)
            {
                panelObject.SetActive(visible);
            }

            SetPaginationVisible(visible);
        }

        public void SetPaginationVisible(bool visible)
        {
            paginationVisible = visible;
            UpdatePaginationState();
        }

        public void SetStatusMessage(string value)
        {
            SetStatus(value);
        }

        public void ShowLoading(string title)
        {
            ShowMessage(title, "LOADING...");
        }

        public void ShowMessage(string title, string message)
        {
            EnsureSlotsCached();
            currentTitle = title;
            SetHeader(title);
            SetStatus(message);
            cachedPlayerNames = null;
            currentPage = 0;
            SetSlotsActive(false);
            ShowFallbackMessage(message);
            SetPageText();
            UpdatePaginationState();
        }

        public void ShowEntries(
            string title,
            int[] ranks,
            string[] playerNames,
            int[] scores,
            int[] rounds,
            int[] totalHits,
            int[] datesYmd)
        {
            EnsureSlotsCached();
            currentTitle = title;
            SetHeader(title);
            cachedRanks = ranks;
            cachedPlayerNames = playerNames;
            cachedScores = scores;
            cachedRounds = rounds;
            cachedTotalHits = totalHits;
            cachedDatesYmd = datesYmd;
            currentPage = 0;
            RenderCurrentPage();
        }

        #endregion

        #region Pagination And Rendering

        public void PreviousPage()
        {
            if (currentPage <= 0)
            {
                return;
            }

            currentPage--;
            RenderCurrentPage();
        }

        public void NextPage()
        {
            if (currentPage + 1 >= GetPageCount())
            {
                return;
            }

            currentPage++;
            RenderCurrentPage();
        }

        private void RenderCurrentPage()
        {
            EnsureSlotsCached();
            var count = cachedPlayerNames != null ? cachedPlayerNames.Length : 0;
            var slotCount = slotObjects != null ? slotObjects.Length : 0;
            var firstEntry = currentPage * slotCount;

            for (var i = 0; i < slotCount; i++)
            {
                var entryIndex = firstEntry + i;
                var hasEntry = entryIndex < count;
                SetSlotActive(i, hasEntry);
                if (!hasEntry)
                {
                    continue;
                }

                var rank = cachedRanks != null && entryIndex < cachedRanks.Length
                    ? cachedRanks[entryIndex]
                    : entryIndex + 1;
                var playerName = cachedPlayerNames[entryIndex];
                var safePlayerName = string.IsNullOrEmpty(playerName)
                    ? "PLAYER"
                    : playerName.Replace("&", "&\u200B").Replace("<", "<\u200B");
                var hits = cachedTotalHits != null && entryIndex < cachedTotalHits.Length
                    ? cachedTotalHits[entryIndex]
                    : 0;
                var score = cachedScores != null && entryIndex < cachedScores.Length
                    ? cachedScores[entryIndex]
                    : 0;
                var round = cachedRounds != null && entryIndex < cachedRounds.Length
                    ? cachedRounds[entryIndex]
                    : 1;
                var date = cachedDatesYmd != null && entryIndex < cachedDatesYmd.Length
                    ? cachedDatesYmd[entryIndex]
                    : 0;

                SetText(positionTexts[i], Mathf.Max(1, rank).ToString());
                SetText(
                    nameTexts[i],
                    safePlayerName + "\n<size=75%>HITS " + Mathf.Max(0, hits) + "</size>");
                SetText(
                    scoreTexts[i],
                    FormatScore(score) + " PTS\n<size=75%>ROUND " + Mathf.Max(1, round)
                    + "  " + FormatDate(date) + "</size>");
            }

            if (count <= 0)
            {
                ShowFallbackMessage("NO RECORDS");
            }

            SetStatus(count > 0 ? "LOADED" : "NO RECORDS");
            SetPageText();
            UpdatePaginationState();
        }

        private int GetPageCount()
        {
            var count = cachedPlayerNames != null ? cachedPlayerNames.Length : 0;
            var slotCount = slotObjects != null ? slotObjects.Length : 0;
            if (count <= 0 || slotCount <= 0)
            {
                return 1;
            }

            return (count + slotCount - 1) / slotCount;
        }

        private void SetPageText()
        {
            if (pageText != null)
            {
                var value = (currentPage + 1) + " / " + GetPageCount();
                if (statusText == null && !string.IsNullOrEmpty(currentStatus))
                {
                    value += "  " + currentStatus;
                }

                SetText(pageText, value);
            }
        }

        private void UpdatePaginationState()
        {
            var pageCount = GetPageCount();
            if (previousPageButton != null && paginationVisible)
            {
                previousPageButton.interactable = currentPage > 0;
            }

            if (nextPageButton != null && paginationVisible)
            {
                nextPageButton.interactable = currentPage + 1 < pageCount;
            }
        }

        #endregion

        #region Slot Cache And Formatting

        private void EnsureSlotsCached()
        {
            if (slotObjects == null || slotObjects.Length == 0)
            {
                CacheSlots();
            }
        }

        private void CacheSlots()
        {
            var count = slotsParent != null ? slotsParent.childCount : 0;
            positionTexts = new TMP_Text[count];
            nameTexts = new TMP_Text[count];
            scoreTexts = new TMP_Text[count];
            slotObjects = new GameObject[count];

            for (var i = 0; i < count; i++)
            {
                var slot = slotsParent.Find("GlobalLeaderboardSlot " + (i + 1).ToString("00"));
                if (slot == null)
                {
                    slot = slotsParent.GetChild(i);
                }

                slotObjects[i] = slot.gameObject;
                positionTexts[i] = FindText(slot, "PositionText");
                nameTexts[i] = FindText(slot, "NameText");
                scoreTexts[i] = FindText(slot, "ScoreText");
            }
        }

        private void ShowFallbackMessage(string message)
        {
            if (statusText != null || slotObjects == null || slotObjects.Length == 0)
            {
                return;
            }

            SetSlotActive(0, true);
            SetText(positionTexts[0], "-");
            SetText(nameTexts[0], currentTitle);
            SetText(scoreTexts[0], message);
        }

        private void SetSlotsActive(bool active)
        {
            if (slotObjects == null)
            {
                return;
            }

            for (var i = 0; i < slotObjects.Length; i++)
            {
                SetSlotActive(i, active);
            }
        }

        private void SetSlotActive(int index, bool active)
        {
            if (slotObjects != null && index >= 0 && index < slotObjects.Length
                && slotObjects[index] != null && slotObjects[index].activeSelf != active)
            {
                slotObjects[index].SetActive(active);
            }
        }

        private TMP_Text FindText(Transform parent, string childName)
        {
            var child = parent != null ? parent.Find(childName) : null;
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private void SetHeader(string value)
        {
            SetText(headerText, value);
        }

        private void SetStatus(string value)
        {
            currentStatus = value;
            if (statusText != null)
            {
                SetText(statusText, value);
                return;
            }

            SetPageText();
        }

        private void SetText(TMP_Text target, string value)
        {
            if (target != null && target.text != value)
            {
                target.text = value;
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

        #endregion
    }
}
