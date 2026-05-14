using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace PigeonHunt
{
    public class QychuiUtilities : UdonSharpBehaviour
    {
        private const float MinVectorSqrMagnitude = 0.0001f;

        /// <summary>
        /// Plays an audio source if the reference is valid.
        /// </summary>
        public static void SafePlay(AudioSource source)
        {
            if (source != null)
            {
                source.Play();
            }
        }

        /// <summary>
        /// Stops an audio source if it is currently playing.
        /// </summary>
        public static void SafeStop(AudioSource source)
        {
            if (source != null && source.isPlaying)
            {
                source.Stop();
            }
        }

        /// <summary>
        /// Plays a particle system when the reference exists.
        /// </summary>
        public static void SafePlay(ParticleSystem particleSystem)
        {
            if (particleSystem != null)
            {
                particleSystem.Play();
            }
        }

        /// <summary>
        /// Stops a particle system when needed.
        /// </summary>
        public static void SafeStop(ParticleSystem particleSystem)
        {
            if (particleSystem != null)
            {
                particleSystem.Stop();
            }
        }

        /// <summary>
        /// Returns a normalized normal vector, falling back to the supplied direction when the original normal is invalid.
        /// </summary>
        public static Vector3 GetSafeNormal(Vector3 normal, Vector3 fallbackDirection)
        {
            if (normal.sqrMagnitude > MinVectorSqrMagnitude)
            {
                return normal.normalized;
            }

            if (fallbackDirection.sqrMagnitude < MinVectorSqrMagnitude)
            {
                return Vector3.up;
            }

            return fallbackDirection.normalized;
        }

        /// <summary>
        /// Attempts to build a usable ray origin and direction from the provided transforms.
        /// </summary>
        public static bool TryGetOriginAndDirection(Transform primaryOrigin, Transform fallbackOrigin, out Vector3 origin, out Vector3 direction)
        {
            var resolved = primaryOrigin != null ? primaryOrigin : fallbackOrigin;

            origin = Vector3.zero;
            direction = Vector3.forward;

            if (resolved == null)
            {
                return false;
            }

            origin = resolved.position;
            direction = resolved.forward;

            if (direction.sqrMagnitude < MinVectorSqrMagnitude && fallbackOrigin != null)
            {
                direction = fallbackOrigin.forward;
            }

            if (direction.sqrMagnitude < MinVectorSqrMagnitude)
            {
                direction = Vector3.forward;
            }

            direction.Normalize();
            return true;
        }

        /// <summary>
        /// Calculates axis-aligned world bounds for a RectTransform.
        /// Returns false when the reference is null.
        /// </summary>
        public static bool TryGetRectWorldBounds(
            RectTransform rect,
            Vector3[] cornerBuffer,
            out float minX,
            out float maxX,
            out float minY,
            out float maxY,
            out float planeZ)
        {
            minX = 0f;
            maxX = 0f;
            minY = 0f;
            maxY = 0f;
            planeZ = 0f;

            if (rect == null)
            {
                return false;
            }

            Vector3[] corners = cornerBuffer != null && cornerBuffer.Length >= 4 ? cornerBuffer : new Vector3[4];
            rect.GetWorldCorners(corners);

            minX = corners[0].x;
            maxX = corners[2].x;
            if (minX > maxX)
            {
                var swap = minX;
                minX = maxX;
                maxX = swap;
            }

            minY = corners[0].y;
            maxY = corners[1].y;
            if (minY > maxY)
            {
                var swap = minY;
                minY = maxY;
                maxY = swap;
            }

            planeZ = (corners[0].z + corners[2].z) * 0.5f;
            return true;
        }

        /// <summary>
        /// Converts a percent value (0-100) to a ratio (0-1).
        /// </summary>
        public static float PercentToRatio(float percent)
        {
            return Mathf.Clamp01(percent * 0.01f);
        }

        /// <summary>
        /// Returns the percent portion of a base value.
        /// </summary>
        public static float GetPercentValue(float baseValue, float percent)
        {
            return Mathf.Max(0f, baseValue) * PercentToRatio(percent);
        }

        /// <summary>
        /// Ensures a BoxCollider reference is assigned by searching the owner hierarchy when needed.
        /// </summary>
        public static BoxCollider EnsureBoxCollider(Component owner, BoxCollider collider)
        {
            if (collider == null && owner != null)
            {
                collider = owner.GetComponentInChildren<BoxCollider>(true);
            }

            return collider;
        }

        /// <summary>
        /// Toggles a collider reference when it exists.
        /// </summary>
        public static void SetColliderEnabled(Collider collider, bool shouldEnable)
        {
            if (collider != null)
            {
                collider.enabled = shouldEnable;
            }
        }
    }
}
