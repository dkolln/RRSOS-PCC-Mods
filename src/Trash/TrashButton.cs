using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;

namespace RRSOS.PCC.Trash
{
    /// <summary>
    /// A small trash can on a container's window, one slot above the sort icon, on the left of its grid (the game's own icons there are sort, move all and the drone supply icon). One click deletes
    /// everything in that container for good. On a container window it is shown only for the groups in the Groups setting; on the personal inventory window (TrashOnPerson) it empties the backpack only, never the equipment.
    /// </summary>
    internal sealed class TrashButton : MonoBehaviour
    {
        private const string RootName = "RRSOS_TrashButton";
        private static readonly FieldInfo RightInventory = typeof(UiWindowContainer).GetField("_inventoryRight", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Sprite _sprite;

        /// <summary>What the button deletes: the container's inventory and what to call it in the log.</summary>
        private sealed class Target : MonoBehaviour
        {
            public Inventory Inventory;
            public string Name;
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
                    foreach (var window in Resources.FindObjectsOfTypeAll<UiWindowContainer>())
                    {
                        if (window == null || !window.gameObject.scene.IsValid() || !window.IsOpen)
                            continue;

                        Refresh(window);
                    }

                    foreach (var window in Resources.FindObjectsOfTypeAll<UiWindowEquipment>())
                    {
                        if (window == null || !window.gameObject.scene.IsValid() || !window.IsOpen)
                            continue;

                        RefreshBackpack(window);
                    }
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("trash-button", "Trash button: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        private static void Refresh(UiWindowContainer window)
        {
            var existing = window.transform.Find(RootName);
            var inventory = RightInventory?.GetValue(window) as Inventory;
            var owner = inventory == null ? null : OwnerOf(inventory);
            var grid = window.containerInventoryContainer == null ? null : window.containerInventoryContainer.GetComponentInChildren<GridLayoutGroup>();

            if (owner == null || grid == null || !Plugin.Groups.Value.Split(',').Select(g => g.Trim()).Contains(owner.GetGroup().GetId()))
            {
                if (existing != null)
                    existing.gameObject.SetActive(false);
                return;
            }

            var root = existing != null ? (RectTransform)existing : Build(window);
            root.gameObject.SetActive(true);

            var target = root.GetComponent<Target>();
            target.Inventory = inventory;
            target.Name = owner.GetGroup().GetId() + " #" + owner.GetId();

            Place(root, window.containerInventoryContainer.transform, grid);
        }

        /// <summary>The trash can on the personal inventory window; it deletes only what is in the backpack, never the equipment.</summary>
        private static void RefreshBackpack(UiWindowEquipment window)
        {
            var existing = window.transform.Find(RootName);
            var container = window.playerInventoryContainer;
            var grid = container == null ? null : container.GetComponentInChildren<GridLayoutGroup>();
            var player = Managers.GetManager<PlayersManager>()?.GetActivePlayerController();
            var inventory = player == null ? null : player.GetPlayerBackpack().GetInventory();

            if (!Plugin.OnPerson.Value || inventory == null || grid == null)
            {
                if (existing != null)
                    existing.gameObject.SetActive(false);
                return;
            }

            var root = existing != null ? (RectTransform)existing : Build(window);
            root.gameObject.SetActive(true);

            var target = root.GetComponent<Target>();
            target.Inventory = inventory;
            target.Name = "the backpack";

            Place(root, container.transform, grid);
        }

        private static void Place(RectTransform root, Transform container, GridLayoutGroup grid)
        {
            // One icon slot above the game's sort icon: the sort icon's centre plus the step from "MoveAll" up to "Order". Centres are in world space, so the UI's scale is allowed for.
            var scale = root.lossyScale.x;
            var order = Find(container, "Order");
            var moveAll = Find(container, "MoveAll");
            Vector3 centre;
            if (order != null && moveAll != null)
            {
                var o = order.TransformPoint(order.rect.center);
                centre = o + (o - moveAll.TransformPoint(moveAll.rect.center));
            }
            else
            {
                // No sort icon found: top left corner of the grid, a little to the left and up.
                var corners = new Vector3[4];
                ((RectTransform)grid.transform).GetWorldCorners(corners);
                centre = corners[1] + new Vector3(-40f * scale, 40f * scale, 0f);
            }

            root.position = centre + new Vector3(Plugin.NudgeX.Value * scale, Plugin.NudgeY.Value * scale, 0f);
        }

        private static RectTransform Find(Transform parent, string name)
        {
            return parent.GetComponentsInChildren<RectTransform>(false).FirstOrDefault(r => r.name == name);
        }

        private static WorldObject OwnerOf(Inventory inventory)
        {
            var id = inventory.GetId();
            return WorldObjectsHandler.Instance.GetAllWorldObjects().Values.FirstOrDefault(w => w != null && w.GetGroup() != null && w.GetLinkedInventoryId() == id);
        }

        private static RectTransform Build(Component window)
        {
            var go = new GameObject(RootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Target), typeof(LayoutElement));
            var rect = (RectTransform)go.transform;
            rect.SetParent(window.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(52f, 52f);
            go.GetComponent<LayoutElement>().ignoreLayout = true; // in case the window lays its children out itself

            var image = go.GetComponent<Image>();
            image.sprite = DrawSprite();
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.colors = new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, 0.9f),
                highlightedColor = new Color(1f, 0.5f, 0.45f, 1f),
                pressedColor = new Color(1f, 0.3f, 0.25f, 1f),
                selectedColor = new Color(1f, 1f, 1f, 0.9f),
                disabledColor = Color.gray,
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            var target = go.GetComponent<Target>();
            button.onClick.AddListener(() =>
            {
                if (target.Inventory != null)
                    EmptyNow(target.Inventory, target.Name);
            });

            ModLog.Note("Trash button added to a container window.");
            return rect;
        }

        /// <summary>Deletes everything inside one container, for good; the container itself and its label stay.</summary>
        private static void EmptyNow(Inventory inventory, string what)
        {
            var inventories = InventoriesHandler.Instance;
            var handler = WorldObjectsHandler.Instance;
            var deleted = 0;
            foreach (var item in inventory.GetInsideWorldObjects().ToList())
            {
                var id = item.GetId();
                try
                {
                    inventories.RemoveItemFromInventory(item, inventory, true);
                    if (handler.GetWorldObjectViaId(id) != null)
                        handler.DestroyWorldObject(id, true);
                    deleted++;
                }
                catch (Exception e)
                {
                    ModLog.Note("Could not delete " + item.GetGroup().GetId() + " #" + id + " from " + what + ": " + e.GetType().Name + ": " + e.Message);
                }
            }

            ModLog.Note("Trash button: " + deleted + " items deleted from " + what + "; " + inventory.GetInsideWorldObjects().Count + " left.");
        }

        /// <summary>A white trash can, drawn once, transparent around it.</summary>
        private static Sprite DrawSprite()
        {
            if (_sprite != null)
                return _sprite;

            const int n = 48;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = Enumerable.Repeat(new Color(0f, 0f, 0f, 0f), n * n).ToArray();

            void Fill(int x0, int y0, int x1, int y1)
            {
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                        pixels[y * n + x] = Color.white;
            }

            Fill(6, 36, 41, 39);   // lid
            Fill(18, 40, 29, 43);  // handle
            Fill(10, 6, 12, 35);   // body, left wall
            Fill(35, 6, 37, 35);   // body, right wall
            Fill(10, 6, 37, 8);    // body, floor
            Fill(18, 13, 19, 30);  // slots
            Fill(23, 13, 24, 30);
            Fill(28, 13, 29, 30);

            texture.SetPixels(pixels);
            texture.Apply();
            _sprite = Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _sprite;
        }
    }
}
