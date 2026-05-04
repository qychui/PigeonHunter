using UdonSharp;
using UnityEngine;

namespace PigeonHunt
{
    [AddComponentMenu("PigeonHunt/Gun Controller")]
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
        public LineRenderer laserLine; 
        public GameObject laserHitDot;
        public float laserDistance = 100f;
        public bool enableLaser;
        public float doubleClickThreshold = 0.2f;
        [Range(0, 2)] public int laserDisplayMode = LaserDisplayOff;
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

            SyncLaserState();
        }

        private void Update()
        {
            if (IsLaserDisplayActive())
            {
                FireLaser();
            }
            else
            {
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

        private void CycleLaserDisplayMode()
        {
            laserDisplayMode++;
            if (laserDisplayMode > LaserDisplayDotOnly)
            {
                laserDisplayMode = LaserDisplayOff;
            }

            SyncLaserState();
        }

        private void SyncLaserState()
        {
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

        private bool ShouldShowLaserLine()
        {
            return laserDisplayMode == LaserDisplayLineAndDot;
        }

        private bool ShouldShowLaserDot()
        {
            return laserDisplayMode == LaserDisplayLineAndDot || laserDisplayMode == LaserDisplayDotOnly;
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
                PlayShotEffects();
                return true;
            }

            if (gameManager != null && !gameManager.TryRegisterShot())
            {
                return false;
            }

            lastFireTime = currentTime;
            PlayShotEffects();

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

            if (didHit)
            {
                var collider = hitInfo.collider;
                var target = collider.GetComponent<PigeonTarget>();
                var clayTarget = target == null ? collider.GetComponentInParent<ClayTarget>() : null;

                if (target != null)
                {
                    hitPigeon = true;
                    var hitPoint = hitInfo.point;
                    var hitNormal = QychuiUtilities.GetSafeNormal(hitInfo.normal, -direction);

                    target.OnShot(hitPoint, hitNormal);

                    Debug.Log("[<color=#0c824c>UdonSharp</color>] hitbox hit");
                }
                else if (clayTarget != null)
                {
                    hitPigeon = true;
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

            if (gameManager != null)
            {
                gameManager.NotifyShotOutcome(hitPigeon);
            }

            return true;
        }

        private void PlayShotEffects()
        {
            QychuiUtilities.SafePlay(muzzleFlash);
            if (gameManager != null && gameManager.soundManager != null)
            {
                gameManager.soundManager.PlayGunShot(shootSfx);
            }
            else
            {
                QychuiUtilities.SafePlay(shootSfx);
            }
        }

    }
}

