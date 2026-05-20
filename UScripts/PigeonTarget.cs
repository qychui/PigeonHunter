using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDKBase;

namespace PigeonHunt
{
    public enum PigeonColorType
    {
        Black = 0,
        Blue = 1,
        Red = 2
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PigeonTarget : UdonSharpBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private RectTransform playArea;
        [SerializeField] private Animator animator;

        [Header("Animator Parameters")]
        [SerializeField] private string flightState = "FlyState";

        [Header("Animation Variants")]
        [Range(0f, 1f)]
        [SerializeField] private float horizontalAnimationChance = 0.35f;

        [Header("Movement (Normalized)")]
        [SerializeField] private float normalizedSpeedPerSecond = 0.25f;

        [Header("Boundary Reflection")]
        [Min(0f)]
        [SerializeField] private float boundaryRandomDeflectionAngle = 0f;

        [Header("Lifetime")]
        [SerializeField] private float defaultLifetime = 12f;
        [SerializeField] private float exitDelay = 1.0f;
        [FormerlySerializedAs("hitFallDelay")]
        [SerializeField] private float hitHoldDuration = 0.35f;
        [SerializeField] private float shotDownDuration = 2.0f;
        [Min(0f)]
        [SerializeField] private float shotDownFallSpeedMultiplier = 2.0f;

        [Header("Escape Behaviour")]
        [SerializeField] private float escapeTriggerTime = 6f;
        [SerializeField] private float escapeDespawnDelay = 1.5f;

        [Header("Feedback")]
        [SerializeField] private AudioSource flightAudio;
        [SerializeField] private AudioSource fallAudio;
        [SerializeField] private AudioSource groundImpactAudio;

        [Header("Particle")]
        [SerializeField] public ParticleSystem normalHitParticle;
        [SerializeField] public ParticleSystem goodHitParticle;
        [SerializeField] public ParticleSystem excellentHitParticle;

        [Header("Score")]
        [SerializeField] private int normalHitScore = 500;
        [SerializeField] private int goodHitScore = 800;
        [SerializeField] private int excellentHitScore = 1000;

        [Header("Pigeon Color")]
        [SerializeField] private PigeonColorType pigeonColorType = PigeonColorType.Black;

        [Header("Collision")]
        [SerializeField] private BoxCollider hitCollider;
        [SerializeField] private Vector2 fallbackExtents = new Vector2(0.25f, 0.25f);

        [Header("Debug")]
        [SerializeField] private bool logDirectionChanges;

        private bool useLocalAreaSpace;

        private const int EdgeNone = 0;
        private const int EdgeX = 1;
        private const int EdgeY = 2;
        private const int EdgeCorner = 3;

        private const int FlightRight = 0;
        private const int FlightRightUp = 1;
        private const int FlightRightDown = 2;
        private const int FlightLeft = 3;
        private const int FlightLeftUp = 4;
        private const int FlightLeftDown = 5;
        private const int FlightHit = 6;
        private const int FlightShotDown = 7;
        private const int FlightUp = 8;

        private Vector3 direction;
        private Vector3 position;
        private Vector2 colliderExtents;
        private Vector2 areaSize = Vector2.one;
        private float speedMultiplier = 1f;
        private float elapsed;
        private float lifetime;
        private float runtimeEscapeTriggerTime;
        private float exitTimer;
        private bool escapeArmed;
        private bool escapeActive;
        private float escapeTimer;
        private bool naturalEscapeActive;
        private bool fallAnimationPending;
        private float fallAnimationTimer;
        private bool isHitHoldActive;
        private float hitHoldTimer;
        private bool isShotDown;
        private float shotDownTimer;

        private float minX;
        private float maxX;
        private float minY;
        private float maxY;
        private float planeZ;

        private bool isActive;
        private bool isDespawning;
        private bool hasBeenHit;
        private bool resolvedAsHit;
        private int currentFlightDirection = -1;
        private bool exitActive;
        private bool exitNotifiedToManager;
        private bool deterministicRandomActive;
        private int deterministicRoundSeed;
        private int deterministicSpawnIndex;
        private int deterministicPoolIndex;
        private int deterministicReflectionCount;
        private int deterministicShotRandomizeCount;

        public bool IsAvailable => !isActive && !isDespawning;
        public bool OccupiesSlot => isActive || isDespawning;
        public bool CanApplySyncedHit => isActive && !isDespawning && !hasBeenHit;
        public PigeonColorType ColorType => pigeonColorType;
        public bool BelongsTo(GameManager manager)
        {
            return gameManager == manager;
        }

        private void Awake()
        {
            CacheColliderExtents();
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;

            UpdateFlightAudioState();

            if (deltaTime <= 0f)
            {
                return;
            }

            if (exitActive)
            {
                TickExitMovement(deltaTime);
                return;
            }

            if (isActive)
            {
                TickMovement(deltaTime);
            }
            else if (isDespawning)
            {
                TickDespawn(deltaTime);
            }
        }

