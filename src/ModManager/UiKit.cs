using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RRSOS.PCC.ModManager
{
    /// <summary>The few UI parts the screen is made of, built from nothing: text, a button, a text box, a scrolling list. Every position is from the top left of the parent.</summary>
    internal sealed class UiKit
    {
        public static readonly Color Face = new Color(0.04f, 0.06f, 0.08f, 0.8f);
        public static readonly Color FaceSelected = new Color(0.12f, 0.2f, 0.26f, 0.95f);
        public static readonly Color Lit = new Color(0.45f, 1f, 0.55f, 1f);
        public static readonly Color Dim = new Color(0.65f, 0.7f, 0.75f, 0.85f);
        public static readonly Color Off = new Color(0.45f, 0.48f, 0.5f, 0.55f);
        public static readonly Color Warn = new Color(1f, 0.55f, 0.45f, 1f);

        private readonly TMP_Text _font;

        public UiKit(TMP_Text fontFrom)
        {
            _font = fontFrom;
        }

        public static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        public RectTransform Panel(RectTransform parent, string name, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            go.GetComponent<Image>().color = colour;
            return rect;
        }

        public TMP_Text Label(RectTransform parent, string text, float x, float y, float w, float h, float size, Color colour, TextAlignmentOptions align = TextAlignmentOptions.Left, bool wrap = false)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, w, h);
            var label = go.GetComponent<TextMeshProUGUI>();
            Style(label, text, size, colour, align, wrap);
            return label;
        }

        public void Style(TMP_Text label, string text, float size, Color colour, TextAlignmentOptions align, bool wrap = false)
        {
            if (_font != null)
            {
                label.font = _font.font;
                label.fontSharedMaterial = _font.fontSharedMaterial;
            }

            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.alignment = align;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
        }

        public Button Button(RectTransform parent, string text, float x, float y, float w, float h, float size, Color colour, bool enabled, Action onClick, Color? face = null)
        {
            var go = new GameObject("Button " + text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, w, h);

            var image = go.GetComponent<Image>();
            image.color = face ?? Face;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.interactable = enabled;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);
            Style(textObject.GetComponent<TextMeshProUGUI>(), text, size, colour, TextAlignmentOptions.Center);
            return button;
        }

        /// <summary>A text box. <paramref name="onEnd"/> gets the text when the player leaves the box or presses Enter.</summary>
        public TMP_InputField Input(RectTransform parent, string text, float x, float y, float w, float h, float size, Action<string> onEnd)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, w, h);
            go.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.04f, 0.95f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.SetParent(rect, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(10f, 2f);
            viewportRect.offsetMax = new Vector2(-10f, -2f);

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(viewportRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var label = textObject.GetComponent<TextMeshProUGUI>();
            Style(label, text, size, Lit, TextAlignmentOptions.Left);
            label.overflowMode = TextOverflowModes.Overflow;

            var field = go.GetComponent<TMP_InputField>();
            field.textViewport = viewportRect;
            field.textComponent = label;
            field.fontAsset = label.font;
            field.pointSize = size;
            field.text = text;
            field.caretColor = Lit;
            field.selectionColor = new Color(0.3f, 0.5f, 0.9f, 0.5f);
            field.onEndEdit.AddListener(new UnityAction<string>(value => onEnd(value)));
            return field;
        }

        /// <summary>A list that scrolls: returns the content to add rows to (a vertical stack that grows with what is put in it).</summary>
        public RectTransform ScrollList(RectTransform parent, float x, float y, float w, float h, float spacing)
        {
            var area = Panel(parent, "ScrollList", new Color(0f, 0f, 0f, 0.28f));
            Place(area, x, y, w, h);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.SetParent(area, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            return contentRect;
        }

        /// <summary>A row of a scroll list: a rectangle of a set height whose parts are placed from its top left.</summary>
        public static RectTransform Row(RectTransform content, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(LayoutElement));
            var rect = (RectTransform)go.transform;
            rect.SetParent(content, false);
            var element = go.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return rect;
        }
    }
}
