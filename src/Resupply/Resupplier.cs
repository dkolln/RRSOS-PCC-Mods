using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// A recycle-arrows icon on a crate's window. One click fills that crate's empty slots with the item it is meant for (CrateFill.ItemFor: its label, or else its demand when that is
    /// exactly one item); what is in the crate stays. The icon sits one slot above the game's sort icon, or two when the Trash mod's can is showing there. The same fill serves
    /// /resupply on the terminal, which goes through every such crate on the planet.
    /// </summary>
    internal sealed class Resupplier : MonoBehaviour
    {
        private const string RootName = "RRSOS_ResupplyButton";
        private const string TrashName = "RRSOS_TrashButton"; // the Trash mod's can; found by name, so the mods do not depend on each other
        private static readonly FieldInfo RightInventory = typeof(UiWindowContainer).GetField("_inventoryRight", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Sprite _sprite;

        private readonly ItemLookup _lookup = new ItemLookup();
        private bool _busy;
        private bool _terminalChecked;

        /// <summary>The one Resupplier, for the Supply Manager screen.</summary>
        internal static Resupplier Instance;

        internal bool Busy => _busy;
        internal ItemLookup Lookup => _lookup;

        private void Awake()
        {
            Instance = this;
            StartCoroutine(AutoTopUp());
        }

        // Every TopUpSeconds, while AutoTopUp is on: crates that have an item and are below TopUpBelowPercent full are topped up. Off unless the player switched it on.
        private IEnumerator AutoTopUp()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(5f, Plugin.TopUpSeconds.Value));

                if (!Plugin.Enabled.Value || !Plugin.AutoTopUp.Value || _busy)
                    continue;

                var lines = new List<string>();
                yield return ResupplyAll(line => lines.Add(line), Mathf.Clamp(Plugin.TopUpBelowPercent.Value, 1, 99));
                var done = lines.LastOrDefault(l => l.StartsWith("Done:")) ?? "";
                if (done.Length > 0 && !done.StartsWith("Done: 0 crates topped up"))
                    ChatHook.Say("Auto top-up. " + done);
            }
        }

        /// <summary>What the icon refills: the crate it is showing.</summary>
        private sealed class Target : MonoBehaviour
        {
            public WorldObject Crate;
        }

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.5f);

                if (!Plugin.Enabled.Value)
                    continue;

                if (!_terminalChecked)
                    RegisterWithTerminal();

                try
                {
                    foreach (var window in Resources.FindObjectsOfTypeAll<UiWindowContainer>())
                    {
                        if (window == null || !window.gameObject.scene.IsValid() || !window.IsOpen)
                            continue;

                        Refresh(window);
                    }
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("resupply-button", "Resupply icon: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // ---- the icon ---------------------------------------------------------------------------------------------------------

        private static HashSet<string> Groups() =>
            new HashSet<string>(Plugin.Groups.Value.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0));

        private void Refresh(UiWindowContainer window)
        {
            var existing = window.transform.Find(RootName);
            var inventory = RightInventory?.GetValue(window) as Inventory;
            var crate = inventory == null ? null : OwnerOf(inventory);
            var container = window.containerInventoryContainer;
            var grid = container == null ? null : container.GetComponentInChildren<GridLayoutGroup>();
            var item = crate == null || !Groups().Contains(crate.GetGroup().GetId()) ? null : CrateFill.ItemFor(crate, _lookup);

            if (item == null || grid == null)
            {
                if (existing != null)
                    existing.gameObject.SetActive(false);
                return;
            }

            var root = existing != null ? (RectTransform)existing : Build(window);
            root.gameObject.SetActive(true);
            root.GetComponent<Target>().Crate = crate;

            // One icon slot above the sort icon (two when the trash can is there): the sort icon's centre plus the step from "MoveAll" up to "Order". World space, so the UI's scale is allowed for.
            var trash = window.transform.Find(TrashName);
            var slots = trash != null && trash.gameObject.activeInHierarchy ? 2 : 1;
            var scale = root.lossyScale.x;
            var order = Find(container.transform, "Order");
            var moveAll = Find(container.transform, "MoveAll");
            Vector3 centre;
            if (order != null && moveAll != null)
            {
                var o = order.TransformPoint(order.rect.center);
                centre = o + (o - moveAll.TransformPoint(moveAll.rect.center)) * slots;
            }
            else
            {
                var corners = new Vector3[4];
                ((RectTransform)grid.transform).GetWorldCorners(corners);
                centre = corners[1] + new Vector3(-40f * scale, (40f + 56f * (slots - 1)) * scale, 0f);
            }

            root.position = centre + new Vector3(Plugin.NudgeX.Value * scale, Plugin.NudgeY.Value * scale, 0f);
        }

        private static RectTransform Find(Transform parent, string name) =>
            parent.GetComponentsInChildren<RectTransform>(false).FirstOrDefault(r => r.name == name);

        private static WorldObject OwnerOf(Inventory inventory)
        {
            var id = inventory.GetId();
            return WorldObjectsHandler.Instance.GetAllWorldObjects().Values.FirstOrDefault(w => w != null && w.GetGroup() != null && w.GetLinkedInventoryId() == id);
        }

        private RectTransform Build(UiWindowContainer window)
        {
            var go = new GameObject(RootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Target), typeof(LayoutElement));
            var rect = (RectTransform)go.transform;
            rect.SetParent(window.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(52f, 52f);
            go.GetComponent<LayoutElement>().ignoreLayout = true;

            var image = go.GetComponent<Image>();
            image.sprite = DrawSprite();
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.colors = new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, 0.9f),
                highlightedColor = new Color(0.55f, 1f, 0.6f, 1f),
                pressedColor = new Color(0.3f, 0.9f, 0.4f, 1f),
                selectedColor = new Color(1f, 1f, 1f, 0.9f),
                disabledColor = Color.gray,
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            var target = go.GetComponent<Target>();
            button.onClick.AddListener(() =>
            {
                if (target.Crate != null && !_busy)
                    StartCoroutine(FillOne(target.Crate));
            });

            ModLog.Note("Resupply icon added to a container window.");
            return rect;
        }

        private IEnumerator FillOne(WorldObject crate)
        {
            _busy = true;
            try
            {
                var item = CrateFill.ItemFor(crate, _lookup);
                if (item == null)
                    yield break;

                var result = new CrateFill.Result();
                yield return CrateFill.Fill(crate, item, result);

                if (result.Empty < 0)
                    yield break;

                var name = Readable.GetGroupName(item);
                ChatHook.Say(result.Empty == 0
                    ? "The crate has no empty slots (" + name + ")."
                    : "Resupplied " + result.Added + " x " + name + ".");
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Two arrows chasing each other round a circle, white on transparent, drawn once.</summary>
        internal static Sprite DrawSprite()
        {
            if (_sprite != null)
                return _sprite;

            const int n = 48;
            const float cx = 23.5f, cy = 23.5f, radius = 15f, half = 3f, length = 130f;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = Enumerable.Repeat(new Color(0f, 0f, 0f, 0f), n * n).ToArray();

            float Side(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

            bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                var d1 = Side(p, a, b);
                var d2 = Side(p, b, c);
                var d3 = Side(p, c, a);
                return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
            }

            Vector2 OnCircle(float degrees, float r) => new Vector2(cx + r * Mathf.Cos(degrees * Mathf.Deg2Rad), cy + r * Mathf.Sin(degrees * Mathf.Deg2Rad));

            // Two arcs of 130 degrees, each ending in an arrowhead that points the way round (anticlockwise).
            float[] starts = { 10f, 190f };
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    var p = new Vector2(x, y);
                    var dx = x - cx;
                    var dy = y - cy;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (angle < 0f)
                        angle += 360f;

                    var on = false;
                    foreach (var start in starts)
                    {
                        var end = start + length;
                        if (Mathf.Abs(r - radius) <= half && angle >= start && angle <= end)
                            on = true;
                        else if (InTriangle(p, OnCircle(end, radius + 7.5f), OnCircle(end, radius - 7.5f), OnCircle(end + 28f, radius)))
                            on = true;
                    }

                    if (on)
                        pixels[y * n + x] = Color.white;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            _sprite = Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _sprite;
        }

        // ---- /resupply (on the terminal) ----------------------------------------------------------------------------------------

        // The terminal is optional. When its mod is loaded this puts /resupply on it; otherwise there is nothing to do.
        private void RegisterWithTerminal()
        {
            try
            {
                if (TerminalRegistration.TryRegister("/resupply", "/resupply", "top up every labelled or demanded crate", OnResupply))
                {
                    _terminalChecked = true;
                    ModLog.Note("The terminal is installed: /resupply is on it.");
                }
            }
            catch (Exception e)
            {
                _terminalChecked = true; // do not try again every half second
                ModLog.Note("Could not register /resupply with the terminal: " + e.Message);
            }
        }

        private void OnResupply(string args, Action<string> print)
        {
            if (_busy)
            {
                print("Busy filling crates; try again in a moment.");
                return;
            }

            StartCoroutine(ResupplyAll(print));
        }

        /// <summary>
        /// Every crate on this planet (the Groups setting) that has an item, by label or demand, is topped up the same way the icon does it. Crates with neither are left alone, and so is everything already in a crate.
        /// </summary>
        internal IEnumerator ResupplyAll(Action<string> print, int belowPercent = 100)
        {
            _busy = true;
            try
            {
                if (!WorldState.Ready(out var planetHash, out _))
                {
                    print("The world is not ready yet.");
                    yield break;
                }

                var groups = Groups();
                var crates = WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                    .Where(w => w != null && w.GetGroup() != null && groups.Contains(w.GetGroup().GetId()) && w.GetLinkedInventoryId() != 0 && w.GetPlanetHash() == planetHash).ToList();

                print("Resupplying " + crates.Count + " crates...");
                int toppedUp = 0, full = 0, noItem = 0, total = 0;
                foreach (var crate in crates)
                {
                    var item = CrateFill.ItemFor(crate, _lookup);
                    if (item == null)
                    {
                        noItem++;
                        continue;
                    }

                    // the auto top-up leaves crates that are still full enough alone
                    if (belowPercent < 100)
                    {
                        var inventory = InventoriesHandler.Instance.GetInventoryById(crate.GetLinkedInventoryId());
                        if (inventory == null || inventory.GetSize() == 0 || inventory.GetInsideWorldObjects().Count * 100 >= belowPercent * inventory.GetSize())
                        {
                            full++;
                            continue;
                        }
                    }

                    var result = new CrateFill.Result();
                    yield return CrateFill.Fill(crate, item, result);
                    if (result.Added > 0)
                    {
                        toppedUp++;
                        total += result.Added;
                        print(Readable.GetGroupName(item) + ": +" + result.Added);
                    }
                    else if (result.Empty == 0)
                    {
                        full++;
                    }
                }

                print("Done: " + toppedUp + " crates topped up (" + total + " items), " + full + " already full, " + noItem + " with no item by label or demand.");
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
