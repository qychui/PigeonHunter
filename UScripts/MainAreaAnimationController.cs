using UdonSharp;
using UnityEngine;

namespace PigeonHunt
{
    internal enum DogMovementType
    {
        None,
        Hit,
        Miss
    }

    [AddComponentMenu("PigeonHunt/MainAreaAnimation Controller")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MainAreaAnimationController : UdonSharpBehaviour
    {
        [Header("Animator Reference")]
        [SerializeField] private Animator animator;

        [Header("Movement Area Reference")]
        [SerializeField] private RectTransform movementArea;

        [Header("Animator Parameters")]
        [SerializeField] private string stateParameter = "DogState";

        [Header("Linked Objects")]
        [SerializeField] private GameObject dogMainObject;
        [SerializeField] private GameObject lmfao;
        [SerializeField] private GameObject got;
        [SerializeField] private GameObject roundStart;
        [SerializeField] private GameObject roundEnd;
        [SerializeField] private GameObject roundNext;

        [Header("State Values")]
        [Tooltip("Default idle or standby animation state.")]
        public int idleState = 0;
        public int gameStartState = 1;
        public int hitState = 2;
        public int missState = 3;
        public int roundEndState = 4;
        public int roundNextState = 5;

        [Header("Animation Durations (seconds)")]
        [Min(0f)]
        [SerializeField] private float gameStartDuration = 0f;
        [Min(0f)]
        [SerializeField] private float gameStartDelay = 0f;

        [Header("Round Start Movement")]
        [Min(0f)]
        [SerializeField] private float roundStartMoveSpeed = 0.09f;
        [Min(0f)]
        [SerializeField] private float roundStartMoveDuration = 2.65f;
        [Min(0f)]
        [SerializeField] private float roundStartSecondMoveDuration = 2.65f;
        [Min(0f)]
        [SerializeField] private float roundStartPauseDuration = 1.15f;
        [Min(0f)]
        [SerializeField] private float roundStartSecondPauseDuration = 0.75f;
        [Tooltip("Optional rect used to determine the width used for normalized movement.")]
        [Min(0f)]
        [SerializeField] private float roundStartMovementFallbackWidth = 1f;
        [Tooltip("Duration of the final arc-style motion segment.")]
        [Min(0f)]
        [SerializeField] private float roundStartArcDuration = 1.5f;
        [Tooltip("Normalized horizontal distance (relative to the reference width) covered during the arc.")]
        [Min(0f)]
        [SerializeField] private float roundStartArcHorizontalDistance = 0.35f;
        [Tooltip("Normalized vertical height (relative to the reference width) reached at the arc peak.")]
        [Min(0f)]
        [SerializeField] private float roundStartArcVerticalDistance = 0.18f;
        [Tooltip("Speed multiplier applied to the arc movement (1 = use duration as-is).")]
        [Min(0f)]
        [SerializeField] private float roundStartArcMoveSpeed = 1f;
        [Tooltip("Additional normalized distance to drop below the baseline near the end of the arc.")]
        [Min(0f)]
        [SerializeField] private float roundStartArcExtraDropDistance = 0f;

        [Header("Hit Movement")]
        [Min(0f)]
        [SerializeField] private float hitMoveUpDelay = 0f;
        [Min(0f)]
        [SerializeField] private float hitMoveUpSpeed = 0.08f;
        [Min(0f)]
        [SerializeField] private float hitMoveUpDuration = 0.45f;
        [Min(0f)]
        [SerializeField] private float hitSlowdownDuration = 0.35f;
        [Min(0f)]
        [SerializeField] private float hitPauseDuration = 0.4f;
        [Min(0f)]
        [SerializeField] private float hitDownAccelerationDuration = 0.4f;
        [Min(0f)]
        [SerializeField] private float hitMoveDownSpeed = 0.08f;
        [Min(0f)]
        [SerializeField] private float hitMoveDownDuration = 0.55f;
        [Min(0f)]
        [SerializeField] private float hitMovementFallbackHeight = 1f;
        [Min(0f)]
        [SerializeField] private float hitResetDelay = 0f;
        [Header("Miss Movement")]
        [Min(0f)]
        [SerializeField] private float missMoveUpDelay = 0f;
        [Min(0f)]
        [SerializeField] private float missMoveUpSpeed = 0.08f;
        [Min(0f)]
        [SerializeField] private float missMoveUpDuration = 0.45f;
        [Min(0f)]
        [SerializeField] private float missSlowdownDuration = 0.35f;
        [Min(0f)]
        [SerializeField] private float missPauseDuration = 0.4f;
        [Min(0f)]
        [SerializeField] private float missDownAccelerationDuration = 0.4f;
        [Min(0f)]
        [SerializeField] private float missMoveDownSpeed = 0.08f;
        [Min(0f)]
        [SerializeField] private float missMoveDownDuration = 0.55f;
        [Min(0f)]
        [SerializeField] private float missMovementFallbackHeight = 1f;
        [Min(0f)]
        [SerializeField] private float missResetDelay = 0f;

        private int stateParameterHash;
        private int currentState = int.MinValue;
        private bool initialized;

        private const int RoundStartPhaseFirstMove = 0;
        private const int RoundStartPhaseFirstPause = 1;
        private const int RoundStartPhaseSecondMove = 2;
        private const int RoundStartPhaseSecondPause = 3;
        private const int RoundStartPhaseArcMove = 4;
        private const int RoundStartPhaseComplete = 5;

        private bool roundStartMovementActive;
        private int roundStartMovementPhase = RoundStartPhaseComplete;
        private float roundStartMovementPhaseTimer;
        private Transform roundStartMovementTarget;
        private float roundStartArcElapsed;
        private bool roundStartArcPeakTriggered;
        private Vector3 roundStartArcBasePosition;
        private Vector3 roundStartBasePosition;
        private Quaternion roundStartBaseRotation;
        private bool roundStartBaseCached;
        private readonly Vector3[] roundStartMovementAreaCorners = new Vector3[4];
        private const int MovementPhaseMoveUp = 0;
        private const int MovementPhaseSlowdown = 1;
        private const int MovementPhasePause = 2;
        private const int MovementPhaseAccelerateDown = 3;
        private const int MovementPhaseMoveDown = 4;
        private const int MovementPhaseComplete = 5;

        private bool movementActive;
        private DogMovementType activeMovementType = DogMovementType.None;
        private int movementPhase = MovementPhaseComplete;
        private float movementPhaseTimer;
        private Transform movementTarget;
        private float movementMoveUpSpeedWorld;
        private float movementMoveDownSpeedWorld;
        private float movementSlowdownRate;
        private float movementDownAccelerationRate;
        private float movementCurrentVerticalSpeed;
        private float movementVerticalOffset;
        private Vector3 movementBasePosition;
        private readonly Vector3[] movementAreaCorners = new Vector3[4];
        private bool movementRequestPending;
        private float movementRequestTimer;
        private DogMovementType movementRequestType = DogMovementType.None;
        private bool stateResetPending;
        private float stateResetTimer;
        private int stateResetSourceState = int.MinValue;

        public float GameStartDuration => Mathf.Max(0f, gameStartDuration);
        public float GameStartDelay => Mathf.Max(0f, gameStartDelay);

        private void Start()
        {
            EnsureInitialized();
            CacheRoundStartPose();
        }

        private void Update()
        {
            if (roundStartMovementActive)
            {
                UpdateRoundStartMovement();
            }

            if (movementRequestPending)
            {
                UpdateMovementRequestDelay();
            }

            if (movementActive)
            {
                UpdateMovementSequence();
            }

            if (stateResetPending)
            {
                UpdateStateReset();
            }
        }

        public void PlayGameStartAnimation()
        {
            ApplyState(gameStartState);
        }

        public void PlayHitAnimation()
        {
            RequestMovement(DogMovementType.Hit);
        }

        public void PlayMissAnimation()
        {
            RequestMovement(DogMovementType.Miss);
        }

        public void ResetToIdle()
        {
            stateResetPending = false;
            stateResetTimer = 0f;
            stateResetSourceState = int.MinValue;
            StopMovement(true);
            ApplyState(idleState);
        }

        private void RequestMovement(DogMovementType type)
        {
            if (type == DogMovementType.None)
            {
                return;
            }

            var delay = GetMoveUpDelay(type);
            if (delay > 0f)
            {
                movementRequestPending = true;
                movementRequestType = type;
                movementRequestTimer = delay;
                StopMovement(false);
                return;
            }

            TriggerMovementState(type);
        }

        private void TriggerMovementState(DogMovementType type)
        {
            movementRequestPending = false;
            movementRequestType = DogMovementType.None;
            movementRequestTimer = 0f;

            var targetState = type == DogMovementType.Hit ? hitState :
                              type == DogMovementType.Miss ? missState : idleState;
            ApplyState(targetState);
            StartMovementSequence(type);
            ScheduleStateReset(type, targetState);
        }

        private void ScheduleStateReset(DogMovementType type, int targetState)
        {
            stateResetPending = false;
            stateResetTimer = 0f;
            stateResetSourceState = int.MinValue;

            if (type == DogMovementType.None)
            {
                return;
            }

            var delay = GetResetDelay(type);
            if (delay <= 0f)
            {
                return;
            }

            stateResetPending = true;
            stateResetTimer = delay;
            stateResetSourceState = targetState;
        }

        public void PlayRoundStartMovementSequence()
        {
            if (roundStartMovementActive)
            {
                return;
            }

            if (roundStart == null)
            {
                return;
            }

            if (!roundStartBaseCached)
            {
                CacheRoundStartPose();
            }

            if (roundStartBaseCached)
            {
                roundStart.transform.position = roundStartBasePosition;
                roundStart.transform.rotation = roundStartBaseRotation;
            }

            roundStartMovementTarget = roundStart.transform;
            if (roundStartMovementTarget == null)
            {
                roundStartMovementActive = false;
                roundStartMovementPhase = RoundStartPhaseComplete;
                return;
            }

            roundStartMovementActive = true;
            roundStartMovementPhase = RoundStartPhaseFirstMove;
            roundStartMovementPhaseTimer = Mathf.Max(0f, roundStartMoveDuration);

            if (roundStartMovementPhaseTimer <= 0f)
            {
                AdvanceRoundStartMovementPhase();
            }
        }

        private void StartMovementSequence(DogMovementType type)
        {
            if (type == DogMovementType.None)
            {
                StopMovement(false);
                return;
            }

            var target = GetMovementTarget(type);
            if (target == null)
            {
                StopMovement(false);
                return;
            }

            StopMovement(false);

            activeMovementType = type;
            movementTarget = target;
            movementBasePosition = target.position;
            movementVerticalOffset = 0f;

            var referenceHeight = Mathf.Max(0.0001f, GetMovementReferenceHeight(type));
            movementMoveUpSpeedWorld = Mathf.Max(0f, GetMoveUpSpeed(type)) * referenceHeight;
            movementMoveDownSpeedWorld = Mathf.Max(0f, GetMoveDownSpeed(type)) * referenceHeight;

            var slowdownDuration = GetSlowdownDuration(type);
            movementSlowdownRate = slowdownDuration > 0f ? movementMoveUpSpeedWorld / slowdownDuration : movementMoveUpSpeedWorld;

            var downAccelerationDuration = GetDownAccelerationDuration(type);
            movementDownAccelerationRate = downAccelerationDuration > 0f ? movementMoveDownSpeedWorld / downAccelerationDuration : movementMoveDownSpeedWorld;

            movementActive = true;
            movementPhase = MovementPhaseMoveUp;
            movementCurrentVerticalSpeed = movementMoveUpSpeedWorld;
            movementPhaseTimer = Mathf.Max(0f, GetMoveUpDuration(type));

            if (movementPhaseTimer <= 0f)
            {
                AdvanceMovementPhase();
            }
        }

        public void PlayState(int customStateValue)
        {
            ApplyState(customStateValue);
        }

        public int GetCurrentState()
        {
            return currentState;
        }

        private void ApplyState(int targetState)
        {
            EnsureInitialized();

            if (!initialized || animator == null)
            {
                return;
            }

            if (currentState == targetState)
            {
                return;
            }

            currentState = targetState;
            animator.SetInteger(stateParameterHash, targetState);
        }

        private void UpdateMovementRequestDelay()
        {
            if (!movementRequestPending)
            {
                return;
            }

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            movementRequestTimer -= deltaTime;
            if (movementRequestTimer <= 0f)
            {
                TriggerMovementState(movementRequestType);
            }
        }

        private void UpdateStateReset()
        {
            if (!stateResetPending)
            {
                return;
            }

            if (currentState != stateResetSourceState)
            {
                stateResetPending = false;
                stateResetTimer = 0f;
                stateResetSourceState = int.MinValue;
                return;
            }

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            stateResetTimer -= deltaTime;
            if (stateResetTimer <= 0f)
            {
                stateResetPending = false;
                stateResetTimer = 0f;
                stateResetSourceState = int.MinValue;
                ResetToIdle();
            }
        }

        public Transform GetTransformForState(int targetState)
        {
            if (targetState == gameStartState && roundStart != null)
            {
                return roundStart.transform;
            }

            if (targetState == hitState && got != null)
            {
                return got.transform;
            }

            if (targetState == missState && lmfao != null)
            {
                return lmfao.transform;
            }

            return null;
        }

        public float GetResolutionAnimationDuration(bool wasHit)
        {
            var target = GetTransformForState(wasHit ? hitState : missState);
            if (target == null)
            {
                return 0f;
            }

            if (wasHit)
            {
                return Mathf.Max(0f, hitMoveUpDelay) +
                       Mathf.Max(0f, hitMoveUpDuration) +
                       Mathf.Max(0f, hitSlowdownDuration) +
                       Mathf.Max(0f, hitPauseDuration) +
                       Mathf.Max(0f, hitDownAccelerationDuration) +
                       Mathf.Max(0f, hitMoveDownDuration) +
                       Mathf.Max(0f, hitResetDelay);
            }

            return Mathf.Max(0f, missMoveUpDelay) +
                   Mathf.Max(0f, missMoveUpDuration) +
                   Mathf.Max(0f, missSlowdownDuration) +
                   Mathf.Max(0f, missPauseDuration) +
                   Mathf.Max(0f, missDownAccelerationDuration) +
                   Mathf.Max(0f, missMoveDownDuration) +
                   Mathf.Max(0f, missResetDelay);
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            if (animator == null)
            {
                animator = GetComponent<Animator>();
                if (animator == null)
                {
                    animator = GetComponentInChildren<Animator>();
                }
            }

            if (animator == null)
            {
                Debug.LogWarning("[AnimationController] Missing Animator reference, animations will not play.");
                return;
            }

            if (string.IsNullOrEmpty(stateParameter))
            {
                Debug.LogWarning("[AnimationController] Animator state parameter name is empty.");
                return;
            }

            stateParameterHash = Animator.StringToHash(stateParameter);
            if (stateParameterHash == 0)
            {
                Debug.LogWarning("[AnimationController] Failed to create hash for parameter: " + stateParameter);
                return;
            }

            initialized = true;
            ApplyState(idleState);
        }

        private void UpdateMovementSequence()
        {
            if (!movementActive)
            {
                return;
            }

            if (movementTarget == null)
            {
                StopMovement(false);
                return;
            }

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            switch (movementPhase)
            {
                case MovementPhaseMoveUp:
                    movementCurrentVerticalSpeed = movementMoveUpSpeedWorld;
                    ApplyMovementDelta(movementCurrentVerticalSpeed, deltaTime);
                    break;
                case MovementPhaseSlowdown:
                    movementCurrentVerticalSpeed = Mathf.Max(0f, movementCurrentVerticalSpeed - movementSlowdownRate * deltaTime);
                    ApplyMovementDelta(movementCurrentVerticalSpeed, deltaTime);
                    break;
                case MovementPhasePause:
                    movementCurrentVerticalSpeed = 0f;
                    break;
                case MovementPhaseAccelerateDown:
                    movementCurrentVerticalSpeed = Mathf.Max(-movementMoveDownSpeedWorld, movementCurrentVerticalSpeed - movementDownAccelerationRate * deltaTime);
                    ApplyMovementDelta(movementCurrentVerticalSpeed, deltaTime);
                    break;
                case MovementPhaseMoveDown:
                    movementCurrentVerticalSpeed = -movementMoveDownSpeedWorld;
                    ApplyMovementDelta(movementCurrentVerticalSpeed, deltaTime);
                    break;
            }

            movementPhaseTimer -= deltaTime;
            if (movementPhaseTimer <= 0f)
            {
                AdvanceMovementPhase();
            }
        }

        private void AdvanceMovementPhase()
        {
            while (movementActive)
            {
                if (movementPhase == MovementPhaseMoveUp)
                {
                    movementPhase = MovementPhaseSlowdown;
                    movementPhaseTimer = Mathf.Max(0f, GetSlowdownDuration(activeMovementType));
                }
                else if (movementPhase == MovementPhaseSlowdown)
                {
                    movementPhase = MovementPhasePause;
                    movementPhaseTimer = Mathf.Max(0f, GetPauseDuration(activeMovementType));
                    movementCurrentVerticalSpeed = 0f;
                }
                else if (movementPhase == MovementPhasePause)
                {
                    movementPhase = MovementPhaseAccelerateDown;
                    movementPhaseTimer = Mathf.Max(0f, GetDownAccelerationDuration(activeMovementType));
                    movementCurrentVerticalSpeed = 0f;
                }
                else if (movementPhase == MovementPhaseAccelerateDown)
                {
                    movementPhase = MovementPhaseMoveDown;
                    movementPhaseTimer = Mathf.Max(0f, GetMoveDownDuration(activeMovementType));
                    movementCurrentVerticalSpeed = -movementMoveDownSpeedWorld;
                }
                else
                {
                    CompleteMovement();
                    break;
                }

                if (movementPhaseTimer > 0f)
                {
                    break;
                }
            }
        }

        private void ApplyMovementDelta(float velocity, float deltaTime)
        {
            if (movementTarget == null)
            {
                return;
            }

            var delta = velocity * deltaTime;
            if (Mathf.Approximately(delta, 0f))
            {
                return;
            }

            movementVerticalOffset += delta;
            movementTarget.position = movementBasePosition + Vector3.up * movementVerticalOffset;
        }

        private void CompleteMovement()
        {
            StopMovement(true);
        }

        private void StopMovement(bool resetPositionToBase)
        {
            if (movementTarget != null && resetPositionToBase)
            {
                movementTarget.position = movementBasePosition;
            }

            movementActive = false;
            activeMovementType = DogMovementType.None;
            movementPhase = MovementPhaseComplete;
            movementPhaseTimer = 0f;
            movementTarget = null;
            movementVerticalOffset = 0f;
            movementCurrentVerticalSpeed = 0f;
        }


        private void UpdateRoundStartMovement()
        {
            if (roundStartMovementTarget == null)
            {
                roundStartMovementActive = false;
                roundStartMovementPhase = RoundStartPhaseComplete;
                return;
            }

            var deltaTime = Time.deltaTime;
            if (roundStartMovementPhase == RoundStartPhaseFirstMove || roundStartMovementPhase == RoundStartPhaseSecondMove)
            {
                MoveRoundStartTarget(deltaTime);
            }
            else if (roundStartMovementPhase == RoundStartPhaseArcMove)
            {
                MoveRoundStartArc(deltaTime);
            }

            roundStartMovementPhaseTimer -= deltaTime;
            if (roundStartMovementPhaseTimer <= 0f)
            {
                AdvanceRoundStartMovementPhase();
            }
        }

        private void AdvanceRoundStartMovementPhase()
        {
            while (true)
            {
                if (roundStartMovementPhase == RoundStartPhaseFirstMove)
                {
                    roundStartMovementPhase = RoundStartPhaseFirstPause;
                    roundStartMovementPhaseTimer = Mathf.Max(0f, roundStartPauseDuration);
                }
                else if (roundStartMovementPhase == RoundStartPhaseFirstPause)
                {
                    roundStartMovementPhase = RoundStartPhaseSecondMove;
                    roundStartMovementPhaseTimer = Mathf.Max(0f, roundStartSecondMoveDuration);
                }
                else if (roundStartMovementPhase == RoundStartPhaseSecondMove)
                {
                    roundStartMovementPhase = RoundStartPhaseSecondPause;
                    roundStartMovementPhaseTimer = Mathf.Max(0f, roundStartSecondPauseDuration);
                }
                else if (roundStartMovementPhase == RoundStartPhaseSecondPause)
                {
                    BeginRoundStartArcPhase();
                }
                else
                {
                    roundStartMovementActive = false;
                    roundStartMovementPhase = RoundStartPhaseComplete;
                    roundStartMovementTarget = null;
                    break;
                }

                if (roundStartMovementPhase == RoundStartPhaseArcMove)
                {
                    if (roundStartMovementPhaseTimer > 0f)
                    {
                        break;
                    }

                    continue;
                }

                if (roundStartMovementPhaseTimer > 0f)
                {
                    break;
                }
            }
        }

        private void MoveRoundStartTarget(float deltaTime)
        {
            if (roundStartMovementTarget == null)
            {
                return;
            }

            var normalizedSpeed = Mathf.Max(0f, roundStartMoveSpeed);
            if (normalizedSpeed <= 0f)
            {
                return;
            }

            var referenceWidth = GetRoundStartMovementReferenceWidth();
            if (referenceWidth <= 0f)
            {
                return;
            }

            var distance = normalizedSpeed * referenceWidth * deltaTime;
            if (distance <= 0f)
            {
                return;
            }

            roundStartMovementTarget.Translate(Vector3.right * distance, Space.World);
        }

        private void MoveRoundStartArc(float deltaTime)
        {
            if (roundStartMovementTarget == null)
            {
                return;
            }

            var arcDuration = Mathf.Max(0.0001f, roundStartArcDuration);
            var arcSpeed = Mathf.Max(0f, roundStartArcMoveSpeed);
            if (arcSpeed <= 0f)
            {
                return;
            }

            var referenceWidth = GetRoundStartMovementReferenceWidth();
            var horizontalDistance = Mathf.Max(0f, roundStartArcHorizontalDistance) * referenceWidth;
            var verticalDistance = Mathf.Max(0f, roundStartArcVerticalDistance) * referenceWidth;
            var extraDropDistance = Mathf.Max(0f, roundStartArcExtraDropDistance) * referenceWidth;

            roundStartArcElapsed += deltaTime * arcSpeed;
            var normalizedTime = Mathf.Clamp01(roundStartArcElapsed / arcDuration);

            var offset = Vector3.right * (horizontalDistance * normalizedTime);
            var arcHeight = Mathf.Sin(normalizedTime * Mathf.PI) * verticalDistance;
            if (extraDropDistance > 0f && normalizedTime >= 0.5f)
            {
                float dropT = (normalizedTime - 0.5f) / 0.5f; // 0→1 over second half
                arcHeight -= extraDropDistance * Mathf.Clamp01(dropT);
            }

            offset += Vector3.up * arcHeight;
            roundStartMovementTarget.position = roundStartArcBasePosition + offset;

            if (!roundStartArcPeakTriggered && normalizedTime >= 0.5f)
            {
                roundStartArcPeakTriggered = true;
                OnArcPeak();
            }
        }

        private void BeginRoundStartArcPhase()
        {
            roundStartMovementPhase = RoundStartPhaseArcMove;
            roundStartMovementPhaseTimer = Mathf.Max(0f, roundStartArcDuration);
            roundStartArcElapsed = 0f;
            roundStartArcPeakTriggered = false;

            if (roundStartMovementTarget != null)
            {
                roundStartArcBasePosition = roundStartMovementTarget.position;
            }
        }

        private float GetRoundStartMovementReferenceWidth()
        {
            var area = movementArea;
            if (area == null && roundStart != null)
            {
                area = roundStart.GetComponent<RectTransform>();
            }

            if (area != null)
            {
                area.GetWorldCorners(roundStartMovementAreaCorners);
                var widthVector = roundStartMovementAreaCorners[3] - roundStartMovementAreaCorners[0];
                var width = widthVector.magnitude;
                if (width > 0.0001f)
                {
                    return width;
                }
            }

            var fallbackWidth = Mathf.Max(0f, roundStartMovementFallbackWidth);
            if (fallbackWidth > 0.0001f)
            {
                return fallbackWidth;
            }

            return 1f;
        }

        private float GetMovementReferenceHeight(DogMovementType type)
        {
            var area = movementArea;
            var referenceObject = type == DogMovementType.Hit ? got : lmfao;

            if (area == null && referenceObject != null)
            {
                area = referenceObject.GetComponent<RectTransform>();
            }

            if (area != null)
            {
                area.GetWorldCorners(movementAreaCorners);
                var heightVector = movementAreaCorners[1] - movementAreaCorners[0];
                var height = heightVector.magnitude;
                if (height > 0.0001f)
                {
                    return height;
                }
            }

            var fallbackHeight = type == DogMovementType.Hit
                ? Mathf.Max(0f, hitMovementFallbackHeight)
                : Mathf.Max(0f, missMovementFallbackHeight);

            if (fallbackHeight > 0.0001f)
            {
                return fallbackHeight;
            }

            return 1f;
        }

        private Transform GetMovementTarget(DogMovementType type)
        {
            if (type == DogMovementType.Hit)
            {
                return got != null ? got.transform : GetTransformForState(hitState);
            }

            if (type == DogMovementType.Miss)
            {
                return lmfao != null ? lmfao.transform : GetTransformForState(missState);
            }

            return null;
        }

        private float GetMoveUpDelay(DogMovementType type)
        {
            return type == DogMovementType.Miss ? Mathf.Max(0f, missMoveUpDelay) : Mathf.Max(0f, hitMoveUpDelay);
        }

        private float GetMoveUpSpeed(DogMovementType type)
        {
            return type == DogMovementType.Miss ? Mathf.Max(0f, missMoveUpSpeed) : Mathf.Max(0f, hitMoveUpSpeed);
        }

        private float GetMoveUpDuration(DogMovementType type)
        {
            return type == DogMovementType.Miss ? Mathf.Max(0f, missMoveUpDuration) : Mathf.Max(0f, hitMoveUpDuration);
        }

        private float GetSlowdownDuration(DogMovementType type)
        {
            return type == DogMovementType.Miss ? Mathf.Max(0f, missSlowdownDuration) : Mathf.Max(0f, hitSlowdownDuration);
        }

        private float GetPauseDuration(DogMovementType type)
        {
            return type == DogMovementType.Miss ? Mathf.Max(0f, missPauseDuration) : Mathf.Max(0f, hitPauseDuration);
        }

        private float GetDownAccelerationDuration(DogMovementType type)
        {
            return type == DogMovementType.Miss ? Mathf.Max(0f, missDownAccelerationDuration) : Mathf.Max(0f, hitDownAccelerationDuration);
        }

        private float GetMoveDownSpeed(DogMovementType type)
        {
            return type == DogMovementType.Miss ? Mathf.Max(0f, missMoveDownSpeed) : Mathf.Max(0f, hitMoveDownSpeed);
        }

        private float GetMoveDownDuration(DogMovementType type)
        {
            return type == DogMovementType.Miss ? Mathf.Max(0f, missMoveDownDuration) : Mathf.Max(0f, hitMoveDownDuration);
        }

        private float GetResetDelay(DogMovementType type)
        {
            return type == DogMovementType.Miss ? Mathf.Max(0f, missResetDelay) : Mathf.Max(0f, hitResetDelay);
        }

        private void OnArcPeak()
        {
            if (dogMainObject != null)
            {
                var canvas = dogMainObject.GetComponent<Canvas>();

                canvas.sortingOrder = 2;
            }  
        }

        private void CacheRoundStartPose()
        {
            if (roundStart == null)
            {
                roundStartBaseCached = false;
                return;
            }

            var transformRef = roundStart.transform;
            if (transformRef == null)
            {
                roundStartBaseCached = false;
                return;
            }

            roundStartBasePosition = transformRef.position;
            roundStartBaseRotation = transformRef.rotation;
            roundStartBaseCached = true;
        }

        public void RestLayerOrder()
        {
            if (dogMainObject != null)
            {
                var canvas = dogMainObject.GetComponent<Canvas>();

                canvas.sortingOrder = 3;
            }
        }
    }
}
