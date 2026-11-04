// This file and every function within was written by Claude Opus 5.5
using UnityEngine;
using UnityEngine.UI;

namespace FiniGolf
{
    /// <summary>Binds a UI Slider to the saved master volume and shows the value as a percentage.</summary>
    [RequireComponent(typeof(Slider))]
    public class VolumeSlider : MonoBehaviour
    {
        public Text valueLabel;

        private Slider slider;
        private float lastClickTime;

        private void Awake()
        {
            slider = GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.onValueChanged.AddListener(OnChanged);
        }

        private void OnEnable()
        {
            if (slider == null) slider = GetComponent<Slider>();
            slider.SetValueWithoutNotify(VolumeSettings.Master);
            UpdateLabel(slider.value);
        }

        private void OnDisable() => VolumeSettings.Save();

        private void OnChanged(float value)
        {
            VolumeSettings.Master = value;
            UpdateLabel(value);
            // Little tick so you can hear the new level while dragging.
            if (Time.unscaledTime - lastClickTime > 0.08f)
            {
                lastClickTime = Time.unscaledTime;
                Sfx.Instance?.Click();
            }
        }

        private void UpdateLabel(float value)
        {
            if (valueLabel != null) valueLabel.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }
    }
}
