// This file and every function within was written by Claude Opus 5.5
using System.Collections;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>Flag in the cup: sways gently and lifts out when the ball rolls close (coroutine).</summary>
    public class HoleFlag : MonoBehaviour
    {
        public float liftHeight = 0.7f;
        public float liftRadius = 1.4f;

        private IEnumerator Start()
        {
            var basePos = transform.localPosition;
            var baseRot = transform.localRotation;
            var ball = FindFirstObjectByType<BallManager>();
            float lift = 0f;
            float seed = Random.value * 10f;

            while (true)
            {
                float target = 0f;
                if (ball != null && ball.BallTransform != null && (ball.IsMoving || ball.State == BallState.Sinking))
                {
                    var d = ball.BallTransform.position - transform.position;
                    d.y = 0f;
                    if (d.magnitude < liftRadius) target = 1f;
                }

                lift = Mathf.MoveTowards(lift, target, Time.deltaTime * 3f);
                float eased = Mathf.SmoothStep(0f, 1f, lift);
                float parentScale = transform.parent != null ? transform.parent.lossyScale.y : 1f;
                transform.localPosition = basePos + Vector3.up * (eased * liftHeight / Mathf.Max(0.0001f, parentScale));

                float sway = Mathf.Sin(Time.time * 1.7f + seed) * 6f;
                transform.localRotation = baseRot * Quaternion.Euler(0f, sway, 0f);
                yield return null;
            }
        }
    }
}
