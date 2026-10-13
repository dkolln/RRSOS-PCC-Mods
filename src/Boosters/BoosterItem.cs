using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Boosters
{
    /// <summary>Marks a placed Boosters screen, so that using it opens the Energy window in booster mode.</summary>
    internal sealed class BoosterScreenMarker : MonoBehaviour
    {
    }

    /// <summary>
    /// Adds a "Boosters" screen to the build menu (the Energy screen underneath; 1 Iron, 1 Aluminium, 1 Alloy) and marks every placed one. Using it opens the game's Energy window,
    /// which the BoosterWindow then fills with the booster controls instead of the energy lists. The game's own Energy screen is not touched. A placed screen is saved in the world like
    /// any other building; if this mod is removed the game drops it from the world the next time the save loads.
    /// </summary>
    internal sealed class BoosterItem : MonoBehaviour
    {
        public const string NewId = "Boosters";
        private const string SourceId = "ScreenEnergy";
        private static readonly string[] Ingredients = { "Iron", "Aluminium", "Alloy" };

        private Group _group;

        private void Awake()
        {
            try
            {
                var harmony = new Harmony("com.rrsos.pcc.boosters.screen");
                harmony.Patch(AccessTools.Method(typeof(ActionOpenUi), "OnAction"), prefix: new HarmonyMethod(typeof(BoosterItem), nameof(BeforeOpen)));
            }
            catch (Exception e)
            {
                ModLog.Note("Boosters: could not hook the screens: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Every use of a screen: the window is in booster mode only when it is a Boosters screen that was used.
        private static void BeforeOpen(ActionOpenUi __instance)
        {
            BoosterWindow.Mode = __instance != null && __instance.GetComponentInParent<BoosterScreenMarker>() != null;
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
                    ModLog.LogOnce("boosters-item", "Boosters screen: " + e.GetType().Name + ": " + e.Message);
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
                _group = GroupCloner.Clone(SourceId, NewId, Ingredients);
                if (_group == null)
                    return;
                ModLog.Note("Boosters: created the build item from " + SourceId + " (" + string.Join(", ", Ingredients) + ").");
            }

            if (!GroupCloner.IsInGame(_group))
            {
                GroupCloner.AddToGame(_group);
                ModLog.Note("Boosters: added to the game's group lists.");
            }
        }

        // The placed screen shows the Energy screen's picture on its face; this puts the gear there instead (BoosterGear.png in CustomIcons: white on transparent, so the black of the
        // screen shows).
        private Sprite _gear;

        private void Face(GameObject go)
        {
            try
            {
                if (_gear == null)
                    _gear = CustomIcons.Load("BoosterGear", 100f);
                if (_gear == null)
                    return;

                var changed = 0;
                foreach (var image in go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (image.sprite == null)
                        continue;
                    image.sprite = _gear;
                    image.preserveAspect = true;
                    changed++;
                }

                foreach (var renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.sprite == null)
                        continue;
                    renderer.sprite = _gear;
                    changed++;
                }

                ModLog.Note("Boosters: put the gear on the face of a screen (" + changed + " pictures changed).");
            }
            catch (Exception e)
            {
                ModLog.LogOnce("boosters-face", "Boosters screen face: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void MarkScreens()
        {
            if (_group == null || WorldObjectsHandler.Instance == null || !WorldState.Ready(out _, out _))
                return;

            foreach (var wo in WorldObjectsHandler.Instance.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() == _group).ToList())
            {
                var go = wo.GetGameObject();
                if (go == null || go.GetComponent<BoosterScreenMarker>() != null)
                    continue;

                go.AddComponent<BoosterScreenMarker>();
                Face(go);
            }
        }
    }
}
