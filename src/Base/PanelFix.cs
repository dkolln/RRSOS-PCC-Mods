using System;
using System.Reflection;
using HarmonyLib;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Base
{
    /// <summary>
    /// Keeps the deconstruct tool working around pod doors and windows. The game's ActionPanelDeconstruct.OnHover (what runs while the tool is aimed at a panel) can throw a
    /// NullReferenceException; the game's aiming then stops for everything under the aim, so nothing around that panel can be deconstructed, the panel included (seen on the door of a
    /// pod that was taken away). The exception is swallowed after it is written to the Base log once, with what was missing; the panel's own deconstruct still works, as it does not use
    /// what the hover needed. Nothing else about the hover is changed.
    /// </summary>
    internal static class PanelFix
    {
        private static readonly FieldInfo PanelField = AccessTools.Field(typeof(ActionPanelDeconstruct), "panel");

        public static void Apply()
        {
            try
            {
                var harmony = new Harmony("com.rrsos.pcc.base.panelfix");
                // Also the hover of anything that opens (a crate): when its text proxy is missing the hover throws, which stops the game's aiming coroutine for good, so every look afterwards
                // keeps the last thing aimed at (always the bottom crate of a stack, wherever the player looks).
                foreach (var type in new[] { typeof(ActionPanelDeconstruct), typeof(ActionOpenable) })
                    foreach (var name in new[] { "OnHover", "OnHoverOut" })
                        harmony.Patch(AccessTools.Method(type, name), finalizer: new HarmonyMethod(typeof(PanelFix), nameof(Swallow)));
            }
            catch (Exception e)
            {
                ModLog.Note("Could not guard the panel hover: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static Exception Swallow(Exception __exception, MonoBehaviour __instance)
        {
            if (__exception == null)
                return null;

            try
            {
                var path = "";
                for (var t = __instance == null ? null : __instance.transform; t != null; t = t.parent)
                    path = "/" + t.name + path;
                var panel = PanelField == null || !(__instance is ActionPanelDeconstruct) ? null : PanelField.GetValue(__instance);
                ModLog.LogOnce("hover-" + (__instance == null ? "" : __instance.GetType().Name), "Hover of " + (__instance == null ? "?" : __instance.GetType().Name) + ": " + __exception.GetType().Name + " at " + path + " (panel: " + (panel == null ? "missing" : "present")
                    + ", GamepadConfig: " + (GamepadConfig.Instance == null ? "missing" : "present") + "): " + __exception.Message + " " + __exception.StackTrace);
            }
            catch
            {
                // reporting must never make it worse
            }

            return null;
        }
    }
}