        private void OnValidate()
        {
            hitCollider = QychuiUtilities.EnsureBoxCollider(this, hitCollider);
            if (hitCollider == null)
            {
                return;
            }

            //if (fallbackExtents.x < 0f || fallbackExtents.y < 0f)
            //{
            //    fallbackExtents = new Vector2(Mathf.Max(0f, fallbackExtents.x), Mathf.Max(0f, fallbackExtents.y));
            //}

            //var size = hitCollider.size;
            //var scale = hitCollider.transform.lossyScale;
            //var scaleX = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            //var scaleY = Mathf.Max(0.0001f, Mathf.Abs(scale.y));

            //var minSizeX = Mathf.Max(0.0001f, fallbackExtents.x * 2f) / scaleX;
            //var minSizeY = Mathf.Max(0.0001f, fallbackExtents.y * 2f) / scaleY;

            //if (size.x < minSizeX)
            //{
            //    size.x = minSizeX;
            //}

            //if (size.y < minSizeY)
            //{
            //    size.y = minSizeY;
            //}

            //if (size.z <= 0f)
            //{
            //    size.z = 0.0001f;
            //}

            //hitCollider.size = size;
            //CacheColliderExtents();
        }

        public void SetManager(GameManager manager)
        {
            gameManager = manager;
        }

        public void SetPlayArea(RectTransform area)
        {
            playArea = area;

            RefreshMovementBounds();
        }

        public void BeginFlight(Vector3 startPosition, int directionIndex, float lifetimeSeconds, float difficultyMultiplier, float escapeTriggerReduction, float minimumEscapeTriggerTime)
        {
            ClearDeterministicRandomContext();
            RefreshMovementBounds();

            CacheColliderExtents();

            if (difficultyMultiplier < 1f)
            {
                difficultyMultiplier = 1f;
            }

            speedMultiplier = Mathf.Max(0.5f, difficultyMultiplier);

            lifetime = lifetimeSeconds > 0f ? lifetimeSeconds : defaultLifetime;
            runtimeEscapeTriggerTime = Mathf.Max(Mathf.Max(0f, minimumEscapeTriggerTime), escapeTriggerTime - Mathf.Max(0f, escapeTriggerReduction));

            elapsed = 0f;
            exitActive = false;
            exitNotifiedToManager = false;
            exitTimer = 0f;
            isActive = true;
            isDespawning = false;
            hasBeenHit = false;
            resolvedAsHit = false;
            escapeActive = false;
            escapeTimer = 0f;
            naturalEscapeActive = false;
            escapeArmed = runtimeEscapeTriggerTime <= 0f;
            fallAnimationPending = false;
            fallAnimationTimer = 0f;
            isHitHoldActive = false;
            hitHoldTimer = 0f;
            isShotDown = false;
            shotDownTimer = 0f;

            position = ClampInsideBounds(WorldToMovementSpace(startPosition));
            transform.position = MovementToWorldSpace(position);

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            ResetDirectionalAnimationState(false);

            var initialDirection = GetDirectionVector(directionIndex);

            ApplyMovementDirection(initialDirection);

            if (animator != null)
            {
                animator.speed = 1f;
                animator.Update(0f);
            }

            hitCollider = QychuiUtilities.EnsureBoxCollider(this, hitCollider);
            QychuiUtilities.SetColliderEnabled(hitCollider, true);

            UpdateFlightAudioState();
        }

        public void SetDeterministicRandomContext(int roundSeed, int spawnIndex, int poolIndex)
        {
            deterministicRandomActive = roundSeed != 0;
            deterministicRoundSeed = roundSeed;
            deterministicSpawnIndex = spawnIndex;
            deterministicPoolIndex = poolIndex;
            deterministicReflectionCount = 0;
            deterministicShotRandomizeCount = 0;
        }

        private void ClearDeterministicRandomContext()
        {
            deterministicRandomActive = false;
            deterministicRoundSeed = 0;
            deterministicSpawnIndex = 0;
            deterministicPoolIndex = 0;
            deterministicReflectionCount = 0;
            deterministicShotRandomizeCount = 0;
        }

        public void BeginExitFlight()
        {
            BeginExitFlightInternal(false);
        }

        public bool TryBeginExitFlight()
        {
            if (!isActive || isDespawning || hasBeenHit || exitActive || escapeActive)
            {
                return false;
            }

            BeginExitFlightInternal(false);
            return true;
        }

        public bool TryBeginNaturalEscape()
        {
            if (!isActive || isDespawning || hasBeenHit || exitActive || escapeActive || naturalEscapeActive)
            {
                return false;
            }

            BeginNaturalEscape();
            return true;
        }

        public bool ConsumeExitNotification()
        {
            if (!exitNotifiedToManager)
            {
                return false;
            }

            exitNotifiedToManager = false;
            return true;
        }

        private void BeginExitFlightInternal(bool requestFlyAwayUi)
        {
            if (!isActive || isDespawning || hasBeenHit || exitActive || escapeActive)
            {
                return;
            }

            RefreshMovementBounds();
            CacheColliderExtents();

            speedMultiplier = 1f;
            lifetime = 0f;
            elapsed = 0f;
            exitTimer = 0f;
            isActive = true;
            isDespawning = false;
            hasBeenHit = false;
            resolvedAsHit = false;
            escapeActive = false;
            escapeTimer = 0f;
            escapeArmed = false;
            fallAnimationPending = false;
            fallAnimationTimer = 0f;
            isHitHoldActive = false;
            hitHoldTimer = 0f;
            isShotDown = false;
            shotDownTimer = 0f;
            exitActive = true;
            exitNotifiedToManager = true;

            position = ClampInsideBounds(WorldToMovementSpace(transform.position));
            transform.position = MovementToWorldSpace(position);

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            SetExitAnimationState();

            if (animator != null)
            {
                animator.speed = 1f;
                animator.Update(0f);
            }

            hitCollider = QychuiUtilities.EnsureBoxCollider(this, hitCollider);
            QychuiUtilities.SetColliderEnabled(hitCollider, false);

            UpdateFlightAudioState();

            if (requestFlyAwayUi && gameManager != null)
            {
                gameManager.NotifyPigeonExitStarted(this);
            }
        }

