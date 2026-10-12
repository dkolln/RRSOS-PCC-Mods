using System.Collections.Generic;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using TMPro;
using UnityEngine;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// One placed terminal. It is a sign underneath: the game's own text window is how you type, and the sign's text is the line being typed. This hides the sign's
    /// own lettering and draws a screen of its own in its place: the lines so far, a "C:\&gt;" prompt with what you are typing, and a blinking cursor. When what you typed has
    /// stopped changing and the text window is closed, it is run as a command and the line is cleared, ready for the next.
    /// </summary>
    internal sealed class TerminalScreen : MonoBehaviour
    {
        public const string Prompt = "C:\\>";

        private const int MaxLines = 14;   // an upper bound; how many really fit is measured
        private const float SettleSeconds = 0.8f;
        private const float BlinkSeconds = 0.5f;

        /// <summary>The screen whose command is running now, so replies from the chat can be printed on it.</summary>
        public static TerminalScreen Active;
        public static float ActiveUntil;

        private readonly List<string> _lines = new List<string>();
        private WorldObject _wo;
        private WorldObjectText _text;
        private TextMeshProUGUI _display;
        private string _lastTyped = "";
        private float _changedAt;
        private float _nextBlink;
        private bool _cursor = true;
        private string _shown;
        private string _renderedTyped;

        public void Init(WorldObject wo)
        {
            _wo = wo;
            _text = GetComponentInChildren<WorldObjectText>(true);
            if (_text == null || _text.textContainer == null)
            {
                ModLog.Note("Terminal #" + wo.GetId() + ": no text component found to draw on. Components: " + string.Join(", ", GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct().ToArray()));
                enabled = false;
                return;
            }

            var original = _text.textContainer;
            _display = Instantiate(original.gameObject, original.transform.parent, false).GetComponent<TextMeshProUGUI>();
            _display.gameObject.name = "TerminalText";
            _display.gameObject.SetActive(true);
            _display.color = new Color(0.45f, 1f, 0.45f);
            _display.alignment = TextAlignmentOptions.TopLeft;

            // The sign's panel is about 2.75 times wider than tall, and its one line of text covers a quarter of it. Measured in the prefab: the face is 200 x 75 text units
            // (the sign's own text box is 200 x 50), which fits nine lines at this size.
            _display.rectTransform.sizeDelta = new Vector2(200f, 75f);
            _display.margin = new Vector4(4f, 2f, 4f, 2f);
            _display.enableAutoSizing = false;
            _display.fontSize = 7f;
            _display.textWrappingMode = TextWrappingModes.Normal;
            _display.overflowMode = TextOverflowModes.Truncate;
            original.gameObject.SetActive(false);

            TerminalMod.Enlarge(transform);

            Print("RRSOS Terminal " + Plugin.Version);
            Print("Type help for the commands.");
            ModLog.Note("Terminal #" + wo.GetId() + " ready.");
        }

        public void Print(string message)
        {
            foreach (var line in (message ?? "").Replace("\r", "").Split('\n'))
                _lines.Add(line);
            if (_lines.Count > 200)
                _lines.RemoveRange(0, _lines.Count - 200);
            _shown = null;
        }

        public void Clear()
        {
            _lines.Clear();
            _shown = null;
        }

        private void Update()
        {
            if (_display == null || _wo == null)
                return;

            var now = Time.realtimeSinceStartup;
            var typed = _wo.GetText() ?? "";
            if (typed != _lastTyped)
            {
                _lastTyped = typed;
                _changedAt = now;
            }
            else if (typed.Trim().Length > 0 && now - _changedAt >= SettleSeconds && !Managers.GetManager<WindowsHandler>().GetHasUiOpen())
            {
                // Typed, settled and the text window is closed: that is Enter.
                _text.SetText("");
                _lastTyped = "";
                TerminalShell.Run(this, typed.Trim());
                typed = "";
            }

            if (now >= _nextBlink)
            {
                _cursor = !_cursor;
                _nextBlink = now + BlinkSeconds;
                _shown = null;
            }

            // Redraw only when the lines, the typed text or the cursor changed (a null _shown means the lines or the cursor did).
            if (_shown != null && typed == _renderedTyped)
                return;

            _renderedTyped = typed;
            _shown = Fit(typed + (_cursor ? "_" : " "));
            _display.text = _shown;
        }

        /// <summary>
        /// The most recent lines that fit the panel, with the prompt line always at the bottom. The font's real line height decides how many that is, so it is measured
        /// (wrapped lines count too) and the oldest lines are dropped until the whole text fits.
        /// </summary>
        private string Fit(string tail)
        {
            var prompt = Prompt + tail;
            var rect = _display.rectTransform.rect;
            var width = rect.width - _display.margin.x - _display.margin.z;
            var limit = rect.height - _display.margin.y - _display.margin.w;

            var take = System.Math.Min(_lines.Count, MaxLines);
            while (true)
            {
                var screen = take > 0 ? string.Join("\n", _lines.Skip(_lines.Count - take).ToArray()) + "\n" + prompt : prompt;
                if (take == 0 || _display.GetPreferredValues(screen, width, 0f).y <= limit)
                    return screen;
                take--;
            }
        }
    }
}
