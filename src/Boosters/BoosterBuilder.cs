using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Boosters
{
    /// <summary>What the screen asks for.</summary>
    internal sealed class Request
    {
        public WorldObject Beacon;
        public Kind Kind;
        public string Machine;          // the machine's group id
        public string Optimizer;        // Optimizer1 or Optimizer2; null for none
        public List<string> Fuses = new List<string>();   // one optimizer each, the kind's own fuses
        public bool Prefill = true;     // the optimizers are filled with their fuses; when false they are placed empty
        public string Fill = "";        // what each machine's inventory is filled with; "" for empty
    }

    /// <summary>
    /// Builds a booster in the running world, the way the dashboard writes one into a save: eight machines (stacked at the beacon, or in a 3 by 3 ring around the optimizers
    /// standing on it), and an optimizer for each fuse, full of that fuse. Anything already standing in its place counts and is not added again. Everything is made through the
    /// game's own calls (Placement.PlaceAt, then AddItemToInventory), free of cost. When all of it is built the beacon is taken away, so it is off the screen's list.
    /// </summary>
    internal static class BoosterBuilder
    {
        internal static readonly (int Dx, int Dz)[] RingCells = { (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1) };

        // the drone pad, in metres from the beacon (see Build): foundation centres, station places
        private static readonly float[] PadX = { 6.3f, 12.3f, 18.3f, 24.3f }, PadZ = { -6f, 0f, 6f, 12f };
        private static readonly float[] StationX = { 9.3f, 21.3f };
        private const float StationZ = 8.1f, PadDrop = -2.519f;

        public sealed class Outcome
        {
            public int Foundations, Machines, Optimizers, Fuses, Items, Found;
            public readonly List<string> Problems = new List<string>();
            public bool Ok => Problems.Count == 0;
        }

        public static IEnumerator Build(Request request, Outcome outcome, Action<string> progress)
        {
            var beacon = request.Beacon;
            var kind = request.Kind;
            var planetHash = beacon.GetPlanetHash();
            var at = beacon.GetPosition();

            // the way the beacon points, as one of the four directions
            var forward = beacon.GetRotation() * Vector3.forward;
            float dirX = 0f, dirZ = 0f;
            if (Mathf.Abs(forward.x) >= Mathf.Abs(forward.z))
                dirX = Mathf.Sign(forward.x);
            else
                dirZ = Mathf.Sign(forward.z);

            var fuses = request.Optimizer == null ? new List<string>() : request.Fuses.Distinct().ToList();

            // where the machines go
            var spots = new List<Vector3>();
            var turns = new List<Quaternion>();
            var pad = new List<Vector3>();
            var parts = new List<KeyValuePair<Slot, KeyValuePair<Vector3, Quaternion>>>();
            Vector3? layoutOptimizer = null;
            if (kind.Sides)
            {
                // The pad is laid out as the owner built it: a 4 x 4 foundation lying behind the beacon (the way opposite to where it points), a drone station at each end of its two long
                // sides, each turned to look across the pad. Measured from a save in which the beacon looked down -X; any other way the beacon points turns the whole layout with it.
                var yaw = Mathf.Round((beacon.GetRotation().eulerAngles.y - 270f) / 90f) * 90f;
                var turn = Quaternion.Euler(0f, yaw, 0f);
                Vector3 Local(float x, float z) => at + turn * new Vector3(x, 0f, z);

                foreach (var x in PadX)
                    foreach (var z in PadZ)
                    {
                        var p = Local(x, z);
                        pad.Add(new Vector3(p.x, at.y + PadDrop, p.z));
                    }

                foreach (var x in StationX)
                    foreach (var z in new[] { -StationZ, StationZ })
                    {
                        spots.Add(Local(x, z));
                        turns.Add(turn * (z > 0 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity));
                    }
            }
            else if (kind.Slots != null)
            {
                // a fixed layout, measured with the beacon looking down -X and the layout on the other side of it; turned with the beacon to the nearest quarter
                var yaw = Mathf.Round((beacon.GetRotation().eulerAngles.y - 270f) / 90f) * 90f;
                var turn = Quaternion.Euler(0f, yaw, 0f);
                foreach (var slot in kind.Slots)
                {
                    spots.Add(at + turn * new Vector3(slot.X, slot.Y, slot.Z));
                    turns.Add(turn * Quaternion.Euler(0f, slot.Yaw, 0f));
                }

                foreach (var part in kind.Parts ?? new Slot[0])
                    parts.Add(new KeyValuePair<Slot, KeyValuePair<Vector3, Quaternion>>(part, new KeyValuePair<Vector3, Quaternion>(at + turn * new Vector3(part.X, part.Y, part.Z), turn * Quaternion.Euler(0f, part.Yaw, 0f))));

                if (kind.OptimizerAt != null)
                    layoutOptimizer = at + turn * new Vector3(kind.OptimizerAt.X, kind.OptimizerAt.Y, kind.OptimizerAt.Z);
            }
            else if (kind.Ring)
            {
                foreach (var (dx, dz) in RingCells)
                    spots.Add(new Vector3(at.x + (float)(dx * kind.Spacing), at.y + (float)kind.YOffset, at.z + (float)(dz * kind.Spacing)));
            }
            else
            {
                for (var i = 0; i < BoosterKinds.Count; i++)
                    spots.Add(at);
            }

            // where the optimizers go: on the beacon side by side for a ring, a little way along the beacon's way for a stack
            var optimizerSpots = new List<Vector3>();
            for (var i = 0; i < fuses.Count; i++)
            {
                var shift = (float)((i - (fuses.Count - 1) / 2.0) * BoosterKinds.OptimizerGap);
                if (layoutOptimizer.HasValue)
                    optimizerSpots.Add(layoutOptimizer.Value + new Vector3(0f, 0f, shift));
                else if (kind.Ring)
                    optimizerSpots.Add(new Vector3(at.x, at.y, at.z + shift));
                else
                    optimizerSpots.Add(new Vector3(at.x + dirX * (float)BoosterKinds.OptimizerDistance + Mathf.Abs(dirZ) * shift, at.y, at.z + dirZ * (float)BoosterKinds.OptimizerDistance + Mathf.Abs(dirX) * shift));
            }

            var all = WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() != null).ToList();
            bool Standing(string groupId, Vector3 p) => all.Any(w => w.GetGroup().GetId() == groupId && Mathf.Abs(w.GetPosition().x - p.x) < 0.3f && Mathf.Abs(w.GetPosition().z - p.z) < 0.3f && Mathf.Abs(w.GetPosition().y - at.y) < 0.8f);

            // machines: a ring cell that has one is kept; a stack counts what stands at its spot
            var toAdd = new List<Vector3>();
            var toAddTurn = new List<Quaternion>();
            var toAddSlot = new List<int>();
            if (kind.Ring || kind.Sides || kind.Slots != null)
            {
                for (var n = 0; n < spots.Count; n++)
                {
                    if (Standing(request.Machine, spots[n]))
                        outcome.Found++;
                    else
                    {
                        toAdd.Add(spots[n]);
                        toAddSlot.Add(n);
                        toAddTurn.Add(kind.Sides || kind.Slots != null ? turns[n] : Quaternion.identity);
                    }
                }
            }
            else
            {
                outcome.Found = all.Count(w => w.GetGroup().GetId() == request.Machine && Mathf.Abs(w.GetPosition().x - at.x) < 0.3f && Mathf.Abs(w.GetPosition().z - at.z) < 0.3f && Mathf.Abs(w.GetPosition().y - at.y) < 0.5f);
                for (var i = outcome.Found; i < BoosterKinds.Count; i++)
                {
                    toAdd.Add(at);
                    toAddSlot.Add(i);
                    toAddTurn.Add(Quaternion.identity);
                }
            }

            // a kind that floats (fish farms on water) stands its optimizers on a foundation under the beacon: placed first, its top at the beacon's height
            if (kind.Foundation)
            {
                var under = new Vector3(at.x, at.y + PadDrop, at.z);
                if (!all.Any(w => w.GetGroup().GetId() == "Foundation" && Mathf.Abs(w.GetPosition().x - under.x) < 0.5f && Mathf.Abs(w.GetPosition().z - under.z) < 0.5f && Mathf.Abs(w.GetPosition().y - under.y) < 1f))
                {
                    progress("Placing the foundation...");
                    try
                    {
                        Placement.PlaceAt("Foundation", under, Quaternion.identity, planetHash);
                        outcome.Foundations++;
                    }
                    catch (Exception e)
                    {
                        outcome.Problems.Add("Could not place the foundation: " + e.Message);
                    }
                }
            }

            // what a fixed layout stands in (foundations, a pod): made first, skipping what is already there
            foreach (var part in parts)
            {
                var where = part.Value.Key;
                if (all.Any(w => w.GetGroup().GetId() == part.Key.Group && Mathf.Abs(w.GetPosition().x - where.x) < 0.5f && Mathf.Abs(w.GetPosition().z - where.z) < 0.5f && Mathf.Abs(w.GetPosition().y - where.y) < 1f))
                    continue;

                progress("Placing the " + part.Key.Group + "...");
                try
                {
                    Placement.PlaceAt(part.Key.Group, where, part.Value.Value, planetHash, string.IsNullOrEmpty(part.Key.Panels) ? null : DataTreatments.StringToIntList(part.Key.Panels).ToArray());
                    outcome.Foundations++;
                }
                catch (Exception e)
                {
                    outcome.Problems.Add("Could not place the " + part.Key.Group + ": " + e.Message);
                    break;
                }

                yield return null;
            }

            var total = toAdd.Count + fuses.Count + pad.Count;
            var done = 0;

            // the foundations under a pad, before the machines stand on them
            foreach (var spot in pad)
            {
                progress("Placing foundation " + (++done) + " of " + total + "...");
                if (all.Any(w => w.GetGroup().GetId() == "Foundation" && Mathf.Abs(w.GetPosition().x - spot.x) < 0.3f && Mathf.Abs(w.GetPosition().z - spot.z) < 0.3f))
                    continue;

                try
                {
                    Placement.PlaceAt("Foundation", spot, Quaternion.identity, planetHash);
                    outcome.Foundations++;
                }
                catch (Exception e)
                {
                    outcome.Problems.Add("Could not place a foundation: " + e.Message);
                    break;
                }

                yield return null;
            }

            // the machines
            for (var n = 0; n < toAdd.Count; n++)
            {
                var spot = toAdd[n];
                progress("Placing " + request.Machine + " " + (++done) + " of " + total + "...");
                WorldObject machine = null;
                try
                {
                    machine = Placement.PlaceAt(request.Machine, spot, toAddTurn[n], planetHash);
                }
                catch (Exception e)
                {
                    outcome.Problems.Add("Could not place " + request.Machine + ": " + e.Message);
                    break;
                }

                outcome.Machines++;
                if (kind.Supply != null)
                    ModLog.LogOnce("boosters-linked-" + request.Machine, "Boosters: a new " + request.Machine + " has linked groups: " + (machine.HasLinkedGroups() ? string.Join(", ", machine.GetLinkedGroups().Where(g => g != null).Select(g => g.GetId()).ToArray()) : "none") + ".");
                if (kind.Grown)
                    machine.SetGrowth(100f);

                // The seed goes in the machine's own inventory: a grower grows from the seed in it and fills its second inventory (the harvest) with the plants itself.
                if (kind.SlotItems != null && toAddSlot[n] < kind.SlotItems.Length)
                {
                    var own = machine.GetLinkedInventoryId() > 0 ? InventoriesHandler.Instance.GetInventoryById(machine.GetLinkedInventoryId()) : null;
                    if (own == null)
                        outcome.Problems.Add("The " + request.Machine + " has no seed slot.");
                    else
                    {
                        var seeded = new Result();
                        var seedGroup = GroupsHandler.GetGroupViaId(kind.SlotItems[toAddSlot[n]]);
                        yield return Add(own, kind.SlotItems[toAddSlot[n]], seeded);

                        // A new grower can answer the add late (its inventory is still being set up) although the seed does arrive: look in the inventory itself before calling it a failure.
                        var lookUntil = Time.realtimeSinceStartup + 6f;
                        while (!seeded.Ok && seedGroup != null && Time.realtimeSinceStartup < lookUntil)
                        {
                            seeded.Ok = own.ContainGroup(seedGroup);
                            if (!seeded.Ok)
                                yield return null;
                        }

                        if (!seeded.Ok)
                            outcome.Problems.Add("Could not put " + kind.SlotItems[toAddSlot[n]] + " in a " + request.Machine + ".");
                        else
                            outcome.Items++;
                    }
                }

                var inventory = machine.GetLinkedInventoryId() > 0 ? InventoriesHandler.Instance.GetInventoryById(machine.GetLinkedInventoryId()) : null;
                if (inventory != null)
                {
                    SetSupply(inventory, kind, outcome);
                    if (!string.IsNullOrEmpty(request.Fill))
                    {
                        for (var i = 0; i < inventory.GetSize(); i++)
                        {
                            var result = new Result();
                            yield return Add(inventory, request.Fill, result);
                            if (!result.Ok)
                            {
                                outcome.Problems.Add("Could not put " + request.Fill + " in a " + request.Machine + ".");
                                break;
                            }

                            outcome.Items++;
                        }
                    }
                }

                yield return null;
            }

            // the optimizers, each holding its fuse in every slot
            for (var i = 0; i < fuses.Count && outcome.Ok; i++)
            {
                progress("Placing the " + fuses[i] + " optimizer...");
                if (Standing(request.Optimizer, optimizerSpots[i]))
                {
                    outcome.Found++;
                    continue;
                }

                WorldObject optimizer;
                try
                {
                    optimizer = Placement.PlaceAt(request.Optimizer, optimizerSpots[i], Quaternion.identity, planetHash);
                }
                catch (Exception e)
                {
                    outcome.Problems.Add("Could not place " + request.Optimizer + ": " + e.Message);
                    break;
                }

                outcome.Optimizers++;
                var inventory = optimizer.GetLinkedInventoryId() > 0 ? InventoriesHandler.Instance.GetInventoryById(optimizer.GetLinkedInventoryId()) : null;
                if (inventory == null)
                {
                    outcome.Problems.Add("The optimizer has no inventory for its fuses.");
                    break;
                }

                for (var s = 0; request.Prefill && s < inventory.GetSize(); s++)
                {
                    var result = new Result();
                    yield return Add(inventory, fuses[i], result);
                    if (!result.Ok)
                    {
                        outcome.Problems.Add("Could not put " + fuses[i] + " in the optimizer.");
                        break;
                    }

                    outcome.Fuses++;
                }
            }

            if (outcome.Ok)
            {
                try
                {
                    WorldObjectsHandler.Instance.DestroyWorldObject(beacon, true);
                }
                catch (Exception e)
                {
                    outcome.Problems.Add("Built, but the beacon could not be removed: " + e.Message);
                }
            }

            ModLog.Note("Booster " + kind.Key + " at " + at + ": " + outcome.Foundations + " foundations, " + outcome.Machines + " machines (" + outcome.Found + " already there), " + outcome.Optimizers + " optimizers, " + outcome.Fuses + " fuses, " + outcome.Items + " items."
                + (outcome.Ok ? "" : " Problems: " + string.Join(" ", outcome.Problems.ToArray())));
        }

        internal sealed class Result
        {
            public bool Ok;
        }

        // one item into an inventory through the game's own call, waited for
        internal static IEnumerator Add(Inventory inventory, string groupId, Result result)
        {
            var group = GroupsHandler.GetGroupViaId(groupId);
            if (group == null)
            {
                ModLog.Note("Boosters: the game has no group " + groupId + ".");
                yield break;
            }

            var done = false;
            try
            {
                InventoriesHandler.Instance.AddItemToInventory(group, inventory, (success, id) =>
                {
                    result.Ok = success;
                    done = true;
                });
            }
            catch (Exception e)
            {
                ModLog.Note("Boosters: adding " + groupId + " failed: " + e.GetType().Name + ": " + e.Message);
                yield break;
            }

            var until = Time.realtimeSinceStartup + 3f;
            while (!done && Time.realtimeSinceStartup < until)
                yield return null;
        }

        // what the machine's inventory offers the drones (a beehive: honey and bee larvae; an ecosystem: every larva and egg)
        private static void SetSupply(Inventory inventory, Kind kind, Outcome outcome)
        {
            if (kind.Supply == null && !kind.SupplyEverything)
                return;

            try
            {
                var groups = new HashSet<Group>();
                if (kind.SupplyEverything)
                {
                    foreach (var group in GroupsHandler.GetAllGroups())
                        if (group.GetId().EndsWith("Larvae", StringComparison.Ordinal) || group.GetId().EndsWith("Eggs", StringComparison.Ordinal))
                            groups.Add(group);
                }
                else
                {
                    foreach (var id in kind.Supply.Split(','))
                    {
                        var group = GroupsHandler.GetGroupViaId(id.Trim());
                        if (group != null)
                            groups.Add(group);
                    }
                }

                var entity = inventory.GetLogisticEntity();
                if (entity == null)
                {
                    ModLog.Note("Boosters: the machine's inventory has no logistic entity; the drone supply is left as it is.");
                    return;
                }

                entity.SetSupplyGroups(groups);
                entity.SetDemandGroups(new HashSet<Group>());
            }
            catch (Exception e)
            {
                ModLog.LogOnce("boosters-supply", "Boosters: could not set the drone supply: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
