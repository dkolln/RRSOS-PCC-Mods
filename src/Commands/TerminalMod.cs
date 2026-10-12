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
        private readonly System.Collections.Generic.HashSet<int> _enlargedGhosts = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Reflection.FieldInfo GhostGroup = typeof(ConstructibleGhost).GetField("_groupConstructible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// Makes a terminal (or its build ghost) larger: width and height by the Scale setting, depth unchanged so it still sits on its wall like a sign. Its colliders
        /// and its text grow with it.
        /// </summary>
        internal static void Enlarge(Transform terminal)
        {
            var scale = Mathf.Max(1f, Plugin.Scale.Value);

            // A sign's panel faces along its own x axis: x is the depth, y the height, z the width.
            terminal.localScale = new Vector3(1f, scale, scale);
        }

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
                    EnlargeGhost();
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

        // The see-through preview that follows you while you place the terminal is made by the game from the sign's model; make it the same size as the real thing.
        private void EnlargeGhost()
        {
            if (_group == null || GhostGroup == null)
                return;

            foreach (var ghost in UnityEngine.Object.FindObjectsByType<ConstructibleGhost>(FindObjectsSortMode.None))
            {
                if (GhostGroup.GetValue(ghost) != _group || !_enlargedGhosts.Add(ghost.GetInstanceID()))
                    continue;

                Enlarge(ghost.transform);
            }
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
