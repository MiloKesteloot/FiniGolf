using System.Collections;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>A block that slides back and forth across the lane, driven by a coroutine.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SlidingHazard : MonoBehaviour
    {
        public Vector3 axis = Vector3.right;
        public float amplitude = 0.5f;
        public float period = 3f;
        public float phase;

        private Rigidbody body;
        private BoxCollider box;
        private BallManager ball;
        private readonly Collider[] overlaps = new Collider[4];

        private IEnumerator Start()
        {
            body = GetComponent<Rigidbody>();
            box = GetComponent<BoxCollider>();
            ball = FindFirstObjectByType<BallManager>();
            var origin = transform.position;
            float t = phase;
            var wait = new WaitForFixedUpdate();

            while (true)
            {
                yield return wait;
                float next = t + Time.fixedDeltaTime;
                var target = origin + axis * (Mathf.Sin(next / period * Mathf.PI * 2f) * amplitude);

                // Never shove a resting ball (it is frozen while you aim), just wait for it to leave.
                if (!WouldHitRestingBall(target))
                {
                    body.MovePosition(target);
                    t = next;
                }
            }
        }

        private bool WouldHitRestingBall(Vector3 target)
        {
            if (ball == null || box == null || !ball.IsResting || ball.Body == null) return false;
            var half = Vector3.Scale(box.size * 0.5f, transform.lossyScale) + Vector3.one * 0.01f;
            int n = Physics.OverlapBoxNonAlloc(target, half, overlaps, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (overlaps[i].attachedRigidbody == ball.Body) return true;
            return false;
        }
    }
}
