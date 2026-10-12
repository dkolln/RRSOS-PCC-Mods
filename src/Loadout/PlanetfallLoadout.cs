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
        private LoadoutCommand _command;
        private string _handled;

        public void Init(LoadoutCommand command) => _command = command;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);

                if (!Plugin.Enabled.Value || !Plugin.AutoAtPlanetfall.Value || _command == null)
                    continue;

                if (!WorldState.Ready(out _, out var key) || key == _handled)
                    continue;

                // Once per world loaded. A new game is one where the player has crafted nothing yet; that is judged from the world itself (a game left without saving
                // starts over, so a mark kept in a file would be wrong), and gear already worn is left alone.
                _handled = key;
                Boot.NewWorld(key);

                var player = Managers.GetManager<PlayersManager>().GetActivePlayerController();
                var crafted = WorldObjectsHandler.Instance.GetTotalCraftedObjects(player.OwnerClientId);
                if (crafted != 0)
                {
                    ModLog.Note("Save " + key + ": not a new game (" + crafted + " objects crafted); the loadout is left to /loadout.");
                    Boot.Skip(Boot.Loadout);
                    continue;
                }

                if (!_command.HasWork())
                {
                    ModLog.Note("Save " + key + ": the loadout is already worn.");
                    Boot.Skip(Boot.Loadout);
                    continue;
                }

                ModLog.Note("Save " + key + ": a new game; fitting the loadout.");
                yield return Boot.Step(Boot.Loadout, "Accessing Loadout Parameters....", _command.RunQuietly());
            }
        }
    }
}
