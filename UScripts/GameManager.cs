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
        public float spawnDelay = 1.5f;

        [Header("Game Mode")]
        [Tooltip("1 = Single mode, 2 = Pair mode")]
        [Range(1, 2)]
        public int gameMode = 1;

        [Header("Difficulty")]
        public float roundDifficultyStep = 0.25f;
        public float hitDifficultyStep = 0.05f;
        public float missDifficultyPenalty = 0.15f;
        public float maxDifficultyMultiplier = 3f;

        [Header("Shot Reaction")]
        [Tooltip("Chance per shot for each flying, unhit pigeon to change direction.")]
        [Range(0f, 1f)]
        public float shotDirectionChangeChance = 0.4f;

        [Header("Play Area")]
        public RectTransform playArea;
        public bool allowLeftEdge = true;
        public bool allowRightEdge = true;
        public bool allowTopEdge = true;
        public bool allowBottomEdge = true;
        [Tooltip("World-space inset applied at both ends of the left edge spawn segment.")]
        [FormerlySerializedAs("leftEdgePadding")]
        public float leftEdgeSpawnSegment = 0.25f;
        [Tooltip("World-space inset applied at both ends of the right edge spawn segment.")]
        [FormerlySerializedAs("rightEdgePadding")]
        public float rightEdgeSpawnSegment = 0.25f;
        [Tooltip("World-space inset applied at both ends of the top edge spawn segment.")]
        [FormerlySerializedAs("topEdgePadding")]
        public float topEdgeSpawnSegment = 0.25f;
        [Tooltip("World-space inset applied at both ends of the bottom edge spawn segment.")]
        [FormerlySerializedAs("bottomEdgePadding")]
        public float bottomEdgeSpawnSegment = 0.25f;

        [Header("References")]
        public PigeonTarget[] pigeonPool;
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

            actionController.HandleInteract();
        }

        private void Update()
        {
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

        public void DebugResetAndRestart()
        {
            exitAnimationActive = false;
            exitAnimationPending = 0;

            if (soundManager != null)
            {
                soundManager.StopAll();
            }

            SetFlyAwayUiActive(false);

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
                actionController.BeginRound();
                actionController.DebugTriggerStartAnimation();
            }
        }

        private bool TryRestartOnGameOver()
        {
            if (actionController == null || !actionController.IsGameOver)
            {
                return false;
            }

            DebugResetAndRestart();
            return true;
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
            return actionController != null && actionController.TryRegisterShot();
        }

        public void NotifyShotOutcome(bool hitPigeon)
        {
            if (actionController != null)
            {
                actionController.NotifyShotOutcome(hitPigeon);
            }
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
    }
}
