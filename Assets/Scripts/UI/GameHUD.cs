// This file and every function within was written by Claude Opus 5.5
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FiniGolf
{
    /// <summary>In-game UI: hole, par, strokes, hearts, upgrade banner, toasts and the end-of-hole / game-over / pause panels.</summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("Top bar")]
        public Text holeText;
        public Text holeSubText;
        public Text parValue;
        public Text strokesValue;
        public Image[] hearts;
        public Color heartFull = new Color(0.96f, 0.28f, 0.4f);
        public Color heartEmpty = new Color(1f, 1f, 1f, 0.18f);

        [Header("Upgrade banner")]
        public GameObject upgradePanel;
        public Image upgradeAccent;
        public Text upgradeTitle;
        public Text upgradeDescription;

        [Header("Aiming")]
        public GameObject powerBar;
        public RectTransform powerFill;
        public Image powerFillImage;
        public Text hintText;

        [Header("Toast")]
        public CanvasGroup toastGroup;
        public Text toastText;

        [Header("Hole complete")]
        public GameObject holePanel;
        public Text holeTitle;
        public Text holeDetail;

        [Header("Game over")]
        public GameObject gameOverPanel;
        public Text gameOverReason;
        public Text gameOverStats;
        public Button retryButton;
        public Button gameOverMenuButton;

        [Header("Pause")]
        public GameObject pausePanel;
        public Button resumeButton;
        public Button pauseMenuButton;

        private GameManager gm;
        private readonly Queue<(string text, Color color)> toasts = new Queue<(string, Color)>();
        private Coroutine toastRoutine;

        private void Start()
        {
            gm = GameManager.Instance;
            if (gm != null)
            {
                gm.StatsChanged += Refresh;
                gm.Toast += ShowToast;
                gm.HeartLost += OnHeartLost;
                gm.HoleCompleted += OnHoleCompleted;
                gm.GameOver += OnGameOver;
                gm.PauseChanged += OnPauseChanged;
            }

            if (retryButton != null) retryButton.onClick.AddListener(() => gm?.Retry());
            if (gameOverMenuButton != null) gameOverMenuButton.onClick.AddListener(() => gm?.QuitToMenu());
            if (resumeButton != null) resumeButton.onClick.AddListener(() => gm?.SetPaused(false));
            if (pauseMenuButton != null) pauseMenuButton.onClick.AddListener(() => gm?.QuitToMenu());

            SetActive(holePanel, false);
            SetActive(gameOverPanel, false);
            SetActive(pausePanel, false);
            SetActive(powerBar, false);
            if (toastGroup != null) toastGroup.alpha = 0f;
            Refresh();
        }

        private void OnDestroy()
        {
            if (gm == null) return;
            gm.StatsChanged -= Refresh;
            gm.Toast -= ShowToast;
            gm.HeartLost -= OnHeartLost;
            gm.HoleCompleted -= OnHoleCompleted;
            gm.GameOver -= OnGameOver;
            gm.PauseChanged -= OnPauseChanged;
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null) go.SetActive(on);
        }

        private void Update()
        {
            var ball = BallManager.Instance;
            bool aiming = ball != null && ball.State == BallState.Aiming && ball.AimPower > ball.cancelPower;
            SetActive(powerBar, aiming);
            if (aiming && powerFill != null)
            {
                float p = ball.AimPower;
                powerFill.anchorMax = new Vector2(p, 1f);
                if (powerFillImage != null)
                    powerFillImage.color = Color.Lerp(new Color(0.3f, 1f, 0.45f), new Color(1f, 0.25f, 0.25f), p);
            }
        }

        private void Refresh()
        {
            if (gm == null) return;

            if (holeText != null) holeText.text = $"HOLE {gm.Hole}";
            if (holeSubText != null)
                holeSubText.text = $"Cleared {gm.HolesCompleted}   ·   Best {Mathf.Max(gm.BestHoles, gm.HolesCompleted)}";
            if (parValue != null) parValue.text = gm.Par.ToString();
            if (strokesValue != null)
            {
                strokesValue.text = gm.Strokes.ToString();
                strokesValue.color = gm.Strokes < gm.Par ? Color.white
                    : gm.Strokes == gm.Par ? UIFactory.Accent
                    : UIFactory.Red;
            }

            if (hearts != null)
            {
                for (int i = 0; i < hearts.Length; i++)
                    if (hearts[i] != null) hearts[i].color = i < gm.Hearts ? heartFull : heartEmpty;
            }

            var shown = gm.ActiveUpgrade != UpgradeType.None ? gm.ActiveUpgrade : gm.BankedUpgrade;
            SetActive(upgradePanel, shown != UpgradeType.None);
            if (shown != UpgradeType.None)
            {
                bool active = gm.ActiveUpgrade != UpgradeType.None;
                if (upgradeTitle != null)
                    upgradeTitle.text = (active ? "ACTIVE: " : "NEXT SHOT: ") + Upgrades.DisplayName(shown).ToUpperInvariant();
                if (upgradeDescription != null) upgradeDescription.text = Upgrades.Description(shown);
                if (upgradeAccent != null) upgradeAccent.color = Upgrades.Tint(shown);
            }
        }

        // ------------------------------------------------------------------
        // Toasts
        // ------------------------------------------------------------------

        private void ShowToast(string text, Color color)
        {
            toasts.Enqueue((text, color));
            if (toastRoutine == null) toastRoutine = StartCoroutine(ToastRoutine());
        }

        private IEnumerator ToastRoutine()
        {
            while (toasts.Count > 0)
            {
                var (text, color) = toasts.Dequeue();
                if (toastText != null)
                {
                    toastText.text = text;
                    toastText.color = color;
                }

                var rt = toastGroup != null ? (RectTransform)toastGroup.transform : null;
                for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.18f)
                {
                    if (toastGroup != null) toastGroup.alpha = t;
                    if (rt != null) rt.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, t);
                    yield return null;
                }
                if (toastGroup != null) toastGroup.alpha = 1f;
                if (rt != null) rt.localScale = Vector3.one;

                yield return new WaitForSecondsRealtime(toasts.Count > 0 ? 1.1f : 1.7f);

                for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.25f)
                {
                    if (toastGroup != null) toastGroup.alpha = t;
                    yield return null;
                }
                if (toastGroup != null) toastGroup.alpha = 0f;
            }
            toastRoutine = null;
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        private void OnHeartLost()
        {
            if (hearts == null || gm == null) return;
            int index = Mathf.Clamp(gm.Hearts, 0, hearts.Length - 1);
            if (hearts[index] != null) StartCoroutine(PunchHeart(hearts[index].rectTransform));
        }

        private IEnumerator PunchHeart(RectTransform heart)
        {
            var basePos = heart.anchoredPosition;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.5f)
            {
                float s = 1f + Mathf.Sin(t * Mathf.PI) * 0.6f;
                heart.localScale = Vector3.one * s;
                heart.anchoredPosition = basePos + new Vector2(Mathf.Sin(t * 50f) * 8f * (1f - t), 0f);
                yield return null;
            }
            heart.localScale = Vector3.one;
            heart.anchoredPosition = basePos;
        }

        private void OnHoleCompleted(int strokes, int par)
        {
            if (holeTitle != null)
            {
                holeTitle.text = GameManager.ScoreName(strokes, par);
                holeTitle.color = strokes <= par ? UIFactory.Accent : Color.white;
            }
            if (holeDetail != null)
                holeDetail.text = $"{strokes} stroke{(strokes == 1 ? "" : "s")}  ·  Par {par}\n<size=26>Next hole coming up...</size>";
            StartCoroutine(ShowPanelFor(holePanel, gm != null ? gm.holeCompleteDelay : 2.5f));
        }

        private IEnumerator ShowPanelFor(GameObject panel, float seconds)
        {
            if (panel == null) yield break;
            yield return PopIn(panel);
            yield return new WaitForSeconds(Mathf.Max(0f, seconds - 0.25f));
            panel.SetActive(false);
        }

        private IEnumerator PopIn(GameObject panel)
        {
            panel.SetActive(true);
            var rt = (RectTransform)panel.transform;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.25f)
            {
                // Overshoot ease-out.
                float k = 1f + 2.2f * Mathf.Pow(t - 1f, 3f) + 1.2f * Mathf.Pow(t - 1f, 2f);
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, k);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        private void OnGameOver(string reason, int holes, int best, bool newBest)
        {
            SetActive(holePanel, false);
            SetActive(pausePanel, false);
            if (gameOverReason != null) gameOverReason.text = reason;
            if (gameOverStats != null)
            {
                gameOverStats.text = $"Holes cleared: <b>{holes}</b>\nBest run: <b>{best}</b>" +
                                     (newBest ? "\n<color=#FFD23F>NEW BEST!</color>" : "");
            }
            if (gameOverPanel != null) StartCoroutine(PopIn(gameOverPanel));
        }

        private void OnPauseChanged(bool paused)
        {
            SetActive(pausePanel, paused);
        }
    }
}
