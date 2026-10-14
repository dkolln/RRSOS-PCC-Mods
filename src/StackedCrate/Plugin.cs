using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.StackedCrate
{
    /// <summary>
    /// A "Stacked crate" in the build menu (3 Iron, places three Container1 on top of each other). A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(TerminalRegistration.TerminalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.stackedcrate";
        public const string Name = "RRSOS PCC Stacked Crate";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            ModLog.Init("StackedCrate", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Add the Stacked crate to the build menu (3 Iron, places three Container1 on top of each other). Writes into the running game.");
            gameObject.AddComponent<StackedCrate>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
