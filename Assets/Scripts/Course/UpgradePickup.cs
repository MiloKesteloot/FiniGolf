// This file and every function within was written by Claude Opus 5.5
using System.Collections;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>Floating power-up. Roll through it to bank an upgrade for your next shot.</summary>
    public class UpgradePickup : MonoBehaviour
    {
        public UpgradeType type;

        private Transform visual;
        private Transform ring;
        private bool collected;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public void Build(UpgradeType upgrade, GameAssets assets, float tileScale)
        {
            type = upgrade;
            var tint = Upgrades.Tint(upgrade);
            float size = 0.12f * tileScale;

            var trigger = gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = size * 1.1f;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);

            GameObject core;
            if (upgrade == UpgradeType.BeachBall)
            {
                core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (assets != null && assets.beachBallMaterial != null)
                    core.GetComponent<Renderer>().sharedMaterial = assets.beachBallMaterial;
                core.transform.localScale = Vector3.one * size;
            }
            else
            {
                core = GameObject.CreatePrimitive(upgrade == UpgradeType.Magnet ? PrimitiveType.Capsule : PrimitiveType.Cube);
                core.transform.localScale = upgrade == UpgradeType.Magnet ? new Vector3(0.5f, 0.6f, 0.5f) * size : Vector3.one * size * 0.75f;
                core.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
                Tint(core, assets, tint);
            }
            Destroy(core.GetComponent<Collider>());
            core.transform.SetParent(visual, false);

            // Glowing halo ring on the ground.
            var halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(halo.GetComponent<Collider>());
            halo.name = "Halo";
            halo.transform.SetParent(transform, false);
            halo.transform.localPosition = Vector3.down * (0.11f * tileScale);
            halo.transform.localScale = new Vector3(size * 2.2f, 0.002f * tileScale, size * 2.2f);
            Tint(halo, assets, tint * 0.9f);
            ring = halo.transform;
        }

        private static void Tint(GameObject go, GameAssets assets, Color tint)
        {
            var r = go.GetComponent<Renderer>();
            if (assets != null && assets.pickupMaterial != null) r.sharedMaterial = assets.pickupMaterial;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, tint);
            block.SetColor(EmissionId, tint * 1.5f);
            r.SetPropertyBlock(block);
        }

        /// <summary>Pool that owns this pickup (null = not pooled, destroy on collect).</summary>
        public PickupPool Pool { get; set; }

        private Vector3 restScale = Vector3.one;
        private Vector3 ringRestScale = Vector3.one;
        private bool built;

        /// <summary>Resets the pickup to its fresh state and enables it at <paramref name="position"/>.</summary>
        public void Activate(Vector3 position)
        {
            if (!built)
            {
                restScale = transform.localScale;
                ringRestScale = ring != null ? ring.localScale : Vector3.one;
                built = true;
            }
            collected = false;
            transform.position = position;
            transform.localScale = restScale;
            if (ring != null) ring.localScale = ringRestScale;
            if (visual != null) visual.localPosition = Vector3.zero;
            gameObject.SetActive(true);
        }

        // Runs every time the pickup is (re)enabled from the pool.
        private void OnEnable()
        {
            StartCoroutine(IdleRoutine());
        }

        private IEnumerator IdleRoutine()
        {
            float seed = Random.value * 6f;
            while (!collected)
            {
                float t = Time.time + seed;
                if (visual != null)
                {
                    visual.localPosition = Vector3.up * Mathf.Sin(t * 2.4f) * 0.03f;
                    visual.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
                }
                if (ring != null)
                {
                    float pulse = 1f + Mathf.Sin(t * 3f) * 0.12f;
                    ring.localScale = new Vector3(ringRestScale.x * pulse, ringRestScale.y, ringRestScale.z * pulse);
                }
                yield return null;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (collected) return;
            var ball = BallManager.Instance;
            if (ball == null || other.attachedRigidbody != ball.Body) return;

            collected = true;
            GameManager.Instance?.BankUpgrade(type);
            Sfx.Instance?.Pickup();
            StartCoroutine(CollectRoutine());
        }

        private IEnumerator CollectRoutine()
        {
            var start = transform.localScale;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.35f;
                float s = t < 0.3f ? Mathf.Lerp(1f, 1.6f, t / 0.3f) : Mathf.Lerp(1.6f, 0f, (t - 0.3f) / 0.7f);
                transform.localScale = start * s;
                transform.position += Vector3.up * Time.deltaTime * 0.8f;
                yield return null;
            }
            // Back to the pool for the next hole instead of being destroyed.
            if (Pool != null) Pool.Release(this);
            else Destroy(gameObject);
        }
    }
}
