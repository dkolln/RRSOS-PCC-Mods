using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Vehicle
{
    /// <summary>
    /// /vehicle on the terminal.
    ///   spawn          puts a new rover, with no gear, in front of the player.
    ///   upgrade min    gives the nearest rover the first tier of every piece of rover gear it has no tier of yet.
    ///   upgrade max    leaves the nearest rover with exactly the top tier of every piece of rover gear: other gear is taken out and deleted, what is already the top tier stays.
    /// Gear is whatever item groups the game calls Vehicle...N (equipment size, inventory size, speed, oxygen, lights, beacon), grouped by the name without its tier number. It goes into
    /// the rover's own equipment inventory through the game's add-to-inventory call, so the rover reacts as when the gear is put in by hand.
    /// </summary>
    internal sealed class VehicleCommand : MonoBehaviour
    {
        private const float WaitForItemSeconds = 3f;
        private static readonly Regex GearId = new Regex(@"^(Vehicle[A-Za-z]+?)(\d+)$");

        private bool _registered;
        private bool _busy;

        private IEnumerator Start()
        {
            while (!_registered)
            {
                yield return new WaitForSecondsRealtime(0.5f);

                if (!Plugin.Enabled.Value)
                    continue;

                try
                {
                    if (TerminalRegistration.TryRegister("/vehicle", "/vehicle spawn | /vehicle upgrade min|max", "spawn a rover, or fit its gear at the first or top tier", OnCommand))
                    {
                        _registered = true;
                        ModLog.Note("The terminal is installed: /vehicle is on it.");
                    }
                }
                catch (Exception e)
                {
                    _registered = true; // do not try again every half second
                    ModLog.Note("Could not register /vehicle with the terminal: " + e.Message);
                }
            }
        }

        private void OnCommand(string args, Action<string> print)
        {
            try
            {
                if (_busy)
                {
                    print("Still busy with the last /vehicle.");
                    return;
                }

                if (!WorldState.Ready(out var planetHash, out _))
                {
                    print("The world is not ready yet.");
                    return;
                }

                var words = (args ?? "").Trim().ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 1 && words[0] == "spawn")
                {
                    StartCoroutine(Spawn(planetHash, print));
                }
                else if (words.Length == 2 && words[0] == "upgrade" && (words[1] == "min" || words[1] == "max"))
                {
                    StartCoroutine(Upgrade(words[1] == "max", planetHash, print));
                }
                else
                {
                    print("Usage: /vehicle spawn   or   /vehicle upgrade min   or   /vehicle upgrade max");
                }
            }
            catch (Exception e)
            {
                ModLog.Note("/vehicle failed: " + e.GetType().Name + ": " + e.Message);
                print("That did not work (" + e.Message + "). See the mod's log.");
            }
        }

        // ---- spawn ------------------------------------------------------------------------------------------------------------

        private IEnumerator Spawn(int planetHash, Action<string> print)
        {
            _busy = true;
            try
            {
                var player = Managers.GetManager<PlayersManager>().GetActivePlayerController();
                var at = player.transform.position;
                var forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f)
                    forward = Vector3.forward;

                // in front of the player, on the ground there
                var spot = at + forward * Plugin.SpawnDistance.Value;
                if (Physics.Raycast(spot + Vector3.up * 30f, Vector3.down, out var hit, 80f))
                    spot = hit.point + Vector3.up * 1f;
                else
                    spot = new Vector3(spot.x, at.y + 1f, spot.z);

                WorldObject made;
                try
                {
                    made = Placement.PlaceAt(Plugin.VehicleGroup.Value, spot, Quaternion.LookRotation(forward), planetHash);
                }
                catch (Exception e)
                {
                    ModLog.Note("/vehicle spawn: " + e.GetType().Name + ": " + e.Message);
                    print("Could not spawn the rover: " + e.Message);
                    yield break;
                }

                yield return null;
                var gearInventory = EquipmentInventory(made);
                ModLog.Note("/vehicle spawn: " + made.GetGroup().GetId() + " #" + made.GetId() + " at " + spot + "; storage inventory " + made.GetLinkedInventoryId()
                    + ", equipment inventory " + (gearInventory == null ? "none" : gearInventory.GetId().ToString()) + ".");
                print("Rover spawned in front of you" + (gearInventory == null ? " (it has no equipment inventory; see the log)." : ", with no gear."));
            }
            finally
            {
                _busy = false;
            }
        }

        // ---- upgrade ----------------------------------------------------------------------------------------------------------

        /// <summary>Every kind of rover gear: family (the id without its tier number) -> its items, lowest tier first.</summary>
        private static Dictionary<string, List<GroupItem>> Families()
        {
            var families = new Dictionary<string, List<GroupItem>>();
            foreach (var group in GroupsHandler.GetAllGroups())
            {
                var item = group as GroupItem;
                if (item == null)
                    continue;

                var match = GearId.Match(item.GetId());
                if (!match.Success)
                    continue;

                var family = match.Groups[1].Value;
                if (!families.TryGetValue(family, out var list))
                    families[family] = list = new List<GroupItem>();
                list.Add(item);
            }

            foreach (var key in families.Keys.ToList())
                families[key] = families[key].OrderBy(i => TierOf(i.GetId())).ToList();
            return families;
        }

        private static int TierOf(string id) => int.Parse(GearId.Match(id).Groups[2].Value);

        private static string FamilyOf(string id)
        {
            var match = GearId.Match(id);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>The rover's equipment inventory: the first of its secondary inventories.</summary>
        private static Inventory EquipmentInventory(WorldObject vehicle)
        {
            var ids = vehicle.GetSecondaryInventoriesId();
            return ids == null || ids.Count == 0 ? null : InventoriesHandler.Instance.GetInventoryById(ids[0]);
        }

        /// <summary>The rover closest to the player (one standing in the world; failing that, any rover that has an equipment inventory).</summary>
        private static WorldObject NearestVehicle(int planetHash)
        {
            var player = Managers.GetManager<PlayersManager>().GetActivePlayerController().transform.position;
            var all = WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                .Where(w => w != null && w.GetGroup() != null && w.GetGroup().GetId() == Plugin.VehicleGroup.Value && EquipmentInventory(w) != null).ToList();

            var standing = all.Where(w => w.GetPosition().sqrMagnitude > 0f && w.GetPlanetHash() == planetHash).OrderBy(w => (w.GetPosition() - player).sqrMagnitude).ToList();
            return standing.Count > 0 ? standing[0] : all.FirstOrDefault();
        }

        private IEnumerator Upgrade(bool max, int planetHash, Action<string> print)
        {
            _busy = true;
            try
            {
                var vehicle = NearestVehicle(planetHash);
                if (vehicle == null)
                {
                    print("There is no rover to upgrade. Spawn one first: /vehicle spawn");
                    yield break;
                }

                var inventory = EquipmentInventory(vehicle);
                var families = Families();
                if (families.Count == 0)
                {
                    print("The game has no rover gear groups (Vehicle...). See the log.");
                    ModLog.Note("/vehicle upgrade: no group matched " + GearId);
                    yield break;
                }

                var targets = families.Values.Select(l => max ? l[l.Count - 1] : l[0]).ToList();
                var removed = new List<string>();

                if (max)
                {
                    // Everything that is rover gear and not the top tier of its family goes (a second piece of the same tier too). What is already the top tier stays, so the rover
                    // never loses room it is about to get back. The equipment-size gear goes last: it is what makes room for the rest.
                    var keep = new HashSet<string>();
                    var toRemove = new List<WorldObject>();
                    foreach (var piece in inventory.GetInsideWorldObjects().ToList())
                    {
                        var id = piece.GetGroup().GetId();
                        if (FamilyOf(id) == null || !families.ContainsKey(FamilyOf(id)))
                            continue;

                        if (targets.Any(t => t.GetId() == id) && keep.Add(id))
                            continue;
                        toRemove.Add(piece);
                    }

                    foreach (var piece in toRemove.OrderBy(p => FamilyOf(p.GetGroup().GetId()) == "VehicleEquipmentSize" ? 1 : 0))
                    {
                        var id = piece.GetId();
                        var name = piece.GetGroup().GetId();
                        try
                        {
                            InventoriesHandler.Instance.RemoveItemFromInventory(piece, inventory, true);
                            if (WorldObjectsHandler.Instance.GetWorldObjectViaId(id) != null)
                                WorldObjectsHandler.Instance.DestroyWorldObject(id, true);
                            removed.Add(name);
                        }
                        catch (Exception e)
                        {
                            ModLog.Note("/vehicle upgrade: could not remove " + name + " #" + id + ": " + e.GetType().Name + ": " + e.Message);
                        }

                        yield return null;
                    }
                }

                // Add what is missing: for min, a family with no piece at all gets its first tier; for max, the top tier of each family unless it is already there.
                // The equipment-size piece goes first, so the inventory has room for the rest.
                var present = inventory.GetInsideWorldObjects().Select(w => w.GetGroup().GetId()).ToList();
                var missing = targets.Where(t => max ? !present.Contains(t.GetId()) : !present.Any(p => FamilyOf(p) == FamilyOf(t.GetId())))
                    .OrderBy(t => FamilyOf(t.GetId()) == "VehicleEquipmentSize" ? 0 : 1).ThenBy(t => t.GetId()).ToList();

                var added = new List<string>();
                var failed = new List<string>();
                foreach (var item in missing)
                {
                    var done = false;
                    var ok = false;
                    try
                    {
                        InventoriesHandler.Instance.AddItemToInventory(item, inventory, (success, woId) =>
                        {
                            ok = success;
                            done = true;
                        });
                    }
                    catch (Exception e)
                    {
                        ModLog.Note("/vehicle upgrade: adding " + item.GetId() + " failed: " + e.GetType().Name + ": " + e.Message);
                        failed.Add(item.GetId());
                        continue;
                    }

                    var until = Time.realtimeSinceStartup + WaitForItemSeconds;
                    while (!done && Time.realtimeSinceStartup < until)
                        yield return null;

                    if (ok)
                        added.Add(item.GetId());
                    else
                        failed.Add(item.GetId());

                    // let the rover take the piece in (the equipment-size piece enlarges the inventory) before the next
                    yield return new WaitForSecondsRealtime(0.25f);
                }

                var summary = (max ? "max" : "min") + ": " + (removed.Count > 0 ? "removed " + string.Join(", ", removed.ToArray()) + "; " : "")
                    + (added.Count > 0 ? "added " + string.Join(", ", added.ToArray()) : "nothing to add");
                ModLog.Note("/vehicle upgrade " + summary + (failed.Count > 0 ? "; could not add " + string.Join(", ", failed.ToArray()) : "") + " (rover #" + vehicle.GetId() + ").");
                print("Rover " + summary + "." + (failed.Count > 0 ? " Could not add: " + string.Join(", ", failed.ToArray()) + " (no room?)." : ""));
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
