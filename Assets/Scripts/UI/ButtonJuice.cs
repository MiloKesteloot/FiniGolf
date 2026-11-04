using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FiniGolf
{
    /// <summary>Hover/press scale animation and click sound for menu buttons.</summary>
    public class ButtonJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public float hoverScale = 1.06f;
        public float pressScale = 0.95f;

        private Vector3 baseScale = Vector3.one;
        private float target = 1f;
        private Coroutine routine;

        private void Awake() => baseScale = transform.localScale;

        private void OnDisable()
        {
            transform.localScale = baseScale;
            target = 1f;
            routine = null;
        }

        public void OnPointerEnter(PointerEventData e) => Animate(hoverScale);
        public void OnPointerExit(PointerEventData e) => Animate(1f);
        public void OnPointerDown(PointerEventData e)
        {
            Animate(pressScale);
            Sfx.Instance?.Click();
        }
        public void OnPointerUp(PointerEventData e) => Animate(hoverScale);

        private void Animate(float to)
        {
            target = to;
            if (routine == null && isActiveAndEnabled) routine = StartCoroutine(ScaleRoutine());
        }

        private IEnumerator ScaleRoutine()
        {
            while (Mathf.Abs(transform.localScale.x / baseScale.x - target) > 0.001f)
            {
                float s = Mathf.Lerp(transform.localScale.x / baseScale.x, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
                transform.localScale = baseScale * s;
                yield return null;
            }
            transform.localScale = baseScale * target;
            routine = null;
        }
    }
}
