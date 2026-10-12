using System;
using System.Collections;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// Adds a "Terminal" to the build menu (a sign underneath; 1 Iron, 1 Silicon, 1 Titanium, 1 Cobalt) and gives every placed terminal its screen.
    /// A terminal is saved in the world like any other building; if this mod is removed the game drops it from the world the next time the save loads.
    /// </summary>
    internal sealed class TerminalMod : MonoBehaviour
    {
        private const string NewId = "Terminal";
        private const string SourceId = "Sign";

        // The big screen the terminal should look like: its icon is used now, its model once the structure of both prefabs is known.
        private const string ScreenId = "ScreenTerraformation";
        private static readonly string[] Ingredients = { "Iron", "Silicon", "Titanium", "Cobalt" };

        private Group _group;
        private ChatHandler _listeningTo;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.5f);

                if (!Plugin.Enabled.Value)
                    continue;

                try
                {
                    EnsureRegistered();
                    ListenToChat();
                    GiveScreens();
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("terminal-loop", "Terminal: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // The game rebuilds its group lists when it reloads its static data, so check each time and put the group back if it is missing.
        private void EnsureRegistered()
        {
            if (GroupsHandler.GetAllGroups() == null)
                return;

            if (_group == null)
            {
                _group = GroupCloner.Clone(SourceId, NewId, Ingredients, ScreenId);
                if (_group == null)
                    return;
                ModLog.Note("Terminal: created the build item from " + SourceId + " (" + string.Join(", ", Ingredients) + ").");
                PrefabDump.Write(SourceId, ScreenId);
            }

            if (!GroupCloner.IsInGame(_group))
            {
                GroupCloner.AddToGame(_group);
                ModLog.Note("Terminal: added to the game's group lists.");
            }
        }

        // Every world has its own ChatHandler; the mods' replies come through its message event.
        private void ListenToChat()
        {
            var chat = ChatHandler.Instance;
            if (chat == null || chat == _listeningTo)
                return;

            if (_listeningTo != null)
                _listeningTo.OnChatMessageReceived -= TerminalShell.OnChatMessage;
            chat.OnChatMessageReceived += TerminalShell.OnChatMessage;
            _listeningTo = chat;
        }

        private void GiveScreens()
        {
            if (_group == null || WorldObjectsHandler.Instance == null || !WorldState.Ready(out _, out _))
                return;

            foreach (var wo in WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() == _group).ToList())
            {
                var go = wo.GetGameObject();
                if (go == null || go.GetComponentInChildren<TerminalScreen>(true) != null)
                    continue;

                go.AddComponent<TerminalScreen>().Init(wo);
            }
        }
    }
}
