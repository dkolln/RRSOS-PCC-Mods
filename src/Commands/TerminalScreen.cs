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

        private const int MaxLines = 9;
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
            _display.fontSize = Mathf.Max(4f, original.fontSize * 0.5f);
            _display.textWrappingMode = TextWrappingModes.Normal;
            _display.overflowMode = TextOverflowModes.Truncate;
            original.gameObject.SetActive(false);

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

            var visible = _lines.Skip(System.Math.Max(0, _lines.Count - (MaxLines - 1)));
            var screen = string.Join("\n", visible.ToArray()) + "\n" + Prompt + typed + (_cursor ? "_" : " ");
            if (screen != _shown)
            {
                _display.text = screen;
                _shown = screen;
            }
        }
    }
}
