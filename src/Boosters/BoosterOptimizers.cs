using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Boosters
{
    /// <summary>
    /// Add Optimizers: finds the boosters already standing in the world (eight machines stacked on one spot, or eight in a ring) that have no optimizer by them, and gives each the
    /// best optimizer that is unlocked, one for each of the kind's fuses, full of that fuse. Nothing to choose: no beacon, no config. A booster that has an optimizer near it is left as it is.
    /// The beacon is gone by then, so the way a stack faces is not known: its optimizers go 3 m along +X from it, side by side across Z.
    /// </summary>
    internal static class BoosterOptimizers
    {
        private const float HasOptimizerWithin = 6f;   // an optimizer this close (along the ground) to a booster's middle counts as its own

        public sealed class Outcome
        {
            public int Boosters, Optimizers, Fuses, Already;
            public readonly List<string> Problems = new List<string>();
        }

        private sealed class Grouping
        {
            public Kind Kind;
            public Vector3 Middle;
            public bool Ring;
        }

        private static bool Unlocked(string groupId)
        {
            var group = GroupsHandler.GetGroupViaId(groupId);
            if (group == null)
                return false;

            try
            {
                return group.GetUnlockingInfos().GetIsUnlocked(true) || group.GetIsGloballyUnlocked();
            }
            catch
            {
                return false;
            }
        }

        public static IEnumerator Run(Outcome outcome, Action<string> progress)
        {
            if (!WorldState.Ready(out var planetHash, out _))
            {
                outcome.Problems.Add("The world is not ready yet.");
                yield break;
            }

            var tier = BoosterKinds.Optimizers.Select(o => o.GId).LastOrDefault(Unlocked);
            if (tier == null)
            {
                outcome.Problems.Add("No optimizer is unlocked yet.");
                yield break;
            }

            var all = WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                .Where(w => w != null && w.GetGroup() != null && w.GetPlanetHash() == planetHash && w.GetPosition().sqrMagnitude > 0f).ToList();
            var optimizers = all.Where(w => w.GetGroup().GetId().StartsWith("Optimizer", StringComparison.Ordinal)).Select(w => w.GetPosition()).ToList();

            var found = new List<Grouping>();
            foreach (var kind in BoosterKinds.All.Where(k => k.DefaultFuses.Length > 0 && k.Slots == null))
            {
                foreach (var machineGroup in kind.Machines.Select(m => m.GId))
                {
                    var machines = all.Where(w => w.GetGroup().GetId() == machineGroup).Select(w => w.GetPosition()).ToList();
                    if (kind.Ring)
                    {
                        foreach (var corner in machines)
                        {
                            var middle = new Vector3(corner.x + (float)kind.Spacing, corner.y - (float)kind.YOffset, corner.z + (float)kind.Spacing);
                            var full = BoosterBuilder.RingCells.All(c =>
                            {
                                var cell = new Vector3(middle.x + (float)(c.Dx * kind.Spacing), middle.y + (float)kind.YOffset, middle.z + (float)(c.Dz * kind.Spacing));
                                return machines.Any(m => Mathf.Abs(m.x - cell.x) < 0.6f && Mathf.Abs(m.z - cell.z) < 0.6f && Mathf.Abs(m.y - cell.y) < 0.8f);
                            });
                            if (full)
                                found.Add(new Grouping { Kind = kind, Middle = middle, Ring = true });
                        }
                    }
                    else
                    {
                        foreach (var stack in machines.GroupBy(m => new Vector3(Mathf.Round(m.x * 2f) / 2f, Mathf.Round(m.y), Mathf.Round(m.z * 2f) / 2f)).Where(g => g.Count() >= BoosterKinds.Count))
                            found.Add(new Grouping { Kind = kind, Middle = stack.First(), Ring = false });
                    }
                }
            }

            foreach (var group in found)
            {
                if (optimizers.Any(o => Mathf.Abs(o.x - group.Middle.x) < HasOptimizerWithin && Mathf.Abs(o.z - group.Middle.z) < HasOptimizerWithin && Mathf.Abs(o.y - group.Middle.y) < 3f))
                {
                    outcome.Already++;
                    continue;
                }

                var fuses = group.Kind.DefaultFuses.Distinct().ToList();
                var placed = 0;
                for (var i = 0; i < fuses.Count; i++)
                {
                    progress("Adding the " + fuses[i] + " optimizer to a " + group.Kind.Name + " booster...");
                    var shift = (float)((i - (fuses.Count - 1) / 2.0) * BoosterKinds.OptimizerGap);
                    var spot = group.Ring ? new Vector3(group.Middle.x, group.Middle.y, group.Middle.z + shift)
                        : new Vector3(group.Middle.x + (float)BoosterKinds.OptimizerDistance, group.Middle.y, group.Middle.z + shift);

                    WorldObject optimizer;
                    try
                    {
                        optimizer = Placement.PlaceAt(tier, spot, Quaternion.identity, planetHash);
                    }
                    catch (Exception e)
                    {
                        outcome.Problems.Add("Could not place " + tier + ": " + e.Message);
                        break;
                    }

                    placed++;
                    outcome.Optimizers++;
                    var inventory = optimizer.GetLinkedInventoryId() > 0 ? InventoriesHandler.Instance.GetInventoryById(optimizer.GetLinkedInventoryId()) : null;
                    if (inventory == null)
                    {
                        outcome.Problems.Add("An optimizer has no inventory for its fuse.");
                        continue;
                    }

                    for (var s = 0; s < inventory.GetSize(); s++)
                    {
                        var result = new BoosterBuilder.Result();
                        yield return BoosterBuilder.Add(inventory, fuses[i], result);
                        if (!result.Ok)
                        {
                            outcome.Problems.Add("Could not put " + fuses[i] + " in an optimizer.");
                            break;
                        }

                        outcome.Fuses++;
                    }

                    yield return null;
                }

                if (placed > 0)
                    outcome.Boosters++;
            }

            ModLog.Note("Add optimizers: " + found.Count + " boosters found, " + outcome.Already + " already had one, " + outcome.Boosters + " given " + outcome.Optimizers + " " + tier + " (" + outcome.Fuses + " fuses)."
                + (outcome.Problems.Count == 0 ? "" : " Problems: " + string.Join(" ", outcome.Problems.ToArray())));
        }
    }
}
