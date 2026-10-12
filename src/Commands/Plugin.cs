using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// A Terminal you can build: a screen with a prompt where you type the other RRSOS mods' commands, with /help listing only what is installed and active.
    /// A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.commands";
        public const string Name = "RRSOS PCC Commands";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Scale;

        private void Awake()
        {
            ModLog.Init("Commands", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Add the Terminal to the build menu (1 Iron, 1 Silicon, 1 Titanium, 1 Cobalt). Writes into the running game.");
            Scale = Config.Bind("General", "Scale", 2.5f, "How much larger than a sign the terminal's screen is (width and height; it stays as thin as a sign). Takes effect when the game starts.");
            gameObject.AddComponent<TerminalMod>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
