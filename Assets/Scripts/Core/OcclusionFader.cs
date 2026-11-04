// This file and every function within was written by Claude Opus 5.5
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// Lives on the game camera. Each frame it sweeps a thin sphere from the camera to the ball and to
    /// the cup; any <see cref="FadeableTile"/> in the way is told to fade out.
    /// </summary>
    public class OcclusionFader : MonoBehaviour
    {
        [Tooltip("Thickness of the view check, as a fraction of the ball radius.")]
        public float probeRadiusFactor = 0.8f;

        private readonly RaycastHit[] hits = new RaycastHit[16];

        private void LateUpdate()
        {
            var course = CourseManager.Instance;
            if (course == null) return;
            float ballRadius = 0.035f * course.tileScale;

            var ball = BallManager.Instance;
            if (ball != null && ball.BallTransform != null && ball.BallTransform.gameObject.activeInHierarchy)
            {
                var p = ball.BallTransform.position;
                Check(p, ballRadius);
                Check(p + Vector3.up * ball.Radius, ballRadius);
            }

            Check(course.HolePosition + Vector3.up * (0.03f * course.tileScale), ballRadius);
        }

        private void Check(Vector3 target, float ballRadius)
        {
            var origin = transform.position;
            var dir = target - origin;
            float dist = dir.magnitude;
            if (dist < 0.01f) return;
            dir /= dist;

            float radius = ballRadius * probeRadiusFactor;
            // Stop a little short of the target so the floor right under it doesn't count.
            float length = dist - ballRadius * 1.5f;
            if (length <= 0f) return;

            int count = Physics.SphereCastNonAlloc(origin, radius, dir, hits, length,
                1 << CourseManager.OcclusionLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var fade = hits[i].collider.GetComponentInParent<FadeableTile>();
                if (fade == null) continue;
                // A raised tile never fades for things standing on it (only for things it hides).
                if (!fade.isStructure && fade.Contains(target)) continue;
                fade.MarkOccluding();
            }
        }
    }
}
