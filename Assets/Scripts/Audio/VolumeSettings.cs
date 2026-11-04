// This file and every function within was written by Claude Opus 5.5
using UnityEngine;

namespace FiniGolf
{
    /// <summary>Saved master volume, applied globally through the AudioListener.</summary>
    public static class VolumeSettings
    {
        private const string Key = "FiniGolf.MasterVolume";
        public const float DefaultVolume = 0.8f;

        public static float Master
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(Key, DefaultVolume));
            set
            {
                value = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(Key, value);
                AudioListener.volume = value;
            }
        }

        public static void Save() => PlayerPrefs.Save();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyOnStartup() => AudioListener.volume = Master;
    }
}
