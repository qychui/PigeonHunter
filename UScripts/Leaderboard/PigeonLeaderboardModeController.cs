using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PigeonLeaderboardModeController : UdonSharpBehaviour
    {
        public const int ScopeLocal = 0;
        public const int ScopeWeekly = 1;
        public const int ScopeAllTime = 2;

        #region Configuration

        [Header("Views")]
        public GameObject localPanel;
        public GameObject globalPanel;

        [Header("Data")]
        public PigeonLocalLeaderboardController localLeaderboard;
        public PigeonGlobalLeaderboardReader globalReader;
        public PigeonGlobalLeaderboardView globalView;

        [Header("Mode Buttons")]
        public Button modeAButton;
        public Button modeBButton;
        public Button modeCButton;

        [Header("Scope Buttons")]
        public Button localButton;
        public Button weeklyButton;
        public Button allTimeButton;

        [Header("Scope Availability")]
        public bool weeklyAvailable;
        public bool allTimeAvailable;

        [Header("Button Colors")]
        public Color normalButtonColor = Color.white;
        public Color selectedButtonColor = new Color(0.35f, 0.8f, 0.5f, 1f);
        public Color unavailableButtonColor = new Color(0.45f, 0.45f, 0.45f, 0.5f);

        [Header("Runtime")]
        [SerializeField] private int currentGameMode = PigeonRunRecordController.ModeA;
        [SerializeField] private int currentBoardScope = ScopeLocal;

        #endregion

        private void Start()
        {
            currentGameMode = PigeonRunRecordController.ModeA;
            currentBoardScope = ScopeLocal;
            ApplyViewState();
            SetMode(currentGameMode);
            UpdateButtonStates();
        }

        public void ShowModeA()
        {
            SetMode(PigeonRunRecordController.ModeA);
        }

        public void ShowModeB()
        {
            SetMode(PigeonRunRecordController.ModeB);
        }

        public void ShowModeC()
        {
            SetMode(PigeonRunRecordController.ModeC);
        }

        public void ShowLocal()
        {
            SetScope(ScopeLocal);
        }

        public void ShowWeekly()
        {
            SetScope(ScopeWeekly);
        }

        public void ShowAllTime()
        {
            SetScope(ScopeAllTime);
        }

        public int GetCurrentGameMode()
        {
            return currentGameMode;
        }

        public int GetCurrentBoardScope()
        {
            return currentBoardScope;
        }

        public void PreviousPage()
        {
            if (currentBoardScope == ScopeLocal && localLeaderboard != null)
            {
                localLeaderboard.PreviousPage();
            }
            else if (currentBoardScope != ScopeLocal && globalView != null)
            {
                globalView.PreviousPage();
            }
        }

        public void NextPage()
        {
            if (currentBoardScope == ScopeLocal && localLeaderboard != null)
            {
                localLeaderboard.NextPage();
            }
            else if (currentBoardScope != ScopeLocal && globalView != null)
            {
                globalView.NextPage();
            }
        }

        private void SetMode(int modeId)
        {
            if (!IsValidMode(modeId))
            {
                return;
            }

            currentGameMode = modeId;
            if (localLeaderboard != null)
            {
                localLeaderboard.SetMode(modeId);
            }

            if (currentBoardScope != ScopeLocal && globalReader != null)
            {
                globalReader.RequestLeaderboard(currentGameMode, currentBoardScope);
            }

            UpdateButtonStates();
        }

        private void SetScope(int scope)
        {
            if (!IsScopeAvailable(scope))
            {
                return;
            }

            currentBoardScope = scope;
            ApplyViewState();
            UpdateButtonStates();

            if (scope == ScopeLocal && localLeaderboard != null)
            {
                localLeaderboard.RequestRefresh();
            }
            else if (scope != ScopeLocal && globalReader != null)
            {
                globalReader.RequestLeaderboard(currentGameMode, currentBoardScope);
            }
        }

        private void ApplyViewState()
        {
            var showLocal = currentBoardScope == ScopeLocal;
            if (localPanel != null)
            {
                localPanel.SetActive(showLocal);
            }

            if (globalPanel != null)
            {
                globalPanel.SetActive(!showLocal);
            }

            if (globalView != null)
            {
                globalView.SetPaginationVisible(!showLocal);
            }
        }

        private void UpdateButtonStates()
        {
            SetButtonState(
                modeAButton,
                currentGameMode == PigeonRunRecordController.ModeA,
                true);
            SetButtonState(
                modeBButton,
                currentGameMode == PigeonRunRecordController.ModeB,
                true);
            SetButtonState(
                modeCButton,
                currentGameMode == PigeonRunRecordController.ModeC,
                true);

            SetButtonState(localButton, currentBoardScope == ScopeLocal, true);
            SetButtonState(weeklyButton, currentBoardScope == ScopeWeekly, weeklyAvailable);
            SetButtonState(allTimeButton, currentBoardScope == ScopeAllTime, allTimeAvailable);
        }

        private void SetButtonState(Button button, bool selected, bool available)
        {
            if (button == null)
            {
                return;
            }

            button.interactable = available && !selected;
            if (button.targetGraphic != null)
            {
                button.targetGraphic.color = selected
                    ? selectedButtonColor
                    : available ? normalButtonColor : unavailableButtonColor;
            }
        }

        private bool IsValidMode(int modeId)
        {
            return modeId == PigeonRunRecordController.ModeA
                   || modeId == PigeonRunRecordController.ModeB
                   || modeId == PigeonRunRecordController.ModeC;
        }

        private bool IsScopeAvailable(int scope)
        {
            if (scope == ScopeLocal)
            {
                return true;
            }

            if (scope == ScopeWeekly)
            {
                return weeklyAvailable;
            }

            return scope == ScopeAllTime && allTimeAvailable;
        }
    }
}
