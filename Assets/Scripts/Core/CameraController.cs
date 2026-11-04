using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FiniGolf
{
    /// <summary>
    /// Third-person orbit camera that follows the ball.
    /// Right-drag or A/D (Q/E, arrow keys) to orbit, W/S to tilt, scroll to zoom.
    /// </summary>
    public class CameraController : Singleton<CameraController>
    {
        public Transform target;
        public float distance = 3.6f;
        public float minDistance = 1.2f;
        public float maxDistance = 12f;
        public float pitch = 36f;
        public float minPitch = 10f;
        public float maxPitch = 85f;
        public float yaw;
        public float mouseOrbitSpeed = 0.25f;
        public float keyOrbitSpeed = 110f;
        public float followSmoothTime = 0.12f;

        public bool FollowTarget { get; set; } = true;

        private Vector3 focus;
        private Vector3 focusVelocity;
        private bool scripted;

        private void LateUpdate()
        {
            if (scripted) return;
            HandleInput(Time.unscaledDeltaTime);

            if (target != null && FollowTarget && target.gameObject.activeInHierarchy)
                focus = Vector3.SmoothDamp(focus, target.position, ref focusVelocity, followSmoothTime);

            ApplyPose(focus, yaw, pitch, distance);
        }

        private void HandleInput(float dt)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (mouse != null)
            {
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
                {
                    var d = mouse.delta.ReadValue();
                    yaw += d.x * mouseOrbitSpeed;
                    pitch -= d.y * mouseOrbitSpeed;
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance *= Mathf.Pow(0.9f, Mathf.Sign(scroll) * Mathf.Min(3f, Mathf.Abs(scroll) / 120f + 0.5f));
            }

            if (kb != null)
            {
                float orbit = 0f;
                if (kb.aKey.isPressed || kb.qKey.isPressed || kb.leftArrowKey.isPressed) orbit += 1f;
                if (kb.dKey.isPressed || kb.eKey.isPressed || kb.rightArrowKey.isPressed) orbit -= 1f;
                yaw += orbit * keyOrbitSpeed * dt;

                float tilt = 0f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) tilt += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) tilt -= 1f;
                pitch += tilt * keyOrbitSpeed * 0.5f * dt;
            }

            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        private void ApplyPose(Vector3 f, float y, float p, float dist)
        {
            var rot = Quaternion.Euler(p, y, 0f);
            transform.SetPositionAndRotation(f + rot * new Vector3(0f, 0f, -dist), rot);
        }

        /// <summary>Instantly frames the target from behind, looking along <paramref name="forward"/>.</summary>
        public void SnapBehind(Transform newTarget, Vector3 forward)
        {
            target = newTarget;
            FollowTarget = true;
            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            focus = target != null ? target.position : focus;
            focusVelocity = Vector3.zero;
            ApplyPose(focus, yaw, pitch, distance);
        }

        private Vector3 overviewFocus;
        private float overviewDistance;
        private const float OverviewPitch = 62f;

        /// <summary>
        /// Jumps straight to the high overview shot of the hole and holds it there
        /// (call before fading in, so the hole is first seen from above).
        /// </summary>
        public void ShowOverview(Bounds course, Vector3 hole)
        {
            scripted = true;
            overviewFocus = Vector3.Lerp(course.center, hole, 0.25f);
            overviewDistance = Mathf.Clamp(course.extents.magnitude * 1.9f, 5f, 40f);
            ApplyPose(overviewFocus, yaw, OverviewPitch, overviewDistance);
        }

        /// <summary>Coroutine: overview of the whole hole, then sweep down behind the ball. Click to skip.</summary>
        public IEnumerator IntroFlyover(Bounds course, Vector3 hole)
        {
            if (!scripted) ShowOverview(course, hole);
            float endYaw = yaw, endPitch = pitch, endDistance = distance;
            var endFocus = target != null ? target.position : focus;
            float overviewPitch = OverviewPitch;
            float overviewYaw = endYaw;

            float hold = 0.9f, sweep = 1.5f, t = 0f;
            bool skipped = false;
            while (t < hold + sweep)
            {
                if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame) { skipped = true; break; }
                if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) { skipped = true; break; }

                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - hold) / sweep));
                ApplyPose(
                    Vector3.Lerp(overviewFocus, endFocus, k),
                    Mathf.LerpAngle(overviewYaw, endYaw, k),
                    Mathf.Lerp(overviewPitch, endPitch, k),
                    Mathf.Lerp(overviewDistance, endDistance, k));
                yield return null;
            }

            if (skipped) yield return null;
            focus = endFocus;
            focusVelocity = Vector3.zero;
            ApplyPose(focus, endYaw, endPitch, endDistance);
            scripted = false;
        }
    }
}
