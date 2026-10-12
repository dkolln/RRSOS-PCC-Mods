using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Loadout
{
    /// <summary>
    /// /loadout in the game's chat: fits the top tier of every piece of personal gear. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.loadout";
        public const string Name = "RRSOS PCC Loadout";
        public const string Version = "0.2.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> AutoAtPlanetfall;

        private void Awake()
        {
            ModLog.Init("Loadout", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Add the /loadout chat command, which fits the top tier of every piece of personal gear. Writes into the running game.");

            AutoAtPlanetfall = Config.Bind("General", "AutoAtPlanetfall", true, "Fit the loadout by itself at the start of a new game (once per save, and only when nothing has been crafted yet). /loadout still works any time.");

            var command = gameObject.AddComponent<LoadoutCommand>();
            gameObject.AddComponent<PlanetfallLoadout>().Init(command);
            if (Enabled.Value && AutoAtPlanetfall.Value)
            {
                Boot.Announce(Boot.Loadout);
                Welcome.Add(1, "You have been auto-equipped with the latest in personnel space gear.");
            }
            ChatHook.Enabled = () => Enabled.Value;
            ChatHook.Add("/loadout", command.OnCommand, "fit the top tier of every piece of gear");
            gameObject.AddComponent<ChatHook>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
