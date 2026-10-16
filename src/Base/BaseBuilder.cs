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
    /// Builds the T0 base (as much of the base as can be built before the first drill, heater or vegetube, with only what is unlocked at the very start) in a world that has none
    /// of it, the way the game itself brings a saved piece to life (see Placement). Every piece comes from the template (T0.json) as an offset from Prime's anchor foundation.
    /// Crates keep their labels and are stocked from them. Once per save, never over anything else.
    /// </summary>
    internal sealed class BaseBuilder : MonoBehaviour
    {
        // This mod's small state files (in %LOCALAPPDATA%\RRSOS-PCC-Mods\Base). Drop build-now.txt or undo-now.txt in while in the world and the mod
        // answers within two seconds (and deletes the file).
        private static string DoneFile => ModLog.FileIn("done.txt");
        internal static string UndoFile => ModLog.FileIn("undo.txt");
        private static string BuildNowFile => ModLog.FileIn("build-now.txt");
        private static string UndoNowFile => ModLog.FileIn("undo-now.txt");

        private const string StartPlanet = "Prime";

        /// <summary>Prime's template location: the foundation under the anchor outdoor lamp (the lamp points north). The terrain of Prime never changes.</summary>
        internal static readonly Vector3 Anchor = new Vector3(766f, 29f, 601f);

        private const float Margin = 6f;        // nothing of the player's own may stand within this of the base's footprint (metres, along the ground)
        private const float SameSpot = 0.5f;    // a piece this close to a template piece, of the same kind, counts as already there
        private const float PieceReach = 3.2f;    // at a beacon: a building of the player's within this (each way, along the ground) of a template piece is in its way
        private const int PiecesPerFrame = 4;

        /// <summary>Set by the /build chat command; answered by the loop below within two seconds.</summary>
        private static volatile bool _buildRequested;

        /// <summary>Set by the /undo chat command; answered by the loop below, after a backup save.</summary>
        private static volatile bool _undoRequested;

        /// <summary>/build missing: place only the template pieces that are not there yet, instead of refusing when part of the base stands (to bring back what an earlier /undo took).</summary>
        private static volatile bool _missingRequested;

        private bool _autoHandled;
        private string _autoKey;
        internal static bool Busy;

        // ---- chat commands ---------------------------------------------------------------------------------------------------

        internal static void OnBuildCommand(string args)
        {
            if (!WorldState.Ready(out _, out _))
            {
                ChatHook.Say("The world is not ready yet.");
                return;
            }

            WorldState.Ready(out var planetHash, out _);
            if (!WorldState.OnPlanet(StartPlanet) && !FindBeacon(planetHash, out _, out _, out _))
            {
                ChatHook.Say("Build a foundation, put a beacon named Base on it and use /build again: the base is built on from there, on the side opposite to where the beacon points.");
                return;
            }

            _missingRequested = string.Equals((args ?? "").Trim(), "missing", StringComparison.OrdinalIgnoreCase);
            _buildRequested = true;
            ChatHook.Say(_missingRequested ? "Placing the pieces of the base that are missing..." : "Building the base...");
        }

        internal static void OnUndoCommand(string args)
        {
            if (!WorldState.Ready(out _, out _))
            {
                ChatHook.Say("The world is not ready yet.");
                return;
            }

            _undoRequested = true;
            ChatHook.Say("Saving a backup, then taking the base back...");
        }

        // ---- the loop --------------------------------------------------------------------------------------------------------

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);

                if (Busy || !Plugin.Enabled.Value)
                    continue;

                // a different world loaded in the same session is handled afresh
                if (_autoHandled && Ready() && WorldState.Ready(out _, out var current) && current != _autoKey)
                    _autoHandled = false;

                var wantBuild = File.Exists(BuildNowFile) || _buildRequested;
                var wantUndo = File.Exists(UndoNowFile) || _undoRequested;
                if (!wantBuild && !wantUndo && _autoHandled)
                    continue;

                string key = null;
                int planetHash = 0;
                bool go = false;
                try
                {
                    go = WorldState.Ready(out planetHash, out key) && (WorldState.OnPlanet(StartPlanet) || wantUndo || (wantBuild && FindBeacon(planetHash, out _, out _, out _)));
                }
                catch (Exception e)
                {
                    ModLog.Note("Could not tell whether the world is ready: " + e.GetType().Name + ": " + e.Message);
                }

                if (!go)
                {
                    // a new game on a planet with no template: the other start-up steps need not wait for this one
                    if (!_autoHandled && !wantBuild && !wantUndo && Ready() && !WorldState.OnPlanet(StartPlanet))
                    {
                        _autoHandled = true;
                        WorldState.Ready(out _, out _autoKey);
                        Boot.NewWorld(_autoKey);
                        Boot.Skip(Boot.Base);
                    }

                    continue; // the request files wait until a world is ready
                }

                Busy = true;
                Boot.NewWorld(key);
                if (wantUndo)
                {
                    var chat = _undoRequested;
                    ModLog.Consume(UndoNowFile);
                    _undoRequested = false;
                    ModLog.Note("Undo requested.");
                    var backedUp = false;
                    yield return SaveBackup.Make("undo", ok => backedUp = ok);
                    if (!backedUp)
                    {
                        ChatHook.Say("No backup save could be made, so nothing was taken back.");
                    }
                    else
                    {
                        try
                        {
                            var removed = Undo(key, planetHash);
                            if (chat)
                                ChatHook.Say(removed == 0 ? "Nothing of the base to undo here." : "Took back the base (" + removed + " pieces). A backup save was made first.");
                        }
                        catch (Exception e)
                        {
                            ModLog.Note("Undo failed: " + e.GetType().Name + ": " + e.Message);
                        }
                    }
                }
                else if (wantBuild)
                {
                    ModLog.Consume(BuildNowFile);
                    _buildRequested = false;
                    ModLog.Note("Build requested for save " + key + ".");
                    var saved = false;
                    yield return SaveBackup.Make("build", ok => saved = ok);
                    if (saved)
                        yield return PlaceIfEmpty(key, planetHash, false);
                    else
                        ChatHook.Say("No backup save could be made, so nothing was built.");
                }
                else
                {
                    _autoHandled = true; // once per world; a request can still ask again
                    _autoKey = key;
                    yield return Decide(key, planetHash);
                    Boot.Skip(Boot.Base); // no-op when the base was built (that finished the step)
                }

                Busy = false;
            }
        }

        private IEnumerator Decide(string key, int planetHash)
        {
            if (Plugin.UndoLastBuild.Value)
            {
                try
                {
                    Undo(key, planetHash);
                }
                catch (Exception e)
                {
                    ModLog.Note("Undo failed: " + e.GetType().Name + ": " + e.Message);
                }

                Plugin.UndoLastBuild.Value = false;
                yield break;
            }

            // A new game is one where the player has crafted nothing yet. What is saved is judged from the world itself (a game left without saving starts over,
            // so a mark kept in a file would be wrong), and the site check below keeps a base that is already there from being built twice.
            var player = Managers.GetManager<PlayersManager>().GetActivePlayerController();
            var crafted = WorldObjectsHandler.Instance.GetTotalCraftedObjects(player.OwnerClientId);
            if (crafted != 0)
            {
                ModLog.Note("Save " + key + ": not a new game (" + crafted + " objects crafted); nothing to do (/build places it on request).");
                yield break;
            }

            yield return PlaceIfEmpty(key, planetHash, true);
        }

        private static bool Ready()
        {
            try
            {
                return WorldState.Ready(out _, out _);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Where this run puts the template: Prime's own anchor, or the foundation under a beacon named Base (and then turned to the beacon's way).
        private Vector3 _anchor = Anchor;
        private Quaternion _turn = Quaternion.identity;
        private bool _atBeacon;
        private WorldObject _beacon;

        private Vector3 WorldPosition(TemplatePiece piece) => _anchor + _turn * new Vector3(piece.x, piece.y, piece.z);

        // The template was captured with its Base beacon looking down -X (yaw 270) and the base on the other side of it, along +X.
        private const float TemplateBeaconYaw = 270f;

        /// <summary>
        /// A beacon named Base standing on a foundation on this planet (the nearest to the player when there are several): the anchor is that foundation, and the base runs on from it
        /// the way the template does from its own beacon: on the side opposite to where the beacon points (as every booster does), turned to the nearest quarter.
        /// </summary>
        private static bool FindBeacon(int planetHash, out WorldObject beacon, out Vector3 anchor, out Quaternion turn)
        {
            beacon = null;
            anchor = Anchor;
            turn = Quaternion.identity;
            var all = WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() != null && w.GetPlanetHash() == planetHash).ToList();
            var foundations = all.Where(w => w.GetGroup().GetId() == "Foundation" && w.GetPosition().sqrMagnitude > 0f).ToList();
            var player = Vector3.zero;
            try
            {
                player = Managers.GetManager<PlayersManager>().GetActivePlayerController().transform.position;
            }
            catch (Exception)
            {
            }

            WorldObject bestBeacon = null;
            WorldObject bestFoundation = null;
            var bestDistance = float.MaxValue;
            foreach (var candidate in all.Where(w => w.GetGroup().GetId() == "Beacon" && string.Equals((w.GetText() ?? "").Trim(), "base", StringComparison.OrdinalIgnoreCase)))
            {
                var at = candidate.GetPosition();
                var under = foundations.Where(f => Mathf.Abs(f.GetPosition().x - at.x) <= 3.1f && Mathf.Abs(f.GetPosition().z - at.z) <= 3.1f && at.y - f.GetPosition().y > 1f && at.y - f.GetPosition().y < 4f)
                    .OrderBy(f => (f.GetPosition() - at).sqrMagnitude).FirstOrDefault();
                if (under == null)
                    continue;

                var d = (at - player).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestBeacon = candidate;
                    bestFoundation = under;
                }
            }

            if (bestBeacon == null)
                return false;

            beacon = bestBeacon;
            anchor = bestFoundation.GetPosition();
            turn = Quaternion.Euler(0f, Mathf.Round((bestBeacon.GetRotation().eulerAngles.y - TemplateBeaconYaw) / 90f) * 90f, 0f);
            return true;
        }

        /// <summary>Builds the base if the site is empty. The automatic planetfall run marks the save as handled even when it places nothing; a request does not.</summary>
        private IEnumerator PlaceIfEmpty(string key, int planetHash, bool automatic)
        {
            Template template;
            try
            {
                template = T0.Get();
            }
            catch (Exception e)
            {
                ModLog.Note("Could not read the template: " + e.Message);
                if (!automatic)
                    ChatHook.Say("The base template could not be read. See the mod's log.");
                yield break;
            }

            // Where: a beacon named Base on a foundation if there is one (any planet), else Prime's own place.
            _atBeacon = !automatic && FindBeacon(planetHash, out _beacon, out _anchor, out _turn);
            if (!_atBeacon)
            {
                _anchor = Anchor;
                _turn = Quaternion.identity;
                _beacon = null;
                if (!WorldState.OnPlanet(StartPlanet))
                {
                    if (!automatic)
                        ChatHook.Say("Put a beacon named Base on a foundation and use /build again.");
                    yield break;
                }
            }
            else
            {
                ModLog.Note("Building at the beacon named Base: anchor foundation " + _anchor + ", turned " + _turn.eulerAngles.y + " degrees.");
            }

            // Only into a world that has none of it, and with nothing of the player's own standing on the base's footprint.
            var handler = WorldObjectsHandler.Instance;
            var pieces = _atBeacon ? template.pieces.Where(p => p.g != "OutsideLamp1").ToArray() : template.pieces;   // the beacon is the lamp
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in pieces)
            {
                var at = WorldPosition(p);
                minX = Mathf.Min(minX, at.x); maxX = Mathf.Max(maxX, at.x);
                minZ = Mathf.Min(minZ, at.z); maxZ = Mathf.Max(maxZ, at.z);
            }

            var missingOnly = !automatic && _missingRequested;
            _missingRequested = false;
            var existing = new List<KeyValuePair<string, Vector3>>();
            var present = 0;
            var near = new List<string>();
            foreach (var wo in handler.GetAllWorldObjects().Values.ToList())
            {
                if (wo == null || wo.GetGroup() == null)
                    continue;

                var pos = wo.GetPosition();
                if (pos.sqrMagnitude <= 0f)
                    continue; // an item inside an inventory

                if (_atBeacon && (wo == _beacon || (wo.GetGroup().GetId() == "Foundation" && (pos - _anchor).sqrMagnitude < SameSpot * SameSpot)))
                    continue;   // the player's own beacon and the foundation it stands on are the start of the base

                var id = wo.GetGroup().GetId();
                existing.Add(new KeyValuePair<string, Vector3>(id, pos));
                if (pieces.Any(p => p.g == id && (pos - WorldPosition(p)).sqrMagnitude < SameSpot * SameSpot))
                    present++;

                // Prime: nothing of the player's may stand within a margin of the whole footprint. At a beacon only what is in the way of a piece counts (the start of the player's own
                // base, a stair up to the foundation, may stand beside it).
                var blocks = !(wo.GetGroup() is GroupConstructible) ? false : _atBeacon
                    ? pieces.Any(p => !(p.g == "Foundation" && (WorldPosition(p) - _anchor).sqrMagnitude < SameSpot * SameSpot)
                        && Mathf.Abs(WorldPosition(p).x - pos.x) < PieceReach && Mathf.Abs(WorldPosition(p).z - pos.z) < PieceReach && Mathf.Abs(WorldPosition(p).y - pos.y) < 6f)
                    : pos.x > minX - Margin && pos.x < maxX + Margin && pos.z > minZ - Margin && pos.z < maxZ + Margin;
                if (wo.GetGroup() is GroupConstructible && blocks)
                    near.Add(id);
            }

            if (missingOnly)
            {
                // what is already there stays; only the pieces of the template with nothing of their kind in their place are made
                var all = pieces.Length;
                pieces = pieces.Where(p => !existing.Any(e => e.Key == p.g && (e.Value - WorldPosition(p)).sqrMagnitude < SameSpot * SameSpot)).ToArray();
                ModLog.Note("Save " + key + ": placing only what is missing: " + pieces.Length + " of " + all + " template pieces.");
                if (pieces.Length == 0)
                {
                    ChatHook.Say("Nothing is missing: every piece of the base is there.");
                    yield break;
                }
            }
            else if (present > 0 || near.Count > 0)
            {
                ModLog.Note("Save " + key + ": the site is not empty (" + present + " template pieces already there; on the footprint: " + (near.Count == 0 ? "nothing" : string.Join(", ", near.Distinct().ToArray())) + "). Nothing placed.");
                if (automatic)
                    ModLog.Append(DoneFile, key);
                else
                    ChatHook.Say("The site is not empty, so nothing was placed.");
                yield break;
            }

            ModLog.Note("Save " + key + ": " + (automatic ? "planetfall with an empty site" : "an empty site") + ". Placing " + template.name + " (" + pieces.Length + " pieces).");
            if (automatic)
                yield return Boot.Begin(Boot.Base, "Preparing main base....");

            var created = new List<KeyValuePair<WorldObject, TemplatePiece>>();
            var skipped = new List<string>();
            var failed = false;
            var sinceFrame = 0;
            foreach (var piece in pieces)
            {
                if (_atBeacon && piece.g == "Foundation" && (WorldPosition(piece) - _anchor).sqrMagnitude < SameSpot * SameSpot)
                    continue;   // the foundation the beacon stands on

                if (GroupsHandler.GetGroupViaId(piece.g) == null)
                {
                    // a piece of a mod that is not installed (the Terminal without the Commands mod)
                    skipped.Add(piece.g);
                    continue;
                }

                try
                {
                    var wo = Placement.PlaceAt(piece.g, WorldPosition(piece), _turn * DataTreatments.StringToQuaternion(piece.rot), planetHash,
                        string.IsNullOrEmpty(piece.panels) ? null : DataTreatments.StringToIntList(piece.panels).ToArray(),
                        string.IsNullOrEmpty(piece.color) ? null : piece.color, piece.text, piece.groups);
                    created.Add(new KeyValuePair<WorldObject, TemplatePiece>(wo, piece));
                }
                catch (Exception e)
                {
                    ModLog.Note("FAILED placing " + piece.g + ": " + e.GetType().Name + ": " + e.Message);
                    failed = true;
                }

                if (failed)
                    break;

                if (++sinceFrame >= PiecesPerFrame)
                {
                    sinceFrame = 0;
                    yield return null;
                }
            }

            if (failed)
            {
                foreach (var made in created)
                {
                    try
                    {
                        handler.DestroyWorldObject(made.Key, true);
                    }
                    catch (Exception e)
                    {
                        ModLog.Note("Could not roll back object " + made.Key.GetId() + ": " + e.Message);
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

            foreach (var made in created)
                ModLog.Append(UndoFile, key + "|" + made.Key.GetId() + "|" + made.Value.g);
            if (!ModLog.ReadLines(DoneFile).Contains(key))
                ModLog.Append(DoneFile, key);

            var kinds = string.Join(", ", created.GroupBy(c => c.Value.g).OrderByDescending(g => g.Count()).Select(g => g.Count() + " " + g.Key).ToArray());
            ModLog.Note("Placed " + created.Count + " pieces: " + kinds + ". Not placed (not installed): " + (skipped.Count == 0 ? "none" : string.Join(", ", skipped.Distinct().ToArray())) + ".");

            // The crates keep their labels; every crate whose label names an item is stocked, as the rename and /resupply do.
            yield return Stock(created.Where(c => c.Value.g.StartsWith("Container") && !string.IsNullOrEmpty(c.Value.text)).Select(c => c.Key).ToList(), automatic);
            yield return FillContents(created);

            if (automatic)
                yield return Boot.End(Boot.Base);
            else
                ChatHook.Say("Base placed (" + created.Count + " pieces). /undo takes it back.");
        }

        /// <summary>Puts into each new piece what the template says it holds (the seeds of a vegetube), through the game's own add-to-inventory call.</summary>
        internal static IEnumerator FillContents(List<KeyValuePair<WorldObject, TemplatePiece>> created)
        {
            var filled = 0;
            foreach (var made in created.Where(c => !string.IsNullOrEmpty(c.Value.fill)))
            {
                Inventory inventory = null;
                var until = Time.realtimeSinceStartup + 3f;
                while (inventory == null && Time.realtimeSinceStartup < until)
                {
                    inventory = made.Key.GetLinkedInventoryId() > 0 ? InventoriesHandler.Instance.GetInventoryById(made.Key.GetLinkedInventoryId()) : null;
                    if (inventory == null)
                        yield return null;
                }

                if (inventory == null)
                {
                    ModLog.Note("Could not fill " + made.Value.g + " #" + made.Key.GetId() + ": no inventory.");
                    continue;
                }

                foreach (var id in made.Value.fill.Split(','))
                {
                    var item = GroupsHandler.GetGroupViaId(id.Trim()) as GroupItem;
                    if (item == null)
                        continue;

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
                        ModLog.Note("Could not fill " + made.Value.g + " #" + made.Key.GetId() + ": " + e.GetType().Name + ": " + e.Message);
                        break;
                    }

                    until = Time.realtimeSinceStartup + 3f;
                    while (!done && Time.realtimeSinceStartup < until)
                        yield return null;

                    if (ok)
                        filled++;
                }
            }

            if (filled > 0)
                ModLog.Note("Filled " + filled + " items into pieces that hold something (" + string.Join(", ", created.Where(c => !string.IsNullOrEmpty(c.Value.fill)).GroupBy(c => c.Value.g).Select(g => g.Count() + " " + g.Key).ToArray()) + ").");
        }

        private IEnumerator Stock(List<WorldObject> crates, bool automatic)
        {
            var lookup = new ItemLookup(ModLog.FileIn("aliases.txt"));   // Base keeps its own names, in its own data folder
            int stocked = 0, items = 0, labelOnly = 0;
            foreach (var crate in crates)
            {
                var item = lookup.Resolve(CrateFill.Label(crate));
                if (item == null)
                {
                    labelOnly++; // a category label (plants, seeds, food): the crate is built empty with its label
                    continue;
                }

                var result = new CrateFill.Result();
                yield return CrateFill.Fill(crate, item, result);
                if (result.Added > 0)
                {
                    stocked++;
                    items += result.Added;
                }
            }

            ModLog.Note("Crates: " + stocked + " stocked from their labels (" + items + " items), " + labelOnly + " kept empty with a category label.");
            if (!automatic)
                ChatHook.Say("Stocked " + stocked + " crates from their labels (" + items + " items); " + labelOnly + " kept empty.");
        }

        /// <summary>Removes what this mod placed in the save and clears the save's "handled" mark. Returns how many pieces it removed.</summary>
        /// <summary>
        /// Takes back what this mod placed in the save on the planet the player is on. What it placed on another planet stays, and so does its record: undoing the base on one planet
        /// must never take pieces from another (the T0 base of Prime, built first, is not the base being tried on Humble).
        /// </summary>
        private static int Undo(string key, int planetHash)
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
            var kept = new List<string>();
            foreach (var line in mine)
            {
                var parts = line.Split('|');
                if (!int.TryParse(parts[parts.Length - 2], out var id))
                    continue;

                var wo = handler.GetWorldObjectViaId(id);
                if (wo == null)
                    continue;   // already gone

                if (wo.GetPlanetHash() != planetHash)
                {
                    kept.Add(line);   // placed on another planet: left alone
                    continue;
                }

                handler.DestroyWorldObject(id, true);
                removed++;
            }

            ModLog.Note("Undo: removed " + removed + " pieces on this planet" + (kept.Count > 0 ? "; " + kept.Count + " placed on another planet were left alone" : "") + ".");
            File.WriteAllLines(UndoFile, lines.Where(l => !l.StartsWith(key + "|")).Concat(kept).ToArray());
            if (kept.Count == 0)
                File.WriteAllLines(DoneFile, ModLog.ReadLines(DoneFile).Where(l => l != key).ToArray());
            return removed;
        }
    }
}
