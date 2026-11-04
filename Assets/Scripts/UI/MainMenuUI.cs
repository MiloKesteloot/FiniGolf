using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FiniGolf
{
    /// <summary>Main menu: Play, Credits, Quit.</summary>
    public class MainMenuUI : MonoBehaviour
    {
        public GameObject mainPanel;
        public GameObject creditsPanel;
        public Button playButton;
        public Button creditsButton;
        public Button quitButton;
        public Button backButton;
        public Text bestText;
        public RectTransform title;

        private bool loading;

        private void Start()
        {
            Time.timeScale = 1f;
            if (playButton != null) playButton.onClick.AddListener(Play);
            if (creditsButton != null) creditsButton.onClick.AddListener(() => ShowCredits(true));
            if (backButton != null) backButton.onClick.AddListener(() => ShowCredits(false));
            if (quitButton != null) quitButton.onClick.AddListener(Quit);

            int best = PlayerPrefs.GetInt(GameManager.BestHolesKey, 0);
            if (bestText != null) bestText.text = best > 0 ? $"Best run: {best} hole{(best == 1 ? "" : "s")}" : "Sink as many holes as you can!";

            ShowCredits(false);
            if (title != null) StartCoroutine(BobTitle());
        }

        private void ShowCredits(bool show)
        {
            if (mainPanel != null) mainPanel.SetActive(!show);
            if (creditsPanel != null) creditsPanel.SetActive(show);
        }

        private void Play()
        {
            if (loading) return;
            loading = true;
            StartCoroutine(PlayRoutine());
        }

        private IEnumerator PlayRoutine()
        {
            if (ScreenFader.Instance != null) yield return ScreenFader.Instance.Fade(1f, 0.4f);
            SceneManager.LoadScene(GameManager.GameScene);
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private IEnumerator BobTitle()
        {
            var basePos = title.anchoredPosition;
            while (true)
            {
                float t = Time.unscaledTime;
                title.anchoredPosition = basePos + Vector2.up * Mathf.Sin(t * 1.6f) * 10f;
                title.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.1f) * 2f);
                yield return null;
            }
        }
    }
}