        private Vector3 GetDirectionVector(int directionIndex)
        {
            switch (directionIndex)
            {
                case FlightUp:
                    return new Vector3(0f, 1f, 0f);
                case FlightRight:
                    return new Vector3(1f, 0f, 0f);
                case FlightRightUp:
                    return new Vector3(0.5f, 0.8660254f, 0f);
                case FlightRightDown:
                    return new Vector3(0.5f, -0.8660254f, 0f);
                case FlightLeft:
                    return new Vector3(-1f, 0f, 0f);
                case FlightLeftUp:
                    return new Vector3(-0.5f, 0.8660254f, 0f);
                case FlightLeftDown:
                    return new Vector3(-0.5f, -0.8660254f, 0f);
                default:
                    return new Vector3(1f, 0f, 0f);
            }
        }

        public void ApplyHit(Vector3 hitPoint, Vector3 hitNormal, VRCPlayerApi shooter)
        {
            ApplyHitInternal(hitPoint, hitNormal, true);
        }

        private void ApplyHitInternal(Vector3 hitPoint, Vector3 hitNormal, bool registerHit)
        {
            ApplyHitInternal(hitPoint, hitNormal, registerHit, true);
        }

        private void ApplyHitInternal(Vector3 hitPoint, Vector3 hitNormal, bool registerHit, bool snapToHitPoint)
        {
            if (!isActive || hasBeenHit)
            {
                return;
            }

            exitActive = false;
            hasBeenHit = true;
            StopFlightAudio();
            escapeArmed = false;
            escapeActive = false;
            escapeTimer = 0f;
            naturalEscapeActive = false;
            fallAnimationPending = false;
            fallAnimationTimer = 0f;

            if (snapToHitPoint)
            {
                var hitPosition = WorldToMovementSpace(hitPoint);
                position = ClampInsideBounds(hitPosition);
                transform.position = MovementToWorldSpace(position);
            }

            direction = Vector3.zero;

            if (animator != null)
            {
                animator.speed = 1f;
            }

            hitHoldTimer = Mathf.Max(0f, hitHoldDuration);
            isHitHoldActive = hitHoldTimer > 0f;
            isShotDown = false;
            shotDownTimer = 0f;

            if (isHitHoldActive)
            {
                SetHitAnimationState();
            }
            else
            {
                EnterShotDownState();
            }

            hitCollider = QychuiUtilities.EnsureBoxCollider(this, hitCollider);

            QychuiUtilities.SetColliderEnabled(hitCollider, false);

            PlayHitParticle(snapToHitPoint ? hitPoint : transform.position, hitNormal);

            if (registerHit && gameManager != null)
            {
                gameManager.RegisterPigeonHit(this);
            }
        }

        public void OnShot(Vector3 hitPoint, Vector3 hitNormal)
        {
            ApplyHit(hitPoint, hitNormal, Networking.LocalPlayer);
        }

        public void ApplySyncedHit()
        {
            ApplyHitInternal(transform.position, -transform.forward, false, false);
        }

        private void PlayHitParticle(Vector3 hitPoint, Vector3 hitNormal)
        {
            var particle = GetHitParticleForCurrentRound();
            if (particle == null)
            {
                return;
            }

            var particlePosition = WorldToMovementSpace(hitPoint);
            particle.transform.position = MovementToWorldSpace(particlePosition);
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
            if (gameManager != null)
            {
                if (gameManager.actionController != null)
                {
                    return Mathf.Max(1, gameManager.actionController.CurrentRoundNumber);
                }

                if (gameManager.uiController != null)
                {
                    return Mathf.Max(1, gameManager.uiController.roundLevel);
                }
            }

            return 1;
        }

        public bool TryRandomizeFlightDirection()
        {
            if (!isActive || isDespawning || hasBeenHit || exitActive || escapeActive)
            {
                return false;
            }

            int newDirectionIndex;
            if (deterministicRandomActive)
            {
                newDirectionIndex = SampleDeterministicFlightDirectionIndex(deterministicShotRandomizeCount);
                deterministicShotRandomizeCount++;
            }
            else
            {
                newDirectionIndex = SampleRandomFlightDirectionIndex();
            }

            ApplyMovementDirection(GetDirectionVector(newDirectionIndex));

            return true;
        }

        public void DespawnImmediate()
        {
            ForceRecycle();
        }

