using UdonSharp;
using UnityEngine;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ClayTarget : UdonSharpBehaviour
    {
        [Header("References")]
        [SerializeField] private GameManager gameManager;

        [Header("Clay Target Objects")]
        [SerializeField] private GameObject[] clayTargetObjects;

        [Header("Visual State Timeline")]
        [SerializeField] private float[] visualStateWeights;

        [Header("Hit Particle")]
        [SerializeField] private ParticleSystem normalHitParticle;
        [SerializeField] private ParticleSystem goodHitParticle;
        [SerializeField] private ParticleSystem excellentHitParticle;

        [Header("Score")]
        [SerializeField] private int normalHitScore = 1000;
        [SerializeField] private int goodHitScore = 1500;
        [SerializeField] private int excellentHitScore = 2000;
        [Min(0f)]
        [SerializeField] private float hitRecycleDelay = 0.35f;

        [Header("Audio Source")]
        public AudioSource clayTargetFalling;
        public AudioSource clayShootingWhistle;

        private Vector3 startPosition;
        private Vector3 endPosition;
        private Transform movementSpace;
        private float hideHeightY;
        private float peakHeight;
        private float duration;
        private float lifetime;
        private float recycleDelayAfterHide;
        private float elapsed;
        private float hideElapsed;
        private float hitRecycleTimer;
        private bool isActive;
        private bool hasResolved;
        private bool visualsHidden;
        private bool isHitStateActive;
        private int activeVisualIndex = -1;

        public bool IsAvailable => !isActive;
        public bool CanApplySyncedHit => isActive && !hasResolved && !isHitStateActive;
        public bool BelongsTo(GameManager manager)
        {
            return gameManager == manager;
        }

        private void Awake()
        {
            SetVisualIndex(-1);
        }

        private void Update()
        {
            if (!isActive)
            {
                return;
            }

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            elapsed += deltaTime;

            if (isHitStateActive)
            {
                hitRecycleTimer -= deltaTime;
                if (hitRecycleTimer <= 0f)
                {
                    CompleteFlight(true);
                }

                return;
            }

            var t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            var isDescending = t >= 0.5f;
            var position = Vector3.Lerp(startPosition, endPosition, t);
            var jumpOffsetY = peakHeight * 4f * t * (1f - t);
            position.y += jumpOffsetY;
            transform.position = MovementToWorldSpace(position);

            UpdateVisual();

            if (!visualsHidden && isDescending && position.y <= hideHeightY)
            {
                HideVisualsOnly();
            }

            if (visualsHidden && recycleDelayAfterHide > 0f)
            {
                hideElapsed += deltaTime;
            }

            if (ShouldRecycle(t))
            {
                CompleteFlight(false);
            }
        }

        public void SetManager(GameManager manager)
        {
            gameManager = manager;
        }

        public void BeginFlight(
            Vector3 spawnPosition,
            Vector3 targetEndPosition,
            float targetHideHeightY,
            float targetDuration,
            float targetPeakHeight,
            float lifetimeSeconds,
            float targetRecycleDelayAfterHide)
        {
            BeginFlightInternal(spawnPosition, targetEndPosition, targetHideHeightY, targetDuration, targetPeakHeight, lifetimeSeconds, targetRecycleDelayAfterHide, null);
        }

        public void BeginFlightInSpace(
            Vector3 spawnPosition,
            Vector3 targetEndPosition,
            float targetHideHeightY,
            float targetDuration,
            float targetPeakHeight,
            float lifetimeSeconds,
            float targetRecycleDelayAfterHide,
            Transform targetMovementSpace)
        {
            BeginFlightInternal(spawnPosition, targetEndPosition, targetHideHeightY, targetDuration, targetPeakHeight, lifetimeSeconds, targetRecycleDelayAfterHide, targetMovementSpace);
        }

        private void BeginFlightInternal(
            Vector3 spawnPosition,
            Vector3 targetEndPosition,
            float targetHideHeightY,
            float targetDuration,
            float targetPeakHeight,
            float lifetimeSeconds,
            float targetRecycleDelayAfterHide,
            Transform targetMovementSpace)
        {
            movementSpace = targetMovementSpace;
            startPosition = spawnPosition;
            endPosition = targetEndPosition;
            hideHeightY = targetHideHeightY;
            duration = Mathf.Max(0.1f, targetDuration);
            peakHeight = Mathf.Max(0.01f, targetPeakHeight);
            lifetime = Mathf.Max(0.1f, lifetimeSeconds);
            recycleDelayAfterHide = Mathf.Max(0f, targetRecycleDelayAfterHide);
            elapsed = 0f;
            hideElapsed = 0f;
            hitRecycleTimer = 0f;
            hasResolved = false;
            isActive = true;
            visualsHidden = false;
            isHitStateActive = false;

            transform.position = MovementToWorldSpace(startPosition);
            UpdateVisual();

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
        }

        private Vector3 MovementToWorldSpace(Vector3 localPosition)
        {
            if (movementSpace == null)
            {
                return localPosition;
            }

            return movementSpace.TransformPoint(localPosition);
        }

        public void OnShot(Vector3 hitPoint, Vector3 hitNormal)
        {
            if (!isActive || hasResolved)
            {
                return;
            }

            if (gameManager != null)
            {
                gameManager.RegisterClayHit(this);
            }

            EnterHitState(hitPoint, hitNormal);
        }

        public void ApplySyncedHit()
        {
            if (!CanApplySyncedHit)
            {
                return;
            }

            EnterHitState(transform.position, -transform.forward);
        }

        public void DespawnImmediate()
        {
            isActive = false;
            hasResolved = false;
            elapsed = 0f;
            hideElapsed = 0f;
            hitRecycleTimer = 0f;
            duration = 0f;
            visualsHidden = false;
            isHitStateActive = false;
            SetVisualIndex(-1);
            StopFlightAudio();

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void CompleteFlight(bool wasHit)
        {
            var shouldNotify = !hasResolved;
            hasResolved = true;
            isActive = false;
            elapsed = 0f;
            hideElapsed = 0f;
            hitRecycleTimer = 0f;
            duration = 0f;
            visualsHidden = false;
            isHitStateActive = false;
            SetVisualIndex(-1);
            StopFlightAudio();

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            if (shouldNotify && gameManager != null)
            {
                gameManager.NotifyClayAvailable(this, wasHit);
            }
        }

        private void UpdateVisual()
        {
            if (clayTargetObjects == null || clayTargetObjects.Length == 0)
            {
                return;
            }

            if (visualsHidden)
            {
                SetVisualIndex(-1);
                return;
            }

            SetVisualIndex(EvaluateVisualIndex());
        }

        private void HideVisualsOnly()
        {
            visualsHidden = true;
            hideElapsed = 0f;
            SetVisualIndex(-1);
            StopFlightAudio();
        }

        private void EnterHitState(Vector3 hitPoint, Vector3 hitNormal)
        {
            visualsHidden = true;
            isHitStateActive = true;
            hitRecycleTimer = Mathf.Max(0f, hitRecycleDelay);
            SetVisualIndex(-1);
            StopFlightAudio();
            PlayHitParticle(hitPoint, hitNormal);

            if (hitRecycleTimer <= 0f)
            {
                CompleteFlight(true);
            }
        }

        private bool ShouldRecycle(float time01)
        {
            if (elapsed >= lifetime)
            {
                return true;
            }

            if (time01 >= 1f)
            {
                return true;
            }

            if (visualsHidden && recycleDelayAfterHide > 0f && hideElapsed >= recycleDelayAfterHide)
            {
                return true;
            }

            return false;
        }

        private int EvaluateVisualIndex()
        {
            if (visualStateWeights == null || visualStateWeights.Length == 0)
            {
                return 0;
            }

            var stateCount = Mathf.Min(visualStateWeights.Length, clayTargetObjects.Length);
            if (stateCount <= 0)
            {
                return 0;
            }

            var lastValidIndex = stateCount - 1;

            var totalWeight = 0f;
            for (int i = 0; i < stateCount; i++)
            {
                lastValidIndex = i;
                var weight = visualStateWeights[i];
                totalWeight += Mathf.Max(0f, weight);
            }

            if (totalWeight <= 0f)
            {
                return lastValidIndex;
            }

            var life01 = lifetime > 0f ? Mathf.Clamp01(elapsed / lifetime) : 1f;
            var threshold = life01 * totalWeight;
            var accumulatedWeight = 0f;

            for (int i = 0; i < stateCount; i++)
            {
                var weight = visualStateWeights[i];
                accumulatedWeight += Mathf.Max(0f, weight);
                if (threshold <= accumulatedWeight || i == stateCount - 1)
                {
                    return i;
                }
            }

            return lastValidIndex;
        }

        private void SetVisualIndex(int index)
        {
            if (clayTargetObjects == null || clayTargetObjects.Length == 0)
            {
                activeVisualIndex = -1;
                return;
            }

            if (activeVisualIndex == index)
            {
                return;
            }

            activeVisualIndex = index;
            for (int i = 0; i < clayTargetObjects.Length; i++)
            {
                var targetObject = clayTargetObjects[i];
                if (targetObject == null)
                {
                    continue;
                }

                var shouldBeActive = i == activeVisualIndex && isActive;
                if (targetObject.activeSelf != shouldBeActive)
                {
                    targetObject.SetActive(shouldBeActive);
                }

                var collider = targetObject.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = shouldBeActive;
                }
            }
        }

        private void StopFlightAudio()
        {
            QychuiUtilities.SafeStop(clayTargetFalling);
            QychuiUtilities.SafeStop(clayShootingWhistle);
        }

        public int GetScoreForCurrentRound()
        {
            switch (GetHitTierForRound(GetCurrentRoundNumber()))
            {
                case 2:
                    return Mathf.Max(0, excellentHitScore);
                case 1:
                    return Mathf.Max(0, goodHitScore);
                default:
                    return Mathf.Max(0, normalHitScore);
            }
        }

        private void PlayHitParticle(Vector3 hitPoint, Vector3 hitNormal)
        {
            var particle = GetHitParticleForCurrentRound();
            if (particle == null)
            {
                return;
            }

            var particleTransform = particle.transform;
            particleTransform.position = hitPoint;
            QychuiUtilities.SafePlay(particle);
        }

        private ParticleSystem GetHitParticleForCurrentRound()
        {
            switch (GetHitTierForRound(GetCurrentRoundNumber()))
            {
                case 2:
                    return excellentHitParticle;
                case 1:
                    return goodHitParticle;
                default:
                    return normalHitParticle;
            }
        }

        private int GetHitTierForRound(int roundNumber)
        {
            if (roundNumber >= 11)
            {
                return 2;
            }

            if (roundNumber >= 6)
            {
                return 1;
            }

            return 0;
        }

        private int GetCurrentRoundNumber()
        {
            if (gameManager != null && gameManager.uiController != null)
            {
                return Mathf.Max(1, gameManager.uiController.roundLevel);
            }

            return 1;
        }
    }
}
