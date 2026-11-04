// This file and every function within was written by Claude Opus 5.5
using System.Collections;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>Full-screen fade used for scene and hole transitions.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ScreenFader : Singleton<ScreenFader>
    {
        [Tooltip("Fade in from black automatically when the scene starts.")]
        public bool fadeInOnStart = false;

        private CanvasGroup group;

        protected override void Awake()
        {
            base.Awake();
            group = GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        private void Start()
        {
            if (fadeInOnStart) StartCoroutine(Fade(0f, 0.6f));
        }

        public IEnumerator Fade(float targetAlpha, float duration)
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            float from = group.alpha;
            float t = 0f;
            group.blocksRaycasts = targetAlpha > 0.5f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
                group.alpha = Mathf.Lerp(from, targetAlpha, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            group.alpha = targetAlpha;
        }
    }
}
