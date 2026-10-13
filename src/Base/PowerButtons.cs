using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using RRSOS.PCC.Shared;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RRSOS.PCC.Base
{
    /// <summary>
    /// Two buttons, [ ON ] and [ OFF ], on the game's Energy window (the one that lists consumption and production, opened from the Energy screen). They switch the base's energy output on and
    /// off; the one that matches the current state is lit. The window is a Unity UI window of the game's own, so the buttons are ordinary UI objects added to it, with
    /// the window's own font.
    /// </summary>
    internal sealed class PowerButtons : MonoBehaviour
    {
        private const string RootName = "RRSOS_PowerButtons";
        private static readonly Color Lit = new Color(0.45f, 1f, 0.55f, 1f);
        private static readonly Color LitOff = new Color(1f, 0.45f, 0.4f, 1f);
        private static readonly Color Dim = new Color(0.65f, 0.7f, 0.75f, 0.75f);
        private static readonly Color Face = new Color(0.04f, 0.06f, 0.08f, 0.72f);

        private TextMeshProUGUI _on, _off;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(1f);

                if (!Plugin.Enabled.Value || !Plugin.PowerButtons.Value)
                    continue;

                try
                {
                    var window = Resources.FindObjectsOfTypeAll<UiWindowEnergy>().FirstOrDefault(w => w != null && w.gameObject.scene.IsValid());
                    if (window == null)
                        continue;

                    if (window.transform.Find(RootName) == null)
                        Build(window);

                    Show(BasePower.IsOffNow);
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("power-buttons", "Power buttons: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // ---- the buttons ------------------------------------------------------------------------------------------------------

        private void Build(UiWindowEnergy window)
        {
            var parent = (RectTransform)window.transform;
            var root = new GameObject(RootName, typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(Plugin.PowerButtonsX.Value, Plugin.PowerButtonsY.Value);
            rect.sizeDelta = new Vector2(420f, 56f);
            root.AddComponent<LayoutElement>().ignoreLayout = true; // in case the window lays its children out itself

            _on = Button(rect, "[ ON ]", new Vector2(-105f, 0f), window.generationValue, () => { BasePower.OnCommand("on", Quiet); StartCoroutine(RedrawLater(window)); });
            _off = Button(rect, "[ OFF ]", new Vector2(105f, 0f), window.generationValue, () => { BasePower.OnCommand("off", Quiet); StartCoroutine(RedrawLater(window)); });
            ModLog.Note("Power buttons added to the Energy window.");
        }

        /// <summary>
        /// The window writes its numbers when it opens and not again, so after a switch it still shows the old ones until it is closed and opened. A few seconds on, once the game
        /// has counted the energy again, this does what opening it does (its own OnOpen), if it is still open.
        /// </summary>
        private static IEnumerator RedrawLater(UiWindowEnergy window)
        {
            yield return new WaitForSecondsRealtime(5f);

            try
            {
                if (window != null && window.IsOpen)
                    window.OnOpen();
            }
            catch (Exception e)
            {
                ModLog.LogOnce("power-buttons-redraw", "Could not redraw the Energy window: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static TextMeshProUGUI Button(RectTransform parent, string label, Vector2 at, TMP_Text fontFrom, Action onClick)
        {
            var go = new GameObject("Button" + label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UnityEngine.UI.Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(190f, 52f);
            rect.anchoredPosition = at;

            var face = go.GetComponent<Image>();
            face.color = Face;
            var button = go.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(() => onClick());

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            if (fontFrom != null)
            {
                text.font = fontFrom.font;
                text.fontSharedMaterial = fontFrom.fontSharedMaterial;
            }

            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 30f;
            text.raycastTarget = false; // the button's face takes the click
            text.color = Dim;
            return text;
        }

        private void Show(bool off)
        {
            if (_on != null)
                _on.color = off ? Dim : Lit;
            if (_off != null)
                _off.color = off ? LitOff : Dim;
        }

        // A click is not reported anywhere (not on the terminal, not in the chat); the lights on the screen and the Base log are the answer.
        private static void Quiet(string message)
        {
        }
    }
}
