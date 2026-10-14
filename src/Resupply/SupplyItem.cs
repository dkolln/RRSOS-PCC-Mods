using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Resupply
{
    /// <summary>Marks a placed Supply Manager screen, so that using it opens the Energy window in supply mode.</summary>
    internal sealed class SupplyScreenMarker : MonoBehaviour
    {
    }

    /// <summary>
    /// Adds a "Supply Manager" screen to the build menu (the Energy screen underneath; 1 Iron, 1 Silicon, 1 Aluminium) and marks every placed one. Using it opens the game's Energy window,
    /// which the SupplyWindow then fills with the supply controls instead of the energy lists. The game's own Energy screen is not touched. A placed screen is saved in the world like any
    /// other building; if this mod is removed the game drops it from the world the next time the save loads.
    /// </summary>
    internal sealed class SupplyItem : MonoBehaviour
    {
        public const string NewId = "Supply Manager";
        private const string SourceId = "ScreenEnergy";
        private static readonly string[] Ingredients = { "Iron", "Silicon", "Aluminium" };

        private Group _group;

        private void Awake()
        {
            try
            {
                var harmony = new Harmony("com.rrsos.pcc.resupply.screen");
                harmony.Patch(AccessTools.Method(typeof(ActionOpenUi), "OnAction"), prefix: new HarmonyMethod(typeof(SupplyItem), nameof(BeforeOpen)));
            }
            catch (Exception e)
            {
                ModLog.Note("Resupply: could not hook the screens: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Every use of a screen: the window is in supply mode only when it is a Supply Manager screen that was used.
        private static void BeforeOpen(ActionOpenUi __instance)
        {
            SupplyWindow.Mode = __instance != null && __instance.GetComponentInParent<SupplyScreenMarker>() != null;
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
                    EnsureRegistered();
                    MarkScreens();
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("supply-item", "Supply Manager screen: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // The game rebuilds its group lists when it reloads its static data, so check each time and put the group back if it is missing.
        private void EnsureRegistered()
        {
            if (GroupsHandler.GetAllGroups() == null)
                return;

            if (_group == null)
            {
                // the build menu's picture: the screen with the warehouse in it, put into CustomIcons once (a PNG dropped there instead is used as it is)
                Seed(NewId, "SupplyManager.png");

                _group = GroupCloner.Clone(SourceId, NewId, Ingredients);
                if (_group == null)
                    return;
                ModLog.Note("Supply Manager: created the build item from " + SourceId + " (" + string.Join(", ", Ingredients) + ").");
            }

            if (!GroupCloner.IsInGame(_group))
            {
                GroupCloner.AddToGame(_group);
                ModLog.Note("Supply Manager: added to the game's group lists.");
            }
        }

        // Copies a picture built into this mod to CustomIcons, unless a file of that name is there already (then that one is used, so a picture can be replaced by dropping a PNG in).
        private static void Seed(string iconName, string resource)
        {
            try
            {
                var path = CustomIcons.PathFor(iconName);
                if (System.IO.File.Exists(path))
                    return;

                using (var stream = typeof(SupplyItem).Assembly.GetManifestResourceStream(resource))
                {
                    if (stream == null)
                    {
                        ModLog.Note("Supply Manager: the picture " + resource + " is not in the mod.");
                        return;
                    }

                    System.IO.Directory.CreateDirectory(CustomIcons.Folder);
                    using (var file = System.IO.File.Create(path))
                        stream.CopyTo(file);
                }
            }
            catch (Exception e)
            {
                ModLog.Note("Supply Manager: could not put " + resource + " in CustomIcons: " + e.Message);
            }
        }

        // The placed screen shows the Energy screen's picture on its face; this puts the warehouse there instead (SupplyFace.png in CustomIcons: white on transparent, so the black of the screen shows).
        private Sprite _warehouse;

        private void Face(GameObject go)
        {
            try
            {
                if (_warehouse == null)
                {
                    Seed("SupplyFace", "SupplyFace.png");
                    _warehouse = CustomIcons.Load("SupplyFace", 100f);
                }

                var arrows = _warehouse;
                if (arrows == null)
                    return;

                var changed = 0;
                foreach (var image in go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (image.sprite == null)
                        continue;
                    image.sprite = arrows;
                    image.preserveAspect = true;
                    changed++;
                }

                foreach (var renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.sprite == null)
                        continue;
                    renderer.sprite = arrows;
                    changed++;
                }

                ModLog.Note("Supply Manager: put the warehouse on the face of a screen (" + changed + " pictures changed).");
            }
            catch (Exception e)
            {
                ModLog.LogOnce("supply-face", "Supply Manager screen face: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void MarkScreens()
        {
            if (_group == null || WorldObjectsHandler.Instance == null || !WorldState.Ready(out _, out _))
                return;

            foreach (var wo in WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() == _group).ToList())
            {
                var go = wo.GetGameObject();
                if (go == null || go.GetComponent<SupplyScreenMarker>() != null)
                    continue;

                go.AddComponent<SupplyScreenMarker>();
                Face(go);
            }
        }
    }
}
