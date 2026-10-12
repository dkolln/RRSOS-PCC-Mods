using System.Collections;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Loadout
{
    /// <summary>
    /// Fits the loadout by itself at the start of a new game, once per save. A game is new when the player has crafted nothing yet, so a game already under way (or a save
    /// loaded later) is never touched; /loadout still does it on request.
    /// </summary>
    internal sealed class PlanetfallLoadout : MonoBehaviour
    {
        private static string DoneFile => ModLog.FileIn("done.txt");

        private LoadoutCommand _command;

        public void Init(LoadoutCommand command) => _command = command;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);

                if (!Plugin.Enabled.Value || !Plugin.AutoAtPlanetfall.Value || _command == null)
                    continue;

                if (!WorldState.Ready(out _, out var key))
                    continue;

                if (ModLog.ReadLines(DoneFile).Contains(key))
                    continue;

                // Decide once for this save, now: whatever the answer, it is not asked again.
                ModLog.Append(DoneFile, key);

                var player = Managers.GetManager<PlayersManager>().GetActivePlayerController();
                var crafted = WorldObjectsHandler.Instance.GetTotalCraftedObjects(player.OwnerClientId);
                if (crafted != 0)
                {
                    ModLog.Note("Save " + key + ": not a new game (" + crafted + " objects crafted); the loadout is left to /loadout.");
                    continue;
                }

                ModLog.Note("Save " + key + ": a new game; fitting the loadout.");
                _command.OnCommand("");
            }
        }
    }
}
