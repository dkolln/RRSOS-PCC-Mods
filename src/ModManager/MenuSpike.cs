using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using RRSOS.PCC.Shared;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RRSOS.PCC.ModManager
{
    /// <summary>
    /// Finds the main menu (the game's Intro object), writes its structure and the loaded plugins with their config entries, and adds a "Manage Mods" button above Quit.
    /// </summary>
    internal sealed class MenuSpike : MonoBehaviour
    {
        private const string ButtonName = "RRSOS_ManageMods";
        private bool _reported;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(1f);

                if (!Plugin.Enabled.Value)
                    continue;

                try
                {
                    var intro = UnityEngine.Object.FindFirstObjectByType<Intro>(FindObjectsInactive.Include);
                    if (intro == null || intro.baseContainer == null)
                        continue;

                    if (!_reported)
                    {
                        _reported = true;
                        WriteMenu(intro);
                        WritePlugins();
                    }

                    if (intro.baseContainer.transform.Find(ButtonName) == null && intro.baseContainer.GetComponentsInChildren<Transform>(true).All(t => t.name != ButtonName))
                        AddButton(intro);
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("menu-spike", "Menu: " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace);
                }
            }
        }

        private static string LabelOf(Button button)
        {
            var text = button.GetComponentInChildren<TMP_Text>(true);
            return text == null ? "" : text.text;
        }

        private static void AddButton(Intro intro)
        {
            var buttons = intro.baseContainer.GetComponentsInChildren<Button>(true);
            var options = buttons.FirstOrDefault(b => LabelOf(b).IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0);
            var quit = buttons.FirstOrDefault(b => LabelOf(b).IndexOf("quit", StringComparison.OrdinalIgnoreCase) >= 0);
            if (options == null || quit == null)
            {
                ModLog.LogOnce("menu-buttons", "Menu: could not find the Options and Quit buttons (found: " + string.Join(", ", buttons.Select(b => "\"" + LabelOf(b) + "\"").ToArray()) + ").");
                return;
            }

            var parent = quit.transform.parent;
            var copy = UnityEngine.Object.Instantiate(options.gameObject, parent, false);
            copy.name = ButtonName;

            var button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();   // the copy must not open the options
            button.onClick.AddListener(() => ModLog.Note("Manage Mods was clicked."));

            var label = copy.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                var localized = label.GetComponent("LocalizedText") as Behaviour;
                if (localized != null)
                    localized.enabled = false;
                label.text = "Manage Mods";
            }

            // Above Quit: with a layout group the order is enough; without one the buttons are moved by hand.
            var quitRect = (RectTransform)quit.transform;
            var optionsRect = (RectTransform)options.transform;
            var copyRect = (RectTransform)copy.transform;
            if (parent.GetComponent<LayoutGroup>() != null)
            {
                copy.transform.SetSiblingIndex(quit.transform.GetSiblingIndex());
            }
            else
            {
                var step = quitRect.anchoredPosition - optionsRect.anchoredPosition;
                copyRect.anchoredPosition = quitRect.anchoredPosition;
                quitRect.anchoredPosition += step;
            }

            ModLog.Note("Menu: added Manage Mods above Quit (layout group: " + (parent.GetComponent<LayoutGroup>() != null) + ").");
        }

        private static void WriteMenu(Intro intro)
        {
            var text = new StringBuilder("Main menu (the game's Intro object), written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
            Walk(intro.baseContainer.transform, 0, text);
            text.AppendLine().AppendLine("optionsMenu: " + (intro.optionsMenu == null ? "none" : intro.optionsMenu.name));
            File.WriteAllText(ModLog.FileIn("menu-structure.txt"), text.ToString());
        }

        private static void Walk(Transform t, int depth, StringBuilder text)
        {
            text.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [inactive]");
            if (t is RectTransform r)
                text.Append("  anchors ").Append(r.anchorMin).Append("-").Append(r.anchorMax).Append("  pos ").Append(r.anchoredPosition).Append("  size ").Append(r.rect.size);
            var parts = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray();
            if (parts.Length > 0)
                text.Append("  [").Append(string.Join(", ", parts)).Append("]");
            var label = t.GetComponent<TMP_Text>();
            if (label != null)
                text.Append("  text \"").Append(label.text).Append("\"");
            text.AppendLine();
            for (var i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), depth + 1, text);
        }

        private static void WritePlugins()
        {
            var text = new StringBuilder("Loaded plugins, written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
            foreach (var info in Chainloader.PluginInfos.Values.OrderBy(i => i.Metadata.Name))
            {
                text.AppendLine().Append(info.Metadata.Name).Append(" ").Append(info.Metadata.Version).Append("  ").AppendLine(info.Metadata.GUID);
                text.Append("  dll: ").AppendLine(info.Location);
                var config = info.Instance == null ? null : info.Instance.Config;
                if (config == null)
                {
                    text.AppendLine("  (no config)");
                    continue;
                }

                text.Append("  config file: ").AppendLine(config.ConfigFilePath);
                foreach (var key in config.Keys)
                {
                    var entry = config[key];
                    var allowed = entry.Description == null || entry.Description.AcceptableValues == null ? "" : "  allowed " + entry.Description.AcceptableValues.ToDescriptionString().Replace(Environment.NewLine, " ");
                    text.Append("    [").Append(key.Section).Append("] ").Append(key.Key).Append(" = ").Append(entry.GetSerializedValue()).Append("  (").Append(entry.SettingType.Name).Append(", default ").Append(entry.DefaultValue).Append(")").AppendLine(allowed);
                }
            }

            File.WriteAllText(ModLog.FileIn("plugins.txt"), text.ToString());
        }
    }
}
