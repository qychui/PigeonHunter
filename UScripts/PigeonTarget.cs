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

    [AddComponentMenu("PigeonHunt/Pigeon Target")]
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

        [Header("Lifetime")]
        [SerializeField] private float defaultLifetime = 12f;
        [SerializeField] private float exitDelay = 1.0f;
        [FormerlySerializedAs("hitFallDelay")]
        [SerializeField] private float hitHoldDuration = 0.35f;
        [SerializeField] private float shotDownDuration = 2.0f;

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

        private Vector3[] canvasCorners = new Vector3[4];

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
        private float exitTimer;
        private bool escapeArmed;
        private bool escapeActive;
        private float escapeTimer;
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

        public bool IsAvailable => !isActive && !isDespawning;
        public bool OccupiesSlot => isActive || isDespawning;
        public PigeonColorType ColorType => pigeonColorType;

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

        public void BeginFlight(Vector3 startPosition, int directionIndex, float lifetimeSeconds, float difficultyMultiplier)
        {
            RefreshMovementBounds();

            CacheColliderExtents();

            if (difficultyMultiplier < 1f)
            {
                difficultyMultiplier = 1f;
            }

            speedMultiplier = Mathf.Max(0.5f, difficultyMultiplier);

            lifetime = lifetimeSeconds > 0f ? lifetimeSeconds : defaultLifetime;

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
            escapeArmed = escapeTriggerTime <= 0f;
            fallAnimationPending = false;
            fallAnimationTimer = 0f;
            isHitHoldActive = false;
            hitHoldTimer = 0f;
            isShotDown = false;
            shotDownTimer = 0f;

            position = ClampInsideBounds(startPosition);
            transform.position = position;

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

            position = ClampInsideBounds(transform.position);
            transform.position = position;

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
            fallAnimationPending = false;
            fallAnimationTimer = 0f;

            var hitPosition = new Vector3(hitPoint.x, hitPoint.y, planeZ);
            position = ClampInsideBounds(hitPosition);
            transform.position = position;

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

            PlayHitParticle(hitPoint, hitNormal);

            if (gameManager != null)
            {
                gameManager.RegisterPigeonHit(this);
            }
        }

        public void OnShot(Vector3 hitPoint, Vector3 hitNormal)
        {
            ApplyHit(hitPoint, hitNormal, Networking.LocalPlayer);
        }

        private void PlayHitParticle(Vector3 hitPoint, Vector3 hitNormal)
        {
            var particle = GetHitParticleForCurrentRound();
            if (particle == null)
            {
                return;
            }

            particle.transform.position = new Vector3(hitPoint.x, hitPoint.y, planeZ);
            var normal = QychuiUtilities.GetSafeNormal(hitNormal, -transform.forward);
            particle.transform.rotation = Quaternion.LookRotation(normal);
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

            var newDirectionIndex = SampleRandomFlightDirectionIndex();
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

                var fallSpeed = normalizedSpeedPerSecond * speedMultiplier * 2f; // Fall speed is twice the normal flight speed
                position.y -= fallSpeed * deltaTime;
                transform.position = position;

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

            if (!hasBeenHit && !exitActive && !escapeActive && escapeTriggerTime > 0f && elapsed >= escapeTriggerTime)
            {
                BeginExitFlightInternal(true);
                return;
            }

            if (!hasBeenHit && lifetime > 0f && elapsed >= lifetime)
            {
                BeginExitFlightInternal(true);
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
                transform.position = position;

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
                transform.position = position;
                return;
            }

            position = clampedPosition;
            transform.position = position;
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
            transform.position = position;

            if (position.y > maxY + colliderExtents.y)
            {
                exitActive = false;
                BeginExpirySequence(position, false);
            }
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
            fallAnimationPending = false;
            fallAnimationTimer = 0f;
            isHitHoldActive = false;
            hitHoldTimer = 0f;
            isShotDown = false;
            shotDownTimer = 0f;

            position = stopPosition;
            position.z = planeZ;
            transform.position = position;

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
            transform.position = position;

            if (escapeDespawnDelay <= 0f)
            {
                BeginExpirySequence(position, false);
            }
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

            ApplyMovementDirection(reflected);
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

                minX = current.x - 5f;
                maxX = current.x + 5f;
                minY = current.y - 3f;
                maxY = current.y + 3f;

                planeZ = current.z;

                areaSize.x = Mathf.Max(0.0001f, maxX - minX);
                areaSize.y = Mathf.Max(0.0001f, maxY - minY);

                return;
            }

            if (!QychuiUtilities.TryGetRectWorldBounds(playArea, canvasCorners, out float boundsMinX, out float boundsMaxX, out float boundsMinY, out float boundsMaxY, out float boundsPlaneZ))
            {
                return;
            }

            minX = boundsMinX;
            maxX = boundsMaxX;
            minY = boundsMinY;
            maxY = boundsMaxY;

            planeZ = boundsPlaneZ;

            areaSize.x = Mathf.Max(0.0001f, maxX - minX);
            areaSize.y = Mathf.Max(0.0001f, maxY - minY);
        }

        private void CacheColliderExtents()
        {
            var computed = Vector2.zero;

            hitCollider = QychuiUtilities.EnsureBoxCollider(this, hitCollider);

            if (hitCollider != null)
            {
                var localSize = hitCollider.size;
                var lossyScale = hitCollider.transform.lossyScale;
                computed.x = Mathf.Abs(localSize.x * lossyScale.x) * 0.5f;
                computed.y = Mathf.Abs(localSize.y * lossyScale.y) * 0.5f;

                if (hitCollider.gameObject.activeInHierarchy)
                {
                    var bounds = hitCollider.bounds;

                    if (bounds.extents.x > 0f)
                    {
                        computed.x = Mathf.Max(computed.x, bounds.extents.x);
                    }

                    if (bounds.extents.y > 0f)
                    {
                        computed.y = Mathf.Max(computed.y, bounds.extents.y);
                    }
                }
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
