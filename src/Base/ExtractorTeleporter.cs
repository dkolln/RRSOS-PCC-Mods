using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Base
{
    /// <summary>
    /// Once the Teleporter is unlocked, gives every ore extractor that stands on a vein a way home: a foundation beside it, a stair up to the foundation, and a teleporter on the foundation
    /// named for the ore it mines, laid out as the owner built the iridium extractor (measured from a save, from the extractor's own position and direction, so it follows the extractor
    /// however it stands). Extractors on no vein are left alone. Whatever of it already stands is kept. An extractor is dealt with once; if the pieces are taken away afterwards they do not
    /// come back. Switched by ExtractorTeleporters.
    /// </summary>
    internal sealed class ExtractorTeleporter : MonoBehaviour
    {
        // From the save: the extractor stood at yaw 180; the foundation was 6 m along X (1.06 m higher), the stair 5.93 m along X and 5.64 m back along Z (1.53 m lower, turned 180),
        // the teleporter on the middle of the foundation, on top of it (the foundation's top is 2.519 m above where it is placed), turned 180 to face the stair.
        private static readonly Vector3 FoundationAt = new Vector3(6f, 1.06f, 0f);
        private static readonly Vector3 StairAt = new Vector3(5.928f, -1.5345f, -5.641f);
        private const float FoundationTop = 2.519f, ExtractorYaw = 180f, Near = 14f;

        // The ores that are generic (the game hands them to any extractor, a sulfur one included once it is a T3): they get no teleporter, only the special ores do.
        private static readonly HashSet<string> Generic = new HashSet<string> { "Iron", "Silicon", "Titanium", "Cobalt", "Magnesium", "Sulfur" };

        private static readonly Regex Extractor = new Regex(@"^OreExtractor\d*$", RegexOptions.Compiled);

        private readonly HashSet<string> _done = new HashSet<string>();
        private string _worldKey;
        private bool _busy;

        private static string DoneFile => ModLog.FileIn("extractor-teleporters.txt");

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(5f);

                if (!Plugin.Enabled.Value || !Plugin.ExtractorTeleporters.Value || _busy || BaseBuilder.Busy)
                    continue;

                List<WorldObject> work;
                var planetHash = 0;
                try
                {
                    if (!WorldState.Ready(out planetHash, out var key) || !Unlocked("Teleporter1"))
                        continue;

                    if (key != _worldKey)
                    {
                        _worldKey = key;
                        _done.Clear();
                        if (File.Exists(DoneFile))
                            foreach (var line in File.ReadAllLines(DoneFile))
                                _done.Add(line.Trim());
                    }

                    work = WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                        .Where(w => w != null && w.GetGroup() != null && Extractor.IsMatch(w.GetGroup().GetId()) && w.GetPlanetHash() == planetHash && w.GetPosition().sqrMagnitude > 0f
                            && w.HasLinkedGroups() && w.GetLinkedGroups().Any(g => g != null && !Generic.Contains(g.GetId())) && !_done.Contains(Mark(w)))
                        .ToList();
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("extractor-teleporter-scan", "Extractor teleporters: " + e.GetType().Name + ": " + e.Message);
                    continue;
                }

                if (work.Count == 0)
                    continue;

                _busy = true;
                foreach (var extractor in work)
                    yield return Build(extractor, planetHash);
                _busy = false;
            }
        }

        private string Mark(WorldObject extractor) => _worldKey + " " + extractor.GetId();

        private static bool Unlocked(string groupId)
        {
            var group = GroupsHandler.GetGroupViaId(groupId);
            if (group == null)
                return false;
            return group.GetUnlockingInfos().GetIsUnlocked(checkIfAvailableOnCurrentPlanet: true) || group.GetIsGloballyUnlocked();
        }

        private IEnumerator Build(WorldObject extractor, int planetHash)
        {
            var ore = extractor.GetLinkedGroups().Where(g => g != null && !Generic.Contains(g.GetId())).Select(g => g.GetId()).First();
            var name = Readable.GetGroupName(GroupsHandler.GetGroupViaId(ore));
            var at = extractor.GetPosition();
            var turn = Quaternion.Euler(0f, extractor.GetRotation().eulerAngles.y - ExtractorYaw, 0f);
            var foundation = at + turn * FoundationAt;
            var stair = at + turn * StairAt;
            var all = WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() != null).ToList();

            var placed = new List<string>();
            bool problem = false, skipped = false;
            try
            {
                if (all.Any(w => w.GetGroup().GetId() == "Teleporter1" && (w.GetPosition() - at).sqrMagnitude < Near * Near))
                {
                    ModLog.Note("Extractor teleporter: the " + name + " extractor #" + extractor.GetId() + " already has a teleporter near it.");
                    Finish(extractor);
                    skipped = true;
                }
                else
                {
                if (!Standing(all, "Foundation", foundation, 0.5f))
                {
                    Placement.PlaceAt("Foundation", foundation, Quaternion.identity, planetHash);
                    placed.Add("foundation");
                }

                if (!Standing(all, "Stairs", stair, 1f))
                {
                    Placement.PlaceAt("Stairs", stair, turn * Quaternion.Euler(0f, 180f, 0f), planetHash);
                    placed.Add("stair");
                }

                Placement.PlaceAt("Teleporter1", foundation + new Vector3(0f, FoundationTop, 0f), turn * Quaternion.Euler(0f, 180f, 0f), planetHash, text: name);
                placed.Add("teleporter \"" + name + "\"");
                }
            }
            catch (Exception e)
            {
                problem = true;
                ModLog.Note("Extractor teleporter: the " + name + " extractor #" + extractor.GetId() + ": " + e.GetType().Name + ": " + e.Message);
            }

            if (!problem && !skipped)
            {
                Finish(extractor);
                ModLog.Note("Extractor teleporter: built " + string.Join(", ", placed.ToArray()) + " at the " + name + " extractor #" + extractor.GetId() + ".");
                TerminalRegistration.Notice("Teleporter \"" + name + "\" built at the " + name + " extractor.");
            }

            yield return null;
        }

        private static bool Standing(List<WorldObject> all, string id, Vector3 p, float range) =>
            all.Any(w => w.GetGroup().GetId() == id && Mathf.Abs(w.GetPosition().x - p.x) < range && Mathf.Abs(w.GetPosition().z - p.z) < range);

        private void Finish(WorldObject extractor)
        {
            var mark = Mark(extractor);
            _done.Add(mark);
            try
            {
                File.AppendAllText(DoneFile, mark + Environment.NewLine);
            }
            catch (Exception e)
            {
                ModLog.LogOnce("extractor-teleporter-save", "Extractor teleporters: could not save the list: " + e.Message);
            }
        }
    }
}
