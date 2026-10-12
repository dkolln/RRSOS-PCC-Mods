using System.Collections;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// The terminal's part of a new game's start-up lines (see Boot): "Processing Additional Commands...". It decides what a new game is the way the Loadout does:
    /// the player has crafted nothing yet, once per save.
    /// </summary>
    internal sealed class BootGreeting : MonoBehaviour
    {
        private string _handled;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);

                if (!Plugin.Enabled.Value || !WorldState.Ready(out _, out var key) || key == _handled)
                    continue;

                _handled = key;
                Boot.NewWorld(key);
                var player = Managers.GetManager<PlayersManager>().GetActivePlayerController();
                if (WorldObjectsHandler.Instance.GetTotalCraftedObjects(player.OwnerClientId) != 0)
                {
                    Boot.Skip(Boot.Commands);
                    continue;
                }

                yield return Boot.Step(Boot.Commands, "Processing Additional Commands......", null, true);
            }
        }
    }
}
