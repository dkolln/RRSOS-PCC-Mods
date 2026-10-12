using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Base
{
    /// <summary>
    /// Places stage 1 of the main base (the anchor lamp on its foundation, one more foundation, one pod) in a world that has none of it, the way the game itself
    /// brings a saved piece to life (see Placement). Once per save, never over anything else.
    /// </summary>
    internal sealed class BaseBuilder : MonoBehaviour
    {
        // This mod's small state files (in %LOCALAPPDATA%\RRSOS-PCC-Mods\Base). Drop build-now.txt or undo-now.txt in while in the world and the mod
        // answers within two seconds (and deletes the file).
        private static string DoneFile => ModLog.FileIn("done.txt");
        private static string UndoFile => ModLog.FileIn("undo.txt");
        private static string BuildNowFile => ModLog.FileIn("build-now.txt");
        private static string UndoNowFile => ModLog.FileIn("undo-now.txt");

        private const string StartPlanet = "Prime";
        private const float Reach = 15f;        // nothing of the player's own may stand this close (metres, along the ground) to the anchor
        private const float SameSpot = 0.5f;    // a piece this close to a stage 1 piece, of the same kind, counts as already there

        private sealed class Piece
        {
            public string Group;
            public Vector3 Pos;
            public Quaternion Rot;
            public int[] Panels;
            public string Color;
        }

        // Stage 1, as captured from Standard-2 on 2026-10-12 (the anchor foundation is at 772, 29, 601; the lamp points north). The terrain of Prime never changes.
        private static readonly Piece[] Stage1 =
        {
            new Piece { Group = "Foundation", Pos = new Vector3(772f, 29f, 601f), Rot = new Quaternion(0f, 0f, 0f, 1f) },
            new Piece { Group = "Foundation", Pos = new Vector3(778f, 29f, 601f), Rot = new Quaternion(0f, 0f, 0f, 1f) },
            new Piece { Group = "pod", Pos = new Vector3(778f, 31.5f, 600.9f), Rot = new Quaternion(0f, 0f, 0f, 1f), Panels = new[] { 1, 1, 1, 4, 5, 7 } },
            new Piece { Group = "OutsideLamp1", Pos = new Vector3(771.9205f, 31.519f, 601.0012f), Rot = new Quaternion(0f, -0.7071068f, 0f, 0.7071068f), Color = "1-0.9696766-0.8443396-1" },
        };

        /// <summary>Set by the /build chat command; answered by the loop below within two seconds.</summary>
        private static volatile bool _buildRequested;

        private bool _autoHandled;
        private bool _busy;

        // ---- chat commands ---------------------------------------------------------------------------------------------------

        internal static void OnBuildCommand(string args)
        {
            if (!WorldState.Ready(out _, out _))
            {
                ChatHook.Say("The world is not ready yet.");
                return;
            }

            if (!WorldState.OnPlanet(StartPlanet))
            {
                ChatHook.Say("No template location is set for this planet.");
                return;
            }

            _buildRequested = true;
            ChatHook.Say("Building the base...");
        }

        internal static void OnUndoCommand(string args)
        {
            if (!WorldState.Ready(out _, out var key))
            {
                ChatHook.Say("The world is not ready yet.");
                return;
            }

            var removed = Undo(key);
            ChatHook.Say(removed == 0 ? "Nothing of the base to undo in this save." : "Took back the base (" + removed + " pieces).");
        }

        // ---- the loop --------------------------------------------------------------------------------------------------------

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);

                if (_busy || !Plugin.Enabled.Value)
                    continue;

                var wantBuild = File.Exists(BuildNowFile) || _buildRequested;
                var wantUndo = File.Exists(UndoNowFile);
                if (!wantBuild && !wantUndo && _autoHandled)
                    continue;

                string key = null;
                int planetHash = 0;
                bool go = false;
                try
                {
                    go = WorldState.Ready(out planetHash, out key) && WorldState.OnPlanet(StartPlanet);
                }
                catch (Exception e)
                {
                    ModLog.Note("Could not tell whether the world is ready: " + e.GetType().Name + ": " + e.Message);
                }

                if (!go)
                    continue; // the request files wait until a world is ready

                _busy = true;
                if (wantUndo)
                {
                    ModLog.Consume(UndoNowFile);
                    ModLog.Note("Undo requested.");
                    try
                    {
                        Undo(key);
                    }
                    catch (Exception e)
                    {
                        ModLog.Note("Undo failed: " + e.GetType().Name + ": " + e.Message);
                    }
                }
                else if (wantBuild)
                {
                    ModLog.Consume(BuildNowFile);
                    _buildRequested = false;
                    ModLog.Note("Build requested for save " + key + ".");
                    yield return PlaceIfEmpty(key, planetHash, false);
                }
                else
                {
                    _autoHandled = true; // once per game session; a request can still ask again
                    yield return Decide(key, planetHash);
                }

                _busy = false;
            }
        }

        private IEnumerator Decide(string key, int planetHash)
        {
            if (Plugin.UndoLastBuild.Value)
            {
                try
                {
                    Undo(key);
                }
                catch (Exception e)
                {
                    ModLog.Note("Undo failed: " + e.GetType().Name + ": " + e.Message);
                }

                Plugin.UndoLastBuild.Value = false;
                yield break;
            }

            if (ModLog.ReadLines(DoneFile).Contains(key))
            {
                ModLog.Note("Save " + key + ": stage 1 was already handled once; nothing to do (a build request would place it again).");
                yield break;
            }

            yield return PlaceIfEmpty(key, planetHash, true);
        }

        /// <summary>Places stage 1 if the site is empty. The automatic planetfall run marks the save as handled even when it places nothing; a request does not.</summary>
        private IEnumerator PlaceIfEmpty(string key, int planetHash, bool automatic)
        {
            // Only into a world that has none of it, and with nothing of the player's own standing near the anchor.
            var handler = WorldObjectsHandler.Instance;
            var anchor = Stage1[0].Pos;
            var present = 0;
            var near = new List<string>();
            foreach (var wo in handler.GetAllWorldObjects().Values.ToList())
            {
                if (wo == null || wo.GetGroup() == null)
                    continue;

                var pos = wo.GetPosition();
                if (pos.sqrMagnitude <= 0f)
                    continue; // an item inside an inventory

                if (Stage1.Any(p => p.Group == wo.GetGroup().GetId() && (pos - p.Pos).sqrMagnitude < SameSpot * SameSpot))
                    present++;

                var flat = new Vector2(pos.x - anchor.x, pos.z - anchor.z);
                if (flat.magnitude < Reach && wo.GetGroup() is GroupConstructible)
                    near.Add(wo.GetGroup().GetId());
            }

            if (present > 0 || near.Count > 0)
            {
                ModLog.Note("Save " + key + ": the site is not empty (" + present + " stage 1 pieces already there; near the anchor: " + (near.Count == 0 ? "nothing" : string.Join(", ", near.Distinct().ToArray())) + "). Nothing placed.");
                if (automatic)
                    ModLog.Append(DoneFile, key);
                else
                    ChatHook.Say("The site is not empty, so nothing was placed.");
                yield break;
            }

            ModLog.Note("Save " + key + ": planetfall with an empty site. Placing stage 1 (" + Stage1.Length + " pieces).");
            var created = new List<WorldObject>();
            var failed = false;
            foreach (var piece in Stage1)
            {
                try
                {
                    var wo = Placement.PlaceAt(piece.Group, piece.Pos, piece.Rot, planetHash, piece.Panels, piece.Color);
                    created.Add(wo);
                    ModLog.Note("Placed " + piece.Group + " #" + wo.GetId() + " at " + piece.Pos.x.ToString("0.###") + ", " + piece.Pos.y.ToString("0.###") + ", " + piece.Pos.z.ToString("0.###") + ".");
                }
                catch (Exception e)
                {
                    ModLog.Note("FAILED placing " + piece.Group + ": " + e.GetType().Name + ": " + e.Message);
                    failed = true;
                }

                if (failed)
                    break;

                yield return null;
            }

            if (failed)
            {
                foreach (var wo in created)
                {
                    try
                    {
                        handler.DestroyWorldObject(wo, true);
                    }
                    catch (Exception e)
                    {
                        ModLog.Note("Could not roll back object " + wo.GetId() + ": " + e.Message);
                    }
                }

                ModLog.Note("Rolled back " + created.Count + " pieces. The save is not marked, so a later start tries again.");
                if (!automatic)
                    ChatHook.Say("The base could not be placed; nothing was kept. See the mod's log.");
                yield break;
            }

            try
            {
                var energy = Managers.GetManager<RequireEnergyHandler>();
                if (energy != null)
                    energy.UpdateAllEnergyRequester();
            }
            catch (Exception e)
            {
                ModLog.Note("Could not refresh the energy network (harmless): " + e.Message);
            }

            foreach (var wo in created)
                ModLog.Append(UndoFile, key + "|" + wo.GetId() + "|" + wo.GetGroup().GetId());
            if (!ModLog.ReadLines(DoneFile).Contains(key))
                ModLog.Append(DoneFile, key);
            ModLog.Note("Stage 1 placed: " + string.Join(", ", created.Select(w => w.GetGroup().GetId() + " #" + w.GetId()).ToArray()) + ".");
            if (!automatic)
                ChatHook.Say("Base stage 1 placed (" + created.Count + " pieces). /undo takes it back.");
        }

        /// <summary>Removes what this mod placed in the save and clears the save's "handled" mark. Returns how many pieces it removed.</summary>
        private static int Undo(string key)
        {
            var handler = WorldObjectsHandler.Instance;
            var lines = ModLog.ReadLines(UndoFile);
            var mine = lines.Where(l => l.StartsWith(key + "|")).ToList();
            if (mine.Count == 0)
            {
                ModLog.Note("Undo: nothing recorded for save " + key + ".");
                return 0;
            }

            var removed = 0;
            foreach (var line in mine)
            {
                var parts = line.Split('|');
                if (int.TryParse(parts[parts.Length - 2], out var id) && handler.GetWorldObjectViaId(id) != null)
                {
                    handler.DestroyWorldObject(id, true);
                    removed++;
                    ModLog.Note("Undo: removed " + parts[parts.Length - 1] + " #" + id + ".");
                }
            }

            File.WriteAllLines(UndoFile, lines.Where(l => !l.StartsWith(key + "|")).ToArray());
            File.WriteAllLines(DoneFile, ModLog.ReadLines(DoneFile).Where(l => l != key).ToArray());
            return removed;
        }
    }
}
