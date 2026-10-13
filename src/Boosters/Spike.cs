using System;
using System.Collections;
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
    /// Read-only discovery, once per start when a world is loaded: writes boosters-spike.txt with what the booster build needs to know. Nothing in the game is changed.
    /// </summary>
    internal sealed class Spike : MonoBehaviour
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private bool _done;

        private IEnumerator Start()
        {
            while (!_done)
            {
                yield return new WaitForSecondsRealtime(3f);

                if (!Plugin.Enabled.Value || !WorldState.Ready(out _, out _))
                    continue;

                _done = true;
                try
                {
                    var text = new StringBuilder();
                    text.AppendLine("Boosters spike, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    Groups(text);
                    Beacons(text);
                    Placed(text);
                    UiTypes(text);
                    File.WriteAllText(ModLog.FileIn("boosters-spike.txt"), text.ToString());
                    ModLog.Note("Spike: wrote boosters-spike.txt.");
                }
                catch (Exception e)
                {
                    ModLog.Note("Spike: " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace);
                }
            }
        }

        // Every group whose id has to do with boosters or the screens, with its kind and unlock facts.
        private static void Groups(StringBuilder text)
        {
            text.AppendLine().AppendLine("== Groups (Spreader, Optimizer, Fuse, Screen, Beacon, Heater, Drill, Farm, Hive, Ecosystem, Amphib, Grass)");
            string[] words = { "spreader", "optimizer", "fuse", "screen", "beacon", "heater", "drill", "farm", "hive", "ecosystem", "amphib", "grass", "aluminium", "alloy" };
            foreach (var group in GroupsHandler.GetAllGroups().OrderBy(g => g.GetId(), StringComparer.Ordinal))
            {
                var id = group.GetId();
                if (!words.Any(w => id.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                string name;
                try { name = Readable.GetGroupName(group); } catch { name = "?"; }
                var line = id + " | " + name + " | " + group.GetType().Name;
                try { line += " | unlocked: " + (group.GetUnlockingInfos().GetIsUnlocked(true) || group.GetIsGloballyUnlocked()); } catch { }
                text.AppendLine(line);
            }
        }

        private static void Beacons(StringBuilder text)
        {
            text.AppendLine().AppendLine("== Beacons in the world (text | position | rotation | forward | planet)");
            foreach (var wo in WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() != null && w.GetGroup().GetId() == "Beacon"))
                text.AppendLine("\"" + wo.GetText() + "\" | " + wo.GetPosition() + " | " + wo.GetRotation() + " | " + (wo.GetRotation() * Vector3.forward) + " | " + wo.GetPlanetHash());
        }

        // What an optimizer, a spreader and the machines already placed look like, and the Energy screen's own parts.
        private static void Placed(StringBuilder text)
        {
            text.AppendLine().AppendLine("== Placed objects of interest (first 3 of each group)");
            var seen = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var wo in WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() != null))
            {
                var id = wo.GetGroup().GetId();
                if (!(id.StartsWith("Optimizer") || id.Contains("Spreader") || id.StartsWith("Screen") || id.StartsWith("Heater") || id.StartsWith("Drill") || id.Contains("Farm") || id.Contains("Hive")))
                    continue;
                seen.TryGetValue(id, out var count);
                if (count >= 3)
                    continue;
                seen[id] = count + 1;

                text.AppendLine(id + " #" + wo.GetId() + " | pos " + wo.GetPosition() + " | rot " + wo.GetRotation() + " | text \"" + wo.GetText() + "\" | growth " + wo.GetGrowth() + " | linkedInv " + wo.GetLinkedInventoryId());
                var inventory = wo.GetLinkedInventoryId() > 0 ? InventoriesHandler.Instance.GetInventoryById(wo.GetLinkedInventoryId()) : null;
                if (inventory != null)
                    text.AppendLine("    inventory size " + inventory.GetSize() + ", holds " + string.Join(", ", inventory.GetInsideWorldObjects().Select(i => i.GetGroup().GetId()).ToArray()));

                if (id.StartsWith("Screen"))
                {
                    var go = wo.GetGameObject();
                    if (go == null)
                        continue;
                    foreach (var component in go.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null)
                            continue;
                        var type = component.GetType();
                        if (type.Name.StartsWith("Action") || type.Name.Contains("Interact"))
                        {
                            text.AppendLine("    " + type.Name + " on " + component.gameObject.name + ": " + string.Join("; ", type.GetFields(All).Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string)).Select(f => f.Name + "=" + f.GetValue(component)).ToArray()));
                        }
                    }
                }
            }

            text.AppendLine().AppendLine("== The optimizer's own settings (the prefab's MachineOptimizer, if one is placed)");
            foreach (var optimizer in UnityEngine.Object.FindObjectsByType<MachineOptimizer>(FindObjectsSortMode.None).Take(2))
                text.AppendLine("range " + optimizer.range + ", maxWorldObjectPerFuse " + optimizer.maxWorldObjectPerFuse + " on " + optimizer.gameObject.name);
        }

        private static void UiTypes(StringBuilder text)
        {
            text.AppendLine().AppendLine("== UiType values");
            text.AppendLine(string.Join(", ", Enum.GetNames(typeof(DataConfig.UiType))));
        }
    }
}
