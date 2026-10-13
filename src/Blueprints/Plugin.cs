using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Blueprints
{
    /// <summary>
    /// Unlocks, at the start of a new game, the blueprints that the game otherwise hands out through blueprint chips. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.blueprints";
        public const string Name = "RRSOS PCC Blueprints";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            ModLog.Init("Blueprints", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Unlock the blueprints listed in blueprints.txt at the start of a new game (once per save, and only when nothing has been crafted yet). Writes into the running game.");

            gameObject.AddComponent<PlanetfallBlueprints>();
            if (Enabled.Value)
            {
                Boot.Announce(Boot.Blueprints);
                Welcome.Add(3, "Every blueprint chip has been decoded for you, so those blueprints are already unlocked.");
            }

            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {ModLog.LogFile}");
        }
    }
}
