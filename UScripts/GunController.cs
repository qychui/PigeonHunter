using UdonSharp;
using UnityEngine;

namespace PigeonHunt
{
    [AddComponentMenu("PigeonHunt/Gun Controller")]
    public class GunController : UdonSharpBehaviour
    {
        public Transform fireOrigin;
        public float fireCooldown = 0.25f;
        public float distance = 40f;
        public LayerMask hitMask = ~0;
        public ParticleSystem muzzleFlash;
        public AudioSource shootSfx;

        [Header("Game State")]
        public GameManager gameManager;

        [Header("Debugging")]
        [SerializeField] private bool logShots;

        private float lastFireTime;

        private void Start()
        {

        }

        public override void OnPickupUseDown()
        {
            TryFire();
        }

        private bool TryFire()
        {
            var currentTime = Time.time;

            if (currentTime < lastFireTime + fireCooldown)
            {
                return false;
            }

            if (gameManager != null && !gameManager.TryRegisterShot())
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

            lastFireTime = currentTime;

            var ray = new Ray(origin, direction);

            var didHit = Physics.Raycast(ray, out var hitInfo, distance, hitMask);

            QychuiUtilities.SafePlay(muzzleFlash);
            if (gameManager != null && gameManager.soundManager != null)
            {
                gameManager.soundManager.PlayGunShot(shootSfx);
            }
            else
            {
                QychuiUtilities.SafePlay(shootSfx);
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

            if (didHit)
            {
                var collider = hitInfo.collider;
                var target = collider.GetComponent<PigeonTarget>();

                if (target != null)
                {
                    hitPigeon = true;
                    var hitPoint = hitInfo.point;
                    var hitNormal = QychuiUtilities.GetSafeNormal(hitInfo.normal, -direction);

                    target.OnShot(hitPoint, hitNormal);

                    Debug.Log("[<color=#0c824c>UdonSharp</color>] hitbox hit");
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
    }
}
