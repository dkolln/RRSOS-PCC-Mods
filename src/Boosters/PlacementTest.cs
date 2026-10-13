using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Boosters
{
    /// <summary>
    /// A one-off test of what the booster build relies on, run once when RunPlacementTest is true: it places an optimizer with heat fuses, two stacked heaters, a tree spreader, a beehive
    /// and a butterfly farm a few metres in front of the player, reports what the game made of each (own inventories, sizes, whether the optimizer counts the heaters), and takes
    /// every test piece away again after ten seconds. The report is boosters-placement.txt.
    /// </summary>
    internal sealed class PlacementTest : MonoBehaviour
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly StringBuilder _text = new StringBuilder();
        private readonly List<WorldObject> _made = new List<WorldObject>();
        private bool _done;

        private IEnumerator Start()
        {
            while (!_done)
            {
                yield return new WaitForSecondsRealtime(3f);

                if (!Plugin.Enabled.Value || !Plugin.RunPlacementTest.Value || !WorldState.Ready(out var planetHash, out _))
                    continue;

                _done = true;
                _text.AppendLine("Boosters placement test, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                yield return Run(planetHash);
                File.WriteAllText(ModLog.FileIn("boosters-placement.txt"), _text.ToString());
                ModLog.Note("Placement test: wrote boosters-placement.txt. Set RunPlacementTest back to false.");
            }
        }

        private IEnumerator Run(int planetHash)
        {
            var player = Managers.GetManager<PlayersManager>().GetActivePlayerController();
            var at = player.transform.position + player.transform.forward * 8f;
            var side = player.transform.right * 4f;
            _text.AppendLine("player " + player.transform.position + ", test spot " + at);

            var optimizer = Place("Optimizer2", at, planetHash);
            var inventory = optimizer == null ? null : InventoryOf(optimizer, "optimizer");

            // the fuses
            if (inventory != null)
            {
                for (var i = 0; i < 3; i++)
                {
                    var done = false;
                    var ok = false;
                    try
                    {
                        InventoriesHandler.Instance.AddItemToInventory(GroupsHandler.GetGroupViaId("FuseHeat1"), inventory, (success, id) => { ok = success; done = true; });
                    }
                    catch (Exception e)
                    {
                        _text.AppendLine("fuse " + i + ": " + e.GetType().Name + ": " + e.Message);
                        break;
                    }

                    var until = Time.realtimeSinceStartup + 3f;
                    while (!done && Time.realtimeSinceStartup < until)
                        yield return null;
                    _text.AppendLine("fuse " + i + ": added " + ok + " (done " + done + ")");
                }

                _text.AppendLine("optimizer now holds: " + string.Join(", ", inventory.GetInsideWorldObjects().Select(w => w.GetGroup().GetId()).ToArray()));
            }

            var heaterA = Place("Heater5", at + side, planetHash);
            var heaterB = Place("Heater5", at + side, planetHash);
            var spreader = Place("TreeSpreader0", at + side * 2f, planetHash);
            if (spreader != null)
            {
                InventoryOf(spreader, "tree spreader");
                try { spreader.SetGrowth(100f); _text.AppendLine("tree spreader growth set to " + spreader.GetGrowth()); } catch (Exception e) { _text.AppendLine("growth: " + e.Message); }
            }

            var hive = Place("Beehive2", at + side * 3f, planetHash);
            if (hive != null)
            {
                InventoryOf(hive, "beehive");
                LogisticsOf(hive);
            }

            var farm = Place("ButterflyFarm2", at + side * 4f, planetHash);
            if (farm != null)
                InventoryOf(farm, "butterfly farm");

            yield return new WaitForSecondsRealtime(5f);

            // does the optimizer count the two heaters?
            if (optimizer != null && optimizer.GetGameObject() != null)
            {
                var machine = optimizer.GetGameObject().GetComponentInChildren<MachineOptimizer>(true);
                _text.AppendLine(machine == null
                    ? "no MachineOptimizer on the optimizer's object; components: " + string.Join(", ", optimizer.GetGameObject().GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct().ToArray())
                    : "MachineOptimizer: range " + machine.range + ", per fuse " + machine.maxWorldObjectPerFuse + ", machines affected now " + machine.GetNumberOfUniquesMachinesCurrentlyAffected() + " (two heaters stand in range)");
            }

            _text.AppendLine("heaters: " + (heaterA != null) + " " + (heaterB != null));

            // clean up
            yield return new WaitForSecondsRealtime(5f);
            foreach (var wo in _made)
            {
                try
                {
                    var linked = wo.GetLinkedInventoryId() > 0 ? InventoriesHandler.Instance.GetInventoryById(wo.GetLinkedInventoryId()) : null;
                    if (linked != null && linked.GetInsideWorldObjects().Count > 0)
                        InventoriesHandler.Instance.RemoveAndDestroyAllItemsFromInventory(linked);
                    WorldObjectsHandler.Instance.DestroyWorldObject(wo, true);
                }
                catch (Exception e)
                {
                    _text.AppendLine("clean-up of " + wo.GetGroup().GetId() + " #" + wo.GetId() + ": " + e.GetType().Name + ": " + e.Message);
                }
            }

            _text.AppendLine("removed " + _made.Count + " test pieces.");
        }

        private WorldObject Place(string groupId, Vector3 pos, int planetHash)
        {
            try
            {
                var wo = Placement.PlaceAt(groupId, pos, Quaternion.identity, planetHash);
                _made.Add(wo);
                _text.AppendLine("placed " + groupId + " #" + wo.GetId() + " at " + wo.GetPosition() + "; game object: " + (wo.GetGameObject() != null));
                return wo;
            }
            catch (Exception e)
            {
                _text.AppendLine("could not place " + groupId + ": " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        private Inventory InventoryOf(WorldObject wo, string label)
        {
            var id = wo.GetLinkedInventoryId();
            var inventory = id > 0 ? InventoriesHandler.Instance.GetInventoryById(id) : null;
            _text.AppendLine("  " + label + ": linked inventory id " + id + (inventory == null ? " (none)" : ", size " + inventory.GetSize()));
            return inventory;
        }

        // what the game keeps for drone supply on a placed machine
        private void LogisticsOf(WorldObject wo)
        {
            var go = wo.GetGameObject();
            if (go == null)
                return;

            foreach (var component in go.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name.IndexOf("Logistic", StringComparison.OrdinalIgnoreCase) >= 0))
                _text.AppendLine("  logistic component " + component.GetType().Name + " on " + component.gameObject.name + ": " + string.Join("; ", component.GetType().GetFields(All).Select(f => f.Name + ":" + f.FieldType.Name).ToArray()));

            var inventory = wo.GetLinkedInventoryId() > 0 ? InventoriesHandler.Instance.GetInventoryById(wo.GetLinkedInventoryId()) : null;
            if (inventory != null)
            {
                foreach (var method in inventory.GetType().GetMethods(All | BindingFlags.Static).Where(m => m.Name.IndexOf("Logistic", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Supply", StringComparison.OrdinalIgnoreCase) >= 0))
                    _text.AppendLine("  Inventory." + method.Name);
                foreach (var field in inventory.GetType().GetFields(All).Where(f => f.Name.IndexOf("Logistic", StringComparison.OrdinalIgnoreCase) >= 0))
                    _text.AppendLine("  Inventory field " + field.Name + ": " + field.FieldType.Name);
            }
        }
    }
}
