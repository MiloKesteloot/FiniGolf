using System;
using UnityEngine;

namespace FiniGolf
{
    /// <summary>
    /// Tiny procedural sound bank, so the game has audio without any sound files.
    /// </summary>
    public class Sfx : Singleton<Sfx>
    {
        [Range(0f, 1f)] public float volume = 0.7f;

        private AudioSource source;
        private AudioClip hit, wall, bounce, sink, pickup, powerUp, heart, gameOver, fall, click;
        private const int SampleRate = 44100;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            var rnd = new System.Random(1234);
            float Noise() => (float)(rnd.NextDouble() * 2.0 - 1.0);

            hit = Make("hit", 0.12f, t => (Mathf.Sin(2f * Mathf.PI * 1400f * t) * 0.5f + Noise() * 0.5f) * Mathf.Exp(-t * 60f));
            wall = Make("wall", 0.1f, t => (Mathf.Sin(2f * Mathf.PI * 520f * t) * 0.6f + Noise() * 0.3f) * Mathf.Exp(-t * 55f));
            bounce = Make("bounce", 0.08f, t => Mathf.Sin(2f * Mathf.PI * 180f * t) * Mathf.Exp(-t * 50f));
            sink = Make("sink", 0.7f, t =>
            {
                float plop = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(600f, 180f, Mathf.Clamp01(t * 6f)) * t) * Mathf.Exp(-t * 18f);
                float chime = t > 0.12f ? Tone(t - 0.12f, 880f, 6f) * 0.35f + Tone(t - 0.12f, 1320f, 6f) * 0.25f : 0f;
                return plop * 0.8f + chime;
            });
            pickup = Make("pickup", 0.45f, t => Arp(t, new[] { 660f, 880f, 1100f, 1320f }, 0.07f) * 0.5f);
            powerUp = Make("powerup", 0.35f, t => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(300f, 900f, t / 0.35f) * t) * 0.35f * Mathf.Exp(-t * 4f));
            heart = Make("heart", 0.4f, t => (Tone(t, 220f, 7f) + Tone(t, 233f, 7f)) * 0.4f);
            gameOver = Make("gameover", 1.1f, t => Arp(t, new[] { 440f, 370f, 311f, 247f }, 0.22f) * 0.45f);
            fall = Make("fall", 0.8f, t => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(700f, 120f, t / 0.8f) * t) * 0.3f * (1f - t / 0.8f));
            click = Make("click", 0.05f, t => Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 90f) * 0.5f);
        }

        private static float Tone(float t, float freq, float decay) => Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * decay);

        private static float Arp(float t, float[] notes, float step)
        {
            int i = Mathf.Min(notes.Length - 1, (int)(t / step));
            float local = t - i * step;
            return Tone(local, notes[i], 9f) * (i == notes.Length - 1 ? 1f : Mathf.Clamp01((step - local) * 80f));
        }

        private static AudioClip Make(string clipName, float seconds, Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float fade = Mathf.Clamp01((n - i) / 200f); // avoid clicks at the end
                data[i] = Mathf.Clamp(wave(t) * fade, -1f, 1f);
            }
            var clip = AudioClip.Create(clipName, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void Play(AudioClip clip, float vol, float pitch = 1f)
        {
            if (clip == null || source == null) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, vol * volume);
        }

        public void Hit(float power) => Play(hit, 0.35f + power * 0.65f, 0.9f + power * 0.25f);
        public void Wall(float strength) => Play(wall, 0.2f + strength * 0.8f, UnityEngine.Random.Range(0.9f, 1.1f));
        public void Bounce(float strength) => Play(bounce, 0.2f + strength * 0.6f);
        public void Sink() => Play(sink, 1f);
        public void Pickup() => Play(pickup, 0.9f);
        public void PowerUp() => Play(powerUp, 0.8f);
        public void HeartLost() => Play(heart, 0.9f);
        public void GameOver() => Play(gameOver, 0.9f);
        public void Fall() => Play(fall, 0.8f);
        public void Click() => Play(click, 0.8f);
    }
}
