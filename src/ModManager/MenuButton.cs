using System;
using System.Collections;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RRSOS.PCC.ModManager
{
    /// <summary>
    /// Adds the "Manage Mods" button to the main menu (the game's Intro object), above Quit, by copying the Options button. The menu is made again whenever the game returns
    /// to it, so this keeps looking.
    /// </summary>
    internal sealed class MenuButton : MonoBehaviour
    {
        private const string ButtonName = "RRSOS_ManageMods";
        private ManagerScreen _screen;

        private IEnumerator Start()
        {
            _screen = gameObject.AddComponent<ManagerScreen>();

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

                    if (intro.baseContainer.GetComponentsInChildren<Transform>(true).All(t => t.name != ButtonName))
                        AddButton(intro);
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("menu-button", "Menu: " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace);
                }
            }
        }

        private static string LabelOf(Button button)
        {
            var text = button.GetComponentInChildren<TMP_Text>(true);
            return text == null ? "" : text.text;
        }

        private void AddButton(Intro intro)
        {
            var buttons = intro.baseContainer.GetComponentsInChildren<Button>(true);
            var options = buttons.FirstOrDefault(b => LabelOf(b).IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0);
            var quit = buttons.FirstOrDefault(b => LabelOf(b).IndexOf("quit", StringComparison.OrdinalIgnoreCase) >= 0);
            if (options == null || quit == null)
            {
                ModLog.LogOnce("menu-buttons", "Menu: could not find the Options and Quit buttons (found: " + string.Join(", ", buttons.Select(b => "\"" + LabelOf(b) + "\"").ToArray()) + ").");
                return;
            }

            var font = options.GetComponentInChildren<TMP_Text>(true);
            var copy = UnityEngine.Object.Instantiate(options.gameObject, quit.transform.parent, false);
            copy.name = ButtonName;

            var button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();   // the copy must not open the options
            button.onClick.AddListener(() => _screen.Open(intro, font));

            var label = copy.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                var localized = label.GetComponent("LocalizedText") as Behaviour;
                if (localized != null)
                    localized.enabled = false;
                label.text = "Manage Mods";
            }

            // the menu stacks its buttons itself: put the copy where Quit is and Quit moves down
            copy.transform.SetSiblingIndex(quit.transform.GetSiblingIndex());
            ModLog.Note("Menu: added Manage Mods above Quit.");
        }
    }
}