        public void ForceRecycle()
        {
            exitActive = false;
            exitNotifiedToManager = false;
            isActive = false;
            isDespawning = false;
            hasBeenHit = false;
            resolvedAsHit = false;
            exitTimer = 0f;
            elapsed = 0f;
            lifetime = 0f;
            escapeArmed = false;
            escapeActive = false;
            escapeTimer = 0f;
            naturalEscapeActive = false;
            fallAnimationPending = false;
            fallAnimationTimer = 0f;
            isHitHoldActive = false;
            hitHoldTimer = 0f;
            isShotDown = false;
            shotDownTimer = 0f;

            StopFlightAudio();

            hitCollider = QychuiUtilities.EnsureBoxCollider(this, hitCollider);
            QychuiUtilities.SetColliderEnabled(hitCollider, false);

            if (animator != null)
            {
                animator.speed = 1f;
                ResetDirectionalAnimationState();
            }

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void UpdateFlightDirectionAnimation()
        {
            if (animator == null || string.IsNullOrEmpty(flightState))
            {
                return;
            }

            var previousDirection = currentFlightDirection;

            var desiredDirection = CalculateFlightDirectionIndex(direction);

            if (desiredDirection == currentFlightDirection)
            {
                return;
            }

            currentFlightDirection = desiredDirection;

            //Debug.Log(currentFlightDirection + flightState);

            animator.SetInteger(flightState, currentFlightDirection);

            if (logDirectionChanges)
            {
                var angle = CalculateDirectionAngle(direction);

                var angleText = float.IsNaN(angle) ? "n/a" : $"{angle:0.##} deg";

                //Debug.Log(
                //    $"[PigeonTarget] {name} direction changed {GetFlightDirectionLabel(previousDirection)} -> {GetFlightDirectionLabel(desiredDirection)} (vector {direction}, angle {angleText})",
                //    gameObject);
            }
        }

        private void ResetDirectionalAnimationState(bool setAnimatorDefault = true)
        {
            currentFlightDirection = -1;

            if (!setAnimatorDefault || animator == null || string.IsNullOrEmpty(flightState))
            {
                return;
            }

            animator.SetInteger(flightState, FlightRight);
        }

        private void SetExitAnimationState()
        {
            currentFlightDirection = FlightUp;

            if (animator == null || string.IsNullOrEmpty(flightState))
            {
                return;
            }

            animator.SetInteger(flightState, FlightUp);
        }

        private void SetHitAnimationState()
        {
            currentFlightDirection = FlightHit;

            if (animator == null || string.IsNullOrEmpty(flightState))
            {
                return;
            }

            animator.SetInteger(flightState, FlightHit);
        }

        private void SetShotDownAnimationState()
        {
            currentFlightDirection = FlightShotDown;

            if (animator == null || string.IsNullOrEmpty(flightState))
            {
                return;
            }

            animator.SetInteger(flightState, FlightShotDown);
        }

        private void ApplyMovementDirection(Vector3 newDirection)
        {
            var planar = new Vector3(newDirection.x, newDirection.y, 0f);

            if (planar.sqrMagnitude > 0f)
            {
                planar.Normalize();
            }
            else
            {
                planar = Vector3.zero;
            }

            direction = planar;
            UpdateFlightDirectionAnimation();
        }

        private void TriggerFallAnimation()
        {
            fallAnimationPending = false;
            fallAnimationTimer = 0f;

            SetHitAnimationState();
        }

        private void EnterShotDownState()
        {
            if (isShotDown)
            {
                return;
            }

            isHitHoldActive = false;
            hitHoldTimer = 0f;
            isShotDown = true;
            shotDownTimer = Mathf.Max(0f, shotDownDuration);

            SetShotDownAnimationState();

            var soundManager = GetSoundManager();
            if (soundManager != null)
            {
                soundManager.PlayFall(fallAudio);
            }
            else
            {
                QychuiUtilities.SafePlay(fallAudio);
            }
        }

        private int CalculateFlightDirectionIndex(Vector3 targetDirection)
        {
            var angle = CalculateDirectionAngle(targetDirection);

            if (float.IsNaN(angle))
            {
                return currentFlightDirection >= 0 ? currentFlightDirection : FlightRight;
            }

            var baseDirection = MapAngleToFlightDirection(angle);
            return MaybeApplyHorizontalAnimation(baseDirection);
        }

        private float CalculateDirectionAngle(Vector3 targetDirection)
        {
            var planar = new Vector2(targetDirection.x, targetDirection.y);

            if (planar.sqrMagnitude <= 0.0001f)
            {
                return float.NaN;
            }

            planar.Normalize();

            var angle = Mathf.Atan2(planar.y, planar.x) * Mathf.Rad2Deg;

            if (angle < 0f)
            {
                angle += 360f;
            }

            return angle;
        }

        private int MapAngleToFlightDirection(float angleDegrees)
        {
            var normalizedAngle = Mathf.Repeat(angleDegrees, 360f);

            if (normalizedAngle >= 330f || normalizedAngle < 30f)
            {
                return FlightRight;
            }

            if (normalizedAngle < 90f)
            {
                return FlightRightUp;
            }

            if (normalizedAngle < 150f)
            {
                return FlightLeftUp;
            }

            if (normalizedAngle < 210f)
            {
                return FlightLeft;
            }

            if (normalizedAngle < 270f)
            {
                return FlightLeftDown;
            }

            return FlightRightDown;
        }

        private int MaybeApplyHorizontalAnimation(int baseDirection)
        {
            var chance = Mathf.Clamp01(horizontalAnimationChance);
            if (chance <= 0f)
            {
                return baseDirection;
            }

            if ((baseDirection == FlightLeftUp || baseDirection == FlightLeftDown) && Random.value <= chance)
            {
                return FlightLeft;
            }

            if ((baseDirection == FlightRightUp || baseDirection == FlightRightDown) && Random.value <= chance)
            {
                return FlightRight;
            }

            return baseDirection;
        }

        private void TickMovement(float deltaTime)
        {
            elapsed += deltaTime;

            if (hasBeenHit)
            {
                if (isHitHoldActive)
                {
                    hitHoldTimer -= deltaTime;

                    if (hitHoldTimer <= 0f)
                    {
                        EnterShotDownState();
                    }

                    return;
                }

                if (!isShotDown)
                {
                    EnterShotDownState();
                }

                shotDownTimer -= deltaTime;

                var fallSpeed = GetWorldVerticalSpeed(normalizedSpeedPerSecond * speedMultiplier * shotDownFallSpeedMultiplier);
                position.y -= fallSpeed * deltaTime;
                transform.position = MovementToWorldSpace(position);

                var hitBottom = HasHitBottom();

                if (shotDownTimer <= 0f || hitBottom)
                {
                    if (hitBottom)
                    {
                        var soundManager = GetSoundManager();
                        if (soundManager != null)
                        {
                            soundManager.PlayGroundImpact(groundImpactAudio);
                        }
                        else
                        {
                            QychuiUtilities.SafePlay(groundImpactAudio);
                        }
                    }

                    BeginExpirySequence(position, true);

                    return;
                }

                return;
            }

            if (!hasBeenHit && !exitActive && !escapeActive && !naturalEscapeActive && runtimeEscapeTriggerTime > 0f && elapsed >= runtimeEscapeTriggerTime)
            {
                if (gameManager != null && gameManager.gameMode == 2)
                {
                    BeginNaturalEscape();
                }
                else
                {
                    BeginExitFlightInternal(true);
                }

                return;
            }

            if (!hasBeenHit && !naturalEscapeActive && lifetime > 0f && elapsed >= lifetime)
            {
                if (gameManager != null && gameManager.gameMode == 2)
                {
                    BeginNaturalEscape();
                }
                else
                {
                    BeginExitFlightInternal(true);
                }

                return;
            }

            var areaWidth = areaSize.x;
            var areaHeight = areaSize.y;
            var invWidth = areaWidth > 0f ? 1f / areaWidth : 0f;
            var invHeight = areaHeight > 0f ? 1f / areaHeight : 0f;
            var directionScale = Mathf.Sqrt(
                (direction.x * invWidth) * (direction.x * invWidth) +
                (direction.y * invHeight) * (direction.y * invHeight)
            );

            if (directionScale < 0.0001f)
            {
                directionScale = 0.0001f;
            }

            var normalizedStep = Mathf.Max(0f, normalizedSpeedPerSecond) * speedMultiplier * deltaTime;
            var nextPosition = position + direction * (normalizedStep / directionScale);

            if (escapeActive)
            {
                position = nextPosition;
                transform.position = MovementToWorldSpace(position);

                if (escapeDespawnDelay <= 0f)
                {
                    BeginExpirySequence(position, false);

                    return;
                }

                escapeTimer -= deltaTime;

                if (escapeTimer <= 0f)
                {
                    BeginExpirySequence(position, false);
                }

                return;
            }

            if (naturalEscapeActive)
            {
                position = nextPosition;
                position.z = planeZ;
                transform.position = MovementToWorldSpace(position);

                if (IsOutsideRecycleBounds(position))
                {
                    BeginExpirySequence(position, false);
                }

                return;
            }

            var clampedPosition = nextPosition;
            var edgeHit = SnapToBounds(ref clampedPosition);

            if (edgeHit != EdgeNone)
            {
                if (escapeArmed && !hasBeenHit)
                {
                    StartEscape(nextPosition);
                    return;
                }

                ReflectDirection(edgeHit);
                position = clampedPosition;
                transform.position = MovementToWorldSpace(position);
                return;
            }

            position = clampedPosition;
            transform.position = MovementToWorldSpace(position);
        }

        private void TickExitMovement(float deltaTime)
        {
            var areaHeight = areaSize.y;
            var invHeight = areaHeight > 0f ? 1f / areaHeight : 0f;
            var directionScale = Mathf.Abs(invHeight);

            if (directionScale < 0.0001f)
            {
                directionScale = 0.0001f;
            }

            var normalizedStep = Mathf.Max(0f, normalizedSpeedPerSecond) * deltaTime;
            position.y += normalizedStep / directionScale;
            position.z = planeZ;
            transform.position = MovementToWorldSpace(position);

            if (position.y > maxY + colliderExtents.y)
            {
                exitActive = false;
                BeginExpirySequence(position, false);
            }
        }

        private float GetWorldVerticalSpeed(float normalizedSpeed)
        {
            return Mathf.Max(0f, normalizedSpeed) * Mathf.Max(0.0001f, areaSize.y);
        }

        private void TickDespawn(float deltaTime)
        {
            if (fallAnimationPending)
            {
                fallAnimationTimer -= deltaTime;

                if (fallAnimationTimer <= 0f)
                {
                    TriggerFallAnimation();
                }
            }

            exitTimer -= deltaTime;

            if (exitTimer <= 0f)
            {
                CompleteDespawn();
            }
        }

        private void BeginExpirySequence(Vector3 stopPosition, bool wasHit)
        {
            var shotDownActive = isShotDown;

            exitActive = false;
            isActive = false;
            isDespawning = true;
            resolvedAsHit = wasHit;
            escapeArmed = false;
            escapeActive = false;
            escapeTimer = 0f;
            naturalEscapeActive = false;
            fallAnimationPending = false;
            fallAnimationTimer = 0f;
            isHitHoldActive = false;
            hitHoldTimer = 0f;
            isShotDown = false;
            shotDownTimer = 0f;
            naturalEscapeActive = false;

            position = stopPosition;
            position.z = planeZ;
            transform.position = MovementToWorldSpace(position);

            StopFlightAudio();

            hitCollider = QychuiUtilities.EnsureBoxCollider(this, hitCollider);
            QychuiUtilities.SetColliderEnabled(hitCollider, false);

            if (animator != null)
            {
                ResetDirectionalAnimationState(false);

                if (wasHit)
                {
                    if (shotDownActive)
                    {
                        SetShotDownAnimationState();
                    }
                    else
                    {
                        SetHitAnimationState();
                    }
                }
            }

            if (wasHit)
            {
                CompleteDespawn();

                return;
            }

            exitTimer = exitDelay;
        }

        private void CompleteDespawn()
        {
            var wasHit = resolvedAsHit;

            StopFlightAudio();

            exitActive = false;
            isDespawning = false;
            resolvedAsHit = false;
            exitTimer = 0f;
            elapsed = 0f;
            lifetime = 0f;
            speedMultiplier = 1f;
            fallAnimationPending = false;
            fallAnimationTimer = 0f;
            isHitHoldActive = false;
            hitHoldTimer = 0f;
            isShotDown = false;
            shotDownTimer = 0f;
            naturalEscapeActive = false;

            if (animator != null)
            {
                animator.speed = 1f;
            }

            if (gameManager != null)
            {
                gameManager.NotifyPigeonAvailable(this, wasHit);
            }

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void StartEscape(Vector3 initialPosition)
        {
            escapeActive = true;
            escapeArmed = false;
            escapeTimer = Mathf.Max(0f, escapeDespawnDelay);
            position = initialPosition;
            transform.position = MovementToWorldSpace(position);

            if (escapeDespawnDelay <= 0f)
            {
                BeginExpirySequence(position, false);
            }
        }

        private void BeginNaturalEscape()
        {
            escapeActive = false;
            escapeArmed = false;
            escapeTimer = 0f;
            naturalEscapeActive = true;
            lifetime = 0f;
        }

        private bool IsOutsideRecycleBounds(Vector3 targetPosition)
        {
            return targetPosition.x < minX - colliderExtents.x ||
                   targetPosition.x > maxX + colliderExtents.x ||
                   targetPosition.y < minY - colliderExtents.y ||
                   targetPosition.y > maxY + colliderExtents.y;
        }

        private void ReflectDirection(int edge)
        {
            var reflected = direction;

            if (edge == EdgeX || edge == EdgeCorner)
            {
                reflected.x = -reflected.x;
            }

            if (edge == EdgeY || edge == EdgeCorner)
            {
                reflected.y = -reflected.y;
            }

            reflected = ApplyRandomReflectionDeflection(reflected, edge);
            ApplyMovementDirection(reflected);
        }

        private Vector3 ApplyRandomReflectionDeflection(Vector3 reflected, int edge)
        {
            var deflectionChance = gameManager != null ? gameManager.CurrentPigeonBoundaryRandomDeflectionChance : 0f;
            var deflectionAngle = Mathf.Max(0f, boundaryRandomDeflectionAngle);
            var reflectionIndex = deterministicReflectionCount++;

            if (deflectionChance <= 0f ||
                deflectionAngle <= 0f)
            {
                return reflected;
            }

            var chanceRoll = deterministicRandomActive ? GetDeterministic01(101, reflectionIndex) : Random.value;
            if (chanceRoll > deflectionChance)
            {
                return reflected;
            }

            var angleRoll = deterministicRandomActive ? GetDeterministic01(102, reflectionIndex) : Random.value;
            var angleOffset = Mathf.Lerp(-deflectionAngle, deflectionAngle, angleRoll);
            var rotated = Quaternion.Euler(0f, 0f, angleOffset) * reflected;
            rotated.z = 0f;

            if (rotated.sqrMagnitude <= 0.0001f)
            {
                return reflected;
            }

            rotated.Normalize();

            // Keep the post-bounce direction moving back into the play area.
            if (!IsValidReflectedDirection(edge, reflected, rotated))
            {
                return reflected;
            }

            return rotated;
        }

        private bool IsValidReflectedDirection(int edge, Vector3 reflected, Vector3 candidate)
        {
            const float epsilon = 0.0001f;

            if ((edge == EdgeX || edge == EdgeCorner) &&
                Mathf.Abs(reflected.x) > epsilon &&
                Mathf.Sign(candidate.x) != Mathf.Sign(reflected.x))
            {
                return false;
            }

            if ((edge == EdgeY || edge == EdgeCorner) &&
                Mathf.Abs(reflected.y) > epsilon &&
                Mathf.Sign(candidate.y) != Mathf.Sign(reflected.y))
            {
                return false;
            }

            return true;
        }

        private int SnapToBounds(ref Vector3 targetPosition)
        {
            var paddedMinX = minX + colliderExtents.x;
            var paddedMaxX = maxX - colliderExtents.x;
            var paddedMinY = minY + colliderExtents.y;
            var paddedMaxY = maxY - colliderExtents.y;

            if (paddedMaxX < paddedMinX)
            {
                var midpoint = (minX + maxX) * 0.5f;
                paddedMinX = midpoint;
                paddedMaxX = midpoint;
            }

            if (paddedMaxY < paddedMinY)
            {
                var midpoint = (minY + maxY) * 0.5f;
                paddedMinY = midpoint;
                paddedMaxY = midpoint;
            }

            var hitX = false;
            var hitY = false;

            if (targetPosition.x < paddedMinX)
            {
                targetPosition.x = paddedMinX;
                hitX = true;
            }
            else if (targetPosition.x > paddedMaxX)
            {
                targetPosition.x = paddedMaxX;
                hitX = true;
            }

            if (targetPosition.y < paddedMinY)
            {
                targetPosition.y = paddedMinY;
                hitY = true;
            }
            else if (targetPosition.y > paddedMaxY)
            {
                targetPosition.y = paddedMaxY;
                hitY = true;
            }

            targetPosition.z = planeZ;

            if (hitX && hitY)
            {
                return EdgeCorner;
            }

            if (hitX)
            {
                return EdgeX;
            }

            if (hitY)
            {
                return EdgeY;
            }

            return EdgeNone;
        }

        private Vector3 ClampInsideBounds(Vector3 targetPosition)
        {
            var paddedMinX = minX + colliderExtents.x;
            var paddedMaxX = maxX - colliderExtents.x;
            var paddedMinY = minY + colliderExtents.y;
            var paddedMaxY = maxY - colliderExtents.y;

            if (paddedMaxX < paddedMinX)
            {
                var midpoint = (minX + maxX) * 0.5f;

                paddedMinX = midpoint;
                paddedMaxX = midpoint;
            }

            if (paddedMaxY < paddedMinY)
            {
                var midpoint = (minY + maxY) * 0.5f;

                paddedMinY = midpoint;
                paddedMaxY = midpoint;
            }

            targetPosition.x = Mathf.Clamp(targetPosition.x, paddedMinX, paddedMaxX);
            targetPosition.y = Mathf.Clamp(targetPosition.y, paddedMinY, paddedMaxY);
            targetPosition.z = planeZ;

            return targetPosition;
        }

        private void RefreshMovementBounds()
        {
            if (playArea == null)
            {
                var current = transform.position;
                useLocalAreaSpace = false;

                minX = current.x - 5f;
                maxX = current.x + 5f;
                minY = current.y - 3f;
                maxY = current.y + 3f;

                planeZ = current.z;

                areaSize.x = Mathf.Max(0.0001f, maxX - minX);
                areaSize.y = Mathf.Max(0.0001f, maxY - minY);

                return;
            }

            useLocalAreaSpace = true;

            var rect = playArea.rect;
            minX = rect.xMin;
            maxX = rect.xMax;
            minY = rect.yMin;
            maxY = rect.yMax;
            planeZ = 0f;

            areaSize.x = Mathf.Max(0.0001f, maxX - minX);
            areaSize.y = Mathf.Max(0.0001f, maxY - minY);
        }

        private void CacheColliderExtents()
        {
            var computed = Vector2.zero;

            hitCollider = QychuiUtilities.EnsureBoxCollider(this, hitCollider);

            if (hitCollider != null)
            {
                computed = GetColliderMovementExtents(hitCollider);
            }

            if (computed.x <= 0f)
            {
                computed.x = Mathf.Max(0.0001f, fallbackExtents.x);
            }

            if (computed.y <= 0f)
            {
                computed.y = Mathf.Max(0.0001f, fallbackExtents.y);
            }

            colliderExtents = computed;
        }

        private Vector2 GetColliderMovementExtents(BoxCollider collider)
        {
            if (collider == null)
            {
                return Vector2.zero;
            }

            var halfSize = collider.size * 0.5f;
            var center = collider.center;
            var min = WorldToMovementSpace(collider.transform.TransformPoint(center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z)));
            var max = min;

            AccumulateColliderCorner(collider, center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z), ref min, ref max);
            AccumulateColliderCorner(collider, center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z), ref min, ref max);
            AccumulateColliderCorner(collider, center + new Vector3(-halfSize.x, halfSize.y, halfSize.z), ref min, ref max);
            AccumulateColliderCorner(collider, center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z), ref min, ref max);
            AccumulateColliderCorner(collider, center + new Vector3(halfSize.x, -halfSize.y, halfSize.z), ref min, ref max);
            AccumulateColliderCorner(collider, center + new Vector3(halfSize.x, halfSize.y, -halfSize.z), ref min, ref max);
            AccumulateColliderCorner(collider, center + new Vector3(halfSize.x, halfSize.y, halfSize.z), ref min, ref max);

