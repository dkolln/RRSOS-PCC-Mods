using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// A resupply icon on a storage crate's window: one click fills that crate's empty slots with its item (its label, or else its demand). A live-write mod, so it does nothing until
    /// <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(TerminalRegistration.TerminalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.resupply";
        public const string Name = "RRSOS PCC Resupply";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Groups;
        internal static ConfigEntry<float> NudgeX, NudgeY;

        private void Awake()
        {
            ModLog.Init("Resupply", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Show the resupply icon on crate windows, and /resupply on the terminal. Writes into the running game.");
            Groups = Config.Bind("General", "Groups", "Container1,Container2,Container3", "The crates that get the resupply icon (and that /resupply tops up).");
            NudgeX = Config.Bind("Placement", "NudgeX", 0f, "Fine adjustment of the icon, sideways from its default spot (UI units, positive is right).");
            NudgeY = Config.Bind("Placement", "NudgeY", 0f, "Fine adjustment of the icon, up from its default spot (UI units, positive is up).");

            gameObject.AddComponent<Resupplier>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. It writes into the running game only when Enabled is true. Log: {ModLog.LogFile}");
        }
    }
}
