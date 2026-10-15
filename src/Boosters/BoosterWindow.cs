using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RRSOS.PCC.Boosters
{
    /// <summary>
    /// The Boosters screen. It is the game's Energy window: while it was opened by a placed Boosters screen (<see cref="Mode"/>), its own lists are hidden (its frame, title and close
    /// button stay) and the booster controls are drawn in their place, from scratch: the beacon, the kind of booster, the machine's tier, the optimizer, its fuses and what the machines
    /// are filled with, and a button that builds it. Opened by the game's own Energy screen the window is left exactly as it is.
    /// </summary>
    internal sealed class BoosterWindow : MonoBehaviour
    {
        /// <summary>True while the window was opened by a Boosters screen.</summary>
        internal static bool Mode;

        private const string RootName = "RRSOS_Boosters";
        private static readonly Color Face = new Color(0.04f, 0.06f, 0.08f, 0.72f);
        private static readonly Color Lit = new Color(0.45f, 1f, 0.55f, 1f);
        private static readonly Color Dim = new Color(0.65f, 0.7f, 0.75f, 0.75f);
        private static readonly Color Off = new Color(0.45f, 0.48f, 0.5f, 0.45f);
        private static readonly Color Warn = new Color(1f, 0.55f, 0.45f, 1f);
        private static readonly string[] Keep = { "LinesBorders", "CloseUiButton", "Energy", RootName };

        private UiWindowEnergy _window;
        private RectTransform _root;
        private TMP_Text _font;
        private bool _shown;
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private TMP_Text _title;
        private string _titleText;
        private Behaviour _titleLocalized;

        // the choices
        private List<WorldObject> _beacons = new List<WorldObject>();
        private WorldObject _beacon;
        private Kind _kind;
        private string _machine;
        private string _optimizer;      // null = none
        private bool _prefill = true;  // the optimizers hold their fuses
        private string _fill = "";
        private string _status = "";
        private bool _building;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.3f);

                if (!Plugin.Enabled.Value)
                    continue;

                try
                {
                    _window = Resources.FindObjectsOfTypeAll<UiWindowEnergy>().FirstOrDefault(w => w != null && w.gameObject.scene.IsValid());
                    if (_window == null)
                        continue;

                    var want = Mode && _window.IsOpen;
                    if (want && !_shown)
                        Show();
                    else if (!want && _shown)
                        Hide();
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("boosters-window", "Boosters window: " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace);
                }
            }
        }

        // ---- showing and hiding ---------------------------------------------------------------------------------------------

        private void Show()
        {
            var container = (RectTransform)_window.transform.Find("Container");
            var content = (RectTransform)container.Find("Content");
            _font = _window.generationValue;

            _hidden.Clear();
            for (var i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (Keep.Contains(child.name) || !child.gameObject.activeSelf)
                    continue;
                _hidden.Add(child.gameObject);
                child.gameObject.SetActive(false);
            }

            var titleTransform = container.Find("Energy");
            _title = titleTransform == null ? null : titleTransform.GetComponent<TMP_Text>();
            if (_title != null)
            {
                _titleText = _title.text;
                _titleLocalized = titleTransform.GetComponent("LocalizedText") as Behaviour;
                if (_titleLocalized != null)
                    _titleLocalized.enabled = false;
                _title.text = "Boosters";
            }

            if (_root == null)
            {
                var go = new GameObject(RootName, typeof(RectTransform));
                _root = (RectTransform)go.transform;
                _root.SetParent(container, false);
                _root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }

            _root.anchorMin = content.anchorMin;
            _root.anchorMax = content.anchorMax;
            _root.anchoredPosition = content.anchoredPosition;
            _root.sizeDelta = content.sizeDelta;
            _root.gameObject.SetActive(true);

            _status = "";
            RefreshBeacons();
            Redraw();
            _shown = true;
        }

        private void Hide()
        {
            _shown = false;
            if (_root != null)
                _root.gameObject.SetActive(false);

            foreach (var go in _hidden)
                if (go != null)
                    go.SetActive(true);
            _hidden.Clear();

            if (_title != null)
            {
                _title.text = _titleText;
                if (_titleLocalized != null)
                    _titleLocalized.enabled = true;
            }
        }

        // ---- the choices ----------------------------------------------------------------------------------------------------

        private void RefreshBeacons()
        {
            _beacons = new List<WorldObject>();
            if (WorldState.Ready(out var planetHash, out _))
            {
                _beacons = WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                    .Where(w => w != null && w.GetGroup() != null && w.GetGroup().GetId() == "Beacon" && w.GetPlanetHash() == planetHash && !IsExcluded(w))
                    .OrderBy(w => w.GetText() ?? "", StringComparer.OrdinalIgnoreCase).ToList();
            }

            if (_beacon != null && !_beacons.Contains(_beacon))
                _beacon = null;
        }

        // Beacons named in ExcludeBeacons ("Base" by default: the T0 base's lamp becomes it) mark something else, never a booster.
        private static bool IsExcluded(WorldObject beacon)
        {
            var name = (beacon.GetText() ?? "").Trim();
            return Plugin.ExcludeBeacons.Value.Split(',').Select(n => n.Trim()).Any(n => n.Length > 0 && string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Unlocked(string groupId)
        {
            var group = GroupsHandler.GetGroupViaId(groupId);
            if (group == null)
                return false;

            try
            {
                return group.GetUnlockingInfos().GetIsUnlocked(true) || group.GetIsGloballyUnlocked();
            }
            catch
            {
                return false;
            }
        }

        private static bool Allowed(string groupId) => GroupsHandler.GetGroupViaId(groupId) != null && (Plugin.AllowLocked.Value || Unlocked(groupId));

        private void PickBeacon(WorldObject beacon)
        {
            _beacon = beacon;
            var guess = BoosterKinds.For(beacon.GetText());
            if (guess != null)
                PickKind(guess);
        }

        // A kind's own starting choices: its highest machine tier that may be built, no optimizer, its fill. The fuses and how many optimizers are the kind's own.
        private void PickKind(Kind kind)
        {
            _kind = kind;
            _machine = kind.Machines.Select(m => m.GId).LastOrDefault(Allowed);
            _optimizer = null;   // optimizers are built only when one is picked
            _fill = BoosterKinds.DefaultFill(kind);
        }

        private void StartBuild()
        {
            if (_building || _beacon == null || _kind == null || _machine == null)
                return;

            var request = new Request
            {
                Beacon = _beacon,
                Kind = _kind,
                Machine = _machine,
                Optimizer = _optimizer,
                Fuses = _kind.DefaultFuses.ToList(),
                Prefill = _prefill,
                Fill = _fill ?? ""
            };

            StartCoroutine(Run(request));
        }

        private IEnumerator Run(Request request)
        {
            _building = true;
            var outcome = new BoosterBuilder.Outcome();
            _status = "Building...";
            Redraw();

            yield return BoosterBuilder.Build(request, outcome, text =>
            {
                _status = text;
                SetStatusText();
            });

            _status = outcome.Ok
                ? "Built: " + (outcome.Foundations > 0 ? outcome.Foundations + " foundations, " : "") + outcome.Machines + " machines" + (outcome.Found > 0 ? " (" + outcome.Found + " were already there)" : "") + ", " + outcome.Optimizers + " optimizers. The beacon is gone."
                : "Problem: " + string.Join(" ", outcome.Problems.ToArray());
            _building = false;
            RefreshBeacons();
            Redraw();
        }

        // ---- drawing ----------------------------------------------------------------------------------------------------

        private RectTransform _body;
        private TMP_Text _statusText;

        private sealed class Option
        {
            public string Key, Label;
            public bool Enabled = true, Selected;
        }

        private void Redraw()
        {
            if (_root == null)
                return;

            if (_body != null)
                Destroy(_body.gameObject);

            var go = new GameObject("Body", typeof(RectTransform));
            _body = (RectTransform)go.transform;
            _body.SetParent(_root, false);
            _body.anchorMin = Vector2.zero;
            _body.anchorMax = Vector2.one;
            _body.offsetMin = _body.offsetMax = Vector2.zero;

            Canvas.ForceUpdateCanvases();
            float w = _root.rect.width, h = _root.rect.height;
            if (w < 300f || h < 300f)
            {
                w = 1350f;
                h = 720f;
            }

            const float m = 28f;
            var colW = (w - 3 * m) / 2f;
            float left = m, right = 2 * m + colW;
            var yLeft = 14f;
            var yRight = 14f;

            // left: where, and what
            if (_beacons.Count == 0)
                yLeft = Section("Beacons", left, yLeft, colW, 1, new List<Option>(), null, "No beacon found. Place a beacon, name it, and open this screen again.");
            else
                yLeft = Section("Beacons", left, yLeft, colW, 2, _beacons.Select(b => new Option { Key = b.GetId().ToString(), Label = string.IsNullOrWhiteSpace(b.GetText()) ? "(no name)" : b.GetText(), Selected = b == _beacon }).ToList(),
                    key => PickBeacon(_beacons.First(b => b.GetId().ToString() == key)));

            yLeft = Section("Booster Type", left, yLeft, colW, 3, BoosterKinds.All.Select(k => new Option { Key = k.Key, Label = k.Name, Selected = k == _kind }).ToList(), key => PickKind(BoosterKinds.ByKey(key)));

            if (_kind != null && _kind.Machines.Length > 1)
                yLeft = Section("Machine Tier", left, yLeft, colW, 5, _kind.Machines.Select((c, i) => new Option { Key = c.GId, Label = "T" + (i + 1), Enabled = Allowed(c.GId), Selected = c.GId == _machine }).ToList(), key => _machine = key);

            // right: the optimizer, its fuses, what fills the machines
            if (_kind == null || _kind.DefaultFuses.Length > 0)
            {
                var optimizers = new List<Option> { new Option { Key = "", Label = "None", Selected = _optimizer == null } };
                optimizers.AddRange(BoosterKinds.Optimizers.Select(c => new Option { Key = c.GId, Label = c.Label, Enabled = Allowed(c.GId), Selected = c.GId == _optimizer }));
                yRight = Section("Optimizers", right, yRight, colW, 3, optimizers, key => _optimizer = key.Length == 0 ? null : key);

                yRight = CheckBox("Pre-Fill Optimizers", right, yRight, _prefill, _optimizer != null && _kind != null, () => _prefill = !_prefill);
            }

            if (_kind != null && _kind.Fill != null)
            {
                var fill = new List<Option> { new Option { Key = "", Label = "No Fill", Selected = string.IsNullOrEmpty(_fill) } };
                fill.AddRange(_kind.Fill.Where(c => _kind.FillAnywhere || BoosterKinds.AvailableHere(c.GId)).Select(c => new Option { Key = c.GId, Label = c.Label, Selected = c.GId == _fill }));
                yRight = Section(_kind.FillTitle, right, yRight, colW, 4, fill, key => _fill = key);
            }

            // the bottom: what will be built, and the button
            var bottom = h - 118f;
            Label(_body, Summary(), left, bottom, w - 2 * m, 36f, 24f, Dim, TextAlignmentOptions.Left);

            var ready = !_building && _beacon != null && _kind != null && _machine != null;
            Button(_body, "[ BUILD ]", left, bottom + 44f, 260f, 56f, 32f, ready ? Lit : Off, ready, StartBuild);
            _statusText = Label(_body, _status, left + 290f, bottom + 50f, w - 2 * m - 290f, 48f, 22f, _status.StartsWith("Problem") ? Warn : Dim, TextAlignmentOptions.Left);
        }

        private void SetStatusText()
        {
            if (_statusText != null)
                _statusText.text = _status;
        }

        private string Summary()
        {
            if (_beacon == null)
                return "Pick a beacon.";
            if (_kind == null)
                return "Pick a booster for this beacon.";
            if (_machine == null)
                return "Nothing of this booster is unlocked yet.";

            var machine = _kind.Machines.First(c => c.GId == _machine).Label;
            var where = "at \"" + (_beacon.GetText() ?? "") + "\"";
            var layout = _kind.Sides ? "on a foundation pad by " : _kind.Ring ? "in a ring " : "stacked ";
            var text = _kind.Number + " x " + machine + " " + layout + where;
            if (_optimizer != null)
            {
                var fuses = BoosterKinds.Fuses.Where(f => _kind.DefaultFuses.Contains(f.GId)).Select(f => f.Label).ToArray();
                text += "; " + fuses.Length + " " + BoosterKinds.Optimizers.First(o => o.GId == _optimizer).Label + (fuses.Length > 1 ? "s" : "") + (_prefill ? " with " + string.Join(" and ", fuses) + " fuses" : " (empty)");
            }

            if (!string.IsNullOrEmpty(_fill) && _kind.Fill != null)
                text += "; filled with " + _kind.Fill.First(c => c.GId == _fill).Label;
            return text;
        }

        // A titled block of buttons in a column, in rows of `cols`; returns the y below it. `empty` is shown when there are no options.
        private float Section(string title, float x, float y, float width, int cols, List<Option> options, Action<string> pick, string empty = null)
        {
            Label(_body, title, x, y, width, 40f, 24f, Lit, TextAlignmentOptions.Left);   // tall enough for the font's line, or the ellipsis hides it
            y += 44f;

            if (options.Count == 0)
            {
                Label(_body, empty ?? "", x, y, width, 40f, 22f, Warn, TextAlignmentOptions.Left);
                return y + 52f;
            }

            const float gap = 6f, height = 40f;
            var bw = (width - gap * (cols - 1)) / cols;
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var cx = x + (i % cols) * (bw + gap);
                var cy = y + (i / cols) * (height + gap);
                var colour = !option.Enabled ? Off : option.Selected ? Lit : Dim;
                var key = option.Key;
                Button(_body, option.Label, cx, cy, bw, height, 20f, colour, option.Enabled && !_building, () =>
                {
                    pick(key);
                    Redraw();
                });
            }

            return y + ((options.Count + cols - 1) / cols) * (height + gap) + 12f;
        }

        // A tick box with its label beside it; returns the y below it.
        private float CheckBox(string text, float x, float y, bool ticked, bool enabled, Action toggle)
        {
            Button(_body, ticked ? "X" : "", x, y, 44f, 44f, 26f, enabled ? Lit : Off, enabled && !_building, () =>
            {
                toggle();
                Redraw();
            });
            Label(_body, text, x + 56f, y + 4f, 520f, 36f, 22f, enabled ? Dim : Off, TextAlignmentOptions.Left);
            return y + 60f;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private TMP_Text Label(RectTransform parent, string text, float x, float y, float w, float h, float size, Color colour, TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, w, h);
            var label = go.GetComponent<TextMeshProUGUI>();
            Style(label, text, size, colour, align);
            return label;
        }

        private void Style(TMP_Text label, string text, float size, Color colour, TextAlignmentOptions align)
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
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
        }

        private void Button(RectTransform parent, string text, float x, float y, float w, float h, float size, Color colour, bool enabled, Action onClick)
        {
            var go = new GameObject("Button " + text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UnityEngine.UI.Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, w, h);

            var face = go.GetComponent<Image>();
            face.color = Face;
            var button = go.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = face;
            button.interactable = enabled;
            button.onClick.AddListener(() => onClick());

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = new Vector2(-6f, 0f);
            Style(textObject.GetComponent<TextMeshProUGUI>(), text, size, colour, TextAlignmentOptions.Center);
        }
    }
}
