using System.Collections;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>Main menu background: generates random holes and slowly orbits the camera around them.</summary>
    public class MenuBackdrop : MonoBehaviour
    {
        public Camera cam;
        public float orbitDegreesPerSecond = 9f;
        public float secondsPerCourse = 14f;
        public float pitch = 38f;

        private IEnumerator Start()
        {
            if (cam == null) cam = Camera.main;
            var course = CourseManager.Instance;
            if (course == null || cam == null || course.assets == null)
            {
                if (ScreenFader.Instance != null) yield return ScreenFader.Instance.Fade(0f, 0.5f);
                yield break;
            }

            float yaw = Random.Range(0f, 360f);
            bool first = true;
            while (true)
            {
                if (!first && ScreenFader.Instance != null) yield return ScreenFader.Instance.Fade(1f, 0.5f);

                course.Generate(Random.Range(4, 12), Random.Range(int.MinValue, int.MaxValue));
                var bounds = course.Bounds;
                float dist = Mathf.Clamp(bounds.extents.magnitude * 1.6f, 6f, 40f);

                OrbitStep(bounds.center, ref yaw, dist, 0f);
                if (ScreenFader.Instance != null) StartCoroutine(ScreenFader.Instance.Fade(0f, first ? 0.8f : 0.5f));
                first = false;

                for (float t = 0f; t < secondsPerCourse; t += Time.deltaTime)
                {
                    OrbitStep(bounds.center, ref yaw, dist, Time.deltaTime);
                    yield return null;
                }
            }
        }

        private void OrbitStep(Vector3 center, ref float yaw, float dist, float dt)
        {
            yaw += orbitDegreesPerSecond * dt;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.SetPositionAndRotation(center + rot * new Vector3(0f, 0f, -dist), rot);
        }
    }
}
