using UnityEngine;
using UnityEngine.UI;

namespace FiniGolf
{
    /// <summary>Helpers for building uGUI hierarchies from code (used by the editor scene builder).</summary>
    public static class UIFactory
    {
        public static readonly Color Panel = new Color(0.06f, 0.09f, 0.18f, 0.78f);
        public static readonly Color PanelSolid = new Color(0.08f, 0.11f, 0.22f, 0.96f);
        public static readonly Color Accent = new Color(1f, 0.82f, 0.25f);
        public static readonly Color Green = new Color(0.16f, 0.78f, 0.52f);
        public static readonly Color Blue = new Color(0.25f, 0.55f, 0.95f);
        public static readonly Color Red = new Color(0.94f, 0.3f, 0.4f);
        public static readonly Color TextDim = new Color(1f, 1f, 1f, 0.72f);

        private static Font font;
        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Canvas Canvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Anchor + pivot at the same normalised point, then offset/size in reference pixels.</summary>
        public static RectTransform At(this RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Image(string name, Transform parent, Color color, Sprite sprite = null)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
                img.pixelsPerUnitMultiplier = 0.35f;
            }
            return img;
        }

        public static Text Text(string name, Transform parent, string content, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Bold, bool shadow = true)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            if (shadow)
            {
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.45f);
                s.effectDistance = new Vector2(2f, -3f);
            }
            return t;
        }

        /// <summary>Horizontal 0..1 slider: rounded track, coloured fill and a round knob.</summary>
        public static Slider Slider(string name, Transform parent, Vector2 size, Sprite track, Sprite knob, Color fillColor)
        {
            var root = Rect(name, parent);
            root.sizeDelta = size;
            var slider = root.gameObject.AddComponent<Slider>();

            var bg = Image("Background", root, new Color(0f, 0f, 0f, 0.45f), track);
            bg.rectTransform.anchorMin = new Vector2(0f, 0.3f);
            bg.rectTransform.anchorMax = new Vector2(1f, 0.7f);
            bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;

            var fillArea = Rect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0f, 0.3f);
            fillArea.anchorMax = new Vector2(1f, 0.7f);
            fillArea.offsetMin = new Vector2(4f, 4f);
            fillArea.offsetMax = new Vector2(-4f, -4f);
            var fill = Image("Fill", fillArea, fillColor, track);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

            var handleArea = Rect("Handle Slide Area", root);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(size.y * 0.5f, 0f);
            handleArea.offsetMax = new Vector2(-size.y * 0.5f, 0f);
            var handle = Image("Handle", handleArea, Color.white, knob);
            handle.rectTransform.sizeDelta = new Vector2(size.y, 0f);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);
            var hs = handle.gameObject.AddComponent<Shadow>();
            hs.effectColor = new Color(0f, 0f, 0f, 0.4f);
            hs.effectDistance = new Vector2(0f, -3f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = VolumeSettings.DefaultVolume;
            return slider;
        }

        /// <summary>"MASTER VOLUME ———o——— 80%" row, wired to the saved volume.</summary>
        public static RectTransform VolumeRow(Transform parent, Vector2 size, Sprite track, Sprite knob)
        {
            var row = Rect("Volume", parent);
            row.sizeDelta = size;

            var label = Text("Label", row, "MASTER VOLUME", 28, TextDim, TextAnchor.UpperLeft);
            label.rectTransform.anchorMin = new Vector2(0f, 1f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.sizeDelta = new Vector2(0f, 40f);
            label.rectTransform.anchoredPosition = Vector2.zero;

            var slider = Slider("Slider", row, new Vector2(size.x - 130f, 56f), track, knob, Green);
            slider.GetComponent<RectTransform>().At(new Vector2(0f, 0f), new Vector2(0f, 4f), new Vector2(size.x - 130f, 56f));

            var value = Text("Value", row, "80%", 36, Color.white, TextAnchor.MiddleRight);
            value.rectTransform.At(new Vector2(1f, 0f), new Vector2(0f, 4f), new Vector2(120f, 56f));

            var binding = slider.gameObject.AddComponent<VolumeSlider>();
            binding.valueLabel = value;
            return row;
        }

        public static Button Button(string name, Transform parent, string label, Color color, Sprite sprite, Vector2 size, int fontSize = 40)
        {
            var img = Image(name, parent, color, sprite);
            img.rectTransform.sizeDelta = size;
            var button = img.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var shadow = img.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -6f);

            var text = Text("Label", img.transform, label, fontSize, Color.white);
            text.rectTransform.Stretch();
            img.gameObject.AddComponent<ButtonJuice>();
            return button;
        }
    }
}
