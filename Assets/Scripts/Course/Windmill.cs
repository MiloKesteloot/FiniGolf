using System.Collections;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>Spins the windmill blades with a coroutine.</summary>
    public class Windmill : MonoBehaviour
    {
        public float degreesPerSecond = 80f;

        private Transform blades;
        private Vector3 hub;
        private Vector3 axis;

        private void Start()
        {
            blades = FindChildContaining(transform, "blade");
            if (blades == null)
            {
                Debug.LogWarning("[FiniGolf] Windmill has no 'blades' child to spin.");
                return;
            }

            var r = blades.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                var b = r.bounds;
                hub = b.center;
                // The blades are thin along their rotation axis.
                axis = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
            }
            else
            {
                hub = blades.position;
                axis = transform.forward;
            }

            StartCoroutine(SpinBlades());
        }

        private IEnumerator SpinBlades()
        {
            // Spin up from rest for a nice touch, then keep turning forever.
            float speed = 0f;
            while (true)
            {
                speed = Mathf.MoveTowards(speed, degreesPerSecond, degreesPerSecond * Time.deltaTime);
                blades.RotateAround(hub, axis, speed * Time.deltaTime);
                yield return null;
            }
        }

        private static Transform FindChildContaining(Transform t, string fragment)
        {
            foreach (Transform child in t)
            {
                if (child.name.ToLowerInvariant().Contains(fragment)) return child;
                var deeper = FindChildContaining(child, fragment);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
