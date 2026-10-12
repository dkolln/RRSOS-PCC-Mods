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
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            ModLog.Init("Loadout", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Add the /loadout chat command, which fits the top tier of every piece of personal gear. Writes into the running game.");

            var command = gameObject.AddComponent<LoadoutCommand>();
            ChatHook.Enabled = () => Enabled.Value;
            ChatHook.Add("/loadout", command.OnCommand, "fit the top tier of every piece of gear");
            gameObject.AddComponent<ChatHook>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
