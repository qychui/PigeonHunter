using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
    public class GunController : UdonSharpBehaviour
    {
        private const int LaserDisplayOff = 0;
        private const int LaserDisplayLineAndDot = 1;
        private const int LaserDisplayDotOnly = 2;

        public Transform fireOrigin;
        public float fireCooldown = 0.25f;
        public float distance = 40f;
        public LayerMask hitMask = ~0;
        public ParticleSystem muzzleFlash;
        public AudioSource shootSfx;
        public AudioSource clayHitSfx;
        public LineRenderer laserLine;
        public GameObject laserHitDot;
        public float laserDistance = 100f;
        public bool enableLaser;
        public float doubleClickThreshold = 0.2f;
        [UdonSynced]
        [Range(0, 2)] public int laserDisplayMode = LaserDisplayOff;
        [UdonSynced] private bool syncedLaserHeld;
        public float laserHitDotSurfaceOffset = 0.005f;

        [Header("Game State")]
        public GameManager gameManager;

        [Header("Debugging")]
        [SerializeField] private bool logShots;

        private float lastFireTime;
        private float lastUseDownTime = -10f;

        private void Start()
        {
            if (laserDisplayMode == LaserDisplayOff && enableLaser)
            {
                laserDisplayMode = LaserDisplayLineAndDot;
            }

            ApplyLaserDisplayMode(laserDisplayMode);
        }

        private void Update()
        {
            if (ShouldRunLaser())
            {
                FireLaser();
            }
            else
            {
                if (laserLine != null && laserLine.enabled)
                {
                    laserLine.enabled = false;
                }

                HideLaserDot();
            }
        }

        void FireLaser()
        {
            var ray = new Ray(fireOrigin.position, fireOrigin.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, laserDistance))
            {
                if (laserLine != null)
                {
                    laserLine.SetPosition(0, fireOrigin.position);
                    laserLine.SetPosition(1, hit.point);
                }

                UpdateLaserDot(hit.point, hit.normal);
            }
            else
            {
                if (laserLine != null)
                {
                    laserLine.SetPosition(0, fireOrigin.position);
                    laserLine.SetPosition(1, ray.GetPoint(laserDistance));
                }

                HideLaserDot();
            }
        }

        public override void OnPickupUseDown()
        {
            var currentTime = Time.time;

            if (currentTime - lastUseDownTime <= doubleClickThreshold)
            {
                CycleLaserDisplayMode();
                lastUseDownTime = -10f;
                return;
            }

            lastUseDownTime = currentTime;

            TryFire();
        }

        public override void OnPickup()
        {
            if (gameManager != null)
            {
                gameManager.TransferGameplayOwnershipToLocalPlayer(gameObject);
                SetLaserHeldState(true);
                return;
            }

            if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            SetLaserHeldState(true);
        }

        public override void OnDrop()
        {
            SetLaserHeldState(false);
        }

        private void CycleLaserDisplayMode()
        {
            var nextMode = laserDisplayMode + 1;
            if (nextMode > LaserDisplayDotOnly)
            {
                nextMode = LaserDisplayOff;
            }

            SetLaserDisplayMode(nextMode);
        }

        private void SetLaserDisplayMode(int mode)
        {
            if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            ApplyLaserDisplayMode(mode);
            RequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkApplyLaserDisplayMode), laserDisplayMode);
        }

        [NetworkCallable]
        public void NetworkApplyLaserDisplayMode(int mode)
        {
            ApplyLaserDisplayMode(mode);
        }

        public override void OnDeserialization()
        {
            ApplyLaserDisplayMode(laserDisplayMode);
        }

        private void ApplyLaserDisplayMode(int mode)
        {
            laserDisplayMode = Mathf.Clamp(mode, LaserDisplayOff, LaserDisplayDotOnly);
            enableLaser = IsLaserDisplayActive();

            if (laserLine != null)
            {
                laserLine.enabled = ShouldShowLaserLine();
            }

            if (!ShouldShowLaserDot())
            {
                HideLaserDot();
            }
        }

        private bool IsLaserDisplayActive()
        {
            return laserDisplayMode != LaserDisplayOff;
        }

        private bool ShouldRunLaser()
        {
            return syncedLaserHeld && IsLaserDisplayActive();
        }

        private bool ShouldShowLaserLine()
        {
            return syncedLaserHeld && laserDisplayMode == LaserDisplayLineAndDot;
        }

        private bool ShouldShowLaserDot()
        {
            return syncedLaserHeld && (laserDisplayMode == LaserDisplayLineAndDot || laserDisplayMode == LaserDisplayDotOnly);
        }

        private void SetLaserHeldState(bool held)
        {
            syncedLaserHeld = held;
            ApplyLaserDisplayMode(laserDisplayMode);
            RequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkApplyLaserHeldState), syncedLaserHeld);
        }

        [NetworkCallable]
        public void NetworkApplyLaserHeldState(bool held)
        {
            syncedLaserHeld = held;
            ApplyLaserDisplayMode(laserDisplayMode);
        }

        private void UpdateLaserDot(Vector3 hitPoint, Vector3 hitNormal)
        {
            if (!ShouldShowLaserDot() || laserHitDot == null)
            {
                return;
            }

            var dotTransform = laserHitDot.transform;
            dotTransform.position = hitPoint + (hitNormal * laserHitDotSurfaceOffset);
            dotTransform.rotation = Quaternion.LookRotation(hitNormal);

            if (!laserHitDot.activeSelf)
            {
                laserHitDot.SetActive(true);
            }
        }

        private void HideLaserDot()
        {
            if (laserHitDot != null && laserHitDot.activeSelf)
            {
                laserHitDot.SetActive(false);
            }
        }

        private bool TryFire()
        {
            if (gameManager != null && !gameManager.IsLocalGameplayOwner())
            {
                gameManager.TransferGameplayOwnershipToLocalPlayer(gameObject);
            }

            var currentTime = Time.time;

            if (currentTime < lastFireTime + fireCooldown)
            {
                return false;
            }

            if (!QychuiUtilities.TryGetOriginAndDirection(fireOrigin, transform, out Vector3 origin, out Vector3 direction))
            {
                if (gameManager != null)
                {
                    gameManager.NotifyShotOutcome(false);
                }

                return false;
            }

            var ray = new Ray(origin, direction);
            var didHit = Physics.Raycast(ray, out var hitInfo, distance, hitMask);

            if (gameManager != null && gameManager.TryHandleModeOptionHit(didHit ? hitInfo.collider : null))
            {
                lastFireTime = currentTime;
                PlayShotEffects(false);
                return true;
            }

            if (gameManager != null && !gameManager.TryRegisterShot())
            {
                return false;
            }

            if (logShots)
            {
                if (didHit)
                {
                    Debug.Log("[GunController] Hit " + hitInfo.collider.name + " at " + hitInfo.point + " (normal " + hitInfo.normal + ")");
                }
                else
                {
                    Debug.Log("[GunController] Shot missed. Origin " + origin + " Direction " + direction);
                }
            }

            var hitPigeon = false;
            var hitClayTarget = false;

            if (didHit)
            {
                var collider = hitInfo.collider;
                var target = collider.GetComponent<PigeonTarget>();
                var clayTarget = target == null ? collider.GetComponentInParent<ClayTarget>() : null;

                if (target != null)
                {
                    if (!target.BelongsTo(gameManager))
                    {
                        lastFireTime = currentTime;
                        PlayShotEffects(false);
                        if (gameManager != null)
                        {
                            gameManager.NotifyShotOutcome(false);
                        }

                        return true;
                    }

                    hitPigeon = true;
                    var hitPoint = hitInfo.point;
                    var hitNormal = QychuiUtilities.GetSafeNormal(hitInfo.normal, -direction);

                    target.OnShot(hitPoint, hitNormal);

                    Debug.Log("[<color=#0c824c>UdonSharp</color>] hitbox hit");
                }
                else if (clayTarget != null)
                {
                    if (!clayTarget.BelongsTo(gameManager))
                    {
                        lastFireTime = currentTime;
                        PlayShotEffects(false);
                        if (gameManager != null)
                        {
                            gameManager.NotifyShotOutcome(false);
                        }

                        return true;
                    }

                    hitPigeon = true;
                    hitClayTarget = true;
                    var hitPoint = hitInfo.point;
                    var hitNormal = QychuiUtilities.GetSafeNormal(hitInfo.normal, -direction);

                    clayTarget.OnShot(hitPoint, hitNormal);

                    Debug.Log("[<color=#0c824c>UdonSharp</color>] clay hitbox hit");
                }
                else
                {
                    Debug.Log("[<color=#0c824c>UdonSharp</color>] hitbox Null");
                }
            }

            lastFireTime = currentTime;
            PlayShotEffects(hitClayTarget);

            if (gameManager != null)
            {
                gameManager.NotifyShotOutcome(hitPigeon);
            }

            return true;
        }

        private void PlayShotEffects(bool useClayHitSfx)
        {
            var shotAudio = useClayHitSfx ? clayHitSfx : shootSfx;
            var syncedAudio = false;
            var syncedParticle = false;
            var syncedFlash = false;

            if (gameManager != null && gameManager.syncController != null)
            {
                if (gameManager.syncController.HasGunShotFlash())
                {
                    gameManager.syncController.SyncGunShotFlash();
                    syncedFlash = true;
                }

                if (gameManager.syncController.HasGunShotAudio())
                {
                    gameManager.syncController.SyncGunShotAudio(useClayHitSfx);
                    syncedAudio = true;
                }

                if (gameManager.syncController.HasGunMuzzleFlash())
                {
                    gameManager.syncController.SyncGunMuzzleFlash();
                    syncedParticle = true;
                }
            }

            if (!syncedFlash && gameManager != null)
            {
                gameManager.SyncGunShotFlashObjects();
            }

            if (!syncedParticle)
            {
                QychuiUtilities.SafePlay(muzzleFlash);
            }

            if (!syncedAudio)
            {
                QychuiUtilities.SafePlay(shotAudio);
            }
        }
    }
}
