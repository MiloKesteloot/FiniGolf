// This file and every function within was written by Claude Opus 5.5
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FiniGolf
{
    /// <summary>
    /// Singleton that runs a round of infinite minigolf: holes, par, strokes, hearts, upgrades and game over.
    /// Hearts: every shot taken past par costs one heart. Falling off the course ends the run.
    /// </summary>
    public class GameManager : Singleton<GameManager>
    {
        public const string MenuScene = "MainMenu";
        public const string GameScene = "Game";
        public const string BestHolesKey = "FiniGolf.BestHoles";

        [Header("Rules")]
        public int startingHearts = 3;
        [Tooltip("Chance of a powerup on a hole while at full health.")]
        [Range(0f, 1f)] public float upgradeChance = 0.4f;
        [Tooltip("Chance multiplier at 2 hearts. At 1 heart (or less) every hole has a powerup.")]
        public float twoHeartsUpgradeMultiplier = 2f;
        [Tooltip("Guarantee an upgrade if none has appeared for this many holes.")]
        public int upgradePityHoles = 2;
        public float holeCompleteDelay = 2.6f;

        public int Hole { get; private set; } = 1;
        public int Hearts { get; private set; }
        public int Strokes { get; private set; }
        public int Par { get; private set; }
        public int HolesCompleted { get; private set; }
        public int TotalStrokes { get; private set; }
        public UpgradeType BankedUpgrade { get; private set; }
        public UpgradeType ActiveUpgrade { get; private set; }
        public bool IsGameOver { get; private set; }
        public bool IsPaused { get; private set; }
        public bool InTransition { get; private set; } = true;
        public bool CanShoot => !IsGameOver && !IsPaused && !InTransition;
        public int BestHoles => PlayerPrefs.GetInt(BestHolesKey, 0);

        /// <summary>Powerups get more common as you lose hearts: always at 1 heart, ~2x at 2 hearts.</summary>
        public float CurrentUpgradeChance
        {
            get
            {
                if (Hearts <= 1) return 1f;
                if (Hearts == 2) return Mathf.Clamp01(upgradeChance * twoHeartsUpgradeMultiplier);
                return upgradeChance;
            }
        }

        public event Action StatsChanged;
        public event Action<string, Color> Toast;
        public event Action HeartLost;
        public event Action<int, int> HoleCompleted;           // strokes, par
        public event Action<string, int, int, bool> GameOver;  // reason, holes, best, newBest
        public event Action<bool> PauseChanged;

        private int runSeed;
        private System.Random rng;
        private int holesSinceUpgrade;

        private void Start()
        {
            Time.timeScale = 1f;
            runSeed = Environment.TickCount;
            rng = new System.Random(runSeed);
            Hearts = startingHearts;
            StartCoroutine(BeginHoleRoutine(true));
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || IsGameOver) return;

            bool ballUsedEscape = BallManager.Instance != null &&
                                  (BallManager.Instance.State == BallState.Aiming ||
                                   BallManager.Instance.LastAimCancelFrame == Time.frameCount);
            if (kb.escapeKey.wasPressedThisFrame && !ballUsedEscape)
                SetPaused(!IsPaused);
        }

        // ------------------------------------------------------------------
        // Hole flow
        // ------------------------------------------------------------------

        private IEnumerator BeginHoleRoutine(bool first)
        {
            InTransition = true;
            var fader = ScreenFader.Instance;
            if (!first && fader != null) yield return fader.Fade(1f, 0.35f);

            Strokes = 0;

            UpgradeType pickup = UpgradeType.None;
            if (rng.NextDouble() < CurrentUpgradeChance || holesSinceUpgrade >= upgradePityHoles)
                pickup = Upgrades.Pickable[rng.Next(Upgrades.Pickable.Length)];

            var course = CourseManager.Instance;
            course.Generate(Hole, runSeed + Hole * 7919, pickup);
            Par = course.Par;

            // Only count it if it really made it onto the course.
            pickup = course.PlacedPickup;
            if (pickup != UpgradeType.None) holesSinceUpgrade = 0;
            else holesSinceUpgrade++;

            var ball = BallManager.Instance;
            ball.SetUpgrade(UpgradeType.None);
            ball.PlaceAt(course.StartPosition);
            ActivateBankedUpgrade(false);
            ShowBestPath();

            var camRig = CameraController.Instance;
            if (camRig != null)
            {
                camRig.SnapBehind(ball.BallTransform, course.StartForward);
                camRig.ShowOverview(course.Bounds, course.HolePosition);
            }
            StatsChanged?.Invoke();

            if (fader != null) yield return fader.Fade(0f, first ? 0.6f : 0.35f);
            if (camRig != null) yield return camRig.IntroFlyover(course.Bounds, course.HolePosition);

            Toast?.Invoke($"Hole {Hole}  ·  Par {Par}", Color.white);
            if (pickup != UpgradeType.None)
                Toast?.Invoke($"Grab the {Upgrades.DisplayName(pickup)} on the course!", Upgrades.Tint(pickup));

            // Wait a frame so the click that skipped the intro doesn't start an aim.
            yield return null;
            InTransition = false;
        }

        /// <summary>Called by the BallManager the moment a shot is struck.</summary>
        public void RegisterShot()
        {
            if (ActiveUpgrade == UpgradeType.Mulligan)
            {
                Toast?.Invoke("Mulligan! This one's free.", Upgrades.Tint(UpgradeType.Mulligan));
                StatsChanged?.Invoke();
                return;
            }

            Strokes++;
            TotalStrokes++;
            if (Strokes > Par && Hearts > 0)
            {
                Hearts--;
                HeartLost?.Invoke();
                Sfx.Instance?.HeartLost();
                Toast?.Invoke(Hearts > 0 ? "Over par!  -1 heart" : "Last heart gone! Sink it now!", new Color(1f, 0.35f, 0.4f));
            }
            StatsChanged?.Invoke();
        }

        /// <summary>Called by the BallManager when the ball comes to rest (not in the cup).</summary>
        public void NotifyBallStopped()
        {
            if (IsGameOver) return;

            // Upgrades only last for one shot.
            if (ActiveUpgrade != UpgradeType.None)
            {
                ActiveUpgrade = UpgradeType.None;
                BallManager.Instance.SetUpgrade(UpgradeType.None);
            }

            // The next shot would cost a heart you don't have.
            if (Hearts <= 0 && Strokes >= Par)
            {
                TriggerGameOver("Out of hearts!");
                return;
            }

            ActivateBankedUpgrade(true);
            ShowBestPath();
            StatsChanged?.Invoke();
        }

        /// <summary>Runs A* from the ball to the cup and draws the guide line.</summary>
        private void ShowBestPath()
        {
            var ball = BallManager.Instance;
            var course = CourseManager.Instance;
            if (ball == null || course == null || ball.Body == null) return;
            var guide = PathGuide.Ensure(course.assets);
            guide.ShowFrom(ball.Body.position);
        }

        private static void HideBestPath()
        {
            if (PathGuide.Instance != null) PathGuide.Instance.Hide();
        }

        public void BankUpgrade(UpgradeType type)
        {
            BankedUpgrade = type;
            Toast?.Invoke($"{Upgrades.DisplayName(type)}! Ready for your next shot.", Upgrades.Tint(type));
            StatsChanged?.Invoke();
        }

        private void ActivateBankedUpgrade(bool announce)
        {
            if (BankedUpgrade == UpgradeType.None) return;
            ActiveUpgrade = BankedUpgrade;
            BankedUpgrade = UpgradeType.None;
            BallManager.Instance.SetUpgrade(ActiveUpgrade);
            if (announce) Sfx.Instance?.PowerUp();
            StatsChanged?.Invoke();
        }

        public void NotifyHoleSunk()
        {
            if (IsGameOver) return;
            InTransition = true;
            HolesCompleted++;
            ActiveUpgrade = UpgradeType.None;
            HideBestPath();
            StatsChanged?.Invoke();
            HoleCompleted?.Invoke(Strokes, Par);
            StartCoroutine(NextHoleRoutine());
        }

        private IEnumerator NextHoleRoutine()
        {
            yield return new WaitForSeconds(holeCompleteDelay);
            Hole++;
            yield return BeginHoleRoutine(false);
        }

        public void NotifyBallFell()
        {
            if (IsGameOver) return;
            InTransition = true;
            HideBestPath();
            if (CameraController.Instance != null) CameraController.Instance.FollowTarget = false;
            Toast?.Invoke("Splat! Off the course...", new Color(1f, 0.4f, 0.4f));
            StartCoroutine(DelayedGameOver("You fell off the course!", 1.4f));
        }

        private IEnumerator DelayedGameOver(string reason, float delay)
        {
            yield return new WaitForSeconds(delay);
            TriggerGameOver(reason);
        }

        private void TriggerGameOver(string reason)
        {
            if (IsGameOver) return;
            IsGameOver = true;
            InTransition = true;

            int best = BestHoles;
            bool newBest = HolesCompleted > best;
            if (newBest)
            {
                best = HolesCompleted;
                PlayerPrefs.SetInt(BestHolesKey, best);
                PlayerPrefs.Save();
            }

            Sfx.Instance?.GameOver();
            StatsChanged?.Invoke();
            GameOver?.Invoke(reason, HolesCompleted, best, newBest);
        }

        // ------------------------------------------------------------------
        // Menu actions
        // ------------------------------------------------------------------

        public void SetPaused(bool paused)
        {
            if (IsGameOver) return;
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            PauseChanged?.Invoke(paused);
        }

        public void Retry() => StartCoroutine(LoadSceneRoutine(GameScene));

        public void QuitToMenu() => StartCoroutine(LoadSceneRoutine(MenuScene));

        private IEnumerator LoadSceneRoutine(string scene)
        {
            Time.timeScale = 1f;
            IsPaused = false;
            if (ScreenFader.Instance != null) yield return ScreenFader.Instance.Fade(1f, 0.3f);
            SceneManager.LoadScene(scene);
        }

        public static string ScoreName(int strokes, int par)
        {
            if (strokes == 1) return "HOLE IN ONE!";
            int diff = strokes - par;
            switch (diff)
            {
                case <= -3: return "Albatross!";
                case -2: return "Eagle!";
                case -1: return "Birdie!";
                case 0: return "Par";
                case 1: return "Bogey";
                case 2: return "Double Bogey";
                default: return $"+{diff}";
            }
        }
    }
}
