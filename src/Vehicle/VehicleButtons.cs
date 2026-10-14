using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RRSOS.PCC.Shared;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RRSOS.PCC.Vehicle
{
    /// <summary>
    /// Three buttons on the vehicle station's window (the game's crafting window, opened from the station's terminal, so they are there only at a vehicle station):
    ///   Spawn Rover   puts a new rover, with no gear, on the station's own spawn spot (the game's own spawn call; nothing happens while the spot is taken).
    ///   Gear Min      gives the rover at the station the first tier of every piece of rover gear it has no tier of yet.
    ///   Gear Max      leaves the rover with exactly the top tier of every piece of rover gear: other gear is taken out and deleted, what is already the top tier stays.
    /// Gear is whatever item groups the game calls Vehicle...N (equipment size, inventory size, speed, oxygen, lights, beacon), grouped by the name without its tier number. It goes into the
    /// rover's own equipment inventory through the game's add-to-inventory call, so the rover reacts as when the gear is put in by hand.
    /// </summary>
    internal sealed class VehicleButtons : MonoBehaviour
    {
        private const string RootName = "RRSOS_VehicleButtons";
        private const float WaitForItemSeconds = 3f;
        private static readonly Regex GearId = new Regex(@"^(Vehicle[A-Za-z]+?)(\d+)$");
        private static readonly FieldInfo SourceCrafter = typeof(UiWindowCraft).GetField("sourceCrafter", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Color Lit = new Color(0.45f, 1f, 0.55f, 1f);
        private static readonly Color Dim = new Color(0.65f, 0.7f, 0.75f, 0.85f);
        private static readonly Color Face = new Color(0.04f, 0.06f, 0.08f, 0.72f);

        private bool _busy;
        private TMP_Text _status;

        /// <summary>The station the buttons are working for: the crafter whose window is open.</summary>
        private sealed class Station : MonoBehaviour
        {
            public ActionCrafter Crafter;
        }

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.5f);

                if (!Plugin.Enabled.Value)
                    continue;

                try
                {
                    foreach (var window in Resources.FindObjectsOfTypeAll<UiWindowCraft>())
                    {
                        if (window == null || !window.gameObject.scene.IsValid())
                            continue;

                        var crafter = window.IsOpen ? SourceCrafter?.GetValue(window) as ActionCrafter : null;
                        var existing = window.transform.Find(RootName);
                        var isStation = crafter != null && crafter.GetCrafterIdentifier() == DataConfig.CraftableIn.CraftVehicleT1;
                        if (!isStation)
                        {
                            if (existing != null)
                                existing.gameObject.SetActive(false);
                            continue;
                        }

                        var root = existing != null ? (RectTransform)existing : Build(window);
                        root.gameObject.SetActive(true);
                        root.GetComponent<Station>().Crafter = crafter;
                    }
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("vehicle-buttons", "Vehicle buttons: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // ---- the buttons ------------------------------------------------------------------------------------------------------

        private RectTransform Build(UiWindowCraft window)
        {
            var root = new GameObject(RootName, typeof(RectTransform), typeof(Station), typeof(LayoutElement));
            var rect = (RectTransform)root.transform;
            rect.SetParent(window.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(Plugin.ButtonsX.Value, Plugin.ButtonsY.Value);
            rect.sizeDelta = new Vector2(960f, 130f);
            root.GetComponent<LayoutElement>().ignoreLayout = true;   // in case the window lays its children out itself

            var station = root.GetComponent<Station>();
            var font = window.GetComponentInChildren<TMP_Text>(true);
            Button(rect, "Spawn Rover", new Vector2(-320f, 66f), font, () => Run(station, Spawn));
            Button(rect, "Gear Min", new Vector2(0f, 66f), font, () => Run(station, (s, planet, say) => Upgrade(false, s.Crafter.GetSpawnPosition(), planet, say)));
            Button(rect, "Gear Max", new Vector2(320f, 66f), font, () => Run(station, (s, planet, say) => Upgrade(true, s.Crafter.GetSpawnPosition(), planet, say)));
            _status = Label(rect, "", new Vector2(0f, 8f), font);
            ModLog.Note("Vehicle buttons added to the vehicle station window.");
            return rect;
        }

        private void Run(Station station, Func<Station, int, Action<string>, IEnumerator> work)
        {
            if (_busy)
            {
                Say("Still busy with the last one.");
                return;
            }

            if (station.Crafter == null || !WorldState.Ready(out var planetHash, out _))
            {
                Say("The world is not ready yet.");
                return;
            }

            StartCoroutine(Guarded(work(station, planetHash, Say)));
        }

        private IEnumerator Guarded(IEnumerator work)
        {
            _busy = true;
            try
            {
                yield return work;
            }
            finally
            {
                _busy = false;
            }
        }

        private void Say(string message)
        {
            if (_status != null)
                _status.text = message;
            ModLog.Note("Vehicle: " + message);
        }

        private static TMP_Text Button(RectTransform parent, string label, Vector2 at, TMP_Text fontFrom, Action onClick)
        {
            var go = new GameObject("Button " + label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UnityEngine.UI.Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(300f, 56f);
            rect.anchoredPosition = at;

            var face = go.GetComponent<Image>();
            face.color = Face;
            var button = go.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(() => onClick());

            var text = Label(rect, label, Vector2.zero, fontFrom);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            text.fontSize = 28f;
            text.color = Lit;
            return text;
        }

        private static TMP_Text Label(RectTransform parent, string text, Vector2 at, TMP_Text fontFrom)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(940f, 48f);
            rect.anchoredPosition = at;
            var label = go.GetComponent<TextMeshProUGUI>();
            if (fontFrom != null)
            {
                label.font = fontFrom.font;
                label.fontSharedMaterial = fontFrom.fontSharedMaterial;
            }

            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 22f;
            label.color = Dim;
            label.raycastTarget = false;   // the button's face takes the click
            return label;
        }

        // ---- spawn ------------------------------------------------------------------------------------------------------------

        // The game's own crafting spawn: the rover appears on the station's spawn spot, free of cost. While something stands on the spot nothing is spawned.
        private IEnumerator Spawn(Station station, int planetHash, Action<string> say)
        {
            var group = GroupsHandler.GetGroupViaId(Plugin.VehicleGroup.Value);
            if (group == null)
            {
                say("The game has no group " + Plugin.VehicleGroup.Value + ".");
                yield break;
            }

            var done = false;
            GameObject made = null;
            bool started;
            try
            {
                started = WorldObjectsHandler.Instance.CreateAndInstantiateWorldObject(group, station.Crafter.GetSpawnPosition(), station.Crafter.GetSpawnRotation(),
                    disolve: true, checkSpawnPosition: true, save: true, addDeconstructIcon: false, result: go =>
                    {
                        made = go;
                        done = true;
                    });
            }
            catch (Exception e)
            {
                ModLog.Note("Spawn: " + e.GetType().Name + ": " + e.Message);
                say("Could not spawn the rover: " + e.Message);
                yield break;
            }

            if (!started)
            {
                say("The game is busy spawning something; try again in a moment.");
                yield break;
            }

            var until = Time.realtimeSinceStartup + 10f;
            while (!done && Time.realtimeSinceStartup < until)
                yield return null;

            say(!done ? "No answer from the game; see the log." : made == null ? "The spawn spot is taken. Move what is on the station first." : "Rover spawned, with no gear.");
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

        /// <summary>The rover closest to the given spot (the station's), standing in the world on this planet; null when there is none.</summary>
        private static WorldObject NearestVehicle(Vector3 near, int planetHash)
        {
            return WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                .Where(w => w != null && w.GetGroup() != null && w.GetGroup().GetId() == Plugin.VehicleGroup.Value && EquipmentInventory(w) != null
                    && w.GetPosition().sqrMagnitude > 0f && w.GetPlanetHash() == planetHash)
                .OrderBy(w => (w.GetPosition() - near).sqrMagnitude).FirstOrDefault();
        }

        private IEnumerator Upgrade(bool max, Vector3 station, int planetHash, Action<string> say)
        {
            var vehicle = NearestVehicle(station, planetHash);
            if (vehicle == null)
            {
                say("There is no rover to upgrade. Spawn one first.");
                yield break;
            }

            var inventory = EquipmentInventory(vehicle);
            var families = Families();
            if (families.Count == 0)
            {
                say("The game has no rover gear groups (Vehicle...). See the log.");
                ModLog.Note("Gear: no group matched " + GearId);
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
                        ModLog.Note("Gear: could not remove " + name + " #" + id + ": " + e.GetType().Name + ": " + e.Message);
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
                    ModLog.Note("Gear: adding " + item.GetId() + " failed: " + e.GetType().Name + ": " + e.Message);
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

            var summary = (max ? "Max" : "Min") + ": " + (removed.Count > 0 ? "removed " + string.Join(", ", removed.ToArray()) + "; " : "")
                + (added.Count > 0 ? "added " + string.Join(", ", added.ToArray()) : "nothing to add");
            say(summary + "." + (failed.Count > 0 ? " Could not add: " + string.Join(", ", failed.ToArray()) + " (no room?)." : "") + " (rover #" + vehicle.GetId() + ")");
        }
    }
}
