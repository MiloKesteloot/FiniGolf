// This file and every function within was written by Claude Opus 5.5
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
        public GameObject optionsPanel;
        public Button playButton;
        public Button optionsButton;
        public Button creditsButton;
        public Button quitButton;
        public Button backButton;
        public Button optionsBackButton;
        public Text bestText;
        public RectTransform title;

        private bool loading;

        private void Start()
        {
            Time.timeScale = 1f;
            if (playButton != null) playButton.onClick.AddListener(Play);
            if (creditsButton != null) creditsButton.onClick.AddListener(() => ShowPanel(creditsPanel));
            if (optionsButton != null) optionsButton.onClick.AddListener(() => ShowPanel(optionsPanel));
            if (backButton != null) backButton.onClick.AddListener(() => ShowPanel(mainPanel));
            if (optionsBackButton != null) optionsBackButton.onClick.AddListener(() => ShowPanel(mainPanel));
            if (quitButton != null) quitButton.onClick.AddListener(Quit);

            int best = PlayerPrefs.GetInt(GameManager.BestHolesKey, 0);
            if (bestText != null) bestText.text = best > 0 ? $"Best run: {best} hole{(best == 1 ? "" : "s")}" : "Sink as many holes as you can!";

            ShowPanel(mainPanel);
            if (title != null) StartCoroutine(BobTitle());
        }

        private void ShowPanel(GameObject panel)
        {
            if (mainPanel != null) mainPanel.SetActive(panel == mainPanel);
            if (creditsPanel != null) creditsPanel.SetActive(panel == creditsPanel);
            if (optionsPanel != null) optionsPanel.SetActive(panel == optionsPanel);
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
