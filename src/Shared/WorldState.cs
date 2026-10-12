using SpaceCraft;
using Unity.Netcode;

namespace RRSOS.PCC.Shared
{
    /// <summary>What the game says about the world right now. Read-only.</summary>
    internal static class WorldState
    {
        /// <summary>True once a world is fully loaded, we are the host and the player is in it, on any planet. The key names the save (its display name and world seed).</summary>
        public static bool Ready(out int planetHash, out string key)
        {
            key = null;
            planetHash = 0;

            var handler = WorldObjectsHandler.Instance;
            var loader = Managers.GetManager<PlanetLoader>();
            var players = Managers.GetManager<PlayersManager>();
            var settings = Managers.GetManager<GameSettingsHandler>();
            if (handler == null || loader == null || players == null || settings == null)
                return false;
            if (!handler.GetHasInitiatedAllObjects() || !loader.GetIsLoaded() || loader.GetCurrentPlanetData() == null)
                return false;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !NetworkManager.Singleton.IsServer)
                return false;

            var player = players.GetActivePlayerController();
            if (player == null || player.GetPlayerGaugesHandler() == null || !player.GetPlayerGaugesHandler().HasStarted())
                return false;

            var state = settings.GetCurrentGameSettings();
            if (state == null || string.IsNullOrEmpty(state.saveDisplayName))
                return false;

            key = state.saveDisplayName + "|" + state.worldSeed;
            planetHash = loader.GetCurrentPlanetData().GetPlanetHash();
            return true;
        }

        /// <summary>True when the player is on the named planet (for example "Prime").</summary>
        public static bool OnPlanet(string planetId)
        {
            var loader = Managers.GetManager<PlanetLoader>();
            return loader != null && loader.GetCurrentPlanetData() != null && loader.GetCurrentPlanetData().GetPlanetId() == planetId;
        }
    }
}
