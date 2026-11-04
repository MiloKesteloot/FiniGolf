// This file and every function within was written by Claude Opus 5.5
using UnityEngine;

namespace FiniGolf
{
    /// <summary>Flat procedural arrow drawn on the ground while aiming. Grows and turns red with power.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class AimArrow : MonoBehaviour
    {
        public float width = 0.07f;
        public float headLength = 0.16f;
        public Gradient powerColors;

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private readonly Vector3[] verts = new Vector3[7];

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static AimArrow Create(Material material)
        {
            var go = new GameObject("AimArrow");
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var arrow = go.AddComponent<AimArrow>();
            arrow.Init();
            go.SetActive(false);
            return arrow;
        }

        private void Init()
        {
            if (mesh != null) return;
            meshRenderer = GetComponent<MeshRenderer>();
            block = new MaterialPropertyBlock();
            mesh = new Mesh { name = "AimArrow" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            if (powerColors == null || powerColors.colorKeys.Length < 2)
            {
                powerColors = new Gradient();
                powerColors.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.3f, 1f, 0.45f), 0f),
                        new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0.55f),
                        new GradientColorKey(new Color(1f, 0.25f, 0.25f), 1f),
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            }
        }

        public void Show(Vector3 origin, Vector3 direction, float startOffset, float length, float power)
        {
            Init();
            gameObject.SetActive(true);
            transform.position = origin;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            float shaftEnd = startOffset + Mathf.Max(0.01f, length - headLength);
            float tip = startOffset + Mathf.Max(length, headLength + 0.01f);
            float w = width * 0.5f;
            float hw = width * 1.25f;

            verts[0] = new Vector3(-w, 0f, startOffset);
            verts[1] = new Vector3(-w, 0f, shaftEnd);
            verts[2] = new Vector3(w, 0f, shaftEnd);
            verts[3] = new Vector3(w, 0f, startOffset);
            verts[4] = new Vector3(-hw, 0f, shaftEnd);
            verts[5] = new Vector3(0f, 0f, tip);
            verts[6] = new Vector3(hw, 0f, shaftEnd);

            mesh.Clear();
            mesh.vertices = verts;
            // Both windings so it renders no matter the shader's cull mode.
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3, 4, 5, 6,
                2, 1, 0, 3, 2, 0, 6, 5, 4,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var c = powerColors.Evaluate(Mathf.Clamp01(power));
            meshRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(ColorId, c);
            meshRenderer.SetPropertyBlock(block);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
