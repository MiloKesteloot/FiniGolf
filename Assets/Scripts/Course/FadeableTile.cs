// This file and every function within was written by Claude Opus 5.5
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// A tall course piece (windmill, castle, tunnel, raised bridge tile) that fades to
    /// <see cref="fadedAlpha"/> while it hides the ball or the cup from the camera.
    /// While faded its renderers use a transparent copy of their material; when fully back they
    /// return to the original opaque material so normal sorting and shadows are unaffected.
    /// </summary>
    public class FadeableTile : MonoBehaviour
    {
        [Range(0f, 1f)] public float fadedAlpha = 0.3f;
        [Tooltip("Alpha change per second.")]
        public float fadeSpeed = 4f;
        [Tooltip("How long the tile stays faded after it stops blocking the view.")]
        public float holdTime = 0.2f;
        [Tooltip("Tall structure (windmill, castle, tunnel) rather than just a raised tile.")]
        public bool isStructure;
        [HideInInspector] public float tileSize = 2f;

        /// <summary>True if the point lies over this tile's grid cell.</summary>
        public bool Contains(Vector3 point)
        {
            var c = transform.position;
            float half = tileSize * 0.5f;
            return Mathf.Abs(point.x - c.x) < half && Mathf.Abs(point.z - c.z) < half;
        }

        private Renderer[] renderers;
        private Material[][] originalMaterials;
        private Material[][] fadedMaterials;
        private Color[] baseColors;
        private MaterialPropertyBlock block;
        private float alpha = 1f;
        private float lastOccludedTime = -100f;
        private bool usingFaded;
        private Coroutine routine;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Dictionary<Material, Material> FadedCache = new Dictionary<Material, Material>();

        /// <summary>Called by the camera every frame this tile blocks the view.</summary>
        public void MarkOccluding()
        {
            lastOccludedTime = Time.time;
            if (routine == null && isActiveAndEnabled) routine = StartCoroutine(FadeRoutine());
        }

        private IEnumerator FadeRoutine()
        {
            Init();
            while (true)
            {
                bool occluded = Time.time - lastOccludedTime < holdTime;
                float target = occluded ? fadedAlpha : 1f;
                alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);
                Apply();
                if (!occluded && alpha >= 1f) break;
                yield return null;
            }
            routine = null;
        }

        private void Init()
        {
            if (renderers != null) return;
            block = new MaterialPropertyBlock();
            var all = GetComponentsInChildren<Renderer>(true);
            if (isStructure)
            {
                var parts = new List<Renderer>();
                foreach (var r in all)
                    if (r.GetComponent<FadePart>() != null) parts.Add(r);
                renderers = parts.ToArray();
            }
            else
            {
                renderers = all;
            }
            originalMaterials = new Material[renderers.Length][];
            fadedMaterials = new Material[renderers.Length][];
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].sharedMaterials;
                originalMaterials[i] = mats;
                fadedMaterials[i] = new Material[mats.Length];
                for (int m = 0; m < mats.Length; m++) fadedMaterials[i][m] = FadedVersion(mats[m]);
                var first = mats.Length > 0 ? mats[0] : null;
                baseColors[i] = first != null && first.HasProperty(BaseColorId) ? first.GetColor(BaseColorId) : Color.white;
            }
        }

        private void Apply()
        {
            bool shouldFade = alpha < 0.999f;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (shouldFade != usingFaded)
                    r.sharedMaterials = shouldFade ? fadedMaterials[i] : originalMaterials[i];

                if (shouldFade)
                {
                    var c = baseColors[i];
                    c.a = alpha;
                    r.GetPropertyBlock(block);
                    block.SetColor(BaseColorId, c);
                    r.SetPropertyBlock(block);
                }
                else
                {
                    r.SetPropertyBlock(null);
                }
            }
            usingFaded = shouldFade;
        }

        private static Material FadedVersion(Material source)
        {
            if (source == null) return null;
            if (FadedCache.TryGetValue(source, out var cached) && cached != null) return cached;

            Material faded;
            var assets = CourseManager.Instance != null ? CourseManager.Instance.assets : null;
            if (assets != null && source == assets.tileMaterial && assets.fadedTileMaterial != null)
            {
                faded = assets.fadedTileMaterial;
            }
            else
            {
                faded = new Material(source) { name = source.name + " (Faded)" };
                MaterialUtil.SetupTransparentLit(faded);
            }
            FadedCache[source] = faded;
            return faded;
        }

        private readonly List<Mesh> ownedMeshes = new List<Mesh>();

        /// <summary>
        /// Splits every mesh of this tile at <paramref name="worldY"/>: triangles fully below stay on the
        /// original (solid) renderer, the rest move to a new child renderer tagged <see cref="FadePart"/>.
        /// Colliders keep the original, unsplit mesh.
        /// </summary>
        public void SplitStructure(float worldY)
        {
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;

                if (mf.gameObject.name.ToLowerInvariant().Contains("blade") || !mf.sharedMesh.isReadable)
                {
                    mf.gameObject.AddComponent<FadePart>();
                    continue;
                }

                var mesh = mf.sharedMesh;
                var l2w = mf.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                var high = new bool[verts.Length];
                for (int i = 0; i < verts.Length; i++) high[i] = l2w.MultiplyPoint3x4(verts[i]).y > worldY;

                int subCount = mesh.subMeshCount;
                var baseTris = new List<int>[subCount];
                var topTris = new List<int>[subCount];
                int baseCount = 0, topCount = 0;
                for (int sub = 0; sub < subCount; sub++)
                {
                    baseTris[sub] = new List<int>();
                    topTris[sub] = new List<int>();
                    var tris = mesh.GetTriangles(sub);
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        bool top = high[tris[t]] || high[tris[t + 1]] || high[tris[t + 2]];
                        var list = top ? topTris[sub] : baseTris[sub];
                        list.Add(tris[t]); list.Add(tris[t + 1]); list.Add(tris[t + 2]);
                        if (top) topCount++; else baseCount++;
                    }
                }

                if (topCount == 0) continue;                                  // all base: never fades
                if (baseCount == 0) { mf.gameObject.AddComponent<FadePart>(); continue; } // all structure

                var baseMesh = Instantiate(mesh);
                baseMesh.name = mesh.name + "_base";
                var topMesh = Instantiate(mesh);
                topMesh.name = mesh.name + "_structure";
                for (int sub = 0; sub < subCount; sub++)
                {
                    baseMesh.SetTriangles(baseTris[sub], sub);
                    topMesh.SetTriangles(topTris[sub], sub);
                }
                baseMesh.RecalculateBounds();
                topMesh.RecalculateBounds();
                ownedMeshes.Add(baseMesh);
                ownedMeshes.Add(topMesh);

                mf.sharedMesh = baseMesh;

                var go = new GameObject(mf.gameObject.name + "_structure");
                go.layer = mf.gameObject.layer;
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = topMesh;
                var topRenderer = go.AddComponent<MeshRenderer>();
                topRenderer.sharedMaterials = mr.sharedMaterials;
                topRenderer.shadowCastingMode = mr.shadowCastingMode;
                topRenderer.receiveShadows = mr.receiveShadows;
                go.AddComponent<FadePart>();
            }
            renderers = null; // re-gather on first fade
        }

        private void OnDestroy()
        {
            foreach (var m in ownedMeshes)
                if (m != null) Destroy(m);
            ownedMeshes.Clear();
        }

        private void OnDisable()
        {
            routine = null;
        }
    }
}
