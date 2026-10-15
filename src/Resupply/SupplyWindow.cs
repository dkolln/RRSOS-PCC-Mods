using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// The Supply Manager screen. It is the game's Energy window: while it was opened by a placed Supply Manager screen (<see cref="Mode"/>), its own lists are hidden (its frame, title and
    /// close button stay) and four controls are drawn in their place: Set Drone Demand (crates ask the drones for their item), Set Drone Supply (producers offer what they make),
    /// Resupply (fill the crates), and Auto Top-Up (on or off). Opened by the game's own Energy screen the window is left exactly as it is. The ON/OFF buttons that the Base mod puts on the
    /// Energy window are found by their name and hidden while this screen is shown.
    /// </summary>
    internal sealed class SupplyWindow : MonoBehaviour
    {
        /// <summary>True while the window was opened by a Supply Manager screen.</summary>
        internal static bool Mode;

        private const string RootName = "RRSOS_SupplyManager";
        private const string PowerButtonsName = "RRSOS_PowerButtons";   // the Base mod's [ ON ] [ OFF ]; found by name, so the mods do not depend on each other
        private static readonly Color Face = new Color(0.04f, 0.06f, 0.08f, 0.72f);
        private static readonly Color Lit = new Color(0.45f, 1f, 0.55f, 1f);
        private static readonly Color Dim = new Color(0.65f, 0.7f, 0.75f, 0.75f);
        private static readonly Color Off = new Color(0.45f, 0.48f, 0.5f, 0.45f);
        private static readonly Color Warn = new Color(1f, 0.55f, 0.45f, 1f);
        private static readonly string[] Keep = { "LinesBorders", "CloseUiButton", "Energy", RootName };

        private UiWindowEnergy _window;
        private RectTransform _root, _body;
        private TMP_Text _font;
        private bool _shown;
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private TMP_Text _title;
        private string _titleText;
        private Behaviour _titleLocalized;
        private Transform _powerButtons;
        private bool _powerButtonsWereOn;
        private TMP_Text _statusText;
        private string _status = "";
        private bool _busy;

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
                    ModLog.LogOnce("supply-window", "Supply Manager window: " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace);
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

            _powerButtons = _window.transform.Find(PowerButtonsName);
            _powerButtonsWereOn = _powerButtons != null && _powerButtons.gameObject.activeSelf;
            if (_powerButtons != null)
                _powerButtons.gameObject.SetActive(false);

            var titleTransform = container.Find("Energy");
            _title = titleTransform == null ? null : titleTransform.GetComponent<TMP_Text>();
            if (_title != null)
            {
                _titleText = _title.text;
                _titleLocalized = titleTransform.GetComponent("LocalizedText") as Behaviour;
                if (_titleLocalized != null)
                    _titleLocalized.enabled = false;
                _title.text = "Supply Manager";
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

            if (_powerButtons != null && _powerButtonsWereOn)
                _powerButtons.gameObject.SetActive(true);
            _powerButtons = null;

            if (_title != null)
            {
                _title.text = _titleText;
                if (_titleLocalized != null)
                    _titleLocalized.enabled = true;
            }
        }

        // ---- the controls ---------------------------------------------------------------------------------------------------

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

            const float m = 40f, buttonW = 420f, rowH = 100f;
            var y = 24f;
            const float textX = m + buttonW + 28f;
            var textW = w - textX - m;

            var drones = DronesUnlocked();
            var stocked = Plugin.AutoTopUp.Value;
            Row(stocked ? "Set Crates To Supply" : "Set Drone Demand", stocked
                    ? "Auto top-up is on, so the crates stock themselves: every crate with an item (its label, or its category) offers that item to the drones and asks for nothing. Crates already set are left alone."
                    : "Every crate with an item (its label, or its category) asks the drones for that item, and for nothing else. Crates already set are left alone.", m, y, buttonW, textX, textW, Lit, () => Do(SetDemand), false, drones);
            y += rowH;
            Row("Set Drone Supply", "Producers (extractors, collectors, beehives, ecosystem, ...) offer what they make to the drones. A hive offers honey and bee larvae, an ecosystem its larvae and eggs.", m, y, buttonW, textX, textW, Lit, () => Do(SetSupply), false, drones);
            y += rowH;
            Row("Resupply", "Fill the empty slots of every crate with its item, at once.", m, y, buttonW, textX, textW, Lit, () => Do(ResupplyNow));
            y += rowH;
            // Only while the T2 ore extractor is unlocked and the T3 is not: the T3 picks its ore itself, so the purge is not needed after that.
            if (PurgeOffered())
            {
                Row("Purge Extractors", "Ore extractors on a vein keep only that vein's ore: everything else in them (iron, cobalt, ...) is deleted. Extractors on no vein are left alone.", m, y, buttonW, textX, textW, Lit, () => Do(PurgeNow));
                y += rowH;
            }

            var on = Plugin.AutoTopUp.Value;
            Row("Auto Top-Up: " + (on ? "ON" : "OFF"),
                "Every " + Mathf.RoundToInt(Plugin.TopUpSeconds.Value) + " seconds, crates less than " + Plugin.TopUpBelowPercent.Value + "% full are topped up. Off unless you switch it on (the numbers are in the config).",
                m, y, buttonW, textX, textW, on ? Lit : Dim, () =>
                {
                    Plugin.AutoTopUp.Value = !Plugin.AutoTopUp.Value;
                    _status = "Auto top-up is " + (Plugin.AutoTopUp.Value ? "on." : "off.");
                    Redraw();
                }, true);
            y += rowH;

            _statusText = Label(_body, _status, m, h - 150f, w - 2 * m, 120f, 24f, _status.StartsWith("Could") || _status.StartsWith("The world") ? Warn : Dim, TextAlignmentOptions.TopLeft, true);
        }

        private void Row(string label, string description, float x, float y, float buttonW, float textX, float textW, Color colour, Action onClick, bool alwaysEnabled = false, bool available = true, string lockedText = "Needs the drone station: it is not unlocked yet.")
        {
            Button(_body, label, x, y, buttonW, 64f, 28f, colour, available && (alwaysEnabled || !_busy), onClick);
            Label(_body, available ? description : lockedText, textX, y - 2f, textW, 80f, 20f, available ? Dim : Warn, TextAlignmentOptions.TopLeft, true);
        }

        // The drone settings only make sense once the drone station is unlocked in the game (everything is, in a game with everything unlocked).
        private static bool DronesUnlocked() => GroupUnlocked(Plugin.DroneStationGroup.Value);

        // The purge is there from the moment the T2 ore extractor is unlocked until the T3 one is.
        private static bool PurgeOffered() => GroupUnlocked(Plugin.PurgeRequiresGroup.Value) && !GroupReallyUnlocked(Plugin.PurgeUntilGroup.Value);

        // Whether a building is unlocked in the game, with no benefit of the doubt: false when the group cannot be found or read.
        private static bool GroupReallyUnlocked(string groupId)
        {
            try
            {
                if (Managers.GetManager<GameSettingsHandler>().GetCurrentGameSettings().GetEverythingUnlocked())
                    return true;

                var group = GroupsHandler.GetGroupViaId(groupId);
                return group != null && (group.GetUnlockingInfos().GetIsUnlocked(true) || group.GetIsGloballyUnlocked());
            }
            catch (Exception e)
            {
                ModLog.LogOnce("supply-really-" + groupId, "Supply Manager: could not tell whether " + groupId + " is unlocked: " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        // Whether a building is unlocked in the game; true in a game with everything unlocked, and true when the group cannot be found or read, so a game update does not lock a button for good.
        private static bool GroupUnlocked(string groupId)
        {
            try
            {
                if (Managers.GetManager<GameSettingsHandler>().GetCurrentGameSettings().GetEverythingUnlocked())
                    return true;

                var group = GroupsHandler.GetGroupViaId(groupId);
                if (group == null)
                    return true;

                return group.GetUnlockingInfos().GetIsUnlocked(true) || group.GetIsGloballyUnlocked();
            }
            catch (Exception e)
            {
                ModLog.LogOnce("supply-unlocked-" + groupId, "Supply Manager: could not tell whether " + groupId + " is unlocked: " + e.GetType().Name + ": " + e.Message);
                return true;
            }
        }

        // ---- the actions ----------------------------------------------------------------------------------------------------

        private void Do(Func<int, string> action)
        {
            if (!WorldState.Ready(out var planetHash, out _))
            {
                Say("The world is not ready yet.");
                return;
            }

            try
            {
                Say(action(planetHash));
            }
            catch (Exception e)
            {
                ModLog.Note("Supply Manager: " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace);
                Say("Could not do that (" + e.Message + "). See the log.");
            }
        }

        private static string SetDemand(int planetHash)
        {
            var result = DroneNetwork.SetDemand(planetHash, Resupplier.Instance.Lookup);
            return (Plugin.AutoTopUp.Value ? "Set Crates To Supply: " : "Set Drone Demand: ") + result.Changed + " crates set, " + result.AlreadySet + " already set, " + result.NoItem + " with no item (no label or category that names one).";
        }

        private static string SetSupply(int planetHash)
        {
            var result = DroneNetwork.SetSupply(planetHash);
            return "Set Drone Supply: " + result.Changed + " producers set, " + result.AlreadySet + " already set, " + result.NoItem + " making nothing yet.";
        }

        private static string PurgeNow(int planetHash)
        {
            var result = ExtractorPurge.Run(planetHash);
            return "Purge Extractors: " + result.Items + " items removed from " + result.Purged + " extractors, " + result.Clean + " already clean, " + result.NoVein + " on no vein.";
        }

        private string ResupplyNow(int planetHash)
        {
            var resupplier = Resupplier.Instance;
            if (resupplier == null || resupplier.Busy)
                return "Still busy filling crates; try again in a moment.";

            _busy = true;
            StartCoroutine(RunResupply(resupplier));
            return "Resupplying...";
        }

        private IEnumerator RunResupply(Resupplier resupplier)
        {
            var lines = new List<string>();
            yield return resupplier.ResupplyAll(line => lines.Add(line));
            _busy = false;
            Say(lines.LastOrDefault(l => l.StartsWith("Done:")) ?? lines.LastOrDefault() ?? "Done.");
        }

        private void Say(string message)
        {
            _status = message;
            ModLog.Note("Supply Manager: " + message);
            if (_statusText != null)
                _statusText.text = message;
            else
                Redraw();
        }

        // ---- drawing helpers (the same as the Boosters screen's) ---------------------------------------------------------------

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private TMP_Text Label(RectTransform parent, string text, float x, float y, float w, float h, float size, Color colour, TextAlignmentOptions align, bool wrap = false)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, w, h);
            var label = go.GetComponent<TextMeshProUGUI>();
            Style(label, text, size, colour, align, wrap);
            return label;
        }

        private void Style(TMP_Text label, string text, float size, Color colour, TextAlignmentOptions align, bool wrap = false)
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
            Style(textObject.GetComponent<TextMeshProUGUI>(), text, size, enabled ? colour : Off, TextAlignmentOptions.Center);
        }
    }
}
