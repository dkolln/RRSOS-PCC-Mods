using System;
using System.Linq;
using System.Text.RegularExpressions;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Boosters
{
    /// <summary>
    /// The chat command /booster teleport: every beacon named teleport or teleporter gets a teleporter on its other side (the way opposite to where the beacon points, as for every booster).
    /// A beacon standing on a foundation gets the connecting foundation first (the next one along, 6 m on), and the teleporter stands on that; a beacon on the ground gets the teleporter 3 m
    /// beyond it. Free, through the game's own placement; what already stands there is kept. The beacon is removed when the teleporter is built.
    /// </summary>
    internal static class TeleportBeacon
    {
        private const float FoundationSize = 6f, FoundationTop = 2.519f, GroundGap = 3f;

        internal static void OnCommand(string args)
        {
            var word = (args ?? "").Trim().ToLowerInvariant();
            if (word != "teleport" && word != "teleporter")
            {
                TerminalRegistration.Notice("Usage: /booster teleport (builds a teleporter at each beacon named teleport).");
                return;
            }

            try
            {
                if (!WorldState.Ready(out var planetHash, out _) || GroupsHandler.GetGroupViaId("Teleporter1") == null)
                {
                    TerminalRegistration.Notice("The game is not ready yet.");
                    return;
                }

                var all = WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() != null).ToList();
                var beacons = all.Where(w => w.GetGroup().GetId() == "Beacon" && w.GetPlanetHash() == planetHash && Regex.IsMatch((w.GetText() ?? "").ToLowerInvariant(), @"\bteleport")).ToList();
                if (beacons.Count == 0)
                {
                    TerminalRegistration.Notice("No beacon is named teleport.");
                    return;
                }

                foreach (var beacon in beacons)
                    Build(beacon, all, planetHash);
            }
            catch (Exception e)
            {
                ModLog.Note("/booster teleport: " + e.GetType().Name + ": " + e.Message);
                TerminalRegistration.Notice("The teleporter could not be built; see the Boosters log.");
            }
        }

        private static void Build(WorldObject beacon, System.Collections.Generic.List<WorldObject> all, int planetHash)
        {
            var at = beacon.GetPosition();
            var forward = beacon.GetRotation() * Vector3.forward;
            var away = Mathf.Abs(forward.x) >= Mathf.Abs(forward.z) ? new Vector3(-Mathf.Sign(forward.x), 0f, 0f) : new Vector3(0f, 0f, -Mathf.Sign(forward.z));

            // the foundation under the beacon, if there is one
            var under = all.Where(w => w.GetGroup().GetId() == "Foundation" && Mathf.Abs(w.GetPosition().x - at.x) <= FoundationSize / 2f && Mathf.Abs(w.GetPosition().z - at.z) <= FoundationSize / 2f
                    && at.y - w.GetPosition().y > 1f && at.y - w.GetPosition().y < 4f)
                .OrderBy(w => (w.GetPosition() - at).sqrMagnitude).FirstOrDefault();

            Vector3 spot;
            var placed = "";
            if (under != null)
            {
                var next = under.GetPosition() + away * FoundationSize;
                if (!all.Any(w => w.GetGroup().GetId() == "Foundation" && Mathf.Abs(w.GetPosition().x - next.x) < 0.5f && Mathf.Abs(w.GetPosition().z - next.z) < 0.5f && Mathf.Abs(w.GetPosition().y - next.y) < 1f))
                {
                    Placement.PlaceAt("Foundation", next, Quaternion.identity, planetHash);
                    placed = "foundation and ";
                }

                spot = next + new Vector3(0f, FoundationTop, 0f);
            }
            else
            {
                spot = at + away * GroundGap;
            }

            var name = Regex.Replace(beacon.GetText() ?? "", @"\bteleport(er)?s?\b", "", RegexOptions.IgnoreCase).Trim();
            if (name.Length == 0)
                name = "Teleporter";

            Placement.PlaceAt("Teleporter1", spot, Quaternion.LookRotation(-away), planetHash, text: name);
            try
            {
                WorldObjectsHandler.Instance.DestroyWorldObject(beacon, true);
            }
            catch (Exception e)
            {
                ModLog.Note("/booster teleport: could not remove the beacon: " + e.Message);
            }

            ModLog.Note("/booster teleport: built " + placed + "teleporter \"" + name + "\" at " + spot + (under != null ? " (beacon on a foundation)" : " (beacon on the ground)") + ".");
            TerminalRegistration.Notice("Built " + placed + "teleporter \"" + name + "\".");
        }
    }
}
