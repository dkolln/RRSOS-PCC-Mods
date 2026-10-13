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
        public List<string> Fuses = new List<string>();   // one optimizer each
        public string Fill = "";        // what each machine's inventory is filled with; "" for empty
    }

    /// <summary>
    /// Builds a booster in the running world, the way the dashboard writes one into a save: eight machines (stacked at the beacon, or in a 3 by 3 ring around the optimizers
    /// standing on it), and an optimizer for each fuse, full of that fuse. Anything already standing in its place counts and is not added again. Everything is made through the
    /// game's own calls (Placement.PlaceAt, then AddItemToInventory), free of cost. When all of it is built the beacon is taken away, so it is off the screen's list.
    /// </summary>
    internal static class BoosterBuilder
    {
        private static readonly (int Dx, int Dz)[] RingCells = { (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1) };

        public sealed class Outcome
        {
            public int Machines, Optimizers, Fuses, Items, Found;
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
            if (kind.Ring)
            {
                foreach (var (dx, dz) in RingCells)
                    spots.Add(new Vector3(at.x + (float)(dx * kind.Spacing), at.y, at.z + (float)(dz * kind.Spacing)));
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
                if (kind.Ring)
                    optimizerSpots.Add(new Vector3(at.x, at.y, at.z + shift));
                else
                    optimizerSpots.Add(new Vector3(at.x + dirX * (float)BoosterKinds.OptimizerDistance + Mathf.Abs(dirZ) * shift, at.y, at.z + dirZ * (float)BoosterKinds.OptimizerDistance + Mathf.Abs(dirX) * shift));
            }

            var all = WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() != null).ToList();
            bool Standing(string groupId, Vector3 p) => all.Any(w => w.GetGroup().GetId() == groupId && Mathf.Abs(w.GetPosition().x - p.x) < 0.3f && Mathf.Abs(w.GetPosition().z - p.z) < 0.3f && Mathf.Abs(w.GetPosition().y - at.y) < 0.5f);

            // machines: a ring cell that has one is kept; a stack counts what stands at its spot
            var toAdd = new List<Vector3>();
            if (kind.Ring)
            {
                foreach (var spot in spots)
                {
                    if (Standing(request.Machine, spot))
                        outcome.Found++;
                    else
                        toAdd.Add(spot);
                }
            }
            else
            {
                outcome.Found = all.Count(w => w.GetGroup().GetId() == request.Machine && Mathf.Abs(w.GetPosition().x - at.x) < 0.3f && Mathf.Abs(w.GetPosition().z - at.z) < 0.3f && Mathf.Abs(w.GetPosition().y - at.y) < 0.5f);
                for (var i = outcome.Found; i < BoosterKinds.Count; i++)
                    toAdd.Add(at);
            }

            var total = toAdd.Count + fuses.Count;
            var done = 0;

            // the machines
            foreach (var spot in toAdd)
            {
                progress("Placing " + request.Machine + " " + (++done) + " of " + total + "...");
                WorldObject machine = null;
                try
                {
                    machine = Placement.PlaceAt(request.Machine, spot, Quaternion.identity, planetHash);
                }
                catch (Exception e)
                {
                    outcome.Problems.Add("Could not place " + request.Machine + ": " + e.Message);
                    break;
                }

                outcome.Machines++;
                if (kind.Grown)
                    machine.SetGrowth(100f);

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

                for (var s = 0; s < inventory.GetSize(); s++)
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

            ModLog.Note("Booster " + kind.Key + " at " + at + ": " + outcome.Machines + " machines (" + outcome.Found + " already there), " + outcome.Optimizers + " optimizers, " + outcome.Fuses + " fuses, " + outcome.Items + " items."
                + (outcome.Ok ? "" : " Problems: " + string.Join(" ", outcome.Problems.ToArray())));
        }

        private sealed class Result
        {
            public bool Ok;
        }

        // one item into an inventory through the game's own call, waited for
        private static IEnumerator Add(Inventory inventory, string groupId, Result result)
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
