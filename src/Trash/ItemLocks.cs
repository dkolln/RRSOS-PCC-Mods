using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RRSOS.PCC.Trash
{
    /// <summary>
    /// Locked items: a middle click (or right click, a setting) on an item in any inventory window locks it, and again unlocks it. A locked item shows the game's own lock icon, is skipped by the
    /// trash can, and stays behind when Move All moves an inventory's contents. Which items are locked is kept per save in locks.txt (an item's id is unique within a save).
    /// </summary>
    internal static class ItemLocks
    {
        private static readonly HashSet<int> Ids = new HashSet<int>();
        private static string _loadedFor;

        private static string File => ModLog.FileIn("locks.txt");

        public static bool IsLocked(int worldObjectId)
        {
            Load();
            return Ids.Contains(worldObjectId);
        }

        public static bool IsLocked(WorldObject item) => item != null && IsLocked(item.GetId());

        public static void Toggle(WorldObject item)
        {
            Load();
            if (!Ids.Remove(item.GetId()))
                Ids.Add(item.GetId());

            Save();
            ModLog.Note("Lock: " + item.GetGroup().GetId() + " #" + item.GetId() + (Ids.Contains(item.GetId()) ? " locked." : " unlocked."));
        }

        private static string Key()
        {
            return WorldState.Ready(out _, out var key) ? key : null;
        }

        private static void Load()
        {
            string key;
            try
            {
                key = Key();
            }
            catch (Exception e)
            {
                ModLog.LogOnce("locks-key", "Lock: could not tell which save this is: " + e.Message);
                return;
            }

            if (key == null || key == _loadedFor)
                return;

            _loadedFor = key;
            Ids.Clear();
            foreach (var line in ModLog.ReadLines(File))
            {
                var parts = line.Split('|');
                if (parts.Length == 2 && parts[0] == key && int.TryParse(parts[1], out var id))
                    Ids.Add(id);
            }
        }

        private static void Save()
        {
            if (_loadedFor == null)
                return;

            // other saves' locks stay in the file
            var others = ModLog.ReadLines(File).Where(l => !l.StartsWith(_loadedFor + "|")).ToList();
            others.AddRange(Ids.OrderBy(i => i).Select(i => _loadedFor + "|" + i));
            System.IO.File.WriteAllLines(File, others.ToArray());
        }
    }

    /// <summary>On an inventory slot with an item in it: shows the lock and takes the lock click.</summary>
    internal sealed class LockSlot : MonoBehaviour, IPointerClickHandler
    {
        public WorldObject Item;
        public InventoryBlock Block;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Plugin.Enabled.Value || !Plugin.LockItems.Value || Item == null || eventData.button != ClickButton())
                return;

            ItemLocks.Toggle(Item);
            var displayer = GetComponentInParent<InventoryDisplayer>();
            if (displayer != null)
                displayer.RefreshContent();
        }

        internal static PointerEventData.InputButton ClickButton() =>
            Plugin.LockClick.Value == "Right" ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Middle;

        // The block shows the game's lock icon in its own Update (for the lock it has after a reactor takes an item) and hides it again when it starts; showing ours afterwards, every frame, wins.
        private void LateUpdate()
        {
            if (Item == null || Block == null || !Plugin.Enabled.Value || !Plugin.LockItems.Value || !ItemLocks.IsLocked(Item))
                return;

            if (Block.lockedImage != null && !Block.lockedImage.activeSelf)
                Block.lockedImage.SetActive(true);
            if (Block.groupImage != null)
                Block.groupImage.color = new Color(1f, 1f, 1f, 0.5f);
        }
    }

    /// <summary>The hooks into the game's inventory windows.</summary>
    internal static class LockPatches
    {
        public static void Apply()
        {
            var harmony = new Harmony("com.rrsos.pcc.trash.locks");
            harmony.Patch(AccessTools.Method(typeof(InventoryBlock), "SetDisplay"), postfix: new HarmonyMethod(typeof(LockPatches), nameof(AfterSetDisplay)));
            harmony.Patch(AccessTools.Method(typeof(InventoryDisplayer), "OnImageClicked"), prefix: new HarmonyMethod(typeof(LockPatches), nameof(BeforeImageClicked)));
            harmony.Patch(AccessTools.Method(typeof(InventoriesHandler), "AMoveAllInventoryHasBeenClicked"), prefix: new HarmonyMethod(typeof(LockPatches), nameof(BeforeMoveAll)));
        }

        // every item slot in an inventory window gets the lock behaviour
        private static void AfterSetDisplay(InventoryBlock __instance, WorldObject _worldObject)
        {
            try
            {
                if (_worldObject == null || __instance.GetComponentInParent<InventoryDisplayer>() == null)
                    return;

                var slot = __instance.GetComponent<LockSlot>() ?? __instance.gameObject.AddComponent<LockSlot>();
                slot.Item = _worldObject;
                slot.Block = __instance;
            }
            catch (Exception e)
            {
                ModLog.LogOnce("locks-slot", "Lock: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // With the lock on the right button, that click must not also use the item (eat, drink, place).
        private static bool BeforeImageClicked(EventTriggerCallbackData eventTriggerCallbackData)
        {
            try
            {
                if (Plugin.Enabled.Value && Plugin.LockItems.Value && eventTriggerCallbackData.pointerEventData != null && eventTriggerCallbackData.pointerEventData.button == LockSlot.ClickButton()
                    && eventTriggerCallbackData.pointerEventData.button == PointerEventData.InputButton.Right)
                    return false;
            }
            catch (Exception e)
            {
                ModLog.LogOnce("locks-click", "Lock: " + e.GetType().Name + ": " + e.Message);
            }

            return true;
        }

        // Move All: when the inventory holds locked items, move the others one by one through the game's own single-item transfer. With no locked item the game's own Move All runs as before.
        private static bool BeforeMoveAll(InventoriesHandler __instance, Inventory inventoryClicked)
        {
            try
            {
                if (!Plugin.Enabled.Value || !Plugin.LockItems.Value || inventoryClicked == null || !inventoryClicked.GetInsideWorldObjects().Any(i => ItemLocks.IsLocked(i)))
                    return true;

                var queue = AccessTools.Field(typeof(InventoriesHandler), "_callbackAddOrRemoveQueue")?.GetValue(__instance) as System.Collections.ICollection;
                if (queue != null && queue.Count > 0)
                    return false; // the game ignores a Move All while another transfer is under way

                var openedUi = Managers.GetManager<WindowsHandler>().GetOpenedUi();
                if (openedUi != DataConfig.UiType.Container && openedUi != DataConfig.UiType.Genetics && openedUi != DataConfig.UiType.GroupSelector
                    && openedUi != DataConfig.UiType.DNAExtractor && openedUi != DataConfig.UiType.DNASynthetizer)
                    return false;

                var window = (UiWindowContainer)Managers.GetManager<WindowsHandler>().GetWindowViaUiId(openedUi);
                var other = window.GetOtherInventory(inventoryClicked);
                if (other == null)
                    return false;

                var items = inventoryClicked.GetInsideWorldObjects().Where(i => !ItemLocks.IsLocked(i)).Reverse().ToList();
                foreach (var item in items)
                    __instance.TransferItem(inventoryClicked, other, item);
            }
            catch (Exception e)
            {
                ModLog.LogOnce("locks-moveall", "Lock: Move All: " + e.GetType().Name + ": " + e.Message);
                return true;
            }

            return false;
        }
    }
}
