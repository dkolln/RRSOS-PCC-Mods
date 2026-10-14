using System;
using System.Collections;
using System.Collections.Generic;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Unlocks
{
    /// <summary>
    /// Watches the same unlocks the game's own alert watches (the items that unlock at a level of terraformation, heat, oxygen and so on) and, in a free-build game only, says
    /// "Unlocked: Solar Panel" in the chat when one is reached. In a normal game the game shows its own popup, so nothing is said. What was already unlocked when a world opened
    /// is not news.
    /// </summary>
    internal sealed class UnlockNotice : MonoBehaviour
    {
        // The units the game's own alert looks at.
        private static readonly DataConfig.WorldUnitType[] Units =
        {
            DataConfig.WorldUnitType.Heat,
            DataConfig.WorldUnitType.Oxygen,
            DataConfig.WorldUnitType.Pressure,
            DataConfig.WorldUnitType.Plants,
            DataConfig.WorldUnitType.Insects,
            DataConfig.WorldUnitType.Animals,
            DataConfig.WorldUnitType.Biomass,
            DataConfig.WorldUnitType.Terraformation,
            DataConfig.WorldUnitType.SystemTerraformation,
            DataConfig.WorldUnitType.Purification,
        };

        private readonly HashSet<string> _known = new HashSet<string>();
        private string _world;
        private bool _seeded;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);

                if (!Plugin.Enabled.Value)
                    continue;

                try
                {
                    Check();
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("unlocks-check", "Could not check the unlocks: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // For the log: where terraformation stands and what unlocks next (the same sum the game unlocks by).
        private static string NextUnlock(UnlockingHandler handler)
        {
            try
            {
                var planet = PlanetNetworkLoader.Instance.GetStartingPlanetData().GetPlanetId();
                var now = Managers.GetManager<WorldUnitsHandler>().GetUnit(DataConfig.WorldUnitType.Terraformation, planet).GetValue();
                var next = handler.GetUnlockableGroupsOverUnit(DataConfig.WorldUnitType.Terraformation);
                return "Terraformation " + now + (next.Count == 0 ? "; nothing more to unlock by it." : "; next: " + Readable.GetGroupName(next[0]) + " at " + next[0].GetUnlockingInfos().GetUnlockingValue() + ".");
            }
            catch (Exception e)
            {
                return "(could not read terraformation: " + e.Message + ")";
            }
        }

        private void Check()
        {
            if (!WorldState.Ready(out _, out var key))
                return;

            var settings = Managers.GetManager<GameSettingsHandler>().GetCurrentGameSettings();
            var handler = Managers.GetManager<UnlockingHandler>();
            if (handler == null)
                return;

            if (key != _world)
            {
                _world = key;
                _known.Clear();
                _seeded = false;
            }

            var fresh = new List<Group>();
            foreach (var unit in Units)
                foreach (var group in handler.GetUnlockableGroupsUnderUnit(unit))
                    if (_known.Add(group.GetId()))
                        fresh.Add(group);

            if (!_seeded)
            {
                _seeded = true; // what was unlocked when the world opened is not news
                ModLog.Note("World " + key + ": " + _known.Count + " items already unlocked; free build: " + settings.GetFreeCraft() + ". " + NextUnlock(handler));
                return;
            }

            foreach (var group in fresh)
            {
                ModLog.Note("Unlocked: " + group.GetId() + (settings.GetFreeCraft() ? "" : " (the game shows its own popup)") + ". " + NextUnlock(handler));
                if (!settings.GetFreeCraft())
                    continue;

                // On the terminal's screens only (nothing is shown when there is no terminal).
                TerminalRegistration.Notice("Unlocked: " + Readable.GetGroupName(group));
            }
        }
    }
}
