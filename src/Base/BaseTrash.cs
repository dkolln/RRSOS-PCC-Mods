using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Base
{
    /// <summary>
    /// /trash and /trash empty on the terminal (and in the chat). Every container on the planet whose label is "Trash" (ignoring case and spaces around it) is a trash can:
    /// /trash says what is in them, /trash empty deletes what is in them for good. Only containers (the groups in the LockerGroups setting, Container1, 2 and 3 by default) and only
    /// with that label are ever emptied; the containers themselves stay.
    /// </summary>
    internal sealed class BaseTrash : MonoBehaviour
    {
        private const string TrashLabel = "Trash";
        private const int ItemsPerFrame = 20;

        private sealed class Request
        {
            public bool Delete;
            public Action<string> Say;
        }

        private static readonly Queue<Request> Requests = new Queue<Request>();
        private bool _registered;

        /// <summary>The terminal's and the chat's command: nothing (what is in the trash) or "empty". The work is done on the game's main thread by the loop below.</summary>
        internal static void OnCommand(string args, Action<string> say)
        {
            var word = (args ?? "").Trim().ToLowerInvariant();
            if (word.Length != 0 && word != "empty")
            {
                say("Usage: /trash empty (deletes what is in every container labelled Trash)");
                return;
            }

            lock (Requests)
                Requests.Enqueue(new Request { Delete = word == "empty", Say = say });
        }

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.5f);

                if (!Plugin.Enabled.Value)
                    continue;

                if (!_registered)
                {
                    try
                    {
                        if (TerminalRegistration.TryRegister("/trash", "/trash empty", "delete everything in every container labelled Trash", OnCommand))
                        {
                            _registered = true;
                            ModLog.Note("The terminal is installed: /trash is on it.");
                        }
                    }
                    catch (Exception e)
                    {
                        _registered = true; // do not try again every half second
                        ModLog.Note("Could not register /trash with the terminal: " + e.Message);
                    }
                }

                Request request = null;
                lock (Requests)
                    if (Requests.Count > 0)
                        request = Requests.Dequeue();
                if (request == null)
                    continue;

                if (BaseBuilder.Busy)
                {
                    request.Say("The base is busy; try again in a moment.");
                    continue;
                }

                var planetHash = 0;
                var ready = false;
                try
                {
                    ready = WorldState.Ready(out planetHash, out _);
                }
                catch (Exception)
                {
                    ready = false;
                }

                if (!ready)
                {
                    request.Say("The world is not ready yet.");
                    continue;
                }

                BaseBuilder.Busy = true;
                try
                {
                    yield return Run(request, planetHash);
                }
                finally
                {
                    BaseBuilder.Busy = false;
                }
            }
        }

        private static List<WorldObject> Lockers(int planetHash)
        {
            var groups = new HashSet<string>(Plugin.LockerGroups.Value.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0));
            return WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                .Where(w => w != null && w.GetGroup() != null && groups.Contains(w.GetGroup().GetId()) && w.GetLinkedInventoryId() != 0 && w.GetPlanetHash() == planetHash
                    && string.Equals(CrateFill.Label(w), TrashLabel, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static IEnumerator Run(Request request, int planetHash)
        {
            var lockers = Lockers(planetHash);
            if (lockers.Count == 0)
            {
                request.Say("No container is labelled " + TrashLabel + ".");
                yield break;
            }

            var inventories = InventoriesHandler.Instance;
            var handler = WorldObjectsHandler.Instance;
            var held = lockers.Sum(l => inventories.GetInventoryById(l.GetLinkedInventoryId())?.GetInsideWorldObjects().Count ?? 0);
            if (!request.Delete)
            {
                request.Say(lockers.Count + " container" + (lockers.Count == 1 ? "" : "s") + " labelled " + TrashLabel + " hold" + (lockers.Count == 1 ? "s " : " ") + held + " items. /trash empty deletes them.");
                yield break;
            }

            var deleted = 0;
            var since = 0;
            foreach (var locker in lockers)
            {
                var inventory = inventories.GetInventoryById(locker.GetLinkedInventoryId());
                if (inventory == null)
                    continue;

                // Only the contents: the locker and its label stay. Taken out of the inventory first, then destroyed if the game has not already done it.
                foreach (var item in inventory.GetInsideWorldObjects().ToList())
                {
                    var id = item.GetId();
                    try
                    {
                        inventories.RemoveItemFromInventory(item, inventory, true);
                        if (handler.GetWorldObjectViaId(id) != null)
                            handler.DestroyWorldObject(id, true);
                        deleted++;
                    }
                    catch (Exception e)
                    {
                        ModLog.Note("Trash: could not delete " + item.GetGroup().GetId() + " #" + id + " from locker #" + locker.GetId() + ": " + e.GetType().Name + ": " + e.Message);
                    }

                    if (++since >= ItemsPerFrame)
                    {
                        since = 0;
                        yield return null;
                    }
                }
            }

            yield return null;
            var left = lockers.Sum(l => inventories.GetInventoryById(l.GetLinkedInventoryId())?.GetInsideWorldObjects().Count ?? 0);
            ModLog.Note("Trash: " + deleted + " of " + held + " items deleted from " + lockers.Count + " lockers; " + left + " left.");
            request.Say("Trash emptied: " + deleted + " items deleted." + (left > 0 ? " " + left + " could not be deleted (see the Base log)." : ""));
        }
    }
}
