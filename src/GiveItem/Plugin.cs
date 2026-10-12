using BepInEx;
using BepInEx.Configuration;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.GiveItem
{
    /// <summary>
    /// /giveitem on the terminal: puts an item into the player's backpack. A live-write mod, so it does nothing until <c>Enabled</c> is true in its config file.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(TerminalRegistration.TerminalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rrsos.pcc.giveitem";
        public const string Name = "RRSOS PCC Give Item";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> MaxQuantity;

        private void Awake()
        {
            ModLog.Init("GiveItem", Logger);
            Enabled = Config.Bind("General", "Enabled", false, "Add /giveitem to the terminal: puts an item into your backpack. Writes into the running game.");
            MaxQuantity = Config.Bind("General", "MaxQuantity", 500, "The most one /giveitem may ask for (the backpack's room is the real limit).");

            gameObject.AddComponent<GiveItemCommand>();
            ModLog.Note($"{Name} {Version} loaded. Enabled: {Enabled.Value}. Log: {ModLog.LogFile}");
        }
    }
}
