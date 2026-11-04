using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// Draws the A* "best path" from the ball to the cup as a glowing dotted line that traces itself out,
    /// drifts toward the hole and slowly pulses. Shown at the start of each hole and every time the ball stops.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class PathGuide : Singleton<PathGuide>
    {
        public Color color = new Color(1f, 0.95f, 0.55f, 1f);
        [Tooltip("Seconds for one slow flash.")]
        public float pulsePeriod = 1.8f;
        public float minAlpha = 0.25f;
        public float maxAlpha = 0.9f;
        public float traceSeconds = 0.7f;
        public float dotSpacing = 0.11f;
        public float flowSpeed = 0.35f;
        public float width = 0.07f;
        public float hoverHeight = 0.02f;

        private LineRenderer line;
        private Material material;
        private readonly List<Vector3> path = new List<Vector3>();
        private Coroutine routine;
        private float visibility;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <summary>Finds or creates the guide (so the Game scene doesn't need rebuilding).</summary>
        public static PathGuide Ensure(GameAssets assets)
        {
            var existing = FindFirstObjectByType<PathGuide>();
            if (existing != null) return existing;
            var go = new GameObject("PathGuide");
            go.SetActive(false);
            var guide = go.AddComponent<PathGuide>();
            guide.Setup(assets);
            go.SetActive(true);
            return guide;
        }

        private void Setup(GameAssets assets)
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Tile;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;

            var s = CourseManager.Instance != null ? CourseManager.Instance.tileScale / 2f : 1f;
            line.widthMultiplier = width * s;

            Material source = assets != null ? assets.pathMaterial : null;
            material = source != null ? new Material(source) : MaterialUtil.CreateTransparentUnlit();
            if (material != null)
            {
                material.SetTexture(BaseMapId, MakeDotTexture());
                material.mainTexture = material.GetTexture(BaseMapId);
                line.sharedMaterial = material;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (line == null) line = GetComponent<LineRenderer>();
            if (line.sharedMaterial == null) Setup(CourseManager.Instance != null ? CourseManager.Instance.assets : null);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (material != null) Destroy(material);
        }

        /// <summary>Runs A* from <paramref name="from"/> to the cup and traces the result.</summary>
        public void ShowFrom(Vector3 from)
        {
            var course = CourseManager.Instance;
            if (course == null) return;

            var points = new List<Vector3>();
            if (!course.Pathfinder.FindPath(from, course.HolePosition, points))
            {
                Hide();
                return;
            }

            path.Clear();
            float lift = hoverHeight * course.tileScale / 2f;
            foreach (var p in points) path.Add(p + Vector3.up * lift);

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(TraceRoutine());
        }

        public void Hide()
        {
            if (!isActiveAndEnabled) return;
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(FadeOutRoutine());
        }

        private IEnumerator TraceRoutine()
        {
            // Cumulative lengths so the trace reveals at constant speed.
            var lengths = new float[path.Count];
            for (int i = 1; i < path.Count; i++) lengths[i] = lengths[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            float total = Mathf.Max(0.001f, lengths[path.Count - 1]);

            var revealed = new List<Vector3>(path.Count);
            float t = 0f;
            visibility = 1f;
            while (t < 1f)
            {
                t += Time.deltaTime / traceSeconds;
                float target = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)) * total;
                revealed.Clear();
                for (int i = 0; i < path.Count; i++)
                {
                    if (lengths[i] <= target) { revealed.Add(path[i]); continue; }
                    float seg = lengths[i] - lengths[i - 1];
                    float k = seg > 0f ? (target - lengths[i - 1]) / seg : 0f;
                    revealed.Add(Vector3.Lerp(path[i - 1], path[i], k));
                    break;
                }
                SetPoints(revealed);
                Animate(1f);
                yield return null;
            }

            SetPoints(path);
            while (true)
            {
                Animate(1f);
                yield return null;
            }
        }

        private IEnumerator FadeOutRoutine()
        {
            while (visibility > 0f)
            {
                visibility = Mathf.MoveTowards(visibility, 0f, Time.deltaTime / 0.2f);
                Animate(visibility);
                yield return null;
            }
            line.positionCount = 0;
            routine = null;
        }

        private void SetPoints(List<Vector3> points)
        {
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        }

        private void Animate(float fade)
        {
            // Slow flash + dots flowing toward the hole.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / pulsePeriod);
            var c = color;
            c.a = Mathf.Lerp(minAlpha, maxAlpha, pulse) * fade;
            var faded = c;
            faded.a *= 0.35f;
            line.startColor = faded;
            line.endColor = c;

            if (material != null)
            {
                float s = CourseManager.Instance != null ? CourseManager.Instance.tileScale / 2f : 1f;
                material.SetTextureScale(BaseMapId, new Vector2(1f / (dotSpacing * s), 1f));
                material.SetTextureOffset(BaseMapId, new Vector2(-Time.time * flowSpeed / (dotSpacing * s), 0f));
            }
        }

        private static Texture2D MakeDotTexture()
        {
            const int w = 64, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "PathDots" };
            var center = new Vector2(w * 0.5f, h * 0.5f);
            float radius = h * 0.42f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float a = Mathf.Clamp01((radius - d) / 1.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