            return new Vector2(
                Mathf.Max(0f, (max.x - min.x) * 0.5f),
                Mathf.Max(0f, (max.y - min.y) * 0.5f));
        }

        private void AccumulateColliderCorner(BoxCollider collider, Vector3 localCorner, ref Vector3 min, ref Vector3 max)
        {
            var point = WorldToMovementSpace(collider.transform.TransformPoint(localCorner));
            if (point.x < min.x)
            {
                min.x = point.x;
            }

            if (point.x > max.x)
            {
                max.x = point.x;
            }

            if (point.y < min.y)
            {
                min.y = point.y;
            }

            if (point.y > max.y)
            {
                max.y = point.y;
            }
        }

        private Vector3 WorldToMovementSpace(Vector3 worldPosition)
        {
            if (!useLocalAreaSpace || playArea == null)
            {
                return worldPosition;
            }

            var local = playArea.InverseTransformPoint(worldPosition);
            return new Vector3(local.x, local.y, 0f);
        }

        private Vector3 MovementToWorldSpace(Vector3 localPosition)
        {
            if (!useLocalAreaSpace || playArea == null)
            {
                localPosition.z = planeZ;
                return localPosition;
            }

            return playArea.TransformPoint(new Vector3(localPosition.x, localPosition.y, 0f));
        }

        private void UpdateFlightAudioState()
        {
            if (flightAudio == null)
            {
                return;
            }

            var shouldPlay = isActive && !hasBeenHit;

            if (shouldPlay)
            {
                var soundManager = GetSoundManager();
                if (soundManager != null)
                {
                    soundManager.PlayFlight(flightAudio);
                }
                else if (!flightAudio.isPlaying)
                {
                    flightAudio.Play();
                }
            }
            else
            {
                StopFlightAudio();
            }
        }

        private void StopFlightAudio()
        {
            var soundManager = GetSoundManager();
            if (soundManager != null)
            {
                soundManager.StopFlight(flightAudio);
            }
            else
            {
                QychuiUtilities.SafeStop(flightAudio);
            }
        }

        private SoundManager GetSoundManager()
        {
            return gameManager != null ? gameManager.soundManager : null;
        }

        private bool HasHitBottom()
        {
            var paddedMinY = minY + colliderExtents.y;

            return position.y <= paddedMinY;
        }

        private int SampleRandomFlightDirectionIndex()
        {
            var currentDirection = GetDiagonalDirectionIndex(direction);
            var candidate = SampleRandomDiagonalDirection();

            if (currentDirection == candidate)
            {
                do
                {
                    candidate = SampleRandomDiagonalDirection();
                }
                while (candidate == currentDirection);
            }

            return candidate;
        }

        private int SampleDeterministicFlightDirectionIndex(int randomizeIndex)
        {
            var currentDirection = GetDiagonalDirectionIndex(direction);
            var candidate = SampleDeterministicDiagonalDirection(randomizeIndex, 201);

            if (currentDirection == candidate)
            {
                candidate = SampleDeterministicDiagonalDirection(randomizeIndex, 202);
                if (currentDirection == candidate)
                {
                    candidate = GetNextDiagonalDirection(currentDirection);
                }
            }

            return candidate;
        }

        private int SampleRandomDiagonalDirection()
        {
            var roll = Random.Range(0, 4);
            if (roll == 0)
            {
                return FlightRightUp;
            }

            if (roll == 1)
            {
                return FlightRightDown;
            }

            if (roll == 2)
            {
                return FlightLeftUp;
            }

            return FlightLeftDown;
        }

        private int SampleDeterministicDiagonalDirection(int randomizeIndex, int salt)
        {
            var roll = GetDeterministicValue(salt, randomizeIndex) & 3;
            if (roll == 0)
            {
                return FlightRightUp;
            }

            if (roll == 1)
            {
                return FlightRightDown;
            }

            if (roll == 2)
            {
                return FlightLeftUp;
            }

            return FlightLeftDown;
        }

        private int GetNextDiagonalDirection(int currentDirection)
        {
            if (currentDirection == FlightRightUp)
            {
                return FlightRightDown;
            }

            if (currentDirection == FlightRightDown)
            {
                return FlightLeftUp;
            }

            if (currentDirection == FlightLeftUp)
            {
                return FlightLeftDown;
            }

            return FlightRightUp;
        }

        private float GetDeterministic01(int salt, int eventIndex)
        {
            var value = GetDeterministicValue(salt, eventIndex) & 0x7fffffff;
            return value / 2147483647f;
        }

        private int GetDeterministicValue(int salt, int eventIndex)
        {
            var value = Mathf.Abs(deterministicRoundSeed);
            value = MixDeterministicValue(value, deterministicSpawnIndex);
            value = MixDeterministicValue(value, deterministicPoolIndex);
            value = MixDeterministicValue(value, eventIndex);
            value = MixDeterministicValue(value, salt);
            return value;
        }

        private int MixDeterministicValue(int value, int salt)
        {
            var mixed = Mathf.Abs(value + 31 * (salt + 1));
            mixed = (mixed * 1103515245 + 12345) & 0x7fffffff;
            return mixed;
        }

        private int GetDiagonalDirectionIndex(Vector3 targetDirection)
        {
            var angle = CalculateDirectionAngle(targetDirection);
            if (float.IsNaN(angle))
            {
                return -1;
            }

            var normalizedAngle = Mathf.Repeat(angle, 360f);
            if (normalizedAngle < 90f)
            {
                return FlightRightUp;
            }

            if (normalizedAngle < 180f)
            {
                return FlightLeftUp;
            }

            if (normalizedAngle < 270f)
            {
                return FlightLeftDown;
            }

            return FlightRightDown;
        }
    }
}
