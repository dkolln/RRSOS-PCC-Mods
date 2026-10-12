using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Unlocks
{
    /// <summary>
    /// Says on the terminal's screens (or in the chat when there is no terminal) what has just been unlocked, but only in a free-build game, where the game switches its own unlock popups off. It only reads the game and writes
    /// chat lines, so unlike the other mods it is on by default.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(TerminalRegistration.TerminalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.unlocks";
        public const string Name = "RRSOS PCC Unlocks";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            ModLog.Init("Unlocks", Logger);
            Enabled = Config.Bind("General", "Enabled", true, "Say 'Unlocked: <name>' in the chat when something is unlocked in a free-build game.");

            gameObject.AddComponent<UnlockNotice>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
