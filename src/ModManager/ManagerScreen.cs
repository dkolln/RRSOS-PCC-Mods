using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RRSOS.PCC.ModManager
{
    /// <summary>
    /// The Manage Mods screen: it covers the main menu (whose own buttons are hidden while it is open). On the left the mods, on the right the selected mod's settings. A setting
    /// is changed through BepInEx's own config entry, which applies it at once and saves it to the mod's .cfg file. A mod can also be switched off for the next start by moving its
    /// folder out of BepInEx\plugins (ModList). Escape or Back closes it, after asking about a restart when something was changed.
    /// </summary>
    internal sealed class ManagerScreen : MonoBehaviour
    {
        private const string RootName = "RRSOS_ModManager";

        private Intro _intro;
        private UiKit _ui;
        private RectTransform _root;
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private List<ModInfo> _mods = new List<ModInfo>();
        private ModInfo _selected;
        private string _message = "";
        private bool _open;
        private bool _asking;                         // the restart question is on the screen
        private Dictionary<string, string> _before = new Dictionary<string, string>();   // every setting and mod switch as it was when the screen opened
        private float _listScroll, _configScroll;
        private RectTransform _listContent, _configContent;

        public void Open(Intro intro, TMP_Text fontFrom)
        {
            _intro = intro;
            _ui = new UiKit(fontFrom);
            _mods = ModList.Load();
            _selected = _selected == null ? _mods.FirstOrDefault() : _mods.FirstOrDefault(m => m.Top == _selected.Top && m.Guid == _selected.Guid) ?? _mods.FirstOrDefault();
            _message = "";
            _asking = false;
            _before = Snapshot();
            _listScroll = _configScroll = 0f;

            var canvas = (RectTransform)intro.baseContainer.transform;
            _hidden.Clear();
            for (var i = 0; i < canvas.childCount; i++)
            {
                var child = canvas.GetChild(i);
                if (child.name == RootName || !child.gameObject.activeSelf)
                    continue;
                _hidden.Add(child.gameObject);
                child.gameObject.SetActive(false);
            }

            var existing = canvas.Find(RootName);
            if (existing != null)
                Destroy(existing.gameObject);

            // the menu's other pieces that sit on their own canvases (the social corner, the DLC adverts) would show through
            foreach (var other in new[] { intro.socialCorner, intro.DLCCanvas })
            {
                if (other != null && other.activeSelf)
                {
                    _hidden.Add(other);
                    other.SetActive(false);
                }
            }

            _root = _ui.Panel(canvas, RootName, new Color(0f, 0f, 0f, 1f));
            var rootCanvas = _root.gameObject.AddComponent<Canvas>();
            rootCanvas.overrideSorting = true;
            rootCanvas.sortingOrder = 5000;
            _root.gameObject.AddComponent<GraphicRaycaster>();
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            _root.SetAsLastSibling();

            _open = true;
            Redraw();
        }

        /// <summary>Back and Escape: when something was changed since the screen opened, the player is asked about a restart first.</summary>
        public void RequestClose()
        {
            if (!_open)
                return;

            if (_asking)
            {
                _asking = false;   // Escape on the question is "not now"
                Close();
                return;
            }

            if (ChangeCount() > 0)
            {
                _asking = true;
                Redraw();
                return;
            }

            Close();
        }

        public void Close()
        {
            if (!_open)
                return;

            _open = false;
            _asking = false;
            if (_root != null)
                Destroy(_root.gameObject);
            _root = null;

            foreach (var go in _hidden)
                if (go != null)
                    go.SetActive(true);
            _hidden.Clear();
        }

        private void Update()
        {
            if (!_open)
                return;

            if (_root == null)
            {
                _open = false;   // the menu scene was left
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && EventSystemNotTyping())
                RequestClose();
        }

        // Escape leaves a text box first; it only closes the screen when no box is being edited.
        private static bool EventSystemNotTyping()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current == null ? null : UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
            var field = selected == null ? null : selected.GetComponent<TMP_InputField>();
            return field == null || !field.isFocused;
        }

        // ---- drawing ---------------------------------------------------------------------------------------------------------

        private void Redraw()
        {
            if (_root == null)
                return;

            if (_listContent != null)
                _listScroll = _listContent.anchoredPosition.y;
            if (_configContent != null)
                _configScroll = _configContent.anchoredPosition.y;

            for (var i = _root.childCount - 1; i >= 0; i--)
                Destroy(_root.GetChild(i).gameObject);

            Canvas.ForceUpdateCanvases();
            var width = _root.rect.width > 600f ? _root.rect.width : 1920f;
            var height = _root.rect.height > 400f ? _root.rect.height : 1080f;

            _ui.Label(_root, "Manage Mods", 0f, 28f, width, 70f, 54f, UiKit.Lit, TextAlignmentOptions.Center);
            _ui.Label(_root, "Settings are saved at once. A mod reads some of them only at start-up: those need a restart of the game.", 0f, 98f, width, 36f, 20f, UiKit.Dim, TextAlignmentOptions.Center);

            // the mods
            _listContent = _ui.ScrollList(_root, 60f, 150f, 540f, height - 150f - 150f, 6f);
            foreach (var mod in _mods)
                AddModRow(mod);

            // the selected mod
            var rightX = 640f;
            var rightW = width - rightX - 60f;
            if (_selected != null)
                DrawDetails(rightX, rightW, height);

            _ui.Button(_root, "Back", 60f, height - 120f, 280f, 70f, 34f, UiKit.Lit, true, RequestClose);
            _ui.Label(_root, _message, 370f, height - 112f, width - 430f, 56f, 22f, _message.StartsWith("Could") || _message.StartsWith("Not") || _message.StartsWith("The") ? UiKit.Warn : UiKit.Dim, TextAlignmentOptions.Left, true);

            if (_asking)
                DrawRestartQuestion(width, height);

            Canvas.ForceUpdateCanvases();
            if (_listContent != null)
                _listContent.anchoredPosition = new Vector2(0f, _listScroll);
            if (_configContent != null)
                _configContent.anchoredPosition = new Vector2(0f, _configScroll);
        }

        // ---- the restart question ----------------------------------------------------------------------------------------------

        private Dictionary<string, string> Snapshot()
        {
            var all = new Dictionary<string, string>();
            foreach (var mod in _mods)
            {
                all["off|" + mod.Guid + "|" + mod.Top] = mod.OffAtNextStart.ToString();
                if (mod.Config == null || !mod.Loaded)
                    continue;
                foreach (var key in mod.Config.Keys)
                    all[mod.Guid + "|" + key.Section + "|" + key.Key] = mod.Config[key].GetSerializedValue();
            }

            return all;
        }

        private int ChangeCount()
        {
            var now = Snapshot();
            return now.Count(pair => !_before.TryGetValue(pair.Key, out var was) || was != pair.Value);
        }

        private void DrawRestartQuestion(float width, float height)
        {
            var count = ChangeCount();
            var cover = _ui.Panel(_root, "RestartQuestion", new Color(0f, 0f, 0f, 0.82f));
            UiKit.Place(cover, 0f, 0f, width, height);
            var box = _ui.Panel(cover, "Box", new Color(0.05f, 0.08f, 0.1f, 1f));
            UiKit.Place(box, width / 2f - 450f, height / 2f - 200f, 900f, 400f);
            _ui.Label(box, "Restart the game?", 0f, 30f, 900f, 70f, 50f, UiKit.Lit, TextAlignmentOptions.Center);
            _ui.Label(box, count + (count == 1 ? " change was" : " changes were") + " made. Some settings, and mods switched on or off, only take effect when the game starts again.",
                50f, 120f, 800f, 120f, 26f, UiKit.Dim, TextAlignmentOptions.Center, true);
            _ui.Button(box, "Restart now", 80f, 290f, 360f, 70f, 32f, UiKit.Lit, true, RestartGame);
            _ui.Button(box, "Not now", 460f, 290f, 360f, 70f, 32f, UiKit.Dim, true, () =>
            {
                _asking = false;
                Close();
            });
        }

        /// <summary>Starts the game again through Steam a few seconds from now, and closes it. From the main menu nothing is unsaved.</summary>
        private void RestartGame()
        {
            try
            {
                var start = new ProcessStartInfo("cmd.exe", "/c ping -n 9 127.0.0.1 >nul & start \"\" \"steam://rungameid/1284190\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(start);
                ModLog.Note("Restart: the game is closing and will be started again through Steam in a few seconds.");
                Application.Quit();
            }
            catch (Exception e)
            {
                _asking = false;
                _message = "Could not restart the game (" + e.Message + "). Close it and start it again by hand.";
                ModLog.Note("Restart failed: " + e.GetType().Name + ": " + e.Message);
                Redraw();
            }
        }

        // "RRSOS PCC Base" is shown as "Base": every mod of the set carries the prefix, so it only takes room
        private static string Plain(string name) => name.StartsWith("RRSOS PCC ", StringComparison.Ordinal) ? name.Substring("RRSOS PCC ".Length) : name;

        private void AddModRow(ModInfo mod)
        {
            var row = UiKit.Row(_listContent, 60f);
            var on = mod.EnabledEntry == null || (bool)mod.EnabledEntry.BoxedValue;
            var colour = mod.OffAtNextStart ? UiKit.Warn : on ? UiKit.Lit : UiKit.Dim;
            var state = mod.OffAtNextStart ? "off at next start" : !mod.Loaded ? "not loaded" : on ? "on" : "off";
            var face = mod == _selected ? UiKit.FaceSelected : UiKit.Face;
            var name = Plain(mod.Name) + (mod.Version.Length > 0 ? "  " + mod.Version : "");
            var button = _ui.Button(row, "", 0f, 0f, 520f, 60f, 22f, colour, true, () =>
            {
                _selected = mod;
                _message = "";
                _configScroll = 0f;
                Redraw();
            }, face);
            _ui.Label(button.GetComponent<RectTransform>(), name, 14f, 6f, 360f, 48f, 24f, colour);
            _ui.Label(button.GetComponent<RectTransform>(), state, 360f, 6f, 150f, 48f, 20f, colour, TextAlignmentOptions.Right);
        }

        private void DrawDetails(float x, float w, float height)
        {
            var mod = _selected;
            _ui.Label(_root, Plain(mod.Name) + (mod.Version.Length > 0 ? "   " + mod.Version : ""), x, 146f, w, 56f, 38f, UiKit.Lit);
            _ui.Label(_root, (mod.Guid.Length > 0 ? mod.Guid + "   " : "") + ModList.FolderText(mod), x, 200f, w, 28f, 17f, UiKit.Dim);

            var text = mod.OffAtNextStart ? "At the next start: OFF  (click to switch it on)" : "At the next start: ON  (click to switch it off)";
            var can = !mod.IsSelf && mod.Top != null;
            _ui.Button(_root, text, x, 236f, 640f, 48f, 22f, mod.OffAtNextStart ? UiKit.Warn : UiKit.Lit, can, () =>
            {
                _message = ModList.Toggle(mod) ?? (mod.OffAtNextStart ? Plain(mod.Name) + " will not load at the next start." : Plain(mod.Name) + " will load at the next start.");
                Redraw();
            });
            if (mod.IsSelf)
                _ui.Label(_root, "The Mod Manager stays on.", x + 660f, 244f, w - 660f, 32f, 20f, UiKit.Dim);

            if (mod.Config == null || !mod.Loaded)
            {
                _ui.Label(_root, mod.Loaded ? "This mod has no settings." : "This mod is not loaded now, so its settings are not available.", x, 310f, w, 40f, 24f, UiKit.Dim);
                _configContent = null;
                return;
            }

            _configContent = _ui.ScrollList(_root, x, 300f, w, height - 300f - 150f, 6f);
            var any = false;
            string section = null;
            foreach (var key in mod.Config.Keys)
            {
                if (mod.IsSelf && key.Key == "Enabled")
                    continue;   // switching the manager off from its own screen would leave no way back

                if (key.Section != section)
                {
                    section = key.Section;
                    var head = UiKit.Row(_configContent, 44f);
                    _ui.Label(head, "[" + section + "]", 8f, 6f, w - 40f, 36f, 26f, UiKit.Dim);
                }

                AddSetting(mod.Config[key], w - 28f);
                any = true;
            }

            if (!any)
                _ui.Label(_root, "This mod has no settings.", x, 310f, w, 40f, 24f, UiKit.Dim);
        }

        private void AddSetting(ConfigEntryBase entry, float width)
        {
            var type = entry.SettingType;
            var choices = Choices(entry);
            var wide = type != typeof(bool) && (choices == null || choices.Length <= 1) && (type == typeof(string) || entry.GetSerializedValue().Length > 16);
            var row = UiKit.Row(_configContent, wide ? 196f : 112f);
            var widgetX = width - 440f;
            _ui.Label(row, entry.Definition.Key, 8f, 2f, wide ? width - 16f : widgetX - 24f, 42f, 26f, UiKit.Lit);
            _ui.Label(row, Describe(entry), 8f, 46f, wide ? width - 16f : widgetX - 24f, 62f, 17f, UiKit.Dim, TextAlignmentOptions.TopLeft, true);

            if (type == typeof(bool))
            {
                var on = (bool)entry.BoxedValue;
                _ui.Button(row, on ? "ON" : "OFF", widgetX, 28f, 250f, 50f, 26f, on ? UiKit.Lit : UiKit.Dim, true, () =>
                {
                    entry.BoxedValue = !on;
                    Redraw();
                });
            }
            else if (choices != null && choices.Length > 1)
            {
                var current = entry.GetSerializedValue();
                _ui.Button(row, current, widgetX, 28f, 250f, 50f, 24f, UiKit.Lit, true, () =>
                {
                    var index = Array.FindIndex(choices, c => string.Equals(Serialize(entry, c), current, StringComparison.OrdinalIgnoreCase));
                    entry.BoxedValue = choices[(index + 1) % choices.Length];
                    Redraw();
                });
            }
            else
            {
                var boxX = wide ? 8f : widgetX;
                var boxY = wide ? 120f : 28f;
                var boxW = wide ? width - 200f : 250f;
                _ui.Input(row, entry.GetSerializedValue(), boxX, boxY, boxW, 50f, 24f, text =>
                {
                    try
                    {
                        entry.SetSerializedValue(text);
                        _message = entry.Definition.Key + " is now " + entry.GetSerializedValue() + ".";
                    }
                    catch (Exception)
                    {
                        _message = "The value \"" + text + "\" does not fit " + entry.Definition.Key + " (" + type.Name + ").";
                    }

                    Redraw();
                });
            }

            var isDefault = entry.GetSerializedValue() == Serialize(entry, entry.DefaultValue);
            _ui.Button(row, "Reset", wide ? width - 170f : widgetX + 262f, wide ? 120f : 28f, 150f, 50f, 22f, isDefault ? UiKit.Off : UiKit.Dim, !isDefault, () =>
            {
                entry.BoxedValue = entry.DefaultValue;
                Redraw();
            });
        }

        private static string Describe(ConfigEntryBase entry)
        {
            var text = entry.Description == null ? "" : (entry.Description.Description ?? "");
            var range = entry.Description == null ? null : entry.Description.AcceptableValues;
            if (range != null && Choices(entry) == null)
                text += "  (" + range.ToDescriptionString().Replace("# ", "").Replace(System.Environment.NewLine, " ") + ")";
            return text.Trim() + "  Default: " + Serialize(entry, entry.DefaultValue);
        }

        private static string Serialize(ConfigEntryBase entry, object value)
        {
            try
            {
                return BepInEx.Configuration.TomlTypeConverter.ConvertToString(value, entry.SettingType);
            }
            catch
            {
                return value == null ? "" : value.ToString();
            }
        }

        // the values a setting can take when they are a fixed list (a list of allowed values, or an enum); null when it is free
        private static object[] Choices(ConfigEntryBase entry)
        {
            if (entry.SettingType.IsEnum)
                return Enum.GetValues(entry.SettingType).Cast<object>().ToArray();

            var allowed = entry.Description == null ? null : entry.Description.AcceptableValues;
            if (allowed == null)
                return null;

            var property = allowed.GetType().GetProperty("AcceptableValues");
            var values = property == null ? null : property.GetValue(allowed, null) as Array;
            return values == null ? null : values.Cast<object>().ToArray();
        }
    }
}
